using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Sitecore.Configuration;
using Sitecore.Collections;
using Sitecore.Diagnostics;
using Sitecore.Exceptions;
using Stendahls.Sc.BlobStorage.Common;

namespace Stendahls.Sc.BlobStorage.AwsS3
{
    public class AwsS3BlobManager : DiskCachingBlobManager
    {
        private const string ConnectionStringName = "BlobStorage.S3.Credentials";

        private static readonly LockSet BlobLockSet = new LockSet();
        public string BucketName { get; protected set; }
        public RegionEndpoint Region { get; protected set; }
        protected AWSCredentials Credentials { private get; set; }
        public static string BucketPrefix { get; protected set; }

        public override void Initialize()
        {
            base.Initialize();

            // Security configuration moved to ConnectionStrings.config.
            // However, we'll fallback on the old way of doing it.
            var bucketName = Settings.GetSetting("Stendahls.BlobStorage.AwsS3.BucketName");
            var regionName = Settings.GetSetting("Stendahls.BlobStorage.AwsS3.Region");

            var accessKey = Settings.GetSetting("Stendahls.BlobStorage.AwsS3.AccessKey");
            var secretKey = Settings.GetSetting("Stendahls.BlobStorage.AwsS3.SecretKey");
            var prefix = Settings.GetSetting("Stendahls.BlobStorage.AwsS3.Prefix");

            // Connection string should be written in the format:
            // <add name="BlobStorage.S3.Credentials" connectionString="Bucket=xxx;Region=xxx;AccessKey=xxx;SecretKey=xxx;Prefix=xxx" />
            if (Settings.ConnectionStringExists(ConnectionStringName))
            {
                var s3Connection = Settings.GetConnectionString(ConnectionStringName)
                    .Split(';', ',', '&').Select(p =>
                    {
                        var param = p.Split('=');
                        return new Tuple<string,string>(param[0], param[1]);
                    }).ToDictionary(d => d.Item1, d => d.Item2, StringComparer.InvariantCultureIgnoreCase);

                if (s3Connection.ContainsKey("Bucket"))
                    bucketName = s3Connection["Bucket"];

                if (s3Connection.ContainsKey("Region"))
                    regionName = s3Connection["Region"];

                if (s3Connection.ContainsKey("AccessKey"))
                    accessKey = s3Connection["AccessKey"];
                if (s3Connection.ContainsKey("SecretKey"))
                    secretKey = s3Connection["SecretKey"];

                if (s3Connection.ContainsKey("Prefix"))
                    prefix = s3Connection["Prefix"];
            }

            if (string.IsNullOrWhiteSpace(accessKey) || string.IsNullOrWhiteSpace(secretKey))
            {
                Log.Info("Stendahls.BlobStorage.AwsS3.AccessKey and/or Stendahls.BlobStorage.AwsS3.SecretKey is not defined in config. Using EC2 machine role", "AwsS3BlobManager");
                Credentials = null;
            }
            else
            {
                Credentials = new BasicAWSCredentials(accessKey, secretKey);
            }

            if (string.IsNullOrWhiteSpace(bucketName))
                throw new ConfigurationException("Stendahls.BlobStorage.AwsS3.BucketName is not defined in config");

            BucketName = bucketName;
            Region = !string.IsNullOrWhiteSpace(regionName) ? RegionEndpoint.GetBySystemName(regionName) : null;
            
            BucketPrefix = prefix;
        }

        protected virtual AmazonS3Client GetS3Client()
        {
            if (Credentials == null)
                return new AmazonS3Client();
            return Region != null ? new AmazonS3Client(Credentials, Region) : new AmazonS3Client(Credentials);
        }

        protected static string GetObjectKey(Guid blobId)
        {
            return $"{BucketPrefix}{blobId:D}";
        }

        public override Stream DownloadToStream(Guid blobId)
        {
            try
            {
                var cacheStream = base.DownloadToStream(blobId);
                if (cacheStream != null)
                    return cacheStream;
            }
            catch (Exception ex)
            {
                Log.Error($"Can't get {blobId} from disk cache", ex, this);
            }

            try
            {
                Log.Info($"Downloading Blob {blobId:D} from Amazon S3", this);
                var client = GetS3Client();
                var response = client.GetObject(BucketName, GetObjectKey(blobId));
                var memoryStream = new MemoryStream();
                response.ResponseStream.CopyTo(memoryStream);

                try
                {
                    memoryStream.Seek(0, SeekOrigin.Begin);
                    base.UploadFromStream(blobId, memoryStream);
                }
                catch (Exception ex)
                {
                    Log.Error($"Can't store {blobId} in disk cache", ex, this);
                }

                memoryStream.Seek(0, SeekOrigin.Begin);
                return memoryStream;
            }
            catch (AmazonS3Exception ex)
            {
                Log.Warn($"Can't download {GetObjectKey(blobId)} from {BucketName}. Using fallback.", ex, this);
                return null;
            }
        }

        public override bool UploadFromStream(Guid blobId, Stream stream)
        {
            lock (BlobLockSet.GetLock(blobId.ToString()))
            {
                try
                {
                    Log.Info($"Uploading Blob {blobId:D} to Amazon S3", this);
                    var memoryStream = new MemoryStream();
                    stream.CopyTo(memoryStream);
                    memoryStream.Seek(0, SeekOrigin.Begin);

                    var client = GetS3Client();
                    var request = new PutObjectRequest
                    {
                        BucketName = BucketName,
                        Key = GetObjectKey(blobId),
                        InputStream = memoryStream,
                        AutoCloseStream = false,
                        AutoResetStreamPosition = true,
                    };
                    var response = client.PutObject(request);
                    if (response == null)
                        return false;

                    try
                    {
                        memoryStream.Seek(0, SeekOrigin.Begin);
                        base.UploadFromStream(blobId, memoryStream);
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"Can't store {blobId} in disk cache", ex, this);
                    }

                    return true;
                }
                catch (AmazonS3Exception ex)
                {
                    Log.Error($"Can't upload {GetObjectKey(blobId)} to {BucketName}", ex, this);
                    return false;
                }
            }
        }

        public override bool Delete(Guid blobId)
        {
            lock (BlobLockSet.GetLock(blobId.ToString()))
            {
                try
                {
                    Log.Info($"Deleting Blob {blobId:D} from Amazon S3", this);
                    var client = GetS3Client();
                    client.DeleteObject(BucketName, GetObjectKey(blobId));
                }
                catch (AmazonS3Exception ex)
                {
                    Log.Error($"Can't delete {GetObjectKey(blobId)} from {BucketName}", ex, this);
                    return false;
                }
            }

            try
            {
                base.Delete(blobId);
            }
            catch (Exception ex)
            {
                Log.Error($"Can't delete {blobId} from disk cache", ex, this);
            }
            return true;
        }

        public override bool Exists(Guid blobId)
        {
            try
            {
                if (base.Exists(blobId))
                    return true;
            }
            catch (Exception ex)
            {
                Log.Error($"Can't test if {blobId} exists in disk cache", ex, this);
            }

            try
            {
                var client = GetS3Client();
                var request = new GetObjectMetadataRequest()
                {
                    BucketName = BucketName,
                    Key = GetObjectKey(blobId),
                };
                client.GetObjectMetadata(request);
                return true;
            }
            catch (AmazonS3Exception ex)
            {
                if (string.Equals(ex.ErrorCode, "NotFound"))
                    return false;
                Log.Error($"Can't test if {GetObjectKey(blobId)} exists in {BucketName}", ex, this);
                throw;
            }
        }

        public override void CleanupBlobs(HashSet<Guid> blobsToKeep)
        {
            base.CleanupBlobs(blobsToKeep);
            var allS3BlobIds = LoadAllS3BlobIds();
            foreach (var s3BlobId in allS3BlobIds)
            {
                if (!blobsToKeep.Contains(s3BlobId))
                {
                    Delete(s3BlobId);
                }
            }
        }

        private IEnumerable<Guid> LoadAllS3BlobIds()
        {
            var s3Guids = new List<Guid>();
            int index = (BucketPrefix ?? string.Empty).Length;
            try
            {
                var client = GetS3Client();
                var request = new ListObjectsV2Request
                {
                    BucketName = BucketName,
                    Prefix = BucketPrefix
                };
                ListObjectsV2Response response;
                do
                {
                    response = client.ListObjectsV2(request);
                    foreach (var s3Object in response.S3Objects)
                    {
                        Guid guid;
                        if (Guid.TryParse(s3Object.Key.Substring(index), out guid))
                            s3Guids.Add(guid);
                    }
                    request.ContinuationToken = response.NextContinuationToken;
                } while (response.IsTruncated);
            }
            catch (AmazonS3Exception ex)
            {
                Log.Error($"Can't list bucket content of {BucketName}{BucketPrefix}", ex, this);
            }
            return s3Guids;
        }
    }
}