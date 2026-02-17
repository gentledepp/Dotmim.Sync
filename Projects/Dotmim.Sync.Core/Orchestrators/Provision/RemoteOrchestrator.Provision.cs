using Wormhole.Sync.Builders;
using Wormhole.Sync.Enumerations;
using Wormhole.Sync.Extensions;
using Wormhole.Sync.Serialization;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Wormhole.Sync
{
    /// <summary>
    /// Contains remote orchestrator provisioning methods.
    /// </summary>
    public partial class RemoteOrchestrator : BaseOrchestrator
    {
        /// <summary>
        /// Provision a server datasource (<strong>triggers</strong>, <strong>stored procedures</strong> (if supported) and <strong>tracking tables</strong> if needed. Create also <strong>scope_info</strong> and <strong>scope_info_client</strong> tables.
        /// <para>
        /// The <paramref name="provision" /> argument specify the objects to provision. See <see cref="SyncProvision" /> enumeration.
        /// </para>
        /// <para>
        /// If The <paramref name="setup" /> argument is not specified, setup is retrieved from the scope_info table. Means that you have done a provision before.
        /// </para>
        /// <para>
        /// <example>
        /// <code>
        /// var remoteOrchestrator = new RemoteOrchestrator(serverProvider);
        /// var setup = new SyncSetup("ProductCategory", "Product");
        /// var sScopeInfo = await remoteOrchestrator.ProvisionAsync(setup);
        /// </code>
        /// </example>
        /// </para>
        /// </summary>
        /// <param name="scopeName">Scope name.</param>
        /// <param name="setup">Setup containing all tables to provision on the server side.</param>
        /// <param name="provision">If you do not specify <c>provision</c>, a default value <c>SyncProvision.StoredProcedures | SyncProvision.Triggers | SyncProvision.TrackingTable</c> is used.</param>
        /// <param name="overwrite">If specified, all metadatas are generated and overwritten even if they already exists.</param>
        /// <param name="connection">Optional Connection.</param>
        /// <param name="transaction">Optional Transaction.</param>
        /// <param name="progress">option IProgress{ProgressArgs}.</param>
        /// <param name="cancellationToken">optional cancellation token.</param>
        /// <returns>
        /// A <see cref="ScopeInfo"/> instance, saved locally in the server datasource.
        /// </returns>
        public virtual async Task<ScopeInfo> ProvisionAsync(string scopeName, SyncSetup setup = null, SyncProvision provision = default, bool overwrite = false,
            DbConnection connection = null, DbTransaction transaction = null, IProgress<ProgressArgs> progress = null, CancellationToken cancellationToken = default)
        {
            var context = new SyncContext(Guid.NewGuid(), scopeName);
            try
            {
                using var runner = await this.GetConnectionAsync(context, SyncMode.WithTransaction, SyncStage.Provisioning, connection, transaction, progress, cancellationToken).ConfigureAwait(false);
                await using (runner.ConfigureAwait(false))
                {
                    ScopeInfo sScopeInfo;
                    (context, sScopeInfo, _) = await this.InternalEnsureScopeInfoAsync(context, setup, overwrite,
                        runner.Connection, runner.Transaction, runner.Progress, runner.CancellationToken).ConfigureAwait(false);

                    if (sScopeInfo.Setup == null || sScopeInfo.Schema == null)
                        throw new MissingServerScopeTablesException(scopeName);

                    // 2) Provision
                    if (provision == SyncProvision.NotSet)
                        provision = SyncProvision.TrackingTable | SyncProvision.StoredProcedures | SyncProvision.Triggers;

                    (context, sScopeInfo) = await this.InternalProvisionServerAsync(sScopeInfo, context, provision, overwrite,
                        runner.Connection, runner.Transaction, runner.Progress, runner.CancellationToken).ConfigureAwait(false);

                    await runner.CommitAsync().ConfigureAwait(false);

                    return sScopeInfo;
                }
            }
            catch (Exception ex)
            {
                string message = null;

                message += $"Provision:{provision}.";
                message += $"Overwrite:{overwrite}.";

                throw this.GetSyncError(context, ex, message);
            }
        }

        /// <summary>
        /// Provision a server datasource (<strong>triggers</strong>, <strong>stored procedures</strong> (if supported) and <strong>tracking tables</strong> if needed. Create also <strong>scope_info</strong> and <strong>scope_info_client</strong> tables.
        /// <para>
        /// The <paramref name="provision" /> argument specify the objects to provision. See <see cref="SyncProvision" /> enumeration.
        /// </para>
        /// <para>
        /// <example>
        /// <code>
        /// var remoteOrchestrator = new RemoteOrchestrator(serverProvider);
        /// var serverScope = await remoteOrchestrator.GetScopeInfoAsync();
        /// var schema = await remoteOrchestrator.GetSchemaAsync(setup);
        /// serverScope.Schema = schema;
        /// serverScope.Setup = setup;
        /// var sScopeInfo = await localOrchestrator.ProvisionAsync(serverScope);
        /// </code>
        /// </example>
        /// </para>
        /// </summary>
        /// <param name="serverScopeInfo"><see cref="ScopeInfo"/> instance to provision on server side.</param>
        /// <param name="provision">If you do not specify <c>provision</c>, a default value <c>SyncProvision.StoredProcedures | SyncProvision.Triggers | SyncProvision.TrackingTable</c> is used.</param>
        /// <param name="overwrite">If specified, all metadatas are generated and overwritten even if they already exists.</param>
        /// <param name="connection">Optional Connection.</param>
        /// <param name="transaction">Optional Transaction.</param>
        /// <param name="progress">option IProgress{ProgressArgs}.</param>
        /// <param name="cancellationToken">optional cancellation token.</param>
        /// <returns>
        /// A <see cref="ScopeInfo"/> instance, saved locally in the server datasource.
        /// </returns>
        public virtual async Task<ScopeInfo> ProvisionAsync(ScopeInfo serverScopeInfo, SyncProvision provision = default, bool overwrite = false,
            DbConnection connection = null, DbTransaction transaction = null, IProgress<ProgressArgs> progress = null, CancellationToken cancellationToken = default)
        {
            Guard.ThrowIfNull(serverScopeInfo);

            var context = new SyncContext(Guid.NewGuid(), serverScopeInfo.Name);
            try
            {
                (_, serverScopeInfo) = await this.InternalProvisionServerAsync(serverScopeInfo, context, provision, overwrite,
                    connection, transaction, progress, cancellationToken).ConfigureAwait(false);
                return serverScopeInfo;
            }
            catch (Exception ex)
            {
                string message = null;

                message += $"Provision:{provision}.";
                message += $"Overwrite:{overwrite}.";

                throw this.GetSyncError(context, ex, message);
            }
        }

        /// <inheritdoc cref="ProvisionAsync(string, SyncSetup, SyncProvision, bool, DbConnection, DbTransaction, IProgress{ProgressArgs}, CancellationToken)"/>
        public virtual Task<ScopeInfo> ProvisionAsync(SyncProvision provision = default, bool overwrite = false,
            DbConnection connection = null, DbTransaction transaction = null, IProgress<ProgressArgs> progress = null, CancellationToken cancellationToken = default)
            => this.ProvisionAsync(SyncOptions.DefaultScopeName, provision, overwrite, connection, transaction, progress, cancellationToken);

        /// <inheritdoc cref="ProvisionAsync(string, SyncSetup, SyncProvision, bool, DbConnection, DbTransaction, IProgress{ProgressArgs}, CancellationToken)"/>
        public virtual Task<ScopeInfo> ProvisionAsync(string scopeName, SyncProvision provision = default, bool overwrite = false,
            DbConnection connection = null, DbTransaction transaction = null, IProgress<ProgressArgs> progress = null, CancellationToken cancellationToken = default)
        {
            if (provision == SyncProvision.NotSet)
                provision = SyncProvision.ScopeInfo | SyncProvision.ScopeInfoClient | SyncProvision.StoredProcedures | SyncProvision.Triggers | SyncProvision.TrackingTable;

            return this.ProvisionAsync(scopeName, null, provision, overwrite, connection, transaction, progress, cancellationToken);
        }

        /// <inheritdoc cref="ProvisionAsync(string, SyncSetup, SyncProvision, bool, DbConnection, DbTransaction, IProgress{ProgressArgs}, CancellationToken)"/>
        public virtual Task<ScopeInfo> ProvisionAsync(SyncSetup setup, SyncProvision provision = default, bool overwrite = false,
            DbConnection connection = null, DbTransaction transaction = null, IProgress<ProgressArgs> progress = null, CancellationToken cancellationToken = default)
            => this.ProvisionAsync(SyncOptions.DefaultScopeName, setup, provision, overwrite, connection, transaction, progress, cancellationToken);

        /// <summary>
        /// Deprovision your server datasource.
        /// <example>
        /// Deprovision a server database:
        /// <code>
        /// var remoteOrchestrator = new RemoteOrchestrator(serverProvider);
        /// await remoteOrchestrator.DeprovisionAsync();
        /// </code>
        /// </example>
        /// </summary>
        /// <remarks>
        /// By default, <strong>DMS</strong> will never deprovision a table, if not explicitly set with the <c>provision</c> argument. <strong>scope_info</strong> and <strong>scope_info_client</strong> tables
        /// are not deprovisioned by default to preserve existing configurations.
        /// </remarks>
        /// <param name="provision">If you do not specify <c>provision</c>, a default value <c>SyncProvision.StoredProcedures | SyncProvision.Triggers</c> is used.</param>
        /// <param name="connection">Optional Connection.</param>
        /// <param name="transaction">Optional Transaction.</param>
        /// <param name="progress">option IProgress{ProgressArgs}.</param>
        /// <param name="cancellationToken">optional cancellation token.</param>
        public virtual Task<bool> DeprovisionAsync(SyncProvision provision = default, DbConnection connection = null, DbTransaction transaction = null, IProgress<ProgressArgs> progress = null, CancellationToken cancellationToken = default)
            => this.DeprovisionAsync(SyncOptions.DefaultScopeName, provision, connection, transaction, progress, cancellationToken);

        /// <inheritdoc cref="DeprovisionAsync(SyncProvision, DbConnection, DbTransaction,  IProgress{ProgressArgs}, CancellationToken)"/>
        public virtual async Task<bool> DeprovisionAsync(string scopeName, SyncProvision provision = default,
            DbConnection connection = null, DbTransaction transaction = null, IProgress<ProgressArgs> progress = null, CancellationToken cancellationToken = default)
        {
            var context = new SyncContext(Guid.NewGuid(), scopeName);
            try
            {
                if (provision == default)
                    provision = SyncProvision.StoredProcedures | SyncProvision.Triggers;

                using var runner = await this.GetConnectionAsync(context, SyncMode.WithTransaction, SyncStage.Deprovisioning, connection, transaction, progress, cancellationToken).ConfigureAwait(false);
                await using (runner.ConfigureAwait(false))
                {
                    ScopeInfo serverScopeInfo = null;
                    bool exists;
                    (context, exists) = await this.InternalExistsScopeInfoTableAsync(context, DbScopeType.ScopeInfo,
                        runner.Connection, runner.Transaction, runner.Progress, runner.CancellationToken).ConfigureAwait(false);

                    if (exists)
                    {

                        (context, serverScopeInfo) = await this.InternalLoadScopeInfoAsync(
                            context,
                            runner.Connection, runner.Transaction, runner.Progress, runner.CancellationToken).ConfigureAwait(false);
                    }

                    bool isDeprovisioned;
                    (context, isDeprovisioned) = await this.InternalDeprovisionAsync(serverScopeInfo, context, provision,
                        runner.Connection, runner.Transaction, runner.Progress, runner.CancellationToken).ConfigureAwait(false);

                    await runner.CommitAsync().ConfigureAwait(false);

                    return isDeprovisioned;
                }
            }
            catch (Exception ex)
            {
                string message = null;

                message += $"Provision:{provision}.";

                throw this.GetSyncError(context, ex, message);
            }
        }

        /// <inheritdoc cref="DeprovisionAsync(string, SyncSetup, SyncProvision, DbConnection, DbTransaction, IProgress{ProgressArgs}, CancellationToken)"/>
        public virtual Task<bool> DeprovisionAsync(SyncSetup setup, SyncProvision provision = default, DbConnection connection = null, DbTransaction transaction = null, IProgress<ProgressArgs> progress = null, CancellationToken cancellationToken = default)
            => this.DeprovisionAsync(SyncOptions.DefaultScopeName, setup, provision, connection, transaction, progress, cancellationToken);

        /// <summary>
        /// Deprovision your client datasource.
        /// <example>
        /// Deprovision a client database:
        /// <code>
        /// var remoteOrchestrator = new RemoteOrchestrator(serverProvider);
        /// var setup = new SyncSetup("ProductCategory", "Product");
        /// await remoteOrchestrator.DeprovisionAsync(setup);
        /// </code>
        /// </example>
        /// </summary>
        /// <remarks>
        /// By default, <strong>DMS</strong> will never deprovision a table, if not explicitly set with the <c>provision</c> argument. <strong>scope_info</strong> and <strong>scope_info_client</strong> tables
        /// are not deprovisioned by default to preserve existing configurations.
        /// </remarks>
        /// <param name="scopeName">scopeName. If not defined, SyncOptions.DefaultScopeName is used.</param>
        /// <param name="setup">Setup containing tables to deprovision.</param>
        /// <param name="provision">If you do not specify <c>provision</c>, a default value <c>SyncProvision.StoredProcedures | SyncProvision.Triggers</c> is used.</param>
        /// <param name="connection">Optional Connection.</param>
        /// <param name="transaction">Optional Transaction.</param>
        /// <param name="progress">optional IProgress{ProgressArgs}.</param>
        /// <param name="cancellationToken">optional cancellation token.</param>
        public virtual async Task<bool> DeprovisionAsync(string scopeName, SyncSetup setup, SyncProvision provision = default,
            DbConnection connection = null, DbTransaction transaction = null, IProgress<ProgressArgs> progress = null, CancellationToken cancellationToken = default)
        {
            var context = new SyncContext(Guid.NewGuid(), scopeName);
            try
            {
                if (provision == default)
                    provision = SyncProvision.ScopeInfo | SyncProvision.ScopeInfoClient | SyncProvision.StoredProcedures | SyncProvision.Triggers | SyncProvision.TrackingTable;

                using var runner = await this.GetConnectionAsync(context, SyncMode.WithTransaction, SyncStage.Deprovisioning, connection, transaction, progress, cancellationToken).ConfigureAwait(false);
                await using (runner.ConfigureAwait(false))
                {
                    // Creating a fake scope info
                    var serverScopeInfo = InternalCreateScopeInfo(scopeName);
                    serverScopeInfo.Setup = setup;
                    serverScopeInfo.Schema = new SyncSet(setup);

                    bool isDeprovisioned;
                    (context, isDeprovisioned) = await this.InternalDeprovisionAsync(serverScopeInfo, context, provision,
                        runner.Connection, runner.Transaction, runner.Progress, runner.CancellationToken).ConfigureAwait(false);

                    await runner.CommitAsync().ConfigureAwait(false);

                    return isDeprovisioned;
                }
            }
            catch (Exception ex)
            {
                string message = null;

                message += $"Provision:{provision}.";

                throw this.GetSyncError(context, ex, message);
            }
        }

        /// <summary>
        /// Drop everything related to DMS. Tracking tables, triggers, tracking tables, sync_scope and sync_scope_client tables.
        /// <example>
        /// Deprovision a server database:
        /// <code>
        /// var remoteOrchestrator = new RemoteOrchestrator(serverProvider);
        /// await remoteOrchestrator.DropAllAsync();
        /// </code>
        /// </example>
        /// </summary>
        public virtual async Task DropAllAsync(DbConnection connection = null, DbTransaction transaction = null, IProgress<ProgressArgs> progress = null, CancellationToken cancellationToken = default)
        {
            var context = new SyncContext(Guid.NewGuid(), SyncOptions.DefaultScopeName);
            try
            {
                using var runner = await this.GetConnectionAsync(context, SyncMode.WithTransaction, SyncStage.Deprovisioning, connection, transaction, progress, cancellationToken).ConfigureAwait(false);
                await using (runner.ConfigureAwait(false))
                {
                    List<ScopeInfo> serverScopeInfos = null;
                    bool exists;
                    (context, exists) = await this.InternalExistsScopeInfoTableAsync(context, DbScopeType.ScopeInfo,
                        runner.Connection, runner.Transaction, runner.Progress, runner.CancellationToken).ConfigureAwait(false);

                    if (exists)
                    {

                        (context, serverScopeInfos) = await this.InternalLoadAllScopeInfosAsync(
                            context,
                            runner.Connection, runner.Transaction, runner.Progress, runner.CancellationToken).ConfigureAwait(false);
                    }

                    // fallback to "try to drop an hypothetical default scope"
                    serverScopeInfos ??= [];

                    var existingFilters = serverScopeInfos?.SelectMany(si => si.Setup == null ? [] : si.Setup.Filters).ToList();

                    var defaultServerScopeInfo = InternalCreateScopeInfo(SyncOptions.DefaultScopeName);

                    SyncSetup setup;
                    (context, setup) = await this.InternalGetAllTablesAsync(
                        context,
                        runner.Connection, runner.Transaction, runner.Progress, runner.CancellationToken).ConfigureAwait(false);

                    var scopeBuilder = this.GetScopeBuilder(this.Options.ScopeInfoTableName);
                    var scopeTableNames = scopeBuilder.GetParsedScopeInfoTableNames();
                    var scopeInfoTableName = scopeTableNames.NormalizedName;
                    var scopeInfoClientTableName = $"{scopeTableNames.NormalizedName}_client";

                    // Considering removing tables with "_tracking" at the end
                    var tables = setup.Tables.Where(setupTable => !setupTable.TableName.EndsWith("_tracking", SyncGlobalization.DataSourceStringComparison) && setupTable.TableName != scopeInfoTableName && setupTable.TableName != scopeInfoClientTableName).ToList();
                    setup.Tables.Clear();
                    setup.Tables.AddRange(tables);
                    defaultServerScopeInfo.Setup = setup;

                    if (defaultServerScopeInfo.Setup != null && defaultServerScopeInfo.Setup.Tables.Count > 0)
                    {
                        var (_, defaultSchema) = await this.InternalGetSchemaAsync(context, defaultServerScopeInfo.Setup,
                        runner.Connection, runner.Transaction, runner.Progress, runner.CancellationToken).ConfigureAwait(false);

                        defaultServerScopeInfo.Schema = defaultSchema;

                        // add any random filters, to try to delete them
                        if (existingFilters != null && existingFilters.Count > 0)
                        {
                            var filters = new SetupFilters();
                            foreach (var filter in existingFilters)
                                filters.Add(filter);

                            defaultServerScopeInfo.Setup.Filters = filters;
                        }

                        serverScopeInfos.Add(defaultServerScopeInfo);
                    }

                    var provision = SyncProvision.StoredProcedures | SyncProvision.Triggers | SyncProvision.TrackingTable | SyncProvision.ScopeInfo | SyncProvision.ScopeInfoClient;

                    foreach (var serverScopeInfo in serverScopeInfos)
                    {
                        if (serverScopeInfo == null || serverScopeInfo.Setup == null || serverScopeInfo.Setup.Tables == null || serverScopeInfo.Setup.Tables.Count <= 0)
                            continue;

                        (context, _) = await this.InternalDeprovisionAsync(serverScopeInfo, context, provision,
                            runner.Connection, runner.Transaction, runner.Progress, runner.CancellationToken).ConfigureAwait(false);
                    }

                    await runner.CommitAsync().ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                throw this.GetSyncError(context, ex);
            }
        }

        /// <summary>
        /// Check if the server datasource should be provisioned.
        /// </summary>
        internal virtual async Task<(SyncContext Context, ScopeInfo ServerScopeInfo)> InternalProvisionServerAsync(ScopeInfo sScopeInfo, SyncContext context,
                                SyncProvision provision, bool overwrite,
                                DbConnection connection, DbTransaction transaction, IProgress<ProgressArgs> progress, CancellationToken cancellationToken)
        {
            try
            {
                Guard.ThrowIfNull(sScopeInfo);
                Guard.ThrowIfNull(sScopeInfo.Setup, $"No Setup in your server scopeInfo {sScopeInfo.Name}");
                Guard.ThrowIfNull(sScopeInfo.Schema, $"No Schema in your server scopeInfo {sScopeInfo.Name}");

                using var runner = await this.GetConnectionAsync(context, SyncMode.WithTransaction, SyncStage.Provisioning, connection, transaction, progress, cancellationToken).ConfigureAwait(false);
                await using (runner.ConfigureAwait(false))
                {
                    if (provision == SyncProvision.NotSet)
                        provision = SyncProvision.TrackingTable | SyncProvision.StoredProcedures | SyncProvision.Triggers;

                    (context, _) = await this.InternalProvisionAsync(sScopeInfo, context, overwrite, provision, runner.Connection, runner.Transaction, runner.Progress, runner.CancellationToken).ConfigureAwait(false);

                    // Write scopes locally
                    (context, sScopeInfo) = await this.InternalSaveScopeInfoAsync(sScopeInfo, context, runner.Connection, runner.Transaction, runner.Progress, runner.CancellationToken).ConfigureAwait(false);

                    await runner.CommitAsync().ConfigureAwait(false);

                    return (context, sScopeInfo);
                }
            }
            catch (Exception ex)
            {
                string message = null;

                message += $"Provision:{provision}.";
                message += $"Overwrite:{overwrite}.";

                throw this.GetSyncError(context, ex, message);
            }
        }

        /// <summary>
        /// Check if the server datasource should be provisioned.
        /// </summary>
        internal virtual async Task<bool> InternalShouldProvisionServerAsync(ScopeInfo sScopeInfo, SyncContext context,
                                DbConnection connection, DbTransaction transaction, IProgress<ProgressArgs> progress, CancellationToken cancellationToken)
        {
            try
            {
                using var runner = await this.GetConnectionAsync(context, SyncMode.NoTransaction, SyncStage.Provisioning, connection, transaction, progress, cancellationToken).ConfigureAwait(false);
                await using (runner.ConfigureAwait(false))
                {
                    var scopeInfoClients = await this.InternalLoadAllScopeInfoClientsAsync(context, runner.Connection, runner.Transaction, runner.Progress, runner.CancellationToken).ConfigureAwait(false);

                    if (scopeInfoClients == null || scopeInfoClients.Count <= 0)
                        return true;

                    var scopeInfoClientsForThisScopeNameAlreadyExists = scopeInfoClients.Any(sic => sic.Name == sScopeInfo.Name);

                    return !scopeInfoClientsForThisScopeNameAlreadyExists;
                }
            }
            catch (Exception ex)
            {
                throw this.GetSyncError(context, ex);
            }
        }

        /// <summary>
        /// Migrate the server schema by applying an additive schema change.
        /// This method:
        /// 1. Loads the current ScopeInfo from server DB
        /// 2. Snapshots the current schema into scope_info_schema_history (preserves the "before" state)
        /// 3. Compares with the new SyncSetup to compute migration results
        /// 4. Validates that changes are additive only (new nullable/default columns, new tables)
        /// 5. Reprovisions affected tables (drop/recreate triggers, SPs, TVPs)
        /// 6. Adds migration name to ScopeInfo.Migrations
        /// 7. Updates ScopeInfo with new schema and hash
        /// 8. Snapshots the new schema into scope_info_schema_history
        /// </summary>
        /// <param name="migrationName">A unique, sortable name for this migration (e.g., "20260217_titlecolumns").</param>
        /// <param name="newSetup">The new SyncSetup with the updated table/column definitions.</param>
        /// <param name="scopeName">The scope name to migrate (default scope if not specified).</param>
        /// <param name="connection">Optional Connection.</param>
        /// <param name="transaction">Optional Transaction.</param>
        /// <param name="progress">Optional progress.</param>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        /// <returns>The updated ScopeInfo with the migration applied.</returns>
        public virtual async Task<ScopeInfo> MigrateSchemaAsync(string migrationName, SyncSetup newSetup,
            string scopeName = null, DbConnection connection = null, DbTransaction transaction = null,
            IProgress<ProgressArgs> progress = null, CancellationToken cancellationToken = default)
        {
            Guard.ThrowIfNull(migrationName);
            Guard.ThrowIfNull(newSetup);

            scopeName ??= SyncOptions.DefaultScopeName;
            var context = new SyncContext(Guid.NewGuid(), scopeName);

            try
            {
                using var runner = await this.GetConnectionAsync(context, SyncMode.WithTransaction, SyncStage.Provisioning, connection, transaction, progress, cancellationToken).ConfigureAwait(false);
                await using (runner.ConfigureAwait(false))
                {
                    // 1. Load current ScopeInfo
                    ScopeInfo currentScopeInfo;
                    (context, currentScopeInfo) = await this.InternalLoadScopeInfoAsync(context, runner.Connection, runner.Transaction, runner.Progress, runner.CancellationToken).ConfigureAwait(false);

                    if (currentScopeInfo == null || currentScopeInfo.Setup == null || currentScopeInfo.Schema == null)
                        throw new Exception($"Cannot migrate schema: server scope '{scopeName}' has not been provisioned yet. Call ProvisionAsync first.");

                    // 2. Snapshot current schema before making changes
                    var currentSchemaJson = Serializer.Serialize(currentScopeInfo.Schema).ToUtf8String();
                    var currentSetupJson = Serializer.Serialize(currentScopeInfo.Setup).ToUtf8String();
                    var currentHash = currentScopeInfo.SchemaHash;

                    // Save the "before" snapshot to the schema history table
                    var beforeMigrationName = $"_before_{migrationName}";
                    await this.InternalSaveSchemaHistoryAsync(context, beforeMigrationName, scopeName, currentHash, currentSchemaJson, currentSetupJson,
                        runner.Connection, runner.Transaction, runner.CancellationToken).ConfigureAwait(false);

                    // 3. Create a temporary scope with new setup to compute migration diff
                    var newScopeInfo = new ScopeInfo
                    {
                        Name = scopeName,
                        Setup = newSetup,
                    };

                    // Get schema for the new setup from the database
                    (context, newScopeInfo, _) = await this.InternalEnsureScopeInfoAsync(context, newSetup, true,
                        runner.Connection, runner.Transaction, runner.Progress, runner.CancellationToken).ConfigureAwait(false);

                    // 4. Compute migration diff and validate additive only
                    var migration = new Migration(currentScopeInfo, newScopeInfo);
                    var results = migration.Compare();

                    if (results.HasChanges && !results.IsAdditiveOnly())
                        throw new Exception($"Migration '{migrationName}' contains non-additive changes (removed/modified columns). " +
                                           "Only additive changes (new nullable/default columns, new tables) are supported.");

                    // 5. Reprovision affected tables (overwrite = true to recreate SPs/triggers/TVPs)
                    var provision = SyncProvision.StoredProcedures | SyncProvision.Triggers | SyncProvision.TrackingTable;
                    (context, _) = await this.InternalProvisionAsync(newScopeInfo, context, true, provision,
                        runner.Connection, runner.Transaction, runner.Progress, runner.CancellationToken).ConfigureAwait(false);

                    // 6. Add migration name to ScopeInfo.Migrations
                    newScopeInfo.AddMigration(migrationName);

                    // 7. Update ScopeInfo with new schema and hash
                    newScopeInfo.UpdateSchemaHash(newScopeInfo.Schema);

                    (context, newScopeInfo) = await this.InternalSaveScopeInfoAsync(newScopeInfo, context,
                        runner.Connection, runner.Transaction, runner.Progress, runner.CancellationToken).ConfigureAwait(false);

                    // 8. Snapshot the new schema into schema history
                    var newSchemaJson = Serializer.Serialize(newScopeInfo.Schema).ToUtf8String();
                    var newSetupJson = Serializer.Serialize(newScopeInfo.Setup).ToUtf8String();
                    await this.InternalSaveSchemaHistoryAsync(context, migrationName, scopeName, newScopeInfo.SchemaHash, newSchemaJson, newSetupJson,
                        runner.Connection, runner.Transaction, runner.CancellationToken).ConfigureAwait(false);

                    await runner.CommitAsync().ConfigureAwait(false);

                    return newScopeInfo;
                }
            }
            catch (Exception ex)
            {
                throw this.GetSyncError(context, ex, $"MigrationName:{migrationName}");
            }
        }

        /// <summary>
        /// Save a schema snapshot to the scope_info_schema_history table.
        /// Creates the table if it doesn't exist.
        /// </summary>
        internal async Task InternalSaveSchemaHistoryAsync(SyncContext context, string migrationName, string scopeName,
            string schemaHash, string schemaJson, string setupJson,
            DbConnection connection, DbTransaction transaction, CancellationToken cancellationToken)
        {
            // Create the schema history table if needed
            var createTableCommand = connection.CreateCommand();
            createTableCommand.Transaction = transaction;

            // Detect provider type from connection
            var isPostgres = connection.GetType().Name.Contains("Npgsql", StringComparison.OrdinalIgnoreCase);
            var isMySql = connection.GetType().Name.Contains("MySql", StringComparison.OrdinalIgnoreCase);
            var isSqlite = connection.GetType().Name.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);

            if (isSqlite)
            {
                createTableCommand.CommandText = @"
                    CREATE TABLE IF NOT EXISTS [scope_info_schema_history] (
                        [migration_name] TEXT NOT NULL,
                        [scope_name] TEXT NOT NULL,
                        [schema_hash] TEXT NULL,
                        [schema_json] TEXT NULL,
                        [setup_json] TEXT NULL,
                        [created_at] DATETIME NOT NULL DEFAULT (datetime('now')),
                        PRIMARY KEY ([migration_name], [scope_name])
                    )";
            }
            else
            {
                // SQL Server (default)
                createTableCommand.CommandText = @"
                    IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'scope_info_schema_history')
                    CREATE TABLE [scope_info_schema_history] (
                        [migration_name] NVARCHAR(200) NOT NULL,
                        [scope_name] NVARCHAR(100) NOT NULL,
                        [schema_hash] NVARCHAR(64) NULL,
                        [schema_json] NVARCHAR(MAX) NULL,
                        [setup_json] NVARCHAR(MAX) NULL,
                        [created_at] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                        CONSTRAINT [PKey_scope_info_schema_history] PRIMARY KEY ([migration_name], [scope_name])
                    )";
            }

            await createTableCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            // Insert the schema snapshot
            var insertCommand = connection.CreateCommand();
            insertCommand.Transaction = transaction;

            if (isSqlite)
            {
                insertCommand.CommandText = @"
                    INSERT OR REPLACE INTO [scope_info_schema_history]
                    ([migration_name], [scope_name], [schema_hash], [schema_json], [setup_json])
                    VALUES (@migration_name, @scope_name, @schema_hash, @schema_json, @setup_json)";
            }
            else
            {
                insertCommand.CommandText = @"
                    MERGE [scope_info_schema_history] AS [target]
                    USING (SELECT @migration_name, @scope_name) AS [source] ([migration_name], [scope_name])
                    ON [target].[migration_name] = [source].[migration_name] AND [target].[scope_name] = [source].[scope_name]
                    WHEN NOT MATCHED THEN
                        INSERT ([migration_name], [scope_name], [schema_hash], [schema_json], [setup_json])
                        VALUES (@migration_name, @scope_name, @schema_hash, @schema_json, @setup_json)
                    WHEN MATCHED THEN
                        UPDATE SET [schema_hash] = @schema_hash, [schema_json] = @schema_json, [setup_json] = @setup_json;";
            }

            var p1 = insertCommand.CreateParameter();
            p1.ParameterName = "@migration_name";
            p1.Value = migrationName;
            insertCommand.Parameters.Add(p1);

            var p2 = insertCommand.CreateParameter();
            p2.ParameterName = "@scope_name";
            p2.Value = scopeName;
            insertCommand.Parameters.Add(p2);

            var p3 = insertCommand.CreateParameter();
            p3.ParameterName = "@schema_hash";
            p3.Value = (object)schemaHash ?? DBNull.Value;
            insertCommand.Parameters.Add(p3);

            var p4 = insertCommand.CreateParameter();
            p4.ParameterName = "@schema_json";
            p4.Value = (object)schemaJson ?? DBNull.Value;
            insertCommand.Parameters.Add(p4);

            var p5 = insertCommand.CreateParameter();
            p5.ParameterName = "@setup_json";
            p5.Value = (object)setupJson ?? DBNull.Value;
            insertCommand.Parameters.Add(p5);

            await insertCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}