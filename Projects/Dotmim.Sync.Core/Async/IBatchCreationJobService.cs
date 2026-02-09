using System.Threading;
using System.Threading.Tasks;

namespace Wormhole.Sync.Async
{
    /// <summary>
    /// Service interface for managing batch creation jobs.
    /// Implementations can use in-memory queues (single-server) or distributed
    /// job systems like Hangfire, Azure Functions, etc. (scale-out).
    /// </summary>
    public interface IBatchCreationJobService
    {
        /// <summary>
        /// Enqueues a batch creation job for background processing.
        /// </summary>
        /// <param name="jobId">Unique identifier for the job. Use a deterministic ID based on
        /// client scope and session to enable idempotent retries.</param>
        /// <param name="parameters">Parameters needed to execute the batch creation.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The job ID for tracking.</returns>
        Task<string> EnqueueBatchCreationAsync(
            string jobId,
            BatchCreationJobParameters parameters,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the current status of a batch creation job.
        /// </summary>
        /// <param name="jobId">The job ID to check.</param>
        /// <returns>The job status, or null if the job doesn't exist.</returns>
        Task<BatchCreationJobStatus> GetJobStatusAsync(string jobId);

        /// <summary>
        /// Removes a job and its associated data.
        /// Should be called after the client has successfully retrieved all batches.
        /// </summary>
        /// <param name="jobId">The job ID to remove.</param>
        Task RemoveJobAsync(string jobId);
    }
}
