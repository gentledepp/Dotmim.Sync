using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Wormhole.Sync.Async;
using Wormhole.Sync.Web.Server.Async;
using Xunit;


namespace Wormhole.Sync.Tests.UnitTests.AsyncBatchCreation
{
    public class InMemoryBatchJobStoreTests : IDisposable
    {
        private Stopwatch stopwatch;
        private readonly InMemoryBatchJobStore store;

        public ITestOutputHelper Output { get; }

        public InMemoryBatchJobStoreTests(ITestOutputHelper output)
        {
            this.Output = output;
            var type = output.GetType();
            this.stopwatch = Stopwatch.StartNew();

            this.store = new InMemoryBatchJobStore();
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
        public void SetParameters_ShouldStoreParameters()
        {
            var jobId = "job1";
            var parameters = CreateTestParameters("MyScope");

            this.store.SetParameters(jobId, parameters);

            var stored = this.store.GetParameters(jobId);
            Assert.NotNull(stored);
            Assert.Equal("MyScope", stored.ScopeName);
        }

        [Fact]
        public void GetParameters_WithNonExistentJob_ShouldReturnNull()
        {
            var result = this.store.GetParameters("nonexistent");

            Assert.Null(result);
        }

        [Fact]
        public void SetParameters_ShouldOverwriteExisting()
        {
            var jobId = "job1";
            this.store.SetParameters(jobId, CreateTestParameters("Scope1"));
            this.store.SetParameters(jobId, CreateTestParameters("Scope2"));

            var stored = this.store.GetParameters(jobId);
            Assert.Equal("Scope2", stored.ScopeName);
        }

        [Fact]
        public void SetStatus_ShouldStoreStatus()
        {
            var jobId = "job1";
            var status = CreateTestStatus(jobId, BatchCreationJobState.Processing);

            this.store.SetStatus(jobId, status);

            var stored = this.store.GetStatus(jobId);
            Assert.NotNull(stored);
            Assert.Equal(jobId, stored.JobId);
            Assert.Equal(BatchCreationJobState.Processing, stored.State);
        }

        [Fact]
        public void GetStatus_WithNonExistentJob_ShouldReturnNull()
        {
            var result = this.store.GetStatus("nonexistent");

            Assert.Null(result);
        }

        [Fact]
        public void SetStatus_ShouldOverwriteExisting()
        {
            var jobId = "job1";
            this.store.SetStatus(jobId, CreateTestStatus(jobId, BatchCreationJobState.Queued));
            this.store.SetStatus(jobId, CreateTestStatus(jobId, BatchCreationJobState.Completed));

            var stored = this.store.GetStatus(jobId);
            Assert.Equal(BatchCreationJobState.Completed, stored.State);
        }

        [Fact]
        public void RemoveJob_ShouldRemoveBothStatusAndParameters()
        {
            var jobId = "job1";
            this.store.SetParameters(jobId, CreateTestParameters());
            this.store.SetStatus(jobId, CreateTestStatus(jobId, BatchCreationJobState.Queued));

            this.store.RemoveJob(jobId);

            Assert.Null(this.store.GetParameters(jobId));
            Assert.Null(this.store.GetStatus(jobId));
        }

        [Fact]
        public void RemoveJob_WithNonExistentJob_ShouldNotThrow()
        {
            var exception = Record.Exception(() => this.store.RemoveJob("nonexistent"));

            Assert.Null(exception);
        }

        [Fact]
        public void Count_ShouldReturnNumberOfJobs()
        {
            this.store.SetStatus("job1", CreateTestStatus("job1", BatchCreationJobState.Queued));
            this.store.SetStatus("job2", CreateTestStatus("job2", BatchCreationJobState.Queued));
            this.store.SetStatus("job3", CreateTestStatus("job3", BatchCreationJobState.Queued));

            Assert.Equal(3, this.store.Count);
        }

        [Fact]
        public void Count_AfterRemove_ShouldDecrement()
        {
            this.store.SetStatus("job1", CreateTestStatus("job1", BatchCreationJobState.Queued));
            this.store.SetStatus("job2", CreateTestStatus("job2", BatchCreationJobState.Queued));

            this.store.RemoveJob("job1");

            Assert.Equal(1, this.store.Count);
        }

        [Fact]
        public void GetExpiredJobIds_ShouldReturnOnlyExpiredCompletedJobs()
        {
            var maxAge = TimeSpan.FromHours(1);
            var now = DateTime.UtcNow;

            // Expired completed job (should be returned)
            this.store.SetStatus("expired1", new BatchCreationJobStatus
            {
                JobId = "expired1",
                State = BatchCreationJobState.Completed,
                EnqueuedAt = now.AddHours(-2),
            });

            // Expired failed job (should be returned)
            this.store.SetStatus("expired2", new BatchCreationJobStatus
            {
                JobId = "expired2",
                State = BatchCreationJobState.Failed,
                EnqueuedAt = now.AddHours(-2),
            });

            // Expired cancelled job (should be returned)
            this.store.SetStatus("expired3", new BatchCreationJobStatus
            {
                JobId = "expired3",
                State = BatchCreationJobState.Cancelled,
                EnqueuedAt = now.AddHours(-2),
            });

            // Not expired completed job (should NOT be returned)
            this.store.SetStatus("recent", new BatchCreationJobStatus
            {
                JobId = "recent",
                State = BatchCreationJobState.Completed,
                EnqueuedAt = now.AddMinutes(-30),
            });

            // Expired but still processing (should NOT be returned)
            this.store.SetStatus("processing", new BatchCreationJobStatus
            {
                JobId = "processing",
                State = BatchCreationJobState.Processing,
                EnqueuedAt = now.AddHours(-2),
            });

            // Expired but still queued (should NOT be returned)
            this.store.SetStatus("queued", new BatchCreationJobStatus
            {
                JobId = "queued",
                State = BatchCreationJobState.Queued,
                EnqueuedAt = now.AddHours(-2),
            });

            var expiredJobs = this.store.GetExpiredJobIds(maxAge).ToList();

            Assert.Equal(3, expiredJobs.Count);
            Assert.Contains("expired1", expiredJobs);
            Assert.Contains("expired2", expiredJobs);
            Assert.Contains("expired3", expiredJobs);
            Assert.DoesNotContain("recent", expiredJobs);
            Assert.DoesNotContain("processing", expiredJobs);
            Assert.DoesNotContain("queued", expiredJobs);
        }

        [Fact]
        public void GetExpiredJobIds_WithNoExpiredJobs_ShouldReturnEmptyList()
        {
            var maxAge = TimeSpan.FromHours(1);

            this.store.SetStatus("recent1", new BatchCreationJobStatus
            {
                JobId = "recent1",
                State = BatchCreationJobState.Completed,
                EnqueuedAt = DateTime.UtcNow.AddMinutes(-30),
            });

            var expiredJobs = this.store.GetExpiredJobIds(maxAge);

            Assert.Empty(expiredJobs);
        }

        [Fact]
        public void GetExpiredJobIds_WithEmptyStore_ShouldReturnEmptyList()
        {
            var expiredJobs = this.store.GetExpiredJobIds(TimeSpan.FromHours(1));

            Assert.Empty(expiredJobs);
        }

        [Fact]
        public void ConcurrentOperations_ShouldBeThreadSafe()
        {
            var tasks = Enumerable.Range(0, 100).Select(i =>
            {
                return Task.Run(() =>
                {
                    var jobId = $"job_{i}";
                    this.store.SetParameters(jobId, CreateTestParameters($"Scope_{i}"));
                    this.store.SetStatus(jobId, CreateTestStatus(jobId, BatchCreationJobState.Queued));

                    // Update status
                    this.store.SetStatus(jobId, CreateTestStatus(jobId, BatchCreationJobState.Processing));

                    // Get operations
                    var status = this.store.GetStatus(jobId);
                    var parameters = this.store.GetParameters(jobId);

                    Assert.NotNull(status);
                    Assert.NotNull(parameters);
                });
            });

            Task.WaitAll(tasks.ToArray());

            Assert.Equal(100, this.store.Count);
        }

        [Fact]
        public void ConcurrentRemoveOperations_ShouldBeThreadSafe()
        {
            // Pre-populate store
            for (int i = 0; i < 100; i++)
            {
                var jobId = $"job_{i}";
                this.store.SetParameters(jobId, CreateTestParameters());
                this.store.SetStatus(jobId, CreateTestStatus(jobId, BatchCreationJobState.Completed));
            }

            // Concurrent removal
            var tasks = Enumerable.Range(0, 100).Select(i =>
            {
                return Task.Run(() => this.store.RemoveJob($"job_{i}"));
            });

            Task.WaitAll(tasks.ToArray());

            Assert.Equal(0, this.store.Count);
        }

        public void Dispose()
        {
            this.stopwatch?.Stop();
            this.Output?.WriteLine($"Test took {this.stopwatch?.Elapsed.ToString() ?? "unknown time"}");
        }
    }
}
