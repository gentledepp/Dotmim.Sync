using Hangfire;
using Hangfire.Server;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Wormhole.Sync.Async;
using Wormhole.Sync.Batch;

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
        private readonly IBatchCreationExecutorProvider executorFactory;
        private readonly IHangfireContextLoggerProvider<HangfireBatchCreationJob> hangfireContextLoggerProvider;
        private readonly ILogger<HangfireBatchCreationJob> logger2;
        private ILogger<HangfireBatchCreationJob> logger;
        private IBatchCreationExecutor executor;

        /// <summary>
        /// Initializes a new instance of the <see cref="HangfireBatchCreationJob"/> class.
        /// </summary>
        /// <param name="jobStore">The distributed job store for status updates.</param>
        /// <param name="executor">The batch creation executor.</param>
        /// <param name="logger">The logger.</param>
        /// <param name="hangfireContextLoggerProvider"></param>
        public HangfireBatchCreationJob(
            IBatchJobStore jobStore,
            IBatchCreationExecutor executor,
            ILogger<HangfireBatchCreationJob> logger,
            IHangfireContextLoggerProvider<HangfireBatchCreationJob> hangfireContextLoggerProvider)
        {
            this.jobStore = jobStore ?? throw new ArgumentNullException(nameof(jobStore));
            this.executor = executor ?? throw new ArgumentNullException(nameof(executor));
            this.logger2 = logger ?? throw new ArgumentNullException(nameof(logger));
            this.hangfireContextLoggerProvider = hangfireContextLoggerProvider;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="HangfireBatchCreationJob"/> class.
        /// </summary>
        /// <param name="jobStore">The distributed job store for status updates.</param>
        /// <param name="executorFactory">allows to create a batchcreationexecutor based on the job parameters (multi-tenant-capable)</param>
        /// <param name="logger">The logger.</param>
        /// <param name="hangfireContextLoggerProvider"></param>
        public HangfireBatchCreationJob(
            IBatchJobStore jobStore,
            IBatchCreationExecutorProvider executorFactory,
            ILogger<HangfireBatchCreationJob> logger,
            IHangfireContextLoggerProvider<HangfireBatchCreationJob> hangfireContextLoggerProvider)
        {
            this.jobStore = jobStore ?? throw new ArgumentNullException(nameof(jobStore));
            this.executorFactory = executorFactory;
            this.logger2 = logger ?? throw new ArgumentNullException(nameof(logger));
            this.hangfireContextLoggerProvider = hangfireContextLoggerProvider;
        }

        /// <summary>
        /// Executes the batch creation job.
        /// </summary>
        /// <param name="jobId">The unique job identifier.</param>
        /// <param name="context">The Hangfire perform context (provides cancellation token).</param>
        [DisplayName("Batch Creation: {0}")]
        [AutomaticRetry(Attempts = 1)]
        public async Task ExecuteAsync(string jobId, PerformContext context)
        {
            var cancellationToken = context?.CancellationToken.ShutdownToken ?? CancellationToken.None;

            this.logger = this.hangfireContextLoggerProvider.Provide(this.logger2, context);

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
                // Define progress callback for incremental updates
                BatchPartProgressCallback onBatchPartCreated = async (BatchInfo batchInfo, BatchPartInfo bpi, int processed, int total) =>
                {
                    var currentStatus = await this.jobStore.GetStatusAsync(jobId, cancellationToken).ConfigureAwait(false);
                    if (currentStatus == null) return;

                    // Add to available batch parts list
                    currentStatus.AvailableBatchParts.Add(bpi);
                    currentStatus.TotalBatchPartsCreated++;
                    currentStatus.TablesProcessed = processed;
                    currentStatus.TotalTables = total;
                    currentStatus.BatchInfo = batchInfo;

                    // Set FirstBatchReady state after first batch
                    if (currentStatus.State == BatchCreationJobState.Processing &&
                        currentStatus.TotalBatchPartsCreated == 1)
                    {
                        currentStatus.State = BatchCreationJobState.FirstBatchReady;
                    }

                    // Calculate progress percentage
                    currentStatus.ProgressPercentage = total > 0 ? (int)(processed * 100.0 / total) : 0;

                    await this.jobStore.SetStatusAsync(jobId, currentStatus, cancellationToken).ConfigureAwait(false);

                    this.logger.LogDebug(
                        "Job {JobId} - Batch part created: {FileName} ({Index}), Tables: {Processed}/{Total}",
                        jobId, bpi.FileName, bpi.Index, processed, total);
                };

                this.executor ??= this.executorFactory.Provide(parameters);

                // Delegate the actual sync execution to the executor with callback
                var result = await this.executor.ExecuteAsync(jobId, parameters, onBatchPartCreated, cancellationToken).ConfigureAwait(false);

                if (result.Success)
                {
                    // Update final status
                    status = await this.jobStore.GetStatusAsync(jobId, cancellationToken).ConfigureAwait(false);
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
