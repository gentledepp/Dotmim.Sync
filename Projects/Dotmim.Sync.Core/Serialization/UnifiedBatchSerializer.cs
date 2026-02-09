using Wormhole.Sync.Batch;
using Wormhole.Sync.Storage;
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Wormhole.Sync.Serialization
{
   /// <summary>
   /// Serializes unified batch data (ContainerSet with multiple tables) to JSON incrementally.
   /// Writes row-by-row to check actual stream size for batch file splitting.
   /// </summary>
   public class UnifiedBatchSerializer : IDisposable, IAsyncDisposable
   {
      private readonly SemaphoreSlim writerLock = new(1, 1);
      private readonly IBatchStorage batchStorage;
      private MemoryStream memoryStream;
      private Utf8JsonWriter writer;
      private int isOpen;
      private bool disposedValue;
      private string currentTableKey; // Track the currently open table (format: "schemaName.tableName")
      private string currentDirectoryPath;
      private string currentFileName;

      /// <summary>
      /// JsonSerializerOptions with ObjectToInferredTypesConverter for direct value writing.
      /// </summary>
      private static readonly JsonSerializerOptions JsonOptions = new()
      {
         Converters = { new ObjectToInferredTypesConverter() },
      };

      /// <summary>
      /// Initializes a new instance of the <see cref="UnifiedBatchSerializer"/> class.
      /// </summary>
      public UnifiedBatchSerializer(IBatchStorage batchStorage)
      {
         this.batchStorage = batchStorage ?? throw new ArgumentNullException(nameof(batchStorage));
      }

      /// <summary>
      /// Gets the file extension (instance property).
      /// </summary>
      public string FileExtension => "json";

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
      public async Task OpenFileAsync(string directoryPath, string fileName, CancellationToken cancellationToken = default)
      {
         Guard.ThrowIfNullOrEmpty(directoryPath);
         Guard.ThrowIfNullOrEmpty(fileName);

         await this.ResetWriterAsync().ConfigureAwait(false);

         this.IsOpen = true;
         this.currentTableKey = null;
         this.currentDirectoryPath = directoryPath;
         this.currentFileName = fileName;

         // Ensure directory exists
         await this.batchStorage.EnsureDirectoryExistsAsync(directoryPath, cancellationToken).ConfigureAwait(false);

         await this.writerLock.WaitAsync(cancellationToken).ConfigureAwait(false);

         try
         {
            this.memoryStream = new MemoryStream();
            this.writer = new Utf8JsonWriter(this.memoryStream);

            // Start ContainerSet JSON structure: {"t":[
            this.writer.WriteStartObject(); // ContainerSet root
            this.writer.WritePropertyName("t"); // Tables array
            this.writer.WriteStartArray();

            await this.writer.FlushAsync(cancellationToken).ConfigureAwait(false);
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

            // Write columns array (no batching column needed, RowState is in position 0)
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

            this.writer.WriteEndArray(); // End columns array

            // Start rows array
            this.writer.WriteStartArray("r");

            await this.writer.FlushAsync().ConfigureAwait(false);

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

            this.currentTableKey = null;
         }
         finally
         {
            this.writerLock.Release();
         }
      }

      /// <summary>
      /// Append a sync row to the current table.
      /// Returns the new total bytes written after adding this row.
      /// </summary>
      public async Task<long> WriteRowAsync(SyncRow row, SyncTable schemaTable)
      {
         Guard.ThrowIfNull(row);

         if (!this.HasCurrentTable)
            throw new InvalidOperationException("No table is currently open. Call OpenTableAsync first.");

         await this.writerLock.WaitAsync().ConfigureAwait(false);

         try
         {
            var innerRow = row.ToArray();

            this.writer.WriteStartArray(); // Start row array

            // Write all values including state at position 0
            // Format: [state, col1, col2, ..., colN]
            // Use direct value writing instead of double serialization via Serializer.Serialize()
            for (var i = 0; i < innerRow.Length; i++)
               ObjectToInferredTypesConverter.WriteValue(this.writer, innerRow[i], JsonOptions);

            this.writer.WriteEndArray(); // End row array

            // No flushing needed here - use BytesPending to get accurate size without I/O
            // BytesCommitted = bytes already written to stream
            // BytesPending = bytes buffered in writer, not yet written
            return this.writer.BytesCommitted + this.writer.BytesPending;
         }
         finally
         {
            this.writerLock.Release();
         }
      }

      /// <summary>
      /// Close the current file, closing the current table if open and the ContainerSet structure.
      /// </summary>
      public async Task CloseFileAsync(CancellationToken cancellationToken = default)
      {
         if (!this.IsOpen)
            return;

         await this.writerLock.WaitAsync(cancellationToken).ConfigureAwait(false);

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

               await this.writer.FlushAsync(cancellationToken).ConfigureAwait(false);
               await this.writer.DisposeAsync().ConfigureAwait(false);
            }

            // Write to batch storage
            if (this.memoryStream != null && !string.IsNullOrEmpty(this.currentDirectoryPath) && !string.IsNullOrEmpty(this.currentFileName))
            {
               this.memoryStream.Position = 0;
               await this.batchStorage.WriteBatchPartAsync(this.currentDirectoryPath, this.currentFileName, this.memoryStream, cancellationToken).ConfigureAwait(false);
            }

            if (this.memoryStream != null)
            {
#if NET6_0_OR_GREATER
               await this.memoryStream.DisposeAsync().ConfigureAwait(false);
#else
               this.memoryStream.Dispose();
#endif
            }

            this.memoryStream = null;
            this.currentDirectoryPath = null;
            this.currentFileName = null;
            this.IsOpen = false;
         }
         finally
         {
            this.writerLock.Release();
         }
      }

      /// <summary>
      /// Gets the current file size in bytes (including buffered data not yet flushed).
      /// </summary>
      public long GetCurrentFileSizeInBytes()
      {
         if (this.writer == null)
            return 0;

         // BytesCommitted = bytes already written to stream
         // BytesPending = bytes buffered in writer, not yet written
         return this.writer.BytesCommitted + this.writer.BytesPending;
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
      /// WARNING: Synchronous disposal is not recommended. Use 'await using' or call DisposeAsync() instead.
      /// This synchronous Dispose does not properly close the file asynchronously.
      /// </summary>
      protected virtual void Dispose(bool disposing)
      {
         if (!this.disposedValue)
         {
            if (disposing)
            {
               // NOTE: We cannot call async methods from synchronous Dispose.
               // Users should use 'await using' or call DisposeAsync() explicitly.
               // If CloseFileAsync has not been called, resources may not be properly released.
               try
               {
                  // Try to dispose the writer synchronously if possible
                  this.writer?.Dispose();
                  this.memoryStream?.Dispose();
               }
               catch
               {
                  // Suppress exceptions during synchronous disposal
               }

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
