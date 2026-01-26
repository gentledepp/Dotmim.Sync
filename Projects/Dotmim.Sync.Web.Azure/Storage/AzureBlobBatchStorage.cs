using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Wormhole.Sync.Storage;

namespace Wormhole.Sync.Web.Azure
{
    /// <summary>
    /// Batch storage implementation using Azure Blob Storage.
    /// Enables scale-out deployments where any server instance can read/write batch files.
    /// </summary>
    /// <remarks>
    /// This implementation requires the Azure.Storage.Blobs NuGet package.
    /// The blob container will be created automatically if it doesn't exist.
    /// Blob names are formatted as "{directoryPath}/{fileName}" where directoryPath acts as a virtual directory.
    /// </remarks>
    public class AzureBlobBatchStorage : IBatchStorage
    {
        private readonly BlobContainerClient containerClient;

        /// <summary>
        /// Initializes a new instance of the <see cref="AzureBlobBatchStorage"/> class.
        /// </summary>
        /// <param name="connectionString">The Azure Storage connection string.</param>
        /// <param name="containerName">The name of the blob container to use for batch storage.</param>
        public AzureBlobBatchStorage(string connectionString, string containerName)
        {
            if (string.IsNullOrEmpty(connectionString))
                throw new ArgumentNullException(nameof(connectionString));
            if (string.IsNullOrEmpty(containerName))
                throw new ArgumentNullException(nameof(containerName));

            var blobServiceClient = new BlobServiceClient(connectionString);
            this.containerClient = blobServiceClient.GetBlobContainerClient(containerName);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="AzureBlobBatchStorage"/> class.
        /// </summary>
        /// <param name="containerClient">A pre-configured BlobContainerClient.</param>
        public AzureBlobBatchStorage(BlobContainerClient containerClient)
        {
            this.containerClient = containerClient ?? throw new ArgumentNullException(nameof(containerClient));
        }

        /// <inheritdoc />
        public async Task WriteBatchPartAsync(
            string directoryPath,
            string fileName,
            Stream dataStream,
            CancellationToken cancellationToken = default)
        {
            var blobName = this.GetBlobName(directoryPath, fileName);
            var blobClient = this.containerClient.GetBlobClient(blobName);

            await this.containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            await blobClient.UploadAsync(dataStream, overwrite: true, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<Stream> ReadBatchPartAsync(
            string directoryPath,
            string fileName,
            CancellationToken cancellationToken = default)
        {
            var blobName = this.GetBlobName(directoryPath, fileName);
            var blobClient = this.containerClient.GetBlobClient(blobName);

            var response = await blobClient.DownloadStreamingAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            return response.Value.Content;
        }

        /// <inheritdoc />
        public async Task DeleteBatchPartAsync(
            string directoryPath,
            string fileName,
            CancellationToken cancellationToken = default)
        {
            var blobName = this.GetBlobName(directoryPath, fileName);
            var blobClient = this.containerClient.GetBlobClient(blobName);
            await blobClient.DeleteIfExistsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<bool> DeleteBatchDirectoryAsync(
            string directoryPath,
            CancellationToken cancellationToken = default)
        {
            var prefix = this.NormalizePath(directoryPath) + "/";

            var deleted = false;

            await foreach (var blobItem in this.containerClient.GetBlobsAsync(
                prefix: prefix, cancellationToken: cancellationToken).ConfigureAwait(false))
            {
                var blobClient = this.containerClient.GetBlobClient(blobItem.Name);
                var ar = await blobClient.DeleteIfExistsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
                deleted |= ar.Value;
            }

            return deleted;
        }

        /// <inheritdoc />
        public async Task<bool> DirectoryExistsAsync(
            string directoryPath,
            CancellationToken cancellationToken = default)
        {
            var prefix = this.NormalizePath(directoryPath) + "/";

            await foreach (var _ in this.containerClient.GetBlobsAsync(
                prefix: prefix, cancellationToken: cancellationToken).ConfigureAwait(false))
            {
                return true;
            }

            return false;
        }

        /// <inheritdoc />
        public async Task EnsureDirectoryExistsAsync(
            string directoryPath,
            CancellationToken cancellationToken = default)
        {
            // Azure Blob Storage doesn't have real directories - they're virtual
            // We just ensure the container exists
            await this.containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<IEnumerable<string>> GetFilesAsync(
            string directoryPath,
            string searchPattern = "*",
            CancellationToken cancellationToken = default)
        {
            var prefix = this.NormalizePath(directoryPath) + "/";
            var files = new List<string>();

            await foreach (var blobItem in this.containerClient.GetBlobsAsync(
                prefix: prefix, cancellationToken: cancellationToken).ConfigureAwait(false))
            {
                var fileName = blobItem.Name.Substring(prefix.Length);

                // Simple wildcard matching
                if (this.MatchesPattern(fileName, searchPattern))
                    files.Add(fileName);
            }

            return files;
        }

        /// <inheritdoc />
        public async Task<long> GetFileSizeAsync(
            string directoryPath,
            string fileName,
            CancellationToken cancellationToken = default)
        {
            var blobName = this.GetBlobName(directoryPath, fileName);
            var blobClient = this.containerClient.GetBlobClient(blobName);

            try
            {
                var properties = await blobClient.GetPropertiesAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
                return properties.Value.ContentLength;
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return 0L;
            }
        }

        /// <inheritdoc />
        public async Task<bool> FileExistsAsync(
            string directoryPath,
            string fileName,
            CancellationToken cancellationToken = default)
        {
            var blobName = this.GetBlobName(directoryPath, fileName);
            var blobClient = this.containerClient.GetBlobClient(blobName);

            var response = await blobClient.ExistsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            return response.Value;
        }

        /// <inheritdoc />
        public async Task<IEnumerable<string>> GetSubdirectoriesAsync(
            string rootPath,
            CancellationToken cancellationToken = default)
        {
            var prefix = string.IsNullOrEmpty(rootPath) ? string.Empty : this.NormalizePath(rootPath) + "/";
            var directories = new HashSet<string>();

            await foreach (var blobItem in this.containerClient.GetBlobsAsync(
                prefix: prefix, cancellationToken: cancellationToken).ConfigureAwait(false))
            {
                // Extract first path segment after prefix as "directory" name
                var relativePath = blobItem.Name.Substring(prefix.Length);
                var slashIndex = relativePath.IndexOf('/');
                if (slashIndex > 0)
                {
                    var dirName = relativePath.Substring(0, slashIndex);
                    directories.Add(dirName);
                }
            }

            return directories;
        }

        private string GetBlobName(string directoryPath, string fileName)
        {
            var normalizedDir = this.NormalizePath(directoryPath);
            return $"{normalizedDir}/{fileName}";
        }

        private string NormalizePath(string path)
        {
            // Convert Windows paths to blob-friendly paths and remove leading slashes
            return path?.Replace("\\", "/").TrimStart('/').TrimEnd('/') ?? string.Empty;
        }

        private bool MatchesPattern(string fileName, string pattern)
        {
            if (pattern == "*")
                return true;

            if (pattern.StartsWith("*."))
            {
                var extension = pattern.Substring(1);
                return fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase);
            }

            return fileName.Equals(pattern, StringComparison.OrdinalIgnoreCase);
        }
    }
}
