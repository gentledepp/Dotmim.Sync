using Wormhole.Sync.Enumerations;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Wormhole.Sync
{
    /// <summary>
    /// Interface for orchestrators that support incremental sync protocol optimizations.
    /// Enables skipping initial HTTP requests for subsequent syncs when server capabilities are cached.
    /// </summary>
    public interface IIncrementalSyncOrchestrator
    {
        /// <summary>
        /// Determines if the orchestrator can skip the initial BeginSession, EnsureScopes, and GetOperation requests
        /// by using cached server capabilities and schema hash validation.
        /// </summary>
        /// <param name="scopeInfo">Client scope information with cached server capabilities</param>
        /// <param name="scopeInfoClient">Client scope info with sync metadata</param>
        /// <returns>True if initial requests can be skipped for optimized sync</returns>
        bool CanUseOptimizedSync(ScopeInfo scopeInfo, ScopeInfoClient scopeInfoClient);

        /// <summary>
        /// Performs an optimized sync that combines multiple protocol steps into fewer HTTP requests.
        /// Skips BeginSession, EnsureScopes, and GetOperation by using cached capabilities.
        /// </summary>
        /// <param name="scopeInfoClient">Client scope info with sync metadata</param>
        /// <param name="scopeInfo">Client scope information</param>
        /// <param name="context">Sync context</param>
        /// <param name="clientChanges">Client changes to send</param>
        /// <param name="connection">Database connection</param>
        /// <param name="transaction">Database transaction</param>
        /// <param name="progress">Progress reporter</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Context, schema validity, operation type, server scope info, updated client scope info, server sync changes, and conflict resolution policy</returns>
        Task<(SyncContext Context, bool isSchemaValid, SyncOperation Operation, ScopeInfo? ServerScopeInfo, ScopeInfo UpdatedClientScopeInfo, ServerSyncChanges ServerSyncChanges, ConflictResolutionPolicy ServerResolutionPolicy)>
            SynchronizeOptimizedAsync(ScopeInfoClient scopeInfoClient, ScopeInfo scopeInfo,
            SyncContext context, ClientSyncChanges clientChanges,
            System.Data.Common.DbConnection connection, System.Data.Common.DbTransaction transaction,
            IProgress<ProgressArgs> progress, CancellationToken cancellationToken);

        /// <summary>
        /// Reports sync errors to the server for analytics and debugging (fire-and-forget).
        /// Only sends reports when server supports error reporting capability.
        /// </summary>
        /// <param name="context">Sync context</param>
        /// <param name="exception">Exception to report</param>
        /// <param name="errorContext">Additional error context</param>
        /// <param name="progress">Progress reporter</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>True if error was reported successfully, false otherwise</returns>
        Task<bool> ReportSyncErrorAsync(SyncContext context, Exception exception,
            SyncErrorContext errorContext = null, IProgress<ProgressArgs> progress = null,
            CancellationToken cancellationToken = default);
    }
}