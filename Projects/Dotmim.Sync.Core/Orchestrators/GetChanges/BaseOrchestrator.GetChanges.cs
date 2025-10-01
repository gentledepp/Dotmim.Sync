using Dotmim.Sync.Batch;
using Dotmim.Sync.Builders;
using Dotmim.Sync.Enumerations;
using Dotmim.Sync.Serialization;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Dotmim.Sync
{
    /// <summary>
    /// Contains the logic to get changes.
    /// </summary>
    public abstract partial class BaseOrchestrator
    {

        /// <summary>
        /// Gets a batch of changes using unified multi-table batching optimization.
        /// Creates a single ContainerSet instead of separate files per table/operation.
        /// </summary>
        /// <returns>A DbSyncContext object that will be used to retrieve the modified data.</returns>
        internal virtual async Task<DatabaseChangesSelected> InternalGetChangesUnifiedAsync(
                             ScopeInfo scopeInfo, SyncContext context, bool isNew, long? fromLastTimestamp, Guid? excludingScopeId,
                             bool supportsMultiActiveResultSets, BatchInfo batchInfo,
                             DbConnection connection, DbTransaction transaction,
                             IProgress<ProgressArgs> progress, CancellationToken cancellationToken)
        {
            try
            {
                // Statistics about changes that are selected
                DatabaseChangesSelected changesSelected;

                context.SyncStage = SyncStage.ChangesSelecting;

                // Create a new empty in-memory batch info
                if (context.SyncWay == SyncWay.Upload && context.SyncType == SyncType.Reinitialize)
                    return new DatabaseChangesSelected();

                // create local directory
                if (!string.IsNullOrEmpty(batchInfo.DirectoryRoot) && !Directory.Exists(batchInfo.DirectoryRoot))
                    Directory.CreateDirectory(batchInfo.DirectoryRoot);

                changesSelected = new DatabaseChangesSelected();

                var cptSyncTable = 0;
                var currentProgress = context.ProgressPercentage;

                var schemaTables = scopeInfo.Schema.Tables.SortByDependencies(tab => tab.GetRelations().Select(r => r.GetParentTable()));

                var lstTableChangesSelected = new ConcurrentBag<TableChangesSelected>();

                var totalRowsCount = 0;
                var batchPartInfos = new List<BatchPartInfo>();

                var threadNumberLimits = supportsMultiActiveResultSets ? 16 : 1;

                // Semaphore to ensure only one thread at a time can add rows and create batch files
                using var batchingLock = new SemaphoreSlim(1, 1);

                // Unified batch serializer for incremental writing
                var unifiedSerializer = new Serialization.UnifiedBatchSerializer();
                var currentBatchRowCount = 0;

                if (supportsMultiActiveResultSets)
                {
                    await schemaTables.ForEachAsync(
                        async syncTable =>
                        {
                            if (cancellationToken.IsCancellationRequested)
                                return;

                            // tmp count of table for report progress pct
                            cptSyncTable++;

                            TableChangesSelected tableChangesSelected;
                            (context, tableChangesSelected, currentBatchRowCount) = await this.InternalReadSyncTableChangesUnifiedAsync(
                                    scopeInfo, context, excludingScopeId, syncTable, unifiedSerializer, batchInfo, batchPartInfos, batchingLock, isNew, fromLastTimestamp, connection, transaction, progress, cancellationToken).ConfigureAwait(false);

                            if (tableChangesSelected != null && (tableChangesSelected.Deletes > 0 || tableChangesSelected.Upserts > 0))
                            {
                                lstTableChangesSelected.Add(tableChangesSelected);
                                Interlocked.Add(ref totalRowsCount, tableChangesSelected.TotalChanges);
                            }

                            context.ProgressPercentage = currentProgress + (cptSyncTable * 0.2d / scopeInfo.Schema.Tables.Count);
                        }, threadNumberLimits).ConfigureAwait(false);
                }
                else
                {
                    foreach (var syncTable in schemaTables)
                    {
                        if (cancellationToken.IsCancellationRequested)
                            continue;

                        // tmp count of table for report progress pct
                        cptSyncTable++;

                        TableChangesSelected tableChangesSelected;
                        (context, tableChangesSelected, currentBatchRowCount) = await this.InternalReadSyncTableChangesUnifiedAsync(
                                scopeInfo, context, excludingScopeId, syncTable, unifiedSerializer, batchInfo, batchPartInfos, batchingLock, isNew, fromLastTimestamp, connection, transaction, progress, cancellationToken).ConfigureAwait(false);

                        if (tableChangesSelected != null && (tableChangesSelected.Deletes > 0 || tableChangesSelected.Upserts > 0))
                        {
                            lstTableChangesSelected.Add(tableChangesSelected);
                            totalRowsCount += tableChangesSelected.TotalChanges;
                        }

                        context.ProgressPercentage = currentProgress + (cptSyncTable * 0.2d / scopeInfo.Schema.Tables.Count);
                    }
                }

                while (!lstTableChangesSelected.IsEmpty)
                {
                    if (lstTableChangesSelected.TryTake(out var tableChangesSelected))
                        changesSelected.TableChangesSelected.Add(tableChangesSelected);
                }

                // Close any remaining open batch file
                if (unifiedSerializer.IsOpen)
                {
                    await unifiedSerializer.CloseFileAsync().ConfigureAwait(false);

                    if (currentBatchRowCount > 0)
                    {
                        var batchIndex = batchPartInfos.Count - 1;
                        if (batchIndex >= 0)
                        {
                            var lastBatchInfo = batchPartInfos[batchIndex];
                            await this.InterceptAsync(new BatchChangesCreatedArgs(context, lastBatchInfo, null, null, SyncRowState.None, connection, transaction), progress, cancellationToken).ConfigureAwait(false);
                        }
                    }
                }

                // Dispose the serializer
                await unifiedSerializer.DisposeAsync().ConfigureAwait(false);

                // Add all batch part infos created during the process
                foreach (var batchPartInfo in batchPartInfos)
                {
                    batchInfo.BatchPartsInfo.Add(batchPartInfo);
                }

                batchInfo.RowsCount = totalRowsCount;
                batchInfo.EnsureLastBatch();

                if (batchInfo.RowsCount <= 0)
                {
                    var cleanFolder = await this.InternalCanCleanFolderAsync(scopeInfo.Name, context.Parameters, batchInfo, cancellationToken: cancellationToken).ConfigureAwait(false);

                    if (cleanFolder)
                        batchInfo.TryRemoveDirectory();
                }

                return changesSelected;
            }
            catch (Exception ex)
            {
                string message = null;

                if (batchInfo != null && batchInfo.DirectoryRoot != null)
                    message += $"Directory:{batchInfo.DirectoryRoot}.";

                message += $"Supports MultiActiveResultSets:{supportsMultiActiveResultSets}.";
                message += $"Is New:{isNew}.";
                message += $"From:{fromLastTimestamp}.";

                throw this.GetSyncError(context, ex, message);
            }
        }

        /// <summary>
        /// Gets a batch of changes to synchronize when given batch size,
        /// destination knowledge, and change data retriever parameters.
        /// </summary>
        /// <returns>A DbSyncContext object that will be used to retrieve the modified data.</returns>
        internal virtual async Task<DatabaseChangesSelected> InternalGetChangesAsync(
                             ScopeInfo scopeInfo, SyncContext context, bool isNew, long? fromLastTimestamp, Guid? excludingScopeId,
                             bool supportsMultiActiveResultSets, BatchInfo batchInfo,
                             DbConnection connection, DbTransaction transaction,
                             IProgress<ProgressArgs> progress, CancellationToken cancellationToken)
        {
            // Use unified batching if enabled by the client
            if (context.UseUnifiedBatching)
            {
                return await this.InternalGetChangesUnifiedAsync(scopeInfo, context, isNew, fromLastTimestamp, excludingScopeId,
                    supportsMultiActiveResultSets, batchInfo, connection, transaction, progress, cancellationToken).ConfigureAwait(false);
            }

            // Fall back to traditional batching
            try
            {
                // Statistics about changes that are selected
                DatabaseChangesSelected changesSelected;

                context.SyncStage = SyncStage.ChangesSelecting;

                // Create a new empty in-memory batch info
                if (context.SyncWay == SyncWay.Upload && context.SyncType == SyncType.Reinitialize)
                    return new DatabaseChangesSelected();

                // create local directory
                if (!string.IsNullOrEmpty(batchInfo.DirectoryRoot) && !Directory.Exists(batchInfo.DirectoryRoot))
                    Directory.CreateDirectory(batchInfo.DirectoryRoot);

                changesSelected = new DatabaseChangesSelected();

                var cptSyncTable = 0;
                var currentProgress = context.ProgressPercentage;

                var schemaTables = scopeInfo.Schema.Tables.SortByDependencies(tab => tab.GetRelations().Select(r => r.GetParentTable()));

                var lstAllBatchPartInfos = new ConcurrentBag<BatchPartInfo>();
                var lstTableChangesSelected = new ConcurrentBag<TableChangesSelected>();

                var threadNumberLimits = supportsMultiActiveResultSets ? 16 : 1;

                if (supportsMultiActiveResultSets)
                {
                    await schemaTables.ForEachAsync(
                        async syncTable =>
                    {
                        if (cancellationToken.IsCancellationRequested)
                            return;

                        // tmp count of table for report progress pct
                        cptSyncTable++;

                        List<BatchPartInfo> syncTableBatchPartInfos;
                        TableChangesSelected tableChangesSelected;
                        (context, syncTableBatchPartInfos, tableChangesSelected) = await this.InternalReadSyncTableChangesAsync(
                                scopeInfo, context, excludingScopeId, syncTable, batchInfo, isNew, fromLastTimestamp, connection, transaction, progress, cancellationToken).ConfigureAwait(false);

                        if (syncTableBatchPartInfos == null)
                            return;

                        // We don't report progress if no table changes is empty, to limit verbosity
                        if (tableChangesSelected != null && (tableChangesSelected.Deletes > 0 || tableChangesSelected.Upserts > 0))
                            lstTableChangesSelected.Add(tableChangesSelected);

                        // Add sync table bpi to all bpi
                        syncTableBatchPartInfos.ForEach(bpi => lstAllBatchPartInfos.Add(bpi));

                        context.ProgressPercentage = currentProgress + (cptSyncTable * 0.2d / scopeInfo.Schema.Tables.Count);
                    }, threadNumberLimits).ConfigureAwait(false);
                }
                else
                {
                    foreach (var syncTable in schemaTables)
                    {
                        if (cancellationToken.IsCancellationRequested)
                            continue;

                        // tmp count of table for report progress pct
                        cptSyncTable++;

                        List<BatchPartInfo> syncTableBatchPartInfos;
                        TableChangesSelected tableChangesSelected;
                        (context, syncTableBatchPartInfos, tableChangesSelected) = await this.InternalReadSyncTableChangesAsync(
                                scopeInfo, context, excludingScopeId, syncTable, batchInfo, isNew, fromLastTimestamp, connection, transaction, progress, cancellationToken).ConfigureAwait(false);

                        if (syncTableBatchPartInfos == null)
                            continue;

                        // We don't report progress if no table changes is empty, to limit verbosity
                        if (tableChangesSelected != null && (tableChangesSelected.Deletes > 0 || tableChangesSelected.Upserts > 0))
                            lstTableChangesSelected.Add(tableChangesSelected);

                        // Add sync table bpi to all bpi
                        syncTableBatchPartInfos.ForEach(bpi => lstAllBatchPartInfos.Add(bpi));

                        context.ProgressPercentage = currentProgress + (cptSyncTable * 0.2d / scopeInfo.Schema.Tables.Count);
                    }
                }

                while (!lstTableChangesSelected.IsEmpty)
                {
                    if (lstTableChangesSelected.TryTake(out var tableChangesSelected))
                        changesSelected.TableChangesSelected.Add(tableChangesSelected);
                }

                // Ensure correct order
                this.EnsureLastBatchInfo(scopeInfo, context, batchInfo, lstAllBatchPartInfos, schemaTables);

                if (batchInfo.RowsCount <= 0)
                {
                    var cleanFolder = await this.InternalCanCleanFolderAsync(scopeInfo.Name, context.Parameters, batchInfo, cancellationToken: cancellationToken).ConfigureAwait(false);

                    if (cleanFolder)
                        batchInfo.TryRemoveDirectory();
                }

                return changesSelected;
            }
            catch (Exception ex)
            {
                string message = null;

                if (batchInfo != null && batchInfo.DirectoryRoot != null)
                    message += $"Directory:{batchInfo.DirectoryRoot}.";

                message += $"Supports MultiActiveResultSets:{supportsMultiActiveResultSets}.";
                message += $"Is New:{isNew}.";
                message += $"From:{fromLastTimestamp}.";

                throw this.GetSyncError(context, ex, message);
            }
        }

        /// <summary>
        /// Read changes from a sync table and add to unified batch file incrementally.
        /// </summary>
        internal virtual async Task<(SyncContext Context, TableChangesSelected TableChangesSelected, int CurrentBatchRowCount)> InternalReadSyncTableChangesUnifiedAsync(
            ScopeInfo scopeInfo, SyncContext context, Guid? excludintScopeId, SyncTable syncTable,
            Serialization.UnifiedBatchSerializer unifiedSerializer, BatchInfo batchInfo, List<BatchPartInfo> batchPartInfos,
            SemaphoreSlim batchingLock, bool isNew, long? lastTimestamp,
            DbConnection connection, DbTransaction transaction,
            IProgress<ProgressArgs> progress, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
                return default;

            DbCommand selectIncrementalChangesCommand = null;

            var currentBatchRowCount = 0;

            try
            {
                var setupTable = scopeInfo.Setup.Tables[syncTable.TableName, syncTable.SchemaName];

                if (setupTable == null)
                    return (context, default, 0);

                // Only table schema is replicated, no datas are applied
                if (setupTable.SyncDirection == SyncDirection.None)
                    return (context, default, 0);

                // if we are in upload stage, so check if table is not download only
                if (context.SyncWay == SyncWay.Upload && setupTable.SyncDirection == SyncDirection.DownloadOnly)
                    return (context, default, 0);

                // if we are in download stage, so check if table is not download only
                if (context.SyncWay == SyncWay.Download && setupTable.SyncDirection == SyncDirection.UploadOnly)
                    return (context, default, 0);

                DbCommandType dbCommandType;
                (selectIncrementalChangesCommand, dbCommandType) = await this.InternalGetSelectChangesCommandAsync(scopeInfo, context, syncTable, isNew,
                        connection, transaction).ConfigureAwait(false);

                if (selectIncrementalChangesCommand == null)
                    return (context, default, 0);

                // Get correct adapter
                var syncAdapter = this.GetSyncAdapter(syncTable, scopeInfo);

                this.InternalSetCommandParametersValues(context, selectIncrementalChangesCommand, dbCommandType, syncAdapter, connection, transaction,
                    sync_scope_id: excludintScopeId, sync_min_timestamp: lastTimestamp, progress: progress, cancellationToken: cancellationToken);

                var schemaChangesTable = CreateChangesTable(syncTable);

                // Statistics
                var tableChangesSelected = new TableChangesSelected(schemaChangesTable.TableName, schemaChangesTable.SchemaName);
                var tableRowCount = 0;

                // launch interceptor if any
                var args = await this.InterceptAsync(new TableChangesSelectingArgs(context, schemaChangesTable, selectIncrementalChangesCommand, connection, transaction), progress, cancellationToken).ConfigureAwait(false);

                if (!args.Cancel && args.Command != null)
                {
                    await this.InterceptAsync(new ExecuteCommandArgs(context, args.Command, dbCommandType, connection, transaction), progress, cancellationToken).ConfigureAwait(false);

                    // Get the reader
                    using var dataReader = await args.Command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

                    // Lock to ensure only one table's rows are written at a time (prevents mixing rows from different tables)
                    await batchingLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        // Check if the correct table is open in the serializer
                        var requiredTableKey = $"{schemaChangesTable.SchemaName}.{schemaChangesTable.TableName}";

                        while (await dataReader.ReadAsync(cancellationToken).ConfigureAwait(false))
                        {
                            // Create a row from dataReader
                            var syncRow = this.CreateSyncRowFromReader(context, dataReader, schemaChangesTable);

                            var tableChangesSelectedSyncRowArgs = await this.InterceptAsync(new RowsChangesSelectedArgs(context, syncRow, schemaChangesTable, connection, transaction), progress, cancellationToken).ConfigureAwait(false);
                            syncRow = tableChangesSelectedSyncRowArgs.SyncRow;

                            if (syncRow == null)
                                continue;

                            // Open batch file if not yet open
                            if (!unifiedSerializer.IsOpen)
                            {
                                var batchIndex = batchPartInfos.Count;
                                var batchPartFileName = $"BATCH_{batchIndex:0000}.json";
                                var batchPartFullPath = Path.Combine(batchInfo.GetDirectoryFullPath(), batchPartFileName);

                                // Ensure directory exists
                                var directoryPath = batchInfo.GetDirectoryFullPath();
                                if (!Directory.Exists(directoryPath))
                                    Directory.CreateDirectory(directoryPath);

                                await unifiedSerializer.OpenFileAsync(batchPartFullPath).ConfigureAwait(false);

                                var batchPartInfo = new BatchPartInfo(batchPartFileName, "UNIFIED", string.Empty, SyncRowState.None, 0, batchIndex)
                                {
                                    IsLastBatch = false,
                                    TableRowCounts = new Dictionary<string, int>()
                                };
                                batchPartInfos.Add(batchPartInfo);
                                currentBatchRowCount = 0;
                            }

                            var currentTableKeyInSerializer = unifiedSerializer.CurrentTableKey;
                            if (currentTableKeyInSerializer != requiredTableKey)
                            {
                                // Close previous table if a different table was open
                                if (unifiedSerializer.HasCurrentTable)
                                    await unifiedSerializer.CloseCurrentTableAsync().ConfigureAwait(false);

                                // Open the correct table for this row
                                await unifiedSerializer.OpenTableAsync(schemaChangesTable).ConfigureAwait(false);
                            }

                            // Write row with RowState included at position 0
                            var batchSizeInBytes = await unifiedSerializer.WriteRowAsync(syncRow, schemaChangesTable).ConfigureAwait(false);

                            // Update statistics
                            if (syncRow.RowState == SyncRowState.Deleted)
                                tableChangesSelected.Deletes++;
                            else
                                tableChangesSelected.Upserts++;

                            tableRowCount++;
                            currentBatchRowCount++;

                            // Update row count in current batch part info
                            if (batchPartInfos.Count > 0)
                            {
                                var currentBatchPartInfo = batchPartInfos[batchPartInfos.Count - 1];
                                currentBatchPartInfo.RowsCount = currentBatchRowCount;

                                // Update per-table row count for unified batches
                                if (currentBatchPartInfo.TableRowCounts != null)
                                {
                                    if (!currentBatchPartInfo.TableRowCounts.ContainsKey(requiredTableKey))
                                        currentBatchPartInfo.TableRowCounts[requiredTableKey] = 0;

                                    currentBatchPartInfo.TableRowCounts[requiredTableKey]++;
                                }
                            }

                            var newSizeKB = batchSizeInBytes / 1024L;
                            // Check if we exceeded the batch size limit
                            if (newSizeKB > this.Options.BatchSize)
                            {
                                // Close current table and file
                                await unifiedSerializer.CloseCurrentTableAsync().ConfigureAwait(false);
                                await unifiedSerializer.CloseFileAsync().ConfigureAwait(false);

                                // Fire interceptor for completed batch
                                if (batchPartInfos.Count > 0)
                                {
                                    var completedBatchPartInfo = batchPartInfos[batchPartInfos.Count - 1];
                                    await this.InterceptAsync(new BatchChangesCreatedArgs(context, completedBatchPartInfo, null, null, SyncRowState.None, connection, transaction), progress, cancellationToken).ConfigureAwait(false);
                                }
                            }
                        }
                    }
                    finally
                    {
                        batchingLock.Release();
                    }

#if NET6_0_OR_GREATER
                    await dataReader.CloseAsync().ConfigureAwait(false);
#else
                    dataReader.Close();
#endif
                }

                var tableChangesSelectedArgs = new TableChangesSelectedArgs(context, null, null, syncTable, tableChangesSelected, connection, transaction);
                await this.InterceptAsync(tableChangesSelectedArgs, progress, cancellationToken).ConfigureAwait(false);

                return (context, tableChangesSelected, currentBatchRowCount);
            }
            catch (Exception ex)
            {
                string message = null;

                if (selectIncrementalChangesCommand != null)
                    message += $"SelectChangesCommand:{selectIncrementalChangesCommand.CommandText}.";

                if (syncTable != null)
                    message += $"Table:{syncTable.GetFullName()}.";

                message += $"Is New:{isNew}.";

                message += $"LastTimestamp:{lastTimestamp}.";

                throw this.GetSyncError(context, ex, message);
            }
        }

        /// <summary>
        /// Create unified batch file from ContainerSet.
        /// </summary>
        internal virtual async Task<BatchPartInfo> InternalCreateUnifiedBatchFileAsync(SyncContext context, BatchInfo batchInfo, ContainerSet containerSet, int rowsCount, int batchIndex,
            DbConnection connection, DbTransaction transaction,
            IProgress<ProgressArgs> progress, CancellationToken cancellationToken)
        {
            try
            {
                var batchPartFileName = $"BATCH_{batchIndex:0000}.json";
                var batchPartFullPath = Path.Combine(batchInfo.GetDirectoryFullPath(), batchPartFileName);

                // Ensure directory exists
                var directoryPath = batchInfo.GetDirectoryFullPath();
                if (!Directory.Exists(directoryPath))
                    Directory.CreateDirectory(directoryPath);

                // Serialize the unified container set to file
                var serializer = SerializersFactory.JsonSerializerFactory.GetSerializer();

                using (var fs = new FileStream(batchPartFullPath, FileMode.Create, FileAccess.Write))
                {
                    var data = await serializer.SerializeAsync(containerSet).ConfigureAwait(false);
                    await fs.WriteAsync(data, 0, data.Length, cancellationToken).ConfigureAwait(false);
                }

                // Create batch part info for the unified batch1
                var batchPartInfo = new BatchPartInfo(batchPartFileName, "UNIFIED", string.Empty, SyncRowState.None, rowsCount, batchIndex)
                {
                    IsLastBatch = true
                };

                await this.InterceptAsync(new BatchChangesCreatedArgs(context, batchPartInfo, null, null, SyncRowState.None, connection, transaction), progress, cancellationToken).ConfigureAwait(false);

                return batchPartInfo;
            }
            catch (Exception ex)
            {
                throw this.GetSyncError(context, ex, "Error creating unified batch file");
            }
        }

        /// <summary>
        /// Read changes from a sync table.
        /// </summary>
        internal virtual async Task<(SyncContext Context, List<BatchPartInfo> BatchPartInfos, TableChangesSelected TableChangesSelected)>
            InternalReadSyncTableChangesAsync(
            ScopeInfo scopeInfo, SyncContext context, Guid? excludintScopeId, SyncTable syncTable,
            BatchInfo batchInfo, bool isNew, long? lastTimestamp,
            DbConnection connection, DbTransaction transaction,
            IProgress<ProgressArgs> progress, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
                return default;

            DbCommand selectIncrementalChangesCommand = null;

            var localSerializerModified = new LocalJsonSerializer(this, context);
            var localSerializerDeleted = new LocalJsonSerializer(this, context);
            try
            {
                var setupTable = scopeInfo.Setup.Tables[syncTable.TableName, syncTable.SchemaName];

                if (setupTable == null)
                    return (context, default, default);

                // Only table schema is replicated, no datas are applied
                if (setupTable.SyncDirection == SyncDirection.None)
                    return (context, default, default);

                // if we are in upload stage, so check if table is not download only
                if (context.SyncWay == SyncWay.Upload && setupTable.SyncDirection == SyncDirection.DownloadOnly)
                    return (context, default, default);

                // if we are in download stage, so check if table is not download only
                if (context.SyncWay == SyncWay.Download && setupTable.SyncDirection == SyncDirection.UploadOnly)
                    return (context, default, default);

                DbCommandType dbCommandType;
                (selectIncrementalChangesCommand, dbCommandType) = await this.InternalGetSelectChangesCommandAsync(scopeInfo, context, syncTable, isNew,
                        connection, transaction).ConfigureAwait(false);

                if (selectIncrementalChangesCommand == null)
                    return (context, default, default);

                // Get correct adapter
                var syncAdapter = this.GetSyncAdapter(syncTable, scopeInfo);

                this.InternalSetCommandParametersValues(context, selectIncrementalChangesCommand, dbCommandType, syncAdapter, connection, transaction,
                    sync_scope_id: excludintScopeId, sync_min_timestamp: lastTimestamp, progress: progress, cancellationToken: cancellationToken);

                var schemaChangesTable = CreateChangesTable(syncTable);

                // Statistics
                var tableChangesSelected = new TableChangesSelected(schemaChangesTable.TableName, schemaChangesTable.SchemaName);

                // var rowsCountInBatchModified = 0;
                // var rowsCountInBatchDeleted = 0;
                var batchPartInfos = new List<BatchPartInfo>();

                BatchPartInfo batchPartInfoUpserts = null;
                BatchPartInfo batchPartInfoDeleted = null;

                // launch interceptor if any
                var args = await this.InterceptAsync(new TableChangesSelectingArgs(context, schemaChangesTable, selectIncrementalChangesCommand, connection, transaction), progress, cancellationToken).ConfigureAwait(false);

                if (!args.Cancel && args.Command != null)
                {
                    await this.InterceptAsync(new ExecuteCommandArgs(context, args.Command, dbCommandType, connection, transaction), progress, cancellationToken).ConfigureAwait(false);

                    // Get the reader
                    using var dataReader = await args.Command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

                    while (await dataReader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        // Create a row from dataReader
                        var syncRow = this.CreateSyncRowFromReader(context, dataReader, schemaChangesTable);

                        var tableChangesSelectedSyncRowArgs = await this.InterceptAsync(new RowsChangesSelectedArgs(context, syncRow, schemaChangesTable, connection, transaction), progress, cancellationToken).ConfigureAwait(false);
                        syncRow = tableChangesSelectedSyncRowArgs.SyncRow;

                        if (syncRow == null)
                            continue;

                        if (syncRow.RowState == SyncRowState.Deleted)
                            batchPartInfoDeleted = await this.InternalAddRowToBatchPartInfoAsync(context, localSerializerDeleted, syncRow, batchInfo, batchPartInfoDeleted, batchPartInfos, schemaChangesTable, tableChangesSelected, connection, transaction, progress, cancellationToken).ConfigureAwait(false);
                        else
                            batchPartInfoUpserts = await this.InternalAddRowToBatchPartInfoAsync(context, localSerializerModified, syncRow, batchInfo, batchPartInfoUpserts, batchPartInfos, schemaChangesTable, tableChangesSelected, connection, transaction, progress, cancellationToken).ConfigureAwait(false);
                    }

#if NET6_0_OR_GREATER
                    await dataReader.CloseAsync().ConfigureAwait(false);
#else
                    dataReader.Close();
#endif

                    // tmp Func
                    var closeSerializer = new Func<LocalJsonSerializer, BatchChangesCreatedArgs, Task>(async (localJsonSerializer, args) =>
                    {
                        // Close file
                        if (localJsonSerializer != null && localJsonSerializer.IsOpen)
                        {
                            await localJsonSerializer.CloseFileAsync().ConfigureAwait(false);
                            await this.InterceptAsync(args, progress, cancellationToken).ConfigureAwait(false);
                        }
                    });

                    if (batchPartInfoUpserts != null || batchPartInfoDeleted != null)
                    {
                        if (batchPartInfoUpserts?.Index > batchPartInfoDeleted?.Index)
                        {
                            await closeSerializer(localSerializerDeleted, new BatchChangesCreatedArgs(context, batchPartInfoDeleted, schemaChangesTable, tableChangesSelected, SyncRowState.Deleted, connection, transaction)).ConfigureAwait(false);
                            await closeSerializer(localSerializerModified, new BatchChangesCreatedArgs(context, batchPartInfoUpserts, schemaChangesTable, tableChangesSelected, SyncRowState.Modified, connection, transaction)).ConfigureAwait(false);
                        }
                        else
                        {
                            await closeSerializer(localSerializerModified, new BatchChangesCreatedArgs(context, batchPartInfoUpserts, schemaChangesTable, tableChangesSelected, SyncRowState.Modified, connection, transaction)).ConfigureAwait(false);
                            await closeSerializer(localSerializerDeleted, new BatchChangesCreatedArgs(context, batchPartInfoDeleted, schemaChangesTable, tableChangesSelected, SyncRowState.Deleted, connection, transaction)).ConfigureAwait(false);
                        }
                    }

                    // Close file
                    if (localSerializerModified.IsOpen)
                    {
                        await localSerializerModified.CloseFileAsync().ConfigureAwait(false);
                        await this.InterceptAsync(new BatchChangesCreatedArgs(context, batchPartInfoUpserts, schemaChangesTable, tableChangesSelected, SyncRowState.Modified, connection, transaction), progress, cancellationToken).ConfigureAwait(false);
                    }

                    if (localSerializerDeleted.IsOpen)
                    {
                        await localSerializerDeleted.CloseFileAsync().ConfigureAwait(false);
                        await this.InterceptAsync(new BatchChangesCreatedArgs(context, batchPartInfoDeleted, schemaChangesTable, tableChangesSelected, SyncRowState.Deleted, connection, transaction), progress, cancellationToken).ConfigureAwait(false);
                    }
                }

                foreach (var bpi in batchPartInfos.ToArray())
                {
                    var fullPath = batchInfo.GetBatchPartInfoFullPath(bpi);

                    if (fullPath != null && bpi.RowsCount == 0 && File.Exists(fullPath))
                    {
                        File.Delete(fullPath);
                        batchPartInfos.Remove(bpi);
                    }
                }

                var tableChangesSelectedArgs = new TableChangesSelectedArgs(context, batchInfo, batchPartInfos, syncTable, tableChangesSelected, connection, transaction);
                await this.InterceptAsync(tableChangesSelectedArgs, progress, cancellationToken).ConfigureAwait(false);

                return (context, batchPartInfos, tableChangesSelected);
            }
            catch (Exception ex)
            {
                string message = null;

                if (selectIncrementalChangesCommand != null)
                    message += $"SelectChangesCommand:{selectIncrementalChangesCommand.CommandText}.";

                if (syncTable != null)
                    message += $"Table:{syncTable.GetFullName()}.";

                message += $"Is New:{isNew}.";

                message += $"LastTimestamp:{lastTimestamp}.";

                throw this.GetSyncError(context, ex, message);
            }
            finally
            {
                await localSerializerModified.DisposeAsync().ConfigureAwait(false);
                await localSerializerDeleted.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Add a row to unified batch using ContainerSet for multi-table batching optimization.
        /// </summary>
        internal void InternalAddRowToUnifiedBatch(ContainerTable containerTable, SyncRow syncRow, SyncTable schemaChangesTable, TableChangesSelected tableChangesSelected)
        {
            // Add the entire SyncRow buffer (including state at position 0) to the container table
            // Format: [state, col1, col2, ..., colN]
            containerTable.Rows.Add(syncRow.ToArray());

            // Update statistics
            if (syncRow.RowState == SyncRowState.Deleted)
                tableChangesSelected.Deletes++;
            else
                tableChangesSelected.Upserts++;
        }

        /// <summary>
        /// Add a row to a batch part info.
        /// </summary>
        internal async Task<BatchPartInfo> InternalAddRowToBatchPartInfoAsync(SyncContext context, LocalJsonSerializer localJsonSerializer, SyncRow syncRow, BatchInfo batchInfo,
            BatchPartInfo batchPartInfo, List<BatchPartInfo> batchPartInfos, SyncTable schemaChangesTable, TableChangesSelected tableChangesSelected,
            DbConnection connection, DbTransaction transaction,
            IProgress<ProgressArgs> progress, CancellationToken cancellationToken)
        {
            // open the file and write table header for all deleted rows
            var ext = syncRow.RowState == SyncRowState.Deleted ? "DELETED" : "UPSERTS";
            var index = batchPartInfos != null ? batchPartInfos.Count : 0;

            if (!localJsonSerializer.IsOpen)
            {
                var (batchPartInfoFullPath, batchPartFileName) = batchInfo.GetNewBatchPartInfoPath(schemaChangesTable, index, LocalJsonSerializer.Extension, ext);
                await localJsonSerializer.OpenFileAsync(batchPartInfoFullPath, schemaChangesTable, syncRow.RowState).ConfigureAwait(false);

                batchPartInfo = new BatchPartInfo(batchPartFileName, schemaChangesTable.TableName, schemaChangesTable.SchemaName, syncRow.RowState, 0, index);
                batchPartInfos.Add(batchPartInfo);
            }

            if (syncRow.RowState == SyncRowState.Deleted)
                tableChangesSelected.Deletes++;
            else
                tableChangesSelected.Upserts++;

            await localJsonSerializer.WriteRowToFileAsync(syncRow, schemaChangesTable).ConfigureAwait(false);
            batchPartInfo.RowsCount++;

            var currentBatchSize = await localJsonSerializer.GetCurrentFileSizeAsync().ConfigureAwait(false);

            if (currentBatchSize > this.Options.BatchSize && localJsonSerializer.IsOpen)
            {
                await localJsonSerializer.CloseFileAsync().ConfigureAwait(false);
                await this.InterceptAsync(new BatchChangesCreatedArgs(context, batchPartInfo, schemaChangesTable, tableChangesSelected, syncRow.RowState, connection, transaction), progress, cancellationToken).ConfigureAwait(false);
            }

            return batchPartInfo;
        }

        /// <summary>
        /// Gets changes rows count estimation.
        /// </summary>
        internal virtual async Task<(SyncContext Context, DatabaseChangesSelected DatabaseChangesSelected)> InternalGetEstimatedChangesCountAsync(
                             ScopeInfo scopeInfo, SyncContext context, bool isNew, long? fromLastTimestamp, Guid? excludingScopeId,
                             bool supportsMultiActiveResultSets,
                             DbConnection connection, DbTransaction transaction,
                             IProgress<ProgressArgs> progress, CancellationToken cancellationToken)
        {

            try
            {
                context.SyncStage = SyncStage.ChangesSelecting;

                // Create stats object to store changes count
                var changes = new DatabaseChangesSelected();

                // Call interceptor
                var databaseChangesSelectingArgs = new DatabaseChangesSelectingArgs(context, default, this.Options.BatchSize, true,
                    fromLastTimestamp, connection, transaction);

                await this.InterceptAsync(databaseChangesSelectingArgs, progress, cancellationToken).ConfigureAwait(false);

                if (context.SyncWay == SyncWay.Upload && context.SyncType == SyncType.Reinitialize)
                    return (context, changes);

                var threadNumberLimits = supportsMultiActiveResultSets ? 8 : 1;

                await scopeInfo.Schema.Tables.ForEachAsync(
                    async syncTable =>
                {
                    if (cancellationToken.IsCancellationRequested)
                        return;

                    var setupTable = scopeInfo.Setup.Tables[syncTable.TableName, syncTable.SchemaName];

                    if (setupTable == null)
                        return;

                    // Only table schema is replicated, no datas are applied
                    if (setupTable.SyncDirection == SyncDirection.None)
                        return;

                    // if we are in upload stage, so check if table is not download only
                    if (context.SyncWay == SyncWay.Upload && setupTable.SyncDirection == SyncDirection.DownloadOnly)
                        return;

                    // if we are in download stage, so check if table is not download only
                    if (context.SyncWay == SyncWay.Download && setupTable.SyncDirection == SyncDirection.UploadOnly)
                        return;

                    // Get correct adapter
                    var syncAdapter = this.GetSyncAdapter(syncTable, scopeInfo);

                    // Get Command
                    var (command, dbCommandType) = await this.InternalGetSelectChangesCommandAsync(scopeInfo, context, syncTable, isNew, connection, transaction).ConfigureAwait(false);

                    if (command == null)
                        return;

                    this.InternalSetCommandParametersValues(context, command, dbCommandType, syncAdapter, connection, transaction,
                        sync_scope_id: excludingScopeId, sync_min_timestamp: fromLastTimestamp, progress: progress, cancellationToken: cancellationToken);

                    // launch interceptor if any
                    var args = new TableChangesSelectingArgs(context, syncTable, command, connection, transaction);
                    await this.InterceptAsync(args, progress, cancellationToken).ConfigureAwait(false);

                    if (args.Cancel || args.Command == null)
                        return;

                    // Statistics
                    var tableChangesSelected = new TableChangesSelected(syncTable.TableName, syncTable.SchemaName);

                    await this.InterceptAsync(new ExecuteCommandArgs(context, args.Command, dbCommandType, connection, transaction), progress, cancellationToken).ConfigureAwait(false);

                    // Get the reader
                    using var dataReader = await args.Command.ExecuteReaderAsync().ConfigureAwait(false);

                    while (await dataReader.ReadAsync().ConfigureAwait(false))
                    {
                        var isTombstone = false;
                        for (var i = 0; i < dataReader.FieldCount; i++)
                        {
                            var columnName = dataReader.GetName(i);

                            // if we have the tombstone value, do not add it to the table
                            if (columnName == "sync_row_is_tombstone")
                            {
                                var objIsTombstone = dataReader.GetValue(i);
                                isTombstone = objIsTombstone != DBNull.Value && SyncTypeConverter.TryConvertTo<long>(objIsTombstone) > 0;
                                continue;
                            }

                            if (columnName == "sync_update_scope_id")
                                continue;
                        }

                        // Set the correct state to be applied
                        if (isTombstone)
                            tableChangesSelected.Deletes++;
                        else
                            tableChangesSelected.Upserts++;
                    }

#if NET6_0_OR_GREATER
                    await dataReader.CloseAsync().ConfigureAwait(false);
#else
                    dataReader.Close();
#endif

                    // Check interceptor
                    var changesArgs = new TableChangesSelectedArgs(context, null, null, syncTable, tableChangesSelected, connection, transaction);
                    await this.InterceptAsync(changesArgs, progress, cancellationToken).ConfigureAwait(false);

                    if (tableChangesSelected.Deletes > 0 || tableChangesSelected.Upserts > 0)
                        changes.TableChangesSelected.Add(tableChangesSelected);
                }, threadNumberLimits).ConfigureAwait(false);

                var databaseChangesSelectedArgs = new DatabaseChangesSelectedArgs(context, fromLastTimestamp,
                            default, changes, connection, transaction);

                await this.InterceptAsync(databaseChangesSelectedArgs, progress, cancellationToken).ConfigureAwait(false);

                return (context, changes);
            }
            catch (Exception ex)
            {
                string message = null;

                message += $"Supports MultiActiveResultSets:{supportsMultiActiveResultSets}.";
                message += $"Is New:{isNew}.";
                message += $"From:{fromLastTimestamp}.";

                throw this.GetSyncError(context, ex, message);
            }
        }

        /// <summary>
        /// Get the correct Select changes command
        /// Can be either
        /// - SelectInitializedChanges              : All changes for first sync
        /// - SelectChanges                         : All changes filtered by timestamp
        /// - SelectInitializedChangesWithFilters   : All changes for first sync with filters
        /// - SelectChangesWithFilters              : All changes filtered by timestamp with filters.
        /// </summary>
        internal async Task<(DbCommand Command, DbCommandType CommandType)> InternalGetSelectChangesCommandAsync(ScopeInfo scopeInfo, SyncContext context,
            SyncTable syncTable, bool isNew, DbConnection connection, DbTransaction transaction)
        {
            var dbCommandType = DbCommandType.None;
            SyncFilter tableFilter = null;

            try
            {
                // Sqlite does not have any filter, since he can't be server side
                // if (this.Provider != null && this.Provider.CanBeServerProvider)
                if (this.Provider != null)
                    tableFilter = syncTable.GetFilter();

                var hasFilters = tableFilter != null;

                // Determing the correct DbCommandType
                if (isNew && hasFilters)
                    dbCommandType = DbCommandType.SelectInitializedChangesWithFilters;
                else if (isNew && !hasFilters)
                    dbCommandType = DbCommandType.SelectInitializedChanges;
                else dbCommandType = !isNew && hasFilters ? DbCommandType.SelectChangesWithFilters : DbCommandType.SelectChanges;

                // Get correct Select incremental changes command
                var syncAdapter = this.GetSyncAdapter(syncTable, scopeInfo);

                var (command, _) = await this.InternalGetCommandAsync(scopeInfo, context, syncAdapter, dbCommandType,
                    connection, transaction, default, default).ConfigureAwait(false);

                return (command, dbCommandType);
            }
            catch (Exception ex)
            {
                string message = null;

                if (syncTable != null)
                    message += $"Table:{syncTable.GetFullName()}.";

                message += $"Is New:{isNew}.";

                if (dbCommandType != DbCommandType.None)
                    message += $"dbCommandType:{dbCommandType}.";

                throw this.GetSyncError(context, ex, message);
            }
        }

        ///// <summary>
        ///// Set common parameters to SelectChanges Sql command
        ///// </summary>
        // internal async Task InternalSetSelectChangesCommonParametersAsync(SyncContext context, SyncTable syncTable, Guid? excludingScopeId, bool isNew, long? lastTimestamp,
        //    DbCommandType commandType, DbCommand selectIncrementalChangesCommand, DbSyncAdapter adapter, DbConnection connection, DbTransaction transaction)
        // {
        //    try
        //    {
        //        // Set the parameters
        //        await adapter.AddCommandParameterValueAsync("sync_min_timestamp", lastTimestamp, commandType, selectIncrementalChangesCommand, connection, transaction).ConfigureAwait(false);
        //        await adapter.AddCommandParameterValueAsync("sync_scope_id", excludingScopeId.HasValue ? excludingScopeId.Value : DBNull.Value, commandType, selectIncrementalChangesCommand, connection, transaction).ConfigureAwait(false);

        // // Check filters
        //        SyncFilter tableFilter = null;

        // // Sqlite does not have any filter, since he can't be server side
        //        if (this.Provider != null && this.Provider.CanBeServerProvider)
        //            tableFilter = syncTable.GetFilter();

        // var hasFilters = tableFilter != null;

        // if (!hasFilters)
        //            return;

        // // context parameters can be null at some point.
        //        var contexParameters = context.Parameters ?? new SyncParameters();

        // foreach (var filterParam in tableFilter.Parameters)
        //        {
        //            var parameter = contexParameters.FirstOrDefault(p =>
        //                p.Name.Equals(filterParam.Name, SyncGlobalization.DataSourceStringComparison));

        // object val = parameter?.Value;

        // await adapter.AddCommandParameterValueAsync(filterParam.Name, val, commandType, selectIncrementalChangesCommand, connection, transaction).ConfigureAwait(false);
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        string message = null;

        // if (syncTable != null)
        //            message += $"Table:{syncTable.GetFullName()}.";

        // message += $"Is New:{isNew}.";
        //        message += $"lastTimestamp:{lastTimestamp}.";

        // throw GetSyncError(context, ex, message);
        //    }
        // }

        /// <summary>
        /// Create a new SyncRow from a dataReader.
        /// </summary>
        internal SyncRow CreateSyncRowFromReader(SyncContext context, IDataReader dataReader, SyncTable schemaTable)
        {
            // Create a new row, based on table structure
            SyncRow syncRow = null;
            try
            {
                syncRow = new SyncRow(schemaTable);

                var isTombstone = false;

                for (var i = 0; i < dataReader.FieldCount; i++)
                {
                    var columnName = dataReader.GetName(i);

                    // if we have the tombstone value, do not add it to the table
                    if (columnName == "sync_row_is_tombstone")
                    {
                        var objIsTombstone = dataReader.GetValue(i);
                        isTombstone = objIsTombstone != DBNull.Value && SyncTypeConverter.TryConvertTo<long>(objIsTombstone) > 0;
                        continue;
                    }

                    if (columnName == "sync_update_scope_id")
                        continue;

                    var columnValueObject = dataReader.GetValue(i);
                    var columnValue = columnValueObject == DBNull.Value ? null : columnValueObject;

                    syncRow[i] = columnValue;
                }

                syncRow.RowState = isTombstone ? SyncRowState.Deleted : SyncRowState.Modified;
                return syncRow;
            }
            catch (Exception ex)
            {
                string message = null;

                if (schemaTable != null)
                    message += $"Table:{schemaTable.GetFullName()}.";

                if (syncRow != null)
                    message += $"Row:{syncRow}.";

                throw this.GetSyncError(context, ex, message);
            }
        }

        /// <summary>
        /// Ensure we have a correct order for last batch in batch part infos.
        /// </summary>
        internal void EnsureLastBatchInfo(ScopeInfo scopeInfo, SyncContext context, BatchInfo batchInfo, IEnumerable<BatchPartInfo> lstAllBatchPartInfos, IEnumerable<SyncTable> schemaTables)
        {
            try
            {
                if (lstAllBatchPartInfos == null)
                    return;

                var batchPartInfos = lstAllBatchPartInfos.ToList();

                // delete all empty batchparts (empty tables)
                foreach (var bpi in batchPartInfos.Where(bpi => bpi.RowsCount <= 0))
                    File.Delete(Path.Combine(batchInfo.GetDirectoryFullPath(), bpi.FileName));

                // Generate a good index order to be compliant with previous versions
                var tmpLstBatchPartInfos = new List<BatchPartInfo>();
                foreach (var table in schemaTables)
                {
                    // get all bpi where count > 0 and ordered by index
                    foreach (var bpi in batchPartInfos.Where(bpi => bpi.RowsCount > 0 && bpi.EqualsByName(new BatchPartInfo { TableName = table.TableName, SchemaName = table.SchemaName })).OrderBy(bpi => bpi.Index).ToArray())
                    {
                        batchInfo.BatchPartsInfo.Add(bpi);
                        batchInfo.RowsCount += bpi.RowsCount;

                        tmpLstBatchPartInfos.Add(bpi);
                    }
                }

                var newBatchIndex = 0;
                foreach (var bpi in tmpLstBatchPartInfos)
                {
                    bpi.Index = newBatchIndex;
                    newBatchIndex++;
                    bpi.IsLastBatch = newBatchIndex == tmpLstBatchPartInfos.Count;
                }

                // Set the total rows count contained in the batch info
                batchInfo.EnsureLastBatch();
            }
            catch (Exception ex)
            {
                string message = null;

                if (batchInfo != null && batchInfo.DirectoryRoot != null)
                    message += $"Directory:{batchInfo.DirectoryRoot}.";

                if (batchInfo != null && batchInfo.DirectoryName != null)
                    message += $"Folder:{batchInfo.DirectoryName}.";

                throw this.GetSyncError(context, ex, message);
            }
        }
    }
}