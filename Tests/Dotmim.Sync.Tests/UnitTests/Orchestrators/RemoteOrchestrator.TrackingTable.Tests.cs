using Wormhole.Sync.Builders;
using Wormhole.Sync.Enumerations;
using Wormhole.Sync.SqlServer;
using Wormhole.Sync.Tests.Core;
using Wormhole.Sync.Tests.Models;
using Microsoft.Data.SqlClient;
using System;
using System.Threading.Tasks;
using Wormhole.Sync.Tests.Misc;
using Xunit;
using Xunit.Abstractions;

namespace Wormhole.Sync.Tests.UnitTests
{
    public partial class RemoteOrchestratorTests
    {
        [Fact]
        public async Task RemoteOrchestrator_CreateTrackingTable_WithSetupTableInterceptor_ShouldModifyCommand()
        {
            var scopeName = "scope";
            var setup = new SyncSetup("SalesLT.Product");

            var interceptorCalled = false;
            var commandTextModified = false;

            // Configure setup-level interceptor
            setup.Tables["Product", "SalesLT"].OnTrackingTableCreating(args =>
            {
                interceptorCalled = true;
                // Modify the command text (add a comment)
                args.Command.CommandText = "-- SETUP INTERCEPTOR MODIFIED\n" + args.Command.CommandText;
                commandTextModified = true;
            });

            var remoteOrchestrator = new RemoteOrchestrator(serverProvider, options);
            var scopeInfo = await remoteOrchestrator.GetScopeInfoAsync(scopeName, setup);

            // Create the tracking table
            await remoteOrchestrator.CreateTrackingTableAsync(scopeInfo, "Product", "SalesLT", false);

            Assert.True(interceptorCalled, "Setup-level interceptor should have been called");
            Assert.True(commandTextModified, "Command text should have been modified by setup-level interceptor");

            // Verify the tracking table exists
            var exists = await remoteOrchestrator.ExistTrackingTableAsync(scopeInfo, "Product", "SalesLT");
            Assert.True(exists, "Tracking table should exist");
        }

        [Fact]
        public async Task RemoteOrchestrator_CreateTrackingTable_WithSetupTableInterceptor_ShouldCancel()
        {
            var scopeName = "scope";
            var setup = new SyncSetup("SalesLT.Product");

            var interceptorCalled = false;

            // Configure setup-level interceptor to cancel
            setup.Tables["Product", "SalesLT"].OnTrackingTableCreating(args =>
            {
                interceptorCalled = true;
                args.Cancel = true; // Cancel tracking table creation
            });

            var remoteOrchestrator = new RemoteOrchestrator(serverProvider, options);
            var scopeInfo = await remoteOrchestrator.GetScopeInfoAsync(scopeName, setup);

            // Try to create tracking table (should be cancelled)
            await remoteOrchestrator.CreateTrackingTableAsync(scopeInfo, "Product", "SalesLT", false);

            Assert.True(interceptorCalled, "Setup-level interceptor should have been called");

            // Verify the tracking table was NOT created
            var exists = await remoteOrchestrator.ExistTrackingTableAsync(scopeInfo, "Product", "SalesLT");
            Assert.False(exists, "Tracking table should not exist because it was cancelled");
        }

        [Fact]
        public async Task RemoteOrchestrator_CreateTrackingTable_WithTrackedColumns_ShouldIncludeColumnsInSchema()
        {
            var dbName = HelperDatabase.GetRandomName("tcp_tt_tracked_");
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

            // Create the tracking table
            await remoteOrchestrator.CreateTrackingTableAsync(scopeInfo, "Product", null, false);

            // Verify the tracking table exists and contains ProductCategoryID
            await using (var connection = new SqlConnection(cs))
            {
                await connection.OpenAsync();

                var getColumnSql = @"
                    SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE
                    FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_NAME = 'Product_tracking'
                    AND TABLE_SCHEMA = 'dbo'
                    AND COLUMN_NAME = 'ProductCategoryID'";

                using var cmd = new SqlCommand(getColumnSql, connection);
                using var reader = await cmd.ExecuteReaderAsync();

                Assert.True(await reader.ReadAsync(), "ProductCategoryID column should exist in tracking table");

                var columnName = reader.GetString(0);
                var dataType = reader.GetString(1);
                var isNullable = reader.GetString(2);

                Assert.Equal("ProductCategoryID", columnName);
                Assert.Equal("uniqueidentifier", dataType);
                Assert.Equal("YES", isNullable); // Tracked columns should be nullable
            }

            HelperDatabase.DropDatabase(ProviderType.Sql, dbName);
        }

        [Fact]
        public async Task RemoteOrchestrator_CreateTrackingTable_WithTrackedColumns_TriggersShouldPopulateValues()
        {
            var dbName = HelperDatabase.GetRandomName("tcp_tt_populate_");
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

            // Provision tracking table and triggers
            await remoteOrchestrator.ProvisionAsync(scopeInfo, SyncProvision.TrackingTable | SyncProvision.Triggers);

            var categoryId = Guid.NewGuid();
            var productId = Guid.NewGuid();

            // Insert a ProductCategory and Product
            await using (var connection = new SqlConnection(cs))
            {
                await connection.OpenAsync();

                // Insert ProductCategory
                var insertCategorySql = @"
                    INSERT INTO [dbo].[ProductCategory] ([ProductCategoryID], [Name])
                    VALUES (@categoryId, @name)";

                using var cmdCategory = new SqlCommand(insertCategorySql, connection);
                cmdCategory.Parameters.AddWithValue("@categoryId", categoryId);
                cmdCategory.Parameters.AddWithValue("@name", "Test Category");
                await cmdCategory.ExecuteNonQueryAsync();

                // Insert Product with ProductCategoryID
                var insertProductSql = @"
                    INSERT INTO [dbo].[Product] ([ProductID], [Name], [ProductCategoryID])
                    VALUES (@productId, @name, @categoryId)";

                using var cmdProduct = new SqlCommand(insertProductSql, connection);
                cmdProduct.Parameters.AddWithValue("@productId", productId);
                cmdProduct.Parameters.AddWithValue("@name", "Test Product");
                cmdProduct.Parameters.AddWithValue("@categoryId", categoryId);
                await cmdProduct.ExecuteNonQueryAsync();

                // Verify the tracking table has the ProductCategoryID value
                var checkTrackingSql = @"
                    SELECT [ProductID], [ProductCategoryID]
                    FROM [dbo].[Product_tracking]
                    WHERE [ProductID] = @productId";

                using var cmdCheck = new SqlCommand(checkTrackingSql, connection);
                cmdCheck.Parameters.AddWithValue("@productId", productId);
                using var reader = await cmdCheck.ExecuteReaderAsync();

                Assert.True(await reader.ReadAsync(), "Tracking table should have a row for the inserted product");

                var trackedProductId = reader.GetGuid(0);
                var trackedCategoryId = reader.GetGuid(1);

                Assert.Equal(productId, trackedProductId);
                Assert.Equal(categoryId, trackedCategoryId); // Verify ProductCategoryID was tracked

                await reader.CloseAsync();

                // Update the Product's ProductCategoryID
                var newCategoryId = Guid.NewGuid();

                var insertNewCategorySql = @"
                    INSERT INTO [dbo].[ProductCategory] ([ProductCategoryID], [Name])
                    VALUES (@categoryId, @name)";

                using var cmdNewCategory = new SqlCommand(insertNewCategorySql, connection);
                cmdNewCategory.Parameters.AddWithValue("@categoryId", newCategoryId);
                cmdNewCategory.Parameters.AddWithValue("@name", "New Category");
                await cmdNewCategory.ExecuteNonQueryAsync();

                var updateProductSql = @"
                    UPDATE [dbo].[Product]
                    SET [ProductCategoryID] = @newCategoryId
                    WHERE [ProductID] = @productId";

                using var cmdUpdate = new SqlCommand(updateProductSql, connection);
                cmdUpdate.Parameters.AddWithValue("@newCategoryId", newCategoryId);
                cmdUpdate.Parameters.AddWithValue("@productId", productId);
                await cmdUpdate.ExecuteNonQueryAsync();

                // Verify the tracking table was updated with the new ProductCategoryID
                using var cmdCheckUpdate = new SqlCommand(checkTrackingSql, connection);
                cmdCheckUpdate.Parameters.AddWithValue("@productId", productId);
                using var readerUpdate = await cmdCheckUpdate.ExecuteReaderAsync();

                Assert.True(await readerUpdate.ReadAsync(), "Tracking table should still have the row");

                var updatedTrackedCategoryId = readerUpdate.GetGuid(1);
                Assert.Equal(newCategoryId, updatedTrackedCategoryId); // Verify ProductCategoryID was updated in tracking table
            }

            HelperDatabase.DropDatabase(ProviderType.Sql, dbName);
        }
    }
}
