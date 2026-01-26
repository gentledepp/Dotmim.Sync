using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Hangfire.Server;
using Microsoft.Extensions.Logging;
using Wormhole.Sync.Async;

namespace Wormhole.Sync.Web.Hangfire
{
    /// <summary>
    /// Hangfire job that executes batch creation for synchronization.
    /// This job is designed to run in a distributed environment where multiple server instances
    /// can process jobs from a shared job storage (SQL Server, Redis, etc.).
    /// </summary>
    public class HangfireBatchCreationJob
    {
        private readonly IBatchJobStore jobStore;
        private readonly IBatchCreationExecutor executor;
        private readonly ILogger<HangfireBatchCreationJob> logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="HangfireBatchCreationJob"/> class.
        /// </summary>
        /// <param name="jobStore">The distributed job store for status updates.</param>
        /// <param name="executor">The batch creation executor.</param>
        /// <param name="logger">The logger.</param>
        public HangfireBatchCreationJob(
            IBatchJobStore jobStore,
            IBatchCreationExecutor executor,
            ILogger<HangfireBatchCreationJob> logger)
        {
            this.jobStore = jobStore ?? throw new ArgumentNullException(nameof(jobStore));
            this.executor = executor ?? throw new ArgumentNullException(nameof(executor));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Executes the batch creation job.
        /// </summary>
        /// <param name="jobId">The unique job identifier.</param>
        /// <param name="context">The Hangfire perform context (provides cancellation token).</param>
        [DisplayName("Batch Creation: {0}")]
        [AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 10, 60, 300 })]
        public async Task ExecuteAsync(string jobId, PerformContext context)
        {
            var cancellationToken = context?.CancellationToken.ShutdownToken ?? CancellationToken.None;

            this.logger.LogInformation("Starting batch creation job {JobId}", jobId);

            var parameters = await this.jobStore.GetParametersAsync(jobId, cancellationToken).ConfigureAwait(false);
            var status = await this.jobStore.GetStatusAsync(jobId, cancellationToken).ConfigureAwait(false);

            if (parameters == null || status == null)
            {
                this.logger.LogWarning("Job {JobId} not found in store, skipping", jobId);
                return;
            }

            // Update to Processing state
            status.State = BatchCreationJobState.Processing;
            status.StartedAt = DateTime.UtcNow;
            await this.jobStore.SetStatusAsync(jobId, status, cancellationToken).ConfigureAwait(false);

            try
            {
                // Delegate the actual sync execution to the executor
                var result = await this.executor.ExecuteAsync(jobId, parameters, cancellationToken).ConfigureAwait(false);

                if (result.Success)
                {
                    // Update status with results
                    status.State = BatchCreationJobState.Completed;
                    status.CompletedAt = DateTime.UtcNow;
                    status.ProgressPercentage = 100;
                    status.RemoteClientTimestamp = result.RemoteClientTimestamp;
                    status.BatchInfo = result.BatchInfo;
                    status.ChangesSelected = result.ChangesSelected;
                    status.ChangesApplied = result.ChangesApplied;
                    await this.jobStore.SetStatusAsync(jobId, status, cancellationToken).ConfigureAwait(false);

                    this.logger.LogInformation(
                        "Job {JobId} completed. Batches: {BatchCount}, ServerRows: {ServerRows}, ClientApplied: {ClientApplied}",
                        jobId,
                        result.BatchInfo?.BatchPartsInfo?.Count ?? 0,
                        result.ChangesSelected?.TotalChangesSelected ?? 0,
                        result.ChangesApplied?.TotalAppliedChanges ?? 0);
                }
                else
                {
                    // Executor returned failure
                    status.State = BatchCreationJobState.Failed;
                    status.CompletedAt = DateTime.UtcNow;
                    status.ErrorMessage = result.ErrorMessage;
                    status.ErrorStackTrace = result.ErrorStackTrace;
                    await this.jobStore.SetStatusAsync(jobId, status, CancellationToken.None).ConfigureAwait(false);

                    throw new InvalidOperationException(result.ErrorMessage);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                this.logger.LogWarning("Job {JobId} was cancelled", jobId);

                status.State = BatchCreationJobState.Cancelled;
                status.CompletedAt = DateTime.UtcNow;
                status.ErrorMessage = "Job was cancelled";
                await this.jobStore.SetStatusAsync(jobId, status, CancellationToken.None).ConfigureAwait(false);

                throw; // Re-throw so Hangfire knows the job was cancelled
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Job {JobId} failed", jobId);

                status.State = BatchCreationJobState.Failed;
                status.CompletedAt = DateTime.UtcNow;
                status.ErrorMessage = ex.Message;
                status.ErrorStackTrace = ex.StackTrace;
                await this.jobStore.SetStatusAsync(jobId, status, CancellationToken.None).ConfigureAwait(false);

                throw; // Re-throw so Hangfire can handle retries
            }
        }
    }
}
