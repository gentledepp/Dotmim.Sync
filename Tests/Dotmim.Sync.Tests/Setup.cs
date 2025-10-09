using Wormhole.Sync.Tests.Core;
#if NET6_0 || NET8_0
using MySqlConnector;
#elif NETCOREAPP3_1
using MySql.Data.MySqlClient;
#endif

using Wormhole.Sync.SqlServer;
using Wormhole.Sync.Tests.Fixtures;
using Wormhole.Sync.Tests.IntegrationTests;
using Wormhole.Sync.Tests.Misc;
using Wormhole.Sync.Tests.Models;
using Wormhole.Sync.Tests.UnitTests;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit.Abstractions;

namespace Wormhole.Sync.Tests
{
    /// <summary>
    /// Setup class is all you need to setup connection string, tables and client enabled for your provider tests.
    /// </summary>
    public static class Setup
    {
        /// <summary>
        /// Gets a value indicating whether gets if the tests are running on Azure Dev.
        /// </summary>
        public static bool IsOnAzureDev
        {
            get
            {
                // check if we are running on appveyor or not
                string isOnAzureDev = Environment.GetEnvironmentVariable("AZUREDEV");
                return !string.IsNullOrEmpty(isOnAzureDev) && string.Equals(isOnAzureDev, "true", SyncGlobalization.DataSourceStringComparison);
            }
        }
    }

    public class SqlServerUnitTests : InterceptorsTests
    {
        public SqlServerUnitTests(ITestOutputHelper output, DatabaseServerFixture fixture)
            : base(output, fixture)
        {
        }

        public override ProviderType ServerProviderType => ProviderType.Sql;

        private string sqlRandomDatabaseName => HelperDatabase.GetPerTestName(this.GetType(), "ut1_sql_");

        public override IEnumerable<CoreProvider> GetClientProviders()
        {
            yield return HelperDatabase.GetSyncProvider(ProviderType.Sql, this.sqlRandomDatabaseName, true);
        }
    }

    public class SqlServerUnitLocalOrchestratorTests : LocalOrchestratorTests
    {
        public SqlServerUnitLocalOrchestratorTests(ITestOutputHelper output, DatabaseServerFixture fixture)
            : base(output, fixture)
        {
        }

        public override ProviderType ServerProviderType => ProviderType.Sql;

        private string sqlRandomDatabaseName => HelperDatabase.GetPerTestName(this.GetType(), "ut2_sql_");

        public override IEnumerable<CoreProvider> GetClientProviders()
        {
            yield return HelperDatabase.GetSyncProvider(ProviderType.Sql, this.sqlRandomDatabaseName, true);
        }
    }

    public class SqlServerChangeTrackingUnitLocalOrchestratorTests : LocalOrchestratorTests
    {
        public SqlServerChangeTrackingUnitLocalOrchestratorTests(ITestOutputHelper output, DatabaseServerFixture fixture)
            : base(output, fixture)
        {
        }

        private string sqlRandomClientDatabaseName => HelperDatabase.GetPerTestName(this.GetType(), "ut2_sql_server_ct_");
        private string sqlRandomServerDatabaseName => HelperDatabase.GetPerTestName(this.GetType(), "ut2_sql_client_ct_");

        public override ProviderType ServerProviderType => ProviderType.Sql;

        public override IEnumerable<CoreProvider> GetClientProviders()
        {
            var cstring = HelperDatabase.GetSqlDatabaseConnectionString(this.sqlRandomClientDatabaseName);
            var provider = new SqlSyncChangeTrackingProvider(cstring);
            provider.UseFallbackSchema(true);

            yield return provider;
        }

        public override CoreProvider GetServerProvider()
        {
            var cstring = HelperDatabase.GetSqlDatabaseConnectionString(this.sqlRandomServerDatabaseName);
            var provider = new SqlSyncChangeTrackingProvider(cstring);
            provider.UseFallbackSchema(true);
            return provider;
        }
    }
    public class SqlServerUnitRemoteOrchestratorTests : RemoteOrchestratorTests
    {
        public SqlServerUnitRemoteOrchestratorTests(ITestOutputHelper output, DatabaseServerFixture fixture)
            : base(output, fixture)
        {
        }

        public override ProviderType ServerProviderType => ProviderType.Sql;

        private string sqlRandomDatabaseName => HelperDatabase.GetPerTestName(this.GetType(), "ut3_sql_");

        public override IEnumerable<CoreProvider> GetClientProviders()
        {
            yield return HelperDatabase.GetSyncProvider(ProviderType.Sql, this.sqlRandomDatabaseName, true);
        }
    }

    public class SqlServerChangeTrackingUnitRemoteOrchestratorTests : RemoteOrchestratorTests
    {
        public SqlServerChangeTrackingUnitRemoteOrchestratorTests(ITestOutputHelper output, DatabaseServerFixture fixture)
            : base(output, fixture)
        {
        }

        private string sqlRandomClientDatabaseName => HelperDatabase.GetPerTestName(this.GetType(), "ut3_sql_server_ct_");
        private string sqlRandomServerDatabaseName => HelperDatabase.GetPerTestName(this.GetType(), "ut3_sql_client_ct_");

        public override ProviderType ServerProviderType => ProviderType.Sql;

        public override IEnumerable<CoreProvider> GetClientProviders()
        {
            var cstring = HelperDatabase.GetSqlDatabaseConnectionString(this.sqlRandomClientDatabaseName);
            var provider = new SqlSyncChangeTrackingProvider(cstring);
            provider.UseFallbackSchema(true);

            yield return provider;
        }

        public override CoreProvider GetServerProvider()
        {
            var cstring = HelperDatabase.GetSqlDatabaseConnectionString(this.sqlRandomServerDatabaseName);
            var provider = new SqlSyncChangeTrackingProvider(cstring);
            provider.UseFallbackSchema(true);
            return provider;
        }
    }
    public class SqlServerTcpTests : TcpTests
    {
        public SqlServerTcpTests(ITestOutputHelper output, DatabaseServerFixture fixture)
            : base(output, fixture)
        {
        }

        public override ProviderType ServerProviderType => ProviderType.Sql;

        private string sqliteRandomDatabaseName => HelperDatabase.GetPerTestName(this.GetType(), "tcp_sqlite_");
        private string sqlClientRandomDatabaseName => HelperDatabase.GetPerTestName(this.GetType(), "tcp_sql_");

        public override IEnumerable<CoreProvider> GetClientProviders()
        {
            yield return HelperDatabase.GetSyncProvider(ProviderType.Sqlite, this.sqliteRandomDatabaseName, false);
            yield return HelperDatabase.GetSyncProvider(ProviderType.Sql, this.sqlClientRandomDatabaseName, true);
        }
    }

    public class SqlServerChangeTrackingTcpTests : TcpTests
    {
        public SqlServerChangeTrackingTcpTests(ITestOutputHelper output, DatabaseServerFixture fixture)
            : base(output, fixture)
        {
        }

        public override ProviderType ServerProviderType => ProviderType.Sql;

        private string sqliteRandomDatabaseName => HelperDatabase.GetPerTestName(this.GetType(), "tcpct_sqlite_");
        private string sqlClientRandomDatabaseName => HelperDatabase.GetPerTestName(this.GetType(), "tcpct_sql_");
        private string sqlServerRandomDatabaseName => HelperDatabase.GetPerTestName(this.GetType(), "tcpct_sql_");

        public override IEnumerable<CoreProvider> GetClientProviders()
        {
            //yield return HelperDatabase.GetSyncProvider(ProviderType.Sqlite, sqliteRandomDatabaseName, false);

            var cstring = HelperDatabase.GetSqlDatabaseConnectionString(sqlClientRandomDatabaseName);
            var provider = new SqlSyncChangeTrackingProvider(cstring);
            provider.UseFallbackSchema(true);
            yield return provider;

        }

        public override CoreProvider GetServerProvider()
        {
            var cstring = HelperDatabase.GetSqlDatabaseConnectionString(this.sqlServerRandomDatabaseName);
            var provider = new SqlSyncChangeTrackingProvider(cstring);
            provider.UseFallbackSchema(true);
            return provider;
        }
    }
    public class SqlServerTcpFilterTests : TcpFilterTests
    {
        public SqlServerTcpFilterTests(ITestOutputHelper output, DatabaseServerFixture fixture)
            : base(output, fixture)
        {
        }

        public override ProviderType ServerProviderType => ProviderType.Sql;

        private string sqliteRandomDatabaseName => HelperDatabase.GetPerTestName(this.GetType(), "tcpf_sqlite_");
        private string sqlClientRandomDatabaseName => HelperDatabase.GetPerTestName(this.GetType(), "tcpf_sql_");

        public override IEnumerable<CoreProvider> GetClientProviders()
        {
            yield return HelperDatabase.GetSyncProvider(ProviderType.Sqlite, this.sqliteRandomDatabaseName, false);
            yield return HelperDatabase.GetSyncProvider(ProviderType.Sql, this.sqlClientRandomDatabaseName, true);
        }
    }

    public class SqlServerChangeTrackingTcpFilterTests : TcpFilterTests
    {
        public SqlServerChangeTrackingTcpFilterTests(ITestOutputHelper output, DatabaseServerFixture fixture)
            : base(output, fixture)
        {
        }

        public override ProviderType ServerProviderType => ProviderType.Sql;

        private string sqliteRandomDatabaseName => HelperDatabase.GetPerTestName(this.GetType(), "tcpctf_sqlite_");
        private string sqlClientRandomDatabaseName => HelperDatabase.GetPerTestName(this.GetType(), "tcpctf_sql_");
        private string sqlServerRandomDatabaseName => HelperDatabase.GetPerTestName(this.GetType(), "tcpctf_sql_");

        public override IEnumerable<CoreProvider> GetClientProviders()
        {
            yield return HelperDatabase.GetSyncProvider(ProviderType.Sqlite, this.sqliteRandomDatabaseName, false);

            var cstring = HelperDatabase.GetSqlDatabaseConnectionString(this.sqlClientRandomDatabaseName);
            var provider = new SqlSyncChangeTrackingProvider(cstring);
            provider.UseFallbackSchema(true);

            yield return provider;
        }

        public override CoreProvider GetServerProvider()
        {
            var cstring = HelperDatabase.GetSqlDatabaseConnectionString(this.sqlServerRandomDatabaseName);
            var provider = new SqlSyncChangeTrackingProvider(cstring);
            provider.UseFallbackSchema(true);
            return provider;
        }
    }
    public class SqlServerHttpTests : HttpTests
    {
        public SqlServerHttpTests(ITestOutputHelper output, DatabaseServerFixture fixture)
            : base(output, fixture)
        {
        }

        public override ProviderType ServerProviderType => ProviderType.Sql;

        private string sqliteRandomDatabaseName => HelperDatabase.GetPerTestName(this.GetType(), "http_sqlite_");
        private string sqlClientRandomDatabaseName => HelperDatabase.GetPerTestName(this.GetType(), "http_sql_");

        public override IEnumerable<CoreProvider> GetClientProviders()
        {
            yield return HelperDatabase.GetSyncProvider(ProviderType.Sqlite, this.sqliteRandomDatabaseName, false);
            yield return HelperDatabase.GetSyncProvider(ProviderType.Sql, this.sqlClientRandomDatabaseName, true);
        }
    }

    public class SqlServerChangeTrackingHttpFilterTests : HttpTests
    {
        public SqlServerChangeTrackingHttpFilterTests(ITestOutputHelper output, DatabaseServerFixture fixture)
            : base(output, fixture)
        {
        }

        public override ProviderType ServerProviderType => ProviderType.Sql;

        private string sqliteRandomDatabaseName => HelperDatabase.GetPerTestName(this.GetType(), "httpctf_sqlite_");
        private string sqlClientRandomDatabaseName => HelperDatabase.GetPerTestName(this.GetType(), "httpctf_sql_");
        private string sqlServerRandomDatabaseName => HelperDatabase.GetPerTestName(this.GetType(), "httpctf_sql_");

        public override IEnumerable<CoreProvider> GetClientProviders()
        {
            yield return HelperDatabase.GetSyncProvider(ProviderType.Sqlite, sqliteRandomDatabaseName, false);
            var cstring = HelperDatabase.GetSqlDatabaseConnectionString(sqlClientRandomDatabaseName);
            var provider = new SqlSyncChangeTrackingProvider(cstring);
            provider.UseFallbackSchema(true);
            yield return provider;
        }

        public override CoreProvider GetServerProvider()
        {
            var cstring = HelperDatabase.GetSqlDatabaseConnectionString(this.sqlServerRandomDatabaseName);
            var provider = new SqlSyncChangeTrackingProvider(cstring);
            provider.UseFallbackSchema(true);
            return provider;
        }
    }
    public class SqlServerConflictTests : TcpConflictsTests
    {
        public SqlServerConflictTests(ITestOutputHelper output, DatabaseServerFixture fixture)
            : base(output, fixture)
        {
        }

        public override ProviderType ServerProviderType => ProviderType.Sql;

        private string sqliteRandomDatabaseName => HelperDatabase.GetPerTestName(this.GetType(), "tcpc_sqlite_");
        private string sqlClientRandomDatabaseName => HelperDatabase.GetPerTestName(this.GetType(), "tcpc_sql_");

        public override IEnumerable<CoreProvider> GetClientProviders()
        {
            yield return HelperDatabase.GetSyncProvider(ProviderType.Sqlite, this.sqliteRandomDatabaseName, false);
            yield return HelperDatabase.GetSyncProvider(ProviderType.Sql, this.sqlClientRandomDatabaseName, true);
        }
    }

    public class SqlServerChangeTrackingConflictTests : TcpConflictsTests
    {
        public SqlServerChangeTrackingConflictTests(ITestOutputHelper output, DatabaseServerFixture fixture)
            : base(output, fixture)
        {
        }

        public override ProviderType ServerProviderType => ProviderType.Sql;

        private string sqliteRandomDatabaseName => HelperDatabase.GetPerTestName(this.GetType(), "tcpctc_sqlite_");
        private string sqlClientRandomDatabaseName => HelperDatabase.GetPerTestName(this.GetType(), "tcpctc_sql_");
        private string sqlServerRandomDatabaseName => HelperDatabase.GetPerTestName(this.GetType(), "httpctf_sql_");

        public override IEnumerable<CoreProvider> GetClientProviders()
        {
            yield return HelperDatabase.GetSyncProvider(ProviderType.Sqlite, this.sqliteRandomDatabaseName, false);

            var cstring = HelperDatabase.GetSqlDatabaseConnectionString(this.sqlClientRandomDatabaseName);
            var provider = new SqlSyncChangeTrackingProvider(cstring);
            provider.UseFallbackSchema(true);
            yield return provider;
        }

        public override CoreProvider GetServerProvider()
        {
            var cstring = HelperDatabase.GetSqlDatabaseConnectionString(this.sqlServerRandomDatabaseName);
            var provider = new SqlSyncChangeTrackingProvider(cstring);
            provider.UseFallbackSchema(true);
            return provider;
        }

        public override Task Conflict_UC_OUTDATED_ServerShouldWins() => Task.CompletedTask;

        public override Task Conflict_UC_OUTDATED_ServerShouldWins_EvenIf_ResolutionIsClientWins() => Task.CompletedTask;
    }
    public class PostgresConflictTests : TcpConflictsTests
    {
        public PostgresConflictTests(ITestOutputHelper output, DatabaseServerFixture fixture)
            : base(output, fixture)
        {
        }

        public override ProviderType ServerProviderType => ProviderType.Postgres;

        private string sqliteRandomDatabaseName => HelperDatabase.GetPerTestName(this.GetType(), "tcpc_npg_sqlite_");
        private string postgreClientRandomDatabaseName => HelperDatabase.GetPerTestName(this.GetType(), "tcpc_npg_");

        public override IEnumerable<CoreProvider> GetClientProviders()
        {
            yield return HelperDatabase.GetSyncProvider(ProviderType.Sqlite, this.sqliteRandomDatabaseName, false);
            yield return HelperDatabase.GetSyncProvider(ProviderType.Postgres, this.postgreClientRandomDatabaseName, true);
        }
    }
    public class PostgresTcpTests : TcpTests
    {
        public PostgresTcpTests(ITestOutputHelper output, DatabaseServerFixture fixture)
            : base(output, fixture)
        {
        }

        public override ProviderType ServerProviderType => ProviderType.Postgres;

        private string sqliteRandomDatabaseName => HelperDatabase.GetPerTestName(this.GetType(), "tcp_npg_sqlite_");
        private string postgreClientRandomDatabaseName => HelperDatabase.GetPerTestName(this.GetType(), "tcp_npg_");

        public override IEnumerable<CoreProvider> GetClientProviders()
        {
            yield return HelperDatabase.GetSyncProvider(ProviderType.Sqlite, this.sqliteRandomDatabaseName, false);
            yield return HelperDatabase.GetSyncProvider(ProviderType.Postgres, this.postgreClientRandomDatabaseName, true);
        }
    }
    public class PostgresTcpFilterTests : TcpFilterTests
    {
        public PostgresTcpFilterTests(ITestOutputHelper output, DatabaseServerFixture fixture)
            : base(output, fixture)
        {
        }

        public override ProviderType ServerProviderType => ProviderType.Postgres;

        private string sqliteRandomDatabaseName => HelperDatabase.GetPerTestName(this.GetType(), "tcfp_npg_sqlite_");
        private string postgreClientRandomDatabaseName => HelperDatabase.GetPerTestName(this.GetType(), "tcpf_npg_");

        public override IEnumerable<CoreProvider> GetClientProviders()
        {
            yield return HelperDatabase.GetSyncProvider(ProviderType.Sqlite, this.sqliteRandomDatabaseName, false);
            yield return HelperDatabase.GetSyncProvider(ProviderType.Postgres, this.postgreClientRandomDatabaseName, true);
        }
    }
    public class PostgresHttpTests : HttpTests
    {
        public PostgresHttpTests(ITestOutputHelper output, DatabaseServerFixture fixture)
            : base(output, fixture)
        {
        }

        public override ProviderType ServerProviderType => ProviderType.Postgres;

        private string sqliteRandomDatabaseName => HelperDatabase.GetPerTestName(this.GetType(), "http_npg_sqlite_");
        private string postgreClientRandomDatabaseName => HelperDatabase.GetPerTestName(this.GetType(), "http_npg_");

        public override IEnumerable<CoreProvider> GetClientProviders()
        {
            yield return HelperDatabase.GetSyncProvider(ProviderType.Sqlite, this.sqliteRandomDatabaseName, false);
            yield return HelperDatabase.GetSyncProvider(ProviderType.Postgres, this.postgreClientRandomDatabaseName, true);
        }
    }

    public class MySqlTcpTests : TcpTests
    {
        public MySqlTcpTests(ITestOutputHelper output, DatabaseServerFixture fixture)
            : base(output, fixture)
        {
        }

        public override ProviderType ServerProviderType => ProviderType.MySql;

        private string sqliteRandomDatabaseName => HelperDatabase.GetPerTestName(this.GetType(), "tcp_mysql_sqlite_");
        private string mysqlClientRandomDatabaseName => HelperDatabase.GetPerTestName(this.GetType(), "tcp_mysql_");

        public override IEnumerable<CoreProvider> GetClientProviders()
        {
            yield return HelperDatabase.GetSyncProvider(ProviderType.Sqlite, this.sqliteRandomDatabaseName, false);
            yield return HelperDatabase.GetSyncProvider(ProviderType.MySql, this.mysqlClientRandomDatabaseName, false);
        }
    }
    public class MySqlTcpFilterTests : TcpFilterTests
    {
        public MySqlTcpFilterTests(ITestOutputHelper output, DatabaseServerFixture fixture)
            : base(output, fixture)
        {
        }

        public override ProviderType ServerProviderType => ProviderType.MySql;

        private string sqliteRandomDatabaseName => HelperDatabase.GetPerTestName(this.GetType(), "tcpf_mysql_sqlite_");
        private string mysqlClientRandomDatabaseName => HelperDatabase.GetPerTestName(this.GetType(), "tcpf_mysql_");

        public override IEnumerable<CoreProvider> GetClientProviders()
        {
            yield return HelperDatabase.GetSyncProvider(ProviderType.Sqlite, this.sqliteRandomDatabaseName, false);
            yield return HelperDatabase.GetSyncProvider(ProviderType.MySql, this.mysqlClientRandomDatabaseName, false);
        }
    }
    public class MySqlHttpTests : HttpTests
    {
        public MySqlHttpTests(ITestOutputHelper output, DatabaseServerFixture fixture)
            : base(output, fixture)
        {
        }

        public override ProviderType ServerProviderType => ProviderType.MySql;

        private string sqliteRandomDatabaseName => HelperDatabase.GetPerTestName(this.GetType(), "http_mysql_sqlite_");
        private string mysqlClientRandomDatabaseName => HelperDatabase.GetPerTestName(this.GetType(), "http_mysql_");

        public override IEnumerable<CoreProvider> GetClientProviders()
        {
            yield return HelperDatabase.GetSyncProvider(ProviderType.Sqlite, this.sqliteRandomDatabaseName, false);
            yield return HelperDatabase.GetSyncProvider(ProviderType.MySql, this.mysqlClientRandomDatabaseName, false);
        }
    }
    public class MySqlConflictTests : TcpConflictsTests
    {
        public MySqlConflictTests(ITestOutputHelper output, DatabaseServerFixture fixture)
            : base(output, fixture)
        {
        }

        public override ProviderType ServerProviderType => ProviderType.MySql;

        private string sqliteRandomDatabaseName => HelperDatabase.GetPerTestName(this.GetType(), "tcpc_mysql_sqlite_");
        private string mysqlClientRandomDatabaseName => HelperDatabase.GetPerTestName(this.GetType(), "tcpc_mysql_");

        public override IEnumerable<CoreProvider> GetClientProviders()
        {
            yield return HelperDatabase.GetSyncProvider(ProviderType.Sqlite, this.sqliteRandomDatabaseName, false);
            yield return HelperDatabase.GetSyncProvider(ProviderType.MySql, this.mysqlClientRandomDatabaseName, false);
        }
    }
    public class MariaDBTcpTests : TcpTests
    {
        public MariaDBTcpTests(ITestOutputHelper output, DatabaseServerFixture fixture)
            : base(output, fixture)
        {
        }

        public override ProviderType ServerProviderType => ProviderType.MariaDB;

        private string sqliteRandomDatabaseName => HelperDatabase.GetPerTestName(this.GetType(), "tcp_maria_sqlite_");
        private string mariaClientRandomDatabaseName => HelperDatabase.GetPerTestName(this.GetType(), "tcp_maria_maria_");

        public override IEnumerable<CoreProvider> GetClientProviders()
        {
            yield return HelperDatabase.GetSyncProvider(ProviderType.Sqlite, sqliteRandomDatabaseName, false);
            yield return HelperDatabase.GetSyncProvider(ProviderType.MariaDB, mariaClientRandomDatabaseName, false);
        }
    }
    public class MariaDBTcpFilterTests : TcpFilterTests
    {
        public MariaDBTcpFilterTests(ITestOutputHelper output, DatabaseServerFixture fixture)
            : base(output, fixture)
        {
        }

        public override ProviderType ServerProviderType => ProviderType.MariaDB;

        private string sqliteRandomDatabaseName => HelperDatabase.GetPerTestName(this.GetType(), "tcpf_maria_sqlite_");
        private string mariaClientRandomDatabaseName => HelperDatabase.GetPerTestName(this.GetType(), "tcpf_maria_maria_");

        public override IEnumerable<CoreProvider> GetClientProviders()
        {
            yield return HelperDatabase.GetSyncProvider(ProviderType.Sqlite, sqliteRandomDatabaseName, false);
            yield return HelperDatabase.GetSyncProvider(ProviderType.MariaDB, mariaClientRandomDatabaseName, false);
        }
    }
    public class MariaDBHttpTests : TcpTests
    {
        public MariaDBHttpTests(ITestOutputHelper output, DatabaseServerFixture fixture)
            : base(output, fixture)
        {
        }

        public override ProviderType ServerProviderType => ProviderType.MariaDB;

        private string sqliteRandomDatabaseName => HelperDatabase.GetPerTestName(this.GetType(), "http_maria_sqlite_");
        private string mariaClientRandomDatabaseName => HelperDatabase.GetPerTestName(this.GetType(), "http_maria_maria_");

        public override IEnumerable<CoreProvider> GetClientProviders()
        {
            yield return HelperDatabase.GetSyncProvider(ProviderType.Sqlite, sqliteRandomDatabaseName, false);
            yield return HelperDatabase.GetSyncProvider(ProviderType.MariaDB, mariaClientRandomDatabaseName, false);
        }
    }
}