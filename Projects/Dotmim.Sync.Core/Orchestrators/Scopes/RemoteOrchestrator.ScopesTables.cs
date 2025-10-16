using Wormhole.Sync.Builders;
using Wormhole.Sync.Enumerations;
using System;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Wormhole.Sync
{
    /// <summary>
    /// Contains the logic to handle scopes tables on the remote/server side.
    /// </summary>
    public partial class RemoteOrchestrator : BaseOrchestrator
    {
        /// <summary>
        /// Ensure custom columns exist in scope_info_client table based on Setup parameters.
        /// This is called before saving scope_info_client to add any missing custom columns.
        /// Uses provider-specific DbScopeBuilder methods to generate database-specific SQL.
        /// </summary>
        internal async Task InternalEnsureScopeInfoClientCustomColumnsAsync(ScopeInfoClientParameters customParameters, SyncContext context,
            DbConnection connection, DbTransaction transaction, IProgress<ProgressArgs> progress, CancellationToken cancellationToken)
        {
            if (customParameters == null || customParameters.Count == 0)
                return;

            // Check provisioning cache for custom columns
            if (this.ProvisioningCache != null)
            {
                var connectionString = this.Provider?.ConnectionString;
                // For custom columns, we use null for setup and just the customParameters
                var (found, areColumnsProvisioned) = await this.ProvisioningCache.TryGetProvisioningStateAsync(connectionString, null, customParameters, context.ScopeName, cancellationToken).ConfigureAwait(false);
                if (found && areColumnsProvisioned)
                {
                    // Columns already provisioned according to cache
                    return;
                }
            }

            try
            {
                var scopeBuilder = this.GetScopeBuilder(this.Options.ScopeInfoTableName);
                var atLeastOneColumnAdded = false;

                using var runner = await this.GetConnectionAsync(context, SyncMode.NoTransaction, SyncStage.None, connection, transaction, progress, cancellationToken).ConfigureAwait(false);
                await using (runner.ConfigureAwait(false))
                {
                    foreach (var customParam in customParameters)
                    {
                        // Check if column exists using provider-specific command
                        using var checkCommand = scopeBuilder.GetExistsScopeInfoClientColumnCommand(runner.Connection, runner.Transaction, customParam.Name);

                        // If provider doesn't support custom columns (e.g., SQLite client), skip
                        if (checkCommand == null)
                            continue;

                        var existsResult = await checkCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                        var exists = Convert.ToInt32(existsResult) > 0;

                        if (!exists)
                        {
                            // Add column using provider-specific command
                            using var alterCommand = scopeBuilder.GetAddScopeInfoClientColumnCommand(runner.Connection, runner.Transaction, customParam);

                            if (alterCommand != null)
                            {
                                await alterCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                                atLeastOneColumnAdded = true;
                            }
                        }
                    }

                    // Update cache after successful column provisioning
                    if (this.ProvisioningCache != null && !atLeastOneColumnAdded)
                    {
                        // All columns already existed, mark as provisioned in cache
                        var connectionString = this.Provider?.ConnectionString;
                        await this.ProvisioningCache.SetProvisioningStateAsync(connectionString, null, customParameters, context.ScopeName, true, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                throw this.GetSyncError(context, ex, $"Error ensuring custom columns for scope_info_client");
            }
        }
    }
}
