using Dotmim.Sync.Batch;
using Dotmim.Sync.Enumerations;
using Dotmim.Sync.Serialization;
using Dotmim.Sync.Web.Client;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Dotmim.Sync.Web.Server
{
    /// <summary>
    /// Enhanced WebServerAgent with incremental sync optimization support.
    /// </summary>
    public partial class WebServerAgent
    {
        /// <summary>
        /// Handle combined incremental sync request.
        /// </summary>
        protected internal virtual async Task<HttpMessageSendChangesIncrementalResponse> SendChangesIncrementalAsync(
            HttpContext httpContext, HttpMessageSendChangesIncrementalRequest httpMessage, SessionCache sessionCache, int clientBatchSize,
            IProgress<ProgressArgs> progress, CancellationToken cancellationToken)
        {
            try
            {   
                // Overriding batch size options value, coming from client
                this.Options.BatchSize = clientBatchSize;

                var context = httpMessage.SyncContext;

                // Step 1: Begin session (allow interceptors to handle begin session event)
                await this.RemoteOrchestrator.BeginSessionAsync().ConfigureAwait(false);

                // Step 2: Ensure scope info and fast schema validation using stored hash
                ScopeInfo serverScopeInfo;
                (context, serverScopeInfo, _) = await this.RemoteOrchestrator.InternalEnsureScopeInfoAsync(context, this.Setup, false, default, default, progress, cancellationToken).ConfigureAwait(false);

                // Set session cache info for the schema
                httpContext.Session.Set(context.ScopeName, serverScopeInfo.Schema);

                // Ensure server has schema hash cached
                if (string.IsNullOrEmpty(serverScopeInfo.SchemaHash) && serverScopeInfo.Schema != null)
                {
                    serverScopeInfo.UpdateSchemaHash(serverScopeInfo.Schema);
                    await this.RemoteOrchestrator.SaveScopeInfoAsync(serverScopeInfo).ConfigureAwait(false);
                }

                // Fast hash comparison (O(1) instead of O(n) schema comparison)
                bool schemaValid = !string.IsNullOrEmpty(httpMessage.SchemaHash) &&
                                 !string.IsNullOrEmpty(serverScopeInfo.SchemaHash) &&
                                 httpMessage.SchemaHash.Equals(serverScopeInfo.SchemaHash, StringComparison.OrdinalIgnoreCase);

                // Step 3: Operation determination
                var operation = SyncOperation.Normal;
                if (!schemaValid || httpMessage.ScopeInfoClient.IsNewScope)
                {
                    operation = SyncOperation.Reinitialize;
                }

                // Step 4: Apply changes and get server changes (only if schema is valid and this is the last batch)
                DatabaseChangesApplied clientChangesApplied = null;
                DatabaseChangesSelected serverChangesSelected = null;
                BatchInfo serverBatchInfo = null;
                long remoteClientTimestamp = 0;

                if (operation == SyncOperation.Normal && httpMessage.IsLastBatch && httpMessage.Changes != null)
                {
                    // ------------------------------------------------------------
                    // FIRST STEP : receive client changes (from ApplyThenGetChangesAsync2 logic)
                    // ------------------------------------------------------------

                    // Get batch info from session cache if exists, otherwise create it
                    sessionCache.ClientBatchInfo ??= new BatchInfo(this.Options.BatchDirectory, info: "REMOTEGETCHANGES");

                    if (httpMessage.Changes.HasRows)
                    {
                        using var localSerializer = new LocalJsonSerializer(this.RemoteOrchestrator, context);

                        // we have only one table here
                        var containerTable = httpMessage.Changes.Tables[0];
                        var schemaTable = BaseOrchestrator.CreateChangesTable(serverScopeInfo.Schema.Tables[containerTable.TableName, containerTable.SchemaName]);

                        var setupTable = new SetupTable(containerTable.TableName, containerTable.SchemaName);
                        var tableName = setupTable.GetFullName().Replace(".", "_").Replace(" ", "_");
                        var fileName = BatchInfo.GenerateNewFileName(httpMessage.BatchIndex.ToString(System.Globalization.CultureInfo.InvariantCulture), tableName, LocalJsonSerializer.Extension, "CLICHANGES");
                        var fullPath = Path.Combine(sessionCache.ClientBatchInfo.GetDirectoryFullPath(), fileName);

                        SyncRowState syncRowState = SyncRowState.None;
                        if (containerTable.Rows != null && containerTable.Rows.Count > 0)
                        {
                            var sr = new SyncRow(schemaTable, containerTable.Rows[0]);
                            syncRowState = sr.RowState;
                        }

                        // open the file and write table header
                        await localSerializer.OpenFileAsync(fullPath, schemaTable, syncRowState);

                        foreach (var row in containerTable.Rows)
                        {
                            var syncRow = new SyncRow(schemaTable, row);

                            if (this.clientConverter != null && syncRow.Length > 0)
                                this.clientConverter.AfterDeserialized(syncRow, schemaTable);

                            await localSerializer.WriteRowToFileAsync(syncRow, schemaTable);
                        }

                        var bpi = new BatchPartInfo
                        {
                            FileName = fileName,
                            TableName = containerTable.TableName,
                            SchemaName = containerTable.SchemaName,
                            RowsCount = containerTable.Rows.Count,
                            IsLastBatch = httpMessage.IsLastBatch,
                            Index = httpMessage.BatchIndex,
                        };

                        sessionCache.ClientBatchInfo.RowsCount += bpi.RowsCount;
                        sessionCache.ClientBatchInfo.BatchPartsInfo.Add(bpi);
                    }

                    // Clear the httpMessage set
                    if (httpMessage.Changes != null)
                        httpMessage.Changes.Clear();

                    // ------------------------------------------------------------
                    // SECOND STEP : apply then return server changes
                    // ------------------------------------------------------------
                    var clientSyncChanges = new ClientSyncChanges(httpMessage.ClientLastSyncTimestamp, sessionCache.ClientBatchInfo, null, null);

                    // Apply client changes and get server changes
                    ServerSyncChanges serverSyncChanges;
                    (context, serverSyncChanges, _) = await this.RemoteOrchestrator.InternalApplyThenGetChangesAsync(
                                                       httpMessage.ScopeInfoClient,
                                                       serverScopeInfo,
                                                       context,
                                                       clientSyncChanges,
                                                       default, default, progress, cancellationToken);

                    // Set session cache infos
                    sessionCache.RemoteClientTimestamp = serverSyncChanges.RemoteClientTimestamp;
                    sessionCache.ServerBatchInfo = serverSyncChanges.ServerBatchInfo;
                    sessionCache.ServerChangesSelected = serverSyncChanges.ServerChangesSelected;
                    sessionCache.ClientChangesApplied = serverSyncChanges.ServerChangesApplied;

                    // Extract results for response
                    clientChangesApplied = serverSyncChanges.ServerChangesApplied;
                    serverChangesSelected = serverSyncChanges.ServerChangesSelected;
                    serverBatchInfo = serverSyncChanges.ServerBatchInfo;
                    remoteClientTimestamp = serverSyncChanges.RemoteClientTimestamp;

                    // Clean up client batch info if needed
                    var cleanFolder = this.Options.CleanFolder;
                    if (cleanFolder)
                        cleanFolder = await this.RemoteOrchestrator.InternalCanCleanFolderAsync(httpMessage.SyncContext.ScopeName, context.Parameters, sessionCache.ClientBatchInfo, default, cancellationToken);

                    if (cleanFolder)
                        sessionCache.ClientBatchInfo.TryRemoveDirectory();

                    // we do not need client batch info now
                    sessionCache.ClientBatchInfo = null;

                    // Retro compatibility to version < 0.9.3
                    if (serverSyncChanges.ServerBatchInfo.BatchPartsInfo == null)
                        serverSyncChanges.ServerBatchInfo.BatchPartsInfo = [];
                }

                // Build optimized response - now inherits from HttpMessageSummaryResponse so we can include server changes
                var response = new HttpMessageSendChangesIncrementalResponse
                {
                    // Incremental-specific properties
                    Operation = operation,
                    SchemaValid = schemaValid,
                    ServerScopeInfo = schemaValid ? null : serverScopeInfo, // Only send schema if invalid

                    // Summary response properties (inherited from HttpMessageSummaryResponse)
                    BatchInfo = serverBatchInfo,
                    RemoteClientTimestamp = remoteClientTimestamp,
                    ClientChangesApplied = clientChangesApplied,
                    ServerChangesSelected = serverChangesSelected,
                    ConflictResolutionPolicy = this.Options.ConflictResolutionPolicy,

                    // Base properties
                    SyncContext = context,
                    Step = HttpStep.SendChangesIncremental
                };
                
                if(serverBatchInfo.BatchPartsInfo.Count <= 1)
                    await this.AutomaticallyEndSession(httpContext, httpMessage.SyncContext, sessionCache, progress, cancellationToken);

                return response;
            }
            catch (Exception ex)
            {
                throw new SyncException(ex, SyncStage.ChangesApplying);
            }
        }

        /// <summary>
        /// Handle detailed error reporting from clients.
        /// </summary>
        protected internal virtual async Task<HttpMessageSendSyncErrorsResponse> SendSyncErrorsAsync(
            HttpContext httpContext, HttpMessageSendSyncErrorsRequest httpMessage,
            IProgress<ProgressArgs> progress, CancellationToken cancellationToken)
        {
            try
            {
                // Generate unique error ID for tracking
                var errorId = Guid.NewGuid().ToString("N");

                // Reconstruct exception from serialized info
                SyncException reconstructedException = null;
                if (httpMessage.SyncException != null)
                {
                    reconstructedException = httpMessage.SyncException.ToException();
                }

                // Create enhanced SessionEndArgs for interceptors
                var context = httpMessage.SyncContext ?? new SyncContext(httpMessage.SessionId, httpMessage.ScopeName);
                var sessionEndArgs = new SessionEndArgs(context, null, reconstructedException, null);


                // Fire OnSessionEnd interceptors with error details
                await this.RemoteOrchestrator.InterceptAsync(sessionEndArgs, progress, cancellationToken);

                // Log detailed error information
                await this.LogDetailedErrorAsync(httpMessage, errorId, reconstructedException);

                // Generate recommendations based on error type
                var recommendations = this.GenerateErrorRecommendations(reconstructedException, httpMessage.ErrorContext);

                // Create response
                var response = new HttpMessageSendSyncErrorsResponse
                {
                    SyncContext = context,
                    ErrorReceived = true,
                    ErrorId = errorId,
                    ServerTimestamp = DateTime.UtcNow,
                    Recommendations = recommendations,
                    ServerStep = HttpStep.SendSyncErrors
                };

                return response;
            }
            catch (Exception ex)
            {
                // Error handling should be robust - log but don't fail
                this.Options.Logger?.LogError(ex, "Failed to handle sync error report");

                var errorResponse = new HttpMessageSendSyncErrorsResponse
                {
                    ErrorReceived = false,
                    ServerTimestamp = DateTime.UtcNow,
                    ServerStep = HttpStep.SendSyncErrors
                };

                return errorResponse;
            }
        }

        /// <summary>
        /// Log detailed error information for monitoring and debugging.
        /// </summary>
        private async Task LogDetailedErrorAsync(HttpMessageSendSyncErrorsRequest request, string errorId, Exception exception)
        {
            try
            {
                var logData = new
                {
                    ErrorId = errorId,
                    SessionId = request.SessionId,
                    ScopeName = request.ScopeName,
                    ClientId = request.ClientId,
                    ErrorTimestamp = request.ErrorTimestamp,
                    ExceptionType = request.SyncException?.ExceptionType,
                    ExceptionMessage = request.SyncException?.Message,
                    SyncStage = request.SyncException?.SyncStage,
                    TableName = request.SyncException?.TableName,
                    ErrorPhase = request.ErrorContext?.Phase,
                    ClientVersion = request.ErrorContext?.ClientInfo?.DotMimSyncVersion,
                    ClientOS = request.ErrorContext?.ClientInfo?.OperatingSystem,
                    DatabaseProvider = request.ErrorContext?.ClientInfo?.DatabaseProvider,
                    StackTrace = request.SyncException?.StackTrace,
                    InnerExceptions = GetInnerExceptionMessages(request.SyncException),
                    ExceptionData = request.SyncException?.Data,
                    AdditionalInfo = request.ErrorContext?.AdditionalInfo
                };

                // Log structured error data
                this.Options.Logger?.LogError(exception, "Detailed sync error report: {ErrorData}",
                    System.Text.Json.JsonSerializer.Serialize(logData, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

                // Fire additional interceptor for custom error handling
                var errorReportArgs = new ErrorReportArgs(null, null, null)
                {
                    ErrorId = errorId,
                    Request = request,
                    Exception = exception,
                    LogData = logData
                };

                await this.RemoteOrchestrator.InterceptAsync(errorReportArgs, progress: null, CancellationToken.None);
            }
            catch (Exception logEx)
            {
                // Don't let logging failures affect error reporting
                this.Options.Logger?.LogWarning(logEx, "Failed to log detailed error information");
            }
        }

        /// <summary>
        /// Generate contextual recommendations based on error type.
        /// </summary>
        private string[] GenerateErrorRecommendations(Exception exception, SyncErrorContext errorContext)
        {
            var recommendations = new List<string>();

            if (exception is SyncException syncEx)
            {
                switch (syncEx.SyncStage)
                {
                    case SyncStage.Provisioning:
                        recommendations.Add("Check database permissions for schema creation");
                        recommendations.Add("Verify database connection and credentials");
                        break;

                    case SyncStage.ChangesApplying:
                        recommendations.Add("Check for foreign key constraint violations");
                        recommendations.Add("Verify data type compatibility between client and server");
                        break;

                    case SyncStage.ChangesSelecting:
                        recommendations.Add("Check database connection stability");
                        recommendations.Add("Verify query timeout settings");
                        break;
                }
            }

            // Network-related recommendations
            if (exception.Message.Contains("timeout") || exception.Message.Contains("connection"))
            {
                recommendations.Add("Check network connectivity to sync server");
                recommendations.Add("Consider increasing connection timeout values");
                recommendations.Add("Verify firewall settings allow sync traffic");
            }

            // Schema-related recommendations
            if (exception.Message.Contains("schema") || exception.Message.Contains("column"))
            {
                recommendations.Add("Verify schema compatibility between client and server");
                recommendations.Add("Consider running schema migration or reinitialization");
            }

            return recommendations.ToArray();
        }

        /// <summary>
        /// Extract all inner exception messages for logging.
        /// </summary>
        private List<string> GetInnerExceptionMessages(SerializableExceptionInfo exceptionInfo)
        {
            var messages = new List<string>();
            var current = exceptionInfo?.InnerException;

            while (current != null)
            {
                messages.Add($"[{current.ExceptionType}] {current.Message}");
                current = current.InnerException;
            }

            return messages;
        }
    }

    /// <summary>
    /// Custom interceptor args for error reporting.
    /// </summary>
    public class ErrorReportArgs : ProgressArgs
    {
        public string ErrorId { get; set; }
        public HttpMessageSendSyncErrorsRequest Request { get; set; }
        public Exception Exception { get; set; }
        public object LogData { get; set; }

        public ErrorReportArgs(SyncContext context, DbConnection connection, DbTransaction transaction) : base(context, connection, transaction)
        {
        }
    }
}