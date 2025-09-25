using Dotmim.Sync.Batch;
using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Dotmim.Sync.Serialization
{
    /// <summary>
    /// Serializes unified batch data (ContainerSet with multiple tables) to JSON incrementally.
    /// Writes row-by-row to check actual stream size for batch file splitting.
    /// </summary>
    public class UnifiedBatchSerializer : IDisposable, IAsyncDisposable
    {
        private static readonly ISerializer Serializer = SerializersFactory.JsonSerializerFactory.GetSerializer();
        private readonly SemaphoreSlim writerLock = new(1, 1);
        private StreamWriter sw;
        private Utf8JsonWriter writer;
        private int isOpen;
        private bool disposedValue;
        private string currentTableKey; // Track the currently open table (format: "schemaName.tableName")
        private long bytesWritten; // Track bytes written to the stream

        /// <summary>
        /// Gets the file extension.
        /// </summary>
        public static string Extension => "json";

        /// <summary>
        /// Gets a value indicating whether the file is opened.
        /// </summary>
        public bool IsOpen
        {
            get => Interlocked.CompareExchange(ref this.isOpen, 0, 0) == 1;
            private set => Interlocked.Exchange(ref this.isOpen, value ? 1 : 0);
        }

        /// <summary>
        /// Gets a value indicating whether a table is currently open.
        /// </summary>
        public bool HasCurrentTable => !string.IsNullOrEmpty(this.currentTableKey);

        /// <summary>
        /// Gets the key of the currently open table (format: "schemaName.tableName"), or null if no table is open.
        /// </summary>
        public string CurrentTableKey => this.currentTableKey;

        /// <summary>
        /// Finalizes an instance of the <see cref="UnifiedBatchSerializer"/> class.
        /// </summary>
        ~UnifiedBatchSerializer()
        {
            this.Dispose(false);
        }

        /// <summary>
        /// Open the file and write ContainerSet header.
        /// </summary>
        public async Task OpenFileAsync(string path)
        {
            Guard.ThrowIfNullOrEmpty(path);

            await this.ResetWriterAsync().ConfigureAwait(false);

            this.IsOpen = true;
            this.bytesWritten = 0;
            this.currentTableKey = null;

            var fi = new FileInfo(path);

            if (!fi.Directory.Exists)
                fi.Directory.Create();

            await this.writerLock.WaitAsync().ConfigureAwait(false);

            try
            {
                this.sw = new StreamWriter(path, append: false);
                this.writer = new Utf8JsonWriter(this.sw.BaseStream);

                // Start ContainerSet JSON structure: {"t":[
                this.writer.WriteStartObject(); // ContainerSet root
                this.writer.WritePropertyName("t"); // Tables array
                this.writer.WriteStartArray();

                await this.writer.FlushAsync().ConfigureAwait(false);
                this.bytesWritten = this.sw.BaseStream.Position;
            }
            finally
            {
                this.writerLock.Release();
            }
        }

        /// <summary>
        /// Opens a new table in the ContainerSet, writing table name, schema, and columns.
        /// If a table is already open, this will throw an exception.
        /// </summary>
        public async Task OpenTableAsync(SyncTable schemaTable)
        {
            Guard.ThrowIfNull(schemaTable);

            if (this.HasCurrentTable)
                throw new InvalidOperationException($"Cannot open table {schemaTable.GetFullName()} because table {this.currentTableKey} is already open. Close it first.");

            await this.writerLock.WaitAsync().ConfigureAwait(false);

            try
            {
                var tableKey = $"{schemaTable.SchemaName}.{schemaTable.TableName}";

                this.writer.WriteStartObject(); // ContainerTable object

                this.writer.WriteString("n", schemaTable.TableName);
                this.writer.WriteString("s", schemaTable.SchemaName);

                // Write columns array with batching column (_rs)
                this.writer.WriteStartArray("c");
                foreach (var c in schemaTable.Columns)
                {
                    this.writer.WriteStartObject();
                    this.writer.WriteString("n", c.ColumnName);
                    this.writer.WriteString("t", c.DataType);
                    if (schemaTable.IsPrimaryKey(c.ColumnName))
                    {
                        this.writer.WriteNumber("p", 1);
                    }
                    this.writer.WriteEndObject();
                }

                // Add the batching operation column (_rs)
                this.writer.WriteStartObject();
                this.writer.WriteString("n", "_rs");
                this.writer.WriteString("t", "String");
                this.writer.WriteEndObject();

                this.writer.WriteEndArray(); // End columns array

                // Start rows array
                this.writer.WriteStartArray("r");

                await this.writer.FlushAsync().ConfigureAwait(false);
                this.bytesWritten = this.sw.BaseStream.Position;

                this.currentTableKey = tableKey;
            }
            finally
            {
                this.writerLock.Release();
            }
        }

        /// <summary>
        /// Closes the currently open table.
        /// </summary>
        public async Task CloseCurrentTableAsync()
        {
            if (!this.HasCurrentTable)
                return;

            await this.writerLock.WaitAsync().ConfigureAwait(false);

            try
            {
                this.writer.WriteEndArray(); // End rows array "r"
                this.writer.WriteEndObject(); // End ContainerTable object

                await this.writer.FlushAsync().ConfigureAwait(false);
                this.bytesWritten = this.sw.BaseStream.Position;

                this.currentTableKey = null;
            }
            finally
            {
                this.writerLock.Release();
            }
        }

        /// <summary>
        /// Append a sync row with operation type to the current table.
        /// Returns the new total bytes written after adding this row.
        /// </summary>
        public async Task<long> WriteRowAsync(SyncRow row, SyncTable schemaTable, BatchOperationType operationType)
        {
            Guard.ThrowIfNull(row);

            if (!this.HasCurrentTable)
                throw new InvalidOperationException("No table is currently open. Call OpenTableAsync first.");

            await this.writerLock.WaitAsync().ConfigureAwait(false);

            try
            {
                var innerRow = row.ToArray();

                this.writer.WriteStartArray(); // Start row array

                // Write each column value
                for (var i = 1; i < innerRow.Length; i++)
                    this.writer.WriteRawValue(Serializer.Serialize(innerRow[i]));

                // Write operation type as the last column (_rs)
                var operationTypeString = ContainerTable.OperationTypeToString(operationType);
                this.writer.WriteStringValue(operationTypeString);

                this.writer.WriteEndArray(); // End row array

                await this.writer.FlushAsync().ConfigureAwait(false);
                this.bytesWritten = this.sw.BaseStream.Position;

                return this.bytesWritten; // Return bytes 
            }
            finally
            {
                this.writerLock.Release();
            }
        }

        /// <summary>
        /// Close the current file, closing the current table if open and the ContainerSet structure.
        /// </summary>
        public async Task CloseFileAsync()
        {
            if (!this.IsOpen)
                return;

            await this.writerLock.WaitAsync().ConfigureAwait(false);

            try
            {
                if (this.writer != null)
                {
                    // Close current table if open
                    if (this.HasCurrentTable)
                    {
                        this.writer.WriteEndArray(); // End rows array "r"
                        this.writer.WriteEndObject(); // End ContainerTable object
                        this.currentTableKey = null;
                    }

                    // Close ContainerSet structure
                    this.writer.WriteEndArray(); // End tables array "t"
                    this.writer.WriteEndObject(); // End ContainerSet root

                    await this.writer.FlushAsync().ConfigureAwait(false);
                    await this.writer.DisposeAsync().ConfigureAwait(false);
                }

#if NET6_0_OR_GREATER
                await this.sw.DisposeAsync().ConfigureAwait(false);
#else
                this.sw?.Dispose();
#endif

                this.IsOpen = false;
            }
            finally
            {
                this.writerLock.Release();
            }
        }

        /// <summary>
        /// Gets the current file size in bytes.
        /// </summary>
        public long GetCurrentFileSizeInBytes()
        {
            return this.bytesWritten;
        }

        /// <summary>
        /// Dispose the current instance.
        /// </summary>
        public void Dispose()
        {
            this.Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Dispose the current instance.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            await this.CloseFileAsync().ConfigureAwait(false);
            this.writerLock.Dispose();
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Dispose the current instance.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (!this.disposedValue)
            {
                if (disposing)
                {
                    this.CloseFileAsync().GetAwaiter().GetResult();
                    this.writerLock?.Dispose();
                }

                this.disposedValue = true;
            }
        }

        private async Task ResetWriterAsync()
        {
            if (this.writer == null)
                return;

            await this.writerLock.WaitAsync().ConfigureAwait(false);

            try
            {
                await this.writer.DisposeAsync().ConfigureAwait(false);
                this.writer = null;
            }
            finally
            {
                this.writerLock.Release();
            }
        }
    }
}