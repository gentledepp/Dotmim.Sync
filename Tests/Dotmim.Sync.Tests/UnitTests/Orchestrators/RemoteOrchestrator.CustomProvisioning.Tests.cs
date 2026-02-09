using Wormhole.Sync.Enumerations;
using Wormhole.Sync.SqlServer;
using Wormhole.Sync.Tests.Core;
using Microsoft.Data.SqlClient;
using System;
using System.Threading.Tasks;
using Wormhole.Sync.Tests.Misc;
using Xunit;


namespace Wormhole.Sync.Tests.UnitTests
{
    public partial class RemoteOrchestratorTests
    {
        [Fact]
        public async Task Provision_WithCustomProvisioningSql_ShouldExecuteCustomSql()
        {
            var dbName = HelperDatabase.GetRandomName("tcp_custom_prov_");
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
            var remoteOrchestrator = new RemoteOrchestrator(provider, options);

            // Act - Provision the database with custom SQL
            var scopeInfo = await remoteOrchestrator.GetScopeInfoAsync("scope", setup);
            await remoteOrchestrator.ProvisionAsync(scopeInfo);

            // Assert - Verify the custom index was created
            bool indexExists = false;
            using (var connection = new SqlConnection(cs))
            {
                connection.Open();
                var checkIndexQuery = @"
                    SELECT COUNT(*)
                    FROM sys.indexes
                    WHERE name = 'IX_ProductCategory_Name'
                    AND object_id = OBJECT_ID('dbo.ProductCategory')";
                using var cmd = new SqlCommand(checkIndexQuery, connection);
                var result = (int)cmd.ExecuteScalar();
                indexExists = result > 0;
            }

            Assert.True(indexExists, "Custom index IX_ProductCategory_Name should have been created");

            HelperDatabase.DropDatabase(ProviderType.Sql, dbName);
        }

        [Fact]
        public async Task Provision_WithMultipleCustomProvisioningSql_ShouldExecuteAllStatements()
        {
            var dbName = HelperDatabase.GetRandomName("tcp_custom_prov_multi_");
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
            var remoteOrchestrator = new RemoteOrchestrator(provider, options);

            // Act - Provision the database with custom SQL
            var scopeInfo = await remoteOrchestrator.GetScopeInfoAsync("scope", setup);
            await remoteOrchestrator.ProvisionAsync(scopeInfo);

            // Assert - Verify both custom indexes were created
            bool nameIndexExists = false;
            bool descriptionIndexExists = false;

            using (var connection = new SqlConnection(cs))
            {
                connection.Open();

                // Check for Name index
                var checkNameIndexQuery = @"
                    SELECT COUNT(*)
                    FROM sys.indexes
                    WHERE name = 'IX_ProductCategory_Name'
                    AND object_id = OBJECT_ID('dbo.ProductCategory')";
                using var cmdName = new SqlCommand(checkNameIndexQuery, connection);
                var nameResult = (int)cmdName.ExecuteScalar();
                nameIndexExists = nameResult > 0;

                // Check for Description index
                var checkDescIndexQuery = @"
                    SELECT COUNT(*)
                    FROM sys.indexes
                    WHERE name = 'IX_ProductCategory_Description'
                    AND object_id = OBJECT_ID('dbo.ProductCategory')";
                using var cmdDesc = new SqlCommand(checkDescIndexQuery, connection);
                var descResult = (int)cmdDesc.ExecuteScalar();
                descriptionIndexExists = descResult > 0;
            }

            Assert.True(nameIndexExists, "Custom index IX_ProductCategory_Name should have been created");
            Assert.True(descriptionIndexExists, "Custom index IX_ProductCategory_Description should have been created");

            HelperDatabase.DropDatabase(ProviderType.Sql, dbName);
        }
    }
}
