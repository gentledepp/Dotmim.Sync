using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using Wormhole.Sync.Tests.Misc;
using Wormhole.Sync.Web.Client;
using Xunit;

namespace Wormhole.Sync.Tests.IntegrationTests
{
    public abstract partial class HttpTests
    {
        /// <summary>
        /// When tables are re-initialized on a client with database-level constraints (SQLite), the client opens a connection
        /// to run "PRAGMA foreign_keys = OFF" before the apply. That connection is closed and disposed once the apply is done.
        /// </summary>
        [Fact]
        public virtual async Task ReinitTables_OnSqliteClient_DisposesTheConnectionUsedToDisableConstraints()
        {
            var options = new SyncOptions { DisableConstraintsOnApplyChanges = false };
            var setupV1 = CreateSchemaEvolutionSetupV1();
            var setupV2 = CreateSchemaEvolutionSetupV2();

            await this.Kestrel.StopAsync();
            this.AddSyncServer(this.serverProvider, setupV1, options);
            var uri = this.Kestrel.Run();

            var clientProvider = this.clientsProvider.First();
            await new SyncAgent(clientProvider, new WebRemoteOrchestrator(uri), options).SynchronizeAsync(setupV1);

            // Server migration, then the client takes it: the migrated table is re-initialized on the client
            await new RemoteOrchestrator(this.serverProvider).MigrateSchemaAsync("mig1", setupV2);
            await InsertProductCategoryWithAttributeOnServerAsync("TestValue");
            this.Kestrel.UpdateSyncSetup(setupV2);

            await AlterClientTableAddAttributeColumnAsync(clientProvider);
            HelperDatabase.ClearAllPools();

            var agent = new SyncAgent(clientProvider, new WebRemoteOrchestrator(uri), options);
            agent.SupportedMigrations = new List<string> { "mig1" };

            HashSet<string> reinitTables = null;
            DbConnection applyConnection = null;
            var disposed = false;
            agent.LocalOrchestrator.OnDatabaseChangesApplying(args =>
            {
                reinitTables = args.ApplyChanges.ReinitTables;
                applyConnection = args.Connection;
                applyConnection.Disposed += (_, _) => disposed = true;
            });

            await agent.SynchronizeAsync(setupV2);

            this.Output.WriteLine($"Reinit tables: [{string.Join(", ", reinitTables ?? new HashSet<string>())}]. Apply connection {applyConnection?.GetType().Name}: state={applyConnection?.State}, disposed={disposed}");

            Assert.NotNull(reinitTables);
            Assert.NotEmpty(reinitTables);
            Assert.NotNull(applyConnection);
            Assert.Equal(ConnectionState.Closed, applyConnection.State);
            Assert.True(disposed, "The connection opened to disable the database-level constraints should be disposed");
        }
    }
}
