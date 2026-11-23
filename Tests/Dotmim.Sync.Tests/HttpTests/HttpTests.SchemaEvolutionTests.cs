using Wormhole.Sync.Enumerations;
using Wormhole.Sync.Tests.Core;
using Wormhole.Sync.Tests.Misc;
using Wormhole.Sync.Web.Client;
using Wormhole.Sync.Web.Server;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Wormhole.Sync.Tests.Models;
using Xunit;

namespace Wormhole.Sync.Tests.IntegrationTests
{
    /// <summary>
    /// HTTP integration tests for schema evolution (additive migrations).
    /// Tests verify that MigrateSchemaAsync + @sync_columns_present SP logic
    /// allows old clients (fewer columns) to sync with migrated servers over HTTP.
    /// </summary>
    public abstract partial class HttpTests
    {
        // -----------------------------------------------------------------------
        // Test 1: MigrateSchemaAsync records migration and rejects non-additive changes
        // -----------------------------------------------------------------------

        [Fact]
        public virtual async Task SchemaEvolution_MigrateSchemaAsync_ShouldRecordMigrationAndReprovision()
        {
            var options = new SyncOptions { DisableConstraintsOnApplyChanges = false };
            var setupV1 = CreateSchemaEvolutionSetupV1();
            var setupV2 = CreateSchemaEvolutionSetupV2();

            // Step 1: Initial HTTP sync with setupV1
            await this.Kestrel.StopAsync();
            this.AddSyncServer(this.serverProvider, setupV1, options);
            var uri = this.Kestrel.Run();

            foreach (var clientProvider in this.clientsProvider)
            {
                var agent = new SyncAgent(clientProvider, new WebRemoteOrchestrator(uri), options);
                var r = await agent.SynchronizeAsync(setupV1);
                Assert.True(r.TotalChangesDownloadedFromServer > 0);
            }

            // Step 2: Server-side migration using RemoteOrchestrator directly (not over HTTP)
            var remoteOrchestrator = new RemoteOrchestrator(this.serverProvider);

            // Read scope before migration to compare hash
            var scopeBefore = await remoteOrchestrator.GetScopeInfoAsync();
            var hashBefore = scopeBefore.SchemaHash;

            // Perform migration
            var migratedScope = await remoteOrchestrator.MigrateSchemaAsync("mig1", setupV2);

            // Step 3: Assert migration was recorded
            Assert.NotNull(migratedScope.Migrations);
            var migrations = migratedScope.GetMigrationsList();
            Assert.Contains("mig1", migrations);

            // Step 4: Assert schema hash changed
            Assert.NotEqual(hashBefore, migratedScope.SchemaHash);

            // Step 5: Verify calling MigrateSchemaAsync with a column removal throws
            var pcFullName = GetProductCategoryFullName();
            var setupRemoval = new SyncSetup(pcFullName);
            setupRemoval.Tables[pcFullName].Columns.AddRange("ProductCategoryId", "rowguid", "ModifiedDate", "Attribute With Space");

            var ex = await Assert.ThrowsAsync<SyncException>(async () =>
               await remoteOrchestrator.MigrateSchemaAsync("mig_bad", setupRemoval));
            Assert.Contains("non-additive", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        // -----------------------------------------------------------------------
        // Test 2: Old client syncs with new server (gets new rows, sends partial columns)
        // -----------------------------------------------------------------------

        [Fact]
        public virtual async Task SchemaEvolution_OldClientSyncsWithNewServer_ShouldSucceed()
        {
            var options = new SyncOptions { DisableConstraintsOnApplyChanges = false };
            var setupV1 = CreateSchemaEvolutionSetupV1();
            var setupV2 = CreateSchemaEvolutionSetupV2();

            // Step 1: Initial HTTP sync with setupV1
            await this.Kestrel.StopAsync();
            this.AddSyncServer(this.serverProvider, setupV1, options);
            var uri = this.Kestrel.Run();

            var clients = this.clientsProvider.ToList();

            foreach (var clientProvider in clients)
            {
                var agent = new SyncAgent(clientProvider, new WebRemoteOrchestrator(uri), options);
                var r = await agent.SynchronizeAsync(setupV1);
                Assert.True(r.TotalChangesDownloadedFromServer > 0);
            }

            // Step 2: Server-side migration
            var remoteOrchestrator = new RemoteOrchestrator(this.serverProvider);
            await remoteOrchestrator.MigrateSchemaAsync("mig1", setupV2);

            // Step 3: Server inserts a row with "Attribute With Space" = "SrvValue"
            var insertedId = await InsertProductCategoryWithAttributeOnServerAsync("SrvValue");

            // Step 4: Hot-swap setup to V2 (no restart needed)
            this.Kestrel.UpdateSyncSetup(setupV2);

            // Step 5: Old client syncs with setupV1 — should NOT throw (additive diff allowed)
            var oldClient = clients.First();
            var oldAgent = new SyncAgent(oldClient, new WebRemoteOrchestrator(uri), options);
            var result = await oldAgent.SynchronizeAsync(setupV1);

            // Should download the new row
            Assert.True(result.TotalChangesDownloadedFromServer >= 1);

            // Step 6: Old client inserts a row via raw SQL, then syncs
            var clientInsertedId = await InsertProductCategoryOnClientRawAsync(oldClient);

            result = await oldAgent.SynchronizeAsync(setupV1);
            Assert.Equal(1, result.TotalChangesUploadedToServer);

            // Step 7: Verify server row has NULL for "Attribute With Space" on the client-inserted row
            var attrValue = await ReadAttributeWithSpaceOnServerAsync(clientInsertedId);
            Assert.True(attrValue == null || attrValue == DBNull.Value,
               $"Expected NULL for 'Attribute With Space' on client-inserted row, got: {attrValue}");
        }

        // -----------------------------------------------------------------------
        // Test 3: New client syncs after migration and gets all columns
        // -----------------------------------------------------------------------

        [Fact]
        public virtual async Task SchemaEvolution_NewClientSyncsAfterMigration_GetsAllColumns()
        {
            var options = new SyncOptions { DisableConstraintsOnApplyChanges = false };
            var setupV1 = CreateSchemaEvolutionSetupV1();
            var setupV2 = CreateSchemaEvolutionSetupV2();

            // Step 1: Initial HTTP sync with setupV1
            await this.Kestrel.StopAsync();
            this.AddSyncServer(this.serverProvider, setupV1, options);
            var uri = this.Kestrel.Run();

            var clients = this.clientsProvider.ToList();
            var clientProvider = clients.First();

            var agent = new SyncAgent(clientProvider, new WebRemoteOrchestrator(uri), options);
            var r = await agent.SynchronizeAsync(setupV1);
            Assert.True(r.TotalChangesDownloadedFromServer > 0);

            // Step 2: Server-side migration
            var remoteOrchestrator = new RemoteOrchestrator(this.serverProvider);
            await remoteOrchestrator.MigrateSchemaAsync("mig1", setupV2);

            // Step 3: Server inserts a row with "Attribute With Space" = "TestValue"
            var insertedId = await InsertProductCategoryWithAttributeOnServerAsync("TestValue");

            // also insert two products to check for side-effects
            await this.serverProvider.AddProductAsync(productCategoryId: insertedId);
            await this.serverProvider.AddProductAsync(productCategoryId: insertedId);

            // Step 4: Hot-swap setup to V2 (no restart needed)
            this.Kestrel.UpdateSyncSetup(setupV2);

            // Step 5: Client upgrades: add column + register migration, framework handles the rest
            await AlterClientTableAddAttributeColumnAsync(clientProvider);
            HelperDatabase.ClearAllPools();

            // Step 6: Sync with setupV2 + SupportedMigrations — framework auto-reprovisions
            // Capture ReinitTables to verify which tables are re-initialized
            HashSet<string> capturedReinitTables = null;
            agent = new SyncAgent(clientProvider, new WebRemoteOrchestrator(uri), options);
            agent.SupportedMigrations = new List<string> { "mig1" };
            agent.LocalOrchestrator.OnDatabaseChangesApplying(args =>
            {
                if(args.ApplyChanges.ReinitTables is {} tbl)
                    capturedReinitTables = tbl;
            });

            var result = await agent.SynchronizeAsync(setupV2);

            Assert.True(result.TotalChangesDownloadedFromServer == 12, "only the product categories should be re-synched");

            // Verify on client that "Attribute With Space" = "TestValue"
            var clientAttrValue = await ReadAttributeWithSpaceOnClientAsync(clientProvider, insertedId);
            Assert.Equal("TestValue", clientAttrValue?.ToString());

            // Step 7: Client inserts row with "Attribute With Space" = "FromClient", syncs
            var clientInsertedId = await InsertProductCategoryWithAttributeOnClientAsync(clientProvider, "FromClient");

            result = await agent.SynchronizeAsync(setupV2);
            Assert.Equal(1, result.TotalChangesUploadedToServer);

            // Verify server got "FromClient" for that column
            var serverAttrValue = await ReadAttributeWithSpaceOnServerAsync(clientInsertedId);
            Assert.Equal("FromClient", serverAttrValue?.ToString());

            // Assert: ReinitTables contains ProductCategory but NOT Product
            Assert.NotNull(capturedReinitTables);

            var pcFullName = GetProductCategoryFullName();
            Assert.True(capturedReinitTables.Contains(pcFullName),
                $"Expected ReinitTables to contain '{pcFullName}', but it contained: [{string.Join(", ", capturedReinitTables)}]");

            var productFullName = GetProductFullName();
            Assert.False(capturedReinitTables.Contains(productFullName),
                $"Expected ReinitTables NOT to contain '{productFullName}', but it did");
        }

        // -----------------------------------------------------------------------
        // Test 4: Old client upgrades mid-lifecycle to new schema
        // -----------------------------------------------------------------------

        [Fact]
        public virtual async Task SchemaEvolution_OldClientUpgradesToNewSchema_ShouldWork()
        {
            var options = new SyncOptions { DisableConstraintsOnApplyChanges = false };
            var setupV1 = CreateSchemaEvolutionSetupV1();
            var setupV2 = CreateSchemaEvolutionSetupV2();

            // Step 1: Initial HTTP sync with setupV1
            await this.Kestrel.StopAsync();
            this.AddSyncServer(this.serverProvider, setupV1, options);
            var uri = this.Kestrel.Run();

            var clients = this.clientsProvider.ToList();
            var clientProvider = clients.First();

            var agent = new SyncAgent(clientProvider, new WebRemoteOrchestrator(uri), options);
            var r = await agent.SynchronizeAsync(setupV1);
            Assert.True(r.TotalChangesDownloadedFromServer > 0);

            // Step 2: Server-side migration, add row with attr = "SrvValue"
            var remoteOrchestrator = new RemoteOrchestrator(this.serverProvider);
            await remoteOrchestrator.MigrateSchemaAsync("mig1", setupV2);
            var insertedId = await InsertProductCategoryWithAttributeOnServerAsync("SrvValue");

            // Step 3: Hot-swap setup to V2 (no restart needed)
            this.Kestrel.UpdateSyncSetup(setupV2);

            // Step 4: Old client syncs with setupV1 (gets row without new column awareness)
            agent = new SyncAgent(clientProvider, new WebRemoteOrchestrator(uri), options);
            var result = await agent.SynchronizeAsync(setupV1);
            Assert.True(result.TotalChangesDownloadedFromServer >= 1);

            // Step 5: Old client "upgrades": add column + register migration
            await AlterClientTableAddAttributeColumnAsync(clientProvider);
            HelperDatabase.ClearAllPools();

            // Step 6: Sync with Reinitialize + SupportedMigrations — framework auto-reprovisions,
            // then Reinitialize re-downloads all rows with new column populated
            agent = new SyncAgent(clientProvider, new WebRemoteOrchestrator(uri), options);
            agent.SupportedMigrations = new List<string> { "mig1" };
            result = await agent.SynchronizeAsync(setupV2, SyncType.Reinitialize);

            Assert.True(result.TotalChangesDownloadedFromServer >= 1);

            // Step 7: Assert "Attribute With Space" = "SrvValue" on the re-downloaded row
            var clientAttrValue = await ReadAttributeWithSpaceOnClientAsync(clientProvider, insertedId);
            Assert.Equal("SrvValue", clientAttrValue?.ToString());
        }

        // -----------------------------------------------------------------------
        // Test 5: Bidirectional sync with mixed clients preserves new column values
        // -----------------------------------------------------------------------

        [Fact]
        public virtual async Task SchemaEvolution_BidirectionalWithMixedClients_PreservesNewColumnValues()
        {
            var options = new SyncOptions { DisableConstraintsOnApplyChanges = false };
            var setupV1 = CreateSchemaEvolutionSetupV1();
            var setupV2 = CreateSchemaEvolutionSetupV2();

            // We need at least 2 client providers for this test
            var clients = this.clientsProvider.ToList();
            if (clients.Count < 2)
            {
                var sqliteDbName = HelperDatabase.GetRandomName("http_se_sqlite2_");
                var secondClient = HelperDatabase.GetSyncProvider(ProviderType.Sqlite, sqliteDbName, false);
                clients.Add(secondClient);
            }

            var clientA = clients[0]; // old client (stays on V1)
            var clientB = clients[1]; // new client (will upgrade to V2)

            // Step 1: Initial HTTP sync with setupV1 for both clients
            await this.Kestrel.StopAsync();
            this.AddSyncServer(this.serverProvider, setupV1, options);
            var uri = this.Kestrel.Run();

            var agentA = new SyncAgent(clientA, new WebRemoteOrchestrator(uri), options);
            var rA = await agentA.SynchronizeAsync(setupV1);
            Assert.True(rA.TotalChangesDownloadedFromServer > 0);

            var agentB = new SyncAgent(clientB, new WebRemoteOrchestrator(uri), options);
            var rB = await agentB.SynchronizeAsync(setupV1);
            Assert.True(rB.TotalChangesDownloadedFromServer > 0);

            // Step 2: Server-side migration, insert row with attr = "FromServer"
            var remoteOrchestrator = new RemoteOrchestrator(this.serverProvider);
            await remoteOrchestrator.MigrateSchemaAsync("mig1", setupV2);
            var insertedId = await InsertProductCategoryWithAttributeOnServerAsync("FromServer");

            // Step 3: Hot-swap setup to V2 (no restart needed)
            this.Kestrel.UpdateSyncSetup(setupV2);

            // Step 4: Client A (old) syncs with setupV1 — gets the row
            agentA = new SyncAgent(clientA, new WebRemoteOrchestrator(uri), options);
            var resultA = await agentA.SynchronizeAsync(setupV1);
            Assert.True(resultA.TotalChangesDownloadedFromServer >= 1);

            // Step 5: Client A modifies the row's Name, syncs back
            var newName = "UpdatedByClientA";
            await UpdateProductCategoryNameOnClientAsync(clientA, insertedId, newName);

            resultA = await agentA.SynchronizeAsync(setupV1);
            Assert.Equal(1, resultA.TotalChangesUploadedToServer);

            // Verify server has updated Name but attr preserved as "FromServer"
            var serverName = await ReadNameOnServerAsync(insertedId);
            Assert.Equal(newName, serverName?.ToString());

            var serverAttr = await ReadAttributeWithSpaceOnServerAsync(insertedId);
            Assert.Equal("FromServer", serverAttr?.ToString());

            // Step 6: Client B upgrades: add column + register migration
            await AlterClientTableAddAttributeColumnAsync(clientB);
            HelperDatabase.ClearAllPools();

            // Step 7: Client B syncs with Reinitialize + SupportedMigrations — framework auto-reprovisions
            agentB = new SyncAgent(clientB, new WebRemoteOrchestrator(uri), options);
            agentB.SupportedMigrations = new List<string> { "mig1" };
            var resultB = await agentB.SynchronizeAsync(setupV2, SyncType.Reinitialize);

            Assert.True(resultB.TotalChangesDownloadedFromServer >= 1);

            // Verify Client B has updated Name AND attr = "FromServer"
            var clientBAttrValue = await ReadAttributeWithSpaceOnClientAsync(clientB, insertedId);
            Assert.Equal("FromServer", clientBAttrValue?.ToString());
        }

        // -----------------------------------------------------------------------
        // Test 6: Full lifecycle — V2 client syncs with V1 server, then server migrates
        // -----------------------------------------------------------------------

        [Fact]
        public virtual async Task SchemaEvolution_NewClientSyncsWithOldServer_ThenServerMigrates_ShouldAutoReinitialize()
        {
            var options = new SyncOptions { DisableConstraintsOnApplyChanges = false };
            var setupV1 = CreateSchemaEvolutionSetupV1();
            var setupV2 = CreateSchemaEvolutionSetupV2();

            // Step 1: Server provisioned with V1, client initial sync with V1
            await this.Kestrel.StopAsync();
            this.AddSyncServer(this.serverProvider, setupV1, options);
            var uri = this.Kestrel.Run();

            var clients = this.clientsProvider.ToList();
            var clientProvider = clients.First();

            var agent = new SyncAgent(clientProvider, new WebRemoteOrchestrator(uri), options);
            var r = await agent.SynchronizeAsync(setupV1);
            Assert.True(r.TotalChangesDownloadedFromServer > 0);

            // Step 2: Client locally adds the new column
            await AlterClientTableAddAttributeColumnAsync(clientProvider);
            HelperDatabase.ClearAllPools();

            // Step 3: Client syncs with V2 setup + SupportedMigrations against V1 server
            // Server tolerates schema mismatch (client is purely ahead) → optimized flow
            agent = new SyncAgent(clientProvider, new WebRemoteOrchestrator(uri), options);
            agent.SupportedMigrations = new List<string> { "mig1" };
            var result = await agent.SynchronizeAsync(setupV2);

            // Sync succeeds (server tolerates client-ahead schema), no exception
            Assert.NotNull(result);

            // Step 4: Client syncs again — server still tolerates (client ahead, no server migrations)
            agent = new SyncAgent(clientProvider, new WebRemoteOrchestrator(uri), options);
            agent.SupportedMigrations = new List<string> { "mig1" };
            result = await agent.SynchronizeAsync(setupV2);
            Assert.NotNull(result);

            // Step 5: Server inserts row with "Attribute With Space" = "SrvValue"
            var insertedId = await InsertProductCategoryWithAttributeOnServerAsync("SrvValue");

            // Step 6: Server migrates
            var remoteOrchestrator = new RemoteOrchestrator(this.serverProvider);
            await remoteOrchestrator.MigrateSchemaAsync("mig1", setupV2);

            // Step 7: Hot-swap setup to V2 on the running server
            this.Kestrel.UpdateSyncSetup(setupV2);

            // Step 8: Client syncs — server rejects optimized flow (client supports "mig1"
            // which server now also has → needs reprovisioning), falls back to traditional
            // flow, auto-reprovisions + selective reinit in ONE sync
            agent = new SyncAgent(clientProvider, new WebRemoteOrchestrator(uri), options);
            agent.SupportedMigrations = new List<string> { "mig1" };
            result = await agent.SynchronizeAsync(setupV2);

            // Assert: auto-reprovision fired and sync succeeded
            Assert.NotNull(result);
            Assert.True(result.TotalChangesDownloadedFromServer >= 1);

            // Assert: "Attribute With Space" = "SrvValue" on client for the inserted row
            var clientAttrValue = await ReadAttributeWithSpaceOnClientAsync(clientProvider, insertedId);
            Assert.Equal("SrvValue", clientAttrValue?.ToString());

            // Assert: sync type stays Normal (NOT ReinitializeWithUpload)
            // (verified implicitly: we didn't set SyncType.Reinitialize and the sync completed normally)
        }

        // -----------------------------------------------------------------------
        // Test 7: Crashed migration sync persists ReinitTables for next sync
        // -----------------------------------------------------------------------

        [Fact]
        public virtual async Task SchemaEvolution_CrashedMigrationSync_ReinitTablesPersistedForNextSync()
        {
            var options = new SyncOptions { DisableConstraintsOnApplyChanges = false };

            // V1: ProductCategory (basic columns) + Product (basic columns)
            // V2: ProductCategory (+ "Attribute With Space") + Product (unchanged)
            var setupV1 = CreateSchemaEvolutionWithProductSetupV1();
            var setupV2 = CreateSchemaEvolutionWithProductSetupV2();

            // Step 1: Initial HTTP sync with setupV1 (both tables)
            await this.Kestrel.StopAsync();
            this.AddSyncServer(this.serverProvider, setupV1, options);
            var uri = this.Kestrel.Run();

            var clients = this.clientsProvider.ToList();
            var clientProvider = clients.First();

            var agent = new SyncAgent(clientProvider, new WebRemoteOrchestrator(uri), options);
            var r = await agent.SynchronizeAsync(setupV1);
            Assert.True(r.TotalChangesDownloadedFromServer > 0);

            // Step 2: Server-side migration (only ProductCategory gets new column)
            var remoteOrchestrator = new RemoteOrchestrator(this.serverProvider);
            await remoteOrchestrator.MigrateSchemaAsync("mig1", setupV2);

            // Step 3: Server inserts data in both tables
            var insertedPcId = await InsertProductCategoryWithAttributeOnServerAsync("CrashTestValue");
            await this.serverProvider.AddProductAsync(productCategoryId: insertedPcId);

            // Step 4: Hot-swap setup to V2
            this.Kestrel.UpdateSyncSetup(setupV2);

            // Step 5: Client upgrades: add column to ProductCategory
            await AlterClientTableAddAttributeColumnAsync(clientProvider);
            HelperDatabase.ClearAllPools();

            // Step 6: First sync attempt — use interceptor to CRASH during apply
            agent = new SyncAgent(clientProvider, new WebRemoteOrchestrator(uri), options);
            agent.SupportedMigrations = new List<string> { "mig1" };

            agent.LocalOrchestrator.OnDatabaseChangesApplying(args =>
            {
                throw new Exception("Simulated crash during apply changes");
            });

            var crashed = false;
            try
            {
                await agent.SynchronizeAsync(setupV2);
            }
            catch (Exception)
            {
                crashed = true;
            }
            Assert.True(crashed, "First sync should have crashed");

            // Step 7: Second sync with a NEW agent — should succeed
            // Capture ReinitTables to verify which tables are re-initialized
            HashSet<string> capturedReinitTables = null;

            agent = new SyncAgent(clientProvider, new WebRemoteOrchestrator(uri), options);
            agent.SupportedMigrations = new List<string> { "mig1" };

            agent.LocalOrchestrator.OnDatabaseChangesApplying(args =>
            {
                capturedReinitTables = args.ApplyChanges.ReinitTables;
            });

            var result = await agent.SynchronizeAsync(setupV2);

            // Assert: sync succeeded
            Assert.NotNull(result);

            // Assert: ReinitTables contains ProductCategory but NOT Product
            Assert.NotNull(capturedReinitTables);

            var pcFullName = GetProductCategoryFullName();
            Assert.True(capturedReinitTables.Contains(pcFullName),
               $"Expected ReinitTables to contain '{pcFullName}', but it contained: [{string.Join(", ", capturedReinitTables)}]");

            var productFullName = GetProductFullName();
            Assert.False(capturedReinitTables.Contains(productFullName),
               $"Expected ReinitTables NOT to contain '{productFullName}', but it did");

            // Verify on client that "Attribute With Space" = "CrashTestValue"
            // (confirms ProductCategory was re-initialized with fresh server data)
            var clientAttrValue = await ReadAttributeWithSpaceOnClientAsync(clientProvider, insertedPcId);
            Assert.Equal("CrashTestValue", clientAttrValue?.ToString());
        }

        // -----------------------------------------------------------------------
        // Test 8: A client that does not support a newer server migration keeps its schema
        //         in the traditional flow and keeps syncing
        // -----------------------------------------------------------------------

        [Fact]
        public virtual async Task SchemaEvolution_ClientNotSupportingNewServerMigration_TraditionalFlow_KeepsItsSchemaAndKeepsSyncing()
        {
            var options = new SyncOptions { DisableConstraintsOnApplyChanges = false };
            var uri = await this.SetupClientProvisionedForMig1Async(options);
            var clientProvider = this.clientsProvider.First();

            // Server applies "mig2", which this client does not support
            await this.AddMig2ColumnOnServerAsync();
            var remoteOrchestrator = new RemoteOrchestrator(this.serverProvider);
            await remoteOrchestrator.MigrateSchemaAsync("mig2", CreateSchemaEvolutionSetupV3());
            this.Kestrel.UpdateSyncSetup(CreateSchemaEvolutionSetupV3());
            var serverInsertedId = await InsertProductCategoryWithAttributeOnServerAsync("AfterMig2");

            // The old client (like the app: no explicit setup) goes through the traditional flow, as after a failed optimized attempt
            var clientInsertedId = await InsertProductCategoryWithAttributeOnClientAsync(clientProvider, "FromOldClient");
            var traditionalOptions = new SyncOptions { DisableConstraintsOnApplyChanges = false, UseOptimizedFlow = false };
            var agent = new SyncAgent(clientProvider, new WebRemoteOrchestrator(uri), traditionalOptions);
            agent.SupportedMigrations = new List<string> { "mig1" };
            var result = await agent.SynchronizeAsync();

            Assert.Equal(1, result.TotalChangesUploadedToServer);
            Assert.True(result.TotalChangesDownloadedFromServer >= 1);
            Assert.Equal("AfterMig2", (await ReadAttributeWithSpaceOnClientAsync(clientProvider, serverInsertedId))?.ToString());
            Assert.Equal("FromOldClient", (await ReadAttributeWithSpaceOnServerAsync(clientInsertedId))?.ToString());

            // The client schema still has no "mig2" column
            var clientScope = await agent.LocalOrchestrator.GetScopeInfoAsync();
            var clientTable = clientScope.Schema.Tables.First(t => t.TableName == "ProductCategory");
            Assert.Null(clientTable.Columns["Mig2Column"]);
            Assert.NotNull(clientTable.Columns["Attribute With Space"]);

            // And the next sync (optimized flow again) uploads a local change
            await InsertProductCategoryWithAttributeOnClientAsync(clientProvider, "Second");
            agent = new SyncAgent(clientProvider, new WebRemoteOrchestrator(uri), options);
            agent.SupportedMigrations = new List<string> { "mig1" };
            result = await agent.SynchronizeAsync();
            Assert.Equal(1, result.TotalChangesUploadedToServer);
        }

        // -----------------------------------------------------------------------
        // Test 9: A client that supports the newer server migration still re-provisions for it
        // -----------------------------------------------------------------------

        [Fact]
        public virtual async Task SchemaEvolution_ClientSupportingNewServerMigration_TraditionalFlow_ReprovisionsForIt()
        {
            var options = new SyncOptions { DisableConstraintsOnApplyChanges = false };
            var uri = await this.SetupClientProvisionedForMig1Async(options);
            var clientProvider = this.clientsProvider.First();

            await this.AddMig2ColumnOnServerAsync();
            var remoteOrchestrator = new RemoteOrchestrator(this.serverProvider);
            await remoteOrchestrator.MigrateSchemaAsync("mig2", CreateSchemaEvolutionSetupV3());
            this.Kestrel.UpdateSyncSetup(CreateSchemaEvolutionSetupV3());
            var serverInsertedId = await InsertProductCategoryWithAttributeOnServerAsync("AfterMig2");
            await this.SetMig2ColumnOnServerAsync(serverInsertedId, "Mig2Value");

            // The client upgrades: adds the column and supports both migrations
            await this.ExecuteOnClientAsync(clientProvider, "ALTER TABLE ProductCategory ADD [Mig2Column] text NULL;");
            HelperDatabase.ClearAllPools();

            var traditionalOptions = new SyncOptions { DisableConstraintsOnApplyChanges = false, UseOptimizedFlow = false };
            var agent = new SyncAgent(clientProvider, new WebRemoteOrchestrator(uri), traditionalOptions);
            agent.SupportedMigrations = new List<string> { "mig1", "mig2" };
            var result = await agent.SynchronizeAsync(CreateSchemaEvolutionSetupV3());

            var clientScope = await agent.LocalOrchestrator.GetScopeInfoAsync();
            var clientTable = clientScope.Schema.Tables.First(t => t.TableName == "ProductCategory");
            Assert.NotNull(clientTable.Columns["Mig2Column"]);
            Assert.Equal("Mig2Value", (await this.ReadMig2ColumnOnClientAsync(clientProvider, serverInsertedId))?.ToString());
        }

        // -----------------------------------------------------------------------
        // Test 10: A never-synced client with its own tables keeps its columns when the server
        //          has a migration the client does not support
        // -----------------------------------------------------------------------

        [Fact]
        public virtual async Task SchemaEvolution_NewClientWithOwnTables_NotSupportingServerMigration_KeepsItsColumns()
        {
            var options = new SyncOptions { DisableConstraintsOnApplyChanges = false };

            // Server provisioned with V1, then migrated to "mig1" and "mig2"
            var remoteOrchestrator = new RemoteOrchestrator(this.serverProvider);
            await remoteOrchestrator.ProvisionAsync(CreateSchemaEvolutionSetupV1());
            await remoteOrchestrator.MigrateSchemaAsync("mig1", CreateSchemaEvolutionSetupV2());
            await this.AddMig2ColumnOnServerAsync();
            await remoteOrchestrator.MigrateSchemaAsync("mig2", CreateSchemaEvolutionSetupV3());

            await this.Kestrel.StopAsync();
            this.AddSyncServer(this.serverProvider, CreateSchemaEvolutionSetupV3(), options);
            var uri = this.Kestrel.Run();

            // The client app creates its own table, with the "mig1" column but without the "mig2" column
            var clientProvider = HelperDatabase.GetSyncProvider(ProviderType.Sqlite, HelperDatabase.GetRandomName("http_se_own_"), false);
            await this.ExecuteOnClientAsync(clientProvider,
                "CREATE TABLE ProductCategory (ProductCategoryId text NOT NULL PRIMARY KEY, Name text NOT NULL, rowguid text NULL, ModifiedDate datetime NULL, [Attribute With Space] text NULL);");

            var agent = new SyncAgent(clientProvider, new WebRemoteOrchestrator(uri), options);
            agent.SupportedMigrations = new List<string> { "mig1" };
            var result = await agent.SynchronizeAsync();
            Assert.True(result.TotalChangesDownloadedFromServer > 0);

            var clientScope = await agent.LocalOrchestrator.GetScopeInfoAsync();
            var clientTable = clientScope.Schema.Tables.First(t => t.TableName == "ProductCategory");
            Assert.Null(clientTable.Columns["Mig2Column"]);

            // A local change uploads
            await InsertProductCategoryWithAttributeOnClientAsync(clientProvider, "FromOwnTable");
            agent = new SyncAgent(clientProvider, new WebRemoteOrchestrator(uri), options);
            agent.SupportedMigrations = new List<string> { "mig1" };
            result = await agent.SynchronizeAsync();
            Assert.Equal(1, result.TotalChangesUploadedToServer);
        }

        // -----------------------------------------------------------------------
        // Test 11: A client ahead of the server re-provisions once the server has its migration
        // -----------------------------------------------------------------------

        [Fact]
        public virtual async Task SchemaEvolution_ClientAheadOfServer_ReprovisionsOnceTheServerHasTheMigration()
        {
            var options = new SyncOptions { DisableConstraintsOnApplyChanges = false };
            var clientProvider = this.clientsProvider.First();

            // The client supports "mig1" and "mig2" (both columns added), the server only has "mig1"
            await this.Kestrel.StopAsync();
            this.AddSyncServer(this.serverProvider, CreateSchemaEvolutionSetupV1(), options);
            var uri = this.Kestrel.Run();
            await new SyncAgent(clientProvider, new WebRemoteOrchestrator(uri), options).SynchronizeAsync(CreateSchemaEvolutionSetupV1());

            var remoteOrchestrator = new RemoteOrchestrator(this.serverProvider);
            await remoteOrchestrator.MigrateSchemaAsync("mig1", CreateSchemaEvolutionSetupV2());
            this.Kestrel.UpdateSyncSetup(CreateSchemaEvolutionSetupV2());

            await AlterClientTableAddAttributeColumnAsync(clientProvider);
            await this.ExecuteOnClientAsync(clientProvider, "ALTER TABLE ProductCategory ADD [Mig2Column] text NULL;");
            HelperDatabase.ClearAllPools();

            var agent = new SyncAgent(clientProvider, new WebRemoteOrchestrator(uri), options);
            agent.SupportedMigrations = new List<string> { "mig1", "mig2" };
            await agent.SynchronizeAsync();

            // The server applies "mig2"
            await this.AddMig2ColumnOnServerAsync();
            await remoteOrchestrator.MigrateSchemaAsync("mig2", CreateSchemaEvolutionSetupV3());
            this.Kestrel.UpdateSyncSetup(CreateSchemaEvolutionSetupV3());
            var serverInsertedId = await InsertProductCategoryWithAttributeOnServerAsync("AfterMig2");
            await this.SetMig2ColumnOnServerAsync(serverInsertedId, "Mig2Value");

            agent = new SyncAgent(clientProvider, new WebRemoteOrchestrator(uri), options);
            agent.SupportedMigrations = new List<string> { "mig1", "mig2" };
            await agent.SynchronizeAsync();

            var clientScope = await agent.LocalOrchestrator.GetScopeInfoAsync();
            var clientTable = clientScope.Schema.Tables.First(t => t.TableName == "ProductCategory");
            Assert.NotNull(clientTable.Columns["Mig2Column"]);
            Assert.Equal("Mig2Value", (await this.ReadMig2ColumnOnClientAsync(clientProvider, serverInsertedId))?.ToString());
        }

        // -----------------------------------------------------------------------
        // Test 12: A client that does not declare its migrations (before 1.8.9) is handed the scope of the
        //          migration configured for such clients, never a newer one
        // -----------------------------------------------------------------------

        [Fact]
        public virtual async Task SchemaEvolution_UndeclaredClient_IsHandedTheScopeOfTheConfiguredMigration()
        {
            var options = new SyncOptions { DisableConstraintsOnApplyChanges = false };
            var uri = await this.SetupClientProvisionedForMig1Async(options, new WebServerOptions { UndeclaredClientsMigration = "mig1" });
            await this.MigrateServerToMig2Async();

            // the public API sends no declaration, as a client before 1.8.9
            var served = await new WebRemoteOrchestrator(uri).GetScopeInfoAsync();

            var servedTable = served.Schema.Tables.First(t => t.TableName == "ProductCategory");
            Assert.Equal(new[] { "mig1" }, served.GetMigrationsList());
            Assert.NotNull(servedTable.Columns["Attribute With Space"]);
            Assert.Null(servedTable.Columns["Mig2Column"]);
            Assert.DoesNotContain("Mig2Column", served.Setup.Tables[GetProductCategoryFullName()].Columns);
        }

        // -----------------------------------------------------------------------
        // Test 13: An old client [mig1] keeps syncing against a server at [mig1, mig2], in the traditional and the
        //          optimized flow, even when it re-provisions from the scope it is handed (as clients before 1.8.9 do)
        // -----------------------------------------------------------------------

        [Fact]
        public virtual async Task SchemaEvolution_UndeclaredOldClient_TraditionalAndOptimizedFlow_KeepsSyncing()
        {
            var options = new SyncOptions { DisableConstraintsOnApplyChanges = false };
            var uri = await this.SetupClientProvisionedForMig1Async(options, new WebServerOptions { UndeclaredClientsMigration = "mig1" });
            var clientProvider = this.clientsProvider.First();
            await this.MigrateServerToMig2Async();
            var serverInsertedId = await InsertProductCategoryWithAttributeOnServerAsync("AfterMig2");
            await this.SetMig2ColumnOnServerAsync(serverInsertedId, "Mig2Value");

            // a client before 1.8.9 re-provisions from the handed scope whenever the server has a migration it supports
            var oldClient = new SyncAgent(clientProvider, new WebRemoteOrchestrator(uri), options);
            var served = await new WebRemoteOrchestrator(uri).GetScopeInfoAsync();
            await oldClient.LocalOrchestrator.ProvisionAsync(served, SyncProvision.StoredProcedures | SyncProvision.Triggers, true);

            // traditional flow: downloads the server row, then updates it (not knowing Mig2Column) and inserts one
            var result = await this.OldClient(clientProvider, uri, useOptimizedFlow: false).SynchronizeAsync();
            await UpdateProductCategoryNameOnClientAsync(clientProvider, serverInsertedId, "UpdatedByOldClient");
            var clientInsertedId = await InsertProductCategoryWithAttributeOnClientAsync(clientProvider, "FromOldClient");
            result = await this.OldClient(clientProvider, uri, useOptimizedFlow: false).SynchronizeAsync();
            Assert.Equal(2, result.TotalChangesUploadedToServer);

            // optimized flow
            var secondId = await InsertProductCategoryWithAttributeOnClientAsync(clientProvider, "Optimized");
            result = await this.OldClient(clientProvider, uri, useOptimizedFlow: true).SynchronizeAsync();
            Assert.Equal(1, result.TotalChangesUploadedToServer);

            var clientScope = await oldClient.LocalOrchestrator.GetScopeInfoAsync();
            Assert.Null(clientScope.Schema.Tables.First(t => t.TableName == "ProductCategory").Columns["Mig2Column"]);
            Assert.Equal("UpdatedByOldClient", (await ReadNameOnServerAsync(serverInsertedId))?.ToString());
            Assert.Equal("Mig2Value", (await this.ReadMig2ColumnOnServerAsync(serverInsertedId))?.ToString());
            Assert.Equal("FromOldClient", (await ReadAttributeWithSpaceOnServerAsync(clientInsertedId))?.ToString());
            Assert.Equal("Optimized", (await ReadAttributeWithSpaceOnServerAsync(secondId))?.ToString());
        }

        // -----------------------------------------------------------------------
        // Test 14: A client that declares the newer migration is not handed the scope for undeclared clients,
        //          neither fresh nor when upgrading a database provisioned for "mig1"
        // -----------------------------------------------------------------------

        [Fact]
        public virtual async Task SchemaEvolution_DeclaringClient_IsNotDowngraded_FreshAndUpgrading()
        {
            var options = new SyncOptions { DisableConstraintsOnApplyChanges = false };
            var uri = await this.SetupClientProvisionedForMig1Async(options, new WebServerOptions { UndeclaredClientsMigration = "mig1" });
            var clientProvider = this.clientsProvider.First();
            await this.MigrateServerToMig2Async();
            var serverInsertedId = await InsertProductCategoryWithAttributeOnServerAsync("AfterMig2");
            await this.SetMig2ColumnOnServerAsync(serverInsertedId, "Mig2Value");

            // upgrading: the app adds the column and declares "mig2"
            await this.ExecuteOnClientAsync(clientProvider, "ALTER TABLE ProductCategory ADD [Mig2Column] text NULL;");
            HelperDatabase.ClearAllPools();
            var agent = new SyncAgent(clientProvider, new WebRemoteOrchestrator(uri), options);
            agent.SupportedMigrations = new List<string> { "mig1", "mig2" };
            await agent.SynchronizeAsync();

            var clientTable = (await agent.LocalOrchestrator.GetScopeInfoAsync()).Schema.Tables.First(t => t.TableName == "ProductCategory");
            Assert.NotNull(clientTable.Columns["Mig2Column"]);
            Assert.Equal("Mig2Value", (await this.ReadMig2ColumnOnClientAsync(clientProvider, serverInsertedId))?.ToString());

            // fresh
            var freshProvider = HelperDatabase.GetSyncProvider(ProviderType.Sqlite, HelperDatabase.GetRandomName("http_se_fresh_"), false);
            var fresh = new SyncAgent(freshProvider, new WebRemoteOrchestrator(uri), options);
            fresh.SupportedMigrations = new List<string> { "mig1", "mig2" };
            await fresh.SynchronizeAsync();

            var freshTable = (await fresh.LocalOrchestrator.GetScopeInfoAsync()).Schema.Tables.First(t => t.TableName == "ProductCategory");
            Assert.NotNull(freshTable.Columns["Mig2Column"]);
            Assert.Equal("Mig2Value", (await this.ReadMig2ColumnOnClientAsync(freshProvider, serverInsertedId))?.ToString());
        }

        // -----------------------------------------------------------------------
        // Test 15: A client already stuck (its scope has the "mig2" column, its table does not, as clients before 1.8.9
        //          were left) selects its changes again once the column exists
        // -----------------------------------------------------------------------

        [Fact]
        public virtual async Task SchemaEvolution_StuckClient_RecoversOnceTheColumnExists()
        {
            var options = new SyncOptions { DisableConstraintsOnApplyChanges = false };
            await this.SetupClientProvisionedForMig1Async(options);
            var clientProvider = this.clientsProvider.First();
            await this.MigrateServerToMig2Async();
            var localOrchestrator = await this.StuckClientAsync(clientProvider, options, version: null);

            await InsertProductCategoryWithAttributeOnClientAsync(clientProvider, "WhileStuck");
            var scopeInfoClient = await localOrchestrator.GetScopeInfoClientAsync();
            var stuck = await Assert.ThrowsAnyAsync<Exception>(async () => await localOrchestrator.GetChangesAsync(scopeInfoClient));
            Assert.Contains("no such column", stuck.Message);

            await this.ExecuteOnClientAsync(clientProvider, "ALTER TABLE ProductCategory ADD [Mig2Column] text NULL;");
            HelperDatabase.ClearAllPools();

            var changes = await localOrchestrator.GetChangesAsync(scopeInfoClient);
            Assert.Equal(1, changes.ClientChangesSelected.TotalChangesSelected);
        }

        // -----------------------------------------------------------------------
        // Test 16: A client already stuck recovers on its first sync with 1.8.9, even without the column: the upgrade
        //          from its older version re-provisions it from its own scope, without the column its table lacks
        // -----------------------------------------------------------------------

        [Fact]
        public virtual async Task SchemaEvolution_StuckClient_RecoversWhenUpgradedFromAnOlderVersion()
        {
            var options = new SyncOptions { DisableConstraintsOnApplyChanges = false };
            var uri = await this.SetupClientProvisionedForMig1Async(options);
            var clientProvider = this.clientsProvider.First();
            await this.MigrateServerToMig2Async();
            var localOrchestrator = await this.StuckClientAsync(clientProvider, options, version: "1.8.3.0");

            await InsertProductCategoryWithAttributeOnClientAsync(clientProvider, "WhileStuck");
            var result = await this.OldClient(clientProvider, uri, useOptimizedFlow: true).SynchronizeAsync();

            Assert.Equal(1, result.TotalChangesUploadedToServer);
            var clientScope = await localOrchestrator.GetScopeInfoAsync();
            Assert.Null(clientScope.Schema.Tables.First(t => t.TableName == "ProductCategory").Columns["Mig2Column"]);
        }

        /// <summary>
        /// Helper: the stuck state of a client before 1.8.9: it adopted the full server scope (with "Mig2Column"), its table has no such column.
        /// </summary>
        private async Task<LocalOrchestrator> StuckClientAsync(CoreProvider clientProvider, SyncOptions options, string version)
        {
            var localOrchestrator = new LocalOrchestrator(clientProvider, options);
            var serverScope = await new RemoteOrchestrator(this.serverProvider).GetScopeInfoAsync();
            var clientScope = await localOrchestrator.GetScopeInfoAsync();
            clientScope.Schema = serverScope.Schema;
            clientScope.Setup = serverScope.Setup;
            if (version != null)
                clientScope.Version = version;
            await localOrchestrator.SaveScopeInfoAsync(clientScope);
            return localOrchestrator;
        }

        /// <summary>
        /// Helper: a client [mig1] that does not declare its migrations, as a client before 1.8.9.
        /// </summary>
        private SyncAgent OldClient(CoreProvider clientProvider, string uri, bool useOptimizedFlow)
        {
            var agent = new SyncAgent(clientProvider, new WebRemoteOrchestrator(uri),
                new SyncOptions { DisableConstraintsOnApplyChanges = false, UseOptimizedFlow = useOptimizedFlow });
            agent.SupportedMigrations = new List<string> { "mig1" };
            agent.LocalOrchestrator.OnSessionBegin(args => args.Context.AdditionalProperties?.Remove(SyncContext.SupportedMigrationsKey));
            return agent;
        }

        /// <summary>
        /// Helper: the server applies "mig2" (adds "Mig2Column").
        /// </summary>
        private async Task MigrateServerToMig2Async()
        {
            await this.AddMig2ColumnOnServerAsync();
            await new RemoteOrchestrator(this.serverProvider).MigrateSchemaAsync("mig2", CreateSchemaEvolutionSetupV3());
            this.Kestrel.UpdateSyncSetup(CreateSchemaEvolutionSetupV3());
        }

        /// <summary>
        /// Helper: read "Mig2Column" on the server.
        /// </summary>
        private async Task<object> ReadMig2ColumnOnServerAsync(string productCategoryId)
        {
            var salesSchema = this.serverProvider.UseFallbackSchema() ? "SalesLT" : null;
            var tableName = string.IsNullOrEmpty(salesSchema) ? "[ProductCategory]" : $"[{salesSchema}].[ProductCategory]";

            using var connection = this.serverProvider.CreateConnection();
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT [Mig2Column] FROM {tableName} WHERE [ProductCategoryId] = @id";

            var pId = command.CreateParameter();
            pId.ParameterName = "@id";
            pId.Value = productCategoryId;
            command.Parameters.Add(pId);

            return await command.ExecuteScalarAsync();
        }

        /// <summary>
        /// Helper: server with setupV1, client synced, then server migrated to "mig1" and the client upgraded to it
        /// (column added, "mig1" supported and provisioned). Returns the server uri.
        /// </summary>
        private async Task<string> SetupClientProvisionedForMig1Async(SyncOptions options, WebServerOptions webServerOptions = null)
        {
            await this.Kestrel.StopAsync();
            this.AddSyncServer(this.serverProvider, CreateSchemaEvolutionSetupV1(), options, webServerOptions);
            var uri = this.Kestrel.Run();

            var clientProvider = this.clientsProvider.First();
            var agent = new SyncAgent(clientProvider, new WebRemoteOrchestrator(uri), options);
            await agent.SynchronizeAsync(CreateSchemaEvolutionSetupV1());

            var remoteOrchestrator = new RemoteOrchestrator(this.serverProvider);
            await remoteOrchestrator.MigrateSchemaAsync("mig1", CreateSchemaEvolutionSetupV2());
            this.Kestrel.UpdateSyncSetup(CreateSchemaEvolutionSetupV2());

            await AlterClientTableAddAttributeColumnAsync(clientProvider);
            HelperDatabase.ClearAllPools();

            agent = new SyncAgent(clientProvider, new WebRemoteOrchestrator(uri), options);
            agent.SupportedMigrations = new List<string> { "mig1" };
            await agent.SynchronizeAsync(CreateSchemaEvolutionSetupV2());

            return uri;
        }

        /// <summary>
        /// Helper: setupV3 for ProductCategory including "Attribute With Space" ("mig1") and "Mig2Column" ("mig2").
        /// </summary>
        private SyncSetup CreateSchemaEvolutionSetupV3()
        {
            var s = CreateSchemaEvolutionSetupV2();
            s.Tables[GetProductCategoryFullName()].Columns.Add("Mig2Column");
            return s;
        }

        /// <summary>
        /// Helper: server-side ALTER TABLE adding "Mig2Column" to ProductCategory.
        /// </summary>
        private async Task AddMig2ColumnOnServerAsync()
        {
            var salesSchema = this.serverProvider.UseFallbackSchema() ? "SalesLT" : null;
            var tableName = string.IsNullOrEmpty(salesSchema) ? "[ProductCategory]" : $"[{salesSchema}].[ProductCategory]";

            using var connection = this.serverProvider.CreateConnection();
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = $"ALTER TABLE {tableName} ADD [Mig2Column] nvarchar(50) NULL;";
            await command.ExecuteNonQueryAsync();
        }

        /// <summary>
        /// Helper: set "Mig2Column" of a row on the server.
        /// </summary>
        private async Task SetMig2ColumnOnServerAsync(string productCategoryId, string value)
        {
            var salesSchema = this.serverProvider.UseFallbackSchema() ? "SalesLT" : null;
            var tableName = string.IsNullOrEmpty(salesSchema) ? "[ProductCategory]" : $"[{salesSchema}].[ProductCategory]";

            using var connection = this.serverProvider.CreateConnection();
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = $"UPDATE {tableName} SET [Mig2Column] = @value WHERE [ProductCategoryId] = @id";

            var pValue = command.CreateParameter();
            pValue.ParameterName = "@value";
            pValue.Value = value;
            command.Parameters.Add(pValue);

            var pId = command.CreateParameter();
            pId.ParameterName = "@id";
            pId.Value = productCategoryId;
            command.Parameters.Add(pId);

            await command.ExecuteNonQueryAsync();
        }

        /// <summary>
        /// Helper: read "Mig2Column" on a client.
        /// </summary>
        private async Task<object> ReadMig2ColumnOnClientAsync(CoreProvider clientProvider, string productCategoryId)
        {
            using var connection = clientProvider.CreateConnection();
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT [Mig2Column] FROM [ProductCategory] WHERE [ProductCategoryId] = @id";

            var pId = command.CreateParameter();
            pId.ParameterName = "@id";
            pId.Value = productCategoryId;
            command.Parameters.Add(pId);

            return await command.ExecuteScalarAsync();
        }

        /// <summary>
        /// Helper: execute a raw SQL statement on a client.
        /// </summary>
        private async Task ExecuteOnClientAsync(CoreProvider clientProvider, string sql)
        {
            using var connection = clientProvider.CreateConnection();
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
        }

        /// <summary>
        /// Helper: setupV1 for ProductCategory without "Attribute With Space".
        /// </summary>
        private SyncSetup CreateSchemaEvolutionSetupV1()
        {
            var salesSchema = this.serverProvider.UseFallbackSchema() ? "SalesLT" : null;
            var salesSchemaWithDot = string.IsNullOrEmpty(salesSchema) ? string.Empty : $"{salesSchema}.";
            var tableName = $"{salesSchemaWithDot}ProductCategory";

            var s = new SyncSetup(tableName);
            s.Tables[tableName].Columns.AddRange("ProductCategoryId", "Name", "rowguid", "ModifiedDate");
            return s;
        }

        /// <summary>
        /// Helper: setupV2 for ProductCategory including "Attribute With Space".
        /// </summary>
        private SyncSetup CreateSchemaEvolutionSetupV2()
        {
            var salesSchema = this.serverProvider.UseFallbackSchema() ? "SalesLT" : null;
            var salesSchemaWithDot = string.IsNullOrEmpty(salesSchema) ? string.Empty : $"{salesSchema}.";
            var tableName = $"{salesSchemaWithDot}ProductCategory";

            var s = new SyncSetup(tableName);
            s.Tables[tableName].Columns.AddRange("ProductCategoryId", "Name", "rowguid", "ModifiedDate", "Attribute With Space");
            return s;
        }

        /// <summary>
        /// Helper: setupV1 for ProductCategory + Product without "Attribute With Space".
        /// </summary>
        private SyncSetup CreateSchemaEvolutionWithProductSetupV1()
        {
            var salesSchema = this.serverProvider.UseFallbackSchema() ? "SalesLT" : null;
            var salesSchemaWithDot = string.IsNullOrEmpty(salesSchema) ? string.Empty : $"{salesSchema}.";
            var pcTable = $"{salesSchemaWithDot}ProductCategory";
            var prodTable = $"{salesSchemaWithDot}Product";

            var s = new SyncSetup(pcTable, prodTable);
            s.Tables[pcTable].Columns.AddRange("ProductCategoryId", "Name", "rowguid", "ModifiedDate");
            s.Tables[prodTable].Columns.AddRange("ProductId", "Name", "ProductNumber", "ProductCategoryId", "rowguid", "ModifiedDate");
            return s;
        }

        /// <summary>
        /// Helper: setupV2 for ProductCategory (+ "Attribute With Space") + Product (unchanged).
        /// </summary>
        private SyncSetup CreateSchemaEvolutionWithProductSetupV2()
        {
            var salesSchema = this.serverProvider.UseFallbackSchema() ? "SalesLT" : null;
            var salesSchemaWithDot = string.IsNullOrEmpty(salesSchema) ? string.Empty : $"{salesSchema}.";
            var pcTable = $"{salesSchemaWithDot}ProductCategory";
            var prodTable = $"{salesSchemaWithDot}Product";

            var s = new SyncSetup(pcTable, prodTable);
            s.Tables[pcTable].Columns.AddRange("ProductCategoryId", "Name", "rowguid", "ModifiedDate", "Attribute With Space");
            s.Tables[prodTable].Columns.AddRange("ProductId", "Name", "ProductNumber", "ProductCategoryId", "rowguid", "ModifiedDate");
            return s;
        }

        /// <summary>
        /// Helper: full table name for ProductCategory (with schema if applicable).
        /// </summary>
        private string GetProductCategoryFullName()
        {
            var salesSchema = this.serverProvider.UseFallbackSchema() ? "SalesLT" : null;
            return string.IsNullOrEmpty(salesSchema) ? "ProductCategory" : $"{salesSchema}.ProductCategory";
        }

        /// <summary>
        /// Helper: full table name for Product (with schema if applicable).
        /// </summary>
        private string GetProductFullName()
        {
            var salesSchema = this.serverProvider.UseFallbackSchema() ? "SalesLT" : null;
            return string.IsNullOrEmpty(salesSchema) ? "Product" : $"{salesSchema}.Product";
        }

        /// <summary>
        /// Helper: insert a row on the server using raw SQL, setting "Attribute With Space".
        /// </summary>
        private async Task<string> InsertProductCategoryWithAttributeOnServerAsync(string attributeValue)
        {
            var name = HelperDatabase.GetRandomName();
            var id = name.ToUpperInvariant().Substring(0, 11);

            var salesSchema = this.serverProvider.UseFallbackSchema() ? "SalesLT" : null;
            var tableName = string.IsNullOrEmpty(salesSchema)
               ? "[ProductCategory]"
               : $"[{salesSchema}].[ProductCategory]";

            var connection = this.serverProvider.CreateConnection();
            connection.Open();
            var command = connection.CreateCommand();
            command.CommandText = $@"INSERT INTO {tableName}
            ([ProductCategoryId], [Name], [rowguid], [ModifiedDate], [Attribute With Space])
            VALUES (@id, @name, @rowguid, @modDate, @attr)";

            var pId = command.CreateParameter();
            pId.ParameterName = "@id";
            pId.Value = id;
            command.Parameters.Add(pId);

            var pName = command.CreateParameter();
            pName.ParameterName = "@name";
            pName.Value = name;
            command.Parameters.Add(pName);

            var pRowguid = command.CreateParameter();
            pRowguid.ParameterName = "@rowguid";
            pRowguid.Value = Guid.NewGuid();
            command.Parameters.Add(pRowguid);

            var pModDate = command.CreateParameter();
            pModDate.ParameterName = "@modDate";
            pModDate.Value = DateTime.UtcNow;
            command.Parameters.Add(pModDate);

            var pAttr = command.CreateParameter();
            pAttr.ParameterName = "@attr";
            pAttr.Value = attributeValue;
            command.Parameters.Add(pAttr);

            await command.ExecuteNonQueryAsync();
            connection.Close();

            return id;
        }

        /// <summary>
        /// Helper: read "Attribute With Space" for a given ProductCategoryId on the server.
        /// </summary>
        private async Task<object> ReadAttributeWithSpaceOnServerAsync(string productCategoryId)
        {
            var salesSchema = this.serverProvider.UseFallbackSchema() ? "SalesLT" : null;
            var tableName = string.IsNullOrEmpty(salesSchema)
               ? "[ProductCategory]"
               : $"[{salesSchema}].[ProductCategory]";

            var connection = this.serverProvider.CreateConnection();
            connection.Open();
            var command = connection.CreateCommand();
            command.CommandText = $"SELECT [Attribute With Space] FROM {tableName} WHERE [ProductCategoryId] = @id";

            var pId = command.CreateParameter();
            pId.ParameterName = "@id";
            pId.Value = productCategoryId;
            command.Parameters.Add(pId);

            var result = await command.ExecuteScalarAsync();
            connection.Close();

            return result;
        }

        /// <summary>
        /// Helper: read [Name] for a given ProductCategoryId on the server.
        /// </summary>
        private async Task<object> ReadNameOnServerAsync(string productCategoryId)
        {
            var salesSchema = this.serverProvider.UseFallbackSchema() ? "SalesLT" : null;
            var tableName = string.IsNullOrEmpty(salesSchema)
               ? "[ProductCategory]"
               : $"[{salesSchema}].[ProductCategory]";

            var connection = this.serverProvider.CreateConnection();
            connection.Open();
            var command = connection.CreateCommand();
            command.CommandText = $"SELECT [Name] FROM {tableName} WHERE [ProductCategoryId] = @id";

            var pId = command.CreateParameter();
            pId.ParameterName = "@id";
            pId.Value = productCategoryId;
            command.Parameters.Add(pId);

            var result = await command.ExecuteScalarAsync();
            connection.Close();

            return result;
        }

        /// <summary>
        /// Helper: insert a row on a client using raw SQL, without "Attribute With Space".
        /// </summary>
        private async Task<string> InsertProductCategoryOnClientRawAsync(CoreProvider clientProvider)
        {
            var name = HelperDatabase.GetRandomName();
            var id = name.ToUpperInvariant().Substring(0, 11);

            var connection = clientProvider.CreateConnection();
            connection.Open();
            var command = connection.CreateCommand();
            command.CommandText = $@"INSERT INTO [ProductCategory]
            ([ProductCategoryId], [Name], [rowguid], [ModifiedDate])
            VALUES (@id, @name, @rowguid, @modDate)";

            var pId = command.CreateParameter();
            pId.ParameterName = "@id";
            pId.Value = id;
            command.Parameters.Add(pId);

            var pName = command.CreateParameter();
            pName.ParameterName = "@name";
            pName.Value = name;
            command.Parameters.Add(pName);

            var pRowguid = command.CreateParameter();
            pRowguid.ParameterName = "@rowguid";
            pRowguid.Value = Guid.NewGuid().ToString();
            command.Parameters.Add(pRowguid);

            var pModDate = command.CreateParameter();
            pModDate.ParameterName = "@modDate";
            pModDate.Value = DateTime.UtcNow.ToString("o");
            command.Parameters.Add(pModDate);

            await command.ExecuteNonQueryAsync();
            connection.Close();

            return id;
        }

        /// <summary>
        /// Helper: update the Name of a row on the client using raw SQL.
        /// </summary>
        private async Task UpdateProductCategoryNameOnClientAsync(CoreProvider clientProvider, string productCategoryId, string newName)
        {
            var connection = clientProvider.CreateConnection();
            connection.Open();
            var command = connection.CreateCommand();
            command.CommandText = "UPDATE [ProductCategory] SET [Name] = @name WHERE [ProductCategoryId] = @id";

            var pName = command.CreateParameter();
            pName.ParameterName = "@name";
            pName.Value = newName;
            command.Parameters.Add(pName);

            var pId = command.CreateParameter();
            pId.ParameterName = "@id";
            pId.Value = productCategoryId;
            command.Parameters.Add(pId);

            await command.ExecuteNonQueryAsync();
            connection.Close();
        }

        /// <summary>
        /// Helper: ALTER TABLE on client to add "Attribute With Space" column (idempotent).
        /// EF's EnsureCreatedAsync may have already created the column, so we check first.
        /// </summary>
        private async Task AlterClientTableAddAttributeColumnAsync(CoreProvider clientProvider)
        {
            var (clientProviderType, _) = HelperDatabase.GetDatabaseType(clientProvider);

            // Check if column already exists
            var connection = clientProvider.CreateConnection();
            connection.Open();
            var checkCmd = connection.CreateCommand();
            checkCmd.Connection = connection;

            bool columnExists;
            if (clientProviderType == ProviderType.Sqlite)
            {
                checkCmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('ProductCategory') WHERE name = 'Attribute With Space'";
                columnExists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync()) > 0;
            }
            else if (clientProviderType == ProviderType.Sql)
            {
                var schema = clientProvider.UseFallbackSchema() ? "SalesLT" : "dbo";
                checkCmd.CommandText = $"SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = '{schema}' AND TABLE_NAME = 'ProductCategory' AND COLUMN_NAME = 'Attribute With Space'";
                columnExists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync()) > 0;
            }
            else
            {
                throw new NotImplementedException($"ALTER TABLE not implemented for {clientProviderType} in this test helper.");
            }

            if (!columnExists)
            {
                var alterCmd = connection.CreateCommand();
                alterCmd.Connection = connection;
                alterCmd.CommandText = clientProviderType switch
                {
                    ProviderType.Sql => $"ALTER TABLE {(clientProvider.UseFallbackSchema() ? "[SalesLT].[ProductCategory]" : "[ProductCategory]")} ADD [Attribute With Space] nvarchar(250) NULL;",
                    ProviderType.Sqlite => "ALTER TABLE ProductCategory ADD [Attribute With Space] text NULL;",
                    _ => throw new NotImplementedException(),
                };
                await alterCmd.ExecuteNonQueryAsync();
            }

            connection.Close();
        }

        /// <summary>
        /// Helper: insert a row on the client with "Attribute With Space" populated.
        /// </summary>
        private async Task<string> InsertProductCategoryWithAttributeOnClientAsync(CoreProvider clientProvider, string attributeValue)
        {
            var name = HelperDatabase.GetRandomName();
            var id = name.ToUpperInvariant().Substring(0, 11);

            var connection = clientProvider.CreateConnection();
            connection.Open();
            var command = connection.CreateCommand();
            command.CommandText = $@"INSERT INTO [ProductCategory]
            ([ProductCategoryId], [Name], [rowguid], [ModifiedDate], [Attribute With Space])
            VALUES (@id, @name, @rowguid, @modDate, @attr)";

            var pId = command.CreateParameter();
            pId.ParameterName = "@id";
            pId.Value = id;
            command.Parameters.Add(pId);

            var pName = command.CreateParameter();
            pName.ParameterName = "@name";
            pName.Value = name;
            command.Parameters.Add(pName);

            var pRowguid = command.CreateParameter();
            pRowguid.ParameterName = "@rowguid";
            pRowguid.Value = Guid.NewGuid().ToString();
            command.Parameters.Add(pRowguid);

            var pModDate = command.CreateParameter();
            pModDate.ParameterName = "@modDate";
            pModDate.Value = DateTime.UtcNow.ToString("o");
            command.Parameters.Add(pModDate);

            var pAttr = command.CreateParameter();
            pAttr.ParameterName = "@attr";
            pAttr.Value = attributeValue;
            command.Parameters.Add(pAttr);

            await command.ExecuteNonQueryAsync();
            connection.Close();

            return id;
        }

        /// <summary>
        /// Helper: read "Attribute With Space" on a client using raw SQL.
        /// </summary>
        private async Task<object> ReadAttributeWithSpaceOnClientAsync(CoreProvider clientProvider, string productCategoryId)
        {
            var connection = clientProvider.CreateConnection();
            connection.Open();
            var command = connection.CreateCommand();
            command.CommandText = "SELECT [Attribute With Space] FROM [ProductCategory] WHERE [ProductCategoryId] = @id";

            var pId = command.CreateParameter();
            pId.ParameterName = "@id";
            pId.Value = productCategoryId;
            command.Parameters.Add(pId);

            var result = await command.ExecuteScalarAsync();
            connection.Close();

            return result;
        }

    }
}
