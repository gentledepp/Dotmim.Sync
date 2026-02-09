using System;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.Extensions.Logging;
using Wormhole.Sync.Async;

namespace Wormhole.Sync.Web.Hangfire
{
    /// <summary>
    /// Hangfire implementation of <see cref="IBatchCreationJobService"/>.
    /// Uses Hangfire for distributed job processing, enabling scale-out deployments.
    /// </summary>
    /// <remarks>
    /// This service requires:
    /// - Hangfire to be configured with a persistent storage (SQL Server, Redis, etc.)
    /// - <see cref="IBatchJobStore"/> to be registered (use <see cref="DistributedBatchJobStore"/> for scale-out)
    /// - <see cref="HangfireBatchCreationJob"/> to be registered in the DI container
    /// </remarks>
    public class HangfireBatchCreationJobService : IBatchCreationJobService
    {
        private readonly IBackgroundJobClient backgroundJobClient;
        private readonly IBatchJobStore jobStore;
        private readonly ILogger<HangfireBatchCreationJobService> logger;
        private readonly HangfireBatchCreationJobServiceOptions options;

        /// <summary>
        /// Initializes a new instance of the <see cref="HangfireBatchCreationJobService"/> class.
        /// </summary>
        /// <param name="backgroundJobClient">The Hangfire background job client.</param>
        /// <param name="jobStore">The distributed job store.</param>
        /// <param name="logger">The logger.</param>
        /// <param name="options">Optional configuration options.</param>
        public HangfireBatchCreationJobService(
            IBackgroundJobClient backgroundJobClient,
            IBatchJobStore jobStore,
            ILogger<HangfireBatchCreationJobService> logger,
            HangfireBatchCreationJobServiceOptions options = null)
        {
            this.backgroundJobClient = backgroundJobClient ?? throw new ArgumentNullException(nameof(backgroundJobClient));
            this.jobStore = jobStore ?? throw new ArgumentNullException(nameof(jobStore));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.options = options ?? new HangfireBatchCreationJobServiceOptions();
        }

        /// <inheritdoc />
        public async Task<string> EnqueueBatchCreationAsync(
            string jobId,
            BatchCreationJobParameters parameters,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(jobId))
                throw new ArgumentNullException(nameof(jobId));
            if (parameters == null)
                throw new ArgumentNullException(nameof(parameters));

            // Check if job already exists (idempotent retry)
            var existingStatus = await this.jobStore.GetStatusAsync(jobId, cancellationToken).ConfigureAwait(false);
            if (existingStatus != null)
            {
                this.logger.LogDebug("Job {JobId} already exists with state {State}", jobId, existingStatus.State);
                return jobId;
            }

            // Store parameters and initial status
            await this.jobStore.SetParametersAsync(jobId, parameters, cancellationToken).ConfigureAwait(false);
            await this.jobStore.SetStatusAsync(jobId, new BatchCreationJobStatus
            {
                JobId = jobId,
                State = BatchCreationJobState.Queued,
                EnqueuedAt = DateTime.UtcNow,
                TotalTables = parameters.ServerScopeInfo?.Schema?.Tables?.Count ?? 0,
            }, cancellationToken).ConfigureAwait(false);

            // Enqueue job with Hangfire
            var hangfireJobId = this.backgroundJobClient.Enqueue<HangfireBatchCreationJob>(
                job => job.ExecuteAsync(jobId, null));

            this.logger.LogInformation(
                "Enqueued batch creation job {JobId} (Hangfire ID: {HangfireJobId}) to queue '{Queue}'",
                jobId, hangfireJobId, this.options.QueueName);

            return jobId;
        }

        /// <inheritdoc />
        public async Task<BatchCreationJobStatus> GetJobStatusAsync(string jobId)
        {
            return await this.jobStore.GetStatusAsync(jobId).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task RemoveJobAsync(string jobId)
        {
            await this.jobStore.RemoveJobAsync(jobId).ConfigureAwait(false);
            this.logger.LogDebug("Removed job {JobId}", jobId);
        }
    }

    /// <summary>
    /// Configuration options for <see cref="HangfireBatchCreationJobService"/>.
    /// </summary>
    public class HangfireBatchCreationJobServiceOptions
    {
        /// <summary>
        /// Gets or sets the Hangfire queue name for batch creation jobs.
        /// Default is "default".
        /// </summary>
        public string QueueName { get; set; } = "default";
    }
}
