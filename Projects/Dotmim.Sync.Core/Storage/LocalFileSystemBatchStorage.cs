using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Wormhole.Sync.Storage
{
    /// <summary>
    /// Default batch storage implementation using the local filesystem.
    /// </summary>
    public class LocalFileSystemBatchStorage : IBatchStorage
    {
        /// <inheritdoc />
        public async Task WriteBatchPartAsync(
            string directoryPath,
            string fileName,
            Stream dataStream,
            CancellationToken cancellationToken = default)
        {
            await this.EnsureDirectoryExistsAsync(directoryPath, cancellationToken).ConfigureAwait(false);

            var fullPath = Path.Combine(directoryPath, fileName);

#if NET6_0_OR_GREATER
            await using var fileStream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true);
#else
            using var fileStream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true);
#endif
            await dataStream.CopyToAsync(fileStream, 81920, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public Task<Stream> ReadBatchPartAsync(
            string directoryPath,
            string fileName,
            CancellationToken cancellationToken = default)
        {
            var fullPath = Path.Combine(directoryPath, fileName);
            Stream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
            return Task.FromResult(stream);
        }

        /// <inheritdoc />
        public Task DeleteBatchPartAsync(
            string directoryPath,
            string fileName,
            CancellationToken cancellationToken = default)
        {
            var fullPath = Path.Combine(directoryPath, fileName);
            if (File.Exists(fullPath))
                File.Delete(fullPath);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<bool> DeleteBatchDirectoryAsync(
            string directoryPath,
            CancellationToken cancellationToken = default)
        {
            if (Directory.Exists(directoryPath))
            {
                try
                {
                    Directory.Delete(directoryPath, recursive: true);
                    return Task.FromResult(true);
                }
                catch
                {
                    // Ignore errors during cleanup - files may be locked
                    return Task.FromResult(false);
                }
            }

            return Task.FromResult(false);
        }

        /// <inheritdoc />
        public Task<bool> DirectoryExistsAsync(
            string directoryPath,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Directory.Exists(directoryPath));

        /// <inheritdoc />
        public Task EnsureDirectoryExistsAsync(
            string directoryPath,
            CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
                Directory.CreateDirectory(directoryPath);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<IEnumerable<string>> GetFilesAsync(
            string directoryPath,
            string searchPattern = "*",
            CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
                return Task.FromResult(Enumerable.Empty<string>());

            var files = Directory.GetFiles(directoryPath, searchPattern)
                .Select(Path.GetFileName);
            return Task.FromResult(files);
        }

        /// <inheritdoc />
        public Task<long> GetFileSizeAsync(
            string directoryPath,
            string fileName,
            CancellationToken cancellationToken = default)
        {
            var fullPath = Path.Combine(directoryPath, fileName);
            var fileInfo = new FileInfo(fullPath);
            return Task.FromResult(fileInfo.Exists ? fileInfo.Length : 0L);
        }

        /// <inheritdoc />
        public Task<bool> FileExistsAsync(
            string directoryPath,
            string fileName,
            CancellationToken cancellationToken = default)
        {
            var fullPath = Path.Combine(directoryPath, fileName);
            return Task.FromResult(File.Exists(fullPath));
        }

        /// <inheritdoc />
        public Task<IEnumerable<string>> GetSubdirectoriesAsync(
            string rootPath,
            CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(rootPath))
                return Task.FromResult(Enumerable.Empty<string>());

            var directories = Directory.EnumerateDirectories(rootPath)
                .Select(Path.GetFileName);
            return Task.FromResult(directories);
        }
    }
}
