using System;
using System.Collections.Generic;
using Wormhole.Sync.Storage;
using Wormhole.Sync.Tests.Core;
using Wormhole.Sync.Tests.Fixtures;
using Wormhole.Sync.Tests.IntegrationTests;
using Wormhole.Sync.Tests.Misc;
using Wormhole.Sync.Tests.UnitTests.Storage;
using Wormhole.Sync.Web.Azure;
using Xunit;

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


    public class SqlServerHttpTests : HttpTests
    {
        public SqlServerHttpTests(ITestOutputHelper output, DatabaseServerFixture fixture)
            : base(output, fixture)
        {
        }

        public override ProviderType ServerProviderType => ProviderType.Sql;

        private string sqliteRandomDatabaseName = HelperDatabase.GetRandomName("http_sqlite_webapi2_");
        //private string sqlClientRandomDatabaseName = HelperDatabase.GetRandomName("http_sql_");

        public override IEnumerable<CoreProvider> GetClientProviders()
        {
            yield return HelperDatabase.GetSyncProvider(ProviderType.Sqlite, this.sqliteRandomDatabaseName, false);
            //yield return HelperDatabase.GetSyncProvider(ProviderType.Sql, this.sqlClientRandomDatabaseName, true);
        }
    }

    public class SqlServerAzureStorageHttpTests : HttpTests
    {
        public SqlServerAzureStorageHttpTests(ITestOutputHelper output, DatabaseServerFixture fixture)
            : base(output, fixture, new AzureBlobBatchStorage(AzureBlobBatchStorageTests.AzuriteConnectionString, "sql-server-webapi2-azureblob"))
        {
        }

        public override ProviderType ServerProviderType => ProviderType.Sql;

        private string sqliteRandomDatabaseName = HelperDatabase.GetRandomName("http_sqlite_");
        private string sqlClientRandomDatabaseName = HelperDatabase.GetRandomName("http_sql_");

        public override IEnumerable<CoreProvider> GetClientProviders()
        {
            yield return HelperDatabase.GetSyncProvider(ProviderType.Sqlite, this.sqliteRandomDatabaseName, false);
            //yield return HelperDatabase.GetSyncProvider(ProviderType.Sql, this.sqlClientRandomDatabaseName, true);
        }
    }

    /// <summary>
    /// WebApi2 async batch creation tests using Hangfire with in-memory storage.
    /// These tests verify that async batch creation works in NET48 OWIN environment using Hangfire.
    /// </summary>
    public class SqlServerAsyncBatchHttpTests : HttpAsyncBatchTests
    {
        public SqlServerAsyncBatchHttpTests(ITestOutputHelper output, DatabaseServerFixture fixture)
            : base(output, fixture, batchStorage: null)
        {
        }

        public override ProviderType ServerProviderType => ProviderType.Sql;

        private string sqliteRandomDatabaseName = HelperDatabase.GetRandomName("http_async_sqlite_webapi2_");

        public override IEnumerable<CoreProvider> GetClientProviders()
        {
            yield return HelperDatabase.GetSyncProvider(ProviderType.Sqlite, this.sqliteRandomDatabaseName, false);
        }
    }

    /// <summary>
    /// WebApi2 async batch creation tests with Azure Blob Storage and Hangfire.
    /// </summary>
    public class SqlServerAzureStorageAsyncBatchHttpTests : HttpAsyncBatchTests
    {
        public SqlServerAzureStorageAsyncBatchHttpTests(ITestOutputHelper output, DatabaseServerFixture fixture)
            : base(output, fixture, new AzureBlobBatchStorage(AzureBlobBatchStorageTests.AzuriteConnectionString, "sql-webapi2-async-azureblob"))
        {
        }

        public override ProviderType ServerProviderType => ProviderType.Sql;

        private string sqliteRandomDatabaseName = HelperDatabase.GetRandomName("http_async_azure_sqlite_webapi2_");

        public override IEnumerable<CoreProvider> GetClientProviders()
        {
            yield return HelperDatabase.GetSyncProvider(ProviderType.Sqlite, this.sqliteRandomDatabaseName, false);
        }
    }

}
