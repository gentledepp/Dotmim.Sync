using Wormhole.Sync.Enumerations;
using Wormhole.Sync.Tests.Misc;
using Wormhole.Sync.Tests.Models;
using Wormhole.Sync.Web.Client;
using Wormhole.Sync.Web.Server;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using Microsoft.Extensions.DependencyInjection;
#if !NET48
using Microsoft.AspNetCore.Http;
#endif
using Wormhole.Sync.Tests.Fixtures;

namespace Wormhole.Sync.Tests.IntegrationTests
{
    /// <summary>
    /// HTTP tests for async batch creation functionality.
    /// These tests verify the async batch creation feature works correctly
    /// for initial syncs when EnableAsyncBatchCreation is enabled.
    /// </summary>
    public abstract partial class HttpTests
    {
        /// <summary>
        /// Test that async batch creation works for initial sync.
        /// When EnableAsyncBatchCreation is enabled, the server should process
        /// batch creation in the background and the client should wait/poll for completion.
        /// </summary>
        [Theory]
        [ClassData(typeof(SyncOptionsData))]
        public virtual async Task AsyncBatchCreation_InitialSync_ShouldComplete(SyncOptions options)
        {
            // Stop the existing server
            await this.Kestrel.StopAsync();

            // Create web server options with async batch creation enabled
            var webServerOptions = new WebServerOptions
            {
                EnableAsyncBatchCreation = true,
                AsyncBatchTimeout = TimeSpan.FromSeconds(60),
                AsyncBatchPollingInterval = TimeSpan.FromMilliseconds(200),
                AsyncBatchWorkerCount = 2,
            };

            // Add sync server with async batch creation enabled
            this.Kestrel.AddSyncServer(serverProvider, setup, options, webServerOptions);
            var asyncServiceUri = this.Kestrel.Run();

            // Get expected row count
            var rowsCount = serverProvider.GetDatabaseRowsCount();

            // Execute initial sync on all clients
            foreach (var clientProvider in clientsProvider)
            {
                var agent = new SyncAgent(clientProvider, new WebRemoteOrchestrator(asyncServiceUri), options);

                var result = await agent.SynchronizeAsync();

                Assert.Equal(rowsCount, result.TotalChangesDownloadedFromServer);
                Assert.Equal(0, result.TotalChangesUploadedToServer);
            }
        }

        /// <summary>
        /// Test that async batch creation processes InProgress responses correctly.
        /// </summary>
        [Theory]
        [ClassData(typeof(SyncOptionsData))]
        public virtual async Task AsyncBatchCreation_WithShortTimeout_ShouldHandleInProgressResponse(SyncOptions options)
        {
            // Stop the existing server
            await this.Kestrel.StopAsync();

            // Create web server options with very short timeout to force InProgress response
            var webServerOptions = new WebServerOptions
            {
                EnableAsyncBatchCreation = true,
                AsyncBatchTimeout = TimeSpan.FromMilliseconds(1), // Very short timeout
                AsyncBatchPollingInterval = TimeSpan.FromMilliseconds(100),
                AsyncBatchWorkerCount = 1,
            };

            // Add sync server with async batch creation enabled
            this.Kestrel.AddSyncServer(serverProvider, setup, options, webServerOptions);
            var asyncServiceUri = this.Kestrel.Run();

            // Get expected row count
            var rowsCount = serverProvider.GetDatabaseRowsCount();

            // Execute initial sync - should eventually complete despite InProgress responses
            foreach (var clientProvider in clientsProvider)
            {
                var webOrchestrator = new WebRemoteOrchestrator(asyncServiceUri);
                var agent = new SyncAgent(clientProvider, webOrchestrator, options);

                var result = await agent.SynchronizeAsync();

                Assert.Equal(rowsCount, result.TotalChangesDownloadedFromServer);
            }
        }

        /// <summary>
        /// Test that async batch creation is idempotent when client retries.
        /// </summary>
        [Theory]
        [ClassData(typeof(SyncOptionsData))]
        public virtual async Task AsyncBatchCreation_ClientRetry_ShouldBeIdempotent(SyncOptions options)
        {
            // Stop the existing server
            await this.Kestrel.StopAsync();

            var webServerOptions = new WebServerOptions
            {
                EnableAsyncBatchCreation = true,
                AsyncBatchTimeout = TimeSpan.FromSeconds(30),
                AsyncBatchPollingInterval = TimeSpan.FromMilliseconds(200),
            };

            this.Kestrel.AddSyncServer(serverProvider, setup, options, webServerOptions);
            var asyncServiceUri = this.Kestrel.Run();

            var rowsCount = serverProvider.GetDatabaseRowsCount();

            // Execute multiple syncs with same client - job should be reused
            foreach (var clientProvider in clientsProvider)
            {
                var agent = new SyncAgent(clientProvider, new WebRemoteOrchestrator(asyncServiceUri), options);

                // First sync
                var result1 = await agent.SynchronizeAsync();
                Assert.Equal(rowsCount, result1.TotalChangesDownloadedFromServer);

                // Second sync (should be incremental, not use async batch creation)
                var result2 = await agent.SynchronizeAsync();
                Assert.Equal(0, result2.TotalChangesDownloadedFromServer);
            }
        }

        /// <summary>
        /// Test that async batch creation works with reinitialize sync type.
        /// </summary>
        [Theory]
        [ClassData(typeof(SyncOptionsData))]
        public virtual async Task AsyncBatchCreation_Reinitialize_ShouldUseAsyncProcessing(SyncOptions options)
        {
            // Stop the existing server
            await this.Kestrel.StopAsync();

            var webServerOptions = new WebServerOptions
            {
                EnableAsyncBatchCreation = true,
                AsyncBatchTimeout = TimeSpan.FromSeconds(60),
                AsyncBatchPollingInterval = TimeSpan.FromMilliseconds(200),
            };

            this.Kestrel.AddSyncServer(serverProvider, setup, options, webServerOptions);
            var asyncServiceUri = this.Kestrel.Run();

            var rowsCount = serverProvider.GetDatabaseRowsCount();

            foreach (var clientProvider in clientsProvider)
            {
                var agent = new SyncAgent(clientProvider, new WebRemoteOrchestrator(asyncServiceUri), options);

                // First sync (initial)
                var result1 = await agent.SynchronizeAsync();
                Assert.Equal(rowsCount, result1.TotalChangesDownloadedFromServer);

                // Reinitialize sync (should also use async batch creation)
                var result2 = await agent.SynchronizeAsync(SyncType.Reinitialize);
                Assert.Equal(rowsCount, result2.TotalChangesDownloadedFromServer);
            }
        }

        /// <summary>
        /// Test that client changes are still applied when async batch creation is enabled.
        /// </summary>
        [Theory]
        [ClassData(typeof(SyncOptionsData))]
        public virtual async Task AsyncBatchCreation_WithClientChanges_ShouldApplyToServer(SyncOptions options)
        {
            // Stop the existing server
            await this.Kestrel.StopAsync();

            var webServerOptions = new WebServerOptions
            {
                EnableAsyncBatchCreation = true,
                AsyncBatchTimeout = TimeSpan.FromSeconds(60),
                AsyncBatchPollingInterval = TimeSpan.FromMilliseconds(200),
            };

            this.Kestrel.AddSyncServer(serverProvider, setup, options, webServerOptions);
            var asyncServiceUri = this.Kestrel.Run();

            // Convert to list to ensure stable references
            var clients = clientsProvider.ToList();

            // First, do initial sync
            foreach (var clientProvider in clients)
            {
                var agent = new SyncAgent(clientProvider, new WebRemoteOrchestrator(asyncServiceUri), options);
                await agent.SynchronizeAsync();
            }

            // Add data on first client
            var clientWithChanges = clients[0];
            await clientWithChanges.AddProductCategoryAsync();

            // Sync again - client changes should be uploaded
            {
                var agent = new SyncAgent(clientWithChanges, new WebRemoteOrchestrator(asyncServiceUri), options);
                var result = await agent.SynchronizeAsync();

                Assert.Equal(1, result.TotalChangesUploadedToServer);
                Assert.Equal(1, result.TotalChangesAppliedOnServer);
            }

            // Other clients should receive the change (skip if only one client)
            for (var i = 1; i < clients.Count; i++)
            {
                var clientProvider = clients[i];
                var agent = new SyncAgent(clientProvider, new WebRemoteOrchestrator(asyncServiceUri), options);
                var result = await agent.SynchronizeAsync();

                Assert.Equal(1, result.TotalChangesDownloadedFromServer);
            }
        }

        /// <summary>
        /// Test that async batch creation is disabled by default.
        /// </summary>
        [Theory]
        [ClassData(typeof(SyncOptionsData))]
        public virtual async Task AsyncBatchCreation_DisabledByDefault_ShouldUseSyncProcessing(SyncOptions options)
        {
            // Default setup (EnableAsyncBatchCreation = false)
            var rowsCount = serverProvider.GetDatabaseRowsCount();

            foreach (var clientProvider in clientsProvider)
            {
                var agent = new SyncAgent(clientProvider, new WebRemoteOrchestrator(serviceUri), options);

                var result = await agent.SynchronizeAsync();

                Assert.Equal(rowsCount, result.TotalChangesDownloadedFromServer);
            }
        }
    }
}
