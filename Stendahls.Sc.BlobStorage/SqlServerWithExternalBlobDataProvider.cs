using System;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using Sitecore.Collections;
using Sitecore.Configuration;
using Sitecore.Data.DataProviders;
using Sitecore.Diagnostics;
using Sitecore.Reflection;
using Stendahls.Sc.BlobStorage.Common;

namespace Stendahls.Sc.BlobStorage
{
    public class SqlServerWithExternalBlobDataProvider : Sitecore.Data.SqlServer.SqlServerDataProvider
    {
        private readonly LockSet _blobLockSet;
        private readonly IBlobManager _blobManager;
        private readonly bool _configured;

        internal static string BlobManagerType => Settings.GetSetting("Stendahls.BlobStorage.Provider");

        public SqlServerWithExternalBlobDataProvider(string connectionString) : base(connectionString)
        {
            _blobLockSet = new LockSet();

            if (string.IsNullOrWhiteSpace(BlobManagerType))
            {
                Log.Error("ExternalBlobDataProvider not configured. Using Sitecore default.", this);
                _configured = false;
                return;
            }
            try
            {
                Log.Info($"Initializing ExternalBlobDataProvider using {BlobManagerType}", this);
                _blobManager = ReflectionUtil.CreateObject(BlobManagerType) as IBlobManager;
                if (_blobManager == null)
                {
                    Log.Error($"Unable to create IBlobManager of type {BlobManagerType}. Using Sitecore default.", this);
                    _configured = false;
                    return;
                }
                _blobManager.Initialize();
                _configured = true;
            }
            catch (Exception ex)
            {
                Log.Error($"Unable to initialize ExternalBlobDataProvider {BlobManagerType}. Using Sitecore default", ex, this);
                _configured = false;
            }
        }

        public override Stream GetBlobStream(Guid blobId, CallContext context)
        {
            if (!_configured)
                return base.GetBlobStream(blobId, context);

            var stream = _blobManager.DownloadToStream(blobId);
            return stream ?? base.GetBlobStream(blobId, context);
        }

        public override bool SetBlobStream(Stream stream, Guid blobId, CallContext context)
        {
            if (!_configured)
                return base.SetBlobStream(stream, blobId, context);

            lock (_blobLockSet.GetLock(blobId.ToString()))
            {
                _blobManager.UploadFromStream(blobId, stream);

                //insert an empty reference to the BlobId into the SQL Blobs table, this is basically to assist with the cleanup process.
                //during cleanup, it's faster to query the database for the blobs that should be removed as opposed to retrieving and 
                // parsing a list from external source. Also, remove any existing references.
                Api.Execute("DELETE FROM {0}Blobs{1} WHERE {0}BlobId{1} = {2}blobId{3}", "@blobId", blobId);
                const string cmdText = "INSERT INTO [Blobs]([Id], [BlobId], [Index], [Created], [Data]) VALUES(NewId(), @blobId, @index, @created, @data)";
                using (var connection = new SqlConnection(Api.ConnectionString))
                {
                    connection.Open();
                    var command = new SqlCommand(cmdText, connection)
                    {
                        CommandTimeout = (int)CommandTimeout.TotalSeconds
                    };
                    command.Parameters.AddWithValue("@blobId", blobId);
                    command.Parameters.AddWithValue("@index", 0);
                    command.Parameters.AddWithValue("@created", DateTime.UtcNow);
                    command.Parameters.Add("@data", SqlDbType.Image, 0).Value = new byte[0];
                    command.ExecuteNonQuery();
                }
            }
            return true;
        }

        public override bool RemoveBlobStream(Guid blobId, CallContext context)
        {
            if (_configured)
            {
                _blobManager.Delete(blobId);
            }
            return base.RemoveBlobStream(blobId, context);
        }

        public override bool BlobStreamExists(Guid blobId, CallContext context)
        {
            if (_configured)
            {
                // Transfer if not exists
                if (_blobManager.Exists(blobId))
                    return true;
            }
            return base.BlobStreamExists(blobId, context);
        }

        protected override void CleanupBlobs(CallContext context)
        {
            // Let Sitecore cleanup the blobs table first
            base.CleanupBlobs(context);
            if (_configured)
            {
                // Then cleanup the blob storage to match the blobs tables
                _blobManager.CleanupBlobs(context);
            }
        }
    }
}