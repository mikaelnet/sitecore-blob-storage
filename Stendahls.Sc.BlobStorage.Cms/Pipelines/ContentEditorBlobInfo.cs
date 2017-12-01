using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using Sitecore;
using Sitecore.Data;
using Sitecore.Pipelines.GetContentEditorWarnings;
using Stendahls.Sc.BlobStorage.Cms.Extensions;

namespace Stendahls.Sc.BlobStorage.Cms.Pipelines
{
    public class ContentEditorBlobInfo
    {
        public static readonly ID UnversionedFile = new ID("{962B53C4-F93B-4DF9-9821-415C867B8903}");
        public static readonly ID VersionedFile = new ID("{611933AC-CE0C-4DDC-9683-F830232DB150}");
        public void Process(GetContentEditorWarningsArgs args)
        {
            var item = args.Item;
            if (item == null || !item.Paths.IsMediaItem)
                return;

            if (!item.Template.InheritsTemplate(UnversionedFile) &&
                !item.Template.InheritsTemplate(VersionedFile))
                return;

            var contentEditorInfo = args.Add();
            contentEditorInfo.Key = "Stendahls.Sc.BlobStorage.Cms.Pipelines.ContentEditorBlobInfo";
            contentEditorInfo.Title = $"Information about blob storage";
            contentEditorInfo.Text = item["Blob"];

        }
    }
}