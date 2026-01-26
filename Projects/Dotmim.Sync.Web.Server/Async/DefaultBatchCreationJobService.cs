using Wormhole.Sync.Async;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Wormhole.Sync.Web.Server.Async
{
    /// <summary>
    /// Default implementation of <see cref="IBatchCreationJobService"/> using in-memory storage
    /// and a Channel-based queue. Suitable for single-server deployments.
    /// </summary>
    public class DefaultBatchCreationJobService : IBatchCreationJobService
    {
        private readonly InMemoryBatchJobStore store;
        private readonly Channel<string> jobQueue;
        private readonly ILogger<DefaultBatchCreationJobService> logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="DefaultBatchCreationJobService"/> class.
        /// </summary>
        /// <param name="store">The in-memory job store.</param>
        /// <param name="logger">The logger.</param>
        public DefaultBatchCreationJobService(
            InMemoryBatchJobStore store,
            ILogger<DefaultBatchCreationJobService> logger)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.jobQueue = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
            {
                SingleReader = false,
                SingleWriter = false,
            });
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
            var existingStatus = this.store.GetStatus(jobId);
            if (existingStatus != null)
            {
                this.logger.LogDebug("Job {JobId} already exists with state {State}", jobId, existingStatus.State);
                return jobId;
            }

            // Store parameters and initial status
            this.store.SetParameters(jobId, parameters);
            this.store.SetStatus(jobId, new BatchCreationJobStatus
            {
                JobId = jobId,
                State = BatchCreationJobState.Queued,
                EnqueuedAt = DateTime.UtcNow,
                TotalTables = parameters.ServerScopeInfo?.Schema?.Tables?.Count ?? 0,
            });

            // Enqueue for background processing
            await this.jobQueue.Writer.WriteAsync(jobId, cancellationToken).ConfigureAwait(false);
            this.logger.LogInformation("Enqueued batch creation job {JobId}", jobId);

            return jobId;
        }

        /// <inheritdoc />
        public Task<BatchCreationJobStatus> GetJobStatusAsync(string jobId)
            => Task.FromResult(this.store.GetStatus(jobId));

        /// <inheritdoc />
        public Task RemoveJobAsync(string jobId)
        {
            this.store.RemoveJob(jobId);
            this.logger.LogDebug("Removed job {JobId}", jobId);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Gets the channel reader for the job queue.
        /// Used by <see cref="BatchCreationWorkerService"/> to process jobs.
        /// </summary>
        internal ChannelReader<string> GetJobQueueReader() => this.jobQueue.Reader;

        /// <summary>
        /// Gets the in-memory job store.
        /// Used by <see cref="BatchCreationWorkerService"/> to update job status.
        /// </summary>
        internal InMemoryBatchJobStore Store => this.store;
    }
}
