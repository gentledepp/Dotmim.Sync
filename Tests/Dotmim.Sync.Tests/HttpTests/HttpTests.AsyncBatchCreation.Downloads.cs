using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Wormhole.Sync.Tests.Misc;
using Wormhole.Sync.Tests.Models;
using Wormhole.Sync.Web.Azure;
using Wormhole.Sync.Web.Client;
using Wormhole.Sync.Web.Server;
using Xunit;

namespace Wormhole.Sync.Tests.IntegrationTests
{
    public abstract partial class HttpAsyncBatchTests
    {
        /// <summary>
        /// First sync with progressive batching (MoreBatchesPending): the client downloads the parts while the server
        /// still creates them. No part of a round is flagged as the last one, but each part must still be downloaded once.
        /// </summary>
        [Fact]
        public async Task FirstSync_ProgressiveBatching_DownloadsEachBatchPartOnce()
        {
            await this.Kestrel.StopAsync();

            await ProgressRecorder.AddLargeProductsAsync(serverProvider);

            // Small batches, to get many batch parts
            var options = new SyncOptions { BatchSize = 5, DisableConstraintsOnApplyChanges = true };

            this.AddSyncServer(serverProvider, setup, options, CreateAsyncWebServerOptions());
            var asyncServiceUri = this.Kestrel.Run();

            var rowsCount = serverProvider.GetDatabaseRowsCount();
            var clientProvider = clientsProvider.First();

            var proxy = new WebRemoteOrchestrator(asyncServiceUri, maxDownladingDegreeOfParallelism: 4);

            var downloadedIndexes = new ConcurrentQueue<int>();
            var polls = 0;
            proxy.OnHttpGettingChangesResponse(args => downloadedIndexes.Enqueue(args.BatchIndex));
            proxy.AddInterceptor<HttpBatchCreationInProgressArgs>(_ => Interlocked.Increment(ref polls));

            var agent = new SyncAgent(clientProvider, proxy, options);
            var s = await agent.SynchronizeAsync();

            var indexes = downloadedIndexes.ToList();
            var duplicates = indexes.GroupBy(i => i).Where(g => g.Count() > 1).Select(g => $"{g.Key} (x{g.Count()})").ToList();

            this.Output.WriteLine($"Rows: {s.TotalChangesDownloadedFromServer}/{rowsCount}. Server polls: {polls}");
            this.Output.WriteLine($"Downloaded parts, in order: {string.Join(", ", indexes)}");
            this.Output.WriteLine($"Downloaded more than once: {(duplicates.Count == 0 ? "none" : string.Join(", ", duplicates))}");

            Assert.Equal(rowsCount, s.TotalChangesDownloadedFromServer);
            Assert.True(polls > 0, "The server must still create batches while the client downloads them");
            Assert.Empty(duplicates);
        }

        /// <summary>
        /// First sync with async batch creation from a client that does not use unified batching (e.g. an older client).
        /// Its batch parts are re-indexed once all tables are read, so they can't be streamed progressively:
        /// the client gets them all once the server is done.
        /// </summary>
        [Fact]
        public async Task FirstSync_AsyncBatchCreation_WithoutUnifiedBatching_DownloadsAllRows()
        {
            // A first sync with traditional (non-unified) batching currently fails with AzureBlobBatchStorage, with or without async batch creation
            Assert.SkipWhen(this.batchStorage is AzureBlobBatchStorage, "First sync with traditional batching fails with AzureBlobBatchStorage (known issue)");

            await this.Kestrel.StopAsync();

            await ProgressRecorder.AddLargeProductsAsync(serverProvider);

            // Small batches, to get many batch parts
            var options = new SyncOptions { BatchSize = 5, DisableConstraintsOnApplyChanges = true, UseUnifiedBatching = false };

            this.AddSyncServer(serverProvider, setup, options, CreateAsyncWebServerOptions());
            var asyncServiceUri = this.Kestrel.Run();

            var rowsCount = serverProvider.GetDatabaseRowsCount();
            var clientProvider = clientsProvider.First();

            var proxy = new WebRemoteOrchestrator(asyncServiceUri, maxDownladingDegreeOfParallelism: 4);

            var downloadedIndexes = new ConcurrentQueue<int>();
            proxy.OnHttpGettingChangesResponse(args => downloadedIndexes.Enqueue(args.BatchIndex));

            var agent = new SyncAgent(clientProvider, proxy, options);
            var s = await agent.SynchronizeAsync();

            var indexes = downloadedIndexes.ToList();

            this.Output.WriteLine($"Rows: {s.TotalChangesDownloadedFromServer}/{rowsCount}. Applied: {s.TotalChangesAppliedOnClient}");
            this.Output.WriteLine($"Downloaded parts, in order: {string.Join(", ", indexes)}");

            Assert.Equal(rowsCount, s.TotalChangesDownloadedFromServer);
            Assert.Equal(rowsCount, s.TotalChangesAppliedOnClient);
            Assert.True(indexes.Count > 1, $"Need several batch parts, got {indexes.Count}");
            Assert.Equal(indexes.Count, indexes.Distinct().Count());
        }

        private static WebServerOptions CreateAsyncWebServerOptions() => new()
        {
            EnableAsyncBatchCreation = true,
            AsyncBatchTimeout = TimeSpan.FromMilliseconds(60000),
            AsyncBatchPollingInterval = TimeSpan.FromMilliseconds(50),
            AsyncBatchWorkerCount = 1,
            SessionStorageMode = SessionCacheStorageMode.MemoryCache,
        };
    }
}
