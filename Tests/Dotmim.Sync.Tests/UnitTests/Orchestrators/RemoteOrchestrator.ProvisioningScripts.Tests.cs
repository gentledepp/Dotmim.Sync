using Wormhole.Sync.Enumerations;
using Wormhole.Sync.SqlServer;
using Wormhole.Sync.Sqlite;
using Wormhole.Sync.Tests.Core;
using Wormhole.Sync.Tests.Fixtures;
using Wormhole.Sync.Tests.Misc;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using System;
using System.Data.Common;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Wormhole.Sync.Tests.UnitTests
{
    public class RemoteOrchestratorProvisioningScriptsTests : IDisposable
    {
        private readonly ITestOutputHelper output;

        public RemoteOrchestratorProvisioningScriptsTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Fact]
        public async Task GetProvisioningSqlScripts_SingleTable_SqlServer()
        {
            var dbName = HelperDatabase.GetRandomName("tcp_prov_");
            await HelperDatabase.CreateDatabaseAsync(ProviderType.Sql, dbName, true);
            var cs = HelperDatabase.GetConnectionString(ProviderType.Sql, dbName);

            // Create a simple ProductCategory table
            using (var connection = new SqlConnection(cs))
            {
                connection.Open();
                var commandText = @"
                    CREATE TABLE [dbo].[ProductCategory] (
                        [ProductCategoryID] [uniqueidentifier] NOT NULL PRIMARY KEY DEFAULT (NEWID()),
                        [Name] [nvarchar](50) NOT NULL,
                        [ModifiedDate] [datetime] NULL
                    )";
                using var cmd = new SqlCommand(commandText, connection);
                cmd.ExecuteNonQuery();
            }

            var setup = new SyncSetup("ProductCategory");
            setup.Tables["ProductCategory"].Columns.AddRange("ProductCategoryID", "Name", "ModifiedDate");

            var provider = new SqlSyncProvider(cs);
            var orchestrator = new RemoteOrchestrator(provider);

            // Act
            var scripts = await orchestrator.GetProvisioningSqlScriptsAsync(setup);

            // Assert
            output.WriteLine("========== SQL Server Single Table Scripts ==========");
            output.WriteLine(scripts);
            output.WriteLine("====================================================");
            
            await VerifyXunit.Verifier.Verify(scripts);

            HelperDatabase.DropDatabase(ProviderType.Sql, dbName);
        }

        [Fact]
        public async Task GetProvisioningSqlScripts_SingleTable_WithCustomTriggerAndTrackingTableNames_SqlServer()
        {
            var dbName = HelperDatabase.GetRandomName("tcp_prov_");
            await HelperDatabase.CreateDatabaseAsync(ProviderType.Sql, dbName, true);
            var cs = HelperDatabase.GetConnectionString(ProviderType.Sql, dbName);

            // Create a simple ProductCategory table
            using (var connection = new SqlConnection(cs))
            {
                connection.Open();
                var commandText = @"
                    CREATE TABLE [dbo].[ProductCategory] (
                        [ProductCategoryID] [uniqueidentifier] NOT NULL PRIMARY KEY DEFAULT (NEWID()),
                        [Name] [nvarchar](50) NOT NULL,
                        [ModifiedDate] [datetime] NULL
                    )";
                using var cmd = new SqlCommand(commandText, connection);
                cmd.ExecuteNonQuery();
            }

            var setup = new SyncSetup("ProductCategory");
            setup.Tables["ProductCategory"].Columns.AddRange("ProductCategoryID", "Name", "ModifiedDate");
            setup.Tables["ProductCategory"]
                .WithInsertTriggerName("my_insert_trigger")
                .WithUpdateTriggerName("my_update_trigger")
                .WithDeleteTriggerName("my_delete_trigger")
                .WithTrackingTableName("my_tracking_table");

            var provider = new SqlSyncProvider(cs);
            var orchestrator = new RemoteOrchestrator(provider);

            // Act
            var scripts = await orchestrator.GetProvisioningSqlScriptsAsync(setup);

            // Assert
            output.WriteLine("========== SQL Server Single Table Scripts ==========");
            output.WriteLine(scripts);
            output.WriteLine("====================================================");

            await VerifyXunit.Verifier.Verify(scripts);

            HelperDatabase.DropDatabase(ProviderType.Sql, dbName);
        }

        [Fact]
        public async Task GetProvisioningSqlScripts_TwoRelatedTables_SqlServer()
        {
            var dbName = HelperDatabase.GetRandomName("tcp_prov_");
            await HelperDatabase.CreateDatabaseAsync(ProviderType.Sql, dbName, true);
            var cs = HelperDatabase.GetConnectionString(ProviderType.Sql, dbName);

            // Create ProductCategory and Product tables with FK relationship
            using (var connection = new SqlConnection(cs))
            {
                connection.Open();

                var commandText = @"
                    CREATE TABLE [dbo].[ProductCategory] (
                        [ProductCategoryID] [uniqueidentifier] NOT NULL PRIMARY KEY DEFAULT (NEWID()),
                        [Name] [nvarchar](50) NOT NULL,
                        [ModifiedDate] [datetime] NULL
                    );

                    CREATE TABLE [dbo].[Product] (
                        [ProductID] [uniqueidentifier] NOT NULL PRIMARY KEY DEFAULT (NEWID()),
                        [Name] [nvarchar](50) NOT NULL,
                        [ProductNumber] [nvarchar](25) NULL,
                        [ProductCategoryID] [uniqueidentifier] NULL,
                        [ModifiedDate] [datetime] NULL,
                        CONSTRAINT [FK_Product_ProductCategory] FOREIGN KEY ([ProductCategoryID])
                            REFERENCES [dbo].[ProductCategory] ([ProductCategoryID])
                    )";

                using var cmd = new SqlCommand(commandText, connection);
                cmd.ExecuteNonQuery();
            }

            var setup = new SyncSetup("ProductCategory", "Product");

            // Explicitly set columns for ProductCategory
            setup.Tables["ProductCategory"].Columns.AddRange("ProductCategoryID", "Name", "ModifiedDate");

            // Explicitly set columns for Product
            setup.Tables["Product"].Columns.AddRange("ProductID", "Name", "ProductNumber", "ProductCategoryID", "ModifiedDate");

            var provider = new SqlSyncProvider(cs);
            var orchestrator = new RemoteOrchestrator(provider);

            // Act
            var scripts = await orchestrator.GetProvisioningSqlScriptsAsync(setup);

            // Assert
            output.WriteLine("========== Table Scripts ==========");
            output.WriteLine(scripts);
            output.WriteLine("=================================================");

            await VerifyXunit.Verifier.Verify(scripts);

            HelperDatabase.DropDatabase(ProviderType.Sql, dbName);
        }

        [Fact]
        public async Task GetProvisioningSqlScripts_SingleTable_Sqlite()
        {
            var dbName = HelperDatabase.GetRandomName("sqlite_prov_") + ".db";
            var cs = HelperDatabase.GetSqliteDatabaseConnectionString(dbName);

            // Create a simple ProductCategory table
            using (var connection = new SqliteConnection(cs))
            {
                connection.Open();
                var commandText = @"
                    CREATE TABLE [ProductCategory] (
                        [ProductCategoryID] TEXT NOT NULL PRIMARY KEY,
                        [Name] TEXT NOT NULL,
                        [ModifiedDate] TEXT NULL
                    )";
                using var cmd = new SqliteCommand(commandText, connection);
                cmd.ExecuteNonQuery();
            }

            var setup = new SyncSetup("ProductCategory");
            setup.Tables["ProductCategory"].Columns.AddRange("ProductCategoryID", "Name", "ModifiedDate");

            var provider = new SqliteSyncProvider(cs);
            var orchestrator = new LocalOrchestrator(provider);

            // Act
            var scripts = await orchestrator.GetProvisioningSqlScriptsAsync(setup);

            // Assert

            output.WriteLine("========== SQLite Single Table Scripts ==========");
            output.WriteLine(scripts);
            output.WriteLine("=================================================");
            
            await VerifyXunit.Verifier.Verify(scripts);

            // Clean up - clear SQLite connection pool and delete file
            SqliteConnection.ClearAllPools();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            if (File.Exists(dbName))
                File.Delete(dbName);
        }

        [Fact]
        public async Task GetProvisioningSqlScripts_TwoRelatedTables_Sqlite()
        {
            var dbName = HelperDatabase.GetRandomName("sqlite_prov_") + ".db";
            var cs = HelperDatabase.GetSqliteDatabaseConnectionString(dbName);

            // Create ProductCategory and Product tables with FK relationship
            using (var connection = new SqliteConnection(cs))
            {
                connection.Open();

                var commandText = @"
                    CREATE TABLE [ProductCategory] (
                        [ProductCategoryID] TEXT NOT NULL PRIMARY KEY,
                        [Name] TEXT NOT NULL,
                        [ModifiedDate] TEXT NULL
                    );

                    CREATE TABLE [Product] (
                        [ProductID] TEXT NOT NULL PRIMARY KEY,
                        [Name] TEXT NOT NULL,
                        [ProductNumber] TEXT NULL,
                        [ProductCategoryID] TEXT NULL,
                        [ModifiedDate] TEXT NULL,
                        FOREIGN KEY ([ProductCategoryID]) REFERENCES [ProductCategory] ([ProductCategoryID])
                    )";

                using var cmd = new SqliteCommand(commandText, connection);
                cmd.ExecuteNonQuery();
            }

            var setup = new SyncSetup("ProductCategory", "Product");

            // Explicitly set columns for ProductCategory
            setup.Tables["ProductCategory"].Columns.AddRange("ProductCategoryID", "Name", "ModifiedDate");

            // Explicitly set columns for Product
            setup.Tables["Product"].Columns.AddRange("ProductID", "Name", "ProductNumber", "ProductCategoryID", "ModifiedDate");

            var provider = new SqliteSyncProvider(cs);
            var orchestrator = new LocalOrchestrator(provider);

            // Act
            var scripts = await orchestrator.GetProvisioningSqlScriptsAsync(setup);

            // Assert
            output.WriteLine("========== SQLite Two Related Tables Scripts ==========");
            output.WriteLine(scripts);
            output.WriteLine("=======================================================");
            
            await VerifyXunit.Verifier.Verify(scripts);


            // Clean up - clear SQLite connection pool and delete file
            SqliteConnection.ClearAllPools();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            if (File.Exists(dbName))
                File.Delete(dbName);
        }

        public void Dispose()
        {
        }
    }
}
