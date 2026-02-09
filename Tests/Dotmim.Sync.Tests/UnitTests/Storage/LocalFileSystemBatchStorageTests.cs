using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Wormhole.Sync.Storage;
using Xunit;


namespace Wormhole.Sync.Tests.UnitTests.Storage
{
    public class LocalFileSystemBatchStorageTests : IDisposable
    {
        private Stopwatch stopwatch;
        private readonly string tempDirectory;
        private readonly LocalFileSystemBatchStorage storage;

        public ITestOutputHelper Output { get; }

        public LocalFileSystemBatchStorageTests(ITestOutputHelper output)
        {
            this.Output = output;
            var type = output.GetType();
            this.stopwatch = Stopwatch.StartNew();

            this.tempDirectory = Path.Combine(Path.GetTempPath(), $"BatchStorageTest_{Guid.NewGuid():N}");
            Directory.CreateDirectory(this.tempDirectory);

            this.storage = new LocalFileSystemBatchStorage();
        }

        [Fact]
        public async Task WriteBatchPartAsync_ShouldCreateFileWithContent()
        {
            var directoryPath = Path.Combine(this.tempDirectory, "write_test");
            var fileName = "batch_001.json";
            var content = "test batch content";
            using var dataStream = new MemoryStream(Encoding.UTF8.GetBytes(content));

            await this.storage.WriteBatchPartAsync(directoryPath, fileName, dataStream);

            var fullPath = Path.Combine(directoryPath, fileName);
            Assert.True(File.Exists(fullPath));
            Assert.Equal(content, await File.ReadAllTextAsync(fullPath));
        }

        [Fact]
        public async Task WriteBatchPartAsync_ShouldCreateDirectoryIfNotExists()
        {
            var directoryPath = Path.Combine(this.tempDirectory, "new_dir", "nested");
            var fileName = "batch.json";
            using var dataStream = new MemoryStream(Encoding.UTF8.GetBytes("content"));

            await this.storage.WriteBatchPartAsync(directoryPath, fileName, dataStream);

            Assert.True(Directory.Exists(directoryPath));
            Assert.True(File.Exists(Path.Combine(directoryPath, fileName)));
        }

        [Fact]
        public async Task WriteBatchPartAsync_ShouldOverwriteExistingFile()
        {
            var directoryPath = Path.Combine(this.tempDirectory, "overwrite_test");
            Directory.CreateDirectory(directoryPath);
            var fileName = "batch.json";
            var fullPath = Path.Combine(directoryPath, fileName);
            await File.WriteAllTextAsync(fullPath, "original content");

            using var dataStream = new MemoryStream(Encoding.UTF8.GetBytes("new content"));
            await this.storage.WriteBatchPartAsync(directoryPath, fileName, dataStream);

            Assert.Equal("new content", await File.ReadAllTextAsync(fullPath));
        }

        [Fact]
        public async Task ReadBatchPartAsync_ShouldReturnFileContent()
        {
            var directoryPath = Path.Combine(this.tempDirectory, "read_test");
            Directory.CreateDirectory(directoryPath);
            var fileName = "batch.json";
            var content = "batch file content";
            await File.WriteAllTextAsync(Path.Combine(directoryPath, fileName), content);

            using var stream = await this.storage.ReadBatchPartAsync(directoryPath, fileName);
            using var reader = new StreamReader(stream);
            var result = await reader.ReadToEndAsync();

            Assert.Equal(content, result);
        }

        [Fact]
        public async Task ReadBatchPartAsync_WithNonExistentFile_ShouldThrowFileNotFoundException()
        {
            var directoryPath = Path.Combine(this.tempDirectory, "read_nonexistent");
            Directory.CreateDirectory(directoryPath);

            await Assert.ThrowsAsync<FileNotFoundException>(
                () => this.storage.ReadBatchPartAsync(directoryPath, "nonexistent.json"));
        }

        [Fact]
        public async Task DeleteBatchPartAsync_ShouldDeleteFile()
        {
            var directoryPath = Path.Combine(this.tempDirectory, "delete_test");
            Directory.CreateDirectory(directoryPath);
            var fileName = "batch.json";
            var fullPath = Path.Combine(directoryPath, fileName);
            await File.WriteAllTextAsync(fullPath, "content");
            Assert.True(File.Exists(fullPath));

            await this.storage.DeleteBatchPartAsync(directoryPath, fileName);

            Assert.False(File.Exists(fullPath));
        }

        [Fact]
        public async Task DeleteBatchPartAsync_WithNonExistentFile_ShouldNotThrow()
        {
            var directoryPath = Path.Combine(this.tempDirectory, "delete_nonexistent");
            Directory.CreateDirectory(directoryPath);

            var exception = await Record.ExceptionAsync(
                () => this.storage.DeleteBatchPartAsync(directoryPath, "nonexistent.json"));

            Assert.Null(exception);
        }

        [Fact]
        public async Task DeleteBatchDirectoryAsync_ShouldDeleteDirectoryAndContents()
        {
            var directoryPath = Path.Combine(this.tempDirectory, "delete_dir_test");
            Directory.CreateDirectory(directoryPath);
            await File.WriteAllTextAsync(Path.Combine(directoryPath, "file1.json"), "content1");
            await File.WriteAllTextAsync(Path.Combine(directoryPath, "file2.json"), "content2");
            var nestedDir = Path.Combine(directoryPath, "nested");
            Directory.CreateDirectory(nestedDir);
            await File.WriteAllTextAsync(Path.Combine(nestedDir, "file3.json"), "content3");

            await this.storage.DeleteBatchDirectoryAsync(directoryPath);

            Assert.False(Directory.Exists(directoryPath));
        }

        [Fact]
        public async Task DeleteBatchDirectoryAsync_WithNonExistentDirectory_ShouldNotThrow()
        {
            var directoryPath = Path.Combine(this.tempDirectory, "nonexistent_dir");

            var exception = await Record.ExceptionAsync(
                () => this.storage.DeleteBatchDirectoryAsync(directoryPath));

            Assert.Null(exception);
        }

        [Fact]
        public async Task DirectoryExistsAsync_WithExistingDirectory_ShouldReturnTrue()
        {
            var directoryPath = Path.Combine(this.tempDirectory, "exists_test");
            Directory.CreateDirectory(directoryPath);

            var result = await this.storage.DirectoryExistsAsync(directoryPath);

            Assert.True(result);
        }

        [Fact]
        public async Task DirectoryExistsAsync_WithNonExistentDirectory_ShouldReturnFalse()
        {
            var directoryPath = Path.Combine(this.tempDirectory, "does_not_exist");

            var result = await this.storage.DirectoryExistsAsync(directoryPath);

            Assert.False(result);
        }

        [Fact]
        public async Task EnsureDirectoryExistsAsync_ShouldCreateDirectory()
        {
            var directoryPath = Path.Combine(this.tempDirectory, "ensure_test");
            Assert.False(Directory.Exists(directoryPath));

            await this.storage.EnsureDirectoryExistsAsync(directoryPath);

            Assert.True(Directory.Exists(directoryPath));
        }

        [Fact]
        public async Task EnsureDirectoryExistsAsync_WithExistingDirectory_ShouldNotThrow()
        {
            var directoryPath = Path.Combine(this.tempDirectory, "ensure_existing");
            Directory.CreateDirectory(directoryPath);

            var exception = await Record.ExceptionAsync(
                () => this.storage.EnsureDirectoryExistsAsync(directoryPath));

            Assert.Null(exception);
        }

        [Fact]
        public async Task GetFilesAsync_ShouldReturnAllFiles()
        {
            var directoryPath = Path.Combine(this.tempDirectory, "list_files_test");
            Directory.CreateDirectory(directoryPath);
            await File.WriteAllTextAsync(Path.Combine(directoryPath, "batch1.json"), "content1");
            await File.WriteAllTextAsync(Path.Combine(directoryPath, "batch2.json"), "content2");
            await File.WriteAllTextAsync(Path.Combine(directoryPath, "batch3.txt"), "content3");

            var files = (await this.storage.GetFilesAsync(directoryPath)).ToList();

            Assert.Equal(3, files.Count);
            Assert.Contains("batch1.json", files);
            Assert.Contains("batch2.json", files);
            Assert.Contains("batch3.txt", files);
        }

        [Fact]
        public async Task GetFilesAsync_WithSearchPattern_ShouldFilterFiles()
        {
            var directoryPath = Path.Combine(this.tempDirectory, "list_filtered_test");
            Directory.CreateDirectory(directoryPath);
            await File.WriteAllTextAsync(Path.Combine(directoryPath, "batch1.json"), "content1");
            await File.WriteAllTextAsync(Path.Combine(directoryPath, "batch2.json"), "content2");
            await File.WriteAllTextAsync(Path.Combine(directoryPath, "batch3.txt"), "content3");

            var files = (await this.storage.GetFilesAsync(directoryPath, "*.json")).ToList();

            Assert.Equal(2, files.Count);
            Assert.Contains("batch1.json", files);
            Assert.Contains("batch2.json", files);
            Assert.DoesNotContain("batch3.txt", files);
        }

        [Fact]
        public async Task GetFilesAsync_WithEmptyDirectory_ShouldReturnEmptyList()
        {
            var directoryPath = Path.Combine(this.tempDirectory, "empty_dir_test");
            Directory.CreateDirectory(directoryPath);

            var files = await this.storage.GetFilesAsync(directoryPath);

            Assert.Empty(files);
        }

        [Fact]
        public async Task GetFilesAsync_WithNonExistentDirectory_ShouldReturnEmptyList()
        {
            var directoryPath = Path.Combine(this.tempDirectory, "nonexistent_list");

            var files = await this.storage.GetFilesAsync(directoryPath);

            Assert.Empty(files);
        }

        [Fact]
        public async Task GetFileSizeAsync_ShouldReturnCorrectSize()
        {
            var directoryPath = Path.Combine(this.tempDirectory, "file_size_test");
            Directory.CreateDirectory(directoryPath);
            var fileName = "batch.json";
            var content = "test content with known size";
            await File.WriteAllTextAsync(Path.Combine(directoryPath, fileName), content);
            var expectedSize = Encoding.UTF8.GetByteCount(content);

            var size = await this.storage.GetFileSizeAsync(directoryPath, fileName);

            Assert.Equal(expectedSize, size);
        }

        [Fact]
        public async Task GetFileSizeAsync_WithNonExistentFile_ShouldReturnZero()
        {
            var directoryPath = Path.Combine(this.tempDirectory, "file_size_nonexistent");
            Directory.CreateDirectory(directoryPath);

            var size = await this.storage.GetFileSizeAsync(directoryPath, "nonexistent.json");

            Assert.Equal(0L, size);
        }

        [Fact]
        public async Task FileExistsAsync_WithExistingFile_ShouldReturnTrue()
        {
            var directoryPath = Path.Combine(this.tempDirectory, "file_exists_test");
            Directory.CreateDirectory(directoryPath);
            var fileName = "batch.json";
            await File.WriteAllTextAsync(Path.Combine(directoryPath, fileName), "content");

            var result = await this.storage.FileExistsAsync(directoryPath, fileName);

            Assert.True(result);
        }

        [Fact]
        public async Task FileExistsAsync_WithNonExistentFile_ShouldReturnFalse()
        {
            var directoryPath = Path.Combine(this.tempDirectory, "file_exists_nonexistent");
            Directory.CreateDirectory(directoryPath);

            var result = await this.storage.FileExistsAsync(directoryPath, "nonexistent.json");

            Assert.False(result);
        }

        [Fact]
        public async Task WriteBatchPartAsync_WithCancellation_ShouldRespectCancellationToken()
        {
            var directoryPath = Path.Combine(this.tempDirectory, "cancellation_test");
            var fileName = "batch.json";
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            using var dataStream = new MemoryStream(Encoding.UTF8.GetBytes("content"));
            await Assert.ThrowsAsync<TaskCanceledException>(
                () => this.storage.WriteBatchPartAsync(directoryPath, fileName, dataStream, cts.Token));
        }

        [Fact]
        public async Task RoundTrip_WriteThenRead_ShouldPreserveContent()
        {
            var directoryPath = Path.Combine(this.tempDirectory, "roundtrip_test");
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

        [Fact]
        public async Task GetSubdirectoriesAsync_ShouldReturnAllSubdirectories()
        {
            var rootPath = Path.Combine(this.tempDirectory, "subdir_test");
            Directory.CreateDirectory(rootPath);
            Directory.CreateDirectory(Path.Combine(rootPath, "batch1"));
            Directory.CreateDirectory(Path.Combine(rootPath, "batch2"));
            Directory.CreateDirectory(Path.Combine(rootPath, "batch3"));

            var subdirs = (await this.storage.GetSubdirectoriesAsync(rootPath)).ToList();

            Assert.Equal(3, subdirs.Count);
            Assert.Contains("batch1", subdirs);
            Assert.Contains("batch2", subdirs);
            Assert.Contains("batch3", subdirs);
        }

        [Fact]
        public async Task GetSubdirectoriesAsync_WithEmptyDirectory_ShouldReturnEmptyList()
        {
            var rootPath = Path.Combine(this.tempDirectory, "empty_subdir_test");
            Directory.CreateDirectory(rootPath);

            var subdirs = await this.storage.GetSubdirectoriesAsync(rootPath);

            Assert.Empty(subdirs);
        }

        [Fact]
        public async Task GetSubdirectoriesAsync_WithNonExistentDirectory_ShouldReturnEmptyList()
        {
            var rootPath = Path.Combine(this.tempDirectory, "nonexistent_subdir");

            var subdirs = await this.storage.GetSubdirectoriesAsync(rootPath);

            Assert.Empty(subdirs);
        }

        [Fact]
        public async Task GetSubdirectoriesAsync_ShouldNotIncludeFiles()
        {
            var rootPath = Path.Combine(this.tempDirectory, "subdir_with_files");
            Directory.CreateDirectory(rootPath);
            Directory.CreateDirectory(Path.Combine(rootPath, "subdir1"));
            await File.WriteAllTextAsync(Path.Combine(rootPath, "file1.txt"), "content");
            await File.WriteAllTextAsync(Path.Combine(rootPath, "file2.json"), "content");

            var subdirs = (await this.storage.GetSubdirectoriesAsync(rootPath)).ToList();

            Assert.Single(subdirs);
            Assert.Contains("subdir1", subdirs);
            Assert.DoesNotContain("file1.txt", subdirs);
            Assert.DoesNotContain("file2.json", subdirs);
        }

        [Fact]
        public async Task GetSubdirectoriesAsync_ShouldReturnOnlyDirectNames()
        {
            var rootPath = Path.Combine(this.tempDirectory, "subdir_names_test");
            Directory.CreateDirectory(rootPath);
            Directory.CreateDirectory(Path.Combine(rootPath, "202401011200_batch1"));
            Directory.CreateDirectory(Path.Combine(rootPath, "202401021200_batch2"));

            var subdirs = (await this.storage.GetSubdirectoriesAsync(rootPath)).ToList();

            Assert.Equal(2, subdirs.Count);
            Assert.All(subdirs, s => Assert.DoesNotContain(Path.DirectorySeparatorChar.ToString(), s));
        }

        public void Dispose()
        {
            this.stopwatch?.Stop();

            if (Directory.Exists(this.tempDirectory))
            {
                try
                {
                    Directory.Delete(this.tempDirectory, true);
                }
                catch
                {
                }
            }

            this.Output?.WriteLine($"Test took {this.stopwatch?.Elapsed.ToString() ?? "unknown time"}");
        }
    }
}
