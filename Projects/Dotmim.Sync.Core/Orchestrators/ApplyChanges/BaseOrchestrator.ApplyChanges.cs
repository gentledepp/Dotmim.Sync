using Wormhole.Sync.Batch;
using Wormhole.Sync.Builders;
using Wormhole.Sync.Enumerations;
using Wormhole.Sync.Serialization;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Wormhole.Sync
{

    /// <summary>
    /// Contains methods to apply changes.
    /// </summary>
    public abstract partial class BaseOrchestrator
    {
        /// <summary>
        /// Check if the containerTable columns differ from the schemaTable columns, requiring name-based mapping.
        /// </summary>
        private static bool NeedsColumnMapping(ContainerTable containerTable, SyncTable schemaTable)
        {
            if (containerTable.Columns.Count != schemaTable.Columns.Count)
                return true;

            for (int i = 0; i < containerTable.Columns.Count; i++)
            {
                if (!string.Equals(containerTable.Columns[i].ColumnName, schemaTable.Columns[i].ColumnName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
        /// <summary>
        /// Get all rows from a unified batch file for a specific table, regardless of operation type, with caching support.
        /// Used for error recovery scenarios.
        /// </summary>
        internal virtual async Task<IEnumerable<SyncRow>> GetAllRowsFromUnifiedBatchFileAsync(string filePath, SyncTable schemaTable, Dictionary<string, ContainerSet> cache, CancellationToken cancellationToken = default)
        {
            var directoryPath = Path.GetDirectoryName(filePath);
            var fileName = Path.GetFileName(filePath);

            var fileExists = await this.BatchStorage.FileExistsAsync(directoryPath, fileName, cancellationToken).ConfigureAwait(false);
            if (!fileExists)
                throw new FileNotFoundException($"Unified batch file not found: {filePath}");

            ContainerSet containerSet;

            // Check cache first if provided
            if (cache != null && cache.TryGetValue(filePath, out containerSet))
            {
                // Use cached version
            }
            else
            {
                // Deserialize the unified ContainerSet
                var serializer = SerializersFactory.JsonSerializerFactory.GetSerializer();

                using (var stream = await this.BatchStorage.ReadBatchPartAsync(directoryPath, fileName, cancellationToken).ConfigureAwait(false))
                {
                    containerSet = await serializer.DeserializeAsync<ContainerSet>(stream).ConfigureAwait(false);
                }

                // Cache it if cache is provided
                if (cache != null)
                {
                    cache[filePath] = containerSet;
                }
            }

            if (containerSet?.Tables == null)
                return Enumerable.Empty<SyncRow>();

            // Find the table that matches our schema table
            var containerTable = containerSet.Tables.FirstOrDefault(t =>
                string.Equals(t.TableName, schemaTable.TableName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(t.SchemaName, schemaTable.SchemaName, StringComparison.OrdinalIgnoreCase));

            if (containerTable == null)
                return Enumerable.Empty<SyncRow>();

            var result = new List<SyncRow>();

            // Check if column mapping is needed (schema evolution — batch has different columns than local schema)
            var needsMapping = NeedsColumnMapping(containerTable, schemaTable);
            IList<string> sourceColumnNames = null;
            if (needsMapping)
            {
                sourceColumnNames = new List<string>(containerTable.Columns.Count);
                foreach (var col in containerTable.Columns)
                    sourceColumnNames.Add(col.ColumnName);
            }

            // Return all rows for this table, converting to SyncRow
            for (int i = 0; i < containerTable.Rows.Count; i++)
            {
                SyncRow syncRow = null;
                try
                {
                    var rowData = containerTable.Rows[i];

                    if (needsMapping)
                        syncRow = SyncRow.CreateWithColumnMapping(schemaTable, rowData, sourceColumnNames);
                    else
                        syncRow = new SyncRow(schemaTable, rowData);
                }
                catch (Exception ex)
                {
                    // Log and skip problematic rows
                    this.Logger?.LogWarning(ex, "Error processing unified batch row {RowIndex} for table {TableName}", i, schemaTable.GetFullName());
                    continue;
                }
                if(syncRow != null)
                    result.Add(syncRow);
            }

            return result;
        }

        /// <summary>
        /// Get rows from a unified batch file, filtered by table and operation type, with caching support.
        /// </summary>
        internal virtual async Task<IEnumerable<SyncRow>> GetRowsFromUnifiedBatchFileAsync(string filePath, SyncTable schemaTable, SyncRowState applyType, Dictionary<string, ContainerSet> cache, CancellationToken cancellationToken = default)
        {
            var directoryPath = Path.GetDirectoryName(filePath);
            var fileName = Path.GetFileName(filePath);

            var fileExists = await this.BatchStorage.FileExistsAsync(directoryPath, fileName, cancellationToken).ConfigureAwait(false);
            if (!fileExists)
                throw new FileNotFoundException($"Unified batch file not found: {filePath}");

            ContainerSet containerSet;

            // Check cache first if provided
            if (cache != null && cache.TryGetValue(filePath, out containerSet))
            {
                // Use cached version
            }
            else
            {
                // Deserialize the unified ContainerSet
                var serializer = SerializersFactory.JsonSerializerFactory.GetSerializer();

                using (var stream = await this.BatchStorage.ReadBatchPartAsync(directoryPath, fileName, cancellationToken).ConfigureAwait(false))
                {
                    containerSet = await serializer.DeserializeAsync<ContainerSet>(stream).ConfigureAwait(false);
                }

                // Cache it if cache is provided
                if (cache != null)
                {
                    cache[filePath] = containerSet;
                }
            }

            if (containerSet?.Tables == null)
                return Enumerable.Empty<SyncRow>();

            // Find the table that matches our schema table
            var containerTable = containerSet.Tables.FirstOrDefault(t =>
                string.Equals(t.TableName, schemaTable.TableName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(t.SchemaName, schemaTable.SchemaName, StringComparison.OrdinalIgnoreCase));

            if (containerTable == null)
                return Enumerable.Empty<SyncRow>();

            var result = new List<SyncRow>();

            // Check if column mapping is needed (schema evolution — batch has different columns than local schema)
            var needsMapping = NeedsColumnMapping(containerTable, schemaTable);
            IList<string> sourceColumnNames = null;
            if (needsMapping)
            {
                sourceColumnNames = new List<string>(containerTable.Columns.Count);
                foreach (var col in containerTable.Columns)
                    sourceColumnNames.Add(col.ColumnName);
            }

            // Filter rows by state and convert to SyncRow
            for (int i = 0; i < containerTable.Rows.Count; i++)
            {
                SyncRow syncRow = null;
                try
                {
                    // Read row data - format: [state, col1, col2, ..., colN]
                    var rowData = containerTable.Rows[i];

                    // Extract the state from position 0
                    var rowState = (SyncRowState)SyncTypeConverter.TryConvertTo<int>(rowData[0]);

                    // Skip rows that don't match our target apply type
                    // For Deleted, we only want Deleted rows
                    // For Modified, we want Modified rows (both inserts and updates)
                    if (applyType == SyncRowState.Deleted && rowState != SyncRowState.Deleted &&
                        rowState != SyncRowState.RetryDeletedOnNextSync && rowState != SyncRowState.ApplyDeletedFailed)
                        continue;

                    if (applyType == SyncRowState.Modified &&
                        (rowState == SyncRowState.Deleted || rowState == SyncRowState.RetryDeletedOnNextSync || rowState == SyncRowState.ApplyDeletedFailed))
                        continue;

                    // Create SyncRow with column mapping if schemas differ
                    if (needsMapping)
                        syncRow = SyncRow.CreateWithColumnMapping(schemaTable, rowData, sourceColumnNames);
                    else
                        syncRow = new SyncRow(schemaTable, rowData);

                }
                catch (Exception ex)
                {
                    // Log and skip problematic rows
                    this.Logger?.LogWarning(ex, "Error processing unified batch row {RowIndex} for table {TableName}", i, schemaTable.GetFullName());
                    continue;
                }
                if(syncRow != null)
                    result.Add(syncRow);
            }

            return result;
        }

        /// <summary>
        /// Apply changes : Delete / Insert / Update
        /// the fromScope is local client scope when this method is called from server
        /// the fromScope is server scope when this method is called from client.
        /// </summary>
        internal virtual async Task<ApplyChangesException>
            InternalApplyChangesAsync(ScopeInfo scopeInfo, SyncContext context, MessageApplyChanges message, DbConnection connection, DbTransaction transaction,
                             IProgress<ProgressArgs> progress, CancellationToken cancellationToken)
        {
            context.SyncStage = SyncStage.ChangesApplying;

            message.ChangesApplied ??= new DatabaseChangesApplied();
            message.FailedRows ??= message.Schema.Clone();

            // Check if we have some data available
            var hasChanges = message.Changes.HasData();

            // critical exception that causes rollback
            ApplyChangesException failureException = null;

            try
            {
                // if we have changes or if we are in re init mode
                if (hasChanges || context.SyncType != SyncType.Normal)
                {
                    this.Logger.LogInformation(
                        $@"[InternalApplyChangesAsync]. directory {{DirectoryName}} BatchPartsInfo count: {{BatchPartsInfoCount}} RowsCount {{RowsCount}}",
                        message.Changes.DirectoryName, message.Changes.BatchPartsInfo.Count, message.Changes.RowsCount);

                    var schemaTables = message.Schema.Tables.SortByDependencies(tab => tab.GetRelations().Select(r => r.GetParentTable())).ToArray();
                    var reverseSchemaTables = schemaTables.Reverse().ToArray();

                    // create local directory
                    if (!string.IsNullOrEmpty(message.BatchDirectory))
                        await this.BatchStorage.EnsureDirectoryExistsAsync(message.BatchDirectory, cancellationToken).ConfigureAwait(false);

                    // Disable check constraints
                    // Because Sqlite does not support "PRAGMA foreign_keys=OFF" Inside a transaction
                    // Report this disabling constraints brefore opening a transaction
                    if (this.Options.DisableConstraintsOnApplyChanges && this.Provider.ConstraintsLevelAction == ConstraintsLevelAction.OnSessionLevel)
                    {
                        foreach (var table in schemaTables)
                            context = await this.InternalDisableConstraintsAsync(scopeInfo, context, table, connection, transaction, progress, cancellationToken).ConfigureAwait(false);
                    }

                    // -----------------------------------------------------
                    // 0) Check if we are in a reinit mode (Check also SyncWay to be sure we don't reset tables on server, then check if we don't have already isApplied a snapshot)
                    // -----------------------------------------------------
                    if (context.SyncRole == SyncRole.Client && context.SyncType != SyncType.Normal && !message.SnapshoteApplied)
                    {
                        foreach (var table in reverseSchemaTables)
                        {

                            context = await this.InternalResetTableAsync(scopeInfo, context, table, connection, transaction,
                                        progress, cancellationToken).ConfigureAwait(false);
                        }
                    }

                    // -----------------------------------------------------
                    // 0b) Per-table reinit for schema evolution:
                    //     Force-disable FK constraints, then reset only the
                    //     reinit tables. Constraints stay disabled through
                    //     the insert and delete phases below and are
                    //     re-enabled after both phases complete.
                    // -----------------------------------------------------
                    var hasReinitTables = context.SyncRole == SyncRole.Client
                        && message.ReinitTables != null && message.ReinitTables.Count > 0;

                    if (hasReinitTables)
                    {
                        // Force-disable FK constraints on ALL tables before reset/insert.
                        // This is required even when DisableConstraintsOnApplyChanges is false,
                        // because other tables may reference the reinit tables via FK.
                        foreach (var table in schemaTables)
                            context = await this.InternalDisableConstraintsAsync(scopeInfo, context, table, connection, transaction, progress, cancellationToken).ConfigureAwait(false);

                        // Reset only the reinit tables (in reverse dependency order)
                        foreach (var table in reverseSchemaTables)
                        {
                            var tableKey = string.IsNullOrEmpty(table.SchemaName)
                                ? table.TableName : $"{table.SchemaName}.{table.TableName}";
                            if (message.ReinitTables.Contains(tableKey))
                                context = await this.InternalResetTableAsync(scopeInfo, context, table, connection, transaction, progress, cancellationToken).ConfigureAwait(false);
                        }
                    }

                    // Trying to change order (from deletes-upserts to upserts-deletes)
                    // see https://github.com/Mimetis/Dotmim.Sync/discussions/453#discussioncomment-380530

                    // -----------------------------------------------------
                    // 1) Applying Inserts and Updates. Apply in table order
                    // -----------------------------------------------------
                    if (hasChanges)
                    {
                        foreach (var table in schemaTables)
                        {
                            failureException = await this.InternalApplyTableChangesAsync(scopeInfo, context, table, message, message.FailedRows.Tables[table.TableName, table.SchemaName],
                                        connection, transaction, SyncRowState.Modified, message.ChangesApplied,
                                        progress, cancellationToken).ConfigureAwait(false);

                            if (failureException != null)
                                break;
                        }
                    }

                    // -----------------------------------------------------
                    // 2) Applying Deletes. Do not apply deletes if we are in a new database
                    // -----------------------------------------------------
                    if (!message.IsNew && hasChanges && failureException == null)
                    {
                        foreach (var table in reverseSchemaTables)
                        {
                            // Skip deletes for reinit tables — they've been reset and re-inserted
                            if (hasReinitTables)
                            {
                                var tableKey = string.IsNullOrEmpty(table.SchemaName)
                                    ? table.TableName : $"{table.SchemaName}.{table.TableName}";
                                if (message.ReinitTables.Contains(tableKey))
                                    continue;
                            }

                            failureException = await this.InternalApplyTableChangesAsync(scopeInfo, context, table, message, message.FailedRows.Tables[table.TableName, table.SchemaName],
                                connection, transaction, SyncRowState.Deleted, message.ChangesApplied,
                                progress, cancellationToken).ConfigureAwait(false);

                            if (failureException != null)
                                break;
                        }
                    }

                    // Re-enable FK constraints that were force-disabled for reinit tables
                    if (hasReinitTables)
                    {
                        foreach (var table in schemaTables)
                            context = await this.InternalEnableConstraintsAsync(scopeInfo, context, table, connection, transaction, progress, cancellationToken).ConfigureAwait(false);
                    }

                    // Re enable check constraints
                    if (this.Options.DisableConstraintsOnApplyChanges && this.Provider.ConstraintsLevelAction == ConstraintsLevelAction.OnSessionLevel)
                    {
                        foreach (var table in schemaTables)
                            context = await this.InternalEnableConstraintsAsync(scopeInfo, context, table, connection, transaction, progress, cancellationToken).ConfigureAwait(false);
                    }
                }

                // if we set option to clean folder && message allows to clean (it won't allow to clean if batch is an error batch)
                if (this.Options.CleanFolder)
                {
                    // Before cleaning, check if we are not applying changes from a snapshotdirectory
                    var cleanFolder = await this.InternalCanCleanFolderAsync(scopeInfo.Name, context.Parameters, message.Changes, progress, cancellationToken).ConfigureAwait(false);

                    // clear the changes because we don't need them anymore
                    if (cleanFolder)
                    {
                        this.Logger.LogInformation("[InternalApplyChangesAsync]. Cleaning directory {DirectoryName}.", message.Changes.DirectoryName);
                        await message.Changes.TryRemoveDirectoryAsync().ConfigureAwait(false);
                    }
                }

                this.Logger.LogInformation(failureException, "[InternalApplyChangesAsync]");

                return failureException;
            }
            catch (Exception ex)
            {
                throw this.GetSyncError(context, ex);
            }
        }

        /// <summary>
        /// Apply changes internal method for one type of query: Insert, Update or Delete for every batch from a table.
        /// </summary>
        internal virtual async Task<ApplyChangesException> InternalApplyTableChangesAsync(ScopeInfo scopeInfo, SyncContext context, SyncTable schemaTable,
            MessageApplyChanges message, SyncTable errorsTable,
            DbConnection connection, DbTransaction transaction, SyncRowState applyType, DatabaseChangesApplied changesApplied,
            IProgress<ProgressArgs> progress, CancellationToken cancellationToken)
        {
            if (this.Provider == null)
                return default;

            // Method-level cache for unified batch files - automatically cleaned up when method exits
            var unifiedBatchCache = context.UnifiedBatchCache;
            
            context.SyncStage = SyncStage.ChangesApplying;

            var setupTable = scopeInfo.Setup.Tables[schemaTable.TableName, schemaTable.SchemaName];

            if (setupTable == null)
                return default;

            // Only table schema is replicated, no datas are isApplied
            if (setupTable.SyncDirection == SyncDirection.None)
                return default;

            // if we are in upload stage, so check if table is not download only
            if (context.SyncRole == SyncRole.Server && setupTable.SyncDirection == SyncDirection.DownloadOnly)
                return default;

            // if we are in download stage, so check if table is not download only
            if (context.SyncRole == SyncRole.Client && setupTable.SyncDirection == SyncDirection.UploadOnly)
                return default;

            var hasChanges = message.Changes.HasData(schemaTable.TableName, schemaTable.SchemaName);

            // Each table in the messages contains scope columns. Don't forget it
            if (!hasChanges)
                return default;

            // what kind of command to execute
            var isReinitTable = message.ReinitTables != null && message.ReinitTables.Contains(
               string.IsNullOrEmpty(schemaTable.SchemaName)
                  ? schemaTable.TableName
                  : $"{schemaTable.SchemaName}.{schemaTable.TableName}");

            var init = message.IsNew || context.SyncType != SyncType.Normal || isReinitTable;
            var dbCommandType = applyType == SyncRowState.Deleted ? DbCommandType.DeleteRows : (init ? DbCommandType.InsertRows : DbCommandType.UpdateRows);
            var dbPreCommandType = applyType == SyncRowState.Deleted ? DbCommandType.PreDeleteRows : (init ? DbCommandType.PreInsertRows : DbCommandType.PreUpdateRows);

            this.Logger.LogInformation("[InternalApplyTableChangesAsync]. table {TableName}. init {Init} command type {DbCommandType}", schemaTable.GetFullName(), init, dbCommandType);

            // tmp sync table with only writable columns
            var changesSet = schemaTable.Schema.Clone(false);
            var schemaChangesTable = CreateChangesTable(schemaTable, changesSet);

            // get executioning adapter
            var syncAdapter = this.GetSyncAdapter(schemaChangesTable, scopeInfo);

            TableChangesApplied tableChangesApplied = null;

            await using var localSerializer = new LocalJsonSerializer(this.BatchStorage, this, context);

            // conflict resolved count
            var conflictsResolvedCount = 0;

            // Failure exception if any
            ApplyChangesException failureException = null;

            // Conflicts occured when trying to apply rows
            var conflictRows = new List<SyncRow>();

            // Track rejected rows and their specified conflict resolutions
            var rejectedRowResolutions = new Dictionary<SyncRow, ConflictResolution?>();

            // failed rows that were ignored
            var failedRows = 0;

            // Errors occured when trying to apply rows
            var errorsRows = new List<(SyncRow SyncRow, Exception Exception)>();

            // Applied row for this particular BPI
            var appliedRows = 0;

            // Get command
            DbCommand command = null;
            var isBatch = false;

            var bpiTables = message.Changes.GetBatchPartsInfos(schemaTable);

            // launch interceptor if any
            var args = new TableChangesApplyingArgs(context, message.Changes, bpiTables, schemaTable, applyType, command, connection, transaction);
            await this.InterceptAsync(args, progress, cancellationToken).ConfigureAwait(false);

            foreach (var batchPartInfo in bpiTables)
            {
                // Get full path of my batchpartinfo
                var fullPath = message.Changes.GetBatchPartInfoFullPath(batchPartInfo);

                if (batchPartInfo.State != SyncRowState.None && batchPartInfo.State != applyType)
                    continue;

                var batchChangesApplyingArgs = new BatchChangesApplyingArgs(context, message.Changes, batchPartInfo, schemaTable, applyType, command, connection, transaction);

                // We don't report progress if we do not have isApplied any changes on the table, to limit verbosity of Progress
                await this.InterceptAsync(batchChangesApplyingArgs, progress, cancellationToken).ConfigureAwait(false);

                this.Logger.LogInformation("[InternalApplyTableChangesAsync]. Directory name {DirectoryName}. BatchParts count {BatchPartsInfoCount}", message.Changes.DirectoryName, message.Changes.BatchPartsInfo.Count);

                // If we have a transient error happening, and we are rerunning the tranaction,
                // raising an interceptor
                var onRetry = new Func<Exception, int, TimeSpan, object, Task>((ex, cpt, ts, arg) =>
                    this.InterceptAsync(new TransientErrorOccuredArgs(context, connection, ex, cpt, ts), progress, cancellationToken).AsTask());

                // Defining my retry policy
                var retryPolicy = this.Options.TransactionMode != TransactionMode.AllOrNothing
                    ? SyncPolicy.WaitAndRetryForever(retryAttempt => TimeSpan.FromMilliseconds(500 * retryAttempt), (ex, arg) => this.Provider.ShouldRetryOn(ex), onRetry)
                    : SyncPolicy.WaitAndRetry(0, TimeSpan.Zero);

                var applyChangesPolicyResult = await retryPolicy.ExecuteAsync(
                    async () =>
                {
                    // Connection & Transaction runner
                    DbConnectionRunner runner = null;

                    // Conflicts occured when trying to apply rows
                    var conflictRows = new List<SyncRow>();

                    // failed rows that were ignored
                    var failedRows = 0;

                    // Errors occured when trying to apply rows
                    var errorsRows = new List<(SyncRow SyncRow, Exception Exception)>();

                    // Applied row for this particular BPI
                    var appliedRows = 0;

                    try
                    {
                        runner = await this.GetConnectionAsync(context, this.Options.TransactionMode == TransactionMode.PerBatch ? SyncMode.WithTransaction : SyncMode.NoTransaction, SyncStage.ChangesApplying, connection, transaction, progress, cancellationToken).ConfigureAwait(false);

                        // Disable check constraints for provider supporting only at table level
                        if (this.Options.DisableConstraintsOnApplyChanges && this.Provider.ConstraintsLevelAction == ConstraintsLevelAction.OnTableLevel)
                            await this.InternalDisableConstraintsAsync(scopeInfo, context, schemaTable, runner.Connection, runner.Transaction, runner.Progress, runner.CancellationToken).ConfigureAwait(false);

                        // Pre command if exists
                        var (preCommand, _) = await this.InternalGetCommandAsync(scopeInfo, context, syncAdapter, dbPreCommandType,
                                runner.Connection, runner.Transaction, runner.Progress, runner.CancellationToken).ConfigureAwait(false);

                        if (preCommand != null)
                        {
                            try
                            {
                                await this.InterceptAsync(new ExecuteCommandArgs(context, preCommand, dbPreCommandType, runner.Connection, runner.Transaction), runner.Progress, runner.CancellationToken).ConfigureAwait(false);
                                await preCommand.ExecuteNonQueryAsync().ConfigureAwait(false);
                            }
                            finally
                            {
                                preCommand.Dispose();
                            }
                        }

                        (command, isBatch) = await this.InternalGetCommandAsync(scopeInfo, context, syncAdapter, dbCommandType,
                                    runner.Connection, runner.Transaction, runner.Progress, runner.CancellationToken).ConfigureAwait(false);

                        if (command == null)
                            return (0, default, default, 0);

                        // Rows fetch from the BPI
                        var rowsFetched = 0;

                        // accumulating rows
                        var batchRows = new List<SyncRow>();

                        if (isBatch)
                        {
                            var expectedRowCount = batchPartInfo.RowsCount;
                            IEnumerable<SyncRow> rowsEnumerable;

                            // Check if this is a unified batch file
                            if (context.UseUnifiedBatching && batchPartInfo.TableName == "UNIFIED")
                            {
                                var rows = (await this.GetRowsFromUnifiedBatchFileAsync(fullPath, schemaChangesTable, applyType,
                                    unifiedBatchCache, runner.CancellationToken).ConfigureAwait(false)).ToList();
                                rowsEnumerable = rows;
                                expectedRowCount = rows.Count;
                            }
                            else
                            {
                               var batchDirectoryPath = Path.GetDirectoryName(fullPath);
                               var batchFileName = Path.GetFileName(fullPath);
                               rowsEnumerable = await localSerializer.GetRowsFromFileAsync(batchDirectoryPath, batchFileName, schemaChangesTable, runner.CancellationToken).ConfigureAwait(false);
                            }

                            foreach (var syncRow in rowsEnumerable)
                            {
                                rowsFetched++;

                                // Adding rows to the batch rows
                                if (batchRows.Count < this.Provider.BulkBatchMaxLinesCount)
                                {
                                    if (applyType == SyncRowState.Modified && syncRow.RowState is SyncRowState.RetryModifiedOnNextSync or SyncRowState.Modified)
                                        batchRows.Add(syncRow);
                                    else if (applyType == SyncRowState.Deleted && syncRow.RowState is SyncRowState.RetryDeletedOnNextSync or SyncRowState.Deleted)
                                        batchRows.Add(syncRow);
                                    else if (syncRow.RowState is SyncRowState.ApplyModifiedFailed or SyncRowState.ApplyDeletedFailed)
                                        errorsRows.Add((syncRow, new Exception("Row failed to be applied on last sync")));

                                    if (rowsFetched < expectedRowCount && batchRows.Count < this.Provider.BulkBatchMaxLinesCount)
                                        continue;
                                }

                                if (batchRows.Count <= 0)
                                    continue;

                                command.Connection = runner.Connection;
                                command.Transaction = runner.Transaction;

                                var (rowAppliedCount, conflictSyncRows, rejectedResolutions, errorException) = await this.InternalApplyBatchRowsAsync(context, command, batchRows, schemaChangesTable, applyType, message, dbCommandType, syncAdapter, setupTable,
                                                runner.Connection, runner.Transaction, runner.Progress, runner.CancellationToken).ConfigureAwait(false);

                                if (errorException == null)
                                {
                                    // Add isApplied rows
                                    appliedRows += rowAppliedCount;

                                    // Check conflicts
                                    if (conflictSyncRows != null)
                                    {
                                        conflictRows.AddRange(conflictSyncRows);
                                    }

                                    // Collect rejected row resolutions
                                    if (rejectedResolutions != null)
                                    {
                                        foreach (var kvp in rejectedResolutions)
                                        {
                                            if (!rejectedRowResolutions.ContainsKey(kvp.Key))
                                                rejectedRowResolutions[kvp.Key] = kvp.Value;
                                        }
                                    }
                                }
                                else
                                {
                                    // if transient error, let the policy tries again, instead of going for 1 by 1 row
                                    var transientError = this.Provider.ShouldRetryOn(errorException);

                                    if (transientError)
                                        throw errorException;

                                    // we have an error in the entire batch
                                    // try to fallback to row per row
                                    // and see if we can still continue to insert rows (excepted the error one) and manage the error
                                    this.Logger.LogInformation(errorException, "[InternalApplyTableChangesAsync]. Using per line apply since we had an error on batch mode.");

                                    // fallback to row per row
                                    var fallbackArgs = new RowsChangesFallbackFromBatchToSingleRowApplyingArgs(context, errorException, message.Changes, batchRows, schemaChangesTable, applyType, command,
                                            runner.Connection, runner.Transaction);

                                    await this.InterceptAsync(fallbackArgs, runner.Progress, runner.CancellationToken).ConfigureAwait(false);

                                    syncAdapter.UseBulkOperations = false;
                                    (command, isBatch) = await this.InternalGetCommandAsync(scopeInfo, context, syncAdapter, dbCommandType,
                                                    runner.Connection, runner.Transaction, runner.Progress, runner.CancellationToken).ConfigureAwait(false);

                                    var cmdText = command.CommandText;

                                    foreach (var batchRow in batchRows)
                                    {
                                        if (batchRow.RowState is SyncRowState.ApplyModifiedFailed or SyncRowState.ApplyDeletedFailed)
                                        {
                                            errorsRows.Add((batchRow, new Exception("Row failed to be applied on last sync")));
                                            continue;
                                        }

                                        if (applyType == SyncRowState.Modified && batchRow.RowState is not (SyncRowState.RetryModifiedOnNextSync or SyncRowState.Modified))
                                            continue;

                                        if (applyType == SyncRowState.Deleted && batchRow.RowState is not (SyncRowState.RetryDeletedOnNextSync or SyncRowState.Deleted))
                                            continue;

#pragma warning disable CA2100 // Review SQL queries for security vulnerabilities
                                        command.CommandText = cmdText;
#pragma warning restore CA2100 // Review SQL queries for security vulnerabilities
                                        command.Connection = runner.Connection;
                                        command.Transaction = runner.Transaction;

                                        var (singleRowAppliedCount, rejectedResolution, singleErrorException) = await this.InternalApplySingleRowAsync(context, command, batchRow, schemaChangesTable, syncAdapter, applyType, message, dbCommandType, setupTable,
                                                        runner.Connection, runner.Transaction, runner.Progress, runner.CancellationToken).ConfigureAwait(false);

                                        if (singleRowAppliedCount > 0)
                                            appliedRows++;
                                        else if (singleErrorException != null && this.Provider.ShouldRetryOn(singleErrorException))
                                            throw singleErrorException;
                                        else if (singleErrorException != null)
                                            errorsRows.Add((batchRow, singleErrorException));
                                        else
                                        {
                                            conflictRows.Add(batchRow);
                                            if (rejectedResolution.HasValue && !rejectedRowResolutions.ContainsKey(batchRow))
                                                rejectedRowResolutions[batchRow] = rejectedResolution;
                                        }
                                    }

                                    // revert back bulk operation
                                    syncAdapter.UseBulkOperations = true;
                                }

                                batchRows.Clear();
                            }
                        }
                        else
                        {
                            command.Connection = runner.Connection;
                            command.Transaction = runner.Transaction;

                            // Check if this is a unified batch file
                            IEnumerable<SyncRow> rowsEnumerable;
                            if (context.UseUnifiedBatching && batchPartInfo.TableName == "UNIFIED")
                            {
                               rowsEnumerable = await this.GetRowsFromUnifiedBatchFileAsync(fullPath, schemaChangesTable, applyType, unifiedBatchCache, runner.CancellationToken).ConfigureAwait(false);
                            }
                            else
                            {
                               var batchDirectoryPath = Path.GetDirectoryName(fullPath);
                               var batchFileName = Path.GetFileName(fullPath);
                               rowsEnumerable = await localSerializer.GetRowsFromFileAsync(batchDirectoryPath, batchFileName, schemaChangesTable, runner.CancellationToken).ConfigureAwait(false);
                            }

                            foreach (var syncRow in rowsEnumerable)
                            {
                                if (syncRow.RowState is SyncRowState.ApplyModifiedFailed or SyncRowState.ApplyDeletedFailed)
                                {
                                    errorsRows.Add((syncRow, new Exception("Row failed to be applied on last sync")));
                                    continue;
                                }

                                if (applyType == SyncRowState.Modified && syncRow.RowState is not (SyncRowState.RetryModifiedOnNextSync or SyncRowState.Modified))
                                    continue;

                                if (applyType == SyncRowState.Deleted && syncRow.RowState is not (SyncRowState.RetryDeletedOnNextSync or SyncRowState.Deleted))
                                    continue;

                                var (rowAppliedCount, rejectedResolution, errorException) = await this.InternalApplySingleRowAsync(context, command, syncRow, schemaChangesTable, syncAdapter, applyType, message, dbCommandType, setupTable,
                                        runner.Connection, runner.Transaction, progress, cancellationToken).ConfigureAwait(false);

                                if (rowAppliedCount > 0)
                                    appliedRows++;
                                else if (errorException != null && this.Provider.ShouldRetryOn(errorException))
                                    throw errorException;
                                else if (errorException != null)
                                    errorsRows.Add((syncRow, errorException));
                                else
                                {
                                    conflictRows.Add(syncRow);
                                    if (rejectedResolution.HasValue && !rejectedRowResolutions.ContainsKey(syncRow))
                                        rejectedRowResolutions[syncRow] = rejectedResolution;
                                }
                            }
                        }

                        // Enable check constraints for provider supporting only at table level
                        if (this.Options.DisableConstraintsOnApplyChanges && this.Provider.ConstraintsLevelAction == ConstraintsLevelAction.OnTableLevel)
                            await this.InternalEnableConstraintsAsync(scopeInfo, context, schemaTable, runner.Connection, runner.Transaction, runner.Progress, runner.CancellationToken).ConfigureAwait(false);

                        await runner.CommitAsync().ConfigureAwait(false);

                        return (appliedRows, conflictRows, errorsRows, failedRows);
                    }
                    catch (Exception ex)
                    {
                        if (runner != null)
                            await runner.RollbackAsync($"InternalApplyTableChangesAsync during apply changes. Error:{ex.Message}").ConfigureAwait(false);

                        throw this.GetSyncError(context, ex);
                    }
                    finally
                    {
                        if (runner != null)
                            await runner.DisposeAsync().ConfigureAwait(false);
                    }
                }, cancellationToken).ConfigureAwait(false);

                var batchChangesAppliedArgs = new BatchChangesAppliedArgs(context, message.Changes, batchPartInfo, schemaTable, applyType, command, connection, transaction);

                // We don't report progress if we do not have isApplied any changes on the table, to limit verbosity of Progress
                await this.InterceptAsync(batchChangesAppliedArgs, progress, cancellationToken).ConfigureAwait(false);

                appliedRows += applyChangesPolicyResult.appliedRows;
                failedRows += applyChangesPolicyResult.failedRows;

                if (applyChangesPolicyResult.conflictRows?.Count > 0)
                    conflictRows.AddRange(applyChangesPolicyResult.conflictRows);

                if (applyChangesPolicyResult.errorsRows?.Count > 0)
                    errorsRows.AddRange(applyChangesPolicyResult.errorsRows);
            }

            try
            {
                var ce = await this.InternalApplyConflictsAndErrorsAsync(scopeInfo, context, schemaChangesTable, applyType, errorsTable, conflictRows, rejectedRowResolutions, errorsRows, message,
                      connection, transaction, progress, cancellationToken).ConfigureAwait(false);

                appliedRows += ce.AppliedRows;
                failedRows += ce.FailedRows;
                conflictsResolvedCount += ce.ConflictsResolvedCount;
                failureException = ce.FailureException;

                // Only Upsert DatabaseChangesApplied if we make an upsert/ delete from the batch or resolved any conflict
                if (appliedRows > 0 || conflictsResolvedCount > 0 || failedRows > 0)
                {
                    // We may have multiple batch files, so we can have multipe sync tables with the same name
                    // We can say that a syncTable may be contained in several files
                    // That's why we should get an isApplied changes instance if already exists from a previous batch file
                    tableChangesApplied = changesApplied.TableChangesApplied.FirstOrDefault(tca =>
                    {
                        var sc = SyncGlobalization.DataSourceStringComparison;

                        var sn = tca.SchemaName ?? string.Empty;
                        var otherSn = schemaTable.SchemaName ?? string.Empty;

                        return tca.TableName.Equals(schemaTable.TableName, sc) &&
                               sn.Equals(otherSn, sc) &&
                               tca.State == applyType;
                    });

                    if (tableChangesApplied == null)
                    {
                        tableChangesApplied = new TableChangesApplied
                        {
                            TableName = schemaTable.TableName,
                            SchemaName = schemaTable.SchemaName,
                            Applied = appliedRows,
                            ResolvedConflicts = conflictsResolvedCount,
                            Failed = failedRows,
                            State = applyType,
                            TotalRowsCount = message.Changes.RowsCount,
                            TotalAppliedCount = changesApplied.TotalAppliedChanges + appliedRows,
                        };
                        changesApplied.TableChangesApplied.Add(tableChangesApplied);
                    }
                    else
                    {
                        tableChangesApplied.Applied += appliedRows;
                        tableChangesApplied.TotalAppliedCount = changesApplied.TotalAppliedChanges;
                        tableChangesApplied.ResolvedConflicts += conflictsResolvedCount;
                        tableChangesApplied.Failed += failedRows;
                    }

                    // we've got 0.25% to fill here
                    var progresspct = appliedRows * 0.25d / tableChangesApplied.TotalRowsCount;
                    context.ProgressPercentage += progresspct;

                    connection ??= this.Provider.CreateConnection();
                    var tableChangesAppliedArgs = new TableChangesAppliedArgs(context, tableChangesApplied, connection, transaction);

                    // We don't report progress if we do not have isApplied any changes on the table, to limit verbosity of Progress
                    await this.InterceptAsync(tableChangesAppliedArgs, progress, cancellationToken).ConfigureAwait(false);

                }
            }
            catch (Exception ex)
            {
                throw this.GetSyncError(context, ex);
            }
            finally
            {
                command?.Dispose();
            }

            this.Logger.LogInformation(failureException, "[InternalApplyChangesAsync]");

            return failureException;
        }

        /// <summary>
        /// Apply a single row.
        /// </summary>
        internal virtual async Task<(int RowAppliedCount, ConflictResolution? RejectedResolution, Exception ErrorException)> InternalApplySingleRowAsync(SyncContext context, DbCommand command,
            SyncRow syncRow, SyncTable schemaChangesTable, DbSyncAdapter syncAdapter,
            SyncRowState applyType, MessageApplyChanges message, DbCommandType dbCommandType, SetupTable setupTable,
            DbConnection connection, DbTransaction transaction, IProgress<ProgressArgs> progress, CancellationToken cancellationToken)
        {
            // VALIDATION PHASE - runs BEFORE applying and is NOT called during conflict resolution
            var validatingArgs = new RowsChangesValidatingArgs(context, message.Changes, [syncRow], schemaChangesTable, applyType, connection, transaction);
            await this.InterceptAsync(validatingArgs, progress, cancellationToken).ConfigureAwait(false);

            // Invoke table-scoped validating interceptors if any are registered
            if (setupTable != null)
            {
                if (setupTable.RowsChangesValidatingInterceptors != null)
                {
                    foreach (var interceptor in setupTable.RowsChangesValidatingInterceptors)
                    {
                        if (interceptor != null)
                            await interceptor.Invoke(validatingArgs).ConfigureAwait(false);
                    }
                }
            }

            // Check if validation was canceled
            if (validatingArgs.Cancel)
                return (-1, null, null);

            // Check if the row was marked as rejected/conflict during validation - if so, skip DB execution and return as not applied
            if (validatingArgs.RejectedRows.ContainsKey(syncRow))
                return (0, validatingArgs.RejectedRows[syncRow], null);

            // APPLYING PHASE - only reached if row was not rejected during validation
            var batchArgs = new RowsChangesApplyingArgs(context, message.Changes, [syncRow], schemaChangesTable, applyType, command, connection, transaction);
            await this.InterceptAsync(batchArgs, progress, cancellationToken).ConfigureAwait(false);

            // Invoke table-scoped applying interceptors if any are registered
            if (setupTable != null)
            {
                if (setupTable.RowsChangesApplyingInterceptors != null)
                {
                    foreach (var interceptor in setupTable.RowsChangesApplyingInterceptors)
                    {
                        if (interceptor != null)
                            await interceptor.Invoke(batchArgs).ConfigureAwait(false);
                    }
                }
            }

            if (batchArgs.Cancel || batchArgs.Command == null || batchArgs.SyncRows == null || batchArgs.SyncRows.Count <= 0)
                return (-1, null, null);

            Exception errorException = null;
            var rowAppliedCount = 0;

            try
            {
                // get the correct pointer to the command from the interceptor in case user change the whole instance
                command = batchArgs.Command;

                // Set the parameters value from row
                this.InternalSetCommandParametersValues(context, command, dbCommandType, syncAdapter, connection, transaction,
                    batchArgs.SyncRows[0], message.SenderScopeId, message.LastTimestamp, applyType == SyncRowState.Deleted, false, progress, cancellationToken);

                await this.InterceptAsync(
                    new ExecuteCommandArgs(context, command, dbCommandType, connection, transaction),
                    progress, cancellationToken).ConfigureAwait(false);

                rowAppliedCount = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

                // Check if we have a return value instead
                var syncRowCountParam = syncAdapter.GetParameter(context, command, "sync_row_count");

                // Check if we have an handled error
                var syncErrorText = syncAdapter.GetParameter(context, command, "sync_error_text");

                if (syncRowCountParam != null && syncRowCountParam.Value != null && syncRowCountParam.Value != DBNull.Value)
                    rowAppliedCount = (int)syncRowCountParam.Value;

                if (syncErrorText != null && syncErrorText.Value != null && syncErrorText.Value != DBNull.Value)
                    throw new Exception(syncErrorText.Value.ToString());
            }
            catch (Exception ex)
            {
                var errorMessage = $"{ex.Message}\nCommand Text:{command.CommandText}\nCommand Type:{Enum.GetName(typeof(DbCommandType), dbCommandType)}";
                errorException = new Exception(errorMessage, ex);
            }

            var rowAppliedArgs = new RowsChangesAppliedArgs(context, message.Changes, [batchArgs.SyncRows[0]], schemaChangesTable, applyType, rowAppliedCount, errorException, connection, transaction);
            await this.InterceptAsync(rowAppliedArgs, progress, cancellationToken).ConfigureAwait(false);

            return (rowAppliedCount, null, errorException);
        }

        /// <summary>
        /// Apply a batch of rows.
        /// </summary>
        internal virtual async Task<(int RowAppliedCount, SyncRows Conflicts, Dictionary<SyncRow, ConflictResolution?> RejectedRowResolutions, Exception ErrorException)> InternalApplyBatchRowsAsync(SyncContext context, DbCommand command, List<SyncRow> batchRows, SyncTable schemaChangesTable,
             SyncRowState applyType, MessageApplyChanges message, DbCommandType dbCommandType, DbSyncAdapter syncAdapter, SetupTable setupTable,
             DbConnection connection, DbTransaction transaction, IProgress<ProgressArgs> progress, CancellationToken cancellationToken)
        {
            var conflictRowsTable = schemaChangesTable.Schema.Clone().Tables[schemaChangesTable.TableName, schemaChangesTable.SchemaName];

            // VALIDATION PHASE - runs BEFORE applying and is NOT called during conflict resolution
            var validatingArgs = new RowsChangesValidatingArgs(context, message.Changes, batchRows, schemaChangesTable, applyType, connection, transaction);
            await this.InterceptAsync(validatingArgs, progress, cancellationToken).ConfigureAwait(false);

            // Invoke table-scoped validating interceptors if any are registered
            if (setupTable != null)
            {
                if (setupTable.RowsChangesValidatingInterceptors != null)
                {
                    foreach (var interceptor in setupTable.RowsChangesValidatingInterceptors)
                    {
                        if (interceptor != null)
                            await interceptor.Invoke(validatingArgs).ConfigureAwait(false);
                    }
                }
            }

            // Check if validation was canceled
            if (validatingArgs.Cancel)
                return (-1, null, null, null);

            // Filter out rejected rows before applying phase
            var rejectedRows = validatingArgs.RejectedRows.Keys.ToList();
            var rowsToApply = batchRows.Where(r => !validatingArgs.RejectedRows.ContainsKey(r)).ToList();

            // If all rows were rejected during validation, return with conflicts
            if (rowsToApply.Count == 0)
            {
                // Add all rejected rows to conflicts table
                foreach (var rejectedRow in rejectedRows)
                    conflictRowsTable.Rows.Add(rejectedRow);

                return (0, conflictRowsTable.Rows, validatingArgs.RejectedRows.ToDictionary(kvp => kvp.Key, kvp => kvp.Value), null);
            }

            // APPLYING PHASE - only reached with non-rejected rows
            // Store the original command text to detect if interceptor modified it
            var originalCommandText = command.CommandText;

            var batchArgs = new RowsChangesApplyingArgs(context, message.Changes, rowsToApply, schemaChangesTable, applyType, command, connection, transaction);
            await this.InterceptAsync(batchArgs, progress, cancellationToken).ConfigureAwait(false);

            // Invoke table-scoped applying interceptors if any are registered
            if (setupTable != null)
            {
                if (setupTable.RowsChangesApplyingInterceptors != null)
                {
                    foreach (var interceptor in setupTable.RowsChangesApplyingInterceptors)
                    {
                        if (interceptor != null)
                            await interceptor.Invoke(batchArgs).ConfigureAwait(false);
                    }
                }
            }

            if (batchArgs.Cancel || batchArgs.Command == null || batchArgs.SyncRows == null || batchArgs.SyncRows.Count <= 0)
                return (-1, null, null, null);

            // get the correct pointer to the command from the interceptor in case user change the whole instance
            command = batchArgs.Command;

            await this.InterceptAsync(new ExecuteCommandArgs(context, command, dbCommandType, connection, transaction), progress, cancellationToken).ConfigureAwait(false);

            Exception errorException = null;

            // Check if the command text was modified by the interceptor
            // If so, we need to fall back to single-row execution to respect the modification
            var commandWasModified = !string.Equals(originalCommandText, command.CommandText, StringComparison.Ordinal);

            if (commandWasModified)
            {
                // Fall back to single-row execution using the modified command
                var appliedCount = 0;
                foreach (var row in rowsToApply)
                {
                    try
                    {
                        this.InternalSetCommandParametersValues(context, command, dbCommandType, syncAdapter, connection, transaction,
                            row, message.SenderScopeId, message.LastTimestamp, applyType == SyncRowState.Deleted, false, progress, cancellationToken);

                        var rowCount = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

                        // Check if we have a return value instead
                        var syncRowCountParam = syncAdapter.GetParameter(context, command, "sync_row_count");
                        if (syncRowCountParam != null && syncRowCountParam.Value != null && syncRowCountParam.Value != DBNull.Value)
                            rowCount = (int)syncRowCountParam.Value;

                        if (rowCount > 0)
                            appliedCount++;
                        else
                        {
                            // Row was not applied - treat as conflict
                            var failedRow = new SyncRow(schemaChangesTable, row.RowState);
                            for (int i = 0; i < schemaChangesTable.Columns.Count; i++)
                                failedRow[i] = row[i];
                            conflictRowsTable.Rows.Add(failedRow);
                        }
                    }
                    catch (Exception ex)
                    {
                        errorException = new Exception($"{ex.Message}\nCommand Text:{command.CommandText}\nCommand Type:{Enum.GetName(typeof(DbCommandType), dbCommandType)}", ex);
                        break;
                    }
                }

                // Add rejected rows to conflicts
                foreach (var rejectedRow in rejectedRows)
                    conflictRowsTable.Rows.Add(rejectedRow);

                var rowAppliedArgs = new RowsChangesAppliedArgs(context, message.Changes, batchRows, schemaChangesTable, applyType, appliedCount, errorException, connection, transaction);
                await this.InterceptAsync(rowAppliedArgs, progress, cancellationToken).ConfigureAwait(false);

                return (appliedCount, conflictRowsTable.Rows, validatingArgs.RejectedRows.ToDictionary(kvp => kvp.Key, kvp => kvp.Value), errorException);
            }

            try
            {
                // Execute batch command only with non-rejected rows
                await syncAdapter.ExecuteBatchCommandAsync(context, command, message.SenderScopeId, rowsToApply, schemaChangesTable, conflictRowsTable, message.LastTimestamp, connection, transaction).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                var errorMessage = $"{ex.Message}\nCommand Text:{command.CommandText}\nCommand Type:{Enum.GetName(typeof(DbCommandType), dbCommandType)}";
                errorException = new Exception(errorMessage, ex);
            }

            // Add rejected rows to conflicts
            foreach (var rejectedRow in rejectedRows)
                conflictRowsTable.Rows.Add(rejectedRow);

            var rowAppliedCount = errorException != null ? 0 : rowsToApply.Count - (conflictRowsTable.Rows.Count - rejectedRows.Count);

            var rowAppliedArgs2 = new RowsChangesAppliedArgs(context, message.Changes, batchRows, schemaChangesTable, applyType, rowAppliedCount, errorException, connection, transaction);
            await this.InterceptAsync(rowAppliedArgs2, progress, cancellationToken).ConfigureAwait(false);

            return (rowAppliedCount, conflictRowsTable.Rows, validatingArgs.RejectedRows.ToDictionary(kvp => kvp.Key, kvp => kvp.Value), errorException);
        }

        /// <summary>
        /// Apply conflicts and errors.
        /// </summary>
        internal virtual async Task<(int AppliedRows, int ConflictsResolvedCount, int FailedRows, ApplyChangesException FailureException)> InternalApplyConflictsAndErrorsAsync(ScopeInfo scopeInfo, SyncContext context,
            SyncTable schemaChangesTable, SyncRowState applyType, SyncTable errorsTable,
            List<SyncRow> conflictRows, Dictionary<SyncRow, ConflictResolution?> rejectedRowResolutions, List<(SyncRow SyncRow, Exception Exception)> errorsRows, MessageApplyChanges message, DbConnection connection, DbTransaction transaction,
            IProgress<ProgressArgs> progress, CancellationToken cancellationToken)
        {

            var conflictsResolvedCount = 0;
            var appliedRows = 0;
            var failedRows = 0;
            ApplyChangesException failureException = null;

            // Track successfully applied rows for the interceptor
            var successfullyAppliedRows = new List<SyncRow>();

            if ((conflictRows != null && conflictRows.Count > 0) || (errorsRows != null && errorsRows.Count > 0))
            {

                using var runnerError = await this.GetConnectionAsync(context, this.Options.TransactionMode == TransactionMode.None ? SyncMode.NoTransaction : SyncMode.WithTransaction, SyncStage.ChangesApplying, connection, transaction, progress, cancellationToken).ConfigureAwait(false);
                await using (runnerError.ConfigureAwait(false))
                {
                    // Disable check constraints for provider supporting only at table level
                    if (this.Options.DisableConstraintsOnApplyChanges && this.Provider.ConstraintsLevelAction == ConstraintsLevelAction.OnTableLevel)
                        await this.InternalDisableConstraintsAsync(scopeInfo, context, schemaChangesTable, runnerError.Connection, runnerError.Transaction, runnerError.Progress, runnerError.CancellationToken).ConfigureAwait(false);

                    // Determine if adapter supports batch operations for conflict resolution
                    var syncAdapter = this.GetSyncAdapter(schemaChangesTable, scopeInfo);
                    var useBatchConflictApply = syncAdapter.UseBulkOperations;

                    // Pre-fetch all local conflict rows in batch (if supported by adapter)
                    Dictionary<string, SyncRow> conflictRowsCache = null;
                    if (conflictRows != null && conflictRows.Count > 0 && useBatchConflictApply)
                    {
                        conflictRowsCache = await syncAdapter.GetConflictRowsBatchAsync(
                            context, conflictRows, schemaChangesTable,
                            runnerError.Connection, runnerError.Transaction).ConfigureAwait(false);
                    }

                    // Deferred batch lists for resolved conflicts
                    List<(SyncRow Row, Guid? SenderScopeId, bool IsDelete)> deferredResolvedRows = null;
                    if (useBatchConflictApply)
                        deferredResolvedRows = new List<(SyncRow, Guid?, bool)>();

                    // If conflicts occured
                    foreach (var conflictRow in conflictRows)
                    {
                        this.Logger.LogInformation("[InternalApplyTableChangesAsync]. Handle {ConflictsCount} conflicts", conflictRows.Count);

                        // Check if this row has a specified conflict resolution
                        ConflictResolution? specifiedResolution = null;
                        if (rejectedRowResolutions != null && rejectedRowResolutions.ContainsKey(conflictRow))
                            specifiedResolution = rejectedRowResolutions[conflictRow];

                        var (isApplied, isConflictResolved, exception, deferredRow, deferredSenderScopeId, deferredIsDelete) =
                            await this.HandleConflictAsync(scopeInfo, context, message.Changes, message.LocalScopeId, message.SenderScopeId, conflictRow, schemaChangesTable,
                                                           message.Policy, specifiedResolution, message.LastTimestamp,
                                                           runnerError.Connection, runnerError.Transaction, runnerError.Progress, runnerError.CancellationToken,
                                                           conflictRowsCache, deferApply: useBatchConflictApply).ConfigureAwait(false);

                        if (exception != null)
                        {
                            errorsRows.Add((conflictRow, exception));
                        }
                        else if (deferredRow != null)
                        {
                            // Row was resolved but deferred for batch apply
                            deferredResolvedRows.Add((deferredRow, deferredSenderScopeId, deferredIsDelete));
                            conflictsResolvedCount++;
                        }
                        else
                        {
                            conflictsResolvedCount += isConflictResolved ? 1 : 0;
                            appliedRows += isApplied ? 1 : 0;

                            // Track successfully applied row
                            if (isApplied)
                                successfullyAppliedRows.Add(conflictRow);
                        }
                    }

                    // Batch-apply deferred resolved conflicts
                    if (deferredResolvedRows != null && deferredResolvedRows.Count > 0)
                    {
                        var batchApplied = await syncAdapter.ApplyResolvedConflictsBatchAsync(
                            context, deferredResolvedRows, schemaChangesTable, message.LastTimestamp,
                            runnerError.Connection, runnerError.Transaction).ConfigureAwait(false);

                        if (batchApplied >= 0)
                        {
                            // Batch apply succeeded
                            appliedRows += batchApplied;
                            foreach (var dr in deferredResolvedRows)
                                successfullyAppliedRows.Add(dr.Row);
                        }
                        else
                        {
                            // Batch not supported at runtime, fall back to individual apply
                            foreach (var dr in deferredResolvedRows)
                            {
                                bool opComplete;
                                Exception opException;

                                if (dr.IsDelete)
                                {
                                    (_, opComplete, opException) = await this.InternalApplyDeleteAsync(scopeInfo, context, message.Changes,
                                        dr.Row, schemaChangesTable, message.LastTimestamp, dr.SenderScopeId, true,
                                        runnerError.Connection, runnerError.Transaction, runnerError.Progress, runnerError.CancellationToken).ConfigureAwait(false);
                                }
                                else
                                {
                                    (_, opComplete, opException) = await this.InternalApplyUpdateAsync(scopeInfo, context, message.Changes,
                                        dr.Row, schemaChangesTable, message.LastTimestamp, dr.SenderScopeId, true,
                                        runnerError.Connection, runnerError.Transaction, runnerError.Progress, runnerError.CancellationToken).ConfigureAwait(false);
                                }

                                if (opException != null)
                                    errorsRows.Add((dr.Row, opException));
                                else if (opComplete)
                                {
                                    appliedRows++;
                                    successfullyAppliedRows.Add(dr.Row);
                                }
                            }
                        }
                    }

                    // If errors occured
                    var shouldRollbackTransaction = false;
                    foreach (var errorRow in errorsRows)
                    {
                        this.Logger.LogInformation("[InternalApplyTableChangesAsync]. Handle {ErrorsRowsCount} errors", errorsRows.Count);

                        if (errorRow.SyncRow.RowState is SyncRowState.ApplyModifiedFailed or SyncRowState.Modified or SyncRowState.RetryModifiedOnNextSync
                            && applyType == SyncRowState.Deleted)
                            continue;

                        if (errorRow.SyncRow.RowState is SyncRowState.ApplyDeletedFailed or SyncRowState.Deleted or SyncRowState.RetryDeletedOnNextSync
                            && applyType == SyncRowState.Modified)
                            continue;

                        ErrorAction errorAction;
                        (errorAction, failureException) = await this.HandleErrorAsync(
                                            scopeInfo, context, message.Changes, errorRow.SyncRow, applyType, schemaChangesTable,
                                            errorRow.Exception, message.SenderScopeId, message.LastTimestamp,
                                            runnerError.Connection, runnerError.Transaction, runnerError.Progress, runnerError.CancellationToken).ConfigureAwait(false);

                        // check if we have already the row in errorsTable
                        var existingRow = SyncRows.GetRowByPrimaryKeys(errorRow.SyncRow, errorsTable.Rows, errorsTable);

                        if (existingRow != null)
                            errorsTable.Rows.Remove(existingRow);

                        // User decides error should be logged
                        if (errorAction != ErrorAction.Resolved)
                            errorsTable.Rows.Add(errorRow.SyncRow);

                        // final throw if any error coming back from HandleErrorAsync
                        if (errorAction != ErrorAction.Ignore)
                        {
                            if (errorAction == ErrorAction.Throw)
                            {
                                failedRows++;
                                shouldRollbackTransaction = true;

                                // Break because a critical error has been raised and we don't want to continue
                                break;
                            }
                            else
                            {
                                failedRows += errorAction == ErrorAction.Log ? 1 : 0;

                                // Track resolved error as applied row
                                if (errorAction == ErrorAction.Resolved)
                                {
                                    appliedRows++;
                                    successfullyAppliedRows.Add(errorRow.SyncRow);
                                }
                            }
                        }
                    }

                    // Enable check constraints for provider supporting only at table level
                    if (this.Options.DisableConstraintsOnApplyChanges && this.Provider.ConstraintsLevelAction == ConstraintsLevelAction.OnTableLevel)
                        await this.InternalEnableConstraintsAsync(scopeInfo, context, schemaChangesTable, runnerError.Connection, runnerError.Transaction, runnerError.Progress, runnerError.CancellationToken).ConfigureAwait(false);

                    // Call the RowsChangesApplied interceptor if we have successfully applied rows
                    if (successfullyAppliedRows.Count > 0)
                    {
                        var rowsAppliedArgs = new RowsChangesAppliedArgs(context, message.Changes, successfullyAppliedRows, schemaChangesTable, applyType, successfullyAppliedRows.Count, null, runnerError.Connection, runnerError.Transaction);
                        await this.InterceptAsync(rowsAppliedArgs, runnerError.Progress, runnerError.CancellationToken).ConfigureAwait(false);
                    }

                    if (shouldRollbackTransaction)
                        await runnerError.RollbackAsync($"Rollback because we can't resolve errors. Failure:{failureException?.Message}").ConfigureAwait(false);
                    else
                        await runnerError.CommitAsync().ConfigureAwait(false);
                }
            }

            return (appliedRows, conflictsResolvedCount, failedRows, failureException);
        }

        /// <summary>
        /// Internal method to apply clean errors.
        /// </summary>
        internal virtual async Task InternalApplyCleanErrorsAsync(ScopeInfo scopeInfo, SyncContext context,
                         BatchInfo lastSyncErrorsBatchInfo, MessageApplyChanges message, DbConnection connection, DbTransaction transaction,
                         IProgress<ProgressArgs> progress, CancellationToken cancellationToken)
        {
            if (lastSyncErrorsBatchInfo == null)
                return;

            // Access to the unified batch cache from the context (may be null if not in scope)
            var unifiedBatchCache = context.UnifiedBatchCache;

            LocalJsonSerializer localSerializerReader = null;

            LocalJsonSerializer localSerializerWriter = null;

            try
            {
                context.SyncStage = SyncStage.ChangesApplying;

                var schemaTables = message.Schema.Tables.SortByDependencies(tab => tab.GetRelations().Select(r => r.GetParentTable()));

                this.Logger.LogInformation("[InternalApplyCleanErrorsAsync]. Directory name {DirectoryName}. BatchParts count {BatchPartsInfoCount}", lastSyncErrorsBatchInfo.DirectoryName, lastSyncErrorsBatchInfo.BatchPartsInfo.Count);

                foreach (var schemaTable in schemaTables)
                {
                    var tableChangesApplied = message.ChangesApplied?.TableChangesApplied?.FirstOrDefault(tca =>
                    {
                        var sc = SyncGlobalization.DataSourceStringComparison;

                        var sn = tca.SchemaName ?? string.Empty;
                        var otherSn = schemaTable.SchemaName ?? string.Empty;

                        return tca.TableName.Equals(schemaTable.TableName, sc) &&
                                sn.Equals(otherSn, sc);
                    });

                    // tmp sync table with only writable columns
                    var changesSet = schemaTable.Schema.Clone(false);
                    var schemaChangesTable = CreateChangesTable(schemaTable, changesSet);

                    // get bpi from changes to be isApplied
                    var bpiTables = message.Changes.GetBatchPartsInfos(schemaTable)?.ToList();

                    if (bpiTables == null || bpiTables.Count == 0)
                        continue;

                    var tableBpis = lastSyncErrorsBatchInfo.GetBatchPartsInfos(schemaTable)?.ToList();

                    if (tableBpis == null || tableBpis.Count == 0)
                        continue;

                    // Load in memory failed rows for this table
                    var failedRows = new List<SyncRow>();

                    // Read already present lines
                    var lastSyncErrorsBpiFullPath = lastSyncErrorsBatchInfo.GetBatchPartInfoFullPath(tableBpis.ToList()[0]);
                    var lastSyncErrorsDirectoryPath = Path.GetDirectoryName(lastSyncErrorsBpiFullPath);
                    var lastSyncErrorsFileName = Path.GetFileName(lastSyncErrorsBpiFullPath);

                    await using (var localFailedRowsSerializerReader = new LocalJsonSerializer(this.BatchStorage, this, context))
                    {
                        var syncRows = await localFailedRowsSerializerReader.GetRowsFromFileAsync(lastSyncErrorsDirectoryPath, lastSyncErrorsFileName, schemaChangesTable, cancellationToken).ConfigureAwait(false);
                        failedRows.AddRange(syncRows);
                    }

                    localSerializerReader = new LocalJsonSerializer(this.BatchStorage, this, context);

                    localSerializerWriter = new LocalJsonSerializer(this.BatchStorage, this, context);

                    // Open again the same file
                    await localSerializerWriter.OpenFileAsync(lastSyncErrorsDirectoryPath, lastSyncErrorsFileName, schemaChangesTable, SyncRowState.None).ConfigureAwait(false);

                    foreach (var batchPartInfo in bpiTables)
                    {
                        // Get full path of my batchpartinfo
                        var fullPath = message.Changes.GetBatchPartInfoFullPath(batchPartInfo);
                        var batchDirectoryPath = Path.GetDirectoryName(fullPath);
                        var batchFileName = Path.GetFileName(fullPath);

                        // Check if this is a unified batch file - for error recovery we need all operations
                        IEnumerable<SyncRow> rowsEnumerable;
                        if (batchPartInfo.TableName == "UNIFIED")
                        {
                            rowsEnumerable = await this.GetAllRowsFromUnifiedBatchFileAsync(fullPath, schemaChangesTable, unifiedBatchCache, cancellationToken).ConfigureAwait(false);
                        }
                        else
                        {
                            rowsEnumerable = await localSerializerReader.GetRowsFromFileAsync(batchDirectoryPath, batchFileName, schemaChangesTable, cancellationToken).ConfigureAwait(false);
                        }

                        foreach (var syncRow in rowsEnumerable)
                        {
                            var rowIsInBatch = SyncRows.GetRowByPrimaryKeys(syncRow, failedRows, schemaTable);

                            // we found the row in the batch, that means the failed row is currently in a progress of being updated
                            // we can remove it from failedRowsTable
                            if (rowIsInBatch != null)
                            {
                                failedRows.Remove(rowIsInBatch);

                                if (tableChangesApplied != null && tableChangesApplied.Failed > 0)
                                    tableChangesApplied.Failed--;
                            }

                            if (failedRows.Count <= 0)
                                break;
                        }

                        if (failedRows.Count <= 0)
                            break;
                    }

                    foreach (var row in failedRows)
                        await localSerializerWriter.WriteRowToFileAsync(row, schemaChangesTable).ConfigureAwait(false);

                    if (failedRows.Count <= 0)
                    {
                        var directoryPath = Path.GetDirectoryName(lastSyncErrorsBpiFullPath);
                        var fileName = Path.GetFileName(lastSyncErrorsBpiFullPath);
                        var fileExists = await this.BatchStorage.FileExistsAsync(directoryPath, fileName, cancellationToken).ConfigureAwait(false);

                        if (fileExists)
                        {
                            if (localSerializerWriter.IsOpen)
                                await localSerializerWriter.CloseFileAsync().ConfigureAwait(false);

                            await this.BatchStorage.DeleteBatchPartAsync(directoryPath, fileName, cancellationToken).ConfigureAwait(false);
                        }
                    }

                    this.Logger.LogInformation("[InternalApplyCleanErrorsAsync]. schemaTable {SchemaTableName} failedRows count {FailedRowsCount}", schemaTable.GetFullName(), failedRows.Count);
                }
            }
            catch (Exception ex)
            {
                throw this.GetSyncError(context, ex);
            }
            finally
            {
                if (localSerializerWriter != null)
                {
                    await localSerializerWriter.DisposeAsync().ConfigureAwait(false);
                }

                if (localSerializerReader != null)
                {
                    await localSerializerReader.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
    }
}