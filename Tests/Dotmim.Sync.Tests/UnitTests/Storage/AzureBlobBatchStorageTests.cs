using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Wormhole.Sync.Web.Azure;
using Xunit;

namespace Wormhole.Sync.Tests.UnitTests.Storage
{
    /// <summary>
    /// Tests for AzureBlobBatchStorage using Azurite (Azure Storage Emulator).
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
    public class AzureBlobBatchStorageTests : IAsyncLifetime
    {
        // Azurite default connection string
        public const string AzuriteConnectionString = "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1;";

        private readonly string containerName;
        private readonly ITestOutputHelper output;
        private AzureBlobBatchStorage storage;
        private bool azuriteAvailable;
        private Stopwatch stopwatch;

        public AzureBlobBatchStorageTests(ITestOutputHelper output)
        {
            this.output = output;
            this.stopwatch = Stopwatch.StartNew();

            // Generate unique container name for test isolation
            this.containerName = $"test-{Guid.NewGuid():N}".Substring(0, 32);
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
                    // Ensure container exists by creating a dummy directory
                    await this.storage.EnsureDirectoryExistsAsync("init");
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

        [SkippableFact]
        public async Task WriteBatchPartAsync_ShouldCreateBlobWithContent()
        {
            SkipIfAzuriteNotAvailable();

            var directoryPath = "write_test";
            var fileName = "batch_001.json";
            var content = "test batch content for azure";
            using var dataStream = new MemoryStream(Encoding.UTF8.GetBytes(content));

            await this.storage.WriteBatchPartAsync(directoryPath, fileName, dataStream);

            // Verify by reading back
            using var readStream = await this.storage.ReadBatchPartAsync(directoryPath, fileName);
            using var reader = new StreamReader(readStream);
            var readContent = await reader.ReadToEndAsync();
            Assert.Equal(content, readContent);
        }

        [SkippableFact]
        public async Task WriteBatchPartAsync_ShouldOverwriteExistingBlob()
        {
            SkipIfAzuriteNotAvailable();

            var directoryPath = "overwrite_test";
            var fileName = "batch.json";

            // Write original content
            using (var dataStream = new MemoryStream(Encoding.UTF8.GetBytes("original content")))
            {
                await this.storage.WriteBatchPartAsync(directoryPath, fileName, dataStream);
            }

            // Overwrite with new content
            using (var dataStream = new MemoryStream(Encoding.UTF8.GetBytes("new content")))
            {
                await this.storage.WriteBatchPartAsync(directoryPath, fileName, dataStream);
            }

            // Verify new content
            using var readStream = await this.storage.ReadBatchPartAsync(directoryPath, fileName);
            using var reader = new StreamReader(readStream);
            var readContent = await reader.ReadToEndAsync();
            Assert.Equal("new content", readContent);
        }

        [SkippableFact]
        public async Task ReadBatchPartAsync_ShouldReturnBlobContent()
        {
            SkipIfAzuriteNotAvailable();

            var directoryPath = "read_test";
            var fileName = "batch.json";
            var content = "batch file content for reading";

            using (var dataStream = new MemoryStream(Encoding.UTF8.GetBytes(content)))
            {
                await this.storage.WriteBatchPartAsync(directoryPath, fileName, dataStream);
            }

            using var stream = await this.storage.ReadBatchPartAsync(directoryPath, fileName);
            using var reader = new StreamReader(stream);
            var result = await reader.ReadToEndAsync();

            Assert.Equal(content, result);
        }

        [SkippableFact]
        public async Task DeleteBatchPartAsync_ShouldDeleteBlob()
        {
            SkipIfAzuriteNotAvailable();

            var directoryPath = "delete_test";
            var fileName = "batch.json";

            using (var dataStream = new MemoryStream(Encoding.UTF8.GetBytes("content")))
            {
                await this.storage.WriteBatchPartAsync(directoryPath, fileName, dataStream);
            }

            Assert.True(await this.storage.FileExistsAsync(directoryPath, fileName));

            await this.storage.DeleteBatchPartAsync(directoryPath, fileName);

            Assert.False(await this.storage.FileExistsAsync(directoryPath, fileName));
        }

        [SkippableFact]
        public async Task DeleteBatchPartAsync_WithNonExistentBlob_ShouldNotThrow()
        {
            SkipIfAzuriteNotAvailable();

            var directoryPath = "delete_nonexistent";

            var exception = await Record.ExceptionAsync(
                () => this.storage.DeleteBatchPartAsync(directoryPath, "nonexistent.json"));

            Assert.Null(exception);
        }

        [SkippableFact]
        public async Task DeleteBatchDirectoryAsync_ShouldDeleteAllBlobsWithPrefix()
        {
            SkipIfAzuriteNotAvailable();

            var directoryPath = "delete_dir_test";

            // Create multiple blobs
            using (var dataStream = new MemoryStream(Encoding.UTF8.GetBytes("content1")))
            {
                await this.storage.WriteBatchPartAsync(directoryPath, "file1.json", dataStream);
            }
            using (var dataStream = new MemoryStream(Encoding.UTF8.GetBytes("content2")))
            {
                await this.storage.WriteBatchPartAsync(directoryPath, "file2.json", dataStream);
            }
            using (var dataStream = new MemoryStream(Encoding.UTF8.GetBytes("content3")))
            {
                await this.storage.WriteBatchPartAsync(directoryPath + "/nested", "file3.json", dataStream);
            }

            await this.storage.DeleteBatchDirectoryAsync(directoryPath);

            Assert.False(await this.storage.FileExistsAsync(directoryPath, "file1.json"));
            Assert.False(await this.storage.FileExistsAsync(directoryPath, "file2.json"));
            Assert.False(await this.storage.FileExistsAsync(directoryPath + "/nested", "file3.json"));
        }

        [SkippableFact]
        public async Task DirectoryExistsAsync_WithExistingBlobs_ShouldReturnTrue()
        {
            SkipIfAzuriteNotAvailable();

            var directoryPath = "exists_test";

            using (var dataStream = new MemoryStream(Encoding.UTF8.GetBytes("content")))
            {
                await this.storage.WriteBatchPartAsync(directoryPath, "file.json", dataStream);
            }

            var result = await this.storage.DirectoryExistsAsync(directoryPath);

            Assert.True(result);
        }

        [SkippableFact]
        public async Task DirectoryExistsAsync_WithNoBlobs_ShouldReturnFalse()
        {
            SkipIfAzuriteNotAvailable();

            var directoryPath = "nonexistent_dir_" + Guid.NewGuid().ToString("N");

            var result = await this.storage.DirectoryExistsAsync(directoryPath);

            Assert.False(result);
        }

        [SkippableFact]
        public async Task GetFilesAsync_ShouldReturnAllFilesInDirectory()
        {
            SkipIfAzuriteNotAvailable();

            var directoryPath = "list_files_test";

            using (var dataStream = new MemoryStream(Encoding.UTF8.GetBytes("content1")))
            {
                await this.storage.WriteBatchPartAsync(directoryPath, "batch1.json", dataStream);
            }
            using (var dataStream = new MemoryStream(Encoding.UTF8.GetBytes("content2")))
            {
                await this.storage.WriteBatchPartAsync(directoryPath, "batch2.json", dataStream);
            }
            using (var dataStream = new MemoryStream(Encoding.UTF8.GetBytes("content3")))
            {
                await this.storage.WriteBatchPartAsync(directoryPath, "batch3.txt", dataStream);
            }

            var files = (await this.storage.GetFilesAsync(directoryPath)).ToList();

            Assert.Equal(3, files.Count);
            Assert.Contains("batch1.json", files);
            Assert.Contains("batch2.json", files);
            Assert.Contains("batch3.txt", files);
        }

        [SkippableFact]
        public async Task GetFilesAsync_WithSearchPattern_ShouldFilterFiles()
        {
            SkipIfAzuriteNotAvailable();

            var directoryPath = "list_filtered_test";

            using (var dataStream = new MemoryStream(Encoding.UTF8.GetBytes("content1")))
            {
                await this.storage.WriteBatchPartAsync(directoryPath, "batch1.json", dataStream);
            }
            using (var dataStream = new MemoryStream(Encoding.UTF8.GetBytes("content2")))
            {
                await this.storage.WriteBatchPartAsync(directoryPath, "batch2.json", dataStream);
            }
            using (var dataStream = new MemoryStream(Encoding.UTF8.GetBytes("content3")))
            {
                await this.storage.WriteBatchPartAsync(directoryPath, "batch3.txt", dataStream);
            }

            var files = (await this.storage.GetFilesAsync(directoryPath, "*.json")).ToList();

            Assert.Equal(2, files.Count);
            Assert.Contains("batch1.json", files);
            Assert.Contains("batch2.json", files);
            Assert.DoesNotContain("batch3.txt", files);
        }

        [SkippableFact]
        public async Task GetFileSizeAsync_ShouldReturnCorrectSize()
        {
            SkipIfAzuriteNotAvailable();

            var directoryPath = "file_size_test";
            var fileName = "batch.json";
            var content = "test content with known size";
            var expectedSize = Encoding.UTF8.GetByteCount(content);

            using (var dataStream = new MemoryStream(Encoding.UTF8.GetBytes(content)))
            {
                await this.storage.WriteBatchPartAsync(directoryPath, fileName, dataStream);
            }

            var size = await this.storage.GetFileSizeAsync(directoryPath, fileName);

            Assert.Equal(expectedSize, size);
        }

        [SkippableFact]
        public async Task GetFileSizeAsync_WithNonExistentBlob_ShouldReturnZero()
        {
            SkipIfAzuriteNotAvailable();

            var directoryPath = "file_size_nonexistent";

            var size = await this.storage.GetFileSizeAsync(directoryPath, "nonexistent.json");

            Assert.Equal(0L, size);
        }

        [SkippableFact]
        public async Task FileExistsAsync_WithExistingBlob_ShouldReturnTrue()
        {
            SkipIfAzuriteNotAvailable();

            var directoryPath = "file_exists_test";
            var fileName = "batch.json";

            using (var dataStream = new MemoryStream(Encoding.UTF8.GetBytes("content")))
            {
                await this.storage.WriteBatchPartAsync(directoryPath, fileName, dataStream);
            }

            var result = await this.storage.FileExistsAsync(directoryPath, fileName);

            Assert.True(result);
        }

        [SkippableFact]
        public async Task FileExistsAsync_WithNonExistentBlob_ShouldReturnFalse()
        {
            SkipIfAzuriteNotAvailable();

            var directoryPath = "file_exists_nonexistent";

            var result = await this.storage.FileExistsAsync(directoryPath, "nonexistent.json");

            Assert.False(result);
        }

        [SkippableFact]
        public async Task RoundTrip_WriteThenRead_ShouldPreserveContent()
        {
            SkipIfAzuriteNotAvailable();

            var directoryPath = "roundtrip_test";
            var fileName = "batch.json";
            var originalContent = "{ \"data\": \"test batch content with special chars: éàü\" }";

            using (var writeStream = new MemoryStream(Encoding.UTF8.GetBytes(originalContent)))
            {
                await this.storage.WriteBatchPartAsync(directoryPath, fileName, writeStream);
            }

            using var readStream = await this.storage.ReadBatchPartAsync(directoryPath, fileName);
            using var reader = new StreamReader(readStream, Encoding.UTF8);
            var readContent = await reader.ReadToEndAsync();

            Assert.Equal(originalContent, readContent);
        }

        [SkippableFact]
        public async Task PathNormalization_WindowsStylePaths_ShouldWork()
        {
            SkipIfAzuriteNotAvailable();

            // Use Windows-style path separators
            var directoryPath = "path\\normalization\\test";
            var fileName = "batch.json";
            var content = "path normalization test";

            using (var dataStream = new MemoryStream(Encoding.UTF8.GetBytes(content)))
            {
                await this.storage.WriteBatchPartAsync(directoryPath, fileName, dataStream);
            }

            // Read back using forward slashes
            using var readStream = await this.storage.ReadBatchPartAsync("path/normalization/test", fileName);
            using var reader = new StreamReader(readStream);
            var readContent = await reader.ReadToEndAsync();

            Assert.Equal(content, readContent);
        }

        [SkippableFact]
        public async Task LargeBatch_ShouldHandleLargeContent()
        {
            SkipIfAzuriteNotAvailable();

            var directoryPath = "large_batch_test";
            var fileName = "large_batch.json";

            // Create 1MB of content
            var largeContent = new string('x', 1024 * 1024);

            using (var dataStream = new MemoryStream(Encoding.UTF8.GetBytes(largeContent)))
            {
                await this.storage.WriteBatchPartAsync(directoryPath, fileName, dataStream);
            }

            using var readStream = await this.storage.ReadBatchPartAsync(directoryPath, fileName);
            using var reader = new StreamReader(readStream);
            var readContent = await reader.ReadToEndAsync();

            Assert.Equal(largeContent.Length, readContent.Length);
        }

        [SkippableFact]
        public async Task GetSubdirectoriesAsync_ShouldReturnVirtualDirectories()
        {
            SkipIfAzuriteNotAvailable();

            var rootPath = "subdir_test";

            // Create files in different virtual directories
            using (var dataStream = new MemoryStream(Encoding.UTF8.GetBytes("content1")))
            {
                await this.storage.WriteBatchPartAsync($"{rootPath}/batch1", "file.json", dataStream);
            }
            using (var dataStream = new MemoryStream(Encoding.UTF8.GetBytes("content2")))
            {
                await this.storage.WriteBatchPartAsync($"{rootPath}/batch2", "file.json", dataStream);
            }
            using (var dataStream = new MemoryStream(Encoding.UTF8.GetBytes("content3")))
            {
                await this.storage.WriteBatchPartAsync($"{rootPath}/batch3", "file.json", dataStream);
            }

            var subdirs = (await this.storage.GetSubdirectoriesAsync(rootPath)).ToList();

            Assert.Equal(3, subdirs.Count);
            Assert.Contains("batch1", subdirs);
            Assert.Contains("batch2", subdirs);
            Assert.Contains("batch3", subdirs);
        }

        [SkippableFact]
        public async Task GetSubdirectoriesAsync_WithEmptyPrefix_ShouldReturnEmptyList()
        {
            SkipIfAzuriteNotAvailable();

            var rootPath = "nonexistent_subdir_test_" + Guid.NewGuid().ToString("N");

            var subdirs = await this.storage.GetSubdirectoriesAsync(rootPath);

            Assert.Empty(subdirs);
        }

        [SkippableFact]
        public async Task GetSubdirectoriesAsync_ShouldNotIncludeFilesAtRootLevel()
        {
            SkipIfAzuriteNotAvailable();

            var rootPath = "subdir_with_files_test";

            // Create a file at the root level (no subdirectory)
            using (var dataStream = new MemoryStream(Encoding.UTF8.GetBytes("root content")))
            {
                await this.storage.WriteBatchPartAsync(rootPath, "file.json", dataStream);
            }
            // Create file in subdirectory
            using (var dataStream = new MemoryStream(Encoding.UTF8.GetBytes("subdir content")))
            {
                await this.storage.WriteBatchPartAsync($"{rootPath}/subdir1", "nested.json", dataStream);
            }

            var subdirs = (await this.storage.GetSubdirectoriesAsync(rootPath)).ToList();

            Assert.Single(subdirs);
            Assert.Contains("subdir1", subdirs);
        }

        [SkippableFact]
        public async Task GetSubdirectoriesAsync_WithTimestampDirectoryNames_ShouldReturnCorrectly()
        {
            SkipIfAzuriteNotAvailable();

            var rootPath = "timestamp_dirs_test";

            // Create directories with timestamp-like names (as used by batch cleanup)
            using (var dataStream = new MemoryStream(Encoding.UTF8.GetBytes("content1")))
            {
                await this.storage.WriteBatchPartAsync($"{rootPath}/202401011200_batch1", "file.json", dataStream);
            }
            using (var dataStream = new MemoryStream(Encoding.UTF8.GetBytes("content2")))
            {
                await this.storage.WriteBatchPartAsync($"{rootPath}/202401021200_batch2", "file.json", dataStream);
            }

            var subdirs = (await this.storage.GetSubdirectoriesAsync(rootPath)).ToList();

            Assert.Equal(2, subdirs.Count);
            Assert.Contains("202401011200_batch1", subdirs);
            Assert.Contains("202401021200_batch2", subdirs);
        }
    }

    /// <summary>
    /// Custom exception for skipping tests when Azurite is not available.
    /// </summary>
    public class SkipException : Exception
    {
        public SkipException(string message) : base(message) { }
    }

    /// <summary>
    /// Attribute for tests that can be skipped.
    /// </summary>
    public class SkippableFactAttribute : FactAttribute
    {
    }
}
