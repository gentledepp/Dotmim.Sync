using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Wormhole.Sync.Tests.UnitTests.Storage;
using Wormhole.Sync.Web.Azure;
using Xunit;


namespace Wormhole.Sync.Tests.UnitTests
{
    /// <summary>
    /// Tests for BatchCleanupService using Azure Blob Storage (Azurite).
    ///
    /// These tests require Azurite to be running locally. To start Azurite:
    /// - Docker: docker run -p 10000:10000 -p 10001:10001 -p 10002:10002 mcr.microsoft.com/azure-storage/azurite azurite --skipApiVersionCheck
    /// - npm: npx azurite --silent --skipApiVersionCheck --location ./azurite --debug ./azurite/debug.log
    ///
    /// Note: The --skipApiVersionCheck flag is required when using newer Azure SDK versions
    /// that may use API versions not yet supported by Azurite.
    ///
    /// Tests are marked with [Trait("Category", "Azurite")] so they can be skipped
    /// in environments where Azurite is not available.
    /// </summary>
    [Trait("Category", "Azurite")]
    public class BatchCleanupServiceAzureTests : IAsyncLifetime
    {
        // Azurite default connection string
        private const string AzuriteConnectionString = "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1;";

        private readonly string containerName;
        private readonly string batchDirectory;
        private readonly ITestOutputHelper output;
        private AzureBlobBatchStorage storage;
        private BatchCleanupService batchCleanupService;
        private bool azuriteAvailable;
        private Stopwatch stopwatch;

        public BatchCleanupServiceAzureTests(ITestOutputHelper output)
        {
            this.output = output;
            this.stopwatch = Stopwatch.StartNew();

            // Generate unique container name for test isolation
            this.containerName = $"test-{Guid.NewGuid():N}".Substring(0, 32);
            this.batchDirectory = "batches";
        }

        public async ValueTask InitializeAsync()
        {
            // Check if Azurite is available
            this.azuriteAvailable = await IsAzuriteAvailableAsync();

            if (this.azuriteAvailable)
            {
                try
                {
                    this.storage = new AzureBlobBatchStorage(AzuriteConnectionString, this.containerName);
                    this.batchCleanupService = new BatchCleanupService(this.storage);
                    // Ensure container exists
                    await this.storage.EnsureDirectoryExistsAsync(this.batchDirectory);
                }
                catch (Exception ex)
                {
                    this.output.WriteLine($"Failed to initialize Azure Blob Storage: {ex.Message}");
                    this.azuriteAvailable = false;
                }
            }
            else
            {
                this.output.WriteLine("Azurite is not available. Skipping Azure Blob Storage tests.");
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (this.azuriteAvailable && this.storage != null)
            {
                try
                {
                    // Clean up by deleting all test data
                    await this.storage.DeleteBatchDirectoryAsync("");
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }

            this.stopwatch?.Stop();
            this.output?.WriteLine($"Test took {this.stopwatch?.Elapsed.ToString() ?? "unknown time"}");
        }

        private async Task<bool> IsAzuriteAvailableAsync()
        {
            try
            {
                // Simple TCP connection check to see if Azurite is listening
                using var client = new TcpClient();
                var connectTask = client.ConnectAsync("127.0.0.1", 10000);
                var timeoutTask = Task.Delay(2000);

                var completedTask = await Task.WhenAny(connectTask, timeoutTask);
                return completedTask == connectTask && client.Connected;
            }
            catch
            {
                return false;
            }
        }

        private void SkipIfAzuriteNotAvailable()
        {
            if (!this.azuriteAvailable)
            {
                throw new SkipException("Azurite is not available. Start Azurite to run this test.");
            }
        }

        private async Task<string> CreateTestBatchDirectoryAsync(DateTime timestamp, string customName = null)
        {
            var directoryName = customName ?? timestamp.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture) + "_test";
            // Use Path.Combine to match the path format returned by BatchCleanupService
            var fullPath = Path.Combine(this.batchDirectory, directoryName);

            // Create a file to make the directory "exist" in blob storage
            using var dataStream = new MemoryStream(Encoding.UTF8.GetBytes("batch content"));
            await this.storage.WriteBatchPartAsync(fullPath, "batch.json", dataStream);

            return fullPath;
        }

        [SkippableFact]
        public async Task CleanupExpiredBatchesAsync_WithExpiredBatches_ShouldCleanupCorrectly()
        {
            SkipIfAzuriteNotAvailable();

            var retentionPeriod = TimeSpan.FromHours(1);

            var expiredDir1 = await CreateTestBatchDirectoryAsync(DateTime.UtcNow.AddHours(-2));
            var expiredDir2 = await CreateTestBatchDirectoryAsync(DateTime.UtcNow.AddHours(-3));
            var validDir = await CreateTestBatchDirectoryAsync(DateTime.UtcNow.AddMinutes(-30));

            var result = await this.batchCleanupService.CleanupExpiredBatchesAsync(this.batchDirectory, retentionPeriod);

            Assert.Equal(2, result);
            Assert.False(await this.storage.DirectoryExistsAsync(expiredDir1));
            Assert.False(await this.storage.DirectoryExistsAsync(expiredDir2));
            Assert.True(await this.storage.DirectoryExistsAsync(validDir));
        }

        [SkippableFact]
        public async Task CleanupExpiredBatchesAsync_WithNoExpiredBatches_ShouldReturnZero()
        {
            SkipIfAzuriteNotAvailable();

            var retentionPeriod = TimeSpan.FromHours(1);

            await CreateTestBatchDirectoryAsync(DateTime.UtcNow.AddMinutes(-30));
            await CreateTestBatchDirectoryAsync(DateTime.UtcNow.AddMinutes(-45));

            var result = await this.batchCleanupService.CleanupExpiredBatchesAsync(this.batchDirectory, retentionPeriod);

            Assert.Equal(0, result);
        }

        [SkippableFact]
        public async Task CleanupExpiredBatchesAsync_WithZeroRetentionPeriod_ShouldReturnZero()
        {
            SkipIfAzuriteNotAvailable();

            await CreateTestBatchDirectoryAsync(DateTime.UtcNow.AddHours(-2));

            var result = await this.batchCleanupService.CleanupExpiredBatchesAsync(this.batchDirectory, TimeSpan.Zero);

            Assert.Equal(0, result);
        }

        [SkippableFact]
        public async Task CleanupExpiredBatchesAsync_WithNonExistentDirectory_ShouldReturnZero()
        {
            SkipIfAzuriteNotAvailable();

            var nonExistentPath = "nonexistent_" + Guid.NewGuid().ToString("N");
            var retentionPeriod = TimeSpan.FromHours(1);

            var result = await this.batchCleanupService.CleanupExpiredBatchesAsync(nonExistentPath, retentionPeriod);

            Assert.Equal(0, result);
        }

        [SkippableFact]
        public async Task CleanupExpiredBatchesAsync_WithInvalidDirectoryNames_ShouldIgnoreInvalidOnes()
        {
            SkipIfAzuriteNotAvailable();

            var retentionPeriod = TimeSpan.FromHours(1);

            var invalidDir1 = Path.Combine(this.batchDirectory, "invalid_name");
            var invalidDir2 = Path.Combine(this.batchDirectory, "20240101abc");

            // Create invalid directories
            using (var dataStream = new MemoryStream(Encoding.UTF8.GetBytes("content")))
            {
                await this.storage.WriteBatchPartAsync(invalidDir1, "file.json", dataStream);
            }
            using (var dataStream = new MemoryStream(Encoding.UTF8.GetBytes("content")))
            {
                await this.storage.WriteBatchPartAsync(invalidDir2, "file.json", dataStream);
            }

            // Create valid expired directory
            var expiredDir = await CreateTestBatchDirectoryAsync(DateTime.UtcNow.AddHours(-2));

            var result = await this.batchCleanupService.CleanupExpiredBatchesAsync(this.batchDirectory, retentionPeriod);

            Assert.Equal(1, result);
            Assert.True(await this.storage.DirectoryExistsAsync(invalidDir1));
            Assert.True(await this.storage.DirectoryExistsAsync(invalidDir2));
            Assert.False(await this.storage.DirectoryExistsAsync(expiredDir));
        }

        [SkippableFact]
        public async Task GetExpiredBatchDirectoriesAsync_WithExpiredBatches_ShouldReturnCorrectList()
        {
            SkipIfAzuriteNotAvailable();

            var retentionPeriod = TimeSpan.FromHours(1);

            var expiredDir1 = await CreateTestBatchDirectoryAsync(DateTime.UtcNow.AddHours(-2));
            var expiredDir2 = await CreateTestBatchDirectoryAsync(DateTime.UtcNow.AddHours(-3));
            var validDir = await CreateTestBatchDirectoryAsync(DateTime.UtcNow.AddMinutes(-30));

            var result = await this.batchCleanupService.GetExpiredBatchDirectoriesAsync(this.batchDirectory, retentionPeriod);

            Assert.Equal(2, result.Count);
            Assert.Contains(expiredDir1, result);
            Assert.Contains(expiredDir2, result);
            Assert.DoesNotContain(validDir, result);
        }

        [SkippableFact]
        public async Task GetExpiredBatchDirectoriesAsync_WithNoExpiredBatches_ShouldReturnEmptyList()
        {
            SkipIfAzuriteNotAvailable();

            var retentionPeriod = TimeSpan.FromHours(1);

            await CreateTestBatchDirectoryAsync(DateTime.UtcNow.AddMinutes(-30));
            await CreateTestBatchDirectoryAsync(DateTime.UtcNow.AddMinutes(-45));

            var result = await this.batchCleanupService.GetExpiredBatchDirectoriesAsync(this.batchDirectory, retentionPeriod);

            Assert.Empty(result);
        }

        [SkippableFact]
        public async Task GetExpiredBatchDirectoriesAsync_ShouldReturnDirectoriesInOrder()
        {
            SkipIfAzuriteNotAvailable();

            var retentionPeriod = TimeSpan.FromHours(1);

            var expiredDir1 = await CreateTestBatchDirectoryAsync(DateTime.UtcNow.AddHours(-5), "202401011200_test1");
            var expiredDir2 = await CreateTestBatchDirectoryAsync(DateTime.UtcNow.AddHours(-3), "202401011000_test2");
            var expiredDir3 = await CreateTestBatchDirectoryAsync(DateTime.UtcNow.AddHours(-4), "202401011100_test3");

            var result = await this.batchCleanupService.GetExpiredBatchDirectoriesAsync(this.batchDirectory, retentionPeriod);

            Assert.Equal(3, result.Count);
            Assert.Equal(Path.GetFileName(expiredDir2), Path.GetFileName(result[0]));
            Assert.Equal(Path.GetFileName(expiredDir3), Path.GetFileName(result[1]));
            Assert.Equal(Path.GetFileName(expiredDir1), Path.GetFileName(result[2]));
        }

        [SkippableFact]
        public async Task CleanupExpiredBatchesAsync_WithCancellationToken_ShouldRespectCancellation()
        {
            SkipIfAzuriteNotAvailable();

            var retentionPeriod = TimeSpan.FromHours(1);
            await CreateTestBatchDirectoryAsync(DateTime.UtcNow.AddHours(-2));

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAsync<TaskCanceledException>(
                () => this.batchCleanupService.CleanupExpiredBatchesAsync(this.batchDirectory, retentionPeriod, cts.Token));
        }

        [SkippableFact]
        public async Task CleanupExpiredBatchesAsync_WithSyncOptions_ShouldUseOptionsValues()
        {
            SkipIfAzuriteNotAvailable();

            var retentionPeriod = TimeSpan.FromHours(1);
            var options = new SyncOptions
            {
                BatchDirectory = this.batchDirectory,
                BatchRetentionPeriod = retentionPeriod
            };

            await CreateTestBatchDirectoryAsync(DateTime.UtcNow.AddHours(-2));

            var result = await this.batchCleanupService.CleanupExpiredBatchesAsync(options);

            Assert.Equal(1, result);
        }
    }
}
