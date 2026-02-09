using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Wormhole.Sync.Async
{
    /// <summary>
    /// Interface for storing batch job state.
    /// Implement this interface for distributed storage in scale-out scenarios.
    /// </summary>
    public interface IBatchJobStore
    {
        /// <summary>
        /// Sets the parameters for a job.
        /// </summary>
        /// <param name="jobId">The job identifier.</param>
        /// <param name="jobParameters">The job parameters.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task SetParametersAsync(string jobId, BatchCreationJobParameters jobParameters, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the parameters for a job.
        /// </summary>
        /// <param name="jobId">The job identifier.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The job parameters, or null if not found.</returns>
        Task<BatchCreationJobParameters> GetParametersAsync(string jobId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sets the status for a job.
        /// </summary>
        /// <param name="jobId">The job identifier.</param>
        /// <param name="status">The job status.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task SetStatusAsync(string jobId, BatchCreationJobStatus status, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the status for a job.
        /// </summary>
        /// <param name="jobId">The job identifier.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The job status, or null if not found.</returns>
        Task<BatchCreationJobStatus> GetStatusAsync(string jobId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Removes a job and its associated data.
        /// </summary>
        /// <param name="jobId">The job identifier.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task RemoveJobAsync(string jobId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets job IDs that have been completed or failed for longer than the specified age.
        /// </summary>
        /// <param name="maxAge">Maximum age before a job is considered expired.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Enumerable of expired job IDs.</returns>
        Task<IEnumerable<string>> GetExpiredJobIdsAsync(TimeSpan maxAge, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the total number of jobs in the store.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The number of jobs.</returns>
        Task<int> GetCountAsync(CancellationToken cancellationToken = default);
    }
}
