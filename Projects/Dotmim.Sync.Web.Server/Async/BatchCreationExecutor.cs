using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Wormhole.Sync.Async;
using Wormhole.Sync.Storage;

namespace Wormhole.Sync.Web.Server.Async
{
    /// <summary>
    /// Default implementation of <see cref="IBatchCreationExecutor"/> that executes
    /// the actual synchronization operation to create server batches.
    /// </summary>
    public class BatchCreationExecutor : IBatchCreationExecutor
    {
        private readonly SyncOptions options;
        private readonly CoreProvider provider;
        private readonly IBatchStorage batchStorage;
        private readonly ILogger<BatchCreationExecutor> logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="BatchCreationExecutor"/> class.
        /// </summary>
        /// <param name="options"></param>
        /// <param name="provider"></param>
        /// <param name="batchStorage"></param>
        /// <param name="logger">The logger.</param>
        public BatchCreationExecutor(SyncOptions options,
            CoreProvider provider,
            IBatchStorage batchStorage, 
            ILogger<BatchCreationExecutor> logger)
        {
            this.options = options;
            this.provider = provider;
            this.batchStorage = batchStorage;
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public async Task<BatchCreationResult> ExecuteAsync(
            string jobId,
            BatchCreationJobParameters parameters,
            BatchPartProgressCallback onBatchPartCreated = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(jobId))
                throw new ArgumentNullException(nameof(jobId));
            if (parameters == null)
                throw new ArgumentNullException(nameof(parameters));

            this.logger.LogInformation("Executing batch creation for job {JobId}", jobId);

            try
            {

                var orchestrator = new RemoteOrchestrator(provider, options)
                {
                    BatchStorage = this.batchStorage
                };

                // Register progress callback via OnBatchChangesCreated interceptor
                if (onBatchPartCreated != null)
                {
                    orchestrator.OnBatchChangesCreated(async args =>
                    {
                        if (args.BatchPartInfo != null && args.BatchPartInfo.RowsCount > 0)
                        {
                            // Estimate table progress (can be refined later with SyncContext tracking)
                            var totalTables = parameters.ServerScopeInfo?.Schema?.Tables?.Count ?? 1;

                            await onBatchPartCreated(args.BatchInfo, args.BatchPartInfo, args.BatchPartInfo.TableRowCounts.Keys.Count, totalTables);
                        }
                    });
                }

                // Create client sync changes from client batch info
                var clientSyncChanges = new ClientSyncChanges(
                    parameters.ClientScopeInfoClient?.LastSyncTimestamp ?? 0,
                    parameters.ClientBatchInfo,
                    null,
                    null);

                // Execute sync operation (using internal API which is accessible from this assembly)
                var (context, serverSyncChanges, _) = await orchestrator.InternalApplyThenGetChangesAsync(
                    parameters.ClientScopeInfoClient,
                    parameters.ServerScopeInfo,
                    parameters.Context,
                    clientSyncChanges,
                    default, default, default, cancellationToken).ConfigureAwait(false);

                this.logger.LogInformation(
                    "Job {JobId} completed. Batches: {BatchCount}, ServerRows: {ServerRows}, ClientApplied: {ClientApplied}",
                    jobId,
                    serverSyncChanges.ServerBatchInfo?.BatchPartsInfo?.Count ?? 0,
                    serverSyncChanges.ServerChangesSelected?.TotalChangesSelected ?? 0,
                    serverSyncChanges.ServerChangesApplied?.TotalAppliedChanges ?? 0);

                return new BatchCreationResult
                {
                    Success = true,
                    RemoteClientTimestamp = serverSyncChanges.RemoteClientTimestamp,
                    BatchInfo = serverSyncChanges.ServerBatchInfo,
                    ChangesSelected = serverSyncChanges.ServerChangesSelected,
                    ChangesApplied = serverSyncChanges.ServerChangesApplied,
                };
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Job {JobId} failed", jobId);

                return new BatchCreationResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    ErrorStackTrace = ex.StackTrace,
                };
            }
        }
    }
}
