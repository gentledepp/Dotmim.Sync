using System.Threading;
using System.Threading.Tasks;
using Wormhole.Sync.Batch;

namespace Wormhole.Sync.Async
{
    /// <summary>
    /// Interface for executing batch creation operations.
    /// This allows the Hangfire project to delegate the actual sync execution
    /// to the Web.Server project which has access to internal APIs.
    /// </summary>
    public interface IBatchCreationExecutor
    {
        /// <summary>
        /// Executes the batch creation operation using the provided job parameters.
        /// </summary>
        /// <param name="jobId">The unique job identifier.</param>
        /// <param name="parameters">The batch creation parameters.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The result of the batch creation operation.</returns>
        Task<BatchCreationResult> ExecuteAsync(
            string jobId,
            BatchCreationJobParameters parameters,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Result of a batch creation operation.
    /// </summary>
    public class BatchCreationResult
    {
        /// <summary>
        /// Gets or sets a value indicating whether the operation was successful.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Gets or sets the remote client timestamp.
        /// </summary>
        public long RemoteClientTimestamp { get; set; }

        /// <summary>
        /// Gets or sets the server batch info containing the created batch parts.
        /// </summary>
        public BatchInfo BatchInfo { get; set; }

        /// <summary>
        /// Gets or sets the changes selected from the server.
        /// </summary>
        public DatabaseChangesSelected ChangesSelected { get; set; }

        /// <summary>
        /// Gets or sets the changes applied on the server.
        /// </summary>
        public DatabaseChangesApplied ChangesApplied { get; set; }

        /// <summary>
        /// Gets or sets the error message if the operation failed.
        /// </summary>
        public string ErrorMessage { get; set; }

        /// <summary>
        /// Gets or sets the error stack trace if the operation failed.
        /// </summary>
        public string ErrorStackTrace { get; set; }
    }
}
