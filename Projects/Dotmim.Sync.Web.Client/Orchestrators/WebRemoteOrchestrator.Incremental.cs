using Dotmim.Sync.Batch;
using Dotmim.Sync.Enumerations;
using Dotmim.Sync.Serialization;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Dotmim.Sync.Web.Client
{
    /// <summary>
    /// Enhanced WebRemoteOrchestrator with incremental sync optimization support.
    /// </summary>
    public partial class WebRemoteOrchestrator : IIncrementalSyncOrchestrator
    {
        /// <summary>
        /// Determine if this is an incremental sync that can skip BeginSession.
        /// </summary>
        public bool CanUseOptimizedSync(ScopeInfo clientScopeInfo, ScopeInfoClient scopeInfoClient)
        {
            // Must be incremental sync (not first sync)
            if (scopeInfoClient.IsNewScope || !scopeInfoClient.LastServerSyncTimestamp.HasValue)
                return false;

            // Must have stored server capabilities
            if (string.IsNullOrEmpty(clientScopeInfo.ServerCapabilities))
                return false;

            // Must support incremental sync feature
            if (!clientScopeInfo.SupportsCapability(ServerCapabilities.SupportsIncrementalSync))
                return false;

            // Must have schema hash for validation
            if (string.IsNullOrEmpty(clientScopeInfo.SchemaHash))
                return false;

            return true;
        }

        public static bool IsOperationSupportedForOptimizedSync(SyncOperation operation)
            => operation switch
            {
                SyncOperation.Normal => true,
                SyncOperation.Reinitialize => true,
                SyncOperation.ReinitializeWithUpload => true,
                _ => false
            };

        private IDisposable _optimized = null;
        
        /// <summary>
        /// Performs an optimized sync that combines multiple protocol steps into fewer HTTP requests.
        /// Skips BeginSession, EnsureScopes, and GetOperation by using cached capabilities.
        /// </summary>
        public async Task<(SyncContext, bool, SyncOperation, ScopeInfo?, ScopeInfo, ServerSyncChanges, ConflictResolutionPolicy)>
            SynchronizeOptimizedAsync(ScopeInfoClient cScopeInfoClient, ScopeInfo cScopeInfo,
               SyncContext context, ClientSyncChanges clientChanges,
            DbConnection connection = default, DbTransaction transaction = default,
            IProgress<ProgressArgs> progress = null, CancellationToken cancellationToken = default)
        {
            // set the "optimized sync" header only for the duration of this flow
            // because in case we detect any error (wrong schema, etc.) we need to fall-back to the legacy flow
            _optimized = this.SetOptimizedSyncHeader();

            SyncSet schema = cScopeInfo.Schema;
            schema.EnsureSchema();

            // if we don't have any BatchPartsInfo, just generate a new one to get, at least, something to send to the server
            // and get a response with new data from server
            clientChanges.ClientBatchInfo ??= new BatchInfo();

            // Build incremental sync request (no BeginSession needed)
            // NOTE: This optimization is only used for the FIRST batch of client changes.
            // If there are multiple batches, subsequent batches will use the traditional flow.
            var firstRequest = new HttpMessageSendChangesIncrementalRequest(context, cScopeInfoClient)
            {
                // Schema validation using stored hash
                SchemaHash = cScopeInfo.SchemaHash, // Pre-calculated and stored
                ClientSchemaVersion = cScopeInfo.Version,

                SyncContext = context
            };
            // --------------------------------------------------------------
            // STEP 1 : Send everything to the server side
            // --------------------------------------------------------------
            HttpResponseMessage response = null;
            HttpMessageSendChangesIncrementalResponse firstResponse = null;
            HttpMessageSummaryResponse summaryResponseContent = null;


            // If not in memory and BatchPartsInfo.Count == 0, nothing to send.
            // But we need to send something, so generate a little batch part
            if (clientChanges.ClientBatchInfo.BatchPartsInfo.Count == 0)
            {
                try
                {
                    context.ProgressPercentage += 0.125;

                    await this.InterceptAsync(new HttpSendingClientChangesRequestArgs(firstRequest, 0, 0, this.GetServiceHost()), progress, cancellationToken).ConfigureAwait(false);

                    firstResponse = await this.ProcessRequestAsync<HttpMessageSendChangesIncrementalResponse>(context,firstRequest, HttpStep.SendChangesIncremental, this.Options.BatchSize, progress, cancellationToken).ConfigureAwait(false);

                    summaryResponseContent = firstResponse;

                    if (!firstResponse.SchemaValid || !IsOperationSupportedForOptimizedSync(firstResponse.Operation))
                    {
                        this._optimized.Dispose();
                        this._optimized = null;
                     
                        return (context, firstResponse.SchemaValid, firstResponse.Operation,
                            firstResponse.ServerScopeInfo, cScopeInfo, null, firstResponse.ConflictResolutionPolicy);
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
            else
            {
                try
                {
                    int tmpRowsSendedCount = 0;

                    // Foreach part, will have to send them to the remote
                    // once finished, return context
                    var initialPctProgress1 = context.ProgressPercentage;
                    using var localSerializer = new LocalJsonSerializer(this, context);

                    foreach (var bpi in clientChanges.ClientBatchInfo.BatchPartsInfo.OrderBy(bpi => bpi.Index))
                    {
                        // Get the updatable schema for the only table contained in the batchpartinfo
                        var schemaTable = CreateChangesTable(schema.Tables[bpi.TableName, bpi.SchemaName]);

                        if (bpi.Index == 0)
                        {
                            firstRequest.IsLastBatch = bpi.IsLastBatch;
                            firstRequest.BatchIndex = bpi.Index;
                            firstRequest.BatchCount = clientChanges.ClientBatchInfo.BatchPartsInfo.Count;
                            firstRequest.ClientLastSyncTimestamp = clientChanges.ClientTimestamp;

                            // Generate the ContainerSet containing rows to send to the user
                            var containerTable = new ContainerTable(schemaTable);
                            firstRequest.Changes.Tables.Add(containerTable);

                            // read rows from file
                            var fullPath = Path.Combine(clientChanges.ClientBatchInfo.GetDirectoryFullPath(), bpi.FileName);
                            foreach (var row in localSerializer.GetRowsFromFile(fullPath, schemaTable))
                            {
                                if (this.Converter != null && row.Length > 0)
                                    this.Converter.BeforeSerialize(row, schemaTable);

                                containerTable.Rows.Add(row.ToArray());
                            }

                            tmpRowsSendedCount += containerTable.Rows.Count;

                            context.ProgressPercentage = initialPctProgress1 + ((firstRequest.BatchIndex + 1) * 0.2d / firstRequest.BatchCount);
                            
                            await this.InterceptAsync(new HttpSendingClientChangesRequestArgs(firstRequest, tmpRowsSendedCount, clientChanges.ClientBatchInfo.RowsCount, this.GetServiceHost()), progress, cancellationToken).ConfigureAwait(false);

                            firstResponse = await this.ProcessRequestAsync<HttpMessageSendChangesIncrementalResponse>(context, firstRequest, HttpStep.SendChangesIncremental, 0, progress, cancellationToken).ConfigureAwait(false);

                            summaryResponseContent = firstResponse;
                            
                            if (!firstResponse.SchemaValid || !IsOperationSupportedForOptimizedSync(firstResponse.Operation))
                            {
                                this._optimized.Dispose();
                                this._optimized = null;
                                
                                return (context, firstResponse.SchemaValid, firstResponse.Operation,
                                    firstResponse.ServerScopeInfo, cScopeInfo, null, firstResponse.ConflictResolutionPolicy);
                            }
                        }
                        else
                        {
                            // Create the send changes request
                            var changesToSend = new HttpMessageSendChangesRequest(context, cScopeInfoClient)
                            {
                                IsLastBatch = bpi.IsLastBatch,
                                BatchIndex = bpi.Index,
                                BatchCount = clientChanges.ClientBatchInfo.BatchPartsInfo.Count,
                                ClientLastSyncTimestamp = clientChanges.ClientTimestamp,
                            };

                            // Generate the ContainerSet containing rows to send to the user
                            var containerTable = new ContainerTable(schemaTable);
                            changesToSend.Changes.Tables.Add(containerTable);

                            // read rows from file
                            var fullPath = Path.Combine(clientChanges.ClientBatchInfo.GetDirectoryFullPath(), bpi.FileName);
                            foreach (var row in localSerializer.GetRowsFromFile(fullPath, schemaTable))
                            {
                                if (this.Converter != null && row.Length > 0)
                                    this.Converter.BeforeSerialize(row, schemaTable);

                                containerTable.Rows.Add(row.ToArray());
                            }

                            tmpRowsSendedCount += containerTable.Rows.Count;

                            context.ProgressPercentage = initialPctProgress1 + ((changesToSend.BatchIndex + 1) * 0.2d / changesToSend.BatchCount);
                            await this.InterceptAsync(new HttpSendingClientChangesRequestArgs(changesToSend, tmpRowsSendedCount, clientChanges.ClientBatchInfo.RowsCount, this.GetServiceHost()), progress, cancellationToken).ConfigureAwait(false);

                            response = await this.ProcessRequestAsync(changesToSend, HttpStep.SendChangesInProgress, this.Options.BatchSize, progress, cancellationToken).ConfigureAwait(false);

                            // Deserialize last response incoming from server after uploading changes
#if NET6_0_OR_GREATER
                            using (var streamResponse = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
#else
                            using (var streamResponse = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
#endif
                            {
                                var responseSerializer = this.SerializerFactory.GetSerializer();
                                summaryResponseContent = await responseSerializer
                                    .DeserializeAsync<HttpMessageSummaryResponse>(streamResponse).ConfigureAwait(false);
                                context = summaryResponseContent.SyncContext;
                            }
                            
                            await this.InterceptAsync(
                                new HttpGettingResponseMessageArgs(response, this.ServiceUri,
                                    HttpStep.SendChangesInProgress, context, summaryResponseContent, this.GetServiceHost()), progress, cancellationToken).ConfigureAwait(false);
                            
                            // See #721 for issue and #721 for PR from slagtejn
                            if (!bpi.IsLastBatch)
                                response.Dispose();
                        }
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
            BatchInfo serverBatchInfo = new BatchInfo();
            try
            {
                context.SyncStage = SyncStage.ChangesSelecting;
                var initialPctProgress = 0.55;
                context.ProgressPercentage = initialPctProgress;

                // Create the BatchInfo
                // Handle first batch data included directly in response (optimized protocol)
                if (summaryResponseContent.Changes != null && summaryResponseContent.Changes.HasRows)
                    serverBatchInfo = await this.ReconstructFirstBatchInfoFromChangeSet(context, summaryResponseContent, schema);

                // Add remaining batch info (if any)
                serverBatchInfo.Timestamp = summaryResponseContent.RemoteClientTimestamp;
                serverBatchInfo.RowsCount += summaryResponseContent.BatchInfo?.RowsCount??0;
                
                if (summaryResponseContent.BatchInfo?.BatchPartsInfo != null)
                {
                    // Add remaining batches (starting from index 1 since index 0 is already handled above)
                    foreach (var bpi in summaryResponseContent.BatchInfo.BatchPartsInfo)
                        serverBatchInfo.BatchPartsInfo.Add(bpi);
                }

                // Only download remaining batches if any exist
                if (summaryResponseContent.BatchInfo?.BatchPartsInfo?.Count > 0)
                {
                    // Generate the batch directory if not already created
                    if (serverBatchInfo.DirectoryRoot == null)
                    {
                        var batchDirectoryRoot = this.Options.BatchDirectory;
                        var batchDirectoryName = string.Concat("WEB_REMOTE_GETCHANGES_", DateTime.UtcNow.ToString("yyyy_MM_dd_ss", CultureInfo.InvariantCulture),
                            Path.GetRandomFileName().Replace(".", string.Empty));

                        serverBatchInfo.DirectoryRoot = batchDirectoryRoot;
                        serverBatchInfo.DirectoryName = batchDirectoryName;

                        if (!Directory.Exists(serverBatchInfo.GetDirectoryFullPath()))
                            Directory.CreateDirectory(serverBatchInfo.GetDirectoryFullPath());
                    }

                    await this.DownladBatchInfoAsync(context, schema, serverBatchInfo, summaryResponseContent, progress, cancellationToken).ConfigureAwait(false);
                }

                // generate the new scope item
                this.CompleteTime = DateTime.UtcNow;

                // Update local client scope with server capabilities if available
                if (!string.IsNullOrEmpty(firstResponse.ServerCapabilities) ||
                    !string.IsNullOrEmpty(firstResponse.ServerVersion) ||
                    firstResponse.CapabilitiesLastUpdated.HasValue)
                {
                    cScopeInfo.ServerCapabilities = firstResponse.ServerCapabilities ?? cScopeInfo.ServerCapabilities;
                    cScopeInfo.ServerVersion = firstResponse.ServerVersion ?? cScopeInfo.ServerVersion;
                    cScopeInfo.CapabilitiesLastUpdated = firstResponse.CapabilitiesLastUpdated ?? cScopeInfo.CapabilitiesLastUpdated;
                }

                var serverSyncChanges = new ServerSyncChanges(
                    summaryResponseContent.RemoteClientTimestamp,
                    serverBatchInfo,
                    summaryResponseContent.ServerChangesSelected,
                    summaryResponseContent.ClientChangesApplied);

                return (context, true, firstResponse.Operation, firstResponse.ServerScopeInfo, cScopeInfo, serverSyncChanges, summaryResponseContent.ConflictResolutionPolicy);
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
            }
        }

        private async Task<BatchInfo> ReconstructFirstBatchInfoFromChangeSet(SyncContext context,
            HttpMessageSummaryResponse summaryResponseContent, SyncSet schema)
        {
            BatchInfo serverBatchInfo = new();
            
            // First batch data is included directly - save it to disk
            var batchDirectoryRoot = this.Options.BatchDirectory;
            var batchDirectoryName = string.Concat("WEB_REMOTE_GETCHANGES_", DateTime.UtcNow.ToString("yyyy_MM_dd_ss", CultureInfo.InvariantCulture),
                Path.GetRandomFileName().Replace(".", string.Empty));

            serverBatchInfo.DirectoryRoot = batchDirectoryRoot;
            serverBatchInfo.DirectoryName = batchDirectoryName;

            if (!Directory.Exists(serverBatchInfo.GetDirectoryFullPath()))
                Directory.CreateDirectory(serverBatchInfo.GetDirectoryFullPath());

            // Process the first batch data from Changes property
            using var localSerializer = new LocalJsonSerializer(this, context);
            foreach (var containerTable in summaryResponseContent.Changes.Tables)
            {
                var schemaTable = CreateChangesTable(schema.Tables[containerTable.TableName, containerTable.SchemaName]);
                var setupTable = new SetupTable(containerTable.TableName, containerTable.SchemaName);
                var tableName = setupTable.GetFullName().Replace(".", "_").Replace(" ", "_");

                // Create first batch part info (index 0)
                var fileName = BatchInfo.GenerateNewFileName("0", tableName, LocalJsonSerializer.Extension, "");
                var fullPath = Path.Combine(serverBatchInfo.GetDirectoryFullPath(), fileName);

                SyncRowState syncRowState = SyncRowState.None;
                if (containerTable.Rows != null && containerTable.Rows.Count > 0)
                {
                    var sr = new SyncRow(schemaTable, containerTable.Rows[0]);
                    syncRowState = sr.RowState;
                }

                // Save first batch data to file
                await localSerializer.OpenFileAsync(fullPath, schemaTable, syncRowState).ConfigureAwait(false);

                foreach (var row in containerTable.Rows)
                {
                    var syncRow = new SyncRow(schemaTable, row);
                    if (this.Converter != null && syncRow.Length > 0)
                        this.Converter.AfterDeserialized(syncRow, schemaTable);

                    await localSerializer.WriteRowToFileAsync(syncRow, schemaTable).ConfigureAwait(false);
                }

                // Create batch part info for first batch
                var firstBpi = new BatchPartInfo
                {
                    FileName = fileName,
                    TableName = containerTable.TableName,
                    SchemaName = containerTable.SchemaName,
                    RowsCount = containerTable.Rows.Count,
                    IsLastBatch = summaryResponseContent.BatchInfo == null || summaryResponseContent.BatchInfo.BatchPartsInfo?.Count == 0,
                    Index = 0,
                };

                serverBatchInfo.BatchPartsInfo.Add(firstBpi);
                serverBatchInfo.RowsCount += firstBpi.RowsCount;
            }

            return serverBatchInfo;
        }


        /// <summary>
        /// Send detailed error information to server (async, non-blocking).
        /// The optimized flow does not send an explicit "EndSession" reuqest anymore where it can forward any caught exceptions.
        /// This is for optimization reasons: It is unlikely, that errors occur. Most of the time, the sync will be fine.
        /// Therefore, the session is closed automatically by the server when it sent the last batch. This saves http requests.
        /// And only in error cases, we need another request to be able to tell the server that something went wrong.
        /// </summary>
        public async Task<bool> ReportSyncErrorAsync(
            SyncContext context, Exception exception, SyncErrorContext errorContext = null,
            IProgress<ProgressArgs> progress = null, CancellationToken cancellationToken = default)
        {
            try
            {
                // Check if server supports error reporting
                var scopeInfo = await this.GetScopeInfoAsync(context.ScopeName);
                if (!scopeInfo.SupportsCapability(ServerCapabilities.SupportsErrorReporting))
                    return false;

                // Enhance error context with client environment
                if (errorContext == null)
                    errorContext = new SyncErrorContext();

                if (errorContext.ClientInfo == null)
                    errorContext.ClientInfo = ClientEnvironmentInfo.GetCurrent();

                // Create error request
                var request = HttpMessageSendSyncErrorsRequest.FromException(context, exception, errorContext);

                // Send error report (fire-and-forget style)
                var response = await this.ProcessRequestAsync<HttpMessageSendSyncErrorsResponse>(
                    context, request, HttpStep.SendSyncErrors, 0, progress, cancellationToken);

                return response.ErrorReceived;
            }
            catch
            {
                // Error reporting should never throw - it's best effort
                return false;
            }
        }
        
        
        private IDisposable SetOptimizedSyncHeader()
            => new UseOptimizedSync(this);

        private class UseOptimizedSync : IDisposable
        {
            private readonly WebRemoteOrchestrator _instance;

            public UseOptimizedSync(WebRemoteOrchestrator instance)
            {
                _instance = instance;
                // inform server that it can close the session implicitly
                _instance.AddCustomHeader("dotmim-sync-optimized", "true");
            }

            public void Dispose()
            {
                this._instance.customHeaders.Remove("dotmim-sync-optimized");
            }
        }
    }
}