using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Wormhole.Sync.Storage;

namespace Wormhole.Sync
{
    /// <summary>
    /// Default implementation of IBatchCleanupService for cleaning up orphaned batch directories.
    /// Supports pluggable storage backends via IBatchStorage.
    /// </summary>
    public class BatchCleanupService : IBatchCleanupService
    {
        private readonly IBatchStorage storage;

        /// <summary>
        /// Initializes a new instance of the <see cref="BatchCleanupService"/> class
        /// with the default local file system storage.
        /// </summary>
        public BatchCleanupService() : this(new LocalFileSystemBatchStorage())
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="BatchCleanupService"/> class
        /// with the specified storage backend.
        /// </summary>
        /// <param name="storage">The storage backend to use for batch operations.</param>
        /// <exception cref="ArgumentNullException">Thrown when storage is null.</exception>
        public BatchCleanupService(IBatchStorage storage)
        {
            this.storage = storage ?? throw new ArgumentNullException(nameof(storage));
        }

        /// <inheritdoc />
        public Task<int> CleanupExpiredBatchesAsync(SyncOptions options, CancellationToken cancellationToken = default)
        {
            return this.CleanupExpiredBatchesAsync(options.BatchDirectory, options.BatchRetentionPeriod, cancellationToken);
        }

        /// <inheritdoc />
        public Task<int> CleanupExpiredBatchesAsync(SyncOptions options, TimeSpan retentionPeriod,
            CancellationToken cancellationToken = default)
        {
            return this.CleanupExpiredBatchesAsync(options.BatchDirectory, retentionPeriod, cancellationToken);
        }

        /// <inheritdoc/>
        public async Task<int> CleanupExpiredBatchesAsync(string batchDirectory, TimeSpan retentionPeriod, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(batchDirectory))
                throw new ArgumentException("Batch directory cannot be null or empty.", nameof(batchDirectory));

            if (!await this.storage.DirectoryExistsAsync(batchDirectory, cancellationToken).ConfigureAwait(false))
                return 0;

            if (retentionPeriod == TimeSpan.Zero)
                return 0; // Time-based cleanup disabled

            var expiredDirectories = await GetExpiredBatchDirectoriesAsync(batchDirectory, retentionPeriod, cancellationToken).ConfigureAwait(false);

            int cleanedCount = 0;

            foreach (var directory in expiredDirectories)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var success = await this.storage.DeleteBatchDirectoryAsync(directory, cancellationToken).ConfigureAwait(false);
                    
                    if(success)
                        cleanedCount++;
                }
                catch (Exception)
                {
                    // Log if needed, but don't fail the entire operation for one directory
                    // Individual directory cleanup failures should not break the batch cleanup
                }
            }

            return cleanedCount;
        }

        /// <inheritdoc />
        public Task<IList<string>> GetExpiredBatchDirectoriesAsync(SyncOptions options, TimeSpan retentionPeriod,
            CancellationToken cancellationToken = default)
        {
            return this.GetExpiredBatchDirectoriesAsync(options.BatchDirectory, retentionPeriod, cancellationToken);
        }

        /// <inheritdoc/>
        public async Task<IList<string>> GetExpiredBatchDirectoriesAsync(string batchDirectory, TimeSpan retentionPeriod, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(batchDirectory))
                throw new ArgumentException("Batch directory cannot be null or empty.", nameof(batchDirectory));

            if (!await this.storage.DirectoryExistsAsync(batchDirectory, cancellationToken).ConfigureAwait(false))
                return new List<string>();

            if (retentionPeriod == TimeSpan.Zero)
                return new List<string>(); // Time-based cleanup disabled

            // Calculate cutoff timestamp and format it as yyyyMMddHHmm
            var cutoffTime = DateTime.UtcNow - retentionPeriod;
            var cutoffTimestamp = cutoffTime.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture);

            try
            {
                var subdirectories = await this.storage.GetSubdirectoriesAsync(batchDirectory, cancellationToken).ConfigureAwait(false);

                return subdirectories
                    .Where(directoryName =>
                    {
                        // Skip error batch directories - these are used for retry mechanism
                        if (directoryName.Contains("_ERRORS", StringComparison.OrdinalIgnoreCase))
                            return false;

                        // Directory must start with yyyyMMddHHmm format and be older than cutoff
                        return directoryName.Length >= 12 &&
                               directoryName.Substring(0, 12).All(char.IsDigit) &&
                               string.Compare(directoryName.Substring(0, 12), cutoffTimestamp, StringComparison.Ordinal) <= 0;
                    })
                    .Select(directoryName => Path.Combine(batchDirectory, directoryName))
                    .OrderBy(Path.GetFileName)
                    .ToList();
            }
            catch (Exception)
            {
                // Return empty list if directory enumeration fails
                return new List<string>();
            }
        }
    }
}
