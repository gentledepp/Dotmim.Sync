using Wormhole.Sync.Builders;
using Wormhole.Sync.Enumerations;
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
    /// Contains internal provisioning methods.
    /// </summary>
    public abstract partial class BaseOrchestrator
    {
        /// <summary>
        /// Internal provision method.
        /// </summary>
        internal virtual async Task<(SyncContext Context, bool Provisioned)> InternalProvisionAsync(ScopeInfo scopeInfo, SyncContext context, bool overwrite, SyncProvision provision,
            DbConnection connection, DbTransaction transaction, IProgress<ProgressArgs> progress, CancellationToken cancellationToken)
        {
            if (this.Provider == null)
                throw new MissingProviderException(nameof(this.InternalProvisionAsync));

            context.SyncStage = SyncStage.Provisioning;

            // If schema does not have any table, raise an exception
            if (scopeInfo.Schema == null || scopeInfo.Schema.Tables == null || !scopeInfo.Schema.HasTables)
                throw new MissingTablesException();

            await this.InterceptAsync(new ProvisioningArgs(context, provision, scopeInfo, connection, transaction), progress, cancellationToken).ConfigureAwait(false);

            try
            {
                // get Database builder
                var builder = this.Provider.GetDatabaseBuilder();
                await builder.EnsureDatabaseAsync(connection, transaction).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw this.GetSyncError(context, ex, "Error during EnsureDatabaseAsync");
            }

            try
            {
                // Check if we have at least something created
                var atLeastOneStoredProcedureHasBeenCreated = false;
                var atLeastOneTriggerHasBeenCreated = false;
                var atLeastOneTrackingTableBeenCreated = false;
                var atLeastOneTableBeenCreated = false;
                var atLeastOneSchemaTableBeenCreated = false;
                var atLeastOneScopeInfoTableBeenCreated = false;
                var atLeastOneScopeInfoClientTableBeenCreated = false;

                // Check if we have tables AND columns
                // If we don't have any columns it's most probably because user called method with the Setup only
                // So far we have only tables names, it's enough to get the schema
                if (scopeInfo.Schema.HasTables && !scopeInfo.Schema.HasColumns)
                    (context, scopeInfo.Schema) = await this.InternalGetSchemaAsync(context, scopeInfo.Setup, connection, transaction, progress, cancellationToken).ConfigureAwait(false);

                // Shoudl we create scope
                if (provision.HasFlag(SyncProvision.ScopeInfo))
                {
                    bool exists;
                    (context, exists) = await this.InternalExistsScopeInfoTableAsync(context, DbScopeType.ScopeInfo, connection, transaction, progress, cancellationToken).ConfigureAwait(false);

                    if (!exists)
                    {
                        var siCreated = false;
                        (context, siCreated) = await this.InternalCreateScopeInfoTableAsync(context, DbScopeType.ScopeInfo, connection, transaction, progress, cancellationToken).ConfigureAwait(false);

                        if (siCreated && !atLeastOneScopeInfoTableBeenCreated)
                            atLeastOneScopeInfoTableBeenCreated = true;
                    }
                }

                if (provision.HasFlag(SyncProvision.ScopeInfoClient))
                {
                    bool exists;
                    (context, exists) = await this.InternalExistsScopeInfoTableAsync(context, DbScopeType.ScopeInfoClient, connection, transaction, progress, cancellationToken).ConfigureAwait(false);

                    if (!exists)
                    {
                        var sicCreated = false;
                        (context, sicCreated) = await this.InternalCreateScopeInfoTableAsync(context, DbScopeType.ScopeInfoClient, connection, transaction, progress, cancellationToken).ConfigureAwait(false);

                        if (sicCreated && !atLeastOneScopeInfoClientTableBeenCreated)
                            atLeastOneScopeInfoClientTableBeenCreated = true;
                    }
                }

                // Sorting tables based on dependencies between them
                var schemaTables = scopeInfo.Schema.Tables
                    .SortByDependencies(tab => tab.GetRelations()
                        .Select(r => r.GetParentTable()));

                foreach (var schemaTable in schemaTables)
                {
                    var tableBuilder = this.GetSyncAdapter(schemaTable, scopeInfo).GetTableBuilder();

                    await this.InterceptAsync(new ProvisioningTableArgs(context, provision, scopeInfo, schemaTable, connection, transaction), progress, cancellationToken).ConfigureAwait(false);

                    // Check if we need to create a schema there
                    bool schemaExists;
                    (context, schemaExists) = await this.InternalExistsSchemaAsync(scopeInfo, context, tableBuilder, connection, transaction, progress, cancellationToken).ConfigureAwait(false);

                    var stCreated = false;
                    var tCreated = false;
                    var trackingTableExist = false;
                    var tgCreated = false;
                    var spCreated = false;

                    if (!schemaExists)
                    {
                        (context, stCreated) = await this.InternalCreateSchemaAsync(scopeInfo, context, tableBuilder, connection, transaction, progress, cancellationToken).ConfigureAwait(false);

                        if (stCreated && !atLeastOneSchemaTableBeenCreated)
                            atLeastOneSchemaTableBeenCreated = true;
                    }

                    if (provision.HasFlag(SyncProvision.Table))
                    {
                        bool tableExists;
                        (context, tableExists) = await this.InternalExistsTableAsync(scopeInfo, context, tableBuilder, connection, transaction, progress, cancellationToken).ConfigureAwait(false);

                        if (!tableExists)
                        {
                            (context, tCreated) = await this.InternalCreateTableAsync(scopeInfo, context, tableBuilder, connection, transaction, progress, cancellationToken).ConfigureAwait(false);

                            if (tCreated && !atLeastOneTableBeenCreated)
                                atLeastOneTableBeenCreated = true;
                        }
                    }

                    if (provision.HasFlag(SyncProvision.TrackingTable))
                    {
                        (context, trackingTableExist) = await this.InternalExistsTrackingTableAsync(scopeInfo, context, tableBuilder, connection, transaction, progress, cancellationToken).ConfigureAwait(false);

                        if (!trackingTableExist)
                        {
                            var ttCreated = false;
                            (context, ttCreated) = await this.InternalCreateTrackingTableAsync(scopeInfo, context, tableBuilder, connection, transaction, progress, cancellationToken).ConfigureAwait(false);

                            if (ttCreated && !atLeastOneTrackingTableBeenCreated)
                                atLeastOneTrackingTableBeenCreated = true;
                        }
                    }

                    if (provision.HasFlag(SyncProvision.Triggers))
                    {
                        (context, tgCreated) = await this.InternalCreateTriggersAsync(scopeInfo, context, overwrite, tableBuilder, connection, transaction, progress, cancellationToken).ConfigureAwait(false);

                        if (tgCreated && !atLeastOneTriggerHasBeenCreated)
                            atLeastOneTriggerHasBeenCreated = true;
                    }

                    if (provision.HasFlag(SyncProvision.StoredProcedures))
                    {
                        (context, spCreated) = await this.InternalCreateStoredProceduresAsync(scopeInfo, context, overwrite, tableBuilder, connection, transaction, progress, cancellationToken).ConfigureAwait(false);

                        if (spCreated && !atLeastOneStoredProcedureHasBeenCreated)
                            atLeastOneStoredProcedureHasBeenCreated = true;
                    }

                    // Execute custom provisioning SQL if configured
                    var setupTable = scopeInfo.Setup?.Tables[schemaTable.TableName, schemaTable.SchemaName];
                    if (setupTable?.CustomProvisioningSql != null && setupTable.CustomProvisioningSql.Count > 0)
                    {
                        foreach (var customSql in setupTable.CustomProvisioningSql)
                        {
                            if (!string.IsNullOrWhiteSpace(customSql))
                            {
                                using var cmd = connection.CreateCommand();
                                cmd.Connection = connection;
                                cmd.Transaction = transaction;
                                cmd.CommandText = customSql;

                                await this.InterceptAsync(new ExecuteCommandArgs(context, cmd, default, connection, transaction), progress, cancellationToken).ConfigureAwait(false);
                                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                            }
                        }
                    }

                    // Check if we have created something on the current table.
                    var atLeastSomethingHasBeenCreatedOnThisTable = stCreated || tCreated || trackingTableExist || tgCreated || spCreated;

                    await this.InterceptAsync(new ProvisionedTableArgs(context, provision, scopeInfo, schemaTable, atLeastSomethingHasBeenCreatedOnThisTable, connection, transaction), progress, cancellationToken).ConfigureAwait(false);
                }

                // Check if we have created something.
                var atLeastSomethingHasBeenCreated = atLeastOneSchemaTableBeenCreated || atLeastOneTableBeenCreated || atLeastOneTrackingTableBeenCreated || atLeastOneTriggerHasBeenCreated
                                                  || atLeastOneStoredProcedureHasBeenCreated || atLeastOneScopeInfoTableBeenCreated || atLeastOneScopeInfoClientTableBeenCreated;

                await this.InterceptAsync(new ProvisionedArgs(context, provision, scopeInfo, atLeastSomethingHasBeenCreated, connection, transaction), progress, cancellationToken).ConfigureAwait(false);

                return (context, true);
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
        /// Internal method to deprovision a scope.
        /// </summary>
        internal virtual async Task<(SyncContext Context, bool IsDeprovisioned)> InternalDeprovisionAsync(ScopeInfo scopeInfo, SyncContext context, SyncProvision provision, DbConnection connection, DbTransaction transaction, IProgress<ProgressArgs> progress, CancellationToken cancellationToken)
        {
            try
            {
                if (this.Provider == null)
                    throw new MissingProviderException(nameof(this.InternalDeprovisionAsync));

                context.SyncStage = SyncStage.Deprovisioning;

                using var runner = await this.GetConnectionAsync(context, SyncMode.WithTransaction, SyncStage.Deprovisioning, connection, transaction, progress, cancellationToken).ConfigureAwait(false);
                await using (runner.ConfigureAwait(false))
                {
                    await this.InterceptAsync(new DeprovisioningArgs(context, provision, scopeInfo?.Setup, runner.Connection, runner.Transaction), runner.Progress, runner.CancellationToken).ConfigureAwait(false);

                    // get Database builder
                    var builder = this.Provider.GetDatabaseBuilder();

                    // Sorting tables based on dependencies between them
                    List<SyncTable> schemaTables;
                    if (scopeInfo == null)
                    {
                        schemaTables = [];
                    }
                    else
                    {
                        if (scopeInfo.Schema != null)
                        {
                            schemaTables = scopeInfo.Schema.Tables.SortByDependencies(tab => tab.GetRelations().Select(r => r.GetParentTable())).ToList();
                        }
                        else
                        {
                            schemaTables = [];
                            foreach (var setupTable in scopeInfo.Setup.Tables)
                                schemaTables.Add(new SyncTable(setupTable.TableName, setupTable.SchemaName));
                        }
                    }

                    // Disable check constraints
                    if (this.Options.DisableConstraintsOnApplyChanges)
                    {
                        foreach (var table in schemaTables.ToArray().Reverse())
                        {
                            var exists = false;
                            var tableBuilder = this.GetSyncAdapter(table, scopeInfo).GetTableBuilder();

                            (context, exists) = await this.InternalExistsTableAsync(scopeInfo, context, tableBuilder, runner.Connection, runner.Transaction, runner.Progress, runner.CancellationToken).ConfigureAwait(false);
                            if (exists)
                                await this.InternalDisableConstraintsAsync(scopeInfo, context, table, runner.Connection, runner.Transaction, runner.Progress, runner.CancellationToken).ConfigureAwait(false);
                        }
                    }

                    // Checking if we have to deprovision tables
                    var hasDeprovisionTableFlag = provision.HasFlag(SyncProvision.Table);

                    // Firstly, removing the flag from the provision, because we need to drop everything in correct order, then drop tables in reverse side
                    if (hasDeprovisionTableFlag)
                        provision ^= SyncProvision.Table;

                    // Check if we have at least dropped something
                    var atLeastOneStoredProcedureHasBeenDropped = false;
                    var atLeastOneTriggerHasBeenDropped = false;
                    var atLeastOneTrackingTableBeenDropped = false;
                    var atLeastOneTableBeenDropped = false;
                    var atLeastScopeInfoTableBeenDropped = false;
                    var atLeastScopeInfoClientTableBeenDropped = false;

                    foreach (var schemaTable in schemaTables)
                    {
                        var tableBuilder = this.GetSyncAdapter(schemaTable, scopeInfo).GetTableBuilder();

                        await this.InterceptAsync(new DeprovisioningTableArgs(context, provision, scopeInfo, schemaTable, connection, transaction), progress, cancellationToken).ConfigureAwait(false);

                        var spDropped = false;
                        var tgDropped = false;
                        var ttDropped = false;

                        if (provision.HasFlag(SyncProvision.StoredProcedures))
                        {
                            (context, spDropped) = await this.InternalDropStoredProceduresAsync(scopeInfo, context, tableBuilder, runner.Connection, runner.Transaction, progress, cancellationToken).ConfigureAwait(false);

                            // Removing cached commands
                            BaseOrchestrator.RemoveCommands();

                            if (spDropped && !atLeastOneStoredProcedureHasBeenDropped)
                                atLeastOneStoredProcedureHasBeenDropped = true;
                        }

                        if (provision.HasFlag(SyncProvision.Triggers))
                        {
                            (context, tgDropped) = await this.InternalDropTriggersAsync(scopeInfo, context, tableBuilder, runner.Connection, runner.Transaction, progress, cancellationToken).ConfigureAwait(false);

                            if (tgDropped && !atLeastOneTriggerHasBeenDropped)
                                atLeastOneTriggerHasBeenDropped = true;
                        }

                        if (provision.HasFlag(SyncProvision.TrackingTable))
                        {
                            bool exists;
                            (context, exists) = await this.InternalExistsTrackingTableAsync(scopeInfo, context, tableBuilder, runner.Connection, runner.Transaction, progress, cancellationToken).ConfigureAwait(false);

                            if (exists)
                            {
                                (context, ttDropped) = await this.InternalDropTrackingTableAsync(scopeInfo, context, tableBuilder, runner.Connection, runner.Transaction, progress, cancellationToken).ConfigureAwait(false);

                                if (ttDropped && !atLeastOneTrackingTableBeenDropped)
                                    atLeastOneTrackingTableBeenDropped = true;
                            }
                        }

                        var atLeastSomethingHasBeenDeprovisioned = spDropped || tgDropped || ttDropped;

                        await this.InterceptAsync(new DeprovisionedTableArgs(context, provision, scopeInfo, schemaTable, atLeastSomethingHasBeenDeprovisioned, connection, transaction), progress, cancellationToken).ConfigureAwait(false);
                    }

                    // Eventually if we have the "Table" flag, then drop the table
                    if (hasDeprovisionTableFlag)
                    {
                        foreach (var schemaTable in schemaTables.ToArray().Reverse())
                        {
                            var tableBuilder = this.GetSyncAdapter(schemaTable, scopeInfo).GetTableBuilder();
                            bool exists;
                            (context, exists) = await this.InternalExistsTableAsync(scopeInfo, context, tableBuilder, runner.Connection, runner.Transaction, progress, cancellationToken).ConfigureAwait(false);

                            if (exists)
                            {
                                var tDropped = false;
                                (context, tDropped) = await this.InternalDropTableAsync(scopeInfo, context, tableBuilder, runner.Connection, runner.Transaction, progress, cancellationToken).ConfigureAwait(false);

                                if (tDropped && !atLeastOneTableBeenDropped)
                                    atLeastOneTableBeenDropped = true;
                            }
                        }
                    }

                    if (provision.HasFlag(SyncProvision.ScopeInfo))
                    {
                        bool exists;
                        (context, exists) = await this.InternalExistsScopeInfoTableAsync(context, DbScopeType.ScopeInfo, runner.Connection, runner.Transaction, progress, cancellationToken).ConfigureAwait(false);

                        if (exists)
                        {
                            var siDropped = false;
                            (context, siDropped) = await this.InternalDropScopeInfoTableAsync(context, DbScopeType.ScopeInfo, runner.Connection, runner.Transaction, progress, cancellationToken).ConfigureAwait(false);

                            if (siDropped && !atLeastScopeInfoTableBeenDropped)
                                atLeastScopeInfoTableBeenDropped = true;
                        }
                    }

                    if (provision.HasFlag(SyncProvision.ScopeInfoClient))
                    {
                        bool exists;
                        (context, exists) = await this.InternalExistsScopeInfoTableAsync(context, DbScopeType.ScopeInfoClient, runner.Connection, runner.Transaction, progress, cancellationToken).ConfigureAwait(false);

                        if (exists)
                        {
                            var sicDropped = false;
                            (context, sicDropped) = await this.InternalDropScopeInfoTableAsync(context, DbScopeType.ScopeInfoClient, runner.Connection, runner.Transaction, progress, cancellationToken).ConfigureAwait(false);

                            if (sicDropped && !atLeastScopeInfoClientTableBeenDropped)
                                atLeastScopeInfoClientTableBeenDropped = true;
                        }
                    }

                    // Disable check constraints
                    if (this.Options.DisableConstraintsOnApplyChanges && !hasDeprovisionTableFlag)
                    {
                        foreach (var table in schemaTables.ToArray().Reverse())
                        {
                            var exists = false;
                            var tableBuilder = this.GetSyncAdapter(table, scopeInfo).GetTableBuilder();

                            (context, exists) = await this.InternalExistsTableAsync(scopeInfo, context, tableBuilder, runner.Connection, runner.Transaction, runner.Progress, runner.CancellationToken).ConfigureAwait(false);
                            if (exists)
                                await this.InternalEnableConstraintsAsync(scopeInfo, context, table, runner.Connection, runner.Transaction, runner.Progress, runner.CancellationToken).ConfigureAwait(false);
                        }
                    }

                    var atLeastSomethingHasBeenDropped = atLeastScopeInfoTableBeenDropped || atLeastScopeInfoClientTableBeenDropped || atLeastOneTableBeenDropped || atLeastOneTrackingTableBeenDropped
                                                      || atLeastOneTriggerHasBeenDropped || atLeastOneStoredProcedureHasBeenDropped;

                    var args = new DeprovisionedArgs(context, provision, scopeInfo?.Setup, atLeastSomethingHasBeenDropped, runner.Connection, runner.Transaction);
                    await this.InterceptAsync(args, progress, cancellationToken).ConfigureAwait(false);

                    await runner.CommitAsync().ConfigureAwait(false);

                    return (context, true);
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
        /// Internal method to get tracking table provisioning SQL script with interceptor support.
        /// </summary>
        internal async Task<string> InternalGetTrackingTableProvisioningSqlAsync(
            SyncContext context, ScopeInfo scopeInfo, SyncTable schemaTable, SetupTable setupTable,
            DbTableBuilder tableBuilder, DbConnection connection, DbTransaction transaction,
            IProgress<ProgressArgs> progress, CancellationToken cancellationToken)
        {
            var trackingTableCmd = await tableBuilder.GetCreateTrackingTableCommandAsync(connection, transaction).ConfigureAwait(false);

            if (trackingTableCmd == null || string.IsNullOrEmpty(trackingTableCmd.CommandText))
                return null;

            var trackingTableNames = tableBuilder.GetParsedTrackingTableNames();

            // Fire TrackingTableCreatingArgs interceptor
            var args = new TrackingTableCreatingArgs(context, scopeInfo, schemaTable, trackingTableNames.QuotedFullName, trackingTableCmd, connection, transaction);

            // Invoke setup-level interceptor if configured
            setupTable?.TrackingTableInterceptor?.Invoke(args);

            // Invoke global interceptor
            await this.InterceptAsync(args, progress, cancellationToken).ConfigureAwait(false);

            if (args.Cancel || args.Command == null)
                return null;

            return args.Command.CommandText;
        }

        /// <summary>
        /// Internal method to get trigger provisioning SQL script with interceptor support.
        /// </summary>
        internal async Task<string> InternalGetTriggerProvisioningSqlAsync(
            SyncContext context, ScopeInfo scopeInfo, SyncTable schemaTable, SetupTable setupTable,
            DbTableBuilder tableBuilder, DbTriggerType triggerType,
            DbConnection connection, DbTransaction transaction,
            IProgress<ProgressArgs> progress, CancellationToken cancellationToken)
        {
            var triggerCmd = await tableBuilder.GetCreateTriggerCommandAsync(triggerType, connection, transaction).ConfigureAwait(false);

            if (triggerCmd == null || string.IsNullOrEmpty(triggerCmd.CommandText))
                return null;

            // Fire TriggerCreatingArgs interceptor
            var args = new TriggerCreatingArgs(context, scopeInfo, schemaTable, triggerType, triggerCmd, connection, transaction);

            // Invoke setup-level interceptor if configured
            setupTable?.TriggerInterceptor?.Invoke(args);

            // Invoke global interceptor
            await this.InterceptAsync(args, progress, cancellationToken).ConfigureAwait(false);

            if (args.Cancel || args.Command == null)
                return null;

            return args.Command.CommandText;
        }

        /// <summary>
        /// Internal method to get stored procedure provisioning SQL script with interceptor support.
        /// </summary>
        internal async Task<string> InternalGetStoredProcedureProvisioningSqlAsync(
            SyncContext context, ScopeInfo scopeInfo, SyncTable schemaTable, SetupTable setupTable,
            DbTableBuilder tableBuilder, DbStoredProcedureType storedProcedureType, SyncFilter filter,
            DbConnection connection, DbTransaction transaction,
            IProgress<ProgressArgs> progress, CancellationToken cancellationToken)
        {
            var spCmd = await tableBuilder.GetCreateStoredProcedureCommandAsync(storedProcedureType, filter, connection, transaction).ConfigureAwait(false);

            if (spCmd == null || string.IsNullOrEmpty(spCmd.CommandText))
                return null;

            // Fire StoredProcedureCreatingArgs interceptor
            var args = new StoredProcedureCreatingArgs(context, scopeInfo, schemaTable, storedProcedureType, spCmd, connection, transaction);

            // Invoke setup-level interceptor if configured
            setupTable?.StoredProcedureInterceptor?.Invoke(args);

            // Invoke global interceptor
            await this.InterceptAsync(args, progress, cancellationToken).ConfigureAwait(false);

            if (args.Cancel || args.Command == null)
                return null;

            return args.Command.CommandText;
        }

        /// <summary>
        /// Internal method to get all provisioning SQL scripts for all tables in the setup.
        /// Supports optional target provider for cross-provider script generation and component filtering.
        /// </summary>
        internal virtual async Task<string> InternalGetProvisioningSqlScriptsAsync(
            SyncSetup setup,
            CoreProvider targetProvider,
            Func<ProvisioningComponentArgs, bool> shouldIncludeComponent,
            string scopeName = null,
            DbConnection connection = null,
            DbTransaction transaction = null,
            string scriptSeparator = null)
        {
            var context = new SyncContext(Guid.NewGuid(), scopeName ?? SyncOptions.DefaultScopeName);

            try
            {
                if (this.Provider == null)
                    throw new MissingProviderException(nameof(this.GetProvisioningSqlScriptsAsync));

                if (setup == null || setup.Tables.Count <= 0)
                    throw new MissingTablesException();

                using var runner = await this.GetConnectionAsync(context, SyncMode.NoTransaction, SyncStage.Provisioning, connection, transaction).ConfigureAwait(false);
                await using (runner.ConfigureAwait(false))
                {
                    // Get schema from database using current provider's connection
                    SyncSet schema;
                    (context, schema) = await this.InternalGetSchemaAsync(context, setup, runner.Connection, runner.Transaction, runner.Progress, runner.CancellationToken).ConfigureAwait(false);

                    // Create scope info with schema and setup
                    var scopeInfo = new ScopeInfo
                    {
                        Name = context.ScopeName,
                        Schema = schema,
                        Setup = setup,
                    };

                    var allScripts = new System.Text.StringBuilder();

                    // Sort tables based on dependencies
                    var schemaTables = schema.Tables
                        .SortByDependencies(tab => tab.GetRelations()
                            .Select(r => r.GetParentTable()));

                    // Determine which provider and connection to use for script generation
                    var scriptProvider = targetProvider ?? this.Provider;
                    DbConnection scriptConnection = null;
                    DbTransaction scriptTransaction = null;

                    try
                    {
                        // If using a different target provider, create its connection
                        if (targetProvider != null)
                            scriptConnection = targetProvider.CreateConnection();
                        else
                        {
                            scriptConnection = runner.Connection;
                            scriptTransaction = runner.Transaction;
                        }

                        foreach (var schemaTable in schemaTables)
                        {
                            // Check if entire table should be included
                            if (shouldIncludeComponent != null)
                            {
                                var tableArgs = new ProvisioningComponentArgs
                                {
                                    Table = schemaTable,
                                    ComponentType = ProvisioningComponentType.Table,
                                };

                                if (!shouldIncludeComponent(tableArgs))
                                    continue;
                            }

                            // Use the appropriate provider to get the sync adapter
                            var syncAdapter = scriptProvider.GetSyncAdapter(schemaTable, scopeInfo);
                            var tableBuilder = syncAdapter.GetTableBuilder();
                            var setupTable = setup.Tables[schemaTable.TableName, schemaTable.SchemaName];
                            var filter = schemaTable.GetFilter();

                            // Tracking Table
                            if (shouldIncludeComponent == null || shouldIncludeComponent(new ProvisioningComponentArgs
                            {
                                Table = schemaTable,
                                ComponentType = ProvisioningComponentType.TrackingTable,
                            }))
                            {
                                var trackingTableScript = await this.InternalGetTrackingTableProvisioningSqlAsync(
                                    context, scopeInfo, schemaTable, setupTable, tableBuilder,
                                    scriptConnection, scriptTransaction, runner.Progress, runner.CancellationToken).ConfigureAwait(false);

                                if (!string.IsNullOrEmpty(trackingTableScript))
                                {
                                    if (allScripts.Length > 0)
                                        allScripts.Append(scriptSeparator ?? syncAdapter.ProvisioningScriptSeparator);
                                    allScripts.Append(trackingTableScript);
                                }
                            }

                            // Triggers (Insert, Update, Delete)
                            foreach (DbTriggerType triggerType in new[] { DbTriggerType.Insert, DbTriggerType.Update, DbTriggerType.Delete })
                            {
                                if (shouldIncludeComponent == null || shouldIncludeComponent(new ProvisioningComponentArgs
                                {
                                    Table = schemaTable,
                                    ComponentType = ProvisioningComponentType.Trigger,
                                    TriggerType = triggerType,
                                }))
                                {
                                    var triggerScript = await this.InternalGetTriggerProvisioningSqlAsync(
                                        context, scopeInfo, schemaTable, setupTable, tableBuilder, triggerType,
                                        scriptConnection, scriptTransaction, runner.Progress, runner.CancellationToken).ConfigureAwait(false);

                                    if (!string.IsNullOrEmpty(triggerScript))
                                    {
                                        if (allScripts.Length > 0)
                                            allScripts.Append(scriptSeparator ?? syncAdapter.ProvisioningScriptSeparator);
                                        allScripts.Append(triggerScript);
                                    }
                                }
                            }

                            // Stored Procedures
                            var storedProcedureTypes = Enum.GetValues(typeof(DbStoredProcedureType)).Cast<DbStoredProcedureType>().OrderByDescending(sp => sp);

                            foreach (var spType in storedProcedureTypes)
                            {
                                // Check if filter-specific SP should be skipped
                                if ((spType == DbStoredProcedureType.SelectChangesWithFilters ||
                                     spType == DbStoredProcedureType.SelectInitializedChangesWithFilters) && filter == null)
                                    continue;

                                if (shouldIncludeComponent == null || shouldIncludeComponent(new ProvisioningComponentArgs
                                {
                                    Table = schemaTable,
                                    ComponentType = ProvisioningComponentType.StoredProcedure,
                                    StoredProcedureType = spType,
                                }))
                                {
                                    var spScript = await this.InternalGetStoredProcedureProvisioningSqlAsync(
                                        context, scopeInfo, schemaTable, setupTable, tableBuilder, spType, filter,
                                        scriptConnection, scriptTransaction, runner.Progress, runner.CancellationToken).ConfigureAwait(false);

                                    if (!string.IsNullOrEmpty(spScript))
                                    {
                                        if (allScripts.Length > 0)
                                            allScripts.Append(scriptSeparator ?? syncAdapter.ProvisioningScriptSeparator);
                                        allScripts.Append(spScript);
                                    }
                                }
                            }

                            // Custom provisioning SQL
                            if (setupTable?.CustomProvisioningSql != null && setupTable.CustomProvisioningSql.Count > 0)
                            {
                                if (shouldIncludeComponent == null || shouldIncludeComponent(new ProvisioningComponentArgs
                                {
                                    Table = schemaTable,
                                    ComponentType = ProvisioningComponentType.CustomSql,
                                }))
                                {
                                    foreach (var customSql in setupTable.CustomProvisioningSql)
                                    {
                                        if (!string.IsNullOrWhiteSpace(customSql))
                                        {
                                            if (allScripts.Length > 0)
                                                allScripts.Append(scriptSeparator ?? syncAdapter.ProvisioningScriptSeparator);

                                            allScripts.Append($"-- Custom Provisioning SQL for {schemaTable.GetFullName()}\n");
                                            allScripts.Append(customSql);
                                        }
                                    }
                                }
                            }
                        }

                        return allScripts.ToString();
                    }
                    finally
                    {
                        // Only dispose the connection if we created it (i.e., when using a different target provider)
                        if (targetProvider != null && scriptConnection != null)
                        {
                            scriptConnection.Close();
#if NETSTANDARD2_0
                            scriptConnection.Dispose();
#else
                            await scriptConnection.DisposeAsync().ConfigureAwait(false);
#endif
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                throw this.GetSyncError(context, ex);
            }
        }

        /// <summary>
        /// Gets all provisioning SQL scripts for all tables in the setup.
        /// Requires connection to discover schema but generates scripts without executing them.
        /// </summary>
        public virtual async Task<string> GetProvisioningSqlScriptsAsync(SyncSetup setup, string scopeName = null, DbConnection connection = null, DbTransaction transaction = null,
            string scriptSeparator = null)
        {
            return await this.InternalGetProvisioningSqlScriptsAsync(setup, null, null, scopeName, connection, transaction, scriptSeparator).ConfigureAwait(false);
        }

        /// <summary>
        /// Gets all provisioning SQL scripts for all tables in the setup, using a different target provider for script generation.
        /// Requires connection to discover schema from the current provider, but generates scripts for the target provider.
        /// Useful for generating scripts for a different database type (e.g., get SQLite scripts from a SQL Server connection).
        /// Note: A connection string for the target database is still required, but the connection is not opened or used for schema discovery.
        /// </summary>
        /// <param name="setup">The sync setup containing table configurations.</param>
        /// <param name="targetProvider">The target provider to generate scripts for (e.g., SqliteSyncProvider to generate SQLite scripts).</param>
        /// <param name="scopeName">Optional scope name.</param>
        /// <param name="connection">Optional existing connection to use for schema discovery.</param>
        /// <param name="transaction">Optional existing transaction.</param>
        /// <param name="scriptSeparator">custom script separator</param>
        /// <returns>A string containing all the provisioning SQL scripts for the target provider.</returns>
        public virtual async Task<string> GetProvisioningSqlScriptsAsync(SyncSetup setup, CoreProvider targetProvider, string scopeName = null, DbConnection connection = null, DbTransaction transaction = null,
            string scriptSeparator = null)
        {
            return await this.InternalGetProvisioningSqlScriptsAsync(setup, targetProvider, null, scopeName, connection, transaction, scriptSeparator).ConfigureAwait(false);
        }

        /// <summary>
        /// Gets all provisioning SQL scripts for all tables in the setup supporting custom filtering.
        /// Requires connection to discover schema from the current provider, but generates scripts for the target provider.
        /// Allows filtering of specific provisioning components through a callback function.
        /// </summary>
        /// <param name="setup">The sync setup containing table configurations.</param>
        /// <param name="shouldIncludeComponent">Optional callback to filter which components should be included in the generated scripts. Return true to include, false to exclude.</param>
        /// <param name="scopeName">Optional scope name.</param>
        /// <param name="connection">Optional existing connection to use for schema discovery.</param>
        /// <param name="transaction">Optional existing transaction.</param>
        /// <param name="scriptSeparator">custom script separator</param>
        /// <returns>A string containing all the provisioning SQL scripts for the target provider, filtered by the callback.</returns>
        public virtual async Task<string> GetProvisioningSqlScriptsAsync(
            SyncSetup setup,
            Func<ProvisioningComponentArgs, bool> shouldIncludeComponent,
            string scopeName = null,
            DbConnection connection = null,
            DbTransaction transaction = null,
            string scriptSeparator = null)
        {
            return await this.InternalGetProvisioningSqlScriptsAsync(setup, null, shouldIncludeComponent, scopeName, connection, transaction, scriptSeparator).ConfigureAwait(false);
        }

        /// <summary>
        /// Gets all provisioning SQL scripts for all tables in the setup, using a different target provider for script generation and custom filtering.
        /// Requires connection to discover schema from the current provider, but generates scripts for the target provider.
        /// Allows filtering of specific provisioning components through a callback function.
        /// </summary>
        /// <param name="setup">The sync setup containing table configurations.</param>
        /// <param name="targetProvider">The target provider to generate scripts for (e.g., SqliteSyncProvider to generate SQLite scripts). If null, uses the current provider.</param>
        /// <param name="shouldIncludeComponent">Optional callback to filter which components should be included in the generated scripts. Return true to include, false to exclude.</param>
        /// <param name="scopeName">Optional scope name.</param>
        /// <param name="connection">Optional existing connection to use for schema discovery.</param>
        /// <param name="transaction">Optional existing transaction.</param>
        /// <param name="scriptSeparator">custom script separator</param>
        /// <returns>A string containing all the provisioning SQL scripts for the target provider, filtered by the callback.</returns>
        public virtual async Task<string> GetProvisioningSqlScriptsAsync(
            SyncSetup setup,
            CoreProvider targetProvider,
            Func<ProvisioningComponentArgs, bool> shouldIncludeComponent,
            string scopeName = null,
            DbConnection connection = null,
            DbTransaction transaction = null,
            string scriptSeparator = null)
        {
            return await this.InternalGetProvisioningSqlScriptsAsync(setup, targetProvider, shouldIncludeComponent, scopeName, connection, transaction, scriptSeparator).ConfigureAwait(false);
        }
    }
}