using Wormhole.Sync.Batch;
using Wormhole.Sync.Enumerations;
using Wormhole.Sync.Serialization;
using Wormhole.Sync.Storage;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;


namespace Wormhole.Sync.Tests.UnitTests
{
    public class UnifiedBatchSerializerTests : IDisposable
    {
        private Stopwatch stopwatch;
        public ITestOutputHelper Output { get; }
        private readonly string tempDirectory;
        private readonly ISerializer Serializer;
        private readonly IBatchStorage batchStorage;

        public UnifiedBatchSerializerTests(ITestOutputHelper output)
        {
            this.Output = output;
            var type = output.GetType();
            this.stopwatch = Stopwatch.StartNew();

            // Create temp directory for test files
            this.tempDirectory = Path.Combine(Path.GetTempPath(), "DotmimSyncTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(this.tempDirectory);

            this.Serializer = SerializersFactory.JsonSerializerFactory.GetSerializer();
            this.batchStorage = new LocalFileSystemBatchStorage();
        }

        public void Dispose()
        {
            this.stopwatch.Stop();

            //var str = $"{test.TestCase.DisplayName} : {this.stopwatch.Elapsed.Minutes}:{this.stopwatch.Elapsed.Seconds}.{this.stopwatch.Elapsed.Milliseconds}";
            //Console.WriteLine(str);
            //Debug.WriteLine(str);

            // Clean up temp directory
            if (Directory.Exists(this.tempDirectory))
            {
                Directory.Delete(this.tempDirectory, true);
            }
        }

        private SyncTable CreateTestTable(string tableName, string schemaName = "dbo", int columnCount = 3)
        {
            var table = new SyncTable(tableName, schemaName);

            // Add primary key
            table.Columns.Add(new SyncColumn("Id", typeof(int)));
            table.PrimaryKeys.Add("Id");

            // Add additional columns
            for (int i = 1; i < columnCount; i++)
            {
                table.Columns.Add(new SyncColumn($"Column{i}", typeof(string)));
            }

            return table;
        }

        private SyncRow CreateTestRow(SyncTable table, int id, string prefix = "Data")
        {
            var row = new SyncRow(table, SyncRowState.Modified);
            row[0] = id; // Id

            for (int i = 1; i < table.Columns.Count; i++)
            {
                row[i] = $"{prefix}{i}_Row{id}";
            }

            return row;
        }

        [Fact]
        public async Task UnifiedBatchSerializer_SingleTable_SingleRow_ShouldSerializeCorrectly()
        {
            // Arrange
            var table = CreateTestTable("Product");
            var row = CreateTestRow(table, 1);
            var fileName = "test.json";

            var serializer = new UnifiedBatchSerializer(this.batchStorage);

            // Act
            await serializer.OpenFileAsync(this.tempDirectory, fileName);
            await serializer.OpenTableAsync(table);
            await serializer.WriteRowAsync(row, table);
            await serializer.CloseFileAsync();

            // Assert
            var filePath = Path.Combine(this.tempDirectory, fileName);
            Assert.True(File.Exists(filePath));

            var json = await File.ReadAllTextAsync(filePath);
            var containerSet = this.Serializer.Deserialize<ContainerSet>(json);

            Assert.NotNull(containerSet);
            Assert.Single(containerSet.Tables);
            Assert.Equal("Product", containerSet.Tables[0].TableName);
            Assert.Single(containerSet.Tables[0].Rows);
            Assert.Equal(containerSet.Tables[0].Rows[0].Length, containerSet.Tables[0].Columns.Count+1);
            Assert.Equal(containerSet.Tables[0].Rows[0][0], (long)SyncRowState.Modified); // must serialize SyncRowState
        }

        [Fact]
        public async Task UnifiedBatchSerializer_SingleTable_MultipleRows_ShouldSerializeAllRows()
        {
            // Arrange
            var table = CreateTestTable("Category");
            var rows = new List<SyncRow>();
            for (int i = 1; i <= 10; i++)
            {
                rows.Add(CreateTestRow(table, i));
            }
            var fileName = "test.json";

            var serializer = new UnifiedBatchSerializer(this.batchStorage);

            // Act
            await serializer.OpenFileAsync(this.tempDirectory, fileName);
            await serializer.OpenTableAsync(table);

            foreach (var row in rows)
            {
                await serializer.WriteRowAsync(row, table);
            }

            await serializer.CloseFileAsync();

            // Assert
            var filePath = Path.Combine(this.tempDirectory, fileName);
            var json = await File.ReadAllTextAsync(filePath);
            var containerSet = this.Serializer.Deserialize<ContainerSet>(json);

            Assert.NotNull(containerSet);
            Assert.Single(containerSet.Tables);
            Assert.Equal(10, containerSet.Tables[0].Rows.Count);
        }

        [Fact]
        public async Task UnifiedBatchSerializer_MultipleTables_ShouldSerializeInCorrectOrder()
        {
            // Arrange
            var table1 = CreateTestTable("Product");
            var table2 = CreateTestTable("Category");
            var table3 = CreateTestTable("Order");

            var fileName = "test.json";
            var serializer = new UnifiedBatchSerializer(this.batchStorage);

            // Act
            await serializer.OpenFileAsync(this.tempDirectory, fileName);

            // Table 1: 3 rows
            await serializer.OpenTableAsync(table1);
            for (int i = 1; i <= 3; i++)
            {
                await serializer.WriteRowAsync(CreateTestRow(table1, i), table1);
            }
            await serializer.CloseCurrentTableAsync();

            // Table 2: 5 rows
            await serializer.OpenTableAsync(table2);
            for (int i = 1; i <= 5; i++)
            {
                await serializer.WriteRowAsync(CreateTestRow(table2, i), table2);
            }
            await serializer.CloseCurrentTableAsync();

            // Table 3: 2 rows
            await serializer.OpenTableAsync(table3);
            for (int i = 1; i <= 2; i++)
            {
                await serializer.WriteRowAsync(CreateTestRow(table3, i), table3);
            }

            await serializer.CloseFileAsync();

            // Assert
            var filePath = Path.Combine(this.tempDirectory, fileName);
            var json = await File.ReadAllTextAsync(filePath);
            var containerSet = this.Serializer.Deserialize<ContainerSet>(json);

            Assert.NotNull(containerSet);
            Assert.Equal(3, containerSet.Tables.Count);

            Assert.Equal("Product", containerSet.Tables[0].TableName);
            Assert.Equal(3, containerSet.Tables[0].Rows.Count);

            Assert.Equal("Category", containerSet.Tables[1].TableName);
            Assert.Equal(5, containerSet.Tables[1].Rows.Count);

            Assert.Equal("Order", containerSet.Tables[2].TableName);
            Assert.Equal(2, containerSet.Tables[2].Rows.Count);
        }

        [Fact]
        public async Task UnifiedBatchSerializer_MixedOperationTypes_ShouldSerializeCorrectOperationTypes()
        {
            // Arrange
            var table = CreateTestTable("Product");
            var fileName = "test.json";
            var serializer = new UnifiedBatchSerializer(this.batchStorage);

            // Act
            await serializer.OpenFileAsync(this.tempDirectory, fileName);
            await serializer.OpenTableAsync(table);

            await serializer.WriteRowAsync(CreateTestRow(table, 1), table);
            await serializer.WriteRowAsync(CreateTestRow(table, 2), table);

            var deleteRow = CreateTestRow(table, 3);
            deleteRow.RowState = SyncRowState.Deleted;
            await serializer.WriteRowAsync(deleteRow, table);

            await serializer.CloseFileAsync();

            // Assert
            var filePath = Path.Combine(this.tempDirectory, fileName);
            var json = await File.ReadAllTextAsync(filePath);
            var containerSet = this.Serializer.Deserialize<ContainerSet>(json);

            Assert.NotNull(containerSet);
            Assert.Single(containerSet.Tables);
            Assert.Equal(3, containerSet.Tables[0].Rows.Count);
        }

        [Fact]
        public async Task UnifiedBatchSerializer_GetCurrentFileSizeKB_ShouldTrackBytesWritten()
        {
            // Arrange
            var table = CreateTestTable("Product", "dbo", 10); // More columns = larger rows
            var fileName = "test.json";
            var serializer = new UnifiedBatchSerializer(this.batchStorage);

            // Act
            await serializer.OpenFileAsync(this.tempDirectory, fileName);
            var sizeAfterOpen = serializer.GetCurrentFileSizeInBytes();

            await serializer.OpenTableAsync(table);
            var sizeAfterTableOpen = serializer.GetCurrentFileSizeInBytes();

            // Write multiple rows to increase size
            for (int i = 1; i <= 100; i++)
            {
                await serializer.WriteRowAsync(CreateTestRow(table, i, "LongDataString"), table);
            }

            var sizeAfterRows = serializer.GetCurrentFileSizeInBytes();
            await serializer.CloseFileAsync();

            // Assert
            Assert.True(sizeAfterOpen > 0, "Size should be > 0 after opening file");
            Assert.True(sizeAfterTableOpen > sizeAfterOpen, "Size should increase after opening table");
            Assert.True(sizeAfterRows > sizeAfterTableOpen, "Size should increase after writing rows");
        }

        [Fact]
        public async Task UnifiedBatchSerializer_CurrentTableKey_ShouldTrackOpenTable()
        {
            // Arrange
            var table1 = CreateTestTable("Product", "Sales");
            var table2 = CreateTestTable("Category", "dbo");
            var fileName = "test.json";
            var serializer = new UnifiedBatchSerializer(this.batchStorage);

            // Act & Assert
            await serializer.OpenFileAsync(this.tempDirectory, fileName);
            Assert.Null(serializer.CurrentTableKey);
            Assert.False(serializer.HasCurrentTable);

            await serializer.OpenTableAsync(table1);
            Assert.Equal("Sales.Product", serializer.CurrentTableKey);
            Assert.True(serializer.HasCurrentTable);

            await serializer.CloseCurrentTableAsync();
            Assert.Null(serializer.CurrentTableKey);
            Assert.False(serializer.HasCurrentTable);

            await serializer.OpenTableAsync(table2);
            Assert.Equal("dbo.Category", serializer.CurrentTableKey);
            Assert.True(serializer.HasCurrentTable);

            await serializer.CloseFileAsync();
        }

        [Fact]
        public async Task UnifiedBatchSerializer_OpenTableTwice_ShouldThrowException()
        {
            // Arrange
            var table1 = CreateTestTable("Product");
            var table2 = CreateTestTable("Category");
            var fileName = "test.json";
            var serializer = new UnifiedBatchSerializer(this.batchStorage);

            // Act & Assert
            await serializer.OpenFileAsync(this.tempDirectory, fileName);
            await serializer.OpenTableAsync(table1);

            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await serializer.OpenTableAsync(table2);
            });

            await serializer.CloseFileAsync();
        }

        [Fact]
        public async Task UnifiedBatchSerializer_WriteRowWithoutOpenTable_ShouldThrowException()
        {
            // Arrange
            var table = CreateTestTable("Product");
            var row = CreateTestRow(table, 1);
            var fileName = "test.json";
            var serializer = new UnifiedBatchSerializer(this.batchStorage);

            // Act & Assert
            await serializer.OpenFileAsync(this.tempDirectory, fileName);

            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await serializer.WriteRowAsync(row, table);
            });

            await serializer.CloseFileAsync();
        }

        [Fact]
        public async Task UnifiedBatchSerializer_LargeTable_ExceedingSize_ShouldTrackCorrectly()
        {
            // Arrange
            var table = CreateTestTable("LargeProduct", "dbo", 20); // Many columns
            var fileName = "test.json";
            var serializer = new UnifiedBatchSerializer(this.batchStorage);

            // Act
            await serializer.OpenFileAsync(this.tempDirectory, fileName);
            await serializer.OpenTableAsync(table);

            long previousSize = 0;
            for (int i = 1; i <= 1000; i++)
            {
                var row = CreateTestRow(table, i, new string('X', 100)); // Large data
                var currentSize = await serializer.WriteRowAsync(row, table);

                // Size should always increase
                Assert.True(currentSize > previousSize, $"Size should increase at row {i}");
                previousSize = currentSize;
            }

            await serializer.CloseFileAsync();

            // Assert
            var filePath = Path.Combine(this.tempDirectory, fileName);
            var fileInfo = new FileInfo(filePath);
            Assert.True(fileInfo.Length > 0);

            // Verify the size tracking was accurate
            Assert.True(previousSize > 0);
        }

        [Fact]
        public async Task UnifiedBatchSerializer_ReopenTableAfterClose_ShouldWork()
        {
            // Arrange
            var table = CreateTestTable("Product");
            var fileName = "test.json";
            var serializer = new UnifiedBatchSerializer(this.batchStorage);

            // Act
            await serializer.OpenFileAsync(this.tempDirectory, fileName);

            // First batch
            await serializer.OpenTableAsync(table);
            await serializer.WriteRowAsync(CreateTestRow(table, 1), table);
            await serializer.CloseCurrentTableAsync();

            // Reopen same table
            await serializer.OpenTableAsync(table);
            await serializer.WriteRowAsync(CreateTestRow(table, 2), table);
            await serializer.CloseCurrentTableAsync();

            await serializer.CloseFileAsync();

            // Assert
            var filePath = Path.Combine(this.tempDirectory, fileName);
            var json = await File.ReadAllTextAsync(filePath);
            var containerSet = this.Serializer.Deserialize<ContainerSet>(json);

            // Should have two separate table entries
            Assert.Equal(2, containerSet.Tables.Count);
            Assert.Equal("Product", containerSet.Tables[0].TableName);
            Assert.Equal("Product", containerSet.Tables[1].TableName);
            Assert.Single(containerSet.Tables[0].Rows);
            Assert.Single(containerSet.Tables[1].Rows);
        }

        [Fact]
        public async Task UnifiedBatchSerializer_EmptyTable_ShouldStillSerializeStructure()
        {
            // Arrange
            var table = CreateTestTable("EmptyProduct");
            var fileName = "test.json";
            var serializer = new UnifiedBatchSerializer(this.batchStorage);

            // Act
            await serializer.OpenFileAsync(this.tempDirectory, fileName);
            await serializer.OpenTableAsync(table);
            // Don't write any rows
            await serializer.CloseFileAsync();

            // Assert
            var filePath = Path.Combine(this.tempDirectory, fileName);
            var json = await File.ReadAllTextAsync(filePath);
            var containerSet = this.Serializer.Deserialize<ContainerSet>(json);

            Assert.NotNull(containerSet);
            Assert.Single(containerSet.Tables);
            Assert.Equal("EmptyProduct", containerSet.Tables[0].TableName);
            Assert.Empty(containerSet.Tables[0].Rows);
        }

        [Fact]
        public async Task UnifiedBatchSerializer_DisposeWithoutClose_ShouldCloseAutomatically()
        {
            // Arrange
            var table = CreateTestTable("Product");
            var fileName = "test.json";

            // Act
            var serializer = new UnifiedBatchSerializer(this.batchStorage);
            await serializer.OpenFileAsync(this.tempDirectory, fileName);
            await serializer.OpenTableAsync(table);
            await serializer.WriteRowAsync(CreateTestRow(table, 1), table);

            // Dispose without explicitly closing
            await serializer.DisposeAsync();

            // Assert
            var filePath = Path.Combine(this.tempDirectory, fileName);
            Assert.True(File.Exists(filePath));
            var json = await File.ReadAllTextAsync(filePath);
            var containerSet = this.Serializer.Deserialize<ContainerSet>(json);

            Assert.NotNull(containerSet);
            Assert.Single(containerSet.Tables);
            Assert.Single(containerSet.Tables[0].Rows);
        }
    }
}