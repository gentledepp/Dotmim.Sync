using Wormhole.Sync.Async;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Wormhole.Sync.Web.Server.Async
{
    /// <summary>
    /// Singleton in-memory storage for job state. Single-server only.
    /// For scale-out deployments, use a distributed store implementation like HangfireBatchJobStore.
    /// </summary>
    public class InMemoryBatchJobStore : IBatchJobStore
    {
        private readonly ConcurrentDictionary<string, BatchCreationJobStatus> statuses = new();
        private readonly ConcurrentDictionary<string, BatchCreationJobParameters> parameters = new();

        /// <inheritdoc />
        public Task SetParametersAsync(string jobId, BatchCreationJobParameters jobParameters, CancellationToken cancellationToken = default)
        {
            this.parameters[jobId] = jobParameters;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<BatchCreationJobParameters> GetParametersAsync(string jobId, CancellationToken cancellationToken = default)
        {
            var result = this.parameters.TryGetValue(jobId, out var p) ? p : null;
            return Task.FromResult(result);
        }

        /// <inheritdoc />
        public Task SetStatusAsync(string jobId, BatchCreationJobStatus status, CancellationToken cancellationToken = default)
        {
            this.statuses[jobId] = status;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<BatchCreationJobStatus> GetStatusAsync(string jobId, CancellationToken cancellationToken = default)
        {
            var result = this.statuses.TryGetValue(jobId, out var s) ? s : null;
            return Task.FromResult(result);
        }

        /// <inheritdoc />
        public Task RemoveJobAsync(string jobId, CancellationToken cancellationToken = default)
        {
            this.statuses.TryRemove(jobId, out _);
            this.parameters.TryRemove(jobId, out _);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<IEnumerable<string>> GetExpiredJobIdsAsync(TimeSpan maxAge, CancellationToken cancellationToken = default)
        {
            var cutoff = DateTime.UtcNow - maxAge;
            var result = this.statuses
                .Where(kvp => kvp.Value.EnqueuedAt < cutoff &&
                              (kvp.Value.State == BatchCreationJobState.Completed ||
                               kvp.Value.State == BatchCreationJobState.Failed ||
                               kvp.Value.State == BatchCreationJobState.Cancelled))
                .Select(kvp => kvp.Key)
                .ToList();
            return Task.FromResult<IEnumerable<string>>(result);
        }

        /// <inheritdoc />
        public Task<int> GetCountAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(this.statuses.Count);
        }

        // Keep synchronous methods for backward compatibility

        /// <summary>
        /// Sets the parameters for a job.
        /// </summary>
        /// <param name="jobId">The job identifier.</param>
        /// <param name="jobParameters">The job parameters.</param>
        public void SetParameters(string jobId, BatchCreationJobParameters jobParameters)
            => this.parameters[jobId] = jobParameters;

        /// <summary>
        /// Gets the parameters for a job.
        /// </summary>
        /// <param name="jobId">The job identifier.</param>
        /// <returns>The job parameters, or null if not found.</returns>
        public BatchCreationJobParameters GetParameters(string jobId)
            => this.parameters.TryGetValue(jobId, out var p) ? p : null;

        /// <summary>
        /// Sets the status for a job.
        /// </summary>
        /// <param name="jobId">The job identifier.</param>
        /// <param name="status">The job status.</param>
        public void SetStatus(string jobId, BatchCreationJobStatus status)
            => this.statuses[jobId] = status;

        /// <summary>
        /// Gets the status for a job.
        /// </summary>
        /// <param name="jobId">The job identifier.</param>
        /// <returns>The job status, or null if not found.</returns>
        public BatchCreationJobStatus GetStatus(string jobId)
            => this.statuses.TryGetValue(jobId, out var s) ? s : null;

        /// <summary>
        /// Removes a job and its associated data.
        /// </summary>
        /// <param name="jobId">The job identifier.</param>
        public void RemoveJob(string jobId)
        {
            this.statuses.TryRemove(jobId, out _);
            this.parameters.TryRemove(jobId, out _);
        }

        /// <summary>
        /// Gets job IDs that have been completed or failed for longer than the specified age.
        /// </summary>
        /// <param name="maxAge">Maximum age before a job is considered expired.</param>
        /// <returns>Enumerable of expired job IDs.</returns>
        public IEnumerable<string> GetExpiredJobIds(TimeSpan maxAge)
        {
            var cutoff = DateTime.UtcNow - maxAge;
            return this.statuses
                .Where(kvp => kvp.Value.EnqueuedAt < cutoff &&
                              (kvp.Value.State == BatchCreationJobState.Completed ||
                               kvp.Value.State == BatchCreationJobState.Failed ||
                               kvp.Value.State == BatchCreationJobState.Cancelled))
                .Select(kvp => kvp.Key)
                .ToList();
        }

        /// <summary>
        /// Gets the total number of jobs in the store.
        /// </summary>
        public int Count => this.statuses.Count;
    }
}
