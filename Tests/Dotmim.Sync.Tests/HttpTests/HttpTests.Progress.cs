using System.Threading;
using System.Threading.Tasks;
using Wormhole.Sync.Enumerations;
using Wormhole.Sync.Tests.Misc;
using Wormhole.Sync.Tests.Models;
using Wormhole.Sync.Web.Client;
using Xunit;

namespace Wormhole.Sync.Tests.IntegrationTests
{
    public abstract partial class HttpTests
    {
        /// <summary>
        /// First sync over HTTP with many batch parts downloaded by 4 parallel workers:
        /// the reported progress must never go down.
        /// </summary>
        [Fact]
        public async Task Progress_FirstSync_ParallelBatchDownloads_NeverDecreases()
        {
            // Small batches, to get many batch parts
            var options = new SyncOptions { BatchSize = 5, DisableConstraintsOnApplyChanges = true, ProgressLevel = SyncProgressLevel.Debug };

            await ProgressRecorder.AddLargeProductsAsync(serverProvider);

            await this.Kestrel.StopAsync();
            this.AddSyncServer(serverProvider, setup, options);
            var serviceUri = this.Kestrel.Run();

            var rowsCount = serverProvider.GetDatabaseRowsCount();

            foreach (var clientProvider in clientsProvider)
            {
                var proxy = new WebRemoteOrchestrator(serviceUri, maxDownladingDegreeOfParallelism: 4);

                var downloadedParts = 0;
                proxy.OnHttpGettingChangesResponse(_ => Interlocked.Increment(ref downloadedParts));

                var recorder = new ProgressRecorder();
                var agent = new SyncAgent(clientProvider, proxy, options);
                var s = await agent.SynchronizeAsync(recorder);

                this.Output.WriteLine($"Rows: {s.TotalChangesDownloadedFromServer}. Batch parts downloaded: {downloadedParts}");

                Assert.Equal(rowsCount, s.TotalChangesDownloadedFromServer);
                Assert.True(downloadedParts > 4, $"Need more batch parts than workers to test parallel downloads, got {downloadedParts}");

                recorder.AssertNeverDecreases(this.Output);
            }
        }
    }
}
