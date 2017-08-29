using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using Sitecore.Collections;
using Sitecore.Data.DataProviders.SqlServer;
using Sitecore.Diagnostics;
using Stendahls.Sc.BlobStorage.Common;

namespace Stendahls.Sc.BlobStorage
{
    public class BlobTransferer
    {
        private readonly IBlobManager _blobManager;
        private readonly LockSet _blobLockSet;

        private readonly string _connectionString;
        protected int CommandTimeout => 300;

        public BlobTransferer(string connectionString, IBlobManager blobManager)
        {
            _connectionString = connectionString;
            _blobManager = blobManager;
            _blobLockSet = new LockSet();
        }

        private int GetScalar(string sql)
        {
            using (var con = new SqlConnection(_connectionString))
            {
                con.Open();
                var cmd = new SqlCommand(sql, con);
                cmd.CommandTimeout = CommandTimeout;
                using (var reader = cmd.ExecuteReader(CommandBehavior.CloseConnection))
                {
                    if (!reader.Read() || reader.IsDBNull(0))
                        return 0;
                    return SqlServerHelper.GetInt(reader, 0);
                }
            }
        }

        public int GetNumberOfDatabaseBlobs()
        {
            return GetScalar("SELECT COUNT(*) FROM [Blobs] WHERE [Index]=0 and DATALENGTH([Data]) != 0");
        }

        public int GetNumberOfCloudBlobs()
        {
            return GetScalar("SELECT COUNT(*) FROM [Blobs] WHERE [Index]=0 and DATALENGTH([Data]) = 0");
        }

        public IEnumerable<Guid> GetBlobIds(bool emptyBlobs = false)
        {
            var guids = new List<Guid>();
            using (var con = new SqlConnection(_connectionString))
            {
                con.Open();
                var cmd = new SqlCommand("SELECT [BlobId] FROM [Blobs] WHERE [Index]=0 and DATALENGTH([Data]) " + (emptyBlobs ? "=" : "!=") + " 0", con);
                cmd.CommandTimeout = CommandTimeout;
                using (var reader = cmd.ExecuteReader(CommandBehavior.CloseConnection))
                {
                    while (reader.Read())
                    {
                        guids.Add(reader.GetGuid(0));
                    }
                }
            }
            return guids;
        }

        public void TransferToBlobManager(Guid blobId)
        {
            long blobSize = -1;
            using (var con = new SqlConnection(_connectionString))
            {
                con.Open();
                var cmd = new SqlCommand("SELECT SUM(DATALENGTH([Data])) FROM [Blobs] WHERE [BlobId] = @blobId", con);
                cmd.CommandTimeout = CommandTimeout;
                cmd.Parameters.AddWithValue("@blobId", blobId);
                using (var reader = cmd.ExecuteReader())
                {
                    if (reader.Read() && !reader.IsDBNull(0))
                        blobSize = SqlServerHelper.GetLong(reader, 0);
                }

                if (blobSize <= 0)
                    return; // Don't transfer 0-byte objects

                cmd = new SqlCommand("SELECT [Data] FROM [Blobs] WHERE [BlobId] = @blobId ORDER BY [Index]", con);
                cmd.CommandTimeout = CommandTimeout;
                cmd.Parameters.AddWithValue("@blobId", blobId);
                var dataReader = cmd.ExecuteReader(CommandBehavior.SequentialAccess);

                try
                {
                    var stream = new SqlServerStream(dataReader, blobSize);
                    var success = _blobManager.UploadFromStream(blobId, stream);
                    if (!dataReader.IsClosed)
                        dataReader.Close();

                    if (success)
                    {
                        cmd = new SqlCommand(@"DELETE FROM [Blobs] WHERE [BlobId] = @blobId
INSERT INTO [Blobs]([Id], [BlobId], [Index], [Created], [Data]) VALUES(NewId(), @blobId, @index, @created, @data)", con);
                        cmd.CommandTimeout = CommandTimeout;
                        cmd.Parameters.AddWithValue("@blobId", blobId);
                        cmd.Parameters.AddWithValue("@index", 0);
                        cmd.Parameters.AddWithValue("@created", DateTime.UtcNow);
                        cmd.Parameters.Add("@data", SqlDbType.Image, 0).Value = new byte[0];
                        cmd.ExecuteNonQuery();
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"Error uploading stream for {blobId}", ex, this);
                }

                if (con.State != ConnectionState.Closed)
                    con.Close();
            }
        }

        public void RestoreFromBlobManager(Guid blobId)
        {
            lock (_blobLockSet.GetLock(blobId))
            {
                var stream = _blobManager.DownloadToStream(blobId);
                if (stream == null)
                    return;

                using (var con = new SqlConnection(_connectionString))
                {
                    con.Open();
                    var cmd = new SqlCommand(@"DELETE FROM [Blobs] WHERE [BlobId] = @blobId", con);
                    cmd.CommandTimeout = CommandTimeout;
                    cmd.Parameters.AddWithValue("@blobId", blobId);
                    cmd.ExecuteNonQuery();

                    var created = DateTime.UtcNow;

                    cmd = new SqlCommand("INSERT INTO [Blobs] ([Id], [BlobId], [Index], [Created], [Data]) VALUES(NewId(), @blobId, @index, @created, @data)", con);
                    cmd.CommandTimeout = CommandTimeout;
                    cmd.Parameters.AddWithValue("@blobId", blobId);
                    cmd.Parameters.AddWithValue("@created", created);

                    if (stream.CanSeek)
                        stream.Seek(0L, SeekOrigin.Begin);

                    const int chunkSize = 1029120;
                    int chunkIndex = 0;
                    var buffer = new byte[chunkSize];
                    var readLength = stream.Read(buffer, 0, chunkSize);
                    while (readLength > 0)
                    {
                        cmd.Parameters.AddWithValue("@index", chunkIndex);
                        cmd.Parameters.Add("@data", SqlDbType.Image, readLength).Value = buffer;
                        cmd.ExecuteNonQuery();

                        readLength = stream.Read(buffer, 0, chunkSize);
                        chunkIndex++;
                    }

                    if (con.State != ConnectionState.Closed)
                        con.Close();
                }
            }
        }
    }
}