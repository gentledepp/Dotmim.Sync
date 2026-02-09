using Wormhole.Sync.Batch;
using Wormhole.Sync.Enumerations;
using Wormhole.Sync.Serialization;
using System;
using System.Data.Common;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Wormhole.Sync.Web.Client
{
    /// <summary>
    /// Contains the logic to get changes from the server side.
    /// </summary>
    public partial class WebRemoteOrchestrator : RemoteOrchestrator
    {

        /// <inheritdoc cref="RemoteOrchestrator.GetChangesAsync(ScopeInfoClient, DbConnection, DbTransaction)"/>
        public override async Task<ServerSyncChanges> GetChangesAsync(ScopeInfoClient cScopeInfoClient, DbConnection connection = null, DbTransaction transaction = null)
        {
            var context = new SyncContext(Guid.NewGuid(), cScopeInfoClient.Name, cScopeInfoClient.Parameters) { ClientId = cScopeInfoClient.Id, UseUnifiedBatching = this.Options.UseUnifiedBatching };

            // Create the BatchInfo
            var serverBatchInfo = new BatchInfo();

            try
            {
                // Get the server scope to start a new session
                ScopeInfo sScopeInfo;
                (context, sScopeInfo, _) = await this.InternalEnsureScopeInfoAsync(context, null, false, connection, transaction, default, default).ConfigureAwait(false);

                // Direction set to Download
                context.SyncRole = SyncRole.Client;

                var changesToSend = new HttpMessageSendChangesRequest(context, cScopeInfoClient);

                context.ProgressPercentage += 0.125;

                await this.InterceptAsync(new HttpSendingClientChangesRequestArgs(changesToSend, 0, 0, this.GetServiceHost())).ConfigureAwait(false);

                // --------------------------------------------------------------
                // STEP 2 : Receive everything from the server side
                // --------------------------------------------------------------

                // Now we have sent all the datas to the server and now :
                // We have a FIRST response from the server with new datas
                // 1) Could be the only one response (enough or InMemory is set on the server side)
                // 2) Could bt the first response and we need to download all batchs
                context.SyncStage = SyncStage.ChangesSelecting;
                var initialPctProgress = 0.55;
                context.ProgressPercentage = initialPctProgress;

                var summaryResponseContent = await this.ProcessRequestAsync<HttpMessageSummaryResponse>(
                    context, changesToSend, HttpStep.SendChangesInProgress, this.Options.BatchSize).ConfigureAwait(false);

                serverBatchInfo.RowsCount = summaryResponseContent.BatchInfo.RowsCount;
                serverBatchInfo.Timestamp = summaryResponseContent.RemoteClientTimestamp;

                if (summaryResponseContent.BatchInfo.BatchPartsInfo != null)
                {
                    foreach (var bpi in summaryResponseContent.BatchInfo.BatchPartsInfo)
                        serverBatchInfo.BatchPartsInfo.Add(bpi);
                }

                //-----------------------
                // In Batch Mode
                //-----------------------
                // From here, we need to serialize everything on disk

                // Generate the batch directory
                var batchDirectoryRoot = this.Options.BatchDirectory;
                var batchDirectoryName = string.Concat("WEB_REMOTE_GETCHANGES_", DateTime.UtcNow.ToString("yyyy_MM_dd_ss", CultureInfo.InvariantCulture), Path.GetRandomFileName().Replace(".", string.Empty));

                serverBatchInfo.DirectoryRoot = batchDirectoryRoot;
                serverBatchInfo.DirectoryName = batchDirectoryName;

                if (!Directory.Exists(serverBatchInfo.GetDirectoryFullPath()))
                    Directory.CreateDirectory(serverBatchInfo.GetDirectoryFullPath());

                await this.DownladBatchInfoAsync(context, sScopeInfo.Schema, serverBatchInfo, summaryResponseContent, default, default).ConfigureAwait(false);

                // generate the new scope item
                this.CompleteTime = DateTime.UtcNow;

                // Reaffect context
                context = summaryResponseContent.SyncContext;

                return new ServerSyncChanges(summaryResponseContent.RemoteClientTimestamp, serverBatchInfo, summaryResponseContent.ServerChangesSelected, null, summaryResponseContent.ServerScopeId);
            }
            catch (HttpSyncWebException)
            {
                // Try to delete the local folder where we download everything from server
                await this.WebRemoteCleanFolderAsync(context, serverBatchInfo).ConfigureAwait(false);

                throw;
            } // throw server error
            catch (Exception ex)
            {
                // Try to delete the local folder where we download everything from server
                await this.WebRemoteCleanFolderAsync(context, serverBatchInfo).ConfigureAwait(false);

                throw this.GetSyncError(context, ex);
            } // throw client error
        }

        /// <summary>
        /// We can't get changes from server, from a web client orchestrator.
        /// </summary>
        ///
        public override async Task<ServerSyncChanges> GetEstimatedChangesCountAsync(ScopeInfoClient cScopeInfoClient, DbConnection connection = null, DbTransaction transaction = null)
        {
            var context = new SyncContext(Guid.NewGuid(), cScopeInfoClient.Name, cScopeInfoClient.Parameters) { ClientId = cScopeInfoClient.Id, UseUnifiedBatching = this.Options.UseUnifiedBatching };

            try
            {
                // Get the server scope to start a new session
                await this.InternalEnsureScopeInfoAsync(context, null, false, connection, transaction, default, default).ConfigureAwait(false);

                // generate a message to send
                var changesToSend = new HttpMessageSendChangesRequest(context, cScopeInfoClient);

                // Raise progress for sending request and waiting server response
                await this.InterceptAsync(new HttpGettingServerChangesRequestArgs(0, 0, context, this.GetServiceHost())).ConfigureAwait(false);

                // response
                var summaryResponseContent = await this.ProcessRequestAsync<HttpMessageSendChangesResponse>(context, changesToSend, HttpStep.GetEstimatedChangesCount, this.Options.BatchSize).ConfigureAwait(false);

                if (summaryResponseContent == null)
                    throw new Exception("Summary can't be null");

                // generate the new scope ite
                this.CompleteTime = DateTime.UtcNow;

                return new(summaryResponseContent.RemoteClientTimestamp, null, summaryResponseContent.ServerChangesSelected, null, summaryResponseContent.ServerScopeId);
            }
            catch (HttpSyncWebException)
            {
                throw;
            } // throw server error
            catch (Exception ex)
            {
                throw this.GetSyncError(context, ex);
            } // throw client error
        }

        private async Task DownladBatchInfoAsync(SyncContext context, SyncSet schema, BatchInfo serverBatchInfo, HttpMessageSummaryResponse summary, IProgress<ProgressArgs> progress, CancellationToken cancellationToken)
        {
            // If we have a snapshot we are raising the batches downloading process that will occurs
            await this.InterceptAsync(new HttpBatchesDownloadingArgs(context, serverBatchInfo, this.GetServiceHost()), progress, cancellationToken).ConfigureAwait(false);

            // hook to get the last batch part info at the end
            var bpis = serverBatchInfo.BatchPartsInfo.Where(bpi => !bpi.IsLastBatch).ToList();
            var lstbpi = serverBatchInfo.BatchPartsInfo.FirstOrDefault(bpi => bpi.IsLastBatch);

            lstbpi ??= serverBatchInfo.BatchPartsInfo.OrderByDescending(bpi => bpi.Index).FirstOrDefault();

            var optimizedFlow = this.customHeaders.TryGetValue("dotmim-sync-optimized", out var se) &&
                                bool.TryParse(se, out var seb) && seb;

            if (optimizedFlow)
            {
                // in optimized flow, the first batch is automatically downloaded in the response of the last "SendChanges" request
                var firstBpinf = bpis.FirstOrDefault(b => b.Index == 0);
                if (firstBpinf is { } firstBpi)
                {
                    var alreadyDownloaded =
                        File.Exists(Path.Combine(serverBatchInfo.GetDirectoryFullPath(), firstBpi.FileName));
                    if (alreadyDownloaded)
                        bpis.Remove(firstBpi);
                }

                // Parrallel download of all bpis except the last one
                await bpis
                    .ForEachAsync(
                        bpi => this.DownloadBatchPartInfoAsync(context, schema, serverBatchInfo, bpi,
                            HttpStep.GetMoreChanges, progress, cancellationToken),
                        this.MaxDownladingDegreeOfParallelism)
                    .ConfigureAwait(false);

                // Download last batch part with cleanup - the method will detect it's the last batch and use SendEndDownloadChanges
                await this.DownloadBatchPartInfoAsync(context, schema, serverBatchInfo, lstbpi, HttpStep.SendEndDownloadChanges , progress, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                // Parrallel download of all bpis except the last one (which will launch the delete directory on the server side)
                await bpis.ForEachAsync(bpi => this.DownloadBatchPartInfoAsync(context, schema, serverBatchInfo, bpi, HttpStep.GetMoreChanges, progress, cancellationToken), this.MaxDownladingDegreeOfParallelism).ConfigureAwait(false);

                // Download last batch part that will launch the server deletion of the tmp dir
                await this.DownloadBatchPartInfoAsync(context, schema, serverBatchInfo, lstbpi, HttpStep.GetMoreChanges, progress, cancellationToken).ConfigureAwait(false);

                // Send end of download
                await this.ProcessRequestAsync<HttpMessageSendChangesResponse>(context, new HttpMessageGetMoreChangesRequest(context, lstbpi == null ? 0 : lstbpi.Index),
                    HttpStep.SendEndDownloadChanges, 0, progress, cancellationToken).ConfigureAwait(false);

            }

            await this.InterceptAsync(new HttpBatchesDownloadedArgs(summary, context, this.GetServiceHost()), progress, cancellationToken).ConfigureAwait(false);
        }

        private async Task DownloadBatchPartInfoAsync(SyncContext context, SyncSet schema, BatchInfo serverBatchInfo, BatchPartInfo bpi, HttpStep step, IProgress<ProgressArgs> progress, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
                return;

            if (bpi == null)
                return;

            var initialPctProgress = 0.55;

            var changesToSend = new HttpMessageGetMoreChangesRequest(context, bpi.Index);

            await this.InterceptAsync(new HttpGettingServerChangesRequestArgs(bpi.Index, serverBatchInfo.BatchPartsInfo.Count, context, this.GetServiceHost()), progress, cancellationToken).ConfigureAwait(false);

            // Raise get changes request
            context.ProgressPercentage = initialPctProgress + ((bpi.Index + 1) * 0.2d / serverBatchInfo.BatchPartsInfo.Count);
            
            var response = await this.ProcessRequestAsync(changesToSend, step, 0, progress, cancellationToken).ConfigureAwait(false);

            // If we are using a serializer that is not JSON, need to load in memory, then serialize to JSON
            // OR If we have an interceptor on getting response
            // OR If we have a converter
            if (this.SerializerFactory.Key != "json" || this.Interceptors.HasInterceptors<HttpGettingResponseMessageArgs>() || this.Converter != null)
            {
                var webSerializer = this.SerializerFactory.GetSerializer();
#if NET6_0_OR_GREATER
                using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
#else
                using var responseStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
#endif
                var getMoreChanges = await webSerializer.DeserializeAsync<HttpMessageSendChangesResponse>(responseStream).ConfigureAwait(false);
                context = getMoreChanges.SyncContext;

                await this.InterceptAsync(
                    new HttpGettingResponseMessageArgs(response, this.ServiceUri,
                    step, context, getMoreChanges, this.GetServiceHost()), progress, cancellationToken).ConfigureAwait(false);

                if (getMoreChanges != null && getMoreChanges.Changes != null && getMoreChanges.Changes.HasRows)
                {
                    var fullPath = Path.Combine(serverBatchInfo.GetDirectoryFullPath(), bpi.FileName);

                    // Check if this is a unified batch (multiple tables or tables with _rs column)
                    var isUnifiedBatch = context.UseUnifiedBatching;

                    if (isUnifiedBatch)
                    {
                        // Apply converter if needed before serialization
                        if (this.Converter != null && getMoreChanges.Changes.HasRows)
                        {
                            foreach (var containerTable in getMoreChanges.Changes.Tables)
                            {
                                if (containerTable.HasRows)
                                {
                                    var schemaTable = CreateChangesTable(schema.Tables[containerTable.TableName, containerTable.SchemaName]);

                                    for (int i = 0; i < containerTable.Rows.Count; i++)
                                    {
                                        var row = containerTable.Rows[i];
                                        // Row format is always: [state, col1, col2, ..., colN]
                                        var syncRow = new SyncRow(schemaTable, row);
                                        this.Converter.AfterDeserialized(syncRow, schemaTable);
                                        // Note: row is a reference to the same array in SyncRow, so modifications are reflected
                                    }
                                }
                            }
                        }

                        // Handle unified batch - serialize the entire ContainerSet directly
                        var serializer = SerializersFactory.JsonSerializerFactory.GetSerializer();
                        using (var fs = new FileStream(fullPath, FileMode.Create, FileAccess.Write))
                        {
                            var data = await serializer.SerializeAsync(getMoreChanges.Changes).ConfigureAwait(false);
                            await fs.WriteAsync(data, 0, data.Length, cancellationToken).ConfigureAwait(false);
                        }
                    }
                    else
                    {
                        // Handle traditional single-table batch
                        await using var localSerializer = new LocalJsonSerializer(this.BatchStorage, this, context);

                        // Should have only one table
                        var table = getMoreChanges.Changes.Tables[0];
                        var schemaTable = CreateChangesTable(schema.Tables[table.TableName, table.SchemaName]);

                        SyncRowState syncRowState = SyncRowState.None;
                        if (table.Rows != null && table.Rows.Count > 0)
                        {
                            var sr = new SyncRow(schemaTable, table.Rows[0]);
                            syncRowState = sr.RowState;
                        }

                        // open the file and write table header
                        var directoryPath = Path.GetDirectoryName(fullPath);
                        var fileName = Path.GetFileName(fullPath);
                        await localSerializer.OpenFileAsync(directoryPath, fileName, schemaTable, syncRowState).ConfigureAwait(false);

                        foreach (var row in table.Rows)
                        {
                            var syncRow = new SyncRow(schemaTable, row);

                            if (this.Converter != null && syncRow.Length > 0)
                                this.Converter.AfterDeserialized(syncRow, schemaTable);

                            await localSerializer.WriteRowToFileAsync(syncRow, schemaTable).ConfigureAwait(false);
                        }
                    }
                }
            }
            else
            {
                // Even with JSON serializer and no interceptors/converters, we still need to extract the Changes property
                var webSerializer = this.SerializerFactory.GetSerializer();
#if NET6_0_OR_GREATER
                using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
#else
                using var responseStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
#endif
                var getMoreChanges = await webSerializer.DeserializeAsync<HttpMessageSendChangesResponse>(responseStream).ConfigureAwait(false);
                context = getMoreChanges.SyncContext;

                if (getMoreChanges != null && getMoreChanges.Changes != null && getMoreChanges.Changes.HasRows)
                {
                    var fullPath = Path.Combine(serverBatchInfo.GetDirectoryFullPath(), bpi.FileName);

                    // Check if this is a unified batch (multiple tables or tables with _rs column)
                    var isUnifiedBatch = context.UseUnifiedBatching;

                    if (isUnifiedBatch)
                    {
                        // Apply converter if needed before serialization
                        if (this.Converter != null && getMoreChanges.Changes.HasRows)
                        {
                            foreach (var containerTable in getMoreChanges.Changes.Tables)
                            {
                                if (containerTable.HasRows)
                                {
                                    var schemaTable = CreateChangesTable(schema.Tables[containerTable.TableName, containerTable.SchemaName]);

                                    for (int i = 0; i < containerTable.Rows.Count; i++)
                                    {
                                        var row = containerTable.Rows[i];
                                        // Row format is always: [state, col1, col2, ..., colN]
                                        var syncRow = new SyncRow(schemaTable, row);
                                        this.Converter.AfterDeserialized(syncRow, schemaTable);
                                        // Note: row is a reference to the same array in SyncRow, so modifications are reflected
                                    }
                                }
                            }
                        }

                        // Handle unified batch - serialize the entire ContainerSet directly
                        var serializer = SerializersFactory.JsonSerializerFactory.GetSerializer();
                        var dirPath = Path.GetDirectoryName(fullPath);
                        if (!Directory.Exists(dirPath))
                            Directory.CreateDirectory(dirPath);
                        using (var fs = new FileStream(fullPath, FileMode.Create, FileAccess.Write))
                        {
                            var data = await serializer.SerializeAsync(getMoreChanges.Changes).ConfigureAwait(false);
                            await fs.WriteAsync(data, 0, data.Length, cancellationToken).ConfigureAwait(false);
                        }
                    }
                    else
                    {
                        // Handle traditional single-table batch
                        await using var localSerializer = new LocalJsonSerializer(this.BatchStorage, this, context);

                        // Should have only one table
                        var table = getMoreChanges.Changes.Tables[0];
                        var schemaTable = CreateChangesTable(schema.Tables[table.TableName, table.SchemaName]);

                        SyncRowState syncRowState = SyncRowState.None;
                        if (table.Rows != null && table.Rows.Count > 0)
                        {
                            var sr = new SyncRow(schemaTable, table.Rows[0]);
                            syncRowState = sr.RowState;
                        }

                        // open the file and write table header
                        var directoryPath2 = Path.GetDirectoryName(fullPath);
                        var fileName2 = Path.GetFileName(fullPath);
                        await localSerializer.OpenFileAsync(directoryPath2, fileName2, schemaTable, syncRowState).ConfigureAwait(false);

                        foreach (var row in table.Rows)
                        {
                            var syncRow = new SyncRow(schemaTable, row);
                            await localSerializer.WriteRowToFileAsync(syncRow, schemaTable).ConfigureAwait(false);
                        }
                    }
                }
            }

            response.Dispose();

            // Raise response from server containing a batch changes
            await this.InterceptAsync(new HttpGettingServerChangesResponseArgs(serverBatchInfo, bpi.Index, bpi.RowsCount, context, this.GetServiceHost()), progress, cancellationToken).ConfigureAwait(false);
        }
    }
}