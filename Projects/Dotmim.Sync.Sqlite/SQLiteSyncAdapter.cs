using Wormhole.Sync.Builders;
using Wormhole.Sync.DatabaseStringParsers;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
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
        public SqliteSyncAdapter(SyncTable tableDescription, ScopeInfo scopeInfo, bool disableSqlFiltersGeneration)
            : base(tableDescription, scopeInfo)
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

            return (command, false);
        }

        /// <inheritdoc />
        public override void AddCommandParameterValue(SyncContext context, DbParameter parameter, object value, DbCommand command, DbCommandType commandType)
            => parameter.Value = value == null || value == DBNull.Value ? DBNull.Value : SyncTypeConverter.TryConvertFromDbType(value, parameter.DbType);

        /// <inheritdoc />
        public override DbCommand EnsureCommandParametersValues(SyncContext context, DbCommand command, DbCommandType commandType, DbConnection connection, DbTransaction transaction)
            => command;

        /// <inheritdoc />
        public override Task ExecuteBatchCommandAsync(SyncContext context, DbCommand cmd, Guid senderScopeId, IEnumerable<SyncRow> arrayItems, SyncTable schemaChangesTable, SyncTable failedRows, long? lastTimestamp, DbConnection connection, DbTransaction transaction = null)
            => throw new NotImplementedException();

        /// <inheritdoc/>
        public override async Task<string> GetProvisioningSqlScriptsAsync(DbConnection connection, DbTransaction transaction)
        {
            var scripts = new System.Text.StringBuilder();
            var tableBuilder = this.GetTableBuilder();

            // Tracking Table
            var trackingTableCmd = await tableBuilder.GetCreateTrackingTableCommandAsync(connection, transaction).ConfigureAwait(false);
            if (trackingTableCmd != null && !string.IsNullOrEmpty(trackingTableCmd.CommandText))
            {
                scripts.Append(trackingTableCmd.CommandText);
            }

            // Triggers: Insert, Update, Delete
            foreach (DbTriggerType triggerType in new[] { DbTriggerType.Insert, DbTriggerType.Update, DbTriggerType.Delete })
            {
                var triggerCmd = await tableBuilder.GetCreateTriggerCommandAsync(triggerType, connection, transaction).ConfigureAwait(false);
                if (triggerCmd != null && !string.IsNullOrEmpty(triggerCmd.CommandText))
                {
                    scripts.Append("\n\n-- ---------------------------------\n;\n\n");
                    scripts.Append(triggerCmd.CommandText);
                }
            }

            // Stored Procedures in descending order
            var storedProcedureTypes = System.Enum.GetValues(typeof(DbStoredProcedureType)).Cast<DbStoredProcedureType>().OrderByDescending(sp => sp);

            // Get filters for this specific table from the schema
            var tableFilters = this.ScopeInfo?.Schema?.Filters?
                .Where(f => f.TableName.Equals(this.TableDescription.TableName, SyncGlobalization.DataSourceStringComparison) &&
                           (string.IsNullOrEmpty(f.SchemaName) || f.SchemaName.Equals(this.TableDescription.SchemaName, SyncGlobalization.DataSourceStringComparison)))
                .ToList();

            foreach (var spType in storedProcedureTypes)
            {
                if (tableFilters != null && tableFilters.Count > 0)
                {
                    foreach (var filter in tableFilters)
                    {
                        var spCmd = await tableBuilder.GetCreateStoredProcedureCommandAsync(spType, filter, connection, transaction).ConfigureAwait(false);
                        if (spCmd != null && !string.IsNullOrEmpty(spCmd.CommandText))
                        {
                            scripts.Append("\n\n-- ---------------------------------\n;\n\n");
                            scripts.Append(spCmd.CommandText);
                        }
                    }
                }
                else
                {
                    var spCmd = await tableBuilder.GetCreateStoredProcedureCommandAsync(spType, null, connection, transaction).ConfigureAwait(false);
                    if (spCmd != null && !string.IsNullOrEmpty(spCmd.CommandText))
                    {
                        scripts.Append("\n\n-- ---------------------------------\n;\n\n");
                        scripts.Append(spCmd.CommandText);
                    }
                }
            }

            return scripts.ToString();
        }
    }
}