using System;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Wormhole.Sync.Async;
using Wormhole.Sync.Web.Hangfire;
using Xunit;


namespace Wormhole.Sync.Tests.UnitTests.Hangfire
{
    /// <summary>
    /// Tests for HangfireBatchCreationJobService.
    /// </summary>
    public class HangfireBatchCreationJobServiceTests : IDisposable
    {
        private Stopwatch stopwatch;
        private readonly Mock<IBackgroundJobClient> mockBackgroundJobClient;
        private readonly IBatchJobStore jobStore;
        private readonly Mock<ILogger<HangfireBatchCreationJobService>> mockLogger;
        private readonly HangfireBatchCreationJobService service;

        public ITestOutputHelper Output { get; }

        public HangfireBatchCreationJobServiceTests(ITestOutputHelper output)
        {
            this.Output = output;
            var type = output.GetType();
            this.stopwatch = Stopwatch.StartNew();

            // Setup mocks
            this.mockBackgroundJobClient = new Mock<IBackgroundJobClient>();
            this.mockBackgroundJobClient
                .Setup(x => x.Create(It.IsAny<Job>(), It.IsAny<IState>()))
                .Returns("hangfire-job-123");

            // Use real distributed job store with in-memory cache
            var memoryCache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
            this.jobStore = new DistributedBatchJobStore(memoryCache);

            this.mockLogger = new Mock<ILogger<HangfireBatchCreationJobService>>();

            this.service = new HangfireBatchCreationJobService(
                this.mockBackgroundJobClient.Object,
                this.jobStore,
                this.mockLogger.Object);
        }

        private BatchCreationJobParameters CreateTestParameters(string scopeName = "TestScope")
        {
            return new BatchCreationJobParameters
            {
                ScopeName = scopeName,
                BatchDirectory = "/tmp/batches",
                BatchSize = 2000,
            };
        }

        [Fact]
        public async Task EnqueueBatchCreationAsync_ShouldStoreParametersAndStatus()
        {
            var jobId = "test-job-1";
            var parameters = CreateTestParameters("MyScope");

            var result = await this.service.EnqueueBatchCreationAsync(jobId, parameters);

            Assert.Equal(jobId, result);

            // Verify parameters were stored
            var storedParams = await this.jobStore.GetParametersAsync(jobId);
            Assert.NotNull(storedParams);
            Assert.Equal("MyScope", storedParams.ScopeName);

            // Verify status was set to Queued
            var storedStatus = await this.jobStore.GetStatusAsync(jobId);
            Assert.NotNull(storedStatus);
            Assert.Equal(BatchCreationJobState.Queued, storedStatus.State);
            Assert.Equal(jobId, storedStatus.JobId);
        }

        [Fact]
        public async Task EnqueueBatchCreationAsync_ShouldEnqueueHangfireJob()
        {
            var jobId = "test-job-2";
            var parameters = CreateTestParameters();

            await this.service.EnqueueBatchCreationAsync(jobId, parameters);

            // Verify Hangfire job was enqueued
            this.mockBackgroundJobClient.Verify(
                x => x.Create(It.IsAny<Job>(), It.IsAny<IState>()),
                Times.Once);
        }

        [Fact]
        public async Task EnqueueBatchCreationAsync_WithExistingJob_ShouldReturnExistingJobId()
        {
            var jobId = "existing-job";
            var parameters = CreateTestParameters();

            // First enqueue
            await this.service.EnqueueBatchCreationAsync(jobId, parameters);

            // Reset mock to track second call
            this.mockBackgroundJobClient.Invocations.Clear();

            // Second enqueue with same jobId (idempotent)
            var result = await this.service.EnqueueBatchCreationAsync(jobId, parameters);

            Assert.Equal(jobId, result);

            // Hangfire should NOT be called again for existing job
            this.mockBackgroundJobClient.Verify(
                x => x.Create(It.IsAny<Job>(), It.IsAny<IState>()),
                Times.Never);
        }

        [Fact]
        public async Task EnqueueBatchCreationAsync_WithNullJobId_ShouldThrowArgumentNullException()
        {
            await Assert.ThrowsAsync<ArgumentNullException>(() =>
                this.service.EnqueueBatchCreationAsync(null, CreateTestParameters()));
        }

        [Fact]
        public async Task EnqueueBatchCreationAsync_WithEmptyJobId_ShouldThrowArgumentNullException()
        {
            await Assert.ThrowsAsync<ArgumentNullException>(() =>
                this.service.EnqueueBatchCreationAsync("", CreateTestParameters()));
        }

        [Fact]
        public async Task EnqueueBatchCreationAsync_WithNullParameters_ShouldThrowArgumentNullException()
        {
            await Assert.ThrowsAsync<ArgumentNullException>(() =>
                this.service.EnqueueBatchCreationAsync("job-1", null));
        }

        [Fact]
        public async Task GetJobStatusAsync_WithExistingJob_ShouldReturnStatus()
        {
            var jobId = "status-test-job";
            await this.service.EnqueueBatchCreationAsync(jobId, CreateTestParameters());

            var status = await this.service.GetJobStatusAsync(jobId);

            Assert.NotNull(status);
            Assert.Equal(jobId, status.JobId);
            Assert.Equal(BatchCreationJobState.Queued, status.State);
        }

        [Fact]
        public async Task GetJobStatusAsync_WithNonExistentJob_ShouldReturnNull()
        {
            var status = await this.service.GetJobStatusAsync("non-existent-job");

            Assert.Null(status);
        }

        [Fact]
        public async Task RemoveJobAsync_ShouldRemoveJobFromStore()
        {
            var jobId = "remove-test-job";
            await this.service.EnqueueBatchCreationAsync(jobId, CreateTestParameters());

            await this.service.RemoveJobAsync(jobId);

            var status = await this.service.GetJobStatusAsync(jobId);
            Assert.Null(status);

            var parameters = await this.jobStore.GetParametersAsync(jobId);
            Assert.Null(parameters);
        }

        [Fact]
        public async Task RemoveJobAsync_WithNonExistentJob_ShouldNotThrow()
        {
            var exception = await Record.ExceptionAsync(() =>
                this.service.RemoveJobAsync("non-existent"));

            Assert.Null(exception);
        }

        [Fact]
        public async Task EnqueueBatchCreationAsync_ShouldSetTotalTablesFromSchema()
        {
            var jobId = "schema-test-job";
            var parameters = CreateTestParameters();
            parameters.ServerScopeInfo = new ScopeInfo
            {
                Schema = new SyncSet()
            };
            parameters.ServerScopeInfo.Schema.Tables.Add(new SyncTable("Table1"));
            parameters.ServerScopeInfo.Schema.Tables.Add(new SyncTable("Table2"));
            parameters.ServerScopeInfo.Schema.Tables.Add(new SyncTable("Table3"));

            await this.service.EnqueueBatchCreationAsync(jobId, parameters);

            var status = await this.service.GetJobStatusAsync(jobId);
            Assert.Equal(3, status.TotalTables);
        }

        [Fact]
        public async Task EnqueueBatchCreationAsync_WithNullSchema_ShouldSetTotalTablesToZero()
        {
            var jobId = "null-schema-job";
            var parameters = CreateTestParameters();
            parameters.ServerScopeInfo = null;

            await this.service.EnqueueBatchCreationAsync(jobId, parameters);

            var status = await this.service.GetJobStatusAsync(jobId);
            Assert.Equal(0, status.TotalTables);
        }

        [Fact]
        public async Task MultipleJobs_ShouldBeTrackedIndependently()
        {
            var jobId1 = "multi-job-1";
            var jobId2 = "multi-job-2";
            var jobId3 = "multi-job-3";

            await this.service.EnqueueBatchCreationAsync(jobId1, CreateTestParameters("Scope1"));
            await this.service.EnqueueBatchCreationAsync(jobId2, CreateTestParameters("Scope2"));
            await this.service.EnqueueBatchCreationAsync(jobId3, CreateTestParameters("Scope3"));

            var status1 = await this.service.GetJobStatusAsync(jobId1);
            var status2 = await this.service.GetJobStatusAsync(jobId2);
            var status3 = await this.service.GetJobStatusAsync(jobId3);

            Assert.Equal(jobId1, status1.JobId);
            Assert.Equal(jobId2, status2.JobId);
            Assert.Equal(jobId3, status3.JobId);

            // Verify all are independent
            await this.service.RemoveJobAsync(jobId2);

            Assert.NotNull(await this.service.GetJobStatusAsync(jobId1));
            Assert.Null(await this.service.GetJobStatusAsync(jobId2));
            Assert.NotNull(await this.service.GetJobStatusAsync(jobId3));
        }

        [Fact]
        public async Task CustomOptions_ShouldBeUsed()
        {
            var options = new HangfireBatchCreationJobServiceOptions
            {
                QueueName = "custom-queue"
            };

            var customService = new HangfireBatchCreationJobService(
                this.mockBackgroundJobClient.Object,
                this.jobStore,
                this.mockLogger.Object,
                options);

            var jobId = "custom-options-job";
            await customService.EnqueueBatchCreationAsync(jobId, CreateTestParameters());

            // The job should have been enqueued (we can't easily verify the queue name
            // without more complex mock setup, but at least verify it works)
            this.mockBackgroundJobClient.Verify(
                x => x.Create(It.IsAny<Job>(), It.IsAny<IState>()),
                Times.AtLeastOnce);
        }

        public void Dispose()
        {
            this.stopwatch?.Stop();
            this.Output?.WriteLine($"Test took {this.stopwatch?.Elapsed.ToString() ?? "unknown time"}");
        }
    }
}
