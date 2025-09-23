using Dotmim.Sync.Enumerations;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Dotmim.Sync
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
        /// <returns>Server sync changes and conflict resolution policy</returns>
        Task<(SyncContext Context, bool isSchemaValid, SyncOperation, ScopeInfo?, ServerSyncChanges ServerSyncChanges, ConflictResolutionPolicy ServerResolutionPolicy)>
            SynchronizeOptimizedAsync(ScopeInfoClient scopeInfoClient, ScopeInfo scopeInfo,
            SyncContext context, ClientSyncChanges clientChanges,
            System.Data.Common.DbConnection connection, System.Data.Common.DbTransaction transaction,
            IProgress<ProgressArgs> progress, CancellationToken cancellationToken);
    }
}