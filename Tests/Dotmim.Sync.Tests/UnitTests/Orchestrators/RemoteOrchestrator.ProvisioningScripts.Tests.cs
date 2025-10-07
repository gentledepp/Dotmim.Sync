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
using VerifyXunit;
using Wormhole.Sync.Builders;
using Xunit;
using Xunit.Abstractions;

namespace Wormhole.Sync.Tests.UnitTests
{
    public class RemoteOrchestratorProvisioningScriptsTests : IDisposable
    {
        private readonly ITestOutputHelper output;
        private string dbName;

        public RemoteOrchestratorProvisioningScriptsTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Fact]
        public async Task GetProvisioningSqlScripts_SingleTable_SqlServer()
        {
            this.dbName = HelperDatabase.GetRandomName("tcp_prov_");
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
            
            await Verifier.Verify(scripts);

            
        }

        [Fact]
        public async Task GetProvisioningSqlScripts_SingleTable_WithCustomTriggerAndTrackingTableNames_SqlServer()
        {
            this.dbName = HelperDatabase.GetRandomName("tcp_prov_");
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

            await Verifier.Verify(scripts);

            
        }

        [Fact]
        public async Task GetProvisioningSqlScripts_TwoRelatedTables_SqlServer()
        {
            this.dbName = HelperDatabase.GetRandomName("tcp_prov_");
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

            await Verifier.Verify(scripts);

            
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
            
            await Verifier.Verify(scripts);

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
            
            await Verifier.Verify(scripts);


            // Clean up - clear SQLite connection pool and delete file
            SqliteConnection.ClearAllPools();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            if (File.Exists(dbName))
                File.Delete(dbName);
        }

        [Fact]
        public async Task GetProvisioningSqlScripts_WithSetupTableTriggerInterceptor_ShouldModifyTriggerScript()
        {
            this.dbName = HelperDatabase.GetRandomName("tcp_prov_interceptor_");
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

            setup.Tables["ProductCategory"].OnTriggerCreating(args =>
            {
                if (args.TriggerType == DbTriggerType.Insert)
                {
                    args.Command.CommandText = "-- CUSTOM INSERT TRIGGER INTERCEPTED\n" + args.Command.CommandText;
                }
            });

            var provider = new SqlSyncProvider(cs);
            var orchestrator = new RemoteOrchestrator(provider);

            // Act
            var scripts = await orchestrator.GetProvisioningSqlScriptsAsync(setup);

            // Assert
            output.WriteLine("========== Trigger Interceptor Test ==========");
            output.WriteLine(scripts);
            output.WriteLine("===============================================");

            Assert.Contains("-- CUSTOM INSERT TRIGGER INTERCEPTED", scripts);

            
            await Verifier.Verify(scripts);

            
        }

        [Fact]
        public async Task GetProvisioningSqlScripts_WithSetupTableStoredProcedureInterceptor_ShouldModifyStoredProcedureScript()
        {
            this.dbName = HelperDatabase.GetRandomName("tcp_prov_interceptor_");
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

            setup.Tables["ProductCategory"].OnStoredProcedureCreating(args =>
            {
                if (args.StoredProcedureType == DbStoredProcedureType.SelectChanges)
                {
                    args.Command.CommandText = "-- CUSTOM SELECT CHANGES PROCEDURE\n" + args.Command.CommandText;
                }
            });

            var provider = new SqlSyncProvider(cs);
            var orchestrator = new RemoteOrchestrator(provider);

            // Act
            var scripts = await orchestrator.GetProvisioningSqlScriptsAsync(setup);

            // Assert
            output.WriteLine("========== Stored Procedure Interceptor Test ==========");
            output.WriteLine(scripts);
            output.WriteLine("========================================================");

            Assert.Contains("-- CUSTOM SELECT CHANGES PROCEDURE", scripts);
            
            await Verifier.Verify(scripts);

            
        }

        [Fact]
        public async Task GetProvisioningSqlScripts_WithSetupTableTrackingTableInterceptor_ShouldModifyTrackingTableScript()
        {
            this.dbName = HelperDatabase.GetRandomName("tcp_prov_interceptor_");
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

            setup.Tables["ProductCategory"].OnTrackingTableCreating(args =>
            {
                args.Command.CommandText = args.Command.CommandText.Replace(
                    "CREATE TABLE",
                    "-- INTERCEPTED TRACKING TABLE\nCREATE TABLE"
                );
            });

            var provider = new SqlSyncProvider(cs);
            var orchestrator = new RemoteOrchestrator(provider);

            // Act
            var scripts = await orchestrator.GetProvisioningSqlScriptsAsync(setup);

            // Assert
            output.WriteLine("========== Tracking Table Interceptor Test ==========");
            output.WriteLine(scripts);
            output.WriteLine("======================================================");

            Assert.Contains("-- INTERCEPTED TRACKING TABLE", scripts);
            
            await Verifier.Verify(scripts);

            
        }

        [Fact]
        public async Task GetProvisioningSqlScripts_WithSetupTableInterceptorCancelling_ShouldSkipTrigger()
        {
            this.dbName = HelperDatabase.GetRandomName("tcp_prov_interceptor_");
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

            setup.Tables["ProductCategory"].OnTriggerCreating(args =>
            {
                if (args.TriggerType == DbTriggerType.Delete)
                {
                    args.Cancel = true; // Skip delete trigger
                }
            });

            var provider = new SqlSyncProvider(cs);
            var orchestrator = new RemoteOrchestrator(provider);

            // Act
            var scripts = await orchestrator.GetProvisioningSqlScriptsAsync(setup);

            // Assert
            output.WriteLine("========== Trigger Cancellation Test ==========");
            output.WriteLine(scripts);
            output.WriteLine("================================================");

            // Verify that Insert and Update triggers are present
            Assert.Contains("ProductCategory_insert_trigger", scripts, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("ProductCategory_update_trigger", scripts, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("ProductCategory_delete_trigger", scripts, StringComparison.OrdinalIgnoreCase);
            
            await Verifier.Verify(scripts);
            
            
        }

        [Fact]
        public async Task GetProvisioningSqlScripts_WithCustomProvisioningSql_ShouldIncludeCustomSql()
        {
            this.dbName = HelperDatabase.GetRandomName("tcp_prov_custom_");
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

            // Add custom provisioning SQL to create an index
            setup.Tables["ProductCategory"].AddCustomProvisioningSql(
                "CREATE NONCLUSTERED INDEX [IX_ProductCategory_Name] ON [dbo].[ProductCategory] ([Name])"
            );

            var provider = new SqlSyncProvider(cs);
            var orchestrator = new RemoteOrchestrator(provider);

            // Act
            var scripts = await orchestrator.GetProvisioningSqlScriptsAsync(setup);

            // Assert
            output.WriteLine("========== Custom Provisioning SQL Test ==========");
            output.WriteLine(scripts);
            output.WriteLine("===================================================");

            Assert.Contains("-- Custom Provisioning SQL for ProductCategory", scripts);
            Assert.Contains("CREATE NONCLUSTERED INDEX [IX_ProductCategory_Name]", scripts);
            
            await Verifier.Verify(scripts);

            
        }

        [Fact]
        public async Task GetProvisioningSqlScripts_WithMultipleCustomProvisioningSql_ShouldIncludeAllStatements()
        {
            this.dbName = HelperDatabase.GetRandomName("tcp_prov_custom_multi_");
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
                        [Description] [nvarchar](200) NULL,
                        [ModifiedDate] [datetime] NULL
                    )";
                using var cmd = new SqlCommand(commandText, connection);
                cmd.ExecuteNonQuery();
            }

            var setup = new SyncSetup("ProductCategory");
            setup.Tables["ProductCategory"].Columns.AddRange("ProductCategoryID", "Name", "Description", "ModifiedDate");

            // Add multiple custom provisioning SQL statements
            setup.Tables["ProductCategory"]
                .AddCustomProvisioningSql("CREATE NONCLUSTERED INDEX [IX_ProductCategory_Name] ON [dbo].[ProductCategory] ([Name])")
                .AddCustomProvisioningSql("CREATE NONCLUSTERED INDEX [IX_ProductCategory_Description] ON [dbo].[ProductCategory] ([Description])");

            var provider = new SqlSyncProvider(cs);
            var orchestrator = new RemoteOrchestrator(provider);

            // Act
            var scripts = await orchestrator.GetProvisioningSqlScriptsAsync(setup);

            // Assert
            output.WriteLine("========== Multiple Custom Provisioning SQL Test ==========");
            output.WriteLine(scripts);
            output.WriteLine("============================================================");

            Assert.Contains("-- Custom Provisioning SQL for ProductCategory", scripts);
            Assert.Contains("CREATE NONCLUSTERED INDEX [IX_ProductCategory_Name]", scripts);
            Assert.Contains("CREATE NONCLUSTERED INDEX [IX_ProductCategory_Description]", scripts);

            await Verifier.Verify(scripts);

            
        }

        [Fact]
        public async Task GetProvisioningSqlScripts_WithTrackedColumns_SqlServer()
        {
            this.dbName = HelperDatabase.GetRandomName("tcp_prov_tracked_");
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

            // Explicitly set columns
            setup.Tables["ProductCategory"].Columns.AddRange("ProductCategoryID", "Name", "ModifiedDate");
            setup.Tables["Product"].Columns.AddRange("ProductID", "Name", "ProductNumber", "ProductCategoryID", "ModifiedDate");

            // Add ProductCategoryID as a tracked column on Product table
            setup.Tables["Product"].AddTrackedColumn("ProductCategoryID");

            var provider = new SqlSyncProvider(cs);
            var orchestrator = new RemoteOrchestrator(provider);

            // Act
            var scripts = await orchestrator.GetProvisioningSqlScriptsAsync(setup);

            // Assert
            output.WriteLine("========== Tracked Columns Test ==========");
            output.WriteLine(scripts);
            output.WriteLine("===========================================");

            // Verify that ProductCategoryID appears in the Product tracking table
            Assert.Contains("Product_tracking", scripts);
            var trackingTableSection = scripts.Substring(scripts.IndexOf("CREATE TABLE") + scripts.Substring(scripts.IndexOf("CREATE TABLE")).IndexOf("Product"));
            Assert.Contains("[ProductCategoryID]", trackingTableSection);
            Assert.Contains("uniqueidentifier", trackingTableSection);

            // Verify that ProductCategoryID appears in INSERT trigger
            Assert.Contains("Product_insert_trigger", scripts);
            var insertTriggerSection = scripts.Substring(scripts.IndexOf("Product_insert_trigger"));
            Assert.Contains("[ProductCategoryID]", insertTriggerSection.Substring(0, Math.Min(2000, insertTriggerSection.Length)));

            // Verify that ProductCategoryID appears in UPDATE trigger
            Assert.Contains("Product_update_trigger", scripts);
            var updateTriggerSection = scripts.Substring(scripts.IndexOf("Product_update_trigger"));
            Assert.Contains("[ProductCategoryID]", updateTriggerSection.Substring(0, Math.Min(2000, updateTriggerSection.Length)));

            // Verify that ProductCategoryID appears in DELETE trigger
            Assert.Contains("Product_delete_trigger", scripts);
            var deleteTriggerSection = scripts.Substring(scripts.IndexOf("Product_delete_trigger"));
            Assert.Contains("[ProductCategoryID]", deleteTriggerSection.Substring(0, Math.Min(2000, deleteTriggerSection.Length)));

            await Verifier.Verify(scripts);

        }

        [Fact]
        public async Task GetProvisioningSqlScripts_SqlServerToSqlite_SingleTable()
        {
            this.dbName = HelperDatabase.GetRandomName("tcp_prov_cross_");
            await HelperDatabase.CreateDatabaseAsync(ProviderType.Sql, dbName, true);
            var cs = HelperDatabase.GetConnectionString(ProviderType.Sql, dbName);

            // Create a simple ProductCategory table in SQL Server
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

            var sqlServerProvider = new SqlSyncProvider(cs);
            var orchestrator = new RemoteOrchestrator(sqlServerProvider);

            var sqliteProvider = new SqliteSyncProvider("data source=:memory:");

            // Act - Get SQLite scripts from SQL Server connection
            var scripts = await orchestrator.GetProvisioningSqlScriptsAsync(setup, sqliteProvider);

            // Assert
            output.WriteLine("========== SQL Server → SQLite Scripts ==========");
            output.WriteLine(scripts);
            output.WriteLine("=================================================");

            // Verify it's SQLite syntax, not SQL Server
            Assert.DoesNotContain("CREATE PROCEDURE", scripts); // SQLite doesn't use stored procedures
            Assert.DoesNotContain("GO", scripts); // SQL Server batch separator
            Assert.Contains("ProductCategory_tracking", scripts); // Should have tracking table
            Assert.Contains("CREATE TRIGGER", scripts); // SQLite uses triggers

            await Verifier.Verify(scripts);
        }

        [Fact]
        public async Task GetProvisioningSqlScripts_SqlServerToSqlite_TwoRelatedTables()
        {
            this.dbName = HelperDatabase.GetRandomName("tcp_prov_cross2_");
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

            var sqlServerProvider = new SqlSyncProvider(cs);
            var orchestrator = new RemoteOrchestrator(sqlServerProvider);

            var sqliteProvider = new SqliteSyncProvider("data source=:memory:");

            // Act - Get SQLite scripts from SQL Server connection
            var scripts = await orchestrator.GetProvisioningSqlScriptsAsync(setup, sqliteProvider);

            // Assert
            output.WriteLine("========== SQL Server → SQLite Two Tables Scripts ==========");
            output.WriteLine(scripts);
            output.WriteLine("============================================================");

            // Verify it's SQLite syntax
            Assert.DoesNotContain("CREATE PROCEDURE", scripts); // SQLite doesn't use stored procedures
            Assert.DoesNotContain("GO", scripts); // SQL Server batch separator
            Assert.Contains("ProductCategory_tracking", scripts);
            Assert.Contains("Product_tracking", scripts);
            Assert.Contains("CREATE TRIGGER", scripts);

            await Verifier.Verify(scripts);
        }

        [Fact]
        public async Task GetProvisioningSqlScripts_SqlServerToSqlServer_CrossOrchestrator()
        {
            this.dbName = HelperDatabase.GetRandomName("tcp_prov_cross_sql_");
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

            var sqlServerProvider = new SqlSyncProvider(cs);
            var orchestrator = new RemoteOrchestrator(sqlServerProvider);

            var expectedScripts = await orchestrator.GetProvisioningSqlScriptsAsync(setup);

            // Create a second SQL Server provider (could be for a different server)
            var targetSqlServerProvider = new SqlSyncProvider(cs);

            // Act - Get SQL Server scripts using target provider
            var scripts = await orchestrator.GetProvisioningSqlScriptsAsync(setup, targetSqlServerProvider);

            // Assert
            output.WriteLine("========== SQL Server → SQL Server Cross-Orchestrator ==========");
            output.WriteLine(scripts);
            output.WriteLine("================================================================");

            // Verify it's SQL Server syntax
            Assert.Contains("CREATE PROCEDURE", scripts); // SQL Server uses stored procedures
            Assert.Contains("GO", scripts); // SQL Server batch separator
            Assert.Contains("ProductCategory_tracking", scripts);
            Assert.Contains("CREATE TRIGGER", scripts);

            await Verifier.Verify(scripts);

            Assert.Equal(expectedScripts, scripts);
        }

        [Fact]
        public async Task GetProvisioningSqlScripts_WithComponentFilter_SkipEntireTable()
        {
            this.dbName = HelperDatabase.GetRandomName("tcp_prov_filter_");
            await HelperDatabase.CreateDatabaseAsync(ProviderType.Sql, dbName, true);
            var cs = HelperDatabase.GetConnectionString(ProviderType.Sql, dbName);

            // Create ProductCategory and Product tables
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
                        [ModifiedDate] [datetime] NULL
                    )";

                using var cmd = new SqlCommand(commandText, connection);
                cmd.ExecuteNonQuery();
            }

            var setup = new SyncSetup("ProductCategory", "Product");
            setup.Tables["ProductCategory"].Columns.AddRange("ProductCategoryID", "Name", "ModifiedDate");
            setup.Tables["Product"].Columns.AddRange("ProductID", "Name", "ProductNumber", "ModifiedDate");

            var provider = new SqlSyncProvider(cs);
            var orchestrator = new RemoteOrchestrator(provider);

            // Act - Filter to exclude Product table entirely
            var scripts = await orchestrator.GetProvisioningSqlScriptsAsync(
                setup,
                args => args.ComponentType != ProvisioningComponentType.Table || args.Table.TableName != "Product"
            );

            // Assert
            output.WriteLine("========== Component Filter - Skip Entire Table ==========");
            output.WriteLine(scripts);
            output.WriteLine("===========================================================");

            // Should contain ProductCategory components
            Assert.Contains("ProductCategory_tracking", scripts);
            Assert.Contains("ProductCategory_insert_trigger", scripts);

            // Should NOT contain any Product components
            Assert.DoesNotContain("Product_tracking", scripts);
            Assert.DoesNotContain("Product_insert_trigger", scripts);

            await Verifier.Verify(scripts);
        }

        [Fact]
        public async Task GetProvisioningSqlScripts_WithComponentFilter_SkipSpecificTriggers()
        {
            this.dbName = HelperDatabase.GetRandomName("tcp_prov_filter_trigger_");
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

            // Act - Skip Delete trigger only
            var scripts = await orchestrator.GetProvisioningSqlScriptsAsync(
                setup,
                args =>
                {
                    if (args.ComponentType == ProvisioningComponentType.Trigger && args.TriggerType == DbTriggerType.Delete)
                        return false;
                    return true;
                }
            );

            // Assert
            output.WriteLine("========== Component Filter - Skip Delete Trigger ==========");
            output.WriteLine(scripts);
            output.WriteLine("============================================================");

            // Should contain Insert and Update triggers
            Assert.Contains("ProductCategory_insert_trigger", scripts, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("ProductCategory_update_trigger", scripts, StringComparison.OrdinalIgnoreCase);

            // Should NOT contain Delete trigger
            Assert.DoesNotContain("ProductCategory_delete_trigger", scripts, StringComparison.OrdinalIgnoreCase);

            // Should still contain tracking table and stored procedures
            Assert.Contains("ProductCategory_tracking", scripts);

            await Verifier.Verify(scripts);
        }

        [Fact]
        public async Task GetProvisioningSqlScripts_WithComponentFilter_SkipSpecificStoredProcedures()
        {
            this.dbName = HelperDatabase.GetRandomName("tcp_prov_filter_sp_");
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

            // Act - Skip BulkUpdate and BulkDelete stored procedures
            var scripts = await orchestrator.GetProvisioningSqlScriptsAsync(
                setup,
                args =>
                {
                    if (args.ComponentType == ProvisioningComponentType.StoredProcedure)
                    {
                        if (args.StoredProcedureType == DbStoredProcedureType.BulkUpdateRows ||
                            args.StoredProcedureType == DbStoredProcedureType.BulkDeleteRows)
                            return false;
                    }
                    return true;
                }
            );

            // Assert
            output.WriteLine("========== Component Filter - Skip Specific SPs ==========");
            output.WriteLine(scripts);
            output.WriteLine("===========================================================");

            // Should contain other stored procedures
            Assert.Contains("CREATE PROCEDURE", scripts);
            Assert.Contains("_changes", scripts, StringComparison.OrdinalIgnoreCase);

            // Should NOT contain BulkUpdate and BulkDelete procedures
            Assert.DoesNotContain("bulkupdate", scripts, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("bulkdelete", scripts, StringComparison.OrdinalIgnoreCase);

            await Verifier.Verify(scripts);
        }

        [Fact]
        public async Task GetProvisioningSqlScripts_WithComponentFilter_SkipTrackingTable()
        {
            this.dbName = HelperDatabase.GetRandomName("tcp_prov_filter_tracking_");
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

            // Act - Skip tracking table
            var scripts = await orchestrator.GetProvisioningSqlScriptsAsync(
                setup,
                args => args.ComponentType != ProvisioningComponentType.TrackingTable
            );

            // Assert
            output.WriteLine("========== Component Filter - Skip Tracking Table ==========");
            output.WriteLine(scripts);
            output.WriteLine("============================================================");

            // Should NOT contain tracking table
            Assert.DoesNotContain("CREATE TABLE IF NOT EXISTS [ProductCategory_tracking]", scripts);

            // Should still contain triggers and stored procedures
            Assert.Contains("ProductCategory_insert_trigger", scripts, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("CREATE PROCEDURE", scripts);

            await Verifier.Verify(scripts);
        }

        [Fact]
        public async Task GetProvisioningSqlScripts_WithComponentFilter_SkipCustomSql()
        {
            this.dbName = HelperDatabase.GetRandomName("tcp_prov_filter_custom_");
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

            // Add custom provisioning SQL
            setup.Tables["ProductCategory"].AddCustomProvisioningSql(
                "CREATE NONCLUSTERED INDEX [IX_ProductCategory_Name] ON [dbo].[ProductCategory] ([Name])"
            );

            var provider = new SqlSyncProvider(cs);
            var orchestrator = new RemoteOrchestrator(provider);

            // Act - Skip custom SQL
            var scripts = await orchestrator.GetProvisioningSqlScriptsAsync(
                setup,
                args => args.ComponentType != ProvisioningComponentType.CustomSql
            );

            // Assert
            output.WriteLine("========== Component Filter - Skip Custom SQL ==========");
            output.WriteLine(scripts);
            output.WriteLine("=========================================================");

            // Should NOT contain custom SQL
            Assert.DoesNotContain("-- Custom Provisioning SQL for ProductCategory", scripts);
            Assert.DoesNotContain("CREATE NONCLUSTERED INDEX [IX_ProductCategory_Name]", scripts);

            // Should still contain standard provisioning components
            Assert.Contains("ProductCategory_tracking", scripts);
            Assert.Contains("ProductCategory_insert_trigger", scripts, StringComparison.OrdinalIgnoreCase);

            await Verifier.Verify(scripts);
        }

        [Fact]
        public async Task GetProvisioningSqlScripts_WithComponentFilter_OnlyTrackingTablesAndTriggers()
        {
            this.dbName = HelperDatabase.GetRandomName("tcp_prov_filter_minimal_");
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

            // Act - Only include tracking tables and triggers
            var scripts = await orchestrator.GetProvisioningSqlScriptsAsync(
                setup,
                args => args.ComponentType == ProvisioningComponentType.Table ||
                        args.ComponentType == ProvisioningComponentType.TrackingTable ||
                        args.ComponentType == ProvisioningComponentType.Trigger
            );

            // Assert
            output.WriteLine("========== Component Filter - Only Tracking & Triggers ==========");
            output.WriteLine(scripts);
            output.WriteLine("=================================================================");

            // Should contain tracking table and triggers
            Assert.Contains("ProductCategory_tracking", scripts);
            Assert.Contains("ProductCategory_insert_trigger", scripts, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("ProductCategory_update_trigger", scripts, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("ProductCategory_delete_trigger", scripts, StringComparison.OrdinalIgnoreCase);

            // Should NOT contain stored procedures
            Assert.DoesNotContain("CREATE PROCEDURE", scripts);

            await Verifier.Verify(scripts);
        }

        [Fact]
        public async Task GetProvisioningSqlScripts_WithComponentFilter_CrossProvider_SkipStoredProcedures()
        {
            this.dbName = HelperDatabase.GetRandomName("tcp_prov_filter_cross_");
            await HelperDatabase.CreateDatabaseAsync(ProviderType.Sql, dbName, true);
            var cs = HelperDatabase.GetConnectionString(ProviderType.Sql, dbName);

            // Create a simple ProductCategory table in SQL Server
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

            var sqlServerProvider = new SqlSyncProvider(cs);
            var orchestrator = new RemoteOrchestrator(sqlServerProvider);

            var sqliteProvider = new SqliteSyncProvider("data source=:memory:");

            // Act - Get SQLite scripts without stored procedures (SQLite doesn't use them anyway)
            var scripts = await orchestrator.GetProvisioningSqlScriptsAsync(
                setup,
                sqliteProvider,
                args => args.ComponentType != ProvisioningComponentType.StoredProcedure
            );

            // Assert
            output.WriteLine("========== Cross-Provider Filter - Skip SPs ==========");
            output.WriteLine(scripts);
            output.WriteLine("=======================================================");

            // Should contain SQLite-style tracking table and triggers
            Assert.Contains("ProductCategory_tracking", scripts);
            Assert.Contains("CREATE TRIGGER", scripts);

            // Should NOT contain stored procedures (SQLite doesn't use them)
            Assert.DoesNotContain("CREATE PROCEDURE", scripts);

            await Verifier.Verify(scripts);
        }

        [Fact]
        public async Task GetProvisioningSqlScripts_WithComponentFilter_MultiTableSelectiveFilter()
        {
            this.dbName = HelperDatabase.GetRandomName("tcp_prov_filter_multi_");
            await HelperDatabase.CreateDatabaseAsync(ProviderType.Sql, dbName, true);
            var cs = HelperDatabase.GetConnectionString(ProviderType.Sql, dbName);

            // Create ProductCategory and Product tables
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
                        [ModifiedDate] [datetime] NULL
                    )";

                using var cmd = new SqlCommand(commandText, connection);
                cmd.ExecuteNonQuery();
            }

            var setup = new SyncSetup("ProductCategory", "Product");
            setup.Tables["ProductCategory"].Columns.AddRange("ProductCategoryID", "Name", "ModifiedDate");
            setup.Tables["Product"].Columns.AddRange("ProductID", "Name", "ProductNumber", "ProductCategoryID", "ModifiedDate");

            var provider = new SqlSyncProvider(cs);
            var orchestrator = new RemoteOrchestrator(provider);

            // Act - Include all ProductCategory components, but only tracking table for Product
            var scripts = await orchestrator.GetProvisioningSqlScriptsAsync(
                setup,
                args =>
                {
                    if (args.Table.TableName == "ProductCategory")
                        return true; // Include all ProductCategory components

                    if (args.Table.TableName == "Product")
                    {
                        // For Product, only include table-level and tracking table
                        return args.ComponentType == ProvisioningComponentType.Table ||
                               args.ComponentType == ProvisioningComponentType.TrackingTable;
                    }

                    return true;
                }
            );

            // Assert
            output.WriteLine("========== Multi-Table Selective Filter ==========");
            output.WriteLine(scripts);
            output.WriteLine("===================================================");

            // ProductCategory should have all components
            Assert.Contains("ProductCategory_tracking", scripts);
            Assert.Contains("ProductCategory_insert_trigger", scripts, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("ProductCategory", scripts);

            // Product should only have tracking table
            Assert.Contains("Product_tracking", scripts);
            Assert.DoesNotContain("Product_insert_trigger", scripts, StringComparison.OrdinalIgnoreCase);

            await Verifier.Verify(scripts);
        }

        public void Dispose()
        {
            HelperDatabase.DropDatabase(ProviderType.Sql, dbName);
        }
    }
}
