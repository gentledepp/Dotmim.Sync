using Wormhole.Sync.Builders;
using Wormhole.Sync.DatabaseStringParsers;
using Microsoft.Data.SqlClient;
using System.Data.Common;
using System.Threading.Tasks;

namespace Wormhole.Sync.SqlServer.Builders
{
    /// <summary>
    /// Sql builder for Sql Server.
    /// </summary>
    public class SqlDatabaseBuilder : DbDatabaseBuilder
    {
        /// <inheritdoc />
        public override async Task EnsureDatabaseAsync(DbConnection connection, DbTransaction transaction = null)
        {
            // Chek if db exists
            var exists = await SqlManagementUtils.DatabaseExistsAsync(connection as SqlConnection, transaction as SqlTransaction).ConfigureAwait(false);

            if (!exists)
                throw new MissingDatabaseException(connection.Database);
        }

        /// <inheritdoc />
        public override Task<SyncSetup> GetAllTablesAsync(DbConnection connection, DbTransaction transaction = null)
            => SqlManagementUtils.GetAllTablesAsync(connection as SqlConnection, transaction as SqlTransaction);

        /// <inheritdoc />
        public override Task<SyncTable> EnsureTableAsync(string tableName, string schemaName, DbConnection connection, DbTransaction transaction = null)
            => Task.FromResult(new SyncTable(tableName, schemaName));

        /// <inheritdoc />
        public override Task<(string DatabaseName, string Version)> GetHelloAsync(DbConnection connection, DbTransaction transaction = null)
            => SqlManagementUtils.GetHelloAsync(connection as SqlConnection, transaction as SqlTransaction);

        /// <inheritdoc />
        public override Task<SyncTable> GetTableAsync(string tableName, string schemaName, DbConnection connection, DbTransaction transaction = null)
        {
            var tableParser = new TableParser($"{schemaName}.{tableName}", SqlObjectNames.LeftQuote, SqlObjectNames.RightQuote);
            return SqlManagementUtils.GetTableAsync(tableParser.TableName, tableParser.SchemaName, connection as SqlConnection, transaction as SqlTransaction);
        }

        /// <inheritdoc />
        public override Task<bool> ExistsTableAsync(string tableName, string schemaName, DbConnection connection, DbTransaction transaction = null)
        {
            var tableParser = new TableParser($"{schemaName}.{tableName}", SqlObjectNames.LeftQuote, SqlObjectNames.RightQuote);
            return SqlManagementUtils.TableExistsAsync(tableParser.TableName, tableParser.SchemaName, connection as SqlConnection, transaction as SqlTransaction);
        }

        /// <inheritdoc />
        public override Task DropsTableIfExistsAsync(string tableName, string schemaName, DbConnection connection, DbTransaction transaction = null)
        {
            var tableParser = new TableParser($"{schemaName}.{tableName}", SqlObjectNames.LeftQuote, SqlObjectNames.RightQuote);
            return SqlManagementUtils.DropTableIfExistsAsync(tableParser.TableName, tableParser.SchemaName, connection as SqlConnection, transaction as SqlTransaction);
        }

        /// <inheritdoc />
        public override Task RenameTableAsync(string tableName, string schemaName, string newTableName, string newSchemaName, DbConnection connection, DbTransaction transaction = null)
        {
            var tableParser = new TableParser($"{schemaName}.{tableName}", SqlObjectNames.LeftQuote, SqlObjectNames.RightQuote);
            var newTableParser = new TableParser($"{newSchemaName}.{newTableName}", SqlObjectNames.LeftQuote, SqlObjectNames.RightQuote);
            return SqlManagementUtils.RenameTableAsync(tableParser.TableName, tableParser.SchemaName,
                newTableParser.TableName, newTableParser.SchemaName, connection as SqlConnection, transaction as SqlTransaction);
        }
    }
}