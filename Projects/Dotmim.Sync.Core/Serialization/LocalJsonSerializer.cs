using Wormhole.Sync.Enumerations;
using Wormhole.Sync.Storage;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Wormhole.Sync.Serialization
{
   /// <summary>
   /// Serialize json rows locally.
   /// IMPORTANT: This class only supports asynchronous disposal. You MUST use 'await using' pattern.
   /// </summary>
   /// <example>
   /// <code>
   /// await using var serializer = new LocalJsonSerializer(batchStorage, orchestrator, context);
   /// await serializer.OpenFileAsync(directory, fileName, table, state);
   /// await serializer.WriteRowToFileAsync(row, table);
   /// // Automatic async disposal when scope exits
   /// </code>
   /// </example>
   public class LocalJsonSerializer : IAsyncDisposable
   {
      private static readonly ISerializer Serializer = SerializersFactory.JsonSerializerFactory.GetSerializer();
      private readonly SemaphoreSlim writerLock = new(1, 1);
      private readonly IBatchStorage batchStorage;
      private MemoryStream memoryStream;
      private Utf8JsonWriter writer;
      private Func<SyncTable, object[], Task<string>> writingRowAsync;
      private Func<SyncTable, string, Task<object[]>> readingRowAsync;
      private int isOpen;
      private bool disposedValue;
      private string currentDirectoryPath;
      private string currentFileName;

      /// <summary>
      /// Initializes a new instance of the <see cref="LocalJsonSerializer"/> class.
      /// </summary>
      public LocalJsonSerializer(IBatchStorage batchStorage, BaseOrchestrator orchestrator = null, SyncContext context = null)
      {
         this.batchStorage = batchStorage ?? throw new ArgumentNullException(nameof(batchStorage));

         if (orchestrator == null)
            return;

         if (orchestrator.HasInterceptors<DeserializingRowArgs>())
         {

            this.OnReadingRow(async (schemaTable, rowString) =>
            {
               var args = new DeserializingRowArgs(context, schemaTable, rowString);
               await orchestrator.InterceptAsync(args).ConfigureAwait(false);
               return args.Result;
            });
         }

         if (orchestrator.HasInterceptors<SerializingRowArgs>())
         {

            this.OnWritingRow(async (schemaTable, rowArray) =>
            {
               var args = new SerializingRowArgs(context, schemaTable, rowArray);
               await orchestrator.InterceptAsync(args).ConfigureAwait(false);
               return args.Result;
            });
         }
      }


      /// <summary>
      /// Gets the file extension (instance property).
      /// </summary>
      public string FileExtension => "json";

      /// <summary>
      /// Gets or sets a value indicating whether returns if the file is opened.
      /// </summary>
      public bool IsOpen
      {
         get => Interlocked.CompareExchange(ref this.isOpen, 0, 0) == 1;
         set => Interlocked.Exchange(ref this.isOpen, value ? 1 : 0);
      }

      /// <summary>
      /// Get the table contained in a serialized file asynchronously using IBatchStorage.
      /// </summary>
      public async Task<(SyncTable SchemaTable, int RowsCount, SyncRowState State)> GetSchemaTableFromFileAsync(
         string directoryPath, string fileName, CancellationToken cancellationToken = default)
      {
         var fileExists = await this.batchStorage.FileExistsAsync(directoryPath, fileName, cancellationToken).ConfigureAwait(false);
         if (!fileExists)
            return default;

         string tableName = null, schemaName = null;
         var rowsCount = 0;

         SyncTable schemaTable = null;
         var state = SyncRowState.None;

         using var stream = await this.batchStorage.ReadBatchPartAsync(directoryPath, fileName, cancellationToken).ConfigureAwait(false);
         using var jsonReader = new JsonReader(stream);

         while (jsonReader.Read())
         {
            if (jsonReader.TokenType != JsonTokenType.PropertyName)
               continue;

            // read current value
            var propertyValue = jsonReader.GetString();

            switch (propertyValue)
            {
               case "n":
                  tableName = jsonReader.ReadAsString();
                  break;
               case "s":
                  schemaName = jsonReader.ReadAsString();
                  break;
               case "st":
                  state = (SyncRowState)jsonReader.ReadAsInt32();
                  break;
               case "c": // Dont want to read columns if any
                  schemaTable = GetSchemaTableFromReader(jsonReader, tableName, schemaName);
                  break;
               case "r":
                  // go into first array
                  var hasToken = jsonReader.Read();

                  if (!hasToken)
                     break;

                  var depth = jsonReader.Depth;

                  // iterate objects array
                  while (jsonReader.Read() && jsonReader.Depth > depth)
                  {
                     var innerDepth = jsonReader.Depth;

                     // iterate values
                     while (jsonReader.Read() && jsonReader.Depth > innerDepth)
                        continue;

                     rowsCount++;
                  }

                  break;
               default:
                  break;
            }
         }

         return (schemaTable, rowsCount, state);
      }


      /// <summary>
      /// Close the current file, close the writer.
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
               this.writer.WriteEndArray();
               this.writer.WriteEndObject();
               this.writer.WriteEndArray();
               this.writer.WriteEndObject();
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
      /// Open the file and write header.
      /// </summary>
      public async Task OpenFileAsync(string directoryPath, string fileName, SyncTable schemaTable, SyncRowState state, bool append = false, CancellationToken cancellationToken = default)
      {
         Guard.ThrowIfNull(schemaTable);
         Guard.ThrowIfNullOrEmpty(directoryPath);
         Guard.ThrowIfNullOrEmpty(fileName);

         await this.ResetWriterAsync().ConfigureAwait(false);

         this.IsOpen = true;
         this.currentDirectoryPath = directoryPath;
         this.currentFileName = fileName;

         // Ensure directory exists
         await this.batchStorage.EnsureDirectoryExistsAsync(directoryPath, cancellationToken).ConfigureAwait(false);

         await this.writerLock.WaitAsync(cancellationToken).ConfigureAwait(false);

         try
         {
            // If appending and file exists, read existing content first
            if (append)
            {
               var fileExists = await this.batchStorage.FileExistsAsync(directoryPath, fileName, cancellationToken).ConfigureAwait(false);
               if (fileExists)
               {
                  using var existingStream = await this.batchStorage.ReadBatchPartAsync(directoryPath, fileName, cancellationToken).ConfigureAwait(false);
                  this.memoryStream = new MemoryStream();
                  await existingStream.CopyToAsync(this.memoryStream).ConfigureAwait(false);
                  this.memoryStream.Position = 0;
               }
               else
               {
                  this.memoryStream = new MemoryStream();
               }
            }
            else
            {
               this.memoryStream = new MemoryStream();
            }

            this.writer = new Utf8JsonWriter(this.memoryStream);

            this.writer.WriteStartObject();
            this.writer.WritePropertyName("t");

            this.writer.WriteStartArray();
            this.writer.WriteStartObject();

            this.writer.WriteString("n", schemaTable.TableName);
            this.writer.WriteString("s", schemaTable.SchemaName);
            this.writer.WriteNumber("st", (int)state);

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

            this.writer.WriteEndArray();
            this.writer.WriteStartArray("r");

            await this.writer.FlushAsync(cancellationToken).ConfigureAwait(false);
         }
         finally
         {
            this.writerLock.Release();
         }
      }

      /// <summary>
      /// Append a sync row to the writer.
      /// </summary>
      public async Task WriteRowToFileAsync(SyncRow row, SyncTable schemaTable)
      {
         Guard.ThrowIfNull(row);

         var innerRow = row.ToArray();

         string str;

         if (this.writingRowAsync != null)
            str = await this.writingRowAsync(schemaTable, innerRow).ConfigureAwait(false);
         else
            str = string.Empty; // This won't ever be used, but is need to compile.

         await this.writerLock.WaitAsync().ConfigureAwait(false);

         try
         {
            this.writer.WriteStartArray();

            if (this.writingRowAsync != null)
            {
               this.writer.WriteStringValue(str);
            }
            else
            {
               for (var i = 0; i < innerRow.Length; i++)
                  this.writer.WriteRawValue(Serializer.Serialize(innerRow[i]));
            }

            this.writer.WriteEndArray();
            await this.writer.FlushAsync().ConfigureAwait(false);
         }
         finally
         {
            this.writerLock.Release();
         }
      }

      /// <summary>
      /// Interceptor on writing row.
      /// </summary>
      public void OnWritingRow(Func<SyncTable, object[], Task<string>> func) => this.writingRowAsync = func;

      /// <summary>
      /// Interceptor on reading row.
      /// </summary>
      public void OnReadingRow(Func<SyncTable, string, Task<object[]>> func) => this.readingRowAsync = func;

      /// <summary>
      /// Gets the current file size.
      /// </summary>
      /// <returns>Current file size as long.</returns>
      public async Task<long> GetCurrentFileSizeAsync()
      {
         var position = 0L;

         await this.writerLock.WaitAsync().ConfigureAwait(false);

         try
         {
            if (this.memoryStream != null)
            {
               position = this.memoryStream.Position / 1024L;
            }
         }
         finally
         {
            this.writerLock.Release();
         }

         return position;
      }

      /// <summary>
      /// Enumerate all rows from file.
      /// </summary>
      public async Task<IEnumerable<SyncRow>> GetRowsFromFileAsync(string directoryPath, string fileName, SyncTable schemaTable, CancellationToken cancellationToken = default)
      {
         var fileExists = await this.batchStorage.FileExistsAsync(directoryPath, fileName, cancellationToken).ConfigureAwait(false);
         if (!fileExists)
            return Enumerable.Empty<SyncRow>();

         using var stream = await this.batchStorage.ReadBatchPartAsync(directoryPath, fileName, cancellationToken).ConfigureAwait(false);
         using var jsonReader = new JsonReader(stream);

         var rows = new List<SyncRow>();

         var state = SyncRowState.None;

         string tableName = null, schemaName = null;
         bool foundMatchingTable = false;
         SyncTable currentTable = schemaTable;

         while (jsonReader.Read())
         {
            if (jsonReader.TokenType != JsonTokenType.PropertyName)
               continue;

            var propertyValue = jsonReader.GetString();

            switch (propertyValue)
            {
               case "n":
                  tableName = jsonReader.ReadAsString();
                  // Reset the flag and current table when we encounter a new table
                  foundMatchingTable = false;
                  currentTable = null;
                  break;
               case "s":
                  schemaName = jsonReader.ReadAsString();
                  break;
               case "st":
                  state = (SyncRowState)jsonReader.ReadAsInt16();
                  break;
               case "c":
                  // Check if this is the table we're looking for before reading its columns
                  var isMatchingTable = schemaTable == null ||
                      (string.Equals(tableName, schemaTable.TableName, StringComparison.OrdinalIgnoreCase) &&
                       string.Equals(schemaName, schemaTable.SchemaName, StringComparison.OrdinalIgnoreCase));

                  if (isMatchingTable)
                  {
                     // Only read and set the table schema if it matches the requested table
                     var tmpTable = GetSchemaTableFromReader(jsonReader, schemaTable?.TableName ?? tableName, schemaTable?.SchemaName ?? schemaName);

                     if (tmpTable != null)
                        currentTable = tmpTable;
                  }
                  else
                  {
                     // Skip the columns array for tables we're not interested in
                     var hasSkipToken = jsonReader.Read();
                     if (hasSkipToken)
                     {
                        var skipDepth = jsonReader.Depth;
                        while (jsonReader.Read() && jsonReader.Depth > skipDepth)
                        {
                           // Just skip all tokens in this columns array
                        }
                     }
                  }

                  continue;
               case "r":
                  // Check if this is the table we're looking for
                  bool isRequestedTable = schemaTable == null ||
                      (string.Equals(tableName, schemaTable.TableName, StringComparison.OrdinalIgnoreCase) &&
                       string.Equals(schemaName, schemaTable.SchemaName, StringComparison.OrdinalIgnoreCase));

                  // If this is not the requested table, we need to skip the rows array entirely
                  if (!isRequestedTable)
                  {
                     // Skip the entire rows array by reading until we exit this depth level
                     var hasSkipToken = jsonReader.Read();
                     if (hasSkipToken)
                     {
                        var skipDepth = jsonReader.Depth;
                        while (jsonReader.Read() && jsonReader.Depth > skipDepth)
                        {
                           // Just skip all tokens in this rows array
                        }
                     }
                     break;
                  }

                  var schemaEmpty = currentTable == null;

                  if (schemaEmpty)
                     currentTable = new SyncTable(tableName, schemaName);

                  // go into first array
                  var hasToken = jsonReader.Read();

                  if (!hasToken)
                     break;

                  var depth = jsonReader.Depth;

                  // iterate objects array
                  while (jsonReader.Read() && jsonReader.Depth > depth)
                  {
                     var innerDepth = jsonReader.Depth;

                     // iterate values
                     var index = 0;
                     var values = new object[currentTable.Columns.Count + 1];
                     var stringBuilder = new StringBuilder();
                     var getStringOnly = this.readingRowAsync != null;

                     while (jsonReader.Read() && jsonReader.TokenType != JsonTokenType.EndArray)
                     {
                        object value = null;
                        var columnType = index >= 1 ? currentTable.Columns[index - 1].GetDataType() : typeof(short);

                        if (this.readingRowAsync != null)
                        {
                           if (index > 0)
                              stringBuilder.Append(',');

                           stringBuilder.Append(jsonReader.GetString());
                        }
                        else
                        {
                           if (jsonReader.TokenType is JsonTokenType.Null or JsonTokenType.None)
                              value = null;
                           else if (jsonReader.TokenType == JsonTokenType.String && jsonReader.TryGetDateTimeOffset(out var datetimeOffset))
                              value = datetimeOffset;
                           else if (jsonReader.TokenType == JsonTokenType.String)
                              value = jsonReader.GetString();
                           else if (jsonReader.TokenType is JsonTokenType.False or JsonTokenType.True)
                              value = jsonReader.GetBoolean();
                           else if (jsonReader.TokenType == JsonTokenType.Number && jsonReader.TryGetInt64(out var l))
                              value = l;
                           else if (jsonReader.TokenType == JsonTokenType.Number)
                              value = jsonReader.GetDouble();

                           try
                           {
                              if (value != null)
                                 values[index] = SyncTypeConverter.TryConvertTo(value, columnType);
                           }
                           catch (Exception)
                           {
                              // No exception as a custom converter could be used to override type
                              // like a datetime converted to ticks (long)
                           }
                        }

                        index++;
                     }

                     if (this.readingRowAsync != null)
                        values = await this.readingRowAsync(currentTable, stringBuilder.ToString()).ConfigureAwait(false);

                     if (values == null || values.Length < 2)
                     {
                        var rowStr = "[" + string.Join(",", values) + "]";
                        throw new Exception($"Can't read row {rowStr} from file {directoryPath}/{fileName}");
                     }

                     if (schemaEmpty) // array[0] contains the state, not a column
                     {
                        for (var i = 1; i < values.Length; i++)
                           currentTable.Columns.Add($"C{i}", values[i].GetType());

                        schemaEmpty = false;
                     }

                     if (values.Length != (currentTable.Columns.Count + 1))
                     {
                        var rowStr = "[" + string.Join(",", values) + "]";
                        throw new Exception($"Table {currentTable.GetFullName()} with {currentTable.Columns.Count} columns does not have the same columns count as the row read {rowStr} which have {values.Length - 1} values.");
                     }

                     // if we have some columns, we check the date time thing
                     if (currentTable.Columns?.HasSyncColumnOfType(typeof(DateTime)) == true)
                     {
                        for (var index2 = 1; index2 < values.Length; index2++)
                        {
                           var column = currentTable.Columns[index2 - 1];

                           // Set the correct value in existing row for DateTime types.
                           // They are being Deserialized as DateTimeOffsets
                           if (column != null && column.GetDataType() == typeof(DateTime) && values[index2] != null && values[index2] is DateTimeOffset)
                              values[index2] = ((DateTimeOffset)values[index2]).DateTime;
                        }
                     }

                     foundMatchingTable = true;
                     rows.Add(new SyncRow(currentTable, values));
                  }

                  // For unified batches, continue reading other tables
                  // For traditional batches, we can break after processing the requested table
                  break;
               default:
                  break;
            }
         }

         return rows;
      }

      /// <summary>
      /// Dispose the current instance asynchronously.
      /// </summary>
      public async ValueTask DisposeAsync()
      {
         if (!this.disposedValue)
         {
            await this.CloseFileAsync().ConfigureAwait(false);
            this.writerLock?.Dispose();
            this.disposedValue = true;
         }
      }

      private static SyncTable GetSchemaTableFromReader(JsonReader jsonReader, string tableName, string schemaName)
      {
         bool hadMoreTokens;

         while ((hadMoreTokens = jsonReader.Read()) && jsonReader.TokenType != JsonTokenType.StartArray)
            continue;

         if (!hadMoreTokens)
            return null;

         // get current depth
         var schemaTable = new SyncTable(tableName, schemaName);

         while (jsonReader.Read() && jsonReader.TokenType != JsonTokenType.EndArray)
         {
            // reading an object containing a column
            if (jsonReader.TokenType == JsonTokenType.StartObject)
            {
               string includedColumnName = null;
               string includedColumnTypeName = null;
               var isPrimaryKey = false;

               while (jsonReader.Read() && jsonReader.TokenType != JsonTokenType.EndObject)
               {
                  var propertyValue = jsonReader.GetString();

                  switch (propertyValue)
                  {
                     case "n":
                        includedColumnName = jsonReader.ReadAsString();
                        break;
                     case "t":
                        includedColumnTypeName = jsonReader.ReadAsString();
                        break;
                     case "p":
                        isPrimaryKey = jsonReader.ReadAsInt16() == 1;
                        break;
                     default:
                        break;
                  }
               }

               var includedColumnType = SyncColumn.GetTypeFromAssemblyQualifiedName(includedColumnTypeName);

               // Adding the column
               if (!string.IsNullOrEmpty(includedColumnName) && !string.IsNullOrEmpty(includedColumnTypeName))
               {
                  schemaTable.Columns.Add(new SyncColumn(includedColumnName, includedColumnType));

                  if (isPrimaryKey)
                     schemaTable.PrimaryKeys.Add(includedColumnName);
               }
            }
         }

         return schemaTable;
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
