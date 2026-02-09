using Wormhole.Sync.Async;
using Wormhole.Sync.Batch;
using Wormhole.Sync.Storage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Wormhole.Sync.Web.Server.Async
{
    /// <summary>
    /// Background service that processes batch creation jobs from the queue.
    /// </summary>
    public class BatchCreationWorkerService : BackgroundService
    {
        private readonly DefaultBatchCreationJobService jobService;
        private readonly IBatchCreationExecutor executor;
        private readonly ILogger<BatchCreationWorkerService> logger;
        private readonly int workerCount;
        private readonly TimeSpan jobCleanupInterval;
        private readonly TimeSpan jobMaxAge;

        /// <summary>
        /// Initializes a new instance of the <see cref="BatchCreationWorkerService"/> class.
        /// </summary>
        /// <param name="jobService">The batch creation job service.</param>
        /// <param name="executor">The batch creation executor.</param>
        /// <param name="logger">The logger.</param>
        /// <param name="workerCount">Number of parallel workers. Default is 2.</param>
        /// <param name="jobCleanupInterval">Interval for cleaning expired jobs. Default is 5 minutes.</param>
        /// <param name="jobMaxAge">Maximum age for completed/failed jobs before cleanup. Default is 1 hour.</param>
        public BatchCreationWorkerService(
            DefaultBatchCreationJobService jobService,
            IBatchCreationExecutor executor,
            ILogger<BatchCreationWorkerService> logger,
            int workerCount = 2,
            TimeSpan? jobCleanupInterval = null,
            TimeSpan? jobMaxAge = null)
        {
            this.jobService = jobService ?? throw new ArgumentNullException(nameof(jobService));
            this.executor = executor ?? throw new ArgumentNullException(nameof(executor));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.workerCount = Math.Max(1, workerCount);
            this.jobCleanupInterval = jobCleanupInterval ?? TimeSpan.FromMinutes(5);
            this.jobMaxAge = jobMaxAge ?? TimeSpan.FromHours(1);
        }

        /// <inheritdoc />
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            this.logger.LogInformation("BatchCreationWorkerService starting with {Count} workers", this.workerCount);

            // Start cleanup task
            var cleanupTask = this.CleanupExpiredJobsAsync(stoppingToken);

            // Start worker tasks
            var workerTasks = Enumerable.Range(0, this.workerCount)
                .Select(i => this.ProcessJobsAsync(i, stoppingToken))
                .ToArray();

            try
            {
                await Task.WhenAll(workerTasks.Append(cleanupTask)).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                this.logger.LogInformation("BatchCreationWorkerService stopped");
            }
        }

        private async Task ProcessJobsAsync(int workerId, CancellationToken stoppingToken)
        {
            var reader = this.jobService.GetJobQueueReader();

            // Use WaitToReadAsync/TryRead pattern for .NET Standard 2.0 compatibility
            // (ReadAllAsync is not available on older frameworks)
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Wait for data to be available
                    if (!await reader.WaitToReadAsync(stoppingToken).ConfigureAwait(false))
                        break; // Channel was completed

                    // Try to read all available items
                    while (reader.TryRead(out var jobId))
                    {
                        if (stoppingToken.IsCancellationRequested)
                            break;

                        this.logger.LogDebug("Worker {WorkerId} picked up job {JobId}", workerId, jobId);
                        try
                        {
                            await this.ProcessJobAsync(jobId, stoppingToken).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                        {
                            this.logger.LogWarning("Worker {WorkerId} job {JobId} cancelled due to shutdown", workerId, jobId);
                            return;
                        }
                        catch (Exception ex)
                        {
                            this.logger.LogError(ex, "Worker {WorkerId} error processing job {JobId}", workerId, jobId);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private async Task ProcessJobAsync(string jobId, CancellationToken cancellationToken)
        {
            var store = this.jobService.Store;
            var parameters = store.GetParameters(jobId);
            var status = store.GetStatus(jobId);

            if (parameters == null || status == null)
            {
                this.logger.LogWarning("Job {JobId} not found in store, skipping", jobId);
                return;
            }

            // Update to Processing state
            status.State = BatchCreationJobState.Processing;
            status.StartedAt = DateTime.UtcNow;
            store.SetStatus(jobId, status);

            try
            {
                // Define progress callback for incremental updates
                BatchPartProgressCallback onBatchPartCreated = async (BatchInfo bi, BatchPartInfo bpi, int processed, int total) =>
                {
                    var currentStatus = store.GetStatus(jobId);
                    if (currentStatus == null) return;

                    // Add to available batch parts list
                    currentStatus.AvailableBatchParts.Add(bpi);
                    currentStatus.TotalBatchPartsCreated++;
                    currentStatus.TablesProcessed = processed;
                    currentStatus.TotalTables = total;
                    currentStatus.BatchInfo = bi;

                    // Set FirstBatchReady state after first batch
                    if (currentStatus.State == BatchCreationJobState.Processing &&
                        currentStatus.TotalBatchPartsCreated == 1)
                    {
                        currentStatus.State = BatchCreationJobState.FirstBatchReady;
                    }

                    // Calculate progress percentage
                    currentStatus.ProgressPercentage = total > 0 ? (int)(processed * 100.0 / total) : 0;

                    store.SetStatus(jobId, currentStatus);

                    this.logger.LogDebug(
                        "Job {JobId} - Batch part created: {FileName} ({Index}), Tables: {Processed}/{Total}",
                        jobId, bpi.FileName, bpi.Index, processed, total);
                };

                // Execute with callback - eliminates all duplicated sync logic
                var result = await this.executor.ExecuteAsync(
                    jobId, parameters, onBatchPartCreated, cancellationToken).ConfigureAwait(false);

                if (!result.Success)
                {
                    // Executor returned failure
                    status = store.GetStatus(jobId);
                    status.State = BatchCreationJobState.Failed;
                    status.CompletedAt = DateTime.UtcNow;
                    status.ErrorMessage = result.ErrorMessage;
                    status.ErrorStackTrace = result.ErrorStackTrace;
                    store.SetStatus(jobId, status);
                    return;
                }

                // Update final status
                status = store.GetStatus(jobId);
                status.State = BatchCreationJobState.Completed;
                status.CompletedAt = DateTime.UtcNow;
                status.ProgressPercentage = 100;
                status.RemoteClientTimestamp = result.RemoteClientTimestamp;
                status.BatchInfo = result.BatchInfo;
                status.ChangesSelected = result.ChangesSelected;
                status.ChangesApplied = result.ChangesApplied;
                store.SetStatus(jobId, status);

                this.logger.LogInformation(
                    "Job {JobId} completed. Batches: {BatchCount}, ServerRows: {ServerRows}, ClientApplied: {ClientApplied}",
                    jobId,
                    result.BatchInfo?.BatchPartsInfo?.Count ?? 0,
                    result.ChangesSelected?.TotalChangesSelected ?? 0,
                    result.ChangesApplied?.TotalAppliedChanges ?? 0);
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Job {JobId} failed", jobId);

                status = store.GetStatus(jobId);
                status.State = BatchCreationJobState.Failed;
                status.CompletedAt = DateTime.UtcNow;
                status.ErrorMessage = ex.Message;
                status.ErrorStackTrace = ex.StackTrace;
                store.SetStatus(jobId, status);
            }
        }

        private async Task CleanupExpiredJobsAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(this.jobCleanupInterval, stoppingToken).ConfigureAwait(false);

                    var store = this.jobService.Store;
                    var expiredJobs = store.GetExpiredJobIds(this.jobMaxAge).ToList();

                    foreach (var jobId in expiredJobs)
                    {
                        store.RemoveJob(jobId);
                        this.logger.LogDebug("Cleaned up expired job {JobId}", jobId);
                    }

                    if (expiredJobs.Count > 0)
                        this.logger.LogInformation("Cleaned up {Count} expired jobs", expiredJobs.Count);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    this.logger.LogError(ex, "Error during job cleanup");
                }
            }
        }
    }
}
