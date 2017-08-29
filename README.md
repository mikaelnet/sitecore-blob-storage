# sitecore-blob-storage
Stores Sitecore binaries in external services, typically a cloud blob storage service, 
instead as SQL Server blobs. The module uses Amazon AWS S3 buckets as a default blob
service, but it's built around a generic interface so virtually any external storage
provider can be created and used.

The main advantages of this is reduced database size, faster publishing times, easier
to make backups and move databases between (prod, stage, test, dev) environments etc.

The module is tested with Sitecore 8.1 update-1, but will probably work on most versions.

## Usage
Build and install the project. (You'll need Hedgehog TDS to install the items).
Then create an S3 bucket and a IAM user with credentials to read and write to the
bucket. Rename the BlobStorage.AwsS3.Credentials.example to .config and enter the name 
of the created bucket and credentials. 
You can also rename the BlobStorage.DiskCache.example to .config and set you're
preferred cache folder and working databases. If this file is not defined, the 
MediaCache folder will be used for caching instead.