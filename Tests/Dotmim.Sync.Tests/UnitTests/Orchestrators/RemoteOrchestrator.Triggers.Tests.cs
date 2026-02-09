using Wormhole.Sync.Builders;
using Wormhole.Sync.Enumerations;
using Wormhole.Sync.SqlServer;
using Wormhole.Sync.Tests.Core;
using Wormhole.Sync.Tests.Models;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VerifyXunit;
using Wormhole.Sync.Tests.Misc;
using Xunit;


namespace Wormhole.Sync.Tests.UnitTests
{
    public partial class RemoteOrchestratorTests
    {
        [Fact]
        public async Task RemoteOrchestrator_Provision_ShouldCreate_Triggers()
        {
            var scopeName = "scope";
            var setup = new SyncSetup("SalesLT.Product")
            {
                TrackingTablesSuffix = "sync",
                TrackingTablesPrefix = "trck",
                TriggersPrefix = "trg_",
                TriggersSuffix = "_trg"
            };

            // trackign table name is composed with prefix and suffix from setup
            var triggerDelete = $"{setup.TriggersPrefix}Product{setup.TriggersSuffix}_delete_trigger";
            var triggerInsert = $"{setup.TriggersPrefix}Product{setup.TriggersSuffix}_insert_trigger";
            var triggerUpdate = $"{setup.TriggersPrefix}Product{setup.TriggersSuffix}_update_trigger";

            var remoteOrchestrator = new RemoteOrchestrator(serverProvider, options);
            var scopeInfo = await remoteOrchestrator.GetScopeInfoAsync(scopeName, setup);

            // Needs the tracking table to be able to create triggers
            var provision = SyncProvision.TrackingTable | SyncProvision.Triggers;

            await remoteOrchestrator.ProvisionAsync(scopeInfo, provision);

            await using (var c = new SqlConnection(serverProvider.ConnectionString))
            {
                await c.OpenAsync();

                var trigDel = await SqlManagementUtils.GetTriggerAsync(triggerDelete, "SalesLT", c, null);
                Assert.Equal(triggerDelete, trigDel.Rows[0]["Name"].ToString());

                var trigIns = await SqlManagementUtils.GetTriggerAsync(triggerInsert, "SalesLT", c, null);
                Assert.Equal(triggerInsert, trigIns.Rows[0]["Name"].ToString());

                var trigUdate = await SqlManagementUtils.GetTriggerAsync(triggerUpdate, "SalesLT", c, null);
                Assert.Equal(triggerUpdate, trigUdate.Rows[0]["Name"].ToString());

                c.Close();
            }
        }

        [Fact]
        public async Task RemoteOrchestrator_Trigger_ShouldCreate()
        {
            var scopeName = "scope";
            var setup = new SyncSetup("SalesLT.Product")
            {
                TriggersPrefix = "trg_",
                TriggersSuffix = "_trg"
            };

            var remoteOrchestrator = new RemoteOrchestrator(serverProvider, options);
            var scopeInfo = await remoteOrchestrator.GetScopeInfoAsync(scopeName, setup);

            var triggerInsert = $"{setup.TriggersPrefix}Product{setup.TriggersSuffix}_insert_trigger";
            await remoteOrchestrator.CreateTriggerAsync(scopeInfo, "Product", "SalesLT", DbTriggerType.Insert, false);

            await using (var c = new SqlConnection(serverProvider.ConnectionString))
            {
                await c.OpenAsync();

                var trigIns = await SqlManagementUtils.GetTriggerAsync(triggerInsert, "SalesLT", c, null);
                Assert.Equal(triggerInsert, trigIns.Rows[0]["Name"].ToString());

                c.Close();
            }

            var triggerUpdate = $"{setup.TriggersPrefix}Product{setup.TriggersSuffix}_update_trigger";
            await remoteOrchestrator.CreateTriggerAsync(scopeInfo, "Product", "SalesLT", DbTriggerType.Update, false);

            await using (var c = new SqlConnection(serverProvider.ConnectionString))
            {
                await c.OpenAsync();

                var trig = await SqlManagementUtils.GetTriggerAsync(triggerUpdate, "SalesLT", c, null);
                Assert.Equal(triggerUpdate, trig.Rows[0]["Name"].ToString());

                c.Close();
            }

            var triggerDelete = $"{setup.TriggersPrefix}Product{setup.TriggersSuffix}_delete_trigger";
            await remoteOrchestrator.CreateTriggerAsync(scopeInfo, "Product", "SalesLT", DbTriggerType.Delete, false);

            await using (var c = new SqlConnection(serverProvider.ConnectionString))
            {
                await c.OpenAsync();

                var trig = await SqlManagementUtils.GetTriggerAsync(triggerDelete, "SalesLT", c, null);
                Assert.Equal(triggerDelete, trig.Rows[0]["Name"].ToString());

                c.Close();
            }
        }

        [Fact]
        public async Task RemoteOrchestrator_Trigger_ShouldOverwrite()
        {

            var scopeName = "scope";

            var options = new SyncOptions();
            var setup = new SyncSetup("SalesLT.Product")
            {
                TriggersPrefix = "trg_",
                TriggersSuffix = "_trg"
            };

            var remoteOrchestrator = new RemoteOrchestrator(serverProvider, options);
            var scopeInfo = await remoteOrchestrator.GetScopeInfoAsync(scopeName, setup);

            var triggerInsert = $"{setup.TriggersPrefix}Product{setup.TriggersSuffix}_insert_trigger";
            await remoteOrchestrator.CreateTriggerAsync(scopeInfo, "Product", "SalesLT", DbTriggerType.Insert, false);

            var assertOverWritten = false;
            remoteOrchestrator.OnTriggerCreating(args =>
            {
                assertOverWritten = true;
            });

            await remoteOrchestrator.CreateTriggerAsync(scopeInfo, "Product", "SalesLT", DbTriggerType.Insert, true);

            Assert.True(assertOverWritten);
        }

        [Fact]
        public async Task RemoteOrchestrator_Trigger_ShouldNotOverwrite()
        {
            var scopeName = "scope";

            var options = new SyncOptions();
            var setup = new SyncSetup("SalesLT.Product")
            {
                TriggersPrefix = "trg_",
                TriggersSuffix = "_trg"
            };

            var remoteOrchestrator = new RemoteOrchestrator(serverProvider, options);
            var scopeInfo = await remoteOrchestrator.GetScopeInfoAsync(scopeName, setup);


            var triggerInsert = $"{setup.TriggersPrefix}Product{setup.TriggersSuffix}_insert_trigger";
            await remoteOrchestrator.CreateTriggerAsync(scopeInfo, "Product", "SalesLT", DbTriggerType.Insert, false);


            var assertOverWritten = false;
            remoteOrchestrator.OnTriggerCreating(args =>
            {
                assertOverWritten = true;
            });

            await remoteOrchestrator.CreateTriggerAsync(scopeInfo, "Product", "SalesLT", DbTriggerType.Insert, false);

            Assert.False(assertOverWritten);
        }

        [Fact]
        public async Task RemoteOrchestrator_Trigger_Exists()
        {
            var scopeName = "scope";

            var options = new SyncOptions();
            var setup = new SyncSetup("SalesLT.Product")
            {
                TriggersPrefix = "trg_",
                TriggersSuffix = "_trg"
            };

            var remoteOrchestrator = new RemoteOrchestrator(serverProvider, options);
            var scopeInfo = await remoteOrchestrator.GetScopeInfoAsync(scopeName, setup);

            await remoteOrchestrator.CreateTriggerAsync(scopeInfo, "Product", "SalesLT", DbTriggerType.Insert, false);

            var insertExists = await remoteOrchestrator.ExistTriggerAsync(scopeInfo, "Product", "SalesLT", DbTriggerType.Insert);
            var updateExists = await remoteOrchestrator.ExistTriggerAsync(scopeInfo, "Product", "SalesLT", DbTriggerType.Update);

            Assert.True(insertExists);
            Assert.False(updateExists);
        }

        [Fact]
        public async Task RemoteOrchestrator_Triggers_ShouldCreate()
        {
            var scopeName = "scope";

            var options = new SyncOptions();
            var setup = new SyncSetup("SalesLT.Product");

            setup.TriggersPrefix = "trg_";
            setup.TriggersSuffix = "_trg";

            var remoteOrchestrator = new RemoteOrchestrator(serverProvider, options);
            var scopeInfo = await remoteOrchestrator.GetScopeInfoAsync(scopeName, setup);

            var triggerInsert = $"{setup.TriggersPrefix}Product{setup.TriggersSuffix}_insert_trigger";
            var triggerUpdate = $"{setup.TriggersPrefix}Product{setup.TriggersSuffix}_update_trigger";
            var triggerDelete = $"{setup.TriggersPrefix}Product{setup.TriggersSuffix}_delete_trigger";

            await remoteOrchestrator.CreateTriggersAsync(scopeInfo, "Product", "SalesLT");

            await using (var c = new SqlConnection(serverProvider.ConnectionString))
            {
                await c.OpenAsync();

                var trigIns = await SqlManagementUtils.GetTriggerAsync(triggerInsert, "SalesLT", c, null);
                Assert.Equal(triggerInsert, trigIns.Rows[0]["Name"].ToString());

                c.Close();
            }

            await using (var c = new SqlConnection(serverProvider.ConnectionString))
            {
                await c.OpenAsync();

                var trig = await SqlManagementUtils.GetTriggerAsync(triggerUpdate, "SalesLT", c, null);
                Assert.Equal(triggerUpdate, trig.Rows[0]["Name"].ToString());

                c.Close();
            }

            await using (var c = new SqlConnection(serverProvider.ConnectionString))
            {
                await c.OpenAsync();

                var trig = await SqlManagementUtils.GetTriggerAsync(triggerDelete, "SalesLT", c, null);
                Assert.Equal(triggerDelete, trig.Rows[0]["Name"].ToString());

                c.Close();
            }
        }

        [Fact]
        public async Task RemoteOrchestrator_CreateTrigger_WithSetupTableInterceptor_ShouldModifyCommand()
        {
            var scopeName = "scope";
            var setup = new SyncSetup("SalesLT.Product");

            var interceptorCalled = false;
            var commandTextModified = false;

            // Configure setup-level interceptor
            setup.Tables["Product", "SalesLT"].OnTriggerCreating(args =>
            {
                interceptorCalled = true;
                if (args.TriggerType == DbTriggerType.Insert)
                {
                    // Modify the command text (add a comment)
                    args.Command.CommandText = "-- SETUP INTERCEPTOR MODIFIED\n" + args.Command.CommandText;
                    commandTextModified = true;
                }
            });

            var remoteOrchestrator = new RemoteOrchestrator(serverProvider, options);
            var scopeInfo = await remoteOrchestrator.GetScopeInfoAsync(scopeName, setup);

            // Provision tracking table first (required for triggers)
            await remoteOrchestrator.ProvisionAsync(scopeInfo, SyncProvision.TrackingTable);

            // Now create the insert trigger
            await remoteOrchestrator.CreateTriggerAsync(scopeInfo, "Product", "SalesLT", DbTriggerType.Insert, false);

            Assert.True(interceptorCalled, "Setup-level interceptor should have been called");
            Assert.True(commandTextModified, "Command text should have been modified by setup-level interceptor");
        }

        [Fact]
        public async Task RemoteOrchestrator_CreateTrigger_WithSetupTableInterceptor_ShouldCancel()
        {
            var scopeName = "scope";
            var setup = new SyncSetup("SalesLT.Product");

            var interceptorCalled = false;

            // Configure setup-level interceptor to cancel
            setup.Tables["Product", "SalesLT"].OnTriggerCreating(args =>
            {
                interceptorCalled = true;
                if (args.TriggerType == DbTriggerType.Delete)
                {
                    args.Cancel = true; // Cancel delete trigger creation
                }
            });

            var remoteOrchestrator = new RemoteOrchestrator(serverProvider, options);
            var scopeInfo = await remoteOrchestrator.GetScopeInfoAsync(scopeName, setup);

            // Provision tracking table first (required for triggers)
            await remoteOrchestrator.ProvisionAsync(scopeInfo, SyncProvision.TrackingTable);

            // Try to create delete trigger (should be cancelled)
            await remoteOrchestrator.CreateTriggerAsync(scopeInfo, "Product", "SalesLT", DbTriggerType.Delete, false);

            Assert.True(interceptorCalled, "Setup-level interceptor should have been called");

            // Verify the trigger was NOT created
            var exists = await remoteOrchestrator.ExistTriggerAsync(scopeInfo, "Product", "SalesLT", DbTriggerType.Delete);
            Assert.False(exists, "Delete trigger should not exist because it was cancelled");
        }

        [Fact]
        public async Task RemoteOrchestrator_CreateTrigger_WithTrackedColumns_ShouldIncludeTrackedColumnsInTriggers()
        {
            var dbName = HelperDatabase.GetRandomName("tcp_trg_tracked_");
            await HelperDatabase.CreateDatabaseAsync(ProviderType.Sql, dbName, true);
            var cs = HelperDatabase.GetConnectionString(ProviderType.Sql, dbName);

            // Create ProductCategory and Product tables with FK relationship
            using (var connection = new SqlConnection(cs))
            {
                connection.Open();

                var commandText = @"
                    CREATE TABLE [dbo].[ProductCategory] (
                        [ProductCategoryID] [uniqueidentifier] NOT NULL PRIMARY KEY DEFAULT (NEWID()),
                        [Name] [nvarchar](50) NOT NULL
                    );

                    CREATE TABLE [dbo].[Product] (
                        [ProductID] [uniqueidentifier] NOT NULL PRIMARY KEY DEFAULT (NEWID()),
                        [Name] [nvarchar](50) NOT NULL,
                        [ProductCategoryID] [uniqueidentifier] NULL,
                        CONSTRAINT [FK_Product_ProductCategory] FOREIGN KEY ([ProductCategoryID])
                            REFERENCES [dbo].[ProductCategory] ([ProductCategoryID])
                    )";

                using var cmd = new SqlCommand(commandText, connection);
                cmd.ExecuteNonQuery();
            }

            var scopeName = "scope";
            var setup = new SyncSetup("ProductCategory", "Product");
            setup.Tables["ProductCategory"].Columns.AddRange("ProductCategoryID", "Name");
            setup.Tables["Product"].Columns.AddRange("ProductID", "Name", "ProductCategoryID");

            // Add ProductCategoryID as a tracked column
            setup.Tables["Product"].AddTrackedColumn("ProductCategoryID");

            var provider = new SqlSyncProvider(cs);
            var remoteOrchestrator = new RemoteOrchestrator(provider, options);
            var scopeInfo = await remoteOrchestrator.GetScopeInfoAsync(scopeName, setup);

            // Provision tracking table first (required for triggers)
            await remoteOrchestrator.ProvisionAsync(scopeInfo, SyncProvision.TrackingTable);

            // Create INSERT trigger
            await remoteOrchestrator.CreateTriggerAsync(scopeInfo, "Product", null, DbTriggerType.Insert, false);

            // Verify trigger exists and contains ProductCategoryID
            await using (var connection = new SqlConnection(cs))
            {
                await connection.OpenAsync();

                var getTriggerSql = @"
                    SELECT OBJECT_DEFINITION(OBJECT_ID(N'dbo.Product_insert_trigger')) AS TriggerDefinition";

                using var cmd = new SqlCommand(getTriggerSql, connection);
                var triggerDef = (string)await cmd.ExecuteScalarAsync();

                Assert.NotNull(triggerDef);
                Assert.Contains("[ProductCategoryID]", triggerDef);
            }

            HelperDatabase.DropDatabase(ProviderType.Sql, dbName);
        }

        [Fact]
        public async Task RemoteOrchestrator_CreateTrigger_WithMultipleTrackedColumns_ShouldIncludeAllColumns()
        {
            var dbName = HelperDatabase.GetRandomName("tcp_trg_multi_tracked_");
            await HelperDatabase.CreateDatabaseAsync(ProviderType.Sql, dbName, true);
            var cs = HelperDatabase.GetConnectionString(ProviderType.Sql, dbName);

            // Create ProductCategory and Product tables
            using (var connection = new SqlConnection(cs))
            {
                connection.Open();

                var commandText = @"
                    CREATE TABLE [dbo].[ProductCategory] (
                        [ProductCategoryID] [uniqueidentifier] NOT NULL PRIMARY KEY DEFAULT (NEWID()),
                        [Name] [nvarchar](50) NOT NULL
                    );

                    CREATE TABLE [dbo].[Product] (
                        [ProductID] [uniqueidentifier] NOT NULL PRIMARY KEY DEFAULT (NEWID()),
                        [Name] [nvarchar](50) NOT NULL,
                        [ProductCategoryID] [uniqueidentifier] NULL,
                        [ModifiedDate] [datetime] NULL,
                        CONSTRAINT [FK_Product_ProductCategory] FOREIGN KEY ([ProductCategoryID])
                            REFERENCES [dbo].[ProductCategory] ([ProductCategoryID])
                    )";

                using var cmd = new SqlCommand(commandText, connection);
                cmd.ExecuteNonQuery();
            }

            var scopeName = "scope";
            var setup = new SyncSetup("ProductCategory", "Product");
            setup.Tables["ProductCategory"].Columns.AddRange("ProductCategoryID", "Name");
            setup.Tables["Product"].Columns.AddRange("ProductID", "Name", "ProductCategoryID", "ModifiedDate");

            // Add both ProductCategoryID and ModifiedDate as tracked columns
            setup.Tables["Product"]
                .AddTrackedColumn("ProductCategoryID")
                .AddTrackedColumn("ModifiedDate");

            var provider = new SqlSyncProvider(cs);
            var remoteOrchestrator = new RemoteOrchestrator(provider, options);
            var scopeInfo = await remoteOrchestrator.GetScopeInfoAsync(scopeName, setup);

            // Provision tracking table first
            await remoteOrchestrator.ProvisionAsync(scopeInfo, SyncProvision.TrackingTable);

            // Create UPDATE trigger
            await remoteOrchestrator.CreateTriggerAsync(scopeInfo, "Product", null, DbTriggerType.Update, false);

            // Verify trigger contains both tracked columns
            await using (var connection = new SqlConnection(cs))
            {
                await connection.OpenAsync();

                var getTriggerSql = @"
                    SELECT OBJECT_DEFINITION(OBJECT_ID(N'dbo.Product_update_trigger')) AS TriggerDefinition";

                using var cmd = new SqlCommand(getTriggerSql, connection);
                var triggerDef = (string)await cmd.ExecuteScalarAsync();

                Assert.NotNull(triggerDef);
                Assert.Contains("[ProductCategoryID]", triggerDef);
                Assert.Contains("[ModifiedDate]", triggerDef);
            }

            HelperDatabase.DropDatabase(ProviderType.Sql, dbName);
        }

        [Fact]
        public async Task RemoteOrchestrator_UpdateTrigger_ShouldOnlyUpdateTrackingTable_WhenTrackedColumnsChange()
        {
            var dbName = HelperDatabase.GetRandomName("tcp_trg_optimized_");
            await HelperDatabase.CreateDatabaseAsync(ProviderType.Sql, dbName, true);
            var cs = HelperDatabase.GetConnectionString(ProviderType.Sql, dbName);

            // Create a test table with both tracked and untracked columns
            using (var connection = new SqlConnection(cs))
            {
                connection.Open();

                var commandText = @"
                    CREATE TABLE [dbo].[TestProduct] (
                        [ProductID] [int] NOT NULL PRIMARY KEY IDENTITY(1,1),
                        [Name] [nvarchar](50) NOT NULL,
                        [Price] [decimal](18, 2) NOT NULL,
                        [UntrackedMetadata] [nvarchar](100) NULL,
                        [LastModified] [datetime] NOT NULL DEFAULT GETDATE()
                    )";

                using var cmd = new SqlCommand(commandText, connection);
                cmd.ExecuteNonQuery();
            }

            var scopeName = "scope";
            // Only include Name and Price in the sync scope, exclude UntrackedMetadata
            var setup = new SyncSetup("TestProduct");
            setup.Tables["TestProduct"].Columns.AddRange("ProductID", "Name", "Price", "LastModified");

            var provider = new SqlSyncProvider(cs);
            var remoteOrchestrator = new RemoteOrchestrator(provider, options);
            var scopeInfo = await remoteOrchestrator.GetScopeInfoAsync(scopeName, setup);

            // Provision tracking table and triggers
            await remoteOrchestrator.ProvisionAsync(scopeInfo, SyncProvision.TrackingTable | SyncProvision.Triggers);

            // Insert a test row
            int productId;
            using (var connection = new SqlConnection(cs))
            {
                await connection.OpenAsync();
                var insertSql = "INSERT INTO [dbo].[TestProduct] ([Name], [Price], [UntrackedMetadata]) VALUES ('Product1', 100.00, 'Initial'); SELECT SCOPE_IDENTITY();";
                using var cmd = new SqlCommand(insertSql, connection);
                productId = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            }

            // Get initial tracking table timestamp
            DateTime? initialTimestamp;
            using (var connection = new SqlConnection(cs))
            {
                await connection.OpenAsync();
                var getSql = "SELECT [last_change_datetime] FROM [dbo].[TestProduct_tracking] WHERE [ProductID] = @ProductID";
                using var cmd = new SqlCommand(getSql, connection);
                cmd.Parameters.AddWithValue("@ProductID", productId);
                initialTimestamp = (DateTime?)await cmd.ExecuteScalarAsync();
            }

            Assert.NotNull(initialTimestamp);

            // Wait a small amount to ensure timestamp would change if updated
            await Task.Delay(50);

            // Update ONLY the untracked column (UntrackedMetadata) - tracking table should NOT be updated
            using (var connection = new SqlConnection(cs))
            {
                await connection.OpenAsync();
                var updateSql = "UPDATE [dbo].[TestProduct] SET [UntrackedMetadata] = 'Modified' WHERE [ProductID] = @ProductID";
                using var cmd = new SqlCommand(updateSql, connection);
                cmd.Parameters.AddWithValue("@ProductID", productId);
                await cmd.ExecuteNonQueryAsync();
            }

            // Check tracking table timestamp - should be UNCHANGED
            DateTime? timestampAfterUntrackedUpdate;
            using (var connection = new SqlConnection(cs))
            {
                await connection.OpenAsync();
                var getSql = "SELECT [last_change_datetime] FROM [dbo].[TestProduct_tracking] WHERE [ProductID] = @ProductID";
                using var cmd = new SqlCommand(getSql, connection);
                cmd.Parameters.AddWithValue("@ProductID", productId);
                timestampAfterUntrackedUpdate = (DateTime?)await cmd.ExecuteScalarAsync();
            }

            Assert.Equal(initialTimestamp, timestampAfterUntrackedUpdate);

            // Wait a small amount
            await Task.Delay(100);

            // Update a TRACKED column (Price) - tracking table SHOULD be updated
            using (var connection = new SqlConnection(cs))
            {
                await connection.OpenAsync();
                var updateSql = "UPDATE [dbo].[TestProduct] SET [Price] = 200.00 WHERE [ProductID] = @ProductID";
                using var cmd = new SqlCommand(updateSql, connection);
                cmd.Parameters.AddWithValue("@ProductID", productId);
                await cmd.ExecuteNonQueryAsync();
            }

            // Check tracking table timestamp - should be CHANGED
            DateTime? timestampAfterTrackedUpdate;
            using (var connection = new SqlConnection(cs))
            {
                await connection.OpenAsync();
                var getSql = "SELECT [last_change_datetime] FROM [dbo].[TestProduct_tracking] WHERE [ProductID] = @ProductID";
                using var cmd = new SqlCommand(getSql, connection);
                cmd.Parameters.AddWithValue("@ProductID", productId);
                timestampAfterTrackedUpdate = (DateTime?)await cmd.ExecuteScalarAsync();
            }

            Assert.NotEqual(initialTimestamp, timestampAfterTrackedUpdate);

            HelperDatabase.DropDatabase(ProviderType.Sql, dbName);
        }

        [Fact]
        public async Task RemoteOrchestrator_UpdateTrigger_ShouldNotUpdateTrackingTable_WhenNoValueChanges()
        {
            var dbName = HelperDatabase.GetRandomName("tcp_trg_nochange_");
            await HelperDatabase.CreateDatabaseAsync(ProviderType.Sql, dbName, true);
            var cs = HelperDatabase.GetConnectionString(ProviderType.Sql, dbName);

            // Create a test table
            using (var connection = new SqlConnection(cs))
            {
                connection.Open();

                var commandText = @"
                    CREATE TABLE [dbo].[TestProduct] (
                        [ProductID] [int] NOT NULL PRIMARY KEY IDENTITY(1,1),
                        [Name] [nvarchar](50) NOT NULL,
                        [Price] [decimal](18, 2) NOT NULL
                    )";

                using var cmd = new SqlCommand(commandText, connection);
                cmd.ExecuteNonQuery();
            }

            var scopeName = "scope";
            var setup = new SyncSetup("TestProduct");
            setup.Tables["TestProduct"].Columns.AddRange("ProductID", "Name", "Price");

            var provider = new SqlSyncProvider(cs);
            var remoteOrchestrator = new RemoteOrchestrator(provider, options);
            var scopeInfo = await remoteOrchestrator.GetScopeInfoAsync(scopeName, setup);

            // Provision tracking table and triggers
            await remoteOrchestrator.ProvisionAsync(scopeInfo, SyncProvision.TrackingTable | SyncProvision.Triggers);

            // Insert a test row
            int productId;
            using (var connection = new SqlConnection(cs))
            {
                await connection.OpenAsync();
                var insertSql = "INSERT INTO [dbo].[TestProduct] ([Name], [Price]) VALUES ('Product1', 100.00); SELECT SCOPE_IDENTITY();";
                using var cmd = new SqlCommand(insertSql, connection);
                productId = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            }

            // Get initial tracking table timestamp
            DateTime? initialTimestamp;
            using (var connection = new SqlConnection(cs))
            {
                await connection.OpenAsync();
                var getSql = "SELECT [last_change_datetime] FROM [dbo].[TestProduct_tracking] WHERE [ProductID] = @ProductID";
                using var cmd = new SqlCommand(getSql, connection);
                cmd.Parameters.AddWithValue("@ProductID", productId);
                initialTimestamp = (DateTime?)await cmd.ExecuteScalarAsync();
            }

            Assert.NotNull(initialTimestamp);

            // Wait a small amount to ensure timestamp would change if updated
            await Task.Delay(100);

            // Update with SAME values - tracking table should NOT be updated (thanks to EXCEPT)
            using (var connection = new SqlConnection(cs))
            {
                await connection.OpenAsync();
                var updateSql = "UPDATE [dbo].[TestProduct] SET [Name] = 'Product1', [Price] = 100.00 WHERE [ProductID] = @ProductID";
                using var cmd = new SqlCommand(updateSql, connection);
                cmd.Parameters.AddWithValue("@ProductID", productId);
                await cmd.ExecuteNonQueryAsync();
            }

            // Check tracking table timestamp - should be UNCHANGED
            DateTime? timestampAfterNoValueChange;
            using (var connection = new SqlConnection(cs))
            {
                await connection.OpenAsync();
                var getSql = "SELECT [last_change_datetime] FROM [dbo].[TestProduct_tracking] WHERE [ProductID] = @ProductID";
                using var cmd = new SqlCommand(getSql, connection);
                cmd.Parameters.AddWithValue("@ProductID", productId);
                timestampAfterNoValueChange = (DateTime?)await cmd.ExecuteScalarAsync();
            }

            Assert.Equal(initialTimestamp, timestampAfterNoValueChange);

            HelperDatabase.DropDatabase(ProviderType.Sql, dbName);
        }

        [Fact]
        public async Task RemoteOrchestrator_UpdateTrigger_ShouldContainOptimizationStages()
        {
            var scopeName = "scope";
            var setup = new SyncSetup("SalesLT.Product");
            setup.Tables["Product", "SalesLT"].Columns.AddRange("ProductID", "Name", "ProductNumber");

            var remoteOrchestrator = new RemoteOrchestrator(serverProvider, options);
            var scopeInfo = await remoteOrchestrator.GetScopeInfoAsync(scopeName, setup);

            // Provision tracking table first
            await remoteOrchestrator.ProvisionAsync(scopeInfo, SyncProvision.TrackingTable);

            // Create UPDATE trigger
            await remoteOrchestrator.CreateTriggerAsync(scopeInfo, "Product", "SalesLT", DbTriggerType.Update, true);

            // Verify trigger contains optimization stages
            await using (var connection = new SqlConnection(serverProvider.ConnectionString))
            {
                await connection.OpenAsync();

                var getTriggerSql = @"
                    SELECT OBJECT_DEFINITION(OBJECT_ID(N'SalesLT.Product_update_trigger')) AS TriggerDefinition";

                using var cmd = new SqlCommand(getTriggerSql, connection);
                var triggerDef = (string)await cmd.ExecuteScalarAsync();

                Assert.NotNull(triggerDef);

                // Verify Stage 1 comment exists
                Assert.Contains("Stage 1: Fast column-level check", triggerDef);

                // Verify Stage 2 comment exists
                Assert.Contains("Stage 2: Precise value-level comparison using EXCEPT", triggerDef);

                // Verify Stage 3 comment exists
                Assert.Contains("Stage 3: Perform the actual tracking update", triggerDef);

                // Verify UPDATE() function is used
                Assert.Contains("UPDATE(", triggerDef);

                // Verify EXCEPT operator is used
                Assert.Contains("EXCEPT", triggerDef);

                await Verifier.Verify(triggerDef);
            }
        }
    }
}
