using Wormhole.Sync.Batch;
using Wormhole.Sync.Enumerations;
using Wormhole.Sync.Serialization;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Wormhole.Sync.Web.Client
{
    /// <summary>
    /// Contains the logic to apply changes to the server and get changes from the server.
    /// </summary>
    public partial class WebRemoteOrchestrator : RemoteOrchestrator
    {
        /// <summary>
        /// Apply changes.
        /// </summary>
        internal override async Task<(SyncContext Context, ServerSyncChanges ServerSyncChanges, ConflictResolutionPolicy ServerResolutionPolicy)>
            InternalApplyThenGetChangesAsync(ScopeInfoClient cScopeInfoClient, ScopeInfo cScopeInfo, SyncContext context, ClientSyncChanges clientChanges,
            DbConnection connection = default, DbTransaction transaction = default, IProgress<ProgressArgs> progress = null, CancellationToken cancellationToken = default)
        {
            SyncSet schema = cScopeInfo.Schema;
            schema.EnsureSchema();

            // if we don't have any BatchPartsInfo, just generate a new one to get, at least, something to send to the server
            // and get a response with new data from server
            clientChanges.ClientBatchInfo ??= new BatchInfo();

            // --------------------------------------------------------------
            // STEP 1 : Send everything to the server side
            // --------------------------------------------------------------
            HttpResponseMessage response = null;

            // If not in memory and BatchPartsInfo.Count == 0, nothing to send.
            // But we need to send something, so generate a little batch part
            if (clientChanges.ClientBatchInfo.BatchPartsInfo.Count == 0)
            {
                try
                {
                    var changesToSend = new HttpMessageSendChangesRequest(context, cScopeInfoClient) { ClientLastSyncTimestamp = clientChanges.ClientTimestamp };

                    context.ProgressPercentage += 0.125;

                    await this.InterceptAsync(new HttpSendingClientChangesRequestArgs(changesToSend, 0, 0, this.GetServiceHost()), progress, cancellationToken).ConfigureAwait(false);

                    response = await this.ProcessRequestAsync(
                        changesToSend, HttpStep.SendChangesInProgress, this.Options.BatchSize, progress, cancellationToken).ConfigureAwait(false);
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
            else
            {
                try
                {
                    int tmpRowsSendedCount = 0;

                    // Foreach part, will have to send them to the remote
                    // once finished, return context
                    var initialPctProgress1 = context.ProgressPercentage;
                    await using var localSerializer = new LocalJsonSerializer(this.BatchStorage, this, context);

                    foreach (var bpi in clientChanges.ClientBatchInfo.BatchPartsInfo.OrderBy(bpi => bpi.Index))
                    {
                        // Create the send changes request
                        var changesToSend = new HttpMessageSendChangesRequest(context, cScopeInfoClient)
                        {
                            IsLastBatch = bpi.IsLastBatch,
                            BatchIndex = bpi.Index,
                            BatchCount = clientChanges.ClientBatchInfo.BatchPartsInfo.Count,
                            ClientLastSyncTimestamp = clientChanges.ClientTimestamp,
                        };

                        var fullPath = Path.Combine(clientChanges.ClientBatchInfo.GetDirectoryFullPath(), bpi.FileName);
                        tmpRowsSendedCount += await this.LoadChangesFromBatch(bpi, schema, fullPath, changesToSend, localSerializer).ConfigureAwait(false);


                        context.ProgressPercentage = initialPctProgress1 + ((changesToSend.BatchIndex + 1) * 0.2d / changesToSend.BatchCount);
                        await this.InterceptAsync(new HttpSendingClientChangesRequestArgs(changesToSend, tmpRowsSendedCount, clientChanges.ClientBatchInfo.RowsCount, this.GetServiceHost()), progress, cancellationToken).ConfigureAwait(false);

                        response = await this.ProcessRequestAsync(changesToSend, HttpStep.SendChangesInProgress, this.Options.BatchSize, progress, cancellationToken).ConfigureAwait(false);

                        // See #721 for issue and #721 for PR from slagtejn
                        if (!bpi.IsLastBatch)
                            response.Dispose();
                    }
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

            // --------------------------------------------------------------
            // STEP 2 : Receive everything from the server side
            // --------------------------------------------------------------

            // Now we have sent all the datas to the server and now :
            // We have a FIRST response from the server with new datas
            // 1) Could be the only one response
            // 2) Could be the first response and we need to download all batchs

            // Create the BatchInfo
            var serverBatchInfo = new BatchInfo();

            try
            {
                context.SyncStage = SyncStage.ChangesSelecting;
                var initialPctProgress = 0.55;
                context.ProgressPercentage = initialPctProgress;

                HttpMessageSummaryResponse summaryResponseContent = null;

                // Deserialize last response incoming from server after uploading changes
#if NET6_0_OR_GREATER
                using (var streamResponse = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
#else
                using (var streamResponse = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
#endif
                {
                    var responseSerializer = this.SerializerFactory.GetSerializer();
                    summaryResponseContent = await responseSerializer.DeserializeAsync<HttpMessageSummaryResponse>(streamResponse).ConfigureAwait(false);
                    context = summaryResponseContent.SyncContext;

                    await this.InterceptAsync(
                        new HttpGettingResponseMessageArgs(response, this.ServiceUri,
                        HttpStep.SendChangesInProgress, context, summaryResponseContent, this.GetServiceHost()), progress, cancellationToken).ConfigureAwait(false);
                }

                // Handle async batch creation (InProgress response from server)
                if (summaryResponseContent.InProgress)
                {
                    const int maxRetries = 120; // Max ~10 minutes with 5-second default intervals
                    var retryCount = 0;

                    while (summaryResponseContent.InProgress && retryCount < maxRetries)
                    {
                        var retryDelay = TimeSpan.FromSeconds(summaryResponseContent.RetryAfterSeconds ?? 5);

                        // Notify progress
                        await this.InterceptAsync(
                            new HttpBatchCreationInProgressArgs(context, summaryResponseContent.AsyncProgress ?? 0, retryCount, this.GetServiceHost()),
                            progress, cancellationToken).ConfigureAwait(false);

                        await Task.Delay(retryDelay, cancellationToken).ConfigureAwait(false);

                        // Retry request - send empty changes to poll for completion
                        response?.Dispose();
                        var pollRequest = new HttpMessageSendChangesRequest(context, cScopeInfoClient)
                        {
                            ClientLastSyncTimestamp = clientChanges.ClientTimestamp,
                            IsLastBatch = true,
                            BatchIndex = 0,
                            BatchCount = 0,
                        };

                        response = await this.ProcessRequestAsync(
                            pollRequest, HttpStep.SendChangesInProgress, this.Options.BatchSize, progress, cancellationToken).ConfigureAwait(false);

#if NET6_0_OR_GREATER
                        using var retryStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
#else
                        using var retryStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
#endif
                        var retrySerializer = this.SerializerFactory.GetSerializer();
                        summaryResponseContent = await retrySerializer.DeserializeAsync<HttpMessageSummaryResponse>(retryStream).ConfigureAwait(false);
                        context = summaryResponseContent.SyncContext;

                        retryCount++;
                    }

                    if (summaryResponseContent.InProgress)
                        throw new SyncException($"Server batch creation timeout after {retryCount} retries. Please try again later.");
                }

                // Set batch info properties from response
                serverBatchInfo.RowsCount = summaryResponseContent.BatchInfo?.RowsCount??0;
                serverBatchInfo.Timestamp = summaryResponseContent.RemoteClientTimestamp;

                if (summaryResponseContent.BatchInfo?.BatchPartsInfo != null)
                {
                    foreach (var bpi in summaryResponseContent.BatchInfo.BatchPartsInfo)
                        serverBatchInfo.BatchPartsInfo.Add(bpi);
                }

                // Generate the batch directory
                var batchDirectoryRoot = this.Options.BatchDirectory;
                var batchDirectoryName = string.Concat("WEB_REMOTE_GETCHANGES_", DateTime.UtcNow.ToString("yyyy_MM_dd_ss", CultureInfo.InvariantCulture),
                    Path.GetRandomFileName().Replace(".", string.Empty));

                serverBatchInfo.DirectoryRoot = batchDirectoryRoot;
                serverBatchInfo.DirectoryName = batchDirectoryName;

                // Download initial batches
                await this.DownladBatchInfoAsync(context, schema, serverBatchInfo, summaryResponseContent, progress, cancellationToken).ConfigureAwait(false);

                // Handle progressive batch streaming: poll for more batches while MoreBatchesPending is true
                if (summaryResponseContent.MoreBatchesPending)
                {
                    var lastReceivedBatchIndex = summaryResponseContent.BatchInfo.BatchPartsInfo.Max(bpi => bpi.Index);
                    const int maxProgressiveRetries = 600; // Max ~10 minutes with 1-second intervals
                    var progressiveRetryCount = 0;

                    while (summaryResponseContent.MoreBatchesPending && progressiveRetryCount < maxProgressiveRetries)
                    {
                        var retryDelay = TimeSpan.FromSeconds(1); // Poll every second for more batches

                        // Notify progress
                        await this.InterceptAsync(
                            new HttpBatchCreationInProgressArgs(context, summaryResponseContent.AsyncProgress ?? 0, progressiveRetryCount, this.GetServiceHost()),
                            progress, cancellationToken).ConfigureAwait(false);

                        await Task.Delay(retryDelay, cancellationToken).ConfigureAwait(false);

                        // Request more batches from server
                        response?.Dispose();
                        var moreBatchesRequest = new HttpMessageSendChangesRequest(context, cScopeInfoClient)
                        {
                            ClientLastSyncTimestamp = clientChanges.ClientTimestamp,
                            IsLastBatch = true,
                            BatchIndex = 0,
                            BatchCount = 0,
                        };

                        response = await this.ProcessRequestAsync(
                            moreBatchesRequest, HttpStep.SendChangesInProgress, this.Options.BatchSize, progress, cancellationToken).ConfigureAwait(false);

#if NET6_0_OR_GREATER
                        using var moreBatchesStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
#else
                        using var moreBatchesStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
#endif
                        var moreBatchesSerializer = this.SerializerFactory.GetSerializer();
                        var moreBatchesResponse = await moreBatchesSerializer.DeserializeAsync<HttpMessageSummaryResponse>(moreBatchesStream).ConfigureAwait(false);
                        context = moreBatchesResponse.SyncContext;

                        // Add new batch parts that we haven't downloaded yet
                        if (moreBatchesResponse.BatchInfo?.BatchPartsInfo != null)
                        {
                            var newBatchParts = moreBatchesResponse.BatchInfo.BatchPartsInfo
                                .Where(bpi => bpi.Index > lastReceivedBatchIndex)
                                .OrderBy(bpi => bpi.Index)
                                .ToList();

                            if (newBatchParts.Count > 0)
                            {
                                // Create a temporary batch info for downloading only new parts
                                var tempBatchInfo = new BatchInfo
                                {
                                    DirectoryRoot = serverBatchInfo.DirectoryRoot,
                                    DirectoryName = serverBatchInfo.DirectoryName,
                                };
                                foreach (var bpi in newBatchParts)
                                {
                                    tempBatchInfo.BatchPartsInfo.Add(bpi);
                                    serverBatchInfo.BatchPartsInfo.Add(bpi);
                                    lastReceivedBatchIndex = Math.Max(lastReceivedBatchIndex, bpi.Index);
                                }

                                // Download only the new batch parts
                                await this.DownladBatchInfoAsync(context, schema, tempBatchInfo, moreBatchesResponse, progress, cancellationToken).ConfigureAwait(false);
                            }
                        }

                        // Update status
                        summaryResponseContent = moreBatchesResponse;
                        serverBatchInfo.RowsCount = moreBatchesResponse.BatchInfo?.RowsCount ?? serverBatchInfo.RowsCount;
                        progressiveRetryCount++;
                    }

                    if (summaryResponseContent.MoreBatchesPending)
                        throw new SyncException($"Server batch creation timeout after {progressiveRetryCount} progressive retries. Please try again later.");
                }

                // generate the new scope item
                this.CompleteTime = DateTime.UtcNow;

                var serverSyncChanges = new ServerSyncChanges(
                    summaryResponseContent.RemoteClientTimestamp,
                    serverBatchInfo,
                    summaryResponseContent.ServerChangesSelected,
                    summaryResponseContent.ClientChangesApplied,
                    summaryResponseContent.ServerScopeId);

                return (context, serverSyncChanges, summaryResponseContent.ConflictResolutionPolicy);
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
    }
}