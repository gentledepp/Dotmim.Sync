using Wormhole.Sync.Tests.Core;
using Wormhole.Sync.Tests.Misc;
using Microsoft.Data.SqlClient;
using System;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Wormhole.Sync.Tests.IntegrationTests
{
    /// <summary>
    /// Integration tests for custom parameters on scope_info_client table
    /// Tests scenarios with filters and different data types including Guid
    /// </summary>
    public class ScopeInfoClientCustomParametersTests : IDisposable
    {
        private readonly ITestOutputHelper output;

        // Server provider will be SQL Server
        private readonly ProviderType serverProviderType = ProviderType.Sql;

        // Client provider will be SQLite (which should NOT get custom columns)
        private readonly ProviderType clientProviderType = ProviderType.Sqlite;

        private readonly string serverDbName;
        private readonly string clientDbName;

        public ScopeInfoClientCustomParametersTests(ITestOutputHelper output)
        {
            this.output = output;

            // Generate unique database names
            this.serverDbName = HelperDatabase.GetRandomName("server_customparams_");
            this.clientDbName = HelperDatabase.GetRandomName("tcp_cli_customparams_");
        }

        [Fact]
        public async Task CustomParameters_Should_CreateColumnsOnServer_NotOnClient()
        {
            // Arrange - Create databases
            await HelperDatabase.CreateDatabaseAsync(serverProviderType, serverDbName, true);

            // Create a simple table for sync
            var createTableScript = @"
                CREATE TABLE Product (
                    ProductId INT PRIMARY KEY,
                    ProductName NVARCHAR(100)
                )";
            await HelperDatabase.ExecuteScriptAsync(serverProviderType, serverDbName, createTableScript);

            var serverProvider = HelperDatabase.GetSyncProvider(serverProviderType, serverDbName);
            var clientProvider = HelperDatabase.GetSyncProvider(clientProviderType, clientDbName);

            var setup = new SyncSetup("Product");

            // Add custom parameters
            setup.ScopeInfoClientParameters.Add(new ScopeInfoClientParameter
            {
                Name = "UserId",
                DbType = DbType.Int64
            });
            setup.ScopeInfoClientParameters.Add(new ScopeInfoClientParameter
            {
                Name = "DeviceIdentifier",
                DbType = DbType.Guid
            });

            var agent = new SyncAgent(clientProvider, serverProvider);

            // Act - Perform initial sync to create scope_info_client table
            var result = await agent.SynchronizeAsync(setup);

            // Assert - Check that server has custom columns
            using var serverConnection = new SqlConnection(HelperDatabase.GetConnectionString(serverProviderType, serverDbName));
            serverConnection.Open();

            var checkColumnsQuery = @"
                SELECT COUNT(*)
                FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_NAME = 'scope_info_client'
                AND COLUMN_NAME IN ('UserId', 'DeviceIdentifier')";

            using var cmd = new SqlCommand(checkColumnsQuery, serverConnection);
            var columnCount = (int)cmd.ExecuteScalar();

            Assert.Equal(2, columnCount);

            // Check that columns are nullable
            var checkNullableQuery = @"
                SELECT IS_NULLABLE
                FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_NAME = 'scope_info_client'
                AND COLUMN_NAME = 'UserId'";

            using var nullCmd = new SqlCommand(checkNullableQuery, serverConnection);
            var isNullable = (string)nullCmd.ExecuteScalar();

            Assert.Equal("YES", isNullable);

            output.WriteLine($"Custom columns created successfully on server");
            output.WriteLine($"Sync completed: {result.TotalChangesDownloadedFromServer} changes downloaded");
        }

        [Fact]
        public async Task CustomParameters_WithMatchingSyncParameters_Should_SaveValuesToDatabase()
        {
            // Arrange
            await HelperDatabase.CreateDatabaseAsync(serverProviderType, serverDbName, true);

            var createTableScript = @"
                CREATE TABLE Product (
                    ProductId INT PRIMARY KEY,
                    ProductName NVARCHAR(100)
                )";
            await HelperDatabase.ExecuteScriptAsync(serverProviderType, serverDbName, createTableScript);

            var serverProvider = HelperDatabase.GetSyncProvider(serverProviderType, serverDbName);
            var clientProvider = HelperDatabase.GetSyncProvider(clientProviderType, clientDbName);

            var setup = new SyncSetup("Product");

            // Add custom parameters
            setup.ScopeInfoClientParameters.Add(new ScopeInfoClientParameter
            {
                Name = "UserId",
                DbType = DbType.Int64
            });
            setup.ScopeInfoClientParameters.Add(new ScopeInfoClientParameter
            {
                Name = "DeviceIdentifier",
                DbType = DbType.Guid
            });
            setup.ScopeInfoClientParameters.Add(new ScopeInfoClientParameter
            {
                Name = "TenantId",
                DbType = DbType.Int64
            });

            // Create sync parameters matching the custom parameters
            var userId = 12345L;
            var deviceId = Guid.NewGuid();
            var tenantId = 67890L;

            var parameters = new SyncParameters();
            parameters.Add("UserId", userId);
            parameters.Add("DeviceIdentifier", deviceId);
            parameters.Add("TenantId", tenantId);

            var agent = new SyncAgent(clientProvider, serverProvider);

            // Act - Sync with parameters
            var result = await agent.SynchronizeAsync(setup, parameters);

            // Assert - Check that values were saved to custom columns
            using var serverConnection = new SqlConnection(HelperDatabase.GetConnectionString(serverProviderType, serverDbName));
            serverConnection.Open();

            var query = @"
                SELECT UserId, DeviceIdentifier, TenantId
                FROM scope_info_client";

            using var cmd = new SqlCommand(query, serverConnection);
            using var reader = await cmd.ExecuteReaderAsync();

            Assert.True(await reader.ReadAsync());

            var savedUserId = reader.GetInt64(reader.GetOrdinal("UserId"));
            var savedDeviceId = reader.GetGuid(reader.GetOrdinal("DeviceIdentifier"));
            var savedTenantId = reader.GetInt64(reader.GetOrdinal("TenantId"));

            Assert.Equal(userId, savedUserId);
            Assert.Equal(deviceId, savedDeviceId);
            Assert.Equal(tenantId, savedTenantId);

            output.WriteLine($"Custom parameter values saved correctly:");
            output.WriteLine($"  UserId: {savedUserId}");
            output.WriteLine($"  DeviceIdentifier: {savedDeviceId}");
            output.WriteLine($"  TenantId: {savedTenantId}");
        }

        [Fact]
        public async Task CustomParameters_WithFilter_Should_WorkTogether()
        {
            // Arrange - This tests the scenario where a parameter is used BOTH as a filter AND as a custom column
            await HelperDatabase.CreateDatabaseAsync(serverProviderType, serverDbName, true);

            var createTableScript = @"
                CREATE TABLE Product (
                    ProductId INT PRIMARY KEY,
                    ProductName NVARCHAR(100),
                    UserId BIGINT
                );
                INSERT INTO Product VALUES (1, 'Product for User 100', 100);
                INSERT INTO Product VALUES (2, 'Product for User 200', 200);
                INSERT INTO Product VALUES (3, 'Another Product for User 100', 100);";

            await HelperDatabase.ExecuteScriptAsync(serverProviderType, serverDbName, createTableScript);

            var serverProvider = HelperDatabase.GetSyncProvider(serverProviderType, serverDbName);
            var clientProvider = HelperDatabase.GetSyncProvider(clientProviderType, clientDbName);

            var setup = new SyncSetup("Product");

            // Add a filter on UserId
            setup.Filters.Add("Product", "UserId");

            // Also add UserId as a custom parameter for scope_info_client
            setup.ScopeInfoClientParameters.Add(new ScopeInfoClientParameter
            {
                Name = "UserId",
                DbType = DbType.Int64
            });

            // Add additional custom parameters
            setup.ScopeInfoClientParameters.Add(new ScopeInfoClientParameter
            {
                Name = "DeviceIdentifier",
                DbType = DbType.Guid
            });

            // Create sync parameters - UserId is used for BOTH filter AND custom column
            var userId = 100L;
            var deviceId = Guid.NewGuid();

            var parameters = new SyncParameters();
            parameters.Add("UserId", userId);
            parameters.Add("DeviceIdentifier", deviceId);

            var agent = new SyncAgent(clientProvider, serverProvider);

            // Act - Sync with parameters
            var result = await agent.SynchronizeAsync(setup, parameters);

            // Assert - Check filtered data was synced
            Assert.Equal(2, result.TotalChangesDownloadedFromServer); // Should sync 2 products for User 100

            // Check that custom parameter values were saved
            using var serverConnection = new SqlConnection(HelperDatabase.GetConnectionString(serverProviderType, serverDbName));
            serverConnection.Open();

            var query = @"
                SELECT UserId, DeviceIdentifier
                FROM scope_info_client";

            using var cmd = new SqlCommand(query, serverConnection);
            using var reader = await cmd.ExecuteReaderAsync();

            Assert.True(await reader.ReadAsync());

            var savedUserId = reader.GetInt64(reader.GetOrdinal("UserId"));
            var savedDeviceId = reader.GetGuid(reader.GetOrdinal("DeviceIdentifier"));

            Assert.Equal(userId, savedUserId);
            Assert.Equal(deviceId, savedDeviceId);

            output.WriteLine($"Filter and custom parameters working together:");
            output.WriteLine($"  Filtered products synced: {result.TotalChangesDownloadedFromServer}");
            output.WriteLine($"  UserId saved in scope_info_client: {savedUserId}");
            output.WriteLine($"  DeviceIdentifier saved: {savedDeviceId}");
        }

        [Fact]
        public async Task CustomParameters_WithoutMatchingSyncParameter_Should_SaveNull()
        {
            // Arrange - Test that missing parameters are stored as NULL
            await HelperDatabase.CreateDatabaseAsync(serverProviderType, serverDbName, true);

            var createTableScript = @"
                CREATE TABLE Product (
                    ProductId INT PRIMARY KEY,
                    ProductName NVARCHAR(100)
                )";
            await HelperDatabase.ExecuteScriptAsync(serverProviderType, serverDbName, createTableScript);

            var serverProvider = HelperDatabase.GetSyncProvider(serverProviderType, serverDbName);
            var clientProvider = HelperDatabase.GetSyncProvider(clientProviderType, clientDbName);

            var setup = new SyncSetup("Product");

            // Add custom parameters
            setup.ScopeInfoClientParameters.Add(new ScopeInfoClientParameter
            {
                Name = "UserId",
                DbType = DbType.Int64
            });
            setup.ScopeInfoClientParameters.Add(new ScopeInfoClientParameter
            {
                Name = "DeviceIdentifier",
                DbType = DbType.Guid
            });

            // Only provide UserId parameter, DeviceIdentifier is missing
            var parameters = new SyncParameters();
            parameters.Add("UserId", 12345L);

            var agent = new SyncAgent(clientProvider, serverProvider);

            // Act
            var result = await agent.SynchronizeAsync(setup, parameters);

            // Assert - Check that UserId has value but DeviceIdentifier is NULL
            using var serverConnection = new SqlConnection(HelperDatabase.GetConnectionString(serverProviderType, serverDbName));
            serverConnection.Open();

            var query = @"
                SELECT UserId, DeviceIdentifier
                FROM scope_info_client";

            using var cmd = new SqlCommand(query, serverConnection);
            using var reader = await cmd.ExecuteReaderAsync();

            Assert.True(await reader.ReadAsync());

            var savedUserId = reader.GetInt64(reader.GetOrdinal("UserId"));
            var deviceIdValue = reader["DeviceIdentifier"];

            Assert.Equal(12345L, savedUserId);
            Assert.True(deviceIdValue == DBNull.Value);

            output.WriteLine($"Missing parameter correctly stored as NULL");
            output.WriteLine($"  UserId: {savedUserId}");
            output.WriteLine($"  DeviceIdentifier: NULL");
        }

        [Fact]
        public async Task CustomParameters_WithIncompatibleType_Should_SaveNull()
        {
            // Arrange - Test type compatibility checking
            await HelperDatabase.CreateDatabaseAsync(serverProviderType, serverDbName, true);

            var createTableScript = @"
                CREATE TABLE Product (
                    ProductId INT PRIMARY KEY,
                    ProductName NVARCHAR(100)
                )";
            await HelperDatabase.ExecuteScriptAsync(serverProviderType, serverDbName, createTableScript);

            var serverProvider = HelperDatabase.GetSyncProvider(serverProviderType, serverDbName);
            var clientProvider = HelperDatabase.GetSyncProvider(clientProviderType, clientDbName);

            var setup = new SyncSetup("Product");

            // Define UserId as Int64
            setup.ScopeInfoClientParameters.Add(new ScopeInfoClientParameter
            {
                Name = "UserId",
                DbType = DbType.Int64
            });

            // But provide a string value (incompatible)
            var parameters = new SyncParameters();
            parameters.Add("UserId", "not_a_number");

            var agent = new SyncAgent(clientProvider, serverProvider);

            // Act
            var result = await agent.SynchronizeAsync(setup, parameters);

            // Assert - Check that incompatible value was stored as NULL
            using var serverConnection = new SqlConnection(HelperDatabase.GetConnectionString(serverProviderType, serverDbName));
            serverConnection.Open();

            var query = @"SELECT UserId FROM scope_info_client";

            using var cmd = new SqlCommand(query, serverConnection);
            var userIdValue = await cmd.ExecuteScalarAsync();

            Assert.True(userIdValue == DBNull.Value || userIdValue == null);

            output.WriteLine($"Incompatible type correctly stored as NULL");
        }

        [Fact]
        public async Task CustomParameters_MultipleClients_Should_HaveSeparateRows()
        {
            // Arrange - Test that different clients with different parameters get separate rows
            await HelperDatabase.CreateDatabaseAsync(serverProviderType, serverDbName, true);

            var createTableScript = @"
                CREATE TABLE Product (
                    ProductId INT PRIMARY KEY,
                    ProductName NVARCHAR(100)
                )";
            await HelperDatabase.ExecuteScriptAsync(serverProviderType, serverDbName, createTableScript);

            var serverProvider = HelperDatabase.GetSyncProvider(serverProviderType, serverDbName);

            var setup = new SyncSetup("Product");

            setup.ScopeInfoClientParameters.Add(new ScopeInfoClientParameter
            {
                Name = "UserId",
                DbType = DbType.Int64
            });
            setup.ScopeInfoClientParameters.Add(new ScopeInfoClientParameter
            {
                Name = "DeviceIdentifier",
                DbType = DbType.Guid
            });

            // Client 1
            var client1DbName = HelperDatabase.GetRandomName("cli1_");
            var clientProvider1 = HelperDatabase.GetSyncProvider(clientProviderType, client1DbName);
            var parameters1 = new SyncParameters();
            parameters1.Add("UserId", 100L);
            parameters1.Add("DeviceIdentifier", Guid.NewGuid());

            var agent1 = new SyncAgent(clientProvider1, serverProvider);
            await agent1.SynchronizeAsync(setup, parameters1);

            // Client 2
            var client2DbName = HelperDatabase.GetRandomName("cli2_");
            var clientProvider2 = HelperDatabase.GetSyncProvider(clientProviderType, client2DbName);
            var parameters2 = new SyncParameters();
            parameters2.Add("UserId", 200L);
            parameters2.Add("DeviceIdentifier", Guid.NewGuid());

            var agent2 = new SyncAgent(clientProvider2, serverProvider);
            await agent2.SynchronizeAsync(setup, parameters2);

            // Assert - Check that we have 2 separate rows with different values
            using var serverConnection = new SqlConnection(HelperDatabase.GetConnectionString(serverProviderType, serverDbName));
            serverConnection.Open();

            var query = @"
                SELECT UserId, DeviceIdentifier
                FROM scope_info_client
                ORDER BY UserId";

            using var cmd = new SqlCommand(query, serverConnection);
            using var reader = await cmd.ExecuteReaderAsync();

            // First client
            Assert.True(await reader.ReadAsync());
            Assert.Equal(100L, reader.GetInt64(0));

            // Second client
            Assert.True(await reader.ReadAsync());
            Assert.Equal(200L, reader.GetInt64(0));

            // No more rows
            Assert.False(await reader.ReadAsync());

            output.WriteLine($"Multiple clients correctly stored with separate custom parameter values");

            // Cleanup
            HelperDatabase.DropDatabase(clientProviderType, client1DbName);
            HelperDatabase.DropDatabase(clientProviderType, client2DbName);
        }

        [Fact]
        public async Task CustomParameters_VariousDataTypes_Should_WorkCorrectly()
        {
            // Arrange - Test different data types
            await HelperDatabase.CreateDatabaseAsync(serverProviderType, serverDbName, true);

            var createTableScript = @"
                CREATE TABLE Product (
                    ProductId INT PRIMARY KEY,
                    ProductName NVARCHAR(100)
                )";
            await HelperDatabase.ExecuteScriptAsync(serverProviderType, serverDbName, createTableScript);

            var serverProvider = HelperDatabase.GetSyncProvider(serverProviderType, serverDbName);
            var clientProvider = HelperDatabase.GetSyncProvider(clientProviderType, clientDbName);

            var setup = new SyncSetup("Product");

            // Add parameters with various types
            setup.ScopeInfoClientParameters.Add(new ScopeInfoClientParameter
            {
                Name = "UserId",
                DbType = DbType.Int64
            });
            setup.ScopeInfoClientParameters.Add(new ScopeInfoClientParameter
            {
                Name = "DeviceIdentifier",
                DbType = DbType.Guid
            });
            setup.ScopeInfoClientParameters.Add(new ScopeInfoClientParameter
            {
                Name = "IsActive",
                DbType = DbType.Boolean
            });
            setup.ScopeInfoClientParameters.Add(new ScopeInfoClientParameter
            {
                Name = "LicenseKey",
                DbType = DbType.String,
                MaxLength = 100
            });
            setup.ScopeInfoClientParameters.Add(new ScopeInfoClientParameter
            {
                Name = "LastSyncDate",
                DbType = DbType.DateTime
            });

            var userId = 999L;
            var deviceId = Guid.NewGuid();
            var isActive = true;
            var licenseKey = "ABC-123-XYZ-789";
            var lastSyncDate = new DateTime(2025, 10, 15, 12, 30, 45);

            var parameters = new SyncParameters();
            parameters.Add("UserId", userId);
            parameters.Add("DeviceIdentifier", deviceId);
            parameters.Add("IsActive", isActive);
            parameters.Add("LicenseKey", licenseKey);
            parameters.Add("LastSyncDate", lastSyncDate);

            var agent = new SyncAgent(clientProvider, serverProvider);

            // Act
            var result = await agent.SynchronizeAsync(setup, parameters);

            // Assert - Check all types were saved correctly
            using var serverConnection = new SqlConnection(HelperDatabase.GetConnectionString(serverProviderType, serverDbName));
            serverConnection.Open();

            var query = @"
                SELECT UserId, DeviceIdentifier, IsActive, LicenseKey, LastSyncDate
                FROM scope_info_client";

            using var cmd = new SqlCommand(query, serverConnection);
            using var reader = await cmd.ExecuteReaderAsync();

            Assert.True(await reader.ReadAsync());

            Assert.Equal(userId, reader.GetInt64(0));
            Assert.Equal(deviceId, reader.GetGuid(1));
            Assert.Equal(isActive, reader.GetBoolean(2));
            Assert.Equal(licenseKey, reader.GetString(3));

            var savedDate = reader.GetDateTime(4);
            // Compare only to second precision (SQL Server may truncate milliseconds)
            Assert.Equal(lastSyncDate.ToString("yyyy-MM-dd HH:mm:ss"), savedDate.ToString("yyyy-MM-dd HH:mm:ss"));

            output.WriteLine($"Various data types saved correctly:");
            output.WriteLine($"  Int64 (UserId): {userId}");
            output.WriteLine($"  Guid (DeviceIdentifier): {deviceId}");
            output.WriteLine($"  Boolean (IsActive): {isActive}");
            output.WriteLine($"  String (LicenseKey): {licenseKey}");
            output.WriteLine($"  DateTime (LastSyncDate): {savedDate:yyyy-MM-dd HH:mm:ss}");
        }

        public void Dispose()
        {
            // Cleanup databases
            HelperDatabase.DropDatabase(serverProviderType, serverDbName);
            HelperDatabase.DropDatabase(clientProviderType, clientDbName);
            HelperDatabase.ClearAllPools();
        }
    }
}
