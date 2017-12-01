using Sitecore.Configuration;

namespace Stendahls.Sc.BlobStorage.Common
{
    public static class BlobManagerHelper
    {
        public static string BlobManagerType => Settings.GetSetting("Stendahls.BlobStorage.Provider");
    }
}
