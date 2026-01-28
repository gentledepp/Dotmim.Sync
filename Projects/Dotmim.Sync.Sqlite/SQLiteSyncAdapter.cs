using Wormhole.Sync.Builders;
using Wormhole.Sync.DatabaseStringParsers;
using Wormhole.Sync.Enumerations;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Wormhole.Sync.Sqlite
{
    /// <summary>
    /// Sqlite sync adapter.
    /// </summary>
    public class SqliteSyncAdapter : DbSyncAdapter
    {
        private bool disableSqlFiltersGeneration;

        /// <inheritdoc />
        public override bool SupportsOutputParameters => false;

        /// <summary>
        /// Gets or sets the SqliteObjectNames.
        /// </summary>
        public SqliteObjectNames SqliteObjectNames { get; set; }

        /// <inheritdoc cref="SqliteSyncAdapter"/>
        public SqliteSyncAdapter(SyncTable tableDescription, ScopeInfo scopeInfo, bool disableSqlFiltersGeneration, bool useBulkOperations = false)
            : base(tableDescription, scopeInfo, useBulkOperations)
        {
            this.SqliteObjectNames = new SqliteObjectNames(this.TableDescription, scopeInfo, disableSqlFiltersGeneration);
            this.disableSqlFiltersGeneration = disableSqlFiltersGeneration;
        }

        /// <inheritdoc />
        public override DbColumnNames GetParsedColumnNames(string name)
        {
            var columnParser = new ObjectParser(name, SqliteObjectNames.LeftQuote, SqliteObjectNames.RightQuote);
            return new DbColumnNames(columnParser.QuotedShortName, columnParser.NormalizedShortName);
        }

        /// <inheritdoc />
        public override DbTableBuilder GetTableBuilder() => new SqliteTableBuilder(this.TableDescription, this.ScopeInfo, this.disableSqlFiltersGeneration);

        /// <inheritdoc />
        public override (DbCommand, bool) GetCommand(SyncContext context, DbCommandType commandType, SyncFilter filter = null)
        {
            var command = new SqliteCommand();
            string text;
            text = this.SqliteObjectNames.GetCommandName(commandType, filter);

            // on Sqlite, everything is text :)
            command.CommandType = CommandType.Text;
            command.CommandText = text;

            bool isBatch = this.UseBulkOperations && commandType is
                DbCommandType.InsertRows or DbCommandType.UpdateRows or DbCommandType.DeleteRows;

            return (command, isBatch);
        }

        /// <inheritdoc />
        public override void AddCommandParameterValue(SyncContext context, DbParameter parameter, object value, DbCommand command, DbCommandType commandType)
            => parameter.Value = value == null || value == DBNull.Value ? DBNull.Value : SyncTypeConverter.TryConvertFromDbType(value, parameter.DbType);

        /// <inheritdoc />
        public override DbCommand EnsureCommandParametersValues(SyncContext context, DbCommand command, DbCommandType commandType, DbConnection connection, DbTransaction transaction)
            => command;

        /// <inheritdoc />
        public override async Task ExecuteBatchCommandAsync(SyncContext context, DbCommand cmd, Guid senderScopeId, IEnumerable<SyncRow> arrayItems, SyncTable schemaChangesTable, SyncTable failedRows, long? lastTimestamp, DbConnection connection, DbTransaction transaction = null)
        {
            var items = arrayItems?.ToList();
            if (items == null || items.Count <= 0)
                return;

            var firstRow = items[0];

            // Determine operation type from the row state and command text
            bool isDelete = firstRow.RowState == SyncRowState.Deleted || firstRow.RowState == SyncRowState.RetryDeletedOnNextSync;
            bool isInsert = !isDelete && cmd.CommandText.StartsWith("INSERT OR REPLACE", StringComparison.OrdinalIgnoreCase);

            if (isInsert)
                await ExecuteBatchInsertAsync(context, cmd, senderScopeId, items, schemaChangesTable, failedRows, lastTimestamp, connection, transaction).ConfigureAwait(false);
            else if (isDelete)
                await ExecuteBatchDeleteAsync(context, senderScopeId, items, schemaChangesTable, failedRows, lastTimestamp, connection, transaction).ConfigureAwait(false);
            else
                await ExecuteBatchUpdateAsync(context, senderScopeId, items, schemaChangesTable, failedRows, lastTimestamp, connection, transaction).ConfigureAwait(false);
        }

        // ──────────────────────────────────────────────
        //  Batch INSERT (initial sync – no conflict check)
        // ──────────────────────────────────────────────

        private async Task ExecuteBatchInsertAsync(SyncContext context, DbCommand cmd, Guid senderScopeId, List<SyncRow> items, SyncTable schemaChangesTable, SyncTable failedRows, long? lastTimestamp, DbConnection connection, DbTransaction transaction)
        {
            var mutableColumns = schemaChangesTable.GetMutableColumns(false, true).ToList();
            var primaryKeyColumns = schemaChangesTable.GetPrimaryKeysColumns().ToList();
            int columnsCount = mutableColumns.Count;

            // Stay well within SQLite's SQLITE_MAX_VARIABLE_NUMBER (999 in older builds, 32766 in modern)
            int subBatchSize = Math.Max(1, 900 / columnsCount);

            for (int offset = 0; offset < items.Count; offset += subBatchSize)
            {
                int count = Math.Min(subBatchSize, items.Count - offset);

                // Build and execute multi-row data INSERT
                await ExecuteMultiRowInsertAsync(mutableColumns, items, offset, count, schemaChangesTable, connection, transaction).ConfigureAwait(false);

                // Build and execute multi-row tracking INSERT OR REPLACE
                await ExecuteMultiRowTrackingInsertAsync(primaryKeyColumns, schemaChangesTable, items, offset, count, senderScopeId, false, connection, transaction).ConfigureAwait(false);
            }
        }

        // ──────────────────────────────────────────────
        //  Batch UPDATE (incremental sync – with conflict check)
        // ──────────────────────────────────────────────

        private async Task ExecuteBatchUpdateAsync(SyncContext context, Guid senderScopeId, List<SyncRow> items, SyncTable schemaChangesTable, SyncTable failedRows, long? lastTimestamp, DbConnection connection, DbTransaction transaction)
        {
            var mutableColumns = schemaChangesTable.GetMutableColumns(false, true).ToList();
            var updateColumns = schemaChangesTable.GetMutableColumns(false, false).ToList();
            var primaryKeyColumns = schemaChangesTable.GetPrimaryKeysColumns().ToList();
            int pkCount = primaryKeyColumns.Count;

            // Sub-batch sized by the most constraining operation (data UPSERT uses all columns)
            int subBatchSize = Math.Max(1, 900 / mutableColumns.Count);

            bool forceWrite = context.SyncType == SyncType.Reinitialize || context.SyncType == SyncType.ReinitializeWithUpload;

            for (int offset = 0; offset < items.Count; offset += subBatchSize)
            {
                int count = Math.Min(subBatchSize, items.Count - offset);
                var subBatch = items.GetRange(offset, count);

                // Step 1: Find conflict PKs
                var conflictPks = await FindUpdateConflictPrimaryKeysAsync(
                    primaryKeyColumns, subBatch, schemaChangesTable,
                    senderScopeId, lastTimestamp, forceWrite,
                    connection, transaction).ConfigureAwait(false);

                // Step 2: Separate conflict rows from applicable rows
                List<SyncRow> rowsToApply;
                if (conflictPks.Count > 0)
                {
                    rowsToApply = new List<SyncRow>(count);
                    foreach (var row in subBatch)
                    {
                        var key = BuildPrimaryKeyString(row, primaryKeyColumns, schemaChangesTable);
                        if (conflictPks.Contains(key))
                        {
                            var failedRow = new SyncRow(schemaChangesTable, row.RowState);
                            for (int i = 0; i < schemaChangesTable.Columns.Count; i++)
                                failedRow[i] = row[i];
                            failedRows.Rows.Add(failedRow);
                        }
                        else
                        {
                            rowsToApply.Add(row);
                        }
                    }
                }
                else
                {
                    rowsToApply = subBatch;
                }

                // Step 3: Batch UPSERT non-conflict rows + tracking
                if (rowsToApply.Count > 0)
                {
                    await ExecuteMultiRowUpsertAsync(mutableColumns, updateColumns, primaryKeyColumns, rowsToApply, 0, rowsToApply.Count, schemaChangesTable, connection, transaction).ConfigureAwait(false);

                    await ExecuteMultiRowTrackingInsertAsync(primaryKeyColumns, schemaChangesTable, rowsToApply, 0, rowsToApply.Count, senderScopeId, false, connection, transaction).ConfigureAwait(false);
                }
            }
        }

        private async Task<HashSet<string>> FindUpdateConflictPrimaryKeysAsync(
            List<SyncColumn> primaryKeyColumns, List<SyncRow> rows, SyncTable schemaChangesTable,
            Guid senderScopeId, long? lastTimestamp, bool forceWrite,
            DbConnection connection, DbTransaction transaction)
        {
            // If force_write, there are never conflicts
            if (forceWrite)
                return new HashSet<string>();

            var quotedPkNames = GetQuotedColumnNames(primaryKeyColumns);

            using var cmd = new SqliteCommand();
            cmd.Connection = (SqliteConnection)connection;
            cmd.Transaction = (SqliteTransaction)transaction;

#pragma warning disable CA2100
            cmd.CommandText = this.SqliteObjectNames.CreateFindUpdateConflictsCommand(rows.Count, quotedPkNames.ToArray());
#pragma warning restore CA2100

            // Add PK parameters using same naming convention as CreateFindUpdateConflictsCommand: @pk{r}_{k}
            for (int r = 0; r < rows.Count; r++)
            {
                for (int k = 0; k < primaryKeyColumns.Count; k++)
                {
                    var pkCol = primaryKeyColumns[k];
                    var columnIndex = schemaChangesTable.Columns.IndexOf(pkCol);
                    var p = cmd.CreateParameter();
                    p.ParameterName = $"@pk{r}_{k}";
                    p.DbType = pkCol.GetDbType();
                    object value = rows[r][columnIndex] ?? DBNull.Value;
                    p.Value = value == DBNull.Value ? DBNull.Value : SyncTypeConverter.TryConvertFromDbType(value, p.DbType);
                    cmd.Parameters.Add(p);
                }
            }

            var scopeParam = cmd.CreateParameter();
            scopeParam.ParameterName = "@sync_scope_id";
            scopeParam.DbType = DbType.String;
            scopeParam.Value = senderScopeId.ToString();
            cmd.Parameters.Add(scopeParam);

            var tsParam = cmd.CreateParameter();
            tsParam.ParameterName = "@sync_min_timestamp";
            tsParam.DbType = DbType.Int64;
            tsParam.Value = lastTimestamp.HasValue ? (object)lastTimestamp.Value : DBNull.Value;
            cmd.Parameters.Add(tsParam);

            var conflictKeys = new HashSet<string>();
            using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                var keyParts = new object[primaryKeyColumns.Count];
                for (int k = 0; k < primaryKeyColumns.Count; k++)
                    keyParts[k] = reader.GetValue(k);
                conflictKeys.Add(BuildPrimaryKeyString(keyParts));
            }

            return conflictKeys;
        }

        // ──────────────────────────────────────────────
        //  Batch DELETE (incremental sync – with conflict check)
        // ──────────────────────────────────────────────

        private async Task ExecuteBatchDeleteAsync(SyncContext context, Guid senderScopeId, List<SyncRow> items, SyncTable schemaChangesTable, SyncTable failedRows, long? lastTimestamp, DbConnection connection, DbTransaction transaction)
        {
            var primaryKeyColumns = schemaChangesTable.GetPrimaryKeysColumns().ToList();
            int pkCount = primaryKeyColumns.Count;

            // Sub-batch sized by PK parameters only (delete doesn't need all columns)
            // Reserve 3 params for scope_id, timestamp, force_write
            int subBatchSize = Math.Max(1, 900 / pkCount);

            bool forceWrite = context.SyncType == SyncType.Reinitialize || context.SyncType == SyncType.ReinitializeWithUpload;

            for (int offset = 0; offset < items.Count; offset += subBatchSize)
            {
                int count = Math.Min(subBatchSize, items.Count - offset);
                var subBatch = items.GetRange(offset, count);

                // Step 1: Find conflict PKs (includes NULLC rows - no tracking entry)
                var conflictPks = await FindDeleteConflictPrimaryKeysAsync(
                    primaryKeyColumns, subBatch, schemaChangesTable,
                    senderScopeId, lastTimestamp, forceWrite,
                    connection, transaction).ConfigureAwait(false);

                // Step 2: Separate conflict rows from applicable rows
                List<SyncRow> rowsToApply;
                if (conflictPks.Count > 0)
                {
                    rowsToApply = new List<SyncRow>(count);
                    foreach (var row in subBatch)
                    {
                        var key = BuildPrimaryKeyString(row, primaryKeyColumns, schemaChangesTable);
                        if (conflictPks.Contains(key))
                        {
                            var failedRow = new SyncRow(schemaChangesTable, row.RowState);
                            for (int i = 0; i < schemaChangesTable.Columns.Count; i++)
                                failedRow[i] = row[i];
                            failedRows.Rows.Add(failedRow);
                        }
                        else
                        {
                            rowsToApply.Add(row);
                        }
                    }
                }
                else
                {
                    rowsToApply = subBatch;
                }

                // Step 3: Batch DELETE non-conflict rows + tracking
                if (rowsToApply.Count > 0)
                {
                    await ExecuteMultiRowDeleteAsync(primaryKeyColumns, rowsToApply, schemaChangesTable, connection, transaction).ConfigureAwait(false);

                    await ExecuteMultiRowTrackingInsertAsync(primaryKeyColumns, schemaChangesTable, rowsToApply, 0, rowsToApply.Count, senderScopeId, true, connection, transaction).ConfigureAwait(false);
                }
            }
        }

        private async Task<HashSet<string>> FindDeleteConflictPrimaryKeysAsync(
            List<SyncColumn> primaryKeyColumns, List<SyncRow> rows, SyncTable schemaChangesTable,
            Guid senderScopeId, long? lastTimestamp, bool forceWrite,
            DbConnection connection, DbTransaction transaction)
        {
            // If force_write, there are never conflicts
            if (forceWrite)
                return new HashSet<string>();

            var quotedPkNames = GetQuotedColumnNames(primaryKeyColumns);

            using var cmd = new SqliteCommand();
            cmd.Connection = (SqliteConnection)connection;
            cmd.Transaction = (SqliteTransaction)transaction;

#pragma warning disable CA2100
            cmd.CommandText = this.SqliteObjectNames.CreateFindDeleteConflictsCommand(rows.Count, quotedPkNames.ToArray());
#pragma warning restore CA2100

            // Add PK parameters using same naming convention as CreateFindDeleteConflictsCommand: @pk{r}_{k}
            for (int r = 0; r < rows.Count; r++)
            {
                for (int k = 0; k < primaryKeyColumns.Count; k++)
                {
                    var pkCol = primaryKeyColumns[k];
                    var columnIndex = schemaChangesTable.Columns.IndexOf(pkCol);
                    var p = cmd.CreateParameter();
                    p.ParameterName = $"@pk{r}_{k}";
                    p.DbType = pkCol.GetDbType();
                    object value = rows[r][columnIndex] ?? DBNull.Value;
                    p.Value = value == DBNull.Value ? DBNull.Value : SyncTypeConverter.TryConvertFromDbType(value, p.DbType);
                    cmd.Parameters.Add(p);
                }
            }

            var scopeParam = cmd.CreateParameter();
            scopeParam.ParameterName = "@sync_scope_id";
            scopeParam.DbType = DbType.String;
            scopeParam.Value = senderScopeId.ToString();
            cmd.Parameters.Add(scopeParam);

            var tsParam = cmd.CreateParameter();
            tsParam.ParameterName = "@sync_min_timestamp";
            tsParam.DbType = DbType.Int64;
            tsParam.Value = lastTimestamp.HasValue ? (object)lastTimestamp.Value : DBNull.Value;
            cmd.Parameters.Add(tsParam);

            var conflictKeys = new HashSet<string>();
            using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                var keyParts = new object[primaryKeyColumns.Count];
                for (int k = 0; k < primaryKeyColumns.Count; k++)
                    keyParts[k] = reader.GetValue(k);
                conflictKeys.Add(BuildPrimaryKeyString(keyParts));
            }

            return conflictKeys;
        }

        // ──────────────────────────────────────────────
        //  Multi-row SQL helpers
        // ──────────────────────────────────────────────

        private async Task ExecuteMultiRowInsertAsync(List<SyncColumn> mutableColumns, List<SyncRow> items, int offset, int count, SyncTable schemaChangesTable, DbConnection connection, DbTransaction transaction)
        {
            // Pre-compute quoted column names (ObjectParser is a ref struct, cannot be used in async methods)
            var quotedColumnNames = GetQuotedColumnNames(mutableColumns);

            using var batchCmd = new SqliteCommand();
            batchCmd.Connection = (SqliteConnection)connection;
            batchCmd.Transaction = (SqliteTransaction)transaction;

#pragma warning disable CA2100
            batchCmd.CommandText = this.SqliteObjectNames.CreateMultiRowInsertCommand(count, quotedColumnNames.ToArray());
#pragma warning restore CA2100

            // Add parameters using same naming convention as CreateMultiRowInsertCommand: @p{r}_{c}
            for (int r = 0; r < count; r++)
            {
                for (int c = 0; c < mutableColumns.Count; c++)
                {
                    var col = mutableColumns[c];
                    var row = items[offset + r];
                    var columnIndex = schemaChangesTable.Columns.IndexOf(col);

                    var p = batchCmd.CreateParameter();
                    p.ParameterName = $"@p{r}_{c}";
                    p.DbType = col.GetDbType();
                    object value = row[columnIndex] ?? DBNull.Value;
                    p.Value = value == DBNull.Value ? DBNull.Value : SyncTypeConverter.TryConvertFromDbType(value, p.DbType);
                    batchCmd.Parameters.Add(p);
                }
            }

            await batchCmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        private async Task ExecuteMultiRowUpsertAsync(List<SyncColumn> mutableColumns, List<SyncColumn> updateColumns, List<SyncColumn> primaryKeyColumns, List<SyncRow> items, int offset, int count, SyncTable schemaChangesTable, DbConnection connection, DbTransaction transaction)
        {
            var quotedColumnNames = GetQuotedColumnNames(mutableColumns);
            var quotedPkNames = GetQuotedColumnNames(primaryKeyColumns);
            var quotedUpdateNames = GetQuotedColumnNames(updateColumns);

            using var batchCmd = new SqliteCommand();
            batchCmd.Connection = (SqliteConnection)connection;
            batchCmd.Transaction = (SqliteTransaction)transaction;

#pragma warning disable CA2100
            batchCmd.CommandText = this.SqliteObjectNames.CreateMultiRowUpsertCommand(
                count, quotedColumnNames.ToArray(), quotedPkNames.ToArray(), quotedUpdateNames.ToArray());
#pragma warning restore CA2100

            // Add parameters using same naming convention as CreateMultiRowUpsertCommand: @p{r}_{c}
            for (int r = 0; r < count; r++)
            {
                for (int c = 0; c < mutableColumns.Count; c++)
                {
                    var col = mutableColumns[c];
                    var row = items[offset + r];
                    var columnIndex = schemaChangesTable.Columns.IndexOf(col);

                    var p = batchCmd.CreateParameter();
                    p.ParameterName = $"@p{r}_{c}";
                    p.DbType = col.GetDbType();
                    object value = row[columnIndex] ?? DBNull.Value;
                    p.Value = value == DBNull.Value ? DBNull.Value : SyncTypeConverter.TryConvertFromDbType(value, p.DbType);
                    batchCmd.Parameters.Add(p);
                }
            }

            await batchCmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        private async Task ExecuteMultiRowDeleteAsync(List<SyncColumn> primaryKeyColumns, List<SyncRow> rows, SyncTable schemaChangesTable, DbConnection connection, DbTransaction transaction)
        {
            var quotedPkNames = GetQuotedColumnNames(primaryKeyColumns);

            using var cmd = new SqliteCommand();
            cmd.Connection = (SqliteConnection)connection;
            cmd.Transaction = (SqliteTransaction)transaction;

#pragma warning disable CA2100
            cmd.CommandText = this.SqliteObjectNames.CreateMultiRowDeleteCommand(rows.Count, quotedPkNames.ToArray());
#pragma warning restore CA2100

            // Add parameters using same naming convention as CreateMultiRowDeleteCommand: @pk{r}_{k}
            for (int r = 0; r < rows.Count; r++)
            {
                for (int k = 0; k < primaryKeyColumns.Count; k++)
                {
                    var pkCol = primaryKeyColumns[k];
                    var columnIndex = schemaChangesTable.Columns.IndexOf(pkCol);
                    var p = cmd.CreateParameter();
                    p.ParameterName = $"@pk{r}_{k}";
                    p.DbType = pkCol.GetDbType();
                    object value = rows[r][columnIndex] ?? DBNull.Value;
                    p.Value = value == DBNull.Value ? DBNull.Value : SyncTypeConverter.TryConvertFromDbType(value, p.DbType);
                    cmd.Parameters.Add(p);
                }
            }

            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        private async Task ExecuteMultiRowTrackingInsertAsync(List<SyncColumn> primaryKeyColumns, SyncTable schemaChangesTable, List<SyncRow> items, int offset, int count, Guid? senderScopeId, bool isTombstone, DbConnection connection, DbTransaction transaction)
        {
            // Pre-compute quoted column names (ObjectParser is a ref struct, cannot be used in async methods)
            var quotedPkNames = GetQuotedColumnNames(primaryKeyColumns);

            // Build tracked columns list
            var trackedColumns = new List<SyncColumn>();
            var quotedTrackedNames = new List<string>();
            if (!isTombstone && schemaChangesTable.TrackedColumns != null && schemaChangesTable.TrackedColumns.Count > 0)
            {
                foreach (var trackedColumnName in schemaChangesTable.TrackedColumns)
                {
                    var col = schemaChangesTable.Columns[trackedColumnName];
                    if (col != null)
                    {
                        trackedColumns.Add(col);
                        var colNames = this.GetParsedColumnNames(col.ColumnName);
                        quotedTrackedNames.Add(colNames.QuotedName);
                    }
                }
            }

            using var trackCmd = new SqliteCommand();
            trackCmd.Connection = (SqliteConnection)connection;
            trackCmd.Transaction = (SqliteTransaction)transaction;

#pragma warning disable CA2100
            trackCmd.CommandText = this.SqliteObjectNames.CreateMultiRowTrackingInsertCommand(
                count, quotedPkNames.ToArray(), quotedTrackedNames.ToArray(), isTombstone);
#pragma warning restore CA2100

            // Add the shared scope parameter
            var scopeParam = trackCmd.CreateParameter();
            scopeParam.ParameterName = "@scope";
            scopeParam.DbType = DbType.String;
            scopeParam.Value = senderScopeId.HasValue ? senderScopeId.Value.ToString() : (object)DBNull.Value;
            trackCmd.Parameters.Add(scopeParam);

            // Add parameters using same naming convention as CreateMultiRowTrackingInsertCommand: @tpk{r}_{k}, @ttc{r}_{t}
            for (int r = 0; r < count; r++)
            {
                var row = items[offset + r];

                // PK values
                for (int k = 0; k < primaryKeyColumns.Count; k++)
                {
                    var pkCol = primaryKeyColumns[k];
                    var columnIndex = schemaChangesTable.Columns.IndexOf(pkCol);
                    var p = trackCmd.CreateParameter();
                    p.ParameterName = $"@tpk{r}_{k}";
                    p.DbType = pkCol.GetDbType();
                    object value = row[columnIndex] ?? DBNull.Value;
                    p.Value = value == DBNull.Value ? DBNull.Value : SyncTypeConverter.TryConvertFromDbType(value, p.DbType);
                    trackCmd.Parameters.Add(p);
                }

                // Tracked column values (only for non-tombstone)
                for (int t = 0; t < trackedColumns.Count; t++)
                {
                    var tcCol = trackedColumns[t];
                    var columnIndex = schemaChangesTable.Columns.IndexOf(tcCol);
                    var p = trackCmd.CreateParameter();
                    p.ParameterName = $"@ttc{r}_{t}";
                    p.DbType = tcCol.GetDbType();
                    object value = row[columnIndex] ?? DBNull.Value;
                    p.Value = value == DBNull.Value ? DBNull.Value : SyncTypeConverter.TryConvertFromDbType(value, p.DbType);
                    trackCmd.Parameters.Add(p);
                }
            }

            await trackCmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        // ──────────────────────────────────────────────
        //  Batch conflict pre-fetch and apply
        // ──────────────────────────────────────────────

        /// <inheritdoc />
        public override async Task<Dictionary<string, SyncRow>> GetConflictRowsBatchAsync(
            SyncContext context, List<SyncRow> conflictRows, SyncTable schemaChangesTable,
            DbConnection connection, DbTransaction transaction)
        {
            if (conflictRows == null || conflictRows.Count == 0)
                return new Dictionary<string, SyncRow>();

            var primaryKeyColumns = schemaChangesTable.GetPrimaryKeysColumns().ToList();
            var mutableColumns = schemaChangesTable.GetMutableColumns(false, true).ToList();
            int pkCount = primaryKeyColumns.Count;

            // Pre-compute quoted names (ObjectParser is ref struct, cannot be used in async)
            var quotedPkNames = GetQuotedColumnNames(primaryKeyColumns);
            var quotedMutableNames = GetQuotedColumnNames(mutableColumns);

            var result = new Dictionary<string, SyncRow>(StringComparer.InvariantCultureIgnoreCase);

            // Sub-batch to stay within parameter limits
            int subBatchSize = Math.Max(1, 900 / pkCount);

            for (int offset = 0; offset < conflictRows.Count; offset += subBatchSize)
            {
                int count = Math.Min(subBatchSize, conflictRows.Count - offset);

                using var cmd = new SqliteCommand();
                cmd.Connection = (SqliteConnection)connection;
                cmd.Transaction = (SqliteTransaction)transaction;

                // Get SQL from SQLiteObjectNames
#pragma warning disable CA2100
                cmd.CommandText = this.SqliteObjectNames.CreateBatchSelectConflictRowsCommand(
                    count, quotedPkNames.ToArray(), quotedMutableNames.ToArray());
#pragma warning restore CA2100

                // Add PK parameters
                for (int r = 0; r < count; r++)
                {
                    for (int k = 0; k < pkCount; k++)
                    {
                        var pkCol = primaryKeyColumns[k];
                        var columnIndex = schemaChangesTable.Columns.IndexOf(pkCol);
                        var p = cmd.CreateParameter();
                        p.ParameterName = $"@ipk{r}_{k}";
                        p.DbType = pkCol.GetDbType();
                        object value = conflictRows[offset + r][columnIndex] ?? DBNull.Value;
                        p.Value = value == DBNull.Value ? DBNull.Value : SyncTypeConverter.TryConvertFromDbType(value, p.DbType);
                        cmd.Parameters.Add(p);
                    }
                }

                // Create the select table once for all rows in this sub-batch
                var changesSet = schemaChangesTable.Schema.Clone(false);
                var selectTable = BaseOrchestrator.CreateChangesTable(schemaChangesTable, changesSet);

                using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
                while (await reader.ReadAsync().ConfigureAwait(false))
                {
                    var syncRow = selectTable.NewRow();
                    for (var i = 0; i < reader.FieldCount; i++)
                    {
                        var columnName = reader.GetName(i);

                        if (columnName == "sync_row_is_tombstone")
                        {
                            var isTombstone = SyncTypeConverter.TryConvertTo<long>(reader.GetValue(i)) > 0;
                            syncRow.RowState = isTombstone ? SyncRowState.Deleted : SyncRowState.Modified;
                            continue;
                        }

                        if (columnName == "sync_update_scope_id")
                            continue;

                        var columnValueObject = reader.GetValue(i);
                        var columnValue = columnValueObject == DBNull.Value ? null : columnValueObject;
                        syncRow[columnName] = columnValue;
                    }

                    if (syncRow.RowState == SyncRowState.None)
                        syncRow.RowState = SyncRowState.Modified;

                    // Build cache key from PK values
                    var cacheKey = BuildConflictRowCacheKey(syncRow, selectTable);
                    result[cacheKey] = syncRow;
                }
            }

            return result;
        }

        /// <inheritdoc />
        public override async Task<int> ApplyResolvedConflictsBatchAsync(
            SyncContext context,
            List<(SyncRow Row, Guid? SenderScopeId, bool IsDelete)> resolvedRows,
            SyncTable schemaChangesTable, long? lastTimestamp,
            DbConnection connection, DbTransaction transaction)
        {
            if (resolvedRows == null || resolvedRows.Count == 0)
                return 0;

            var mutableColumns = schemaChangesTable.GetMutableColumns(false, true).ToList();
            var primaryKeyColumns = schemaChangesTable.GetPrimaryKeysColumns().ToList();
            int columnsCount = mutableColumns.Count;
            int pkCount = primaryKeyColumns.Count;

            int totalApplied = 0;

            // Group by (SenderScopeId, IsDelete) since each group needs different tracking
            var groups = new Dictionary<(string ScopeKey, bool IsDelete), (Guid? ScopeId, List<SyncRow> Rows)>();

            foreach (var (row, scopeId, isDelete) in resolvedRows)
            {
                var scopeKey = scopeId?.ToString() ?? "NULL";
                var key = (scopeKey, isDelete);

                if (!groups.TryGetValue(key, out var group))
                {
                    group = (scopeId, new List<SyncRow>());
                    groups[key] = group;
                }

                group.Rows.Add(row);
            }

            foreach (var kvp in groups)
            {
                var isDelete = kvp.Key.IsDelete;
                var scopeId = kvp.Value.ScopeId;
                var rows = kvp.Value.Rows;

                if (isDelete)
                {
                    // Batch DELETE + tombstone tracking
                    int subBatchSize = Math.Max(1, 900 / pkCount);
                    for (int offset = 0; offset < rows.Count; offset += subBatchSize)
                    {
                        int count = Math.Min(subBatchSize, rows.Count - offset);
                        var subBatch = rows.GetRange(offset, count);

                        await ExecuteMultiRowDeleteAsync(primaryKeyColumns, subBatch, schemaChangesTable, connection, transaction).ConfigureAwait(false);
                        await ExecuteMultiRowTrackingInsertAsync(primaryKeyColumns, schemaChangesTable, subBatch, 0, subBatch.Count, scopeId, true, connection, transaction).ConfigureAwait(false);
                    }

                    totalApplied += rows.Count;
                }
                else
                {
                    // Batch INSERT OR REPLACE + tracking (force_write, no conflict check)
                    int subBatchSize = Math.Max(1, 900 / columnsCount);
                    for (int offset = 0; offset < rows.Count; offset += subBatchSize)
                    {
                        int count = Math.Min(subBatchSize, rows.Count - offset);

                        await ExecuteMultiRowInsertAsync(mutableColumns, rows, offset, count, schemaChangesTable, connection, transaction).ConfigureAwait(false);
                        await ExecuteMultiRowTrackingInsertAsync(primaryKeyColumns, schemaChangesTable, rows, offset, count, scopeId, false, connection, transaction).ConfigureAwait(false);
                    }

                    totalApplied += rows.Count;
                }
            }

            return totalApplied;
        }

        // ──────────────────────────────────────────────
        //  Helpers
        // ──────────────────────────────────────────────

        private static List<string> GetQuotedColumnNames(List<SyncColumn> columns)
        {
            var result = new List<string>(columns.Count);
            foreach (var col in columns)
            {
                var columnParser = new ObjectParser(col.ColumnName, SqliteObjectNames.LeftQuote, SqliteObjectNames.RightQuote);
                result.Add(columnParser.QuotedShortName);
            }

            return result;
        }

        private static string BuildPrimaryKeyString(SyncRow row, List<SyncColumn> primaryKeyColumns, SyncTable schemaChangesTable)
        {
            if (primaryKeyColumns.Count == 1)
            {
                var idx = schemaChangesTable.Columns.IndexOf(primaryKeyColumns[0]);
                return Convert.ToString(row[idx]) ?? string.Empty;
            }

            var sb = new StringBuilder();
            for (int k = 0; k < primaryKeyColumns.Count; k++)
            {
                if (k > 0)
                    sb.Append('|');
                var idx = schemaChangesTable.Columns.IndexOf(primaryKeyColumns[k]);
                sb.Append(Convert.ToString(row[idx]) ?? string.Empty);
            }

            return sb.ToString();
        }

        private static string BuildPrimaryKeyString(object[] keyParts)
        {
            if (keyParts.Length == 1)
                return Convert.ToString(keyParts[0]) ?? string.Empty;

            var sb = new StringBuilder();
            for (int k = 0; k < keyParts.Length; k++)
            {
                if (k > 0)
                    sb.Append('|');
                sb.Append(Convert.ToString(keyParts[k]) ?? string.Empty);
            }

            return sb.ToString();
        }
    }
}
