using Wormhole.Sync.Enumerations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Wormhole.Sync
{
    /// <summary>
    /// Contains the methods to hand a client the server scope at the newest migration it supports.
    /// </summary>
    public partial class RemoteOrchestrator : BaseOrchestrator
    {
        /// <summary>
        /// Returns the server scope as the client may adopt it: at the newest server migration the client supports, so that
        /// a client never provisions the tables or columns of a migration it does not support. Migration names are ordered,
        /// so the client's level is the longest run of server migrations, from the first, that the client supports.
        /// <para>
        /// A client declares its supported migrations in the sync context (<see cref="SyncContext.SupportedMigrationsKey"/>,
        /// since Wormhole.Sync 1.8.9). For a client that does not declare them, <paramref name="undeclaredClientsMigration"/>
        /// is the newest migration it is assumed to support (null: the current server scope, as before 1.8.9).
        /// </para>
        /// The scope of an older migration level is the snapshot that <see cref="MigrateSchemaAsync"/> stored when it applied that migration.
        /// </summary>
        internal virtual async Task<(SyncContext Context, ScopeInfo ScopeInfo)> InternalGetScopeInfoForClientAsync(ScopeInfo serverScopeInfo, SyncContext context,
            string undeclaredClientsMigration, IProgress<ProgressArgs> progress, CancellationToken cancellationToken)
        {
            var serverMigrations = serverScopeInfo?.GetMigrationsList() ?? new List<string>();
            if (serverMigrations.Count == 0)
                return (context, serverScopeInfo);

            serverMigrations.Sort(StringComparer.Ordinal);

            var clientMigrations = context.GetSupportedMigrations();
            if (clientMigrations == null)
            {
                if (string.IsNullOrEmpty(undeclaredClientsMigration))
                    return (context, serverScopeInfo);

                clientMigrations = serverMigrations.Where(m => string.CompareOrdinal(m, undeclaredClientsMigration) <= 0).ToList();
            }

            var clientLevel = serverMigrations.TakeWhile(m => clientMigrations.Contains(m)).ToList();
            if (clientLevel.Count == serverMigrations.Count)
                return (context, serverScopeInfo);

            // the snapshot after the newest supported migration, or before the first one when the client supports none
            var snapshotName = clientLevel.Count > 0 ? clientLevel[clientLevel.Count - 1] : $"_before_{serverMigrations[0]}";

            using var runner = await this.GetConnectionAsync(context, SyncMode.NoTransaction, SyncStage.ScopeLoading, default, default, progress, cancellationToken).ConfigureAwait(false);
            await using (runner.ConfigureAwait(false))
            {
                using var command = runner.Connection.CreateCommand();
                command.Transaction = runner.Transaction;
                command.CommandText = "SELECT [schema_json], [setup_json], [schema_hash] FROM [scope_info_schema_history] WHERE [migration_name] = @migration_name AND [scope_name] = @scope_name";

                var migrationName = command.CreateParameter();
                migrationName.ParameterName = "@migration_name";
                migrationName.Value = snapshotName;
                command.Parameters.Add(migrationName);

                var scopeName = command.CreateParameter();
                scopeName.ParameterName = "@scope_name";
                scopeName.Value = serverScopeInfo.Name;
                command.Parameters.Add(scopeName);

                using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false) || reader.IsDBNull(0) || reader.IsDBNull(1))
                    throw new Exception($"Scope '{serverScopeInfo.Name}': no schema snapshot '{snapshotName}' in scope_info_schema_history, " +
                                        $"so the scope of the client's migration level [{string.Join(", ", clientLevel)}] is unknown.");

                var schema = Serializer.Deserialize<SyncSet>(reader.GetString(0));
                schema.EnsureSchema();

                var clientScopeInfo = new ScopeInfo
                {
                    Id = serverScopeInfo.Id,
                    Name = serverScopeInfo.Name,
                    Schema = schema,
                    Setup = Serializer.Deserialize<SyncSetup>(reader.GetString(1)),
                    Version = serverScopeInfo.Version,
                    LastCleanupTimestamp = serverScopeInfo.LastCleanupTimestamp,
                    Properties = serverScopeInfo.Properties,
                    ServerCapabilities = serverScopeInfo.ServerCapabilities,
                    SchemaHash = reader.IsDBNull(2) ? null : reader.GetString(2),
                };
                clientScopeInfo.SetMigrationsList(clientLevel);

                return (context, clientScopeInfo);
            }
        }
    }
}
