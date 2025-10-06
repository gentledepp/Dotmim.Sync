using Wormhole.Sync.Builders;
using Wormhole.Sync.Enumerations;
using Wormhole.Sync.SqlServer;
using Wormhole.Sync.Tests.Core;
using Wormhole.Sync.Tests.Models;
using Microsoft.Data.SqlClient;
using System;
using System.Threading.Tasks;
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
    }
}
