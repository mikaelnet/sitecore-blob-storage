using System;
using System.IO;
using System.Linq;
using Sitecore;
using Sitecore.Configuration;
using Sitecore.Diagnostics;

namespace Stendahls.Sc.BlobStorage.Common
{
    public class DiskCachingCleanupAgent
    {
        private readonly long _maxDiskCacheSize;
        public DiskCachingCleanupAgent(string maxCacheSize)
        {
            _maxDiskCacheSize = StringUtil.ParseSizeString(maxCacheSize);
        }

        public void Run()
        {
            if (_maxDiskCacheSize <= 0)
            {
                Log.Info("No valid disk cache size specified. Using unlimit size", this);
                return;
            }

            var cacheFolder = Settings.GetSetting("Stendahls.BlobStorage.DiskCache.Folder");
            long totalSize = _maxDiskCacheSize;

            // Loop over all cached files with the newest files first
            foreach (var fileInfo in Directory
                .GetFiles(cacheFolder, "*.bin", SearchOption.AllDirectories)
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.CreationTimeUtc))
            {
                // Skip all files that doesnt match the {guid}.bin pattern
                Guid guid;
                if (!Guid.TryParse(Path.GetFileNameWithoutExtension(fileInfo.FullName), out guid))
                    continue;

                // Leave all files that fits within the total cache size
                if (totalSize > 0)
                {
                    totalSize -= fileInfo.Length;
                    continue;
                }
                
                // Remove surplus files
                Log.Info($"Deleting {fileInfo.FullName} from disk cache due to cache size constraint", this);
                fileInfo.Delete();
            }
        }
    }
}