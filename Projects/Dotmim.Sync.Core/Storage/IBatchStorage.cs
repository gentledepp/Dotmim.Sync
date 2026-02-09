using System.Collections.Generic;
using System.Data.SqlTypes;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Wormhole.Sync.Storage
{
    /// <summary>
    /// Abstraction for batch file storage operations.
    /// Allows pluggable storage backends (local filesystem, Azure Blob, S3, etc.)
    /// for scale-out deployments.
    /// </summary>
    public interface IBatchStorage
    {
        /// <summary>
        /// Writes a batch part to storage.
        /// </summary>
        /// <param name="directoryPath">The directory/container path for the batch.</param>
        /// <param name="fileName">The name of the batch part file.</param>
        /// <param name="dataStream">The data stream to write.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task WriteBatchPartAsync(
            string directoryPath,
            string fileName,
            Stream dataStream,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Reads a batch part from storage.
        /// </summary>
        /// <param name="directoryPath">The directory/container path for the batch.</param>
        /// <param name="fileName">The name of the batch part file.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A stream containing the batch part data. Caller is responsible for disposing.</returns>
        Task<Stream> ReadBatchPartAsync(
            string directoryPath,
            string fileName,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes a batch part from storage.
        /// </summary>
        /// <param name="directoryPath">The directory/container path for the batch.</param>
        /// <param name="fileName">The name of the batch part file.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task DeleteBatchPartAsync(
            string directoryPath,
            string fileName,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes an entire batch directory from storage.
        /// </summary>
        /// <param name="directoryPath">The directory/container path to delete.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task<bool> DeleteBatchDirectoryAsync(
            string directoryPath,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Checks if a batch directory exists.
        /// </summary>
        /// <param name="directoryPath">The directory/container path to check.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>True if the directory exists, false otherwise.</returns>
        Task<bool> DirectoryExistsAsync(
            string directoryPath,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Ensures the directory/container exists, creating it if necessary.
        /// </summary>
        /// <param name="directoryPath">The directory/container path to ensure.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task EnsureDirectoryExistsAsync(
            string directoryPath,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets all files in a batch directory.
        /// </summary>
        /// <param name="directoryPath">The directory/container path to list.</param>
        /// <param name="searchPattern">Optional search pattern (e.g., "*.json").</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Enumerable of file names (not full paths).</returns>
        Task<IEnumerable<string>> GetFilesAsync(
            string directoryPath,
            string searchPattern = "*",
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets file size in bytes.
        /// </summary>
        /// <param name="directoryPath">The directory/container path.</param>
        /// <param name="fileName">The name of the file.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>File size in bytes, or 0 if file does not exist.</returns>
        Task<long> GetFileSizeAsync(
            string directoryPath,
            string fileName,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Checks if a specific file exists in storage.
        /// </summary>
        /// <param name="directoryPath">The directory/container path.</param>
        /// <param name="fileName">The name of the file.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>True if the file exists, false otherwise.</returns>
        Task<bool> FileExistsAsync(
            string directoryPath,
            string fileName,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets all subdirectories (batch directories) under the specified root path.
        /// For Azure Blob Storage, this returns virtual directory prefixes.
        /// </summary>
        /// <param name="rootPath">The root directory/container path.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Enumerable of subdirectory names (not full paths).</returns>
        Task<IEnumerable<string>> GetSubdirectoriesAsync(
            string rootPath,
            CancellationToken cancellationToken = default);
    }
}
