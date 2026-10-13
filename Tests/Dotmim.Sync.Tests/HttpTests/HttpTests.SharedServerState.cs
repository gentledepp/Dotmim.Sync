using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Wormhole.Sync.Enumerations;
using Wormhole.Sync.Serialization;
using Wormhole.Sync.Tests.Core;
using Wormhole.Sync.Tests.Misc;
using Wormhole.Sync.Tests.Models;
using Wormhole.Sync.Web.Client;
using Wormhole.Sync.Web.Server;
using Xunit;

namespace Wormhole.Sync.Tests.IntegrationTests
{
    public abstract partial class HttpTests
    {
        private static readonly TimeSpan InterleaveTimeout = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Two clients with different batch sizes sync at the same time. The server agents share one <see cref="SyncOptions"/> instance.
        /// Client A is held on the server right before its batch parts are created, until client B has started creating its own,
        /// so B's batch size would leak into A's batch parts if the client batch size were stored in the shared options.
        /// </summary>
        [Fact]
        public async Task ConcurrentClients_EachGetBatchPartsOfTheirOwnBatchSize()
        {
            const int smallBatchSize = 100;
            const int largeBatchSize = 100000;

            var serverOptions = new SyncOptions { BatchSize = 2000, DisableConstraintsOnApplyChanges = true };

            await ProgressRecorder.AddLargeProductsAsync(serverProvider);
            var rowsCount = serverProvider.GetDatabaseRowsCount();

            var smallHeld = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var largeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var held = 0;

            await this.Kestrel.StopAsync();
            this.AddSyncServer(serverProvider, setup, serverOptions);
            var serviceUri = this.Kestrel.Run(async context =>
            {
                // A new agent per request, as registered by AddSyncServer: they only share the options
                var webServerAgent = context.RequestServices.GetService<WebServerAgent>();

                webServerAgent.RemoteOrchestrator.OnDatabaseChangesSelecting(async args =>
                {
                    this.Output.WriteLine($"Server selecting changes with batch size {args.BatchSize}");

                    if (args.BatchSize == largeBatchSize)
                    {
                        largeStarted.TrySetResult();
                    }
                    else if (args.BatchSize == smallBatchSize && Interlocked.Exchange(ref held, 1) == 0)
                    {
                        smallHeld.TrySetResult();
                        try
                        {
                            await largeStarted.Task.WaitAsync(InterleaveTimeout);
                        }
                        catch (TimeoutException)
                        {
                            this.Output.WriteLine("Client B never started selecting changes");
                        }
                    }
                });

                await webServerAgent.HandleRequestAsync(context);
            });

            var clientBName = HelperDatabase.GetRandomName("http_sqlite_b_");
            var clientB = HelperDatabase.GetSyncProvider(ProviderType.Sqlite, clientBName, false);
            try
            {
                var partsA = new ConcurrentDictionary<int, int>();
                var proxyA = new WebRemoteOrchestrator(serviceUri);
                proxyA.OnHttpGettingChangesResponse(args => partsA[args.BatchIndex] = args.BatchRowsCount);
                var agentA = new SyncAgent(clientsProvider.First(), proxyA, new SyncOptions { BatchSize = smallBatchSize, DisableConstraintsOnApplyChanges = true });

                var partsB = new ConcurrentDictionary<int, int>();
                var proxyB = new WebRemoteOrchestrator(serviceUri);
                proxyB.OnHttpGettingChangesResponse(args => partsB[args.BatchIndex] = args.BatchRowsCount);
                var agentB = new SyncAgent(clientB, proxyB, new SyncOptions { BatchSize = largeBatchSize, DisableConstraintsOnApplyChanges = true });

                var syncA = agentA.SynchronizeAsync();
                await smallHeld.Task.WaitAsync(InterleaveTimeout);
                var syncB = agentB.SynchronizeAsync();

                var results = await Task.WhenAll(syncA, syncB);

                this.Output.WriteLine($"Client A (batch size {smallBatchSize}): {results[0].TotalChangesDownloadedFromServer}/{rowsCount} rows in {partsA.Count} parts");
                this.Output.WriteLine($"Client B (batch size {largeBatchSize}): {results[1].TotalChangesDownloadedFromServer}/{rowsCount} rows in {partsB.Count} parts");
                this.Output.WriteLine($"Server options batch size after the syncs: {serverOptions.BatchSize}");

                Assert.Equal(rowsCount, results[0].TotalChangesDownloadedFromServer);
                Assert.Equal(rowsCount, results[1].TotalChangesDownloadedFromServer);
                Assert.True(partsA.Count > 1, $"Client A should get several batch parts of {smallBatchSize} KB, got {partsA.Count}");
                Assert.True(partsB.Count <= 1, $"Client B should get at most one batch part of {largeBatchSize} KB, got {partsB.Count}");
                Assert.Equal(2000, serverOptions.BatchSize);
            }
            finally
            {
                HelperDatabase.DropDatabase(ProviderType.Sqlite, clientBName);
            }
        }

        /// <summary>
        /// One <see cref="WebServerAgent"/> instance serves two clients at the same time: client A uses a converter, client B does not.
        /// A's upload is held on the server after its converter was resolved, until a request of B came in.
        /// The server must still apply A's converter to A's uploaded rows.
        /// </summary>
        [Fact]
        public async Task SharedWebServerAgent_ConcurrentClients_EachUseTheirOwnConverter()
        {
            var options = new SyncOptions { DisableConstraintsOnApplyChanges = true };

            var serverConverter = new CountingConverter();
            var webServerOptions = new WebServerOptions();
            webServerOptions.Converters.Add(serverConverter);

            var sharedAgent = new WebServerAgent(serverProvider, setup, options, webServerOptions, batchStore: this.batchStorage);

            var uploadHeld = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var otherClientCameIn = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var held = 0;

            sharedAgent.OnHttpGettingRequest(async args =>
            {
                var hasConverter = args.HttpContext.Request.Headers.ContainsKey("dotmim-sync-converter");

                if (!hasConverter)
                {
                    otherClientCameIn.TrySetResult();
                }
                else if (args.Data is HttpMessageSendChangesRequest request && request.Changes?.HasRows == true && Interlocked.Exchange(ref held, 1) == 0)
                {
                    this.Output.WriteLine($"Holding the upload of client A ({args.HttpStep})");
                    uploadHeld.TrySetResult();
                    try
                    {
                        await otherClientCameIn.Task.WaitAsync(InterleaveTimeout);
                    }
                    catch (TimeoutException)
                    {
                        this.Output.WriteLine("Client B never sent a request");
                    }
                }
            });

            await this.Kestrel.StopAsync();
            this.AddSyncServer(serverProvider, setup, options, webServerOptions);
            var serviceUri = this.Kestrel.Run(async context => await sharedAgent.HandleRequestAsync(context));

            var clientBName = HelperDatabase.GetRandomName("http_sqlite_b_");
            var clientB = HelperDatabase.GetSyncProvider(ProviderType.Sqlite, clientBName, false);
            try
            {
                var clientA = clientsProvider.First();
                var agentA = new SyncAgent(clientA, new WebRemoteOrchestrator(serviceUri, new CountingConverter()), options);
                await agentA.SynchronizeAsync();

                await clientA.AddProductCategoryAsync();
                var convertedBefore = serverConverter.AfterDeserializedCount;

                var agentB = new SyncAgent(clientB, new WebRemoteOrchestrator(serviceUri), options);

                var syncA = agentA.SynchronizeAsync();
                await uploadHeld.Task.WaitAsync(InterleaveTimeout);
                var syncB = agentB.SynchronizeAsync();

                var results = await Task.WhenAll(syncA, syncB);
                var converted = serverConverter.AfterDeserializedCount - convertedBefore;

                this.Output.WriteLine($"Client A uploaded {results[0].TotalChangesUploadedToServer} rows, the server converted {converted} uploaded rows with A's converter");

                Assert.Equal(1, results[0].TotalChangesUploadedToServer);
                Assert.Equal(1, converted);
            }
            finally
            {
                HelperDatabase.DropDatabase(ProviderType.Sqlite, clientBName);
            }
        }

        /// <summary>
        /// Every http response the client got during a sync with an upload is disposed once the sync is done.
        /// </summary>
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task Sync_DisposesAllResponses(bool useOptimizedFlow)
        {
            // Small client batches, so that the upload takes several requests
            var options = new SyncOptions { DisableConstraintsOnApplyChanges = true, UseOptimizedFlow = useOptimizedFlow, BatchSize = 1 };
            const int rowsToUpload = 50;

            foreach (var clientProvider in clientsProvider)
            {
                await new SyncAgent(clientProvider, new WebRemoteOrchestrator(serviceUri), options).SynchronizeAsync();
                for (var i = 0; i < rowsToUpload; i++)
                    await clientProvider.AddProductCategoryAsync();

                var handler = new DisposeTrackingHandler();
                using var client = new HttpClient(handler);
                var proxy = new WebRemoteOrchestrator(serviceUri, client: client);

                var s = await new SyncAgent(clientProvider, proxy, options).SynchronizeAsync();
                Assert.Equal(rowsToUpload, s.TotalChangesUploadedToServer);

                // EndSession is sent fire-and-forget, so it may still be in flight when the sync returns
                var responses = handler.Responses.Where(r => r.Step != nameof(HttpStep.EndSession)).ToList();
                var notDisposed = responses.Where(r => !r.Content.IsDisposed).Select(r => r.Step).ToList();

                this.Output.WriteLine($"Responses: {string.Join(", ", responses.Select(r => r.Step))}. Not disposed: {string.Join(", ", notDisposed)}");

                var uploads = responses.Count(r => r.Step == nameof(HttpStep.SendChangesIncremental) || r.Step == nameof(HttpStep.SendChangesInProgress));
                Assert.True(uploads > 1, $"The upload should take several requests, took {uploads}");
                Assert.Empty(notDisposed);
            }
        }

        /// <summary>
        /// Without a transaction around the apply, the client creates a connection only to raise <see cref="DatabaseChangesAppliedArgs"/>.
        /// That connection is disposed once the interceptors ran.
        /// </summary>
        [Fact]
        public async Task LocalApply_WithoutTransaction_DisposesConnectionOfDatabaseChangesAppliedArgs()
        {
            var options = new SyncOptions { DisableConstraintsOnApplyChanges = true, TransactionMode = TransactionMode.None };

            foreach (var clientProvider in clientsProvider)
            {
                var agent = new SyncAgent(clientProvider, new WebRemoteOrchestrator(serviceUri), options);

                var connections = new ConcurrentQueue<(DbConnection Connection, bool[] Disposed)>();
                agent.LocalOrchestrator.OnDatabaseChangesApplied(args =>
                {
                    var disposed = new bool[1];
                    args.Connection.Disposed += (_, _) => disposed[0] = true;
                    connections.Enqueue((args.Connection, disposed));
                });

                await agent.SynchronizeAsync();

                foreach (var (connection, disposed) in connections)
                    this.Output.WriteLine($"DatabaseChangesAppliedArgs connection {connection.GetType().Name}: disposed={disposed[0]}");

                Assert.NotEmpty(connections);
                Assert.All(connections, c => Assert.True(c.Disposed[0]));
            }
        }

        /// <summary>
        /// Wraps each response content, to know whether the response (and so its content) has been disposed.
        /// </summary>
        private sealed class DisposeTrackingHandler : DelegatingHandler
        {
            public DisposeTrackingHandler()
                : base(new HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate })
            {
            }

            public ConcurrentQueue<(string Step, TrackingContent Content)> Responses { get; } = new();

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var response = await base.SendAsync(request, cancellationToken);

                var step = request.Headers.TryGetValues("dotmim-sync-step", out var values) ? ((HttpStep)int.Parse(values.First())).ToString() : "?";
                var content = new TrackingContent(response.Content);
                response.Content = content;
                this.Responses.Enqueue((step, content));

                return response;
            }
        }

        private sealed class TrackingContent : HttpContent
        {
            private readonly HttpContent inner;

            public TrackingContent(HttpContent inner)
            {
                this.inner = inner;
                foreach (var header in inner.Headers)
                    this.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            public bool IsDisposed { get; private set; }

            protected override Task SerializeToStreamAsync(System.IO.Stream stream, System.Net.TransportContext context) => this.inner.CopyToAsync(stream);

            protected override Task<System.IO.Stream> CreateContentReadStreamAsync() => this.inner.ReadAsStreamAsync();

            protected override bool TryComputeLength(out long length)
            {
                length = 0;
                return false;
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    this.IsDisposed = true;
                    this.inner.Dispose();
                }

                base.Dispose(disposing);
            }
        }

        private sealed class CountingConverter : IConverter
        {
            private int afterDeserializedCount;

            public string Key => "counting";

            public int AfterDeserializedCount => Volatile.Read(ref this.afterDeserializedCount);

            public void BeforeSerialize(SyncRow row, SyncTable schemaTable)
            {
            }

            public void AfterDeserialized(SyncRow row, SyncTable schemaTable) => Interlocked.Increment(ref this.afterDeserializedCount);
        }
    }
}
