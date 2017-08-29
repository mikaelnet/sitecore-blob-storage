using System;
using System.IO;
using Sitecore.Data.DataProviders;

namespace Stendahls.Sc.BlobStorage.Common
{
    public interface IBlobManager
    {
        Stream DownloadToStream(Guid blobId);
        bool UploadFromStream(Guid blobId, Stream stream);
        bool Delete(Guid blobId);
        bool Exists(Guid blobId);
        void CleanupBlobs(CallContext context);
    }
}
