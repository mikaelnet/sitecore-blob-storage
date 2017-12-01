using System;
using System.Collections.Specialized;
using System.Configuration;
using Sitecore.Configuration;
using Sitecore.Data;
using Sitecore.Diagnostics;
using Sitecore.Reflection;
using Sitecore.Shell.Framework.Commands;
using Sitecore.Web.UI.Sheer;
using Stendahls.Sc.BlobStorage.Common;

namespace Stendahls.Sc.BlobStorage.Cms.Commands
{
    public class UploadBlobsCommand : Command
    {
        public override void Execute(CommandContext context)
        {
            Assert.ArgumentNotNull(context, "context");
            var item = context.Items[0];
            var parameters = new NameValueCollection();
            parameters["db"] = item.Database.Name;
            Sitecore.Context.ClientPage.Start(this, "Run", parameters);
        }

        protected virtual void Run(ClientPipelineArgs args)
        {
            Assert.ArgumentNotNull((object)args, "args");
            var db = Factory.GetDatabase(args.Parameters["db"]);

            Sitecore.Shell.Applications.Dialogs.ProgressBoxes.ProgressBox
                .Execute("Transfer blobs", "Sending Blobs to Cloud",
                    StartProcess, new object[] { db });
        }

        public void StartProcess(params object[] parameters)
        {
            var database = (Database)parameters[0];
            var progressStatus = Sitecore.Context.Job.Status;

            progressStatus.Messages.Add("Loading number of blobs...");
            progressStatus.Total = 100;

            var conStringName = database.ConnectionStringName;
            var conString = ConfigurationManager.ConnectionStrings[conStringName].ConnectionString;

            var blobManager = ReflectionUtil.CreateObject(BlobManagerHelper.BlobManagerType) as IBlobManager;
            var transferer = new BlobTransferer(conString, blobManager);
            var numberOfBlobs = transferer.GetNumberOfDatabaseBlobs();
            progressStatus.Total = numberOfBlobs;
            progressStatus.Processed = 0;
            progressStatus.Messages.Add($"Uploading blob 1 to {progressStatus.Total} out of {numberOfBlobs} blobs");

            var blobs = transferer.GetBlobIds();
            foreach (var blobId in blobs)
            {
                transferer.TransferToBlobManager(blobId);
                progressStatus.IncrementProcessed();
            }

            // TODO: Show confirmation dialog
        }
    }
}