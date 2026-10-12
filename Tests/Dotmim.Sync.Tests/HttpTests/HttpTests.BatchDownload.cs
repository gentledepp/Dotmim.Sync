using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Wormhole.Sync.Tests.Misc;
using Wormhole.Sync.Tests.Models;
using Wormhole.Sync.Web.Client;
using Xunit;

namespace Wormhole.Sync.Tests.IntegrationTests
{
    public abstract partial class HttpTests
    {
        /// <summary>
        /// First sync over HTTP with many batch parts: each part is downloaded exactly once, and the row counts
        /// announced by the server for each part add up to the rows downloaded.
        /// The response interceptor makes the client deserialize each part in memory before writing it to disk.
        /// </summary>
        [Fact]
        public async Task FirstSync_ManyBatchParts_DownloadsEachPartOnceWithCorrectRowCounts()
        {
            // Small batches, to get many batch parts. Several tables share a unified batch part.
            var options = new SyncOptions { BatchSize = 5, DisableConstraintsOnApplyChanges = true };

            await ProgressRecorder.AddLargeProductsAsync(serverProvider);

            await this.Kestrel.StopAsync();
            this.AddSyncServer(serverProvider, setup, options);
            var serviceUri = this.Kestrel.Run();

            var rowsCount = serverProvider.GetDatabaseRowsCount();

            foreach (var clientProvider in clientsProvider)
            {
                var proxy = new WebRemoteOrchestrator(serviceUri, maxDownladingDegreeOfParallelism: 4);

                var responses = 0;
                proxy.OnHttpGettingResponse(_ => Interlocked.Increment(ref responses));

                var downloads = new ConcurrentBag<(int Index, int RowsCount)>();
                proxy.OnHttpGettingChangesResponse(args => downloads.Add((args.BatchIndex, args.BatchRowsCount)));

                var agent = new SyncAgent(clientProvider, proxy, options);
                var s = await agent.SynchronizeAsync();

                var parts = downloads.OrderBy(d => d.Index).ToList();
                foreach (var (index, partRows) in parts)
                    this.Output.WriteLine($"Batch part {index}: {partRows} rows");

                this.Output.WriteLine($"Rows: {s.TotalChangesDownloadedFromServer}/{rowsCount}. Parts downloaded: {parts.Count}. Distinct: {parts.Select(p => p.Index).Distinct().Count()}. Sum of part row counts: {parts.Sum(p => p.RowsCount)}. Responses: {responses}");

                Assert.Equal(rowsCount, s.TotalChangesDownloadedFromServer);
                Assert.True(parts.Count > 1, $"Need several batch parts, got {parts.Count}");
                Assert.Equal(parts.Count, parts.Select(p => p.Index).Distinct().Count());
                Assert.Equal(rowsCount, parts.Sum(p => p.RowsCount));
            }
        }
    }
}
