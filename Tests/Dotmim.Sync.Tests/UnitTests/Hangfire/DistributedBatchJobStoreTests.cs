using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Wormhole.Sync.Async;
using Wormhole.Sync.Web.Hangfire;
using Xunit;
using Xunit.Abstractions;

namespace Wormhole.Sync.Tests.UnitTests.Hangfire
{
    /// <summary>
    /// Tests for DistributedBatchJobStore using an in-memory distributed cache.
    /// </summary>
    public class DistributedBatchJobStoreTests : IDisposable
    {
        private ITest test;
        private Stopwatch stopwatch;
        private readonly IDistributedCache cache;
        private readonly DistributedBatchJobStore store;

        public ITestOutputHelper Output { get; }

        public DistributedBatchJobStoreTests(ITestOutputHelper output)
        {
            this.Output = output;
            var type = output.GetType();
            var testMember = type.GetField("test", BindingFlags.Instance | BindingFlags.NonPublic);
            this.test = (ITest)testMember.GetValue(output);
            this.stopwatch = Stopwatch.StartNew();

            // Use MemoryDistributedCache for testing
            var memoryCache = new MemoryCache(new MemoryCacheOptions());
            this.cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
            this.store = new DistributedBatchJobStore(this.cache);
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
        public async Task SetParametersAsync_ShouldStoreParameters()
        {
            var jobId = "job1";
            var parameters = CreateTestParameters("MyScope");

            await this.store.SetParametersAsync(jobId, parameters);

            var stored = await this.store.GetParametersAsync(jobId);
            Assert.NotNull(stored);
            Assert.Equal("MyScope", stored.ScopeName);
        }

        [Fact]
        public async Task GetParametersAsync_WithNonExistentJob_ShouldReturnNull()
        {
            var result = await this.store.GetParametersAsync("nonexistent");

            Assert.Null(result);
        }

        [Fact]
        public async Task SetParametersAsync_ShouldOverwriteExisting()
        {
            var jobId = "job1";
            await this.store.SetParametersAsync(jobId, CreateTestParameters("Scope1"));
            await this.store.SetParametersAsync(jobId, CreateTestParameters("Scope2"));

            var stored = await this.store.GetParametersAsync(jobId);
            Assert.Equal("Scope2", stored.ScopeName);
        }

        [Fact]
        public async Task SetStatusAsync_ShouldStoreStatus()
        {
            var jobId = "job1";
            var status = CreateTestStatus(jobId, BatchCreationJobState.Processing);

            await this.store.SetStatusAsync(jobId, status);

            var stored = await this.store.GetStatusAsync(jobId);
            Assert.NotNull(stored);
            Assert.Equal(jobId, stored.JobId);
            Assert.Equal(BatchCreationJobState.Processing, stored.State);
        }

        [Fact]
        public async Task GetStatusAsync_WithNonExistentJob_ShouldReturnNull()
        {
            var result = await this.store.GetStatusAsync("nonexistent");

            Assert.Null(result);
        }

        [Fact]
        public async Task SetStatusAsync_ShouldOverwriteExisting()
        {
            var jobId = "job1";
            await this.store.SetStatusAsync(jobId, CreateTestStatus(jobId, BatchCreationJobState.Queued));
            await this.store.SetStatusAsync(jobId, CreateTestStatus(jobId, BatchCreationJobState.Completed));

            var stored = await this.store.GetStatusAsync(jobId);
            Assert.Equal(BatchCreationJobState.Completed, stored.State);
        }

        [Fact]
        public async Task RemoveJobAsync_ShouldRemoveBothStatusAndParameters()
        {
            var jobId = "job1";
            await this.store.SetParametersAsync(jobId, CreateTestParameters());
            await this.store.SetStatusAsync(jobId, CreateTestStatus(jobId, BatchCreationJobState.Queued));

            await this.store.RemoveJobAsync(jobId);

            Assert.Null(await this.store.GetParametersAsync(jobId));
            Assert.Null(await this.store.GetStatusAsync(jobId));
        }

        [Fact]
        public async Task RemoveJobAsync_WithNonExistentJob_ShouldNotThrow()
        {
            var exception = await Record.ExceptionAsync(() => this.store.RemoveJobAsync("nonexistent"));

            Assert.Null(exception);
        }

        [Fact]
        public async Task GetCountAsync_ShouldReturnNumberOfJobs()
        {
            await this.store.SetStatusAsync("job1", CreateTestStatus("job1", BatchCreationJobState.Queued));
            await this.store.SetStatusAsync("job2", CreateTestStatus("job2", BatchCreationJobState.Queued));
            await this.store.SetStatusAsync("job3", CreateTestStatus("job3", BatchCreationJobState.Queued));

            var count = await this.store.GetCountAsync();

            Assert.Equal(3, count);
        }

        [Fact]
        public async Task GetCountAsync_AfterRemove_ShouldDecrement()
        {
            await this.store.SetStatusAsync("job1", CreateTestStatus("job1", BatchCreationJobState.Queued));
            await this.store.SetStatusAsync("job2", CreateTestStatus("job2", BatchCreationJobState.Queued));

            await this.store.RemoveJobAsync("job1");

            var count = await this.store.GetCountAsync();
            Assert.Equal(1, count);
        }

        [Fact]
        public async Task GetExpiredJobIdsAsync_ShouldReturnOnlyExpiredCompletedJobs()
        {
            var maxAge = TimeSpan.FromHours(1);
            var now = DateTime.UtcNow;

            // Expired completed job (should be returned)
            await this.store.SetStatusAsync("expired1", new BatchCreationJobStatus
            {
                JobId = "expired1",
                State = BatchCreationJobState.Completed,
                EnqueuedAt = now.AddHours(-2),
            });

            // Expired failed job (should be returned)
            await this.store.SetStatusAsync("expired2", new BatchCreationJobStatus
            {
                JobId = "expired2",
                State = BatchCreationJobState.Failed,
                EnqueuedAt = now.AddHours(-2),
            });

            // Expired cancelled job (should be returned)
            await this.store.SetStatusAsync("expired3", new BatchCreationJobStatus
            {
                JobId = "expired3",
                State = BatchCreationJobState.Cancelled,
                EnqueuedAt = now.AddHours(-2),
            });

            // Not expired completed job (should NOT be returned)
            await this.store.SetStatusAsync("recent", new BatchCreationJobStatus
            {
                JobId = "recent",
                State = BatchCreationJobState.Completed,
                EnqueuedAt = now.AddMinutes(-30),
            });

            // Expired but still processing (should NOT be returned)
            await this.store.SetStatusAsync("processing", new BatchCreationJobStatus
            {
                JobId = "processing",
                State = BatchCreationJobState.Processing,
                EnqueuedAt = now.AddHours(-2),
            });

            // Expired but still queued (should NOT be returned)
            await this.store.SetStatusAsync("queued", new BatchCreationJobStatus
            {
                JobId = "queued",
                State = BatchCreationJobState.Queued,
                EnqueuedAt = now.AddHours(-2),
            });

            var expiredJobs = (await this.store.GetExpiredJobIdsAsync(maxAge)).ToList();

            Assert.Equal(3, expiredJobs.Count);
            Assert.Contains("expired1", expiredJobs);
            Assert.Contains("expired2", expiredJobs);
            Assert.Contains("expired3", expiredJobs);
            Assert.DoesNotContain("recent", expiredJobs);
            Assert.DoesNotContain("processing", expiredJobs);
            Assert.DoesNotContain("queued", expiredJobs);
        }

        [Fact]
        public async Task GetExpiredJobIdsAsync_WithNoExpiredJobs_ShouldReturnEmptyList()
        {
            var maxAge = TimeSpan.FromHours(1);

            await this.store.SetStatusAsync("recent1", new BatchCreationJobStatus
            {
                JobId = "recent1",
                State = BatchCreationJobState.Completed,
                EnqueuedAt = DateTime.UtcNow.AddMinutes(-30),
            });

            var expiredJobs = await this.store.GetExpiredJobIdsAsync(maxAge);

            Assert.Empty(expiredJobs);
        }

        [Fact]
        public async Task GetExpiredJobIdsAsync_WithEmptyStore_ShouldReturnEmptyList()
        {
            var expiredJobs = await this.store.GetExpiredJobIdsAsync(TimeSpan.FromHours(1));

            Assert.Empty(expiredJobs);
        }

        [Fact]
        public async Task ConcurrentOperations_ShouldBeThreadSafe()
        {
            var tasks = Enumerable.Range(0, 50).Select(async i =>
            {
                var jobId = $"job_{i}";
                await this.store.SetParametersAsync(jobId, CreateTestParameters($"Scope_{i}"));
                await this.store.SetStatusAsync(jobId, CreateTestStatus(jobId, BatchCreationJobState.Queued));

                // Update status
                await this.store.SetStatusAsync(jobId, CreateTestStatus(jobId, BatchCreationJobState.Processing));

                // Get operations
                var status = await this.store.GetStatusAsync(jobId);
                var parameters = await this.store.GetParametersAsync(jobId);

                Assert.NotNull(status);
                Assert.NotNull(parameters);
            });

            await Task.WhenAll(tasks);

            var count = await this.store.GetCountAsync();
            Assert.Equal(50, count);
        }

        [Fact]
        public async Task ConcurrentRemoveOperations_ShouldBeThreadSafe()
        {
            // Pre-populate store
            for (int i = 0; i < 50; i++)
            {
                var jobId = $"job_{i}";
                await this.store.SetParametersAsync(jobId, CreateTestParameters());
                await this.store.SetStatusAsync(jobId, CreateTestStatus(jobId, BatchCreationJobState.Completed));
            }

            // Concurrent removal
            var tasks = Enumerable.Range(0, 50).Select(i =>
                this.store.RemoveJobAsync($"job_{i}"));

            await Task.WhenAll(tasks);

            var count = await this.store.GetCountAsync();
            Assert.Equal(0, count);
        }

        [Fact]
        public async Task CustomOptions_ShouldUseCustomKeyPrefix()
        {
            var options = new DistributedBatchJobStoreOptions
            {
                KeyPrefix = "custom:prefix",
            };
            var customStore = new DistributedBatchJobStore(this.cache, options);

            await customStore.SetStatusAsync("job1", CreateTestStatus("job1", BatchCreationJobState.Queued));

            // The custom store should be able to retrieve its own data
            var status = await customStore.GetStatusAsync("job1");
            Assert.NotNull(status);

            // The default store (with different prefix) should NOT see this data
            var statusFromDefault = await this.store.GetStatusAsync("job1");
            Assert.Null(statusFromDefault);
        }

        [Fact]
        public async Task NullJobId_ShouldThrowArgumentNullException()
        {
            await Assert.ThrowsAsync<ArgumentNullException>(() =>
                this.store.SetParametersAsync(null, CreateTestParameters()));

            await Assert.ThrowsAsync<ArgumentNullException>(() =>
                this.store.SetStatusAsync(null, CreateTestStatus("test", BatchCreationJobState.Queued)));
        }

        [Fact]
        public async Task EmptyJobId_ShouldThrowArgumentNullException()
        {
            await Assert.ThrowsAsync<ArgumentNullException>(() =>
                this.store.SetParametersAsync("", CreateTestParameters()));

            await Assert.ThrowsAsync<ArgumentNullException>(() =>
                this.store.SetStatusAsync("", CreateTestStatus("test", BatchCreationJobState.Queued)));
        }

        [Fact]
        public async Task StatusWithComplexData_ShouldSerializeCorrectly()
        {
            var jobId = "complex-job";
            var status = new BatchCreationJobStatus
            {
                JobId = jobId,
                State = BatchCreationJobState.Completed,
                EnqueuedAt = DateTime.UtcNow.AddMinutes(-10),
                StartedAt = DateTime.UtcNow.AddMinutes(-9),
                CompletedAt = DateTime.UtcNow,
                ProgressPercentage = 100,
                TotalTables = 5,
                TablesProcessed = 5,
                RemoteClientTimestamp = 123456789,
                ErrorMessage = null,
            };

            await this.store.SetStatusAsync(jobId, status);

            var retrieved = await this.store.GetStatusAsync(jobId);

            Assert.NotNull(retrieved);
            Assert.Equal(jobId, retrieved.JobId);
            Assert.Equal(BatchCreationJobState.Completed, retrieved.State);
            Assert.Equal(100, retrieved.ProgressPercentage);
            Assert.Equal(5, retrieved.TotalTables);
            Assert.Equal(5, retrieved.TablesProcessed);
            Assert.Equal(123456789, retrieved.RemoteClientTimestamp);
            Assert.Null(retrieved.ErrorMessage);
        }

        [Fact]
        public async Task ParametersWithComplexData_ShouldSerializeCorrectly()
        {
            var jobId = "complex-params-job";
            var parameters = new BatchCreationJobParameters
            {
                ScopeName = "ComplexScope",
                BatchDirectory = "/var/batches/complex",
                BatchSize = 5000,
                UseUnifiedBatching = true,
                ConnectionString = "Server=localhost;Database=test;",
                ProviderTypeName = "Wormhole.Sync.SqlServer.SqlSyncProvider",
            };

            await this.store.SetParametersAsync(jobId, parameters);

            var retrieved = await this.store.GetParametersAsync(jobId);

            Assert.NotNull(retrieved);
            Assert.Equal("ComplexScope", retrieved.ScopeName);
            Assert.Equal("/var/batches/complex", retrieved.BatchDirectory);
            Assert.Equal(5000, retrieved.BatchSize);
            Assert.True(retrieved.UseUnifiedBatching);
            Assert.Equal("Server=localhost;Database=test;", retrieved.ConnectionString);
            Assert.Equal("Wormhole.Sync.SqlServer.SqlSyncProvider", retrieved.ProviderTypeName);
        }

        public void Dispose()
        {
            this.stopwatch?.Stop();
            this.Output?.WriteLine($"Test {this.test?.DisplayName} took {this.stopwatch?.Elapsed.ToString() ?? "unknown time"}");
        }
    }
}
