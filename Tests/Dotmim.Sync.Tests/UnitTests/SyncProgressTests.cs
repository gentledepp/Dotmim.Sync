using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Wormhole.Sync.Sqlite;
using Wormhole.Sync.Tests.Misc;
using Wormhole.Sync.Web.Client;
using Xunit;

namespace Wormhole.Sync.Tests.UnitTests
{
    /// <summary>
    /// Tests that the overall sync progress (SyncContext / ProgressArgs.ProgressPercentage) never goes down.
    /// </summary>
    public class SyncProgressTests
    {
        public SyncProgressTests(ITestOutputHelper output) => this.Output = output;

        public ITestOutputHelper Output { get; }

        [Theory]
        [InlineData(-0.5, 0d)]
        [InlineData(0.42, 0.42)]
        [InlineData(1.65, 1d)]
        [InlineData(double.PositiveInfinity, 1d)]
        public void SyncContext_ProgressPercentage_IsClamped(double value, double expected)
        {
            var context = new SyncContext(Guid.NewGuid(), "scope") { ProgressPercentage = value };

            this.Output.WriteLine($"set {value} -> {context.ProgressPercentage}");
            Assert.Equal(expected, context.ProgressPercentage);
        }

        [Fact]
        public void SyncContext_ProgressPercentage_IgnoresNaN()
        {
            var context = new SyncContext(Guid.NewGuid(), "scope") { ProgressPercentage = 0.6 };
            context.ProgressPercentage = double.NaN;

            this.Output.WriteLine($"after NaN: {context.ProgressPercentage}");
            Assert.Equal(0.6, context.ProgressPercentage);
        }

        [Fact]
        public void AdoptServerContext_KeepsClientProgress()
        {
            var clientContext = new SyncContext(Guid.NewGuid(), "scope") { ProgressPercentage = 0.62 };
            var serverContext = new SyncContext(clientContext.SessionId, "scope") { ProgressPercentage = 0.75 };

            var adopted = WebRemoteOrchestrator.AdoptServerContext(clientContext, serverContext);

            this.Output.WriteLine($"client 0.62, server 0.75 -> adopted {adopted.ProgressPercentage}");
            Assert.Same(serverContext, adopted);
            Assert.Equal(0.62, adopted.ProgressPercentage);

            Assert.Same(clientContext, WebRemoteOrchestrator.AdoptServerContext(clientContext, null));
        }

        [Fact]
        public async Task BatchDownloadProgress_ParallelWorkers_NeverGoesDown()
        {
            const int parts = 200;
            var context = new SyncContext(Guid.NewGuid(), "scope") { ProgressPercentage = 0.55 };
            var downloadProgress = new WebRemoteOrchestrator.BatchDownloadProgress(parts);
            var observed = new List<double>();

            // 4 workers, like the default MaxDownladingDegreeOfParallelism
            await Enumerable.Range(0, parts).ForEachAsync(
                async _ =>
                {
                    await Task.Yield();
                    lock (observed)
                    {
                        downloadProgress.PartDownloaded(context);
                        observed.Add(context.ProgressPercentage);
                    }
                }, 4);

            this.Output.WriteLine($"first {observed.First():P1}, last {observed.Last():P1}, count {observed.Count}");
            for (var i = 1; i < observed.Count; i++)
                Assert.True(observed[i] >= observed[i - 1], $"#{i}: {observed[i - 1]} -> {observed[i]}");

            Assert.Equal(0.75, context.ProgressPercentage, 6);
        }

        [Fact]
        public void BatchDownloadProgress_ProgressiveRounds_StaysInDownloadBand()
        {
            var context = new SyncContext(Guid.NewGuid(), "scope") { ProgressPercentage = 0.55 };

            // First round: 3 parts known
            var downloadProgress = new WebRemoteOrchestrator.BatchDownloadProgress(3);
            var observed = new List<double>();

            for (var i = 0; i < 3; i++)
            {
                downloadProgress.PartDownloaded(context);
                observed.Add(context.ProgressPercentage);
            }

            // Following rounds: 2 new parts each time (global indexes 3..12). The old code computed
            // 0.55 + (index + 1) * 0.2 / 2, e.g. 1.65 for index 10.
            for (var round = 0; round < 5; round++)
            {
                downloadProgress.TotalParts += 2;
                for (var i = 0; i < 2; i++)
                {
                    downloadProgress.PartDownloaded(context);
                    observed.Add(context.ProgressPercentage);
                }
            }

            this.Output.WriteLine(string.Join(" ", observed.Select(o => o.ToString("P1"))));

            for (var i = 1; i < observed.Count; i++)
                Assert.True(observed[i] >= observed[i - 1], $"#{i}: {observed[i - 1]} -> {observed[i]}");

            Assert.All(observed, o => Assert.InRange(o, 0.55, 0.75 + 1e-9));
            Assert.Equal(0.75, observed.Last(), 6);
        }

        [Theory]
        [InlineData(10, false, null, 10)]
        [InlineData(10, false, 40, 10)]
        [InlineData(10, true, 25, 40)]
        [InlineData(10, true, 99, 11)]
        [InlineData(10, true, 0, 20)]
        [InlineData(10, true, null, 20)]
        public void BatchDownloadProgress_SetKnownParts_EstimatesTotalWhileServerCreatesBatches(int knownParts, bool moreBatchesPending, int? asyncProgress, int expectedTotal)
        {
            var downloadProgress = new WebRemoteOrchestrator.BatchDownloadProgress(0);
            downloadProgress.SetKnownParts(knownParts, moreBatchesPending, asyncProgress);

            this.Output.WriteLine($"known {knownParts}, pending {moreBatchesPending}, server {asyncProgress}% -> total {downloadProgress.TotalParts}");
            Assert.Equal(expectedTotal, downloadProgress.TotalParts);
        }

        [Fact]
        public async Task ReportProgress_ParallelContextCopies_ReportedValuesNeverGoDown()
        {
            var orchestrator = new LocalOrchestrator(new SqliteSyncProvider("progress_unused.db"));
            var recorder = new ProgressRecorder();
            var sessionId = Guid.NewGuid();
            var shared = new SyncContext(sessionId, "scope") { ProgressPercentage = 0.55 };
            var gate = new object();

            // Each worker reports with its own copy of the context, taken before the shared value moves on.
            // This is what parallel batch downloads do with the server's echo of the context.
            await Enumerable.Range(0, 100).ForEachAsync(
                async i =>
                {
                    SyncContext copy;
                    lock (gate)
                    {
                        copy = new SyncContext(sessionId, "scope") { ProgressPercentage = shared.ProgressPercentage };
                        shared.ProgressPercentage += 0.002;
                    }

                    await Task.Yield();
                    orchestrator.ReportProgress(copy, recorder, new ProgressArgs(copy, null));

                    // Change the context after the report: the reported args must not follow
                    copy.ProgressPercentage = 0;
                }, 4);

            recorder.AssertNeverDecreases(this.Output);
        }

        [Fact]
        public void ReportProgress_NewSession_StartsAgainFromItsOwnValue()
        {
            var orchestrator = new LocalOrchestrator(new SqliteSyncProvider("progress_unused.db"));
            var recorder = new ProgressRecorder();

            var first = new SyncContext(Guid.NewGuid(), "scope") { ProgressPercentage = 1 };
            orchestrator.ReportProgress(first, recorder, new ProgressArgs(first, null));

            var second = new SyncContext(Guid.NewGuid(), "scope") { ProgressPercentage = 0.1 };
            orchestrator.ReportProgress(second, recorder, new ProgressArgs(second, null));

            var reports = recorder.Reports;
            this.Output.WriteLine($"first session {reports[0].ReadAtReport}, second session {reports[1].ReadAtReport}");
            Assert.Equal(1, reports[0].ReadAtReport);
            Assert.Equal(0.1, reports[1].ReadAtReport);
        }
    }
}
