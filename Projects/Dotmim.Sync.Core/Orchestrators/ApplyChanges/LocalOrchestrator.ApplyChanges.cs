using Wormhole.Sync.Batch;
using Wormhole.Sync.Enumerations;
using Wormhole.Sync.Extensions;
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
    /// Contains methods to apply changes on the local provider.
    /// </summary>
    public partial class LocalOrchestrator : BaseOrchestrator
    {
        /// <summary>
        /// Apply changes locally.
        /// </summary>
        internal virtual async Task<(SyncContext Context, ClientSyncChanges ClientSyncChange, ScopeInfoClient CScopeInfoClient)>
            InternalApplyChangesAsync(ScopeInfo cScopeInfo, ScopeInfoClient cScopeInfoClient, SyncContext context, ServerSyncChanges serverSyncChanges,
                              ClientSyncChanges clientSyncChanges, ConflictResolutionPolicy policy, bool snapshotApplied,
                              DbConnection connection = default, DbTransaction transaction = default,
                              IProgress<ProgressArgs> progress = null, CancellationToken cancellationToken = default)
        {
            using var _ = context.UsingBatchCache();

            // If we have a transient error happening, and we are rerunning the tranaction,
            // raising an interceptor
            var onRetry = new Func<Exception, int, TimeSpan, object, Task>((ex, cpt, ts, arg) =>
                this.InterceptAsync(new TransientErrorOccuredArgs(context, connection, ex, cpt, ts), progress, cancellationToken).AsTask());

            // Defining my retry policy
            SyncPolicy retryPolicy = this.Options.TransactionMode == TransactionMode.AllOrNothing
             ? retryPolicy = SyncPolicy.WaitAndRetryForever(retryAttempt => TimeSpan.FromMilliseconds(500 * retryAttempt), (ex, arg) => this.Provider.ShouldRetryOn(ex), onRetry)
             : retryPolicy = SyncPolicy.WaitAndRetry(0, TimeSpan.Zero);

            // Execute my OpenAsync in my policy context
            var applyChangesResult = await retryPolicy.ExecuteAsync<(SyncContext Context, ClientSyncChanges ClientSyncChange, ScopeInfoClient CScopeInfoClient)>(
            async ct =>
            {
                // Connection & Transaction runner
                DbConnectionRunner runner = null;
                bool disabledDbLevelConstraints = false;
                try
                {
                    var serverBatchInfo = serverSyncChanges.ServerBatchInfo;
                    var remoteClientTimestamp = serverSyncChanges.RemoteClientTimestamp;

                    // applied changes to clients
                    var clientChangesApplied = new DatabaseChangesApplied();

                    // Create a message containing everything needed to apply errors rows
                    BatchInfo lastSyncErrorsBatchInfo = null;

                    // Storeing all failed rows in a Set
                    var failedRows = cScopeInfo.Schema.Clone();

                    // if not null, rollback
                    ApplyChangesException failureException = null;

                    // BatchInfo containing errors
                    BatchInfo errorsBatchInfo = null;

                    // Gets the existing errors from past sync
                    if (cScopeInfoClient != null && !string.IsNullOrEmpty(cScopeInfoClient.Errors))
                    {
                        try
                        {
                            lastSyncErrorsBatchInfo = !string.IsNullOrEmpty(cScopeInfoClient.Errors) ? Serializer.Deserialize<BatchInfo>(cScopeInfoClient.Errors) : null;
                        }
                        catch (Exception)
                        {
                        }
                    }

                    context.SyncRole = SyncRole.Client;

                    // Check for reinit tables BEFORE opening any transaction.
                    // For database-level constraint providers (e.g. SQLite), PRAGMA foreign_keys = OFF
                    // must be executed BEFORE starting a transaction, as it is silently ignored inside one.
                    // This covers FK references from tables outside the sync scope as well.
                    var reinitSet = cScopeInfoClient.GetReinitTablesSet();

                    if (reinitSet.Count > 0 && this.Provider.ConstraintsLevelAction == ConstraintsLevelAction.OnDatabaseLevel)
                    {
                        connection ??= this.Provider.CreateConnection();

                        if (connection.State != System.Data.ConnectionState.Open)
                            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

                        using var pragmaOffCmd = connection.CreateCommand();
                        pragmaOffCmd.CommandText = "PRAGMA foreign_keys = OFF";
                        await pragmaOffCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                        disabledDbLevelConstraints = true;
                    }

                    // Transaction mode
                    if (this.Options.TransactionMode == TransactionMode.AllOrNothing)
                    {
                        runner = await this.GetConnectionAsync(context, SyncMode.WithTransaction, SyncStage.ChangesApplying, connection, transaction, progress, cancellationToken).ConfigureAwait(false);

                        // affect connection and transaction to reaffect later on save scope
                        connection = runner.Connection;
                        transaction = runner.Transaction;
                        cancellationToken = runner.CancellationToken;
                        progress = runner.Progress;
                    }

                    // Create the message containing everything needed to apply changes
                    var applyChanges = new MessageApplyChanges(cScopeInfoClient.Id, cScopeInfo.Id, cScopeInfoClient.IsNewScope, cScopeInfoClient.LastSyncTimestamp,
                        cScopeInfo.Schema, policy, snapshotApplied, this.Options.BatchDirectory, serverBatchInfo, failedRows, clientChangesApplied);

                    // Pass per-table reinit set so apply logic can reset + insert-only for schema-evolved tables
                    if (reinitSet.Count > 0)
                        applyChanges.ReinitTables = reinitSet;

                    // call interceptor
                    var databaseChangesApplyingArgs = new DatabaseChangesApplyingArgs(context, applyChanges, connection, transaction);
                    await this.InterceptAsync(databaseChangesApplyingArgs, progress, cancellationToken).ConfigureAwait(false);

                    // If we have existing errors happened last sync, we should try to apply them now
                    if (lastSyncErrorsBatchInfo != null && lastSyncErrorsBatchInfo.HasData())
                    {
                        // Try to clean errors before trying
                        applyChanges.Changes = serverBatchInfo;
                        await this.InternalApplyCleanErrorsAsync(cScopeInfo, context, lastSyncErrorsBatchInfo, applyChanges, connection, transaction, progress, cancellationToken).ConfigureAwait(false);

                        // Call apply errors on provider
                        applyChanges.Changes = lastSyncErrorsBatchInfo;
                        failureException = await this.InternalApplyChangesAsync(cScopeInfo, context, applyChanges, connection, transaction, progress, cancellationToken).ConfigureAwait(false);
                    }

                    if (failureException != null)
                        throw failureException;

                    if (serverBatchInfo != null && serverBatchInfo.HasData())
                    {
                        // Call apply changes on provider
                        applyChanges.Changes = serverBatchInfo;
                        failureException = await this.InternalApplyChangesAsync(cScopeInfo, context, applyChanges, connection, transaction, progress, cancellationToken).ConfigureAwait(false);
                    }

                    if (failureException != null)
                        throw failureException;

                    // Call OnAfterApplyChanges for all tables in the scope to clean up sync_session_id
                    // This must be called even if no changes were applied from the server
                    if (this.Provider is IAfterApplyChangesProvider afterApplyChangesProvider)
                    {
                        DbConnectionRunner r2 = null;
                        try
                        {
                            DbConnection conn = connection;
                            DbTransaction trans = transaction;
                            if (conn is null)
                            {
                                r2 = await this.GetConnectionAsync(context,
                                    this.Options.TransactionMode == TransactionMode.PerBatch
                                        ? SyncMode.WithTransaction
                                        : SyncMode.NoTransaction, SyncStage.ChangesApplying, connection, transaction,
                                    progress, cancellationToken).ConfigureAwait(false);

                                conn = r2.Connection;
                                trans = r2.Transaction;
                            }

                            foreach (var syncTable in cScopeInfo.Schema.Tables)
                            {
                                await afterApplyChangesProvider.OnAfterApplyChangesAsync(
                                        cScopeInfo, context, syncTable, conn, trans, cancellationToken)
                                    .ConfigureAwait(false);
                            }
                        }
                        catch (Exception ex)
                        {
                            if (r2 != null)
                                await r2.RollbackAsync($"InternalApplyChangesAsync Rollback. Error:{ex.Message}").ConfigureAwait(false);

                            throw this.GetSyncError(context, ex);
                        }
                        finally
                        {
                            if (r2 != null)
                                await r2.DisposeAsync().ConfigureAwait(false);
                        }
                    }

                    if (cancellationToken.IsCancellationRequested)
                        cancellationToken.ThrowIfCancellationRequested();

                    // check if we need to delete metadatas
                    if (this.Options.CleanMetadatas && cScopeInfoClient.LastSyncTimestamp.HasValue)
                    {
                        using var runnerMetadata = await this.GetConnectionAsync(context, SyncMode.NoTransaction, SyncStage.MetadataCleaning, connection, transaction, progress, cancellationToken).ConfigureAwait(false);

                        var allScopeHistories = await this.InternalLoadAllScopeInfoClientsAsync(context, runnerMetadata.Connection, runnerMetadata.Transaction, runnerMetadata.Progress, runnerMetadata.CancellationToken).ConfigureAwait(false);

                        List<ScopeInfo> allClientScopes;
                        (context, allClientScopes) = await this.InternalLoadAllScopeInfosAsync(context, runnerMetadata.Connection, runnerMetadata.Transaction, runnerMetadata.Progress, runnerMetadata.CancellationToken).ConfigureAwait(false);

                        if (allScopeHistories.Count > 0 && allClientScopes.Count > 0)
                        {
                            // Get the min value from LastSyncTimestamp from all scopes
                            var minLastTimeStamp = allScopeHistories.Min(scope => scope.LastSyncTimestamp.HasValue ? scope.LastSyncTimestamp.Value : long.MaxValue);
                            minLastTimeStamp = minLastTimeStamp > cScopeInfoClient.LastSyncTimestamp.Value ? cScopeInfoClient.LastSyncTimestamp.Value : minLastTimeStamp;

                            DatabaseMetadatasCleaned databaseMetadatasCleaned;
                            (context, databaseMetadatasCleaned) = await this.InternalDeleteMetadatasAsync(allClientScopes, context, minLastTimeStamp, runnerMetadata.Connection, runnerMetadata.Transaction, runnerMetadata.Progress, runnerMetadata.CancellationToken).ConfigureAwait(false);

                            // save last cleanup timestamp
                            if (databaseMetadatasCleaned?.RowsCleanedCount > 0)
                            {
                                foreach (var clientScopeInfo in allClientScopes)
                                {
                                    clientScopeInfo.LastCleanupTimestamp = databaseMetadatasCleaned.TimestampLimit;

                                    await this.InternalSaveScopeInfoAsync(clientScopeInfo, context,
                                        runnerMetadata.Connection, runnerMetadata.Transaction, runnerMetadata.Progress, runnerMetadata.CancellationToken).ConfigureAwait(false);
                                }
                            }
                        }
                    }

                    // now the sync is complete, remember the time
                    this.CompleteTime = DateTime.UtcNow;

                    // Save all failed rows to disk
                    if (failedRows.Tables.Any(st => st.HasRows))
                    {
                        // Create a batch info for error rows
                        var info = connection != null && !string.IsNullOrEmpty(connection.Database) ? $"{connection.Database}_ERRORS" : "ERRORS";
                        errorsBatchInfo = new BatchInfo(this.Options.BatchDirectory, info: info);

                        var batchIndex = 0;
                        foreach (var table in failedRows.Tables)
                        {
                            if (!table.HasRows)
                                continue;

                            await using var localSerializer = new LocalJsonSerializer(this.BatchStorage, this, context);

                            var (filePath, fileName) = errorsBatchInfo.GetNewBatchPartInfoPath(table, batchIndex, "json", info);
                            var directoryPath = Path.GetDirectoryName(filePath);
                            var batchPartInfo = new BatchPartInfo(fileName, table.TableName, table.SchemaName, SyncRowState.None, table.Rows.Count, batchIndex);
                            errorsBatchInfo.BatchPartsInfo.Add(batchPartInfo);

                            await localSerializer.OpenFileAsync(directoryPath, fileName, table, SyncRowState.None).ConfigureAwait(false);

                            foreach (var row in table.Rows)
                                await localSerializer.WriteRowToFileAsync(row, table).ConfigureAwait(false);

                            batchIndex++;
                        }

                        failedRows.Clear();
                    }

                    // generate the new scope item
                    var newCScopeInfoClient = new ScopeInfoClient
                    {
                        Hash = cScopeInfoClient.Hash,
                        Name = cScopeInfoClient.Name,
                        Parameters = cScopeInfoClient.Parameters,
                        Id = cScopeInfoClient.Id,
                        IsNewScope = cScopeInfoClient.IsNewScope,
                        LastSyncTimestamp = clientSyncChanges.ClientTimestamp,
                        LastSync = this.CompleteTime,
                        LastServerSyncTimestamp = remoteClientTimestamp,
                        LastSyncDuration = this.CompleteTime.Value.Subtract(context.StartTime).Ticks,
                        Properties = cScopeInfoClient.Properties,
                        Errors = errorsBatchInfo != null && errorsBatchInfo.BatchPartsInfo != null && errorsBatchInfo.BatchPartsInfo.Count > 0 ? Serializer.Serialize(errorsBatchInfo).ToUtf8String() : null,
                        SupportedMigrations = cScopeInfoClient.SupportedMigrations,
                        LastKnownServerMigrations = cScopeInfoClient.LastKnownServerMigrations,
                        // NOTE: ReinitTables is intentionally NOT copied — it's a one-shot mechanism
                    };

                    // Write scopes locally
                    using (var runnerScopeInfo = await this.GetConnectionAsync(context, SyncMode.NoTransaction, SyncStage.ScopeWriting, connection, transaction, progress, cancellationToken).ConfigureAwait(false))
                    {
                        // re-apply parameters
                        context.Parameters = cScopeInfoClient.Parameters;

                        (context, cScopeInfoClient) = await this.InternalSaveScopeInfoClientAsync(newCScopeInfoClient, context,
                            runnerScopeInfo.Connection, runnerScopeInfo.Transaction, runnerScopeInfo.Progress, runnerScopeInfo.CancellationToken).ConfigureAwait(false);
                    }

                    var databaseChangesAppliedArgs = new DatabaseChangesAppliedArgs(context, clientChangesApplied, connection ??= this.Provider.CreateConnection(), transaction);
                    await this.InterceptAsync(databaseChangesAppliedArgs, progress, cancellationToken).ConfigureAwait(false);

                    if (this.Options.TransactionMode == TransactionMode.AllOrNothing && runner != null)
                        await runner.CommitAsync().ConfigureAwait(false);

                    clientSyncChanges.ClientChangesApplied = clientChangesApplied;

                    return (context, clientSyncChanges, cScopeInfoClient);
                }
                catch (Exception ex)
                {
                    if (runner != null)
                        await runner.RollbackAsync($"InternalApplyChangesAsync Rollback. Error:{ex.Message}").ConfigureAwait(false);

                    throw this.GetSyncError(context, ex);
                }
                finally
                {
                    // Re-enable database-level FK constraints if we force-disabled them.
                    // Only re-enable when the user did not request global constraint disabling,
                    // otherwise we would override their intent.
                    if (disabledDbLevelConstraints && !this.Options.DisableConstraintsOnApplyChanges)
                    {
                        try
                        {
                            if (connection?.State == System.Data.ConnectionState.Open)
                            {
                                using var pragmaOnCmd = connection.CreateCommand();
                                pragmaOnCmd.CommandText = "PRAGMA foreign_keys = ON";
                                await pragmaOnCmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                            }
                        }
                        catch
                        {
                            // Best-effort re-enable; connection may already be closed.
                        }
                    }

                    if (runner != null)
                        await runner.DisposeAsync().ConfigureAwait(false);
                }
            }, null, cancellationToken).ConfigureAwait(false);

            return applyChangesResult;
        }

        /// <summary>
        /// Apply a snapshot locally.
        /// </summary>
        internal virtual async Task<(SyncContext Context, ClientSyncChanges ClientSyncChanges, ScopeInfoClient CScopeInfoClient)>
            InternalApplySnapshotAsync(
            ScopeInfo clientScopeInfo,
            ScopeInfoClient cScopeInfoClient,
            SyncContext context, ServerSyncChanges serverSyncChanges, ClientSyncChanges clientSyncChanges,
            DbConnection connection = default, DbTransaction transaction = default, IProgress<ProgressArgs> progress = null, CancellationToken cancellationToken = default)
        {
            try
            {
                if (serverSyncChanges?.ServerBatchInfo == null)
                    return (context, clientSyncChanges, cScopeInfoClient);

                // Get context or create a new one
                context.SyncStage = SyncStage.SnapshotApplying;
                await this.InterceptAsync(new SnapshotApplyingArgs(context, this.Provider.CreateConnection()), progress, cancellationToken).ConfigureAwait(false);

                Guard.ThrowIfNull(clientScopeInfo.Schema);

                // Applying changes and getting the new client scope info
                (context, clientSyncChanges, cScopeInfoClient) = await this.InternalApplyChangesAsync(clientScopeInfo, cScopeInfoClient, context, serverSyncChanges,
                        clientSyncChanges, ConflictResolutionPolicy.ServerWins, false, connection, transaction, progress, cancellationToken).ConfigureAwait(false);

                await this.InterceptAsync(new SnapshotAppliedArgs(context, clientSyncChanges.ClientChangesApplied), progress, cancellationToken).ConfigureAwait(false);

                // re-apply scope is new flag
                // to be sure we are calling the Initialize method, even for the delta
                // in that particular case, we want the delta rows coming from the current scope
                // cScopeInfoClient.IsNewScope = true;
                return (context, clientSyncChanges, cScopeInfoClient);
            }
            catch (Exception ex)
            {
                throw this.GetSyncError(context, ex);
            }
        }
    }
}