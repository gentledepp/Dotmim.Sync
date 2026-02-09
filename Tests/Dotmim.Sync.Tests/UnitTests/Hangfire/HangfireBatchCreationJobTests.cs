using Hangfire.Server;
using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Wormhole.Sync.Async;
using Wormhole.Sync.Batch;
using Wormhole.Sync.Web.Hangfire;
using Xunit;


namespace Wormhole.Sync.Tests.UnitTests.Hangfire
{
    /// <summary>
    /// Tests for HangfireBatchCreationJob.
    /// </summary>
    public class HangfireBatchCreationJobTests : IDisposable
    {
        private Stopwatch stopwatch;
        private readonly IBatchJobStore jobStore;
        private readonly Mock<IBatchCreationExecutor> mockExecutor;
        private readonly Mock<ILogger<HangfireBatchCreationJob>> mockLogger;
        private readonly HangfireBatchCreationJob job;
        private readonly Mock<IHangfireContextLoggerProvider<HangfireBatchCreationJob>> mockProvider;

        public ITestOutputHelper Output { get; }

        public HangfireBatchCreationJobTests(ITestOutputHelper output)
        {
            this.Output = output;
            var type = output.GetType();
            this.stopwatch = Stopwatch.StartNew();

            // Use real distributed job store with in-memory cache
            var memoryCache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
            this.jobStore = new DistributedBatchJobStore(memoryCache);

            this.mockExecutor = new Mock<IBatchCreationExecutor>();
            this.mockLogger = new Mock<ILogger<HangfireBatchCreationJob>>();
            this.mockProvider = new Mock<IHangfireContextLoggerProvider<HangfireBatchCreationJob>>();
            this.mockProvider.Setup(s => s.Provide(It.IsAny<ILogger<HangfireBatchCreationJob>>(), It.IsAny<PerformContext>()))
                .Returns(this.mockLogger.Object);

            this.job = new HangfireBatchCreationJob(
                this.jobStore,
                this.mockExecutor.Object,
                this.mockLogger.Object,
                this.mockProvider.Object);
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

        private BatchCreationJobStatus CreateTestStatus(string jobId, BatchCreationJobState state)
        {
            return new BatchCreationJobStatus
            {
                JobId = jobId,
                State = state,
                EnqueuedAt = DateTime.UtcNow,
            };
        }

        [Fact]
        public async Task ExecuteAsync_WithSuccessfulExecution_ShouldUpdateStatusToCompleted()
        {
            var jobId = "success-job";

            // Setup job in store
            await this.jobStore.SetParametersAsync(jobId, CreateTestParameters());
            await this.jobStore.SetStatusAsync(jobId, CreateTestStatus(jobId, BatchCreationJobState.Queued));

            // Setup executor to return success
            this.mockExecutor
                .Setup(x => x.ExecuteAsync(jobId, It.IsAny<BatchCreationJobParameters>(), It.IsAny<BatchPartProgressCallback>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BatchCreationResult
                {
                    Success = true,
                    RemoteClientTimestamp = 123456789,
                    BatchInfo = new BatchInfo(),
                    ChangesSelected = new DatabaseChangesSelected(),
                    ChangesApplied = new DatabaseChangesApplied(),
                });

            await this.job.ExecuteAsync(jobId, null);

            var status = await this.jobStore.GetStatusAsync(jobId);
            Assert.Equal(BatchCreationJobState.Completed, status.State);
            Assert.Equal(100, status.ProgressPercentage);
            Assert.Equal(123456789, status.RemoteClientTimestamp);
            Assert.NotNull(status.CompletedAt);
        }

        [Fact]
        public async Task ExecuteAsync_WithFailedExecution_ShouldUpdateStatusToFailed()
        {
            var jobId = "failed-job";

            // Setup job in store
            await this.jobStore.SetParametersAsync(jobId, CreateTestParameters());
            await this.jobStore.SetStatusAsync(jobId, CreateTestStatus(jobId, BatchCreationJobState.Queued));

            // Setup executor to return failure
            this.mockExecutor
                .Setup(x => x.ExecuteAsync(jobId, It.IsAny<BatchCreationJobParameters>(), It.IsAny<BatchPartProgressCallback>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BatchCreationResult
                {
                    Success = false,
                    ErrorMessage = "Test error message",
                    ErrorStackTrace = "Test stack trace",
                });

            // Should throw because executor returned failure
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                this.job.ExecuteAsync(jobId, null));

            var status = await this.jobStore.GetStatusAsync(jobId);
            Assert.Equal(BatchCreationJobState.Failed, status.State);
            Assert.Equal("Test error message", status.ErrorMessage);
            Assert.NotNull(status.CompletedAt);
        }

        [Fact]
        public async Task ExecuteAsync_WithException_ShouldUpdateStatusToFailed()
        {
            var jobId = "exception-job";

            // Setup job in store
            await this.jobStore.SetParametersAsync(jobId, CreateTestParameters());
            await this.jobStore.SetStatusAsync(jobId, CreateTestStatus(jobId, BatchCreationJobState.Queued));

            // Setup executor to throw exception
            this.mockExecutor
                .Setup(x => x.ExecuteAsync(jobId, It.IsAny<BatchCreationJobParameters>(), It.IsAny<BatchPartProgressCallback>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("Unexpected error"));

            await Assert.ThrowsAsync<Exception>(() =>
                this.job.ExecuteAsync(jobId, null));

            var status = await this.jobStore.GetStatusAsync(jobId);
            Assert.Equal(BatchCreationJobState.Failed, status.State);
            Assert.Equal("Unexpected error", status.ErrorMessage);
            Assert.NotNull(status.ErrorStackTrace);
        }

        [Fact]
        public async Task ExecuteAsync_ShouldUpdateStatusToProcessingFirst()
        {
            var jobId = "processing-job";

            // Setup job in store
            await this.jobStore.SetParametersAsync(jobId, CreateTestParameters());
            await this.jobStore.SetStatusAsync(jobId, CreateTestStatus(jobId, BatchCreationJobState.Queued));

            BatchCreationJobState? capturedState = null;

            // Setup executor to capture state during execution
            this.mockExecutor
                .Setup(x => x.ExecuteAsync(jobId, It.IsAny<BatchCreationJobParameters>(), It.IsAny<BatchPartProgressCallback>(), It.IsAny<CancellationToken>()))
                .Returns(async () =>
                {
                    // Capture the state during execution
                    var status = await this.jobStore.GetStatusAsync(jobId);
                    capturedState = status?.State;

                    return new BatchCreationResult { Success = true };
                });

            await this.job.ExecuteAsync(jobId, null);

            // The state should have been Processing during execution
            Assert.Equal(BatchCreationJobState.Processing, capturedState);
        }

        [Fact]
        public async Task ExecuteAsync_WithNonExistentJob_ShouldNotThrow()
        {
            var jobId = "non-existent-job";

            var exception = await Record.ExceptionAsync(() =>
                this.job.ExecuteAsync(jobId, null));

            Assert.Null(exception);

            // Executor should not have been called
            this.mockExecutor.Verify(
                x => x.ExecuteAsync(It.IsAny<string>(), It.IsAny<BatchCreationJobParameters>(), It.IsAny<BatchPartProgressCallback>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task ExecuteAsync_WithMissingParameters_ShouldNotThrow()
        {
            var jobId = "missing-params-job";

            // Only set status, not parameters
            await this.jobStore.SetStatusAsync(jobId, CreateTestStatus(jobId, BatchCreationJobState.Queued));

            var exception = await Record.ExceptionAsync(() =>
                this.job.ExecuteAsync(jobId, null));

            Assert.Null(exception);

            // Executor should not have been called
            this.mockExecutor.Verify(
                x => x.ExecuteAsync(It.IsAny<string>(), It.IsAny<BatchCreationJobParameters>(), It.IsAny<BatchPartProgressCallback>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task ExecuteAsync_WithMissingStatus_ShouldNotThrow()
        {
            var jobId = "missing-status-job";

            // Only set parameters, not status
            await this.jobStore.SetParametersAsync(jobId, CreateTestParameters());

            var exception = await Record.ExceptionAsync(() =>
                this.job.ExecuteAsync(jobId, null));

            Assert.Null(exception);

            // Executor should not have been called
            this.mockExecutor.Verify(
                x => x.ExecuteAsync(It.IsAny<string>(), It.IsAny<BatchCreationJobParameters>(), It.IsAny<BatchPartProgressCallback>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task ExecuteAsync_ShouldPassCorrectParameters()
        {
            var jobId = "params-verify-job";
            var parameters = CreateTestParameters("VerifyScope");
            parameters.BatchSize = 5000;
            parameters.BatchDirectory = "/custom/path";

            await this.jobStore.SetParametersAsync(jobId, parameters);
            await this.jobStore.SetStatusAsync(jobId, CreateTestStatus(jobId, BatchCreationJobState.Queued));

            BatchCreationJobParameters capturedParams = null;

            this.mockExecutor
                .Setup(x => x.ExecuteAsync(jobId, It.IsAny<BatchCreationJobParameters>(), It.IsAny<BatchPartProgressCallback>(), It.IsAny<CancellationToken>()))
                .Callback<string, BatchCreationJobParameters, BatchPartProgressCallback, CancellationToken>((id, p, cb, ct) => capturedParams = p)
                .ReturnsAsync(new BatchCreationResult { Success = true });

            await this.job.ExecuteAsync(jobId, null);

            Assert.NotNull(capturedParams);
            Assert.Equal("VerifyScope", capturedParams.ScopeName);
            Assert.Equal(5000, capturedParams.BatchSize);
            Assert.Equal("/custom/path", capturedParams.BatchDirectory);
        }

        [Fact]
        public async Task ExecuteAsync_ShouldSetStartedAtTimestamp()
        {
            var jobId = "timestamp-job";
            var beforeExecution = DateTime.UtcNow;

            await this.jobStore.SetParametersAsync(jobId, CreateTestParameters());
            await this.jobStore.SetStatusAsync(jobId, CreateTestStatus(jobId, BatchCreationJobState.Queued));

            this.mockExecutor
                .Setup(x => x.ExecuteAsync(jobId, It.IsAny<BatchCreationJobParameters>(), It.IsAny<BatchPartProgressCallback>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BatchCreationResult { Success = true });

            await this.job.ExecuteAsync(jobId, null);

            var status = await this.jobStore.GetStatusAsync(jobId);
            Assert.NotNull(status.StartedAt);
            Assert.True(status.StartedAt >= beforeExecution);
        }

        [Fact]
        public async Task ExecuteAsync_WithSuccessfulResult_ShouldStoreBatchInfo()
        {
            var jobId = "batch-info-job";

            await this.jobStore.SetParametersAsync(jobId, CreateTestParameters());
            await this.jobStore.SetStatusAsync(jobId, CreateTestStatus(jobId, BatchCreationJobState.Queued));

            var batchInfo = new BatchInfo
            {
                DirectoryRoot = "/batches",
                DirectoryName = "batch-123",
            };

            var changesSelected = new DatabaseChangesSelected();
            changesSelected.TableChangesSelected.Add(new TableChangesSelected
            {
                TableName = "TestTable",
                Upserts = 15,
                Deletes = 2,
            });

            this.mockExecutor
                .Setup(x => x.ExecuteAsync(jobId, It.IsAny<BatchCreationJobParameters>(), It.IsAny<BatchPartProgressCallback>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BatchCreationResult
                {
                    Success = true,
                    BatchInfo = batchInfo,
                    ChangesSelected = changesSelected,
                });

            await this.job.ExecuteAsync(jobId, null);

            var status = await this.jobStore.GetStatusAsync(jobId);
            Assert.NotNull(status.BatchInfo);
            Assert.NotNull(status.ChangesSelected);
        }

        [Fact]
        public void Constructor_WithNullJobStore_ShouldThrowArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new HangfireBatchCreationJob(null, this.mockExecutor.Object, this.mockLogger.Object,
                    this.mockProvider.Object));
        }

        [Fact]
        public void Constructor_WithNullExecutor_ShouldThrowArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new HangfireBatchCreationJob(this.jobStore, (IBatchCreationExecutor)null, this.mockLogger.Object,
                    this.mockProvider.Object));
        }

        [Fact]
        public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new HangfireBatchCreationJob(this.jobStore, this.mockExecutor.Object, null,
                    this.mockProvider.Object));
        }

        public void Dispose()
        {
            this.stopwatch?.Stop();
            this.Output?.WriteLine($"Test took {this.stopwatch?.Elapsed.ToString() ?? "unknown time"}");
        }
    }
}
