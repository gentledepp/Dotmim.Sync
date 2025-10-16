using Wormhole.Sync.Builders;
using Wormhole.Sync.DatabaseStringParsers;
using Wormhole.Sync.SqlServer.Builders;
using System.Data;
using System.Data.Common;

namespace Wormhole.Sync.SqlServer.Scope
{
    /// <summary>
    /// Sql scope builder for Sql Server.
    /// </summary>
    public class SqlScopeBuilder : DbScopeBuilder
    {
        /// <summary>
        /// Gets the scope info table names.
        /// </summary>
        protected DbTableNames ScopeInfoTableNames { get; }

        /// <summary>
        /// Gets the scope info client table names.
        /// </summary>
        protected DbTableNames ScopeInfoClientTableNames { get; }

        /// <inheritdoc cref="SqlScopeBuilder"/>
        public SqlScopeBuilder(string scopeInfoTableName, string prefix = "", string suffix = "")
        {

            var tableParser = new TableParser(scopeInfoTableName, SqlObjectNames.LeftQuote, SqlObjectNames.RightQuote);

            var schema = SqlManagementUtils.GetUnquotedSqlSchemaName(tableParser);

            // Apply prefix and suffix to the table names
            var scopeInfoName = $"{prefix}{tableParser.TableName}{suffix}";
            var scopeInfoFullTableName = $"[{schema}].[{scopeInfoName}]";
            var scopeInfoClientFullTableName = $"[{schema}].[{scopeInfoName}_client]";

            tableParser = new TableParser(scopeInfoFullTableName, SqlObjectNames.LeftQuote, SqlObjectNames.RightQuote);

            this.ScopeInfoTableNames = new DbTableNames(SqlObjectNames.LeftQuote, SqlObjectNames.RightQuote,
                tableParser.TableName, tableParser.NormalizedFullName, tableParser.NormalizedShortName,
                tableParser.QuotedFullName, tableParser.QuotedShortName, tableParser.SchemaName);

            tableParser = new TableParser(scopeInfoClientFullTableName, SqlObjectNames.LeftQuote, SqlObjectNames.RightQuote);

            this.ScopeInfoClientTableNames = new DbTableNames(SqlObjectNames.LeftQuote, SqlObjectNames.RightQuote,
                tableParser.TableName, tableParser.NormalizedFullName, tableParser.NormalizedShortName,
                tableParser.QuotedFullName, tableParser.QuotedShortName, tableParser.SchemaName);
        }

        /// <inheritdoc />
        public override DbTableNames GetParsedScopeInfoTableNames() => this.ScopeInfoTableNames;

        /// <inheritdoc />
        public override DbTableNames GetParsedScopeInfoClientTableNames() => this.ScopeInfoClientTableNames;

        /// <inheritdoc />
        public override DbCommand GetLocalTimestampCommand(DbConnection connection, DbTransaction transaction)
        {
            // UPDATE Nov 2019 : We don't use min_active_rowversion anymore, since we are in a transaction
            // and we still need the last row version "during the transaction", so check back to @@DBTS
            var command = connection.CreateCommand();
            command.Transaction = transaction;

            command.CommandText = "SELECT CONVERT(bigint, @@DBTS) as lastTimestamp";

            DbParameter p = command.CreateParameter();
            p.ParameterName = "@sync_new_timestamp";
            p.DbType = DbType.Int64;
            p.Direction = ParameterDirection.Output;
            command.Parameters.Add(p);

            return command;
        }

        /// <inheritdoc />
        public override DbCommand GetExistsScopeInfoTableCommand(DbConnection connection, DbTransaction transaction)
        {
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $@"IF EXISTS (SELECT t.name FROM sys.tables t WHERE t.name = N'{this.ScopeInfoTableNames.Name}') SELECT 1 ELSE SELECT 0";
            return command;
        }

        /// <inheritdoc />
        public override DbCommand GetExistsScopeInfoClientTableCommand(DbConnection connection, DbTransaction transaction)
        {
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $@"IF EXISTS (SELECT t.name FROM sys.tables t WHERE t.name = N'{this.ScopeInfoClientTableNames.Name}') SELECT 1 ELSE SELECT 0";
            return command;
        }

        /// <inheritdoc />
        public override DbCommand GetCreateScopeInfoTableCommand(DbConnection connection, DbTransaction transaction)
        {

            var commandText =
                $@"CREATE TABLE {this.ScopeInfoTableNames.QuotedFullName} (
                    [sync_scope_id] [uniqueidentifier] NOT NULL DEFAULT NEWID(),
                    [sync_scope_name] [nvarchar](100) NOT NULL,
                    [sync_scope_schema] [nvarchar](max) NULL,
                    [sync_scope_setup] [nvarchar](max) NULL,
                    [sync_scope_version] [nvarchar](10) NULL,
                    [sync_scope_last_clean_timestamp] [bigint] NULL,
                    [sync_scope_properties] [nvarchar](MAX) NULL,
                    [sync_scope_server_capabilities] [nvarchar](MAX) NULL,
                    [sync_scope_schema_hash] [nvarchar](64) NULL,
                    CONSTRAINT [PKey_{this.ScopeInfoTableNames.NormalizedFullName}]
                    PRIMARY KEY CLUSTERED ([sync_scope_name] ASC)
                    )";

            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = commandText;
            return command;
        }

        /// <inheritdoc />
        public override DbCommand GetCreateScopeInfoClientTableCommand(DbConnection connection, DbTransaction transaction)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"CREATE TABLE {this.ScopeInfoClientTableNames.QuotedFullName}(");
            sb.AppendLine("    [sync_scope_id] [uniqueidentifier] NOT NULL,");
            sb.AppendLine("    [sync_scope_name] [nvarchar](100) NOT NULL,");
            sb.AppendLine("    [sync_scope_hash] [nvarchar](100) NOT NULL,");
            sb.AppendLine("    [sync_scope_parameters] [nvarchar](MAX) NULL,");
            sb.AppendLine("    [scope_last_sync_timestamp] [bigint] NULL,");
            sb.AppendLine("    [scope_last_server_sync_timestamp] [bigint] NULL,");
            sb.AppendLine("    [scope_last_sync_duration] [bigint] NULL,");
            sb.AppendLine("    [scope_last_sync] [datetime] NULL,");
            sb.AppendLine("    [sync_scope_errors] [nvarchar](MAX) NULL,");
            sb.AppendLine("    [sync_scope_properties] [nvarchar](MAX) NULL");

            // Add custom parameter columns
            if (this.ScopeInfoClientParameters != null && this.ScopeInfoClientParameters.Count > 0)
            {
                foreach (var param in this.ScopeInfoClientParameters)
                {
                    sb.AppendLine($",   {DbScopeBuilderHelper.GetSqlServerColumnDefinition(param)}");
                }
            }

            sb.AppendLine($"    CONSTRAINT [PKey_{this.ScopeInfoClientTableNames.NormalizedName}] PRIMARY KEY CLUSTERED ([sync_scope_id] ASC, [sync_scope_name] ASC, [sync_scope_hash] ASC)");
            sb.AppendLine(")");

            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sb.ToString();
            return command;
        }

        /// <inheritdoc />
        public override DbCommand GetAllScopeInfosCommand(DbConnection connection, DbTransaction transaction)
        {

            var commandText =
                $@"SELECT [sync_scope_id],
                          [sync_scope_name],
                          [sync_scope_schema],
                          [sync_scope_setup],
                          [sync_scope_version],
                          [sync_scope_last_clean_timestamp],
                          [sync_scope_properties],
                          [sync_scope_server_capabilities],
                          [sync_scope_schema_hash]
                    FROM  {this.ScopeInfoTableNames.QuotedFullName}";

            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = commandText;

            return command;
        }

        /// <inheritdoc />
        public override DbCommand GetAllScopeInfoClientsCommand(DbConnection connection, DbTransaction transaction)
        {
            var customColumns = DbScopeBuilderHelper.GetColumnListForSelect(this.ScopeInfoClientParameters);

            var commandText =
                $@"SELECT  [sync_scope_id]
                         , [sync_scope_name]
                         , [sync_scope_hash]
                         , [sync_scope_parameters]
                         , [scope_last_sync_timestamp]
                         , [scope_last_server_sync_timestamp]
                         , [scope_last_sync_duration]
                         , [scope_last_sync]
                         , [sync_scope_errors]
                         , [sync_scope_properties]
                         {customColumns}
                    FROM  {this.ScopeInfoClientTableNames.QuotedFullName}";

            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = commandText;

            return command;
        }

        /// <inheritdoc />
        public override DbCommand GetScopeInfoCommand(DbConnection connection, DbTransaction transaction)
        {

            var commandText =
                    $@"SELECT [sync_scope_id],
                          [sync_scope_name],
                          [sync_scope_schema],
                          [sync_scope_setup],
                          [sync_scope_version],
                          [sync_scope_last_clean_timestamp],
                          [sync_scope_properties],
                          [sync_scope_server_capabilities],
                          [sync_scope_schema_hash]
                    FROM  {this.ScopeInfoTableNames.QuotedFullName}
                    WHERE [sync_scope_name] = @sync_scope_name";

            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = commandText;

            var p = command.CreateParameter();
            p.ParameterName = "@sync_scope_name";
            p.DbType = DbType.String;
            p.Size = 100;
            command.Parameters.Add(p);

            return command;
        }

        /// <inheritdoc />
        public override DbCommand GetScopeInfoClientCommand(DbConnection connection, DbTransaction transaction)
        {
            var customColumns = DbScopeBuilderHelper.GetColumnListForSelect(this.ScopeInfoClientParameters);

            var commandText =
                $@"SELECT    [sync_scope_id]
                           , [sync_scope_name]
                           , [sync_scope_hash]
                           , [sync_scope_parameters]
                           , [scope_last_sync_timestamp]
                           , [scope_last_server_sync_timestamp]
                           , [scope_last_sync_duration]
                           , [scope_last_sync]
                           , [sync_scope_errors]
                           , [sync_scope_properties]
                           {customColumns}
                    FROM  {this.ScopeInfoClientTableNames.QuotedFullName}
                    WHERE [sync_scope_name] = @sync_scope_name and [sync_scope_id] = @sync_scope_id and [sync_scope_hash] = @sync_scope_hash";

            var command = connection.CreateCommand();
            command.Transaction = transaction;

            command.CommandText = commandText;

            var p = command.CreateParameter();
            p.ParameterName = "@sync_scope_name";
            p.DbType = DbType.String;
            p.Size = 100;
            command.Parameters.Add(p);

            var p0 = command.CreateParameter();
            p0.ParameterName = "@sync_scope_id";
            p0.DbType = DbType.Guid;
            p0.Size = -1;
            command.Parameters.Add(p0);

            var p1 = command.CreateParameter();
            p1.ParameterName = "@sync_scope_hash";
            p1.DbType = DbType.String;
            p1.Size = 100;
            command.Parameters.Add(p1);

            return command;
        }

        /// <inheritdoc />
        public override DbCommand GetInsertScopeInfoCommand(DbConnection connection, DbTransaction transaction)
            => this.GetUpdateScopeInfoCommand(connection, transaction);

        /// <inheritdoc />
        public override DbCommand GetInsertScopeInfoClientCommand(DbConnection connection, DbTransaction transaction)
            => this.GetUpdateScopeInfoClientCommand(connection, transaction);

        /// <inheritdoc />
        public override DbCommand GetUpdateScopeInfoCommand(DbConnection connection, DbTransaction transaction)
        {

            var commandText = $@"
                    MERGE {this.ScopeInfoTableNames.QuotedFullName} WITH (READCOMMITTED) AS [base] 
                    USING (
                               SELECT  @sync_scope_name AS sync_scope_name,  
	                                   @sync_scope_schema AS sync_scope_schema,  
	                                   @sync_scope_setup AS sync_scope_setup,  
	                                   @sync_scope_version AS sync_scope_version,
                                       @sync_scope_last_clean_timestamp AS sync_scope_last_clean_timestamp,
                                       @sync_scope_properties as sync_scope_properties,
                                       @sync_scope_server_capabilities as sync_scope_server_capabilities,
                                       @sync_scope_schema_hash as sync_scope_schema_hash
                           ) AS [changes] 
                    ON [base].[sync_scope_name] = [changes].[sync_scope_name]
                    WHEN NOT MATCHED THEN
	                    INSERT ([sync_scope_name], [sync_scope_schema], [sync_scope_setup], [sync_scope_version], [sync_scope_last_clean_timestamp], [sync_scope_properties], [sync_scope_server_capabilities], [sync_scope_schema_hash])
	                    VALUES ([changes].[sync_scope_name], [changes].[sync_scope_schema], [changes].[sync_scope_setup], [changes].[sync_scope_version], [changes].[sync_scope_last_clean_timestamp], [changes].[sync_scope_properties], [changes].[sync_scope_server_capabilities], [changes].[sync_scope_schema_hash])
                    WHEN MATCHED THEN
	                    UPDATE SET [sync_scope_name] = [changes].[sync_scope_name], 
                                   [sync_scope_schema] = [changes].[sync_scope_schema], 
                                   [sync_scope_setup] = [changes].[sync_scope_setup], 
                                   [sync_scope_version] = [changes].[sync_scope_version],
                                   [sync_scope_last_clean_timestamp] = [changes].[sync_scope_last_clean_timestamp],
                                   [sync_scope_properties] = [changes].[sync_scope_properties],
                                   [sync_scope_server_capabilities] = [changes].[sync_scope_server_capabilities],
                                   [sync_scope_schema_hash] = [changes].[sync_scope_schema_hash]
                    OUTPUT  INSERTED.[sync_scope_id],
                            INSERTED.[sync_scope_name],
                            INSERTED.[sync_scope_schema],
                            INSERTED.[sync_scope_setup],
                            INSERTED.[sync_scope_version],
                            INSERTED.[sync_scope_last_clean_timestamp],
                            INSERTED.[sync_scope_properties],
                            INSERTED.[sync_scope_server_capabilities],
                            INSERTED.[sync_scope_schema_hash];";

            var command = connection.CreateCommand();
            command.Transaction = transaction;

            command.CommandText = commandText;

            var p = command.CreateParameter();
            p.ParameterName = "@sync_scope_name";
            p.DbType = DbType.String;
            p.Size = 100;
            command.Parameters.Add(p);

            p = command.CreateParameter();
            p.ParameterName = "@sync_scope_schema";
            p.DbType = DbType.String;
            p.Size = -1;
            command.Parameters.Add(p);

            p = command.CreateParameter();
            p.ParameterName = "@sync_scope_setup";
            p.DbType = DbType.String;
            p.Size = -1;
            command.Parameters.Add(p);

            p = command.CreateParameter();
            p.ParameterName = "@sync_scope_version";
            p.DbType = DbType.String;
            p.Size = 10;
            command.Parameters.Add(p);

            p = command.CreateParameter();
            p.ParameterName = "@sync_scope_last_clean_timestamp";
            p.DbType = DbType.Int64;
            command.Parameters.Add(p);

            p = command.CreateParameter();
            p.ParameterName = "@sync_scope_properties";
            p.DbType = DbType.String;
            p.Size = -1;
            command.Parameters.Add(p);

            p = command.CreateParameter();
            p.ParameterName = "@sync_scope_server_capabilities";
            p.DbType = DbType.String;
            p.Size = -1;
            command.Parameters.Add(p);

            p = command.CreateParameter();
            p.ParameterName = "@sync_scope_schema_hash";
            p.DbType = DbType.String;
            p.Size = 64;
            command.Parameters.Add(p);

            return command;
        }

        /// <inheritdoc />
        public override DbCommand GetUpdateScopeInfoClientCommand(DbConnection connection, DbTransaction transaction)
        {
            // Get custom column SQL parts
            var customSelectForUsing = DbScopeBuilderHelper.GetSelectForMergeUsing(this.ScopeInfoClientParameters);
            var customColumnsInsert = DbScopeBuilderHelper.GetColumnListForInsert(this.ScopeInfoClientParameters);
            var customParametersInsert = DbScopeBuilderHelper.GetParameterListForInsert(this.ScopeInfoClientParameters);
            var customSetClause = DbScopeBuilderHelper.GetSetClauseForMerge(this.ScopeInfoClientParameters);
            var customColumnsSelect = DbScopeBuilderHelper.GetColumnListForSelect(this.ScopeInfoClientParameters, "INSERTED.");

            var commandText = $@"
                    MERGE {this.ScopeInfoClientTableNames.QuotedFullName} WITH (READCOMMITTED) AS [base]
                    USING (
                               SELECT  @sync_scope_id AS sync_scope_id,
	                                   @sync_scope_name AS sync_scope_name,
	                                   @sync_scope_hash AS sync_scope_hash,
	                                   @sync_scope_parameters AS sync_scope_parameters,
                                       @scope_last_sync_timestamp AS scope_last_sync_timestamp,
                                       @scope_last_server_sync_timestamp AS scope_last_server_sync_timestamp,
                                       @scope_last_sync_duration AS scope_last_sync_duration,
                                       @scope_last_sync AS scope_last_sync,
                                       @sync_scope_errors AS sync_scope_errors,
                                       @sync_scope_properties AS sync_scope_properties
                                       {customSelectForUsing}
                           ) AS [changes]
                    ON [base].[sync_scope_id] = [changes].[sync_scope_id] and [base].[sync_scope_name] = [changes].[sync_scope_name] and [base].[sync_scope_hash] = [changes].[sync_scope_hash]
                    WHEN NOT MATCHED THEN
	                    INSERT ([sync_scope_name], [sync_scope_id], [sync_scope_hash], [sync_scope_parameters], [scope_last_sync_timestamp],  [scope_last_server_sync_timestamp], [scope_last_sync], [scope_last_sync_duration], [sync_scope_errors], [sync_scope_properties]{customColumnsInsert})
	                    VALUES ([changes].[sync_scope_name], [changes].[sync_scope_id], [changes].[sync_scope_hash], [changes].[sync_scope_parameters], [changes].[scope_last_sync_timestamp], [changes].[scope_last_server_sync_timestamp], [changes].[scope_last_sync], [changes].[scope_last_sync_duration], [changes].[sync_scope_errors], [changes].[sync_scope_properties]{customParametersInsert})
                    WHEN MATCHED THEN
	                    UPDATE SET [scope_last_sync_timestamp] = [changes].[scope_last_sync_timestamp],
                                   [scope_last_server_sync_timestamp] = [changes].[scope_last_server_sync_timestamp],
                                   [scope_last_sync] = [changes].[scope_last_sync],
                                   [scope_last_sync_duration] = [changes].[scope_last_sync_duration],
                                   [sync_scope_errors] = [changes].[sync_scope_errors],
                                   [sync_scope_properties] = [changes].[sync_scope_properties]
                                   {customSetClause}
                    OUTPUT  INSERTED.[sync_scope_name],
                            INSERTED.[sync_scope_id],
                            INSERTED.[sync_scope_hash],
                            INSERTED.[sync_scope_parameters],
                            INSERTED.[scope_last_sync_timestamp],
                            INSERTED.[scope_last_server_sync_timestamp],
                            INSERTED.[scope_last_sync],
                            INSERTED.[scope_last_sync_duration],
                            INSERTED.[sync_scope_errors],
                            INSERTED.[sync_scope_properties]
                            {customColumnsSelect}; ";

            var command = connection.CreateCommand();
            if (transaction != null)
                command.Transaction = transaction;

            command.CommandText = commandText;

            var p = command.CreateParameter();
            p.ParameterName = "@sync_scope_name";
            p.DbType = DbType.String;
            p.Size = 100;
            command.Parameters.Add(p);

            p = command.CreateParameter();
            p.ParameterName = "@sync_scope_id";
            p.DbType = DbType.Guid;
            command.Parameters.Add(p);

            p = command.CreateParameter();
            p.ParameterName = "@sync_scope_hash";
            p.DbType = DbType.String;
            p.Size = 100;
            command.Parameters.Add(p);

            p = command.CreateParameter();
            p.ParameterName = "@sync_scope_parameters";
            p.DbType = DbType.String;
            p.Size = int.MaxValue;
            command.Parameters.Add(p);

            p = command.CreateParameter();
            p.ParameterName = "@scope_last_sync_timestamp";
            p.DbType = DbType.Int64;
            command.Parameters.Add(p);

            p = command.CreateParameter();
            p.ParameterName = "@scope_last_server_sync_timestamp";
            p.DbType = DbType.Int64;
            command.Parameters.Add(p);

            p = command.CreateParameter();
            p.ParameterName = "@scope_last_sync";
            p.DbType = DbType.DateTime;
            command.Parameters.Add(p);

            p = command.CreateParameter();
            p.ParameterName = "@scope_last_sync_duration";
            p.DbType = DbType.Int64;
            command.Parameters.Add(p);

            p = command.CreateParameter();
            p.ParameterName = "@sync_scope_errors";
            p.DbType = DbType.String;
            p.Size = -1;
            command.Parameters.Add(p);

            p = command.CreateParameter();
            p.ParameterName = "@sync_scope_properties";
            p.DbType = DbType.String;
            p.Size = -1;
            command.Parameters.Add(p);

            // Add parameters for custom columns
            if (this.ScopeInfoClientParameters != null && this.ScopeInfoClientParameters.Count > 0)
            {
                foreach (var param in this.ScopeInfoClientParameters)
                {
                    var customParam = command.CreateParameter();
                    customParam.ParameterName = $"@{param.Name}";
                    customParam.DbType = param.DbType;
                    if (param.MaxLength > 0)
                        customParam.Size = param.MaxLength;
                    command.Parameters.Add(customParam);
                }
            }

            return command;
        }

        /// <inheritdoc />
        public override DbCommand GetDeleteScopeInfoCommand(DbConnection connection, DbTransaction transaction)
        {

            var commandText = $@"DELETE FROM {this.ScopeInfoTableNames.QuotedFullName} WHERE [sync_scope_name] = @sync_scope_name";

            var command = connection.CreateCommand();
            command.Transaction = transaction;

            command.CommandText = commandText;

            var p = command.CreateParameter();
            p.ParameterName = "@sync_scope_name";
            p.DbType = DbType.String;
            p.Size = 100;
            command.Parameters.Add(p);

            return command;
        }

        /// <inheritdoc />
        public override DbCommand GetDeleteScopeInfoClientCommand(DbConnection connection, DbTransaction transaction)
        {

            var commandText =
                $@"DELETE FROM  {this.ScopeInfoClientTableNames.QuotedFullName}
                   WHERE [sync_scope_name] = @sync_scope_name and [sync_scope_id] = @sync_scope_id and [sync_scope_hash] = @sync_scope_hash";

            var command = connection.CreateCommand();
            command.Transaction = transaction;

            command.CommandText = commandText;

            var p = command.CreateParameter();
            p.ParameterName = "@sync_scope_name";
            p.DbType = DbType.String;
            p.Size = 100;
            command.Parameters.Add(p);

            p = command.CreateParameter();
            p.ParameterName = "@sync_scope_id";
            p.DbType = DbType.Guid;
            p.Size = -1;
            command.Parameters.Add(p);

            p = command.CreateParameter();
            p.ParameterName = "@sync_scope_hash";
            p.DbType = DbType.String;
            p.Size = 100;
            command.Parameters.Add(p);

            return command;
        }

        /// <inheritdoc />
        public override DbCommand GetDropScopeInfoTableCommand(DbConnection connection, DbTransaction transaction)
        {

            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"DROP TABLE IF EXISTS {this.ScopeInfoTableNames.QuotedFullName}";
            return command;
        }

        /// <inheritdoc />
        public override DbCommand GetDropScopeInfoClientTableCommand(DbConnection connection, DbTransaction transaction)
        {
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"DROP TABLE IF EXISTS {this.ScopeInfoClientTableNames.QuotedFullName}";
            return command;
        }

        /// <inheritdoc />
        public override DbCommand GetExistsScopeInfoCommand(DbConnection connection, DbTransaction transaction)
        {
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $@"Select count(*) from {this.ScopeInfoTableNames.QuotedFullName} where sync_scope_name = @sync_scope_name;";

            var p1 = command.CreateParameter();
            p1.ParameterName = "@sync_scope_name";
            p1.DbType = DbType.String;
            p1.Size = 100;
            command.Parameters.Add(p1);

            return command;
        }

        /// <inheritdoc />
        public override DbCommand GetExistsScopeInfoClientCommand(DbConnection connection, DbTransaction transaction)
        {
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $@"Select count(*) from {this.ScopeInfoClientTableNames.QuotedFullName} 
                                     where sync_scope_id = @sync_scope_id 
                                     and sync_scope_name = @sync_scope_name
                                     and sync_scope_hash = @sync_scope_hash;";

            var p = command.CreateParameter();
            p.ParameterName = "@sync_scope_id";
            p.DbType = DbType.Guid;
            p.Size = -1;
            command.Parameters.Add(p);

            p = command.CreateParameter();
            p.ParameterName = "@sync_scope_name";
            p.DbType = DbType.String;
            p.Size = 100;
            command.Parameters.Add(p);

            p = command.CreateParameter();
            p.ParameterName = "@sync_scope_hash";
            p.DbType = DbType.String;
            p.Size = 100;
            command.Parameters.Add(p);

            return command;
        }

        /// <inheritdoc/>
        public override DbCommand GetMigrateScopeInfoTableCommand(DbConnection connection, DbTransaction transaction)
        {
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $@"
                ALTER TABLE {this.ScopeInfoTableNames.QuotedFullName} ADD [sync_scope_server_capabilities] NVARCHAR(MAX) NULL;
                ALTER TABLE {this.ScopeInfoTableNames.QuotedFullName} ADD [sync_scope_schema_hash] NVARCHAR(64) NULL;
            ";
            return command;
        }

        /// <inheritdoc/>
        public override DbCommand GetExistsScopeInfoClientColumnCommand(DbConnection connection, DbTransaction transaction, string columnName)
        {
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $@"
                SELECT COUNT(*)
                FROM sys.columns
                WHERE object_id = OBJECT_ID('{this.ScopeInfoClientTableNames.QuotedFullName}')
                AND name = @columnName";

            var p = command.CreateParameter();
            p.ParameterName = "@columnName";
            p.DbType = DbType.String;
            p.Value = columnName;
            command.Parameters.Add(p);

            return command;
        }

        /// <inheritdoc/>
        public override DbCommand GetAddScopeInfoClientColumnCommand(DbConnection connection, DbTransaction transaction, ScopeInfoClientParameter parameter)
        {
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            var columnDef = DbScopeBuilderHelper.GetSqlServerColumnDefinition(parameter);
            command.CommandText = $"ALTER TABLE {this.ScopeInfoClientTableNames.QuotedFullName} ADD {columnDef}";
            return command;
        }

        /// <inheritdoc/>
        public override DbCommand GetCreateScopeInfoClientIndexesCommand(DbConnection connection, DbTransaction transaction)
        {
            // Check if there are any indexed parameters
            if (this.ScopeInfoClientParameters == null || this.ScopeInfoClientParameters.Count == 0)
                return null;

            var indexedParams = new System.Collections.Generic.List<ScopeInfoClientParameter>();
            foreach (var param in this.ScopeInfoClientParameters)
            {
                if (param.IsIndexed)
                    indexedParams.Add(param);
            }

            if (indexedParams.Count == 0)
                return null;

            var sb = new System.Text.StringBuilder();
            foreach (var param in indexedParams)
            {
                // Generate a unique index name based on table name and column name
                var indexName = $"IX_{this.ScopeInfoClientTableNames.Name}_{param.Name}";
                sb.AppendLine($"CREATE NONCLUSTERED INDEX [{indexName}] ON {this.ScopeInfoClientTableNames.QuotedFullName} ([{param.Name}] ASC);");
            }

            if (sb.Length == 0)
                return null;

            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sb.ToString();
            return command;
        }
    }
}