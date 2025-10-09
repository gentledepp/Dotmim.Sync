using Wormhole.Sync.SqlServer;
using Wormhole.Sync.Tests.Core;
using Wormhole.Sync.Tests.Models;
using Wormhole.Sync.Tests.Misc;
#if !NET48
using Microsoft.AspNetCore.Hosting.Server;
#endif
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
#if NET6_0 || NET8_0
using MySqlConnector;
#elif NETCOREAPP3_1
using MySql.Data.MySqlClient;
#endif
#if !NET48
using Microsoft.EntityFrameworkCore;
using Npgsql;
#endif
using Respawn;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using System.Transactions;
#if !NET48
using static Microsoft.EntityFrameworkCore.DbLoggerCategory.Database;
#endif

namespace Wormhole.Sync.Tests.Fixtures
{

    public class DatabaseServerFixture : IDisposable
    {
        public Stopwatch OverallStopwatch { get; }

        // Track databases created by this fixture
        private readonly ConcurrentDictionary<string, (ProviderType providerType, string dbName)> createdDatabases
            = new ConcurrentDictionary<string, (ProviderType, string)>();

        // Track Respawn checkpoints per database
        private readonly ConcurrentDictionary<string, Checkpoint> checkpoints
            = new ConcurrentDictionary<string, Checkpoint>();

        public DatabaseServerFixture() => this.OverallStopwatch = Stopwatch.StartNew();

        /// <summary>
        /// Register a database that was created during tests
        /// </summary>
        public void RegisterDatabase(ProviderType providerType, string dbName)
        {
            var key = $"{providerType}_{dbName}";
            createdDatabases.TryAdd(key, (providerType, dbName));
        }

        /// <summary>
        /// Get or create a Respawn checkpoint for a database
        /// </summary>
        public Checkpoint GetCheckpoint(ProviderType providerType, string dbName)
        {
            var key = $"{providerType}_{dbName}";
            return checkpoints.GetOrAdd(key, _ => CreateCheckpoint(providerType));
        }

        private Checkpoint CreateCheckpoint(ProviderType providerType)
        {
            return providerType switch
            {
                ProviderType.Sql => new Checkpoint
                {
                    TablesToIgnore = new[] { "sysdiagrams" },
                    SchemasToExclude = new[] { "sys", "INFORMATION_SCHEMA" },
                    DbAdapter = DbAdapter.SqlServer
                },
#if !NET48
                ProviderType.MySql or ProviderType.MariaDB => new Checkpoint
                {
                    TablesToIgnore = new[] { "sysdiagrams" },
                    DbAdapter = DbAdapter.MySql
                },
                ProviderType.Postgres => new Checkpoint
                {
                    TablesToIgnore = new[] { "sysdiagrams" },
                    SchemasToExclude = new[] { "pg_catalog", "information_schema" },
                    DbAdapter = DbAdapter.Postgres
                },
#endif
                // SQLite is not supported by Respawn 4.0.0, it will be handled separately
                ProviderType.Sqlite => throw new NotSupportedException("SQLite does not support Respawn. Use drop/recreate instead."),
                _ => throw new NotSupportedException($"Provider type {providerType} is not supported")
            };
        }

        /// <summary>
        /// Reset a database using Respawn (does not support SQLite - use drop/recreate for SQLite)
        /// </summary>
        public async Task ResetDatabaseAsync(ProviderType providerType, string dbName)
        {
            // SQLite is not supported by Respawn 4.0.0, caller should use drop/recreate instead
            if (providerType == ProviderType.Sqlite)
                throw new NotSupportedException("SQLite does not support Respawn. Use drop/recreate instead.");

            var checkpoint = GetCheckpoint(providerType, dbName);

            switch (providerType)
            {
                case ProviderType.Sql:
                    using (var connection = new SqlConnection(HelperDatabase.GetSqlDatabaseConnectionString(dbName)))
                    {
                        await connection.OpenAsync();
                        await checkpoint.Reset(connection);
                    }
                    break;
#if !NET48
                case ProviderType.MySql:
                    using (var connection = new MySqlConnection(HelperDatabase.GetMySqlDatabaseConnectionString(dbName)))
                    {
                        await connection.OpenAsync();
                        await checkpoint.Reset(connection);
                    }
                    break;
                case ProviderType.MariaDB:
                    using (var connection = new MySqlConnection(HelperDatabase.GetMariaDBDatabaseConnectionString(dbName)))
                    {
                        await connection.OpenAsync();
                        await checkpoint.Reset(connection);
                    }
                    break;
                case ProviderType.Postgres:
                    using (var connection = new NpgsqlConnection(HelperDatabase.GetPostgresDatabaseConnectionString(dbName)))
                    {
                        await connection.OpenAsync();
                        await checkpoint.Reset(connection);
                    }
                    break;
#endif
            }
        }

        public void Dispose()
        {
            this.OverallStopwatch.Stop();

            // Drop all databases that were created during tests
            foreach (var kvp in createdDatabases)
            {
                var (providerType, dbName) = kvp.Value;
                try
                {
                    HelperDatabase.DropDatabase(providerType, dbName);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error dropping database {dbName} of type {providerType}: {ex.Message}");
                }
            }

            createdDatabases.Clear();
            checkpoints.Clear();
        }
    }
}
