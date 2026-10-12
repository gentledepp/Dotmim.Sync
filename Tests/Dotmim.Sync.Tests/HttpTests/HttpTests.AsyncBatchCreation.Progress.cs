using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Wormhole.Sync.Enumerations;
using Wormhole.Sync.Tests.Misc;
using Wormhole.Sync.Tests.Models;
using Wormhole.Sync.Web.Client;
using Wormhole.Sync.Web.Server;
using Xunit;

namespace Wormhole.Sync.Tests.IntegrationTests
{
    public abstract partial class HttpAsyncBatchTests
    {
        /// <summary>
        /// First sync with async batch creation. A very short server timeout forces InProgress responses,
        /// a longer one lets the client download batches while the server still creates them (MoreBatchesPending).
        /// In both cases the client adopts the server's context on every poll: the reported progress must never go down.
        /// </summary>
        [Theory]
        [InlineData(1)]
        [InlineData(60000)]
        public async Task Progress_FirstSync_AsyncBatchCreation_NeverDecreases(int asyncBatchTimeoutMs)
        {
            await this.Kestrel.StopAsync();

            await ProgressRecorder.AddLargeProductsAsync(serverProvider);

            // Small batches, to get many batch parts
            var options = new SyncOptions { BatchSize = 5, DisableConstraintsOnApplyChanges = true, ProgressLevel = SyncProgressLevel.Debug };

            var webServerOptions = new WebServerOptions
            {
                EnableAsyncBatchCreation = true,
                AsyncBatchTimeout = TimeSpan.FromMilliseconds(asyncBatchTimeoutMs),
                AsyncBatchPollingInterval = TimeSpan.FromMilliseconds(50),
                AsyncBatchWorkerCount = 1,
                SessionStorageMode = SessionCacheStorageMode.MemoryCache,
            };

            this.AddSyncServer(serverProvider, setup, options, webServerOptions);
            var asyncServiceUri = this.Kestrel.Run();

            var rowsCount = serverProvider.GetDatabaseRowsCount();
            var clientProvider = clientsProvider.First();

            var proxy = new WebRemoteOrchestrator(asyncServiceUri, maxDownladingDegreeOfParallelism: 4);

            var downloadedParts = 0;
            var polls = 0;
            proxy.OnHttpGettingChangesResponse(_ => Interlocked.Increment(ref downloadedParts));
            proxy.AddInterceptor<HttpBatchCreationInProgressArgs>(_ => Interlocked.Increment(ref polls));

            var recorder = new ProgressRecorder();
            var agent = new SyncAgent(clientProvider, proxy, options);
            var s = await agent.SynchronizeAsync(recorder);

            this.Output.WriteLine($"Rows: {s.TotalChangesDownloadedFromServer}. Batch parts downloaded: {downloadedParts}. Server polls: {polls}");

            Assert.Equal(rowsCount, s.TotalChangesDownloadedFromServer);
            Assert.True(downloadedParts > 4, $"Need more batch parts than workers to test parallel downloads, got {downloadedParts}");

            recorder.AssertNeverDecreases(this.Output);
        }
    }
}
