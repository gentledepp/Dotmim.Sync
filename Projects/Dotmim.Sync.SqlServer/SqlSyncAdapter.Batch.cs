using Wormhole.Sync.Builders;
using Wormhole.Sync.DatabaseStringParsers;
using Wormhole.Sync.Enumerations;
using Microsoft.Data.SqlClient;
using Microsoft.Data.SqlClient.Server;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;

namespace Wormhole.Sync.SqlServer.Builders
{

    /// <summary>
    /// Sql Server Sync Adapter.
    /// </summary>
    public partial class SqlSyncAdapter : DbSyncAdapter
    {
        /// <summary>
        /// Executing a batch command.
        /// </summary>
        public override async Task ExecuteBatchCommandAsync(SyncContext context, DbCommand cmd, Guid senderScopeId, IEnumerable<SyncRow> arrayItems, SyncTable schemaChangesTable,
                                                            SyncTable failedRows, long? lastTimestamp, DbConnection connection, DbTransaction transaction = null)
        {

            var items = arrayItems?.ToList();

            if (items == null)
                return;

            var applyRowsCount = items.Count;

            if (applyRowsCount <= 0)
                return;

            var syncRowState = SyncRowState.None;

            var records = new List<SqlDataRecord>(applyRowsCount);

            SqlMetaData[] metadatas = new SqlMetaData[schemaChangesTable.Columns.Count];

            for (int i = 0; i < schemaChangesTable.Columns.Count; i++)
                metadatas[i] = this.GetSqlMetadaFromType(schemaChangesTable.Columns[i]);

            try
            {
                foreach (var row in items)
                {
                    syncRowState = row.RowState;

                    var record = new SqlDataRecord(metadatas);

                    int sqlMetadataIndex = 0;

                    for (int i = 0; i < schemaChangesTable.Columns.Count; i++)
                    {
                        var schemaColumn = schemaChangesTable.Columns[i];

                        // Get the default value
                        // var columnType = schemaColumn.GetDataType();
                        object defaultValue = schemaColumn.GetDefaultValue();

                        // metadatas don't have readonly values, so get from sqlMetadataIndex
                        var sqlMetadataType = metadatas[sqlMetadataIndex].SqlDbType;

                        object rowValue = SetRowValue(row, i, sqlMetadataType);

                        record.SetValue(sqlMetadataIndex, rowValue);
                        sqlMetadataIndex++;
                    }

                    records.Add(record);
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Can't create a SqlRecord based on the rows we have: {ex.Message}");
            }

            var sqlParameters = cmd.Parameters as SqlParameterCollection;

            sqlParameters["@changeTable"].TypeName = string.Empty;
            sqlParameters["@changeTable"].Value = records;

            if (sqlParameters.Contains("@sync_min_timestamp"))
                sqlParameters["@sync_min_timestamp"].Value = lastTimestamp.HasValue ? lastTimestamp.Value : DBNull.Value;

            if (sqlParameters.Contains("@sync_force_write"))
                sqlParameters["@sync_force_write"].Value = context.SyncType == SyncType.Reinitialize || context.SyncType == SyncType.ReinitializeWithUpload ? 1 : 0;

            if (sqlParameters.Contains("@sync_scope_id"))
                sqlParameters["@sync_scope_id"].Value = senderScopeId;

            bool alreadyOpened = connection.State == ConnectionState.Open;

            try
            {
                if (!alreadyOpened)
                    await connection.OpenAsync().ConfigureAwait(false);

                cmd.Transaction = transaction;

                using var dataReader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);

                while (await dataReader.ReadAsync().ConfigureAwait(false))
                {
                    var failedRow = new SyncRow(schemaChangesTable, syncRowState);

                    for (var i = 0; i < dataReader.FieldCount; i++)
                    {
                        var columnValueObject = dataReader.GetValue(i);
                        var columnName = dataReader.GetName(i);

                        failedRow[columnName] = columnValueObject == DBNull.Value ? null : columnValueObject;
                    }

                    // don't care about row state
                    // Since it will be requested by next request from GetConflict()
                    failedRows.Rows.Add(failedRow);
                }

#if NET6_0_OR_GREATER
                await dataReader.CloseAsync().ConfigureAwait(false);
#else
                dataReader.Close();
#endif
            }
            finally
            {
                records.Clear();

                if (!alreadyOpened && connection.State != ConnectionState.Closed)
#if NET6_0_OR_GREATER
                    await connection.CloseAsync().ConfigureAwait(false);
#else
                    connection.Close();
#endif
            }
        }

        private static object SetRowValue(SyncRow row, int i, SqlDbType sqlMetadataType)
        {
            object rowValue = row[i];

            if (rowValue != null)
            {
                switch (sqlMetadataType)
                {
                    case SqlDbType.BigInt:
                        rowValue = SyncTypeConverter.TryConvertTo<long>(rowValue);
                        break;
                    case SqlDbType.Bit:
                        rowValue = SyncTypeConverter.TryConvertTo<bool>(rowValue);
                        break;
                    case SqlDbType.Date:
#if NET6_0_OR_GREATER
                        var rowValue3 = SyncTypeConverter.TryConvertTo<DateOnly>(rowValue);

                        if (rowValue3 < DateOnly.FromDateTime(sqlDateMin))
                            rowValue3 = DateOnly.FromDateTime(sqlDateMin);

                        // Even if sqlmetadata is Date (and it's a perfect match for DateOnly)
                        // We still need to convert it to DateTime, since SqlDataRecord doesn't support DateOnly
                        rowValue = ((DateOnly)rowValue3).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
                        break;
#endif
                    case SqlDbType.DateTime:
                    case SqlDbType.DateTime2:
                    case SqlDbType.SmallDateTime:
                        var rowValue2 = SyncTypeConverter.TryConvertTo<DateTime>(rowValue);
                        if (sqlMetadataType == SqlDbType.DateTime && rowValue2 < sqlDateMin)
                            rowValue2 = sqlDateMin;
                        else if (sqlMetadataType == SqlDbType.SmallDateTime && rowValue2 < sqlSmallDateMin)
                            rowValue2 = sqlSmallDateMin;
                        rowValue = rowValue2;
                        break;
                    case SqlDbType.DateTimeOffset:
                        rowValue = SyncTypeConverter.TryConvertTo<DateTimeOffset>(rowValue);
                        break;
                    case SqlDbType.Decimal:
                        rowValue = SyncTypeConverter.TryConvertTo<decimal>(rowValue);
                        break;
                    case SqlDbType.Float:
                        rowValue = SyncTypeConverter.TryConvertTo<double>(rowValue);
                        break;
                    case SqlDbType.Real:
                        rowValue = SyncTypeConverter.TryConvertTo<float>(rowValue);
                        break;
                    case SqlDbType.Image:
                    case SqlDbType.Binary:
                    case SqlDbType.VarBinary:
                        rowValue = SyncTypeConverter.TryConvertTo<byte[]>(rowValue);
                        break;
                    case SqlDbType.Variant:
                        break;
                    case SqlDbType.Int:
                        rowValue = SyncTypeConverter.TryConvertTo<int>(rowValue);
                        break;
                    case SqlDbType.Money:
                    case SqlDbType.SmallMoney:
                        rowValue = SyncTypeConverter.TryConvertTo<decimal>(rowValue);
                        break;
                    case SqlDbType.NChar:
                    case SqlDbType.NText:
                    case SqlDbType.VarChar:
                    case SqlDbType.Xml:
                    case SqlDbType.NVarChar:
                    case SqlDbType.Text:
                    case SqlDbType.Char:
                        rowValue = SyncTypeConverter.TryConvertTo<string>(rowValue);
                        break;
                    case SqlDbType.SmallInt:
                        rowValue = SyncTypeConverter.TryConvertTo<short>(rowValue);
                        break;
                    case SqlDbType.Time:
                        rowValue = SyncTypeConverter.TryConvertTo<TimeSpan>(rowValue);
                        break;
                    case SqlDbType.Timestamp:
                        break;
                    case SqlDbType.TinyInt:
                        rowValue = SyncTypeConverter.TryConvertTo<byte>(rowValue);
                        break;
                    case SqlDbType.Udt:
                        throw new ArgumentException($"Can't use UDT as SQL Type");
                    case SqlDbType.UniqueIdentifier:
                        rowValue = SyncTypeConverter.TryConvertTo<Guid>(rowValue);
                        break;
                }
            }

            return rowValue ?? DBNull.Value;
        }

        private SqlMetaData GetSqlMetadaFromType(SyncColumn column)
        {
            long maxLength = column.MaxLength;

            var sqlDbType = this.GetSqlDbType(column);

            // Since we validate length before, it's not mandatory here.
            // let's say.. just in case..
            switch (sqlDbType)
            {
                case SqlDbType.NVarChar:
                    maxLength = maxLength <= 0 ? SqlMetaData.Max : Math.Min(maxLength, 4000);
                    break;
                case SqlDbType.VarChar:
                case SqlDbType.VarBinary:
                    maxLength = maxLength <= 0 ? SqlMetaData.Max : Math.Min(maxLength, 8000);
                    break;
                case SqlDbType.NChar:
                    maxLength = maxLength <= 0 ? 4000 : maxLength;
                    break;
                case SqlDbType.Char:
                case SqlDbType.Binary:
                    maxLength = maxLength <= 0 ? 8000 : maxLength;
                    break;
                case SqlDbType.Decimal:
                    var (p, s) = this.SqlMetadata.GetPrecisionAndScale(column);
                    if (p <= 0 || p <= s)
                    {
                        if (p == 0)
                            p = 18;
                        if (s == 0)
                            s = Math.Min((byte)(p - 1), (byte)6);
                    }

                    return new SqlMetaData(column.ColumnName, sqlDbType, p, s);
                default:
                    var dataType = column.GetDataType();

                    if (dataType != typeof(char))
                    {
                        return new SqlMetaData(column.ColumnName, sqlDbType);
                    }

                    maxLength = 1;
                    break;
            }

            return new SqlMetaData(column.ColumnName, sqlDbType, maxLength);
        }

        private SqlDbType GetSqlDbType(SyncColumn column)
        {
            // TODO : Find something better than string comparison for change tracking provider
            var isSameProvider = this.TableDescription.OriginalProvider == SqlSyncProvider.ProviderType ||
            this.TableDescription.OriginalProvider == "SqlSyncChangeTrackingProvider, Dotmim.Sync.SqlServer.SqlSyncChangeTrackingProvider";

            if (isSameProvider)
                return this.SqlMetadata.GetSqlDbType(column);

            return this.SqlMetadata.GetOwnerDbTypeFromDbType(column);
        }

        /// <inheritdoc />
        public override async Task<Dictionary<string, SyncRow>> GetConflictRowsBatchAsync(
            SyncContext context, List<SyncRow> conflictRows, SyncTable schemaChangesTable,
            DbConnection connection, DbTransaction transaction)
        {
            if (conflictRows == null || conflictRows.Count == 0)
                return new Dictionary<string, SyncRow>();

            var primaryKeyColumns = schemaChangesTable.GetPrimaryKeysColumns().ToList();
            int pkCount = primaryKeyColumns.Count;

            var result = new Dictionary<string, SyncRow>(StringComparer.InvariantCultureIgnoreCase);

            // Pre-compute quoted PK names (must be done outside async context due to ref struct limitation)
            var quotedPkNames = GetQuotedPkColumnNames(primaryKeyColumns);

            // Sub-batch to stay within parameter limits (2100 params max in SQL Server)
            int subBatchSize = Math.Max(1, 2000 / pkCount);

            for (int offset = 0; offset < conflictRows.Count; offset += subBatchSize)
            {
                int count = Math.Min(subBatchSize, conflictRows.Count - offset);

                // Build the SELECT query with VALUES clause
                var sql = this.SqlObjectNames.CreateBatchSelectRowCommand(count, quotedPkNames);

                using var cmd = new SqlCommand();
                cmd.Connection = (SqlConnection)connection;
                cmd.Transaction = (SqlTransaction)transaction;
                cmd.CommandText = sql;
                cmd.CommandType = CommandType.Text;

                // Add PK parameters using same naming convention: @pk{r}_{k}
                for (int r = 0; r < count; r++)
                {
                    for (int k = 0; k < pkCount; k++)
                    {
                        var pkCol = primaryKeyColumns[k];
                        var columnIndex = schemaChangesTable.Columns.IndexOf(pkCol);
                        var sqlDbType = this.GetSqlDbType(pkCol);
                        var rowValue = SetRowValue(conflictRows[offset + r], columnIndex, sqlDbType);

                        var p = cmd.Parameters.AddWithValue($"@pk{r}_{k}", rowValue ?? DBNull.Value);
                        p.SqlDbType = sqlDbType;
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

#if NET6_0_OR_GREATER
                await reader.CloseAsync().ConfigureAwait(false);
#else
                reader.Close();
#endif
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

            int totalApplied = 0;

            // Group by (SenderScopeId, IsDelete) since each group needs different handling
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

            // Build SqlMetaData for the table columns
            var metadatas = new SqlMetaData[schemaChangesTable.Columns.Count];
            for (int i = 0; i < schemaChangesTable.Columns.Count; i++)
                metadatas[i] = this.GetSqlMetadaFromType(schemaChangesTable.Columns[i]);

            foreach (var kvp in groups)
            {
                var isDelete = kvp.Key.IsDelete;
                var scopeId = kvp.Value.ScopeId;
                var rows = kvp.Value.Rows;

                // Create SqlDataRecords for this group
                var records = new List<SqlDataRecord>(rows.Count);
                foreach (var row in rows)
                {
                    var record = new SqlDataRecord(metadatas);
                    for (int i = 0; i < schemaChangesTable.Columns.Count; i++)
                    {
                        var sqlDbType = metadatas[i].SqlDbType;
                        var rowValue = SetRowValue(row, i, sqlDbType);
                        record.SetValue(i, rowValue);
                    }

                    records.Add(record);
                }

                // Get the appropriate bulk stored procedure
                var spCommandType = isDelete ? DbStoredProcedureType.BulkDeleteRows : DbStoredProcedureType.BulkUpdateRows;
                var spName = this.SqlObjectNames.GetStoredProcedureCommandName(spCommandType, null);

                bool alreadyOpened = connection.State == ConnectionState.Open;
                using var cmd = new SqlCommand(spName);
                cmd.Connection = (SqlConnection)connection;
                cmd.Transaction = (SqlTransaction)transaction;
                cmd.CommandType = CommandType.StoredProcedure;

                // Derive parameters from the stored procedure
                try
                {
                    if (!alreadyOpened)
                        await connection.OpenAsync().ConfigureAwait(false);

                    ((SqlConnection)connection).DeriveParameters(cmd, false, (SqlTransaction)transaction);

                    if (cmd.Parameters.Count > 0 && cmd.Parameters[0].ParameterName == "@RETURN_VALUE")
                        cmd.Parameters.RemoveAt(0);

                    // Set parameter values
                    if (cmd.Parameters.Contains("@changeTable"))
                    {
                        cmd.Parameters["@changeTable"].TypeName = string.Empty;
                        cmd.Parameters["@changeTable"].Value = records;
                    }

                    if (cmd.Parameters.Contains("@sync_min_timestamp"))
                        cmd.Parameters["@sync_min_timestamp"].Value = lastTimestamp.HasValue ? lastTimestamp.Value : DBNull.Value;

                    // Force write = 1 for resolved conflicts
                    if (cmd.Parameters.Contains("@sync_force_write"))
                        cmd.Parameters["@sync_force_write"].Value = 1;

                    if (cmd.Parameters.Contains("@sync_scope_id"))
                        cmd.Parameters["@sync_scope_id"].Value = scopeId.HasValue ? scopeId.Value : DBNull.Value;

                    // Execute - the stored procedure returns failed rows, but for force_write there shouldn't be any
                    using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);

                    // Count any failed rows (shouldn't happen with force_write)
                    int failedCount = 0;
                    while (await reader.ReadAsync().ConfigureAwait(false))
                        failedCount++;

#if NET6_0_OR_GREATER
                    await reader.CloseAsync().ConfigureAwait(false);
#else
                    reader.Close();
#endif

                    totalApplied += rows.Count - failedCount;
                }
                finally
                {
                    records.Clear();

                    if (!alreadyOpened && connection.State != ConnectionState.Closed)
#if NET6_0_OR_GREATER
                        await connection.CloseAsync().ConfigureAwait(false);
#else
                        connection.Close();
#endif
                }
            }

            return totalApplied;
        }

        /// <summary>
        /// Helper to compute quoted PK column names outside async context.
        /// ObjectParser is a ref struct and cannot be used in async methods.
        /// </summary>
        private static string[] GetQuotedPkColumnNames(List<SyncColumn> primaryKeyColumns)
        {
            var quotedPkNames = new string[primaryKeyColumns.Count];
            for (int i = 0; i < primaryKeyColumns.Count; i++)
            {
                var columnParser = new ObjectParser(primaryKeyColumns[i].ColumnName, SqlObjectNames.LeftQuote, SqlObjectNames.RightQuote);
                quotedPkNames[i] = columnParser.QuotedShortName;
            }

            return quotedPkNames;
        }
    }
}