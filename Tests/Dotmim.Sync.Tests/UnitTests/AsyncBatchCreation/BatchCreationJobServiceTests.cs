using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Wormhole.Sync.Async;
using Wormhole.Sync.Web.Server.Async;
using Xunit;
using Xunit.Abstractions;

namespace Wormhole.Sync.Tests.UnitTests.AsyncBatchCreation
{
    public class BatchCreationJobServiceTests : IDisposable
    {
        private ITest test;
        private Stopwatch stopwatch;
        private readonly InMemoryBatchJobStore store;
        private readonly DefaultBatchCreationJobService jobService;
        private readonly ILogger<DefaultBatchCreationJobService> logger;

        public ITestOutputHelper Output { get; }

        public BatchCreationJobServiceTests(ITestOutputHelper output)
        {
            this.Output = output;
            var type = output.GetType();
            var testMember = type.GetField("test", BindingFlags.Instance | BindingFlags.NonPublic);
            this.test = (ITest)testMember.GetValue(output);
            this.stopwatch = Stopwatch.StartNew();

            this.store = new InMemoryBatchJobStore();
            this.logger = NullLogger<DefaultBatchCreationJobService>.Instance;
            this.jobService = new DefaultBatchCreationJobService(this.store, this.logger);
        }

        private BatchCreationJobParameters CreateTestParameters(string scopeName = "TestScope")
        {
            return new BatchCreationJobParameters
            {
                ScopeName = scopeName,
                ServerScopeInfo = new ScopeInfo { Name = scopeName },
                ClientScopeInfoClient = new ScopeInfoClient { Id = Guid.NewGuid(), IsNewScope = true },
                Context = new SyncContext(Guid.NewGuid(), scopeName),
                BatchDirectory = "/tmp/batches",
                BatchSize = 2000,
                ProviderTypeName = "TestProvider",
                ConnectionString = "TestConnectionString",
            };
        }

        [Fact]
        public async Task EnqueueBatchCreationAsync_ShouldCreateJobWithQueuedState()
        {
            var jobId = Guid.NewGuid().ToString();
            var parameters = CreateTestParameters();

            var result = await this.jobService.EnqueueBatchCreationAsync(jobId, parameters);

            Assert.Equal(jobId, result);
            var status = await this.jobService.GetJobStatusAsync(jobId);
            Assert.NotNull(status);
            Assert.Equal(jobId, status.JobId);
            Assert.Equal(BatchCreationJobState.Queued, status.State);
        }

        [Fact]
        public async Task EnqueueBatchCreationAsync_ShouldStoreParameters()
        {
            var jobId = Guid.NewGuid().ToString();
            var parameters = CreateTestParameters("MyScopeName");

            await this.jobService.EnqueueBatchCreationAsync(jobId, parameters);

            var storedParams = this.store.GetParameters(jobId);
            Assert.NotNull(storedParams);
            Assert.Equal("MyScopeName", storedParams.ScopeName);
            Assert.Equal(2000, storedParams.BatchSize);
        }

        [Fact]
        public async Task EnqueueBatchCreationAsync_WithExistingJob_ShouldBeIdempotent()
        {
            var jobId = Guid.NewGuid().ToString();
            var parameters1 = CreateTestParameters("Scope1");
            var parameters2 = CreateTestParameters("Scope2");

            await this.jobService.EnqueueBatchCreationAsync(jobId, parameters1);
            await this.jobService.EnqueueBatchCreationAsync(jobId, parameters2);

            // Should keep original parameters
            var storedParams = this.store.GetParameters(jobId);
            Assert.Equal("Scope1", storedParams.ScopeName);
        }

        [Fact]
        public async Task EnqueueBatchCreationAsync_WithNullJobId_ShouldThrow()
        {
            var parameters = CreateTestParameters();

            await Assert.ThrowsAsync<ArgumentNullException>(
                () => this.jobService.EnqueueBatchCreationAsync(null, parameters));
        }

        [Fact]
        public async Task EnqueueBatchCreationAsync_WithNullParameters_ShouldThrow()
        {
            var jobId = Guid.NewGuid().ToString();

            await Assert.ThrowsAsync<ArgumentNullException>(
                () => this.jobService.EnqueueBatchCreationAsync(jobId, null));
        }

        [Fact]
        public async Task EnqueueBatchCreationAsync_ShouldSetEnqueuedAtTimestamp()
        {
            var jobId = Guid.NewGuid().ToString();
            var parameters = CreateTestParameters();
            var beforeEnqueue = DateTime.UtcNow;

            await this.jobService.EnqueueBatchCreationAsync(jobId, parameters);

            var status = await this.jobService.GetJobStatusAsync(jobId);
            Assert.True(status.EnqueuedAt >= beforeEnqueue);
            Assert.True(status.EnqueuedAt <= DateTime.UtcNow);
        }

        [Fact]
        public async Task GetJobStatusAsync_WithNonExistentJob_ShouldReturnNull()
        {
            var status = await this.jobService.GetJobStatusAsync("nonexistent");

            Assert.Null(status);
        }

        [Fact]
        public async Task GetJobStatusAsync_ShouldReturnCorrectStatus()
        {
            var jobId = Guid.NewGuid().ToString();
            var parameters = CreateTestParameters();
            await this.jobService.EnqueueBatchCreationAsync(jobId, parameters);

            var status = await this.jobService.GetJobStatusAsync(jobId);

            Assert.NotNull(status);
            Assert.Equal(jobId, status.JobId);
            Assert.Equal(BatchCreationJobState.Queued, status.State);
        }

        [Fact]
        public async Task RemoveJobAsync_ShouldRemoveJobFromStore()
        {
            var jobId = Guid.NewGuid().ToString();
            var parameters = CreateTestParameters();
            await this.jobService.EnqueueBatchCreationAsync(jobId, parameters);

            await this.jobService.RemoveJobAsync(jobId);

            var status = await this.jobService.GetJobStatusAsync(jobId);
            Assert.Null(status);
            Assert.Null(this.store.GetParameters(jobId));
        }

        [Fact]
        public async Task RemoveJobAsync_WithNonExistentJob_ShouldNotThrow()
        {
            var exception = await Record.ExceptionAsync(
                () => this.jobService.RemoveJobAsync("nonexistent"));

            Assert.Null(exception);
        }

        [Fact]
        public async Task EnqueueBatchCreationAsync_MultipleJobs_ShouldAllBeQueued()
        {
            var jobIds = Enumerable.Range(1, 10).Select(_ => Guid.NewGuid().ToString()).ToList();

            foreach (var jobId in jobIds)
            {
                await this.jobService.EnqueueBatchCreationAsync(jobId, CreateTestParameters());
            }

            foreach (var jobId in jobIds)
            {
                var status = await this.jobService.GetJobStatusAsync(jobId);
                Assert.NotNull(status);
                Assert.Equal(BatchCreationJobState.Queued, status.State);
            }

            Assert.Equal(10, this.store.Count);
        }

        [Fact]
        public async Task EnqueueBatchCreationAsync_WithCancellation_ShouldThrow()
        {
            var jobId = Guid.NewGuid().ToString();
            var parameters = CreateTestParameters();
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAsync<TaskCanceledException>(
                () => this.jobService.EnqueueBatchCreationAsync(jobId, parameters, cts.Token));
        }

        public void Dispose()
        {
            this.stopwatch?.Stop();
            this.Output?.WriteLine($"Test {this.test?.DisplayName} took {this.stopwatch?.Elapsed.ToString() ?? "unknown time"}");
        }
    }
}
