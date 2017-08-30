using System;
using System.Collections;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using Sitecore.Collections;
using Sitecore.Configuration;
using Sitecore.Data;
using Sitecore.Data.DataProviders;
using Sitecore.Diagnostics;
using Sitecore.IO;

namespace Stendahls.Sc.BlobStorage.Common
{
    public abstract class DiskCachingBlobManager : IBlobManager
    {
        private static string CacheFolder { get; set; }

        private static readonly LockSet BlobLockSet = new LockSet();

        protected static string GetFilePath(Guid blobId)
        {
            var id = $"{blobId:D}.bin";
            id = id.Substring(0, 2) + "\\" + id;
            return Path.Combine(CacheFolder, id);
        }

        public virtual void Initialize()
        {
            var cacheFolder = Settings.GetSetting("Stendahls.BlobStorage.DiskCache.Folder");
            if (string.IsNullOrWhiteSpace(cacheFolder))
            {
                cacheFolder = Path.Combine(Settings.DataFolder, "BlobCache");
            }
            CacheFolder = cacheFolder;
        }

        public virtual Stream DownloadToStream(Guid blobId)
        {
            lock (BlobLockSet.GetLock(blobId.ToString()))
            {
                var file = GetFilePath(blobId);
                if (!Exists(file))
                    return null;

                var memoryStream = new MemoryStream(File.ReadAllBytes(file));
                memoryStream.Seek(0, SeekOrigin.Begin);
                return memoryStream;
            }
        }

        public virtual bool UploadFromStream(Guid blobId, Stream stream)
        {
            lock (BlobLockSet.GetLock(blobId.ToString()))
            {
                var file = GetFilePath(blobId);
                var fi = new FileInfo(file);
                if (fi.DirectoryName != null)
                {
                    Directory.CreateDirectory(fi.DirectoryName);
                }

                Log.Info($"Caching Blob {blobId:D} in disk cache", this);
                using (var fs = File.Create(file))
                {
                    stream.CopyTo(fs);
                }
                return true;
            }
        }

        public virtual bool Delete(Guid blobId)
        {
            lock (BlobLockSet.GetLock(blobId.ToString()))
            {
                var file = GetFilePath(blobId);
                if (Exists(file))
                {
                    Log.Info($"Deleting Blob {blobId:D} from disk cache", this);
                    File.Delete(file);
                    return true;
                }
                return false;
            }
        }

        public virtual bool Exists(Guid blobId)
        {
            var file = GetFilePath(blobId);
            return Exists(file);
        }

        public virtual void CleanupBlobs(CallContext context)
        {
            var blobsToKeep = LoadAllBlobIds();
            CleanupBlobs(blobsToKeep);
        }

        public virtual void CleanupBlobs(HashSet<Guid> blobsToKeep)
        {
            foreach (var cachedGuid in LoadAllDiskCachedBlobIds())
            {
                if (!blobsToKeep.Contains(cachedGuid))
                {
                    Delete(cachedGuid);
                }
            }
        }

        public virtual HashSet<Guid> LoadAllBlobIds()
        {
            var databases = Settings.GetSetting("Stendahls.BlobStorage.Databases", "master,web,core")
                .Split(',', ';').Select(s => s.Trim()).Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();

            // Load all BlobId references from all configured databases, so that we
            // don't remove blobs that are used in other databases. The cloud blob
            // storage typically keeps only one copy.
            var allBlobIds = new HashSet<Guid>();
            foreach (var databaseName in databases)
            {
                var database = Database.GetDatabase(databaseName);
                var connectionString = ConfigurationManager.ConnectionStrings[database.ConnectionStringName].ConnectionString;
                using (var con = new SqlConnection(connectionString))
                {
                    con.Open();
                    var cmd = new SqlCommand("SELECT [BlobId] FROM [Blobs]", con) { CommandTimeout = 300 };
                    using (var reader = cmd.ExecuteReader(CommandBehavior.CloseConnection))
                    {
                        while (reader.Read())
                        {
                            var guid = reader.GetGuid(0);
                            allBlobIds.Add(guid);
                        }
                    }
                }
            }

            return allBlobIds;
        }

        private IEnumerable<Guid> LoadAllDiskCachedBlobIds()
        {
            foreach (var fullpath in Directory.GetFiles(CacheFolder, "*.bin", SearchOption.AllDirectories))
            {
                var filename = Path.GetFileNameWithoutExtension(fullpath);
                Guid guid;
                if (filename != null && Guid.TryParse(filename, out guid))
                    yield return guid;
            }
        }

        private bool Exists(string path)
        {
            return File.Exists(path);
        }
    }
}