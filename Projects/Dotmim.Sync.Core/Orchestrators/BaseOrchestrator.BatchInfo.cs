using Wormhole.Sync.Batch;
using Wormhole.Sync.Enumerations;
using Wormhole.Sync.Serialization;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Wormhole.Sync
{
   /// <summary>
   /// Contains methods to load and save batch info.
   /// </summary>
   public abstract partial class BaseOrchestrator
   {

      /// <summary>
      /// Load all batch infos from the batch directory (see <see cref="SyncOptions.BatchDirectory"/>).
      /// <example>
      /// <code>
      /// var batchInfos = await agent.LocalOrchestrator.LoadBatchInfosAsync();
      ///
      /// foreach (var batchInfo in batchInfos)
      ///     Console.WriteLine(batchInfo.RowsCount);
      /// </code>
      /// </example>
      /// </summary>
      public virtual async Task<IList<BatchInfo>> LoadBatchInfosAsync(CancellationToken cancellationToken = default)
      {
         var directoryExists = await this.BatchStorage.DirectoryExistsAsync(this.Options.BatchDirectory, cancellationToken).ConfigureAwait(false);

         if (!directoryExists)
            return null;

         var batchInfos = new List<BatchInfo>();

         var subdirectories = await this.BatchStorage.GetSubdirectoriesAsync(this.Options.BatchDirectory, cancellationToken).ConfigureAwait(false);

         foreach (var subDirectory in subdirectories)
         {
            var batchInfo = new BatchInfo(this.Options.BatchDirectory, subDirectory);

            var files = await this.BatchStorage.GetFilesAsync(batchInfo.GetDirectoryFullPath(), "*", cancellationToken).ConfigureAwait(false);

            await using var localSerializer = new LocalJsonSerializer(this.BatchStorage);
            foreach (var fileName in files)
            {
               var directoryPath = batchInfo.GetDirectoryFullPath();
               var (schemaTable, rowsCount, state) = await localSerializer.GetSchemaTableFromFileAsync(directoryPath, fileName, cancellationToken).ConfigureAwait(false);
               if (schemaTable != null)
                  batchInfo.BatchPartsInfo.Add(new BatchPartInfo(fileName, schemaTable.TableName, schemaTable.SchemaName, state, rowsCount));
            }

            batchInfos.Add(batchInfo);
         }

         return batchInfos;
      }

      /// <inheritdoc cref="LoadTablesFromBatchInfoAsync(string, BatchInfo, SyncRowState?, CancellationToken)"/>
      public virtual Task<IEnumerable<SyncTable>> LoadTablesFromBatchInfoAsync(BatchInfo batchInfo, SyncRowState? syncRowState = default, CancellationToken cancellationToken = default)
         => this.LoadTablesFromBatchInfoAsync(SyncOptions.DefaultScopeName, batchInfo, syncRowState, cancellationToken);

      /// <summary>
      /// Load all tables from a batch info. All rows serialized on disk are loaded in memory once you are iterating.
      ///
      /// <code>
      /// var batchInfos = await agent.LocalOrchestrator.LoadBatchInfosAsync();
      /// foreach (var batchInfo in batchInfos)
      /// {
      ///    // Load all rows from error tables specifying the specific SyncRowState states
      ///    var allTables = await agent.LocalOrchestrator.LoadTablesFromBatchInfoAsync(batchInfo, SyncRowState.ApplyDeletedFailed | SyncRowState.ApplyModifiedFailed);
      ///
      ///    // Enumerate all rows in error
      ///    foreach (var table in allTables)
      ///      foreach (var row in table.Rows)
      ///        Console.WriteLine(row);
      /// }
      /// </code>
      /// </summary>
      public virtual async Task<IEnumerable<SyncTable>> LoadTablesFromBatchInfoAsync(string scopeName, BatchInfo batchInfo, SyncRowState? syncRowState = default, CancellationToken cancellationToken = default)
      {
         if (batchInfo == null || batchInfo.BatchPartsInfo == null || batchInfo.BatchPartsInfo.Count == 0)
            return Enumerable.Empty<SyncTable>();

         var context = new SyncContext(Guid.NewGuid(), scopeName);
         var bpiGroupedTables = batchInfo.BatchPartsInfo.GroupBy(st => st.TableName + st.SchemaName);

         await using var localSerializer = new LocalJsonSerializer(this.BatchStorage, this, context);

         var result = new List<SyncTable>();
         SyncTable currentTable = null;

         foreach (var bpiGroupedTable in bpiGroupedTables)
         {
            var bpiTable = bpiGroupedTable.FirstOrDefault();

            if (bpiTable == null)
               continue;

            // Gets all BPI containing this table
            foreach (var bpi in batchInfo.GetBatchPartsInfos(bpiTable.TableName, bpiTable.SchemaName))
            {
               try
               {
                  // Get full path of my batchpartinfo
                  var fullPath = batchInfo.GetBatchPartInfoFullPath(bpi);
                  var directoryPath = Path.GetDirectoryName(fullPath);
                  var fileName = Path.GetFileName(fullPath);

                  var fileExists = await this.BatchStorage.FileExistsAsync(directoryPath, fileName, cancellationToken).ConfigureAwait(false);
                  if (!fileExists)
                     continue;

                  var (syncTable, _, _) = await localSerializer.GetSchemaTableFromFileAsync(directoryPath, fileName, cancellationToken).ConfigureAwait(false);

                  // on first iteration, creating the return table
                  currentTable ??= syncTable.Clone();

                  var syncRows = await localSerializer.GetRowsFromFileAsync(directoryPath, fileName, syncTable, cancellationToken).ConfigureAwait(false);
                  foreach (var syncRow in syncRows)
                  {
                     if (!syncRowState.HasValue || syncRowState == default || (syncRowState.HasValue && syncRowState.Value.HasFlag(syncRow.RowState)))
                        currentTable.Rows.Add(syncRow);
                  }
               }
               catch (Exception ex)
               {
                  throw this.GetSyncError(context, ex);
               }
            }

            if (currentTable != null)
               result.Add(currentTable);
         }

         return result;
      }

      //-------------------------------------------------------
      // Load Batch Info for a given Table Name into SyncTable
      //-------------------------------------------------------

      /// <inheritdoc cref="LoadTableFromBatchInfoAsync(string, BatchInfo, string, string, SyncRowState?, CancellationToken)"/>
      public virtual Task<SyncTable> LoadTableFromBatchInfoAsync(BatchInfo batchInfo, string tableName, string schemaName = default, SyncRowState? syncRowState = default, CancellationToken cancellationToken = default)
         => this.LoadTableFromBatchInfoAsync(SyncOptions.DefaultScopeName, batchInfo, tableName, schemaName, syncRowState, cancellationToken);

      /// <summary>
      /// Load a table with all rows from a <see cref="BatchInfo"/> instance. You need a <see cref="ScopeInfoClient"/> instance to be able to load rows for this client.
      /// <para>
      /// Once loaded, all rows are in memory.
      /// </para>
      /// <example>
      /// <code>
      /// // get the local client scope info
      /// var cScopeInfoClient = await localOrchestrator.GetScopeInfoClientAsync(scopeName, parameters);
      /// // get all changes from server
      /// var changes = await remoteOrchestrator.GetChangesAsync(cScopeInfoClient);
      /// // load changes for table ProductCategory in memory
      /// var productCategoryTable = await localOrchestrator.LoadTableFromBatchInfoAsync(scopeName, changes, "ProductCategory")
      /// foreach (var productCategoryRow in productCategoryTable.Rows)
      /// {
      ///    ....
      /// }
      /// </code>
      /// </example>
      /// </summary>
      public virtual async Task<SyncTable> LoadTableFromBatchInfoAsync(string scopeName, BatchInfo batchInfo, string tableName, string schemaName = default, SyncRowState? syncRowState = default, CancellationToken cancellationToken = default)
      {
         if (batchInfo == null)
            return null;

         var context = new SyncContext(Guid.NewGuid(), scopeName);

         try
         {
            return await this.InternalLoadTableFromBatchInfoAsync(context, batchInfo, tableName, schemaName, syncRowState, cancellationToken).ConfigureAwait(false);
         }
         catch (Exception ex)
         {
            throw this.GetSyncError(context, ex);
         }
      }

      //-------------------------------------------------------
      // Load Batch Part Info into SyncTable
      //-------------------------------------------------------

      /// <summary>
      /// Load a table from a batch part info.
      /// </summary>
      public virtual Task<SyncTable> LoadTableFromBatchPartInfoAsync(string directoryPath, string fileName, SyncRowState? syncRowState = default, DbConnection connection = null, DbTransaction transaction = null, CancellationToken cancellationToken = default)
         => this.LoadTableFromBatchPartInfoAsync(SyncOptions.DefaultScopeName, directoryPath, fileName, syncRowState, connection, transaction, cancellationToken);

      /// <summary>
      /// Load a table from a batch part info.
      /// </summary>
      public virtual async Task<SyncTable> LoadTableFromBatchPartInfoAsync(string scopeName, string directoryPath, string fileName, SyncRowState? syncRowState = default, DbConnection connection = null, DbTransaction transaction = null, CancellationToken cancellationToken = default)
      {
         var context = new SyncContext(Guid.NewGuid(), scopeName);
         try
         {
            return await this.InternalLoadTableFromBatchPartInfoAsync(context, directoryPath, fileName, syncRowState, connection, transaction, cancellationToken).ConfigureAwait(false);
         }
         catch (Exception ex)
         {
            throw this.GetSyncError(context, ex);
         }
      }

      /// <summary>
      /// Save a batch part info containing all rows from a sync table.
      /// </summary>
      /// <param name="batchInfo">Represents the directory containing all batch parts and the schema associated.</param>
      /// <param name="batchPartInfo">Represents the table to serialize in a batch part.</param>
      /// <param name="syncTable">The table to serialize.</param>
      /// <param name="cancellationToken">Cancellation token.</param>
      public virtual Task SaveTableToBatchPartInfoAsync(BatchInfo batchInfo, BatchPartInfo batchPartInfo, SyncTable syncTable, CancellationToken cancellationToken = default)
         => this.SaveTableToBatchPartInfoAsync(SyncOptions.DefaultScopeName, batchInfo, batchPartInfo, syncTable, cancellationToken);

      /// <inheritdoc cref="SaveTableToBatchPartInfoAsync(BatchInfo, BatchPartInfo, SyncTable, CancellationToken)"/>
      public virtual Task SaveTableToBatchPartInfoAsync(string scopeName, BatchInfo batchInfo, BatchPartInfo batchPartInfo, SyncTable syncTable, CancellationToken cancellationToken = default)
      {
         var context = new SyncContext(Guid.NewGuid(), scopeName);
         try
         {

            return this.InternalSaveTableToBatchPartInfoAsync(context, batchInfo, batchPartInfo, syncTable, cancellationToken);
         }
         catch (Exception ex)
         {
            throw this.GetSyncError(context, ex);
         }
      }

      /// <summary>
      /// Load the Batch info in memory, in a SyncTable.
      /// </summary>
      internal async Task<SyncTable> InternalLoadTableFromBatchInfoAsync(SyncContext context, BatchInfo batchInfo, string tableName, string schemaName = default, SyncRowState? syncRowState = default, CancellationToken cancellationToken = default)
      {
         await using var localSerializer = new LocalJsonSerializer(this.BatchStorage, this, context);
         SyncTable syncTable = null;

         // Gets all BPI containing this table
         foreach (var bpi in batchInfo.GetBatchPartsInfos(tableName, schemaName))
         {
            // Get full path of my batchpartinfo
            var fullPath = batchInfo.GetBatchPartInfoFullPath(bpi);
            var directoryPath = Path.GetDirectoryName(fullPath);
            var fileName = Path.GetFileName(fullPath);

            var fileExists = await this.BatchStorage.FileExistsAsync(directoryPath, fileName, cancellationToken).ConfigureAwait(false);
            if (!fileExists)
               continue;

            // For unified batches, create a filter table with the requested name
            // so GetRowsFromFile knows which table to extract
            if (syncTable == null)
               syncTable = new SyncTable(tableName, schemaName);

            var syncRows = await localSerializer.GetRowsFromFileAsync(directoryPath, fileName, syncTable, cancellationToken).ConfigureAwait(false);
            foreach (var syncRow in syncRows)
            {
               // Update syncTable reference to use the one with full schema from the first row
               if (syncTable.Columns.Count == 0 && syncRow.SchemaTable != null)
                  syncTable = syncRow.SchemaTable;

               if (!syncRowState.HasValue || syncRowState == default || (syncRowState.HasValue && syncRowState.Value.HasFlag(syncRow.RowState)))
                  syncTable.Rows.Add(syncRow);
            }
         }

         return syncTable;
      }

      /// <summary>
      /// Load the Batch part info in memory, in a SyncTable.
      /// </summary>
      internal async Task<SyncTable> InternalLoadTableFromBatchPartInfoAsync(SyncContext context, string directoryPath, string fileName, SyncRowState? syncRowState = default, DbConnection connection = null, DbTransaction transaction = null, CancellationToken cancellationToken = default)
      {
         var fileExists = await this.BatchStorage.FileExistsAsync(directoryPath, fileName, cancellationToken).ConfigureAwait(false);
         if (!fileExists)
            return null;

         await using var localSerializer = new LocalJsonSerializer(this.BatchStorage, this, context);

         // Get table from file
         var (syncTable, _, _) = await localSerializer.GetSchemaTableFromFileAsync(directoryPath, fileName, cancellationToken).ConfigureAwait(false);

         var syncRows = await localSerializer.GetRowsFromFileAsync(directoryPath, fileName, syncTable, cancellationToken).ConfigureAwait(false);
         foreach (var syncRow in syncRows)
         {
            if (!syncRowState.HasValue || syncRowState == default || (syncRowState.HasValue && syncRowState.Value.HasFlag(syncRow.RowState)))
               syncTable.Rows.Add(syncRow);
         }

         return syncTable;
      }

      /// <summary>
      /// Save a sync table to a batch part info.
      /// </summary>
      internal async Task InternalSaveTableToBatchPartInfoAsync(SyncContext context, BatchInfo batchInfo, BatchPartInfo batchPartInfo, SyncTable syncTable, CancellationToken cancellationToken = default)
      {
         if (syncTable?.Rows == null || syncTable.Rows.Count <= 0)
            return;

         // Get full path of my batchpartinfo
         var fullPath = batchInfo.GetBatchPartInfoFullPath(batchPartInfo);
         var directoryPath = Path.GetDirectoryName(fullPath);
         var fileName = Path.GetFileName(fullPath);

         var fileExists = await this.BatchStorage.FileExistsAsync(directoryPath, fileName, cancellationToken).ConfigureAwait(false);
         if (fileExists)
            await this.BatchStorage.DeleteBatchPartAsync(directoryPath, fileName, cancellationToken).ConfigureAwait(false);

         await using var localSerializer = new LocalJsonSerializer(this.BatchStorage, this, context);

         // open the file and write table header
         await localSerializer.OpenFileAsync(directoryPath, fileName, syncTable, batchPartInfo.State, cancellationToken: cancellationToken).ConfigureAwait(false);

         foreach (var row in syncTable.Rows)
            await localSerializer.WriteRowToFileAsync(row, syncTable).ConfigureAwait(false);
      }
   }
}
