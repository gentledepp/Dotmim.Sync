using Wormhole.Sync.Batch;
using Wormhole.Sync.Builders;
using Wormhole.Sync.Enumerations;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace Wormhole.Sync
{
    /// <summary>
    /// Contains methods to handle conflicts.
    /// </summary>
    public abstract partial class BaseOrchestrator
    {

        /// <summary>
        /// Try to get a source row that is in conflict.
        /// </summary>
        internal async Task<(SyncContext Context, SyncRow SyncRow)> InternalGetConflictRowAsync(ScopeInfo scopeInfo, SyncContext context,
            SyncTable schemaTable, SyncRow primaryKeyRow, DbConnection connection, DbTransaction transaction,
            IProgress<ProgressArgs> progress, CancellationToken cancellationToken)
        {
            try
            {
                var syncAdapter = this.GetSyncAdapter(schemaTable, scopeInfo);

                // Get the row in the local repository
                var (command, _) = await this.InternalGetCommandAsync(scopeInfo, context, syncAdapter, DbCommandType.SelectRow,
                    connection, transaction, default, default).ConfigureAwait(false);

                if (command == null)
                    return (context, null);

                // Set the parameters value from row
                this.InternalSetCommandParametersValues(context, command, DbCommandType.SelectRow, syncAdapter, connection, transaction,
                    primaryKeyRow, progress: progress, cancellationToken: cancellationToken);

                await this.InterceptAsync(new ExecuteCommandArgs(context, command, DbCommandType.SelectRow, connection, transaction), cancellationToken: cancellationToken).ConfigureAwait(false);

                // Create a select table based on the schema in parameter + scope columns
                var changesSet = schemaTable.Schema.Clone(false);
                var selectTable = CreateChangesTable(schemaTable, changesSet);

                using var dataReader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

                var read = await dataReader.ReadAsync(cancellationToken).ConfigureAwait(false);

                if (!read)
                {

#if NET6_0_OR_GREATER
                    await dataReader.CloseAsync().ConfigureAwait(false);
#else
                    dataReader.Close();
#endif
                    command.Dispose();
                    return (context, null);
                }

                // Create a new empty row
                var syncRow = selectTable.NewRow();
                for (var i = 0; i < dataReader.FieldCount; i++)
                {
                    var columnName = dataReader.GetName(i);

                    // if we have the tombstone value, do not add it to the table
                    if (columnName == "sync_row_is_tombstone")
                    {
                        var isTombstone = SyncTypeConverter.TryConvertTo<long>(dataReader.GetValue(i)) > 0;
                        syncRow.RowState = isTombstone ? SyncRowState.Deleted : SyncRowState.Modified;
                        continue;
                    }

                    if (columnName == "sync_update_scope_id")
                        continue;

                    var columnValueObject = dataReader.GetValue(i);
                    var columnValue = columnValueObject == DBNull.Value ? null : columnValueObject;
                    syncRow[columnName] = columnValue;
                }

                // if syncRow is not a deleted row, we can check for which kind of row it is.
                if (syncRow != null && syncRow.RowState == SyncRowState.None)
                    syncRow.RowState = SyncRowState.Modified;

#if NET6_0_OR_GREATER
                await dataReader.CloseAsync().ConfigureAwait(false);
#else
                dataReader.Close();
#endif

                command.Dispose();

                return (context, syncRow);
            }
            catch (Exception ex)
            {
                string message = null;

                if (primaryKeyRow != null)
                    message += $"Primary Key:{primaryKeyRow}.";

                throw this.GetSyncError(context, ex, message);
            }
        }

        /// <summary>
        /// We have a conflict, try to get the source row and generate a conflict.
        /// </summary>
        internal SyncConflict InternalGetConflict(SyncContext context, SyncRow remoteConflictRow, SyncRow localConflictRow)
        {
            try
            {
                var dbConflictType = ConflictType.ErrorsOccurred;

                if (remoteConflictRow == null)
                    throw new UnknownException("Remote Conflict Row Should Exists.");

                // local row is null
                if (localConflictRow == null && remoteConflictRow.RowState == SyncRowState.Modified)
                    dbConflictType = ConflictType.RemoteExistsLocalNotExists;
                else if (localConflictRow == null && remoteConflictRow.RowState == SyncRowState.Deleted)
                    dbConflictType = ConflictType.RemoteIsDeletedLocalNotExists;

                //// remote row is null. Can't happen
                // else if (remoteConflictRow == null && localConflictRow.RowState == SyncRowState.Modified)
                //    dbConflictType = ConflictType.RemoteNotExistsLocalExists;
                // else if (remoteConflictRow == null && localConflictRow.RowState == SyncRowState.Deleted)
                //    dbConflictType = ConflictType.RemoteNotExistsLocalIsDeleted;
                else if (remoteConflictRow.RowState == SyncRowState.Deleted && localConflictRow.RowState == SyncRowState.Deleted)
                    dbConflictType = ConflictType.RemoteIsDeletedLocalIsDeleted;
                else if (remoteConflictRow.RowState == SyncRowState.Modified && localConflictRow.RowState == SyncRowState.Deleted)
                    dbConflictType = ConflictType.RemoteExistsLocalIsDeleted;
                else if (remoteConflictRow.RowState == SyncRowState.Deleted && localConflictRow.RowState == SyncRowState.Modified)
                    dbConflictType = ConflictType.RemoteIsDeletedLocalExists;
                else if (remoteConflictRow.RowState == SyncRowState.Modified && localConflictRow.RowState == SyncRowState.Modified)
                    dbConflictType = ConflictType.RemoteExistsLocalExists;

                // Generate the conflict
                var conflict = new SyncConflict(dbConflictType);
                conflict.AddRemoteRow(remoteConflictRow);

                if (localConflictRow != null)
                    conflict.AddLocalRow(localConflictRow);

                return conflict;
            }
            catch (Exception ex)
            {
                string message = null;

                if (localConflictRow != null)
                    message += $"Local Row:{localConflictRow}.";

                if (remoteConflictRow != null)
                    message += $"Remote Row:{remoteConflictRow}.";

                throw this.GetSyncError(context, ex, message);
            }
        }

        /// <summary>
        /// A conflict has occured, we try to ask for the solution to the user.
        /// </summary>
        private async Task<(ConflictResolution ConflictResolution, ConflictType ConflictType, SyncRow FinalRow, Guid? FinalSenderScopeId, bool InterceptorWasCalled)>
            GetConflictResolutionAsync(ScopeInfo scopeInfo, SyncContext context, Guid localScopeId, SyncRow conflictRow,
            SyncTable schemaChangesTable, ConflictResolutionPolicy policy, ConflictResolution? specifiedResolution, Guid senderScopeId,
            DbConnection connection, DbTransaction transaction, IProgress<ProgressArgs> progress, CancellationToken cancellationToken,
            Dictionary<string, SyncRow> conflictRowsCache = null)
        {

            // Use specified resolution if provided, otherwise use policy
            var resolution = specifiedResolution ?? (policy == ConflictResolutionPolicy.ClientWins ? ConflictResolution.ClientWins : ConflictResolution.ServerWins);

            // check the interceptor
            var interceptors = this.Interceptors.GetInterceptors<ApplyChangesConflictOccuredArgs>();

            SyncRow finalRow = null;
            Guid? finalSenderScopeId = senderScopeId;
            var interceptorWasCalled = false;

            // Check if we have a pre-resolved conflict (from MarkAsResolvedConflict)
            // If resolution is pre-determined and NOT MergeRow, skip calling conflict interceptors
            // MergeRow requires interceptor to provide the merged row, so we still call it
            var skipInterceptors = specifiedResolution.HasValue && specifiedResolution.Value != ConflictResolution.MergeRow;

            var arg = new ApplyChangesConflictOccuredArgs(scopeInfo, context, this, conflictRow, schemaChangesTable, resolution, senderScopeId, connection, transaction, conflictRowsCache);

            // Always determine the correct conflict type by checking if local row exists
            // This is critical for NULLC scenarios (remote deleted, local not exists) where the
            // default assumption of "local exists" would cause incorrect behavior
            // Uses cache when available (batch mode) for efficiency
            var conflict = await arg.GetSyncConflictAsync().ConfigureAwait(false);
            var conflictType = conflict?.Type ?? (conflictRow.RowState == SyncRowState.Deleted ? ConflictType.RemoteIsDeletedLocalExists : ConflictType.RemoteExistsLocalExists);

            if (interceptors.Count > 0 && !skipInterceptors)
            {
                // Interceptor
                await this.InterceptAsync(arg, progress, cancellationToken).ConfigureAwait(false);

                resolution = arg.Resolution;
                finalRow = arg.Resolution == ConflictResolution.MergeRow ? arg.FinalRow : null;
                finalSenderScopeId = arg.SenderScopeId;
                interceptorWasCalled = true;
            }
            else
            {
                // Check logger, because we make some reflection here
                if (this.Logger.IsEnabled(LogLevel.Debug))
                    this.Logger.LogDebug(new EventId(arg.EventId, "ApplyChangesConflictOccured"), arg);
            }

            // returning the action to take, and actually the finalRow if action is set to Merge
            return (resolution, conflictType, finalRow, finalSenderScopeId, interceptorWasCalled);
        }

        /// <summary>
        /// Handle a conflict
        /// The int returned is the conflict count I need.
        /// When deferApply is true, the method returns the resolved action without executing it,
        /// allowing the caller to batch-apply resolved conflicts.
        /// </summary>
        private async Task<(bool IsApplied, bool IsConflictResolved, Exception Exception, SyncRow DeferredRow, Guid? DeferredSenderScopeId, bool DeferredIsDelete)> HandleConflictAsync(ScopeInfo scopeInfo, SyncContext context,
                                BatchInfo batchInfo, Guid localScopeId, Guid senderScopeId, SyncRow conflictRow,
                                SyncTable schemaChangesTable,
                                ConflictResolutionPolicy policy, ConflictResolution? specifiedResolution, long? lastTimestamp,
                                DbConnection connection, DbTransaction transaction,
                                IProgress<ProgressArgs> progress, CancellationToken cancellationToken,
                                Dictionary<string, SyncRow> conflictRowsCache = null, bool deferApply = false)
        {
            var (conflictResolution, conflictType, finalRow, nullableSenderScopeId, interceptorWasCalled) =
                 await this.GetConflictResolutionAsync(scopeInfo, context, localScopeId, conflictRow, schemaChangesTable,
                policy, specifiedResolution, senderScopeId, connection, transaction, progress, cancellationToken, conflictRowsCache).ConfigureAwait(false);

            Exception exception = null;
            var applied = false;
            var conflictResolved = false;
            bool operationComplete;

            switch (conflictResolution)
            {
                case ConflictResolution.ServerWins:
                    var weAreTheClient = context.SyncRole == SyncRole.Client;

                    if (weAreTheClient)
                    {
                        // ON CLIENT: Apply what the server has (server wins)
                        switch (conflictType)
                        {
                            // Server has the row (Modified) → Update/insert on client
                            case ConflictType.RemoteExistsLocalExists:
                            case ConflictType.RemoteExistsLocalNotExists:
                            case ConflictType.RemoteExistsLocalIsDeleted:
                            case ConflictType.UniqueKeyConstraint:
                                if (deferApply)
                                    return (false, true, null, conflictRow, nullableSenderScopeId, false);

                                (_, operationComplete, exception) = await this.InternalApplyUpdateAsync(scopeInfo, context, batchInfo,
                                    conflictRow, schemaChangesTable, lastTimestamp, nullableSenderScopeId, true, connection, transaction, progress, cancellationToken).ConfigureAwait(false);

                                applied = operationComplete;
                                conflictResolved = operationComplete && exception == null;
                                break;

                            // Server deleted, client deleted → Both deleted, nothing to do
                            case ConflictType.RemoteIsDeletedLocalIsDeleted:
                                applied = false;
                                conflictResolved = true;
                                break;

                            // Server deleted, client doesn't have it → Nothing to do
                            // This is a valid conflict resolution - the conflict was handled with no action needed
                            case ConflictType.RemoteIsDeletedLocalNotExists:
                                applied = false;
                                conflictResolved = true;
                                break;

                            // Server deleted, client has it → Delete on client
                            case ConflictType.RemoteIsDeletedLocalExists:
                                if (deferApply)
                                    return (false, true, null, conflictRow, nullableSenderScopeId, true);

                                (_, operationComplete, exception) = await this.InternalApplyDeleteAsync(scopeInfo, context, batchInfo,
                                    conflictRow, schemaChangesTable, lastTimestamp, nullableSenderScopeId, true, connection, transaction, progress, cancellationToken).ConfigureAwait(false);

                                applied = operationComplete;
                                conflictResolved = operationComplete && exception == null;
                                break;

                            case ConflictType.ErrorsOccurred:
                                break;
                        }
                    }
                    else
                    {
                        // ON SERVER: Server wins behavior depends on whether conflict was pre-resolved
                        if (specifiedResolution.HasValue)
                        {
                            // Pre-resolved conflict (from MarkAsResolvedConflict) - fetch and send server's data to client
                            // This is needed because the row was rejected in validation, so client needs server's version
                            (_, var serverRow) = await this.InternalGetConflictRowAsync(scopeInfo, context, schemaChangesTable,
                                conflictRow, connection, transaction, progress, cancellationToken).ConfigureAwait(false);

                            if (serverRow != null)
                            {
                                // Update tracking table metadata to mark server's row for sending to client
                                // This updates update_scope_id to Guid.Empty, marking the row as changed without touching base table
                                (_, var isUpdated, exception) = await this.InternalUpdateMetadatasAsync(scopeInfo, context,
                                    serverRow, schemaChangesTable, Guid.Empty, true, connection, transaction, progress, cancellationToken).ConfigureAwait(false);

                                applied = isUpdated;
                                conflictResolved = isUpdated && exception == null;
                            }
                            else
                            {
                                // Server row doesn't exist (deleted?) - nothing to send back
                                applied = false;
                                conflictResolved = true;
                            }
                        }
                        else
                        {
                            // Normal policy-based ServerWins - server already has winning data, no action needed
                            applied = false;
                            conflictResolved = true;
                        }
                    }
                    break;
                case ConflictResolution.ClientWins:
                    weAreTheClient = context.SyncRole == SyncRole.Client;

                    if (weAreTheClient)
                    {
                        // ON CLIENT: Client wins, so client already has the winning data
                        // No matter what the conflict type is, we do nothing on the client
                        applied = false;
                        conflictResolved = true;
                    }
                    else
                    {
                        // ON SERVER: Apply what the client has (client wins)
                        switch (conflictType)
                        {
                            // Client has the row (Modified) → Update/insert on server
                            case ConflictType.RemoteExistsLocalExists:
                            case ConflictType.RemoteExistsLocalNotExists:
                            case ConflictType.RemoteExistsLocalIsDeleted:
                            case ConflictType.UniqueKeyConstraint:
                                if (deferApply)
                                    return (false, true, null, conflictRow, nullableSenderScopeId, false);

                                (_, operationComplete, exception) = await this.InternalApplyUpdateAsync(scopeInfo, context, batchInfo,
                                    conflictRow, schemaChangesTable, lastTimestamp, nullableSenderScopeId, true, connection, transaction, progress, cancellationToken).ConfigureAwait(false);

                                applied = operationComplete;
                                conflictResolved = operationComplete && exception == null;
                                break;

                            // Client deleted, server deleted → Both deleted, nothing to do
                            case ConflictType.RemoteIsDeletedLocalIsDeleted:
                                applied = false;
                                conflictResolved = true;
                                break;

                            // Client deleted, server doesn't have it → Nothing to do
                            // This is a valid conflict resolution - the conflict was handled with no action needed
                            case ConflictType.RemoteIsDeletedLocalNotExists:
                                applied = false;
                                conflictResolved = true;
                                break;

                            // Client deleted, server has it → Delete on server
                            case ConflictType.RemoteIsDeletedLocalExists:
                                if (deferApply)
                                    return (false, true, null, conflictRow, nullableSenderScopeId, true);

                                (_, operationComplete, exception) = await this.InternalApplyDeleteAsync(scopeInfo, context, batchInfo,
                                    conflictRow, schemaChangesTable, lastTimestamp, nullableSenderScopeId, true, connection, transaction, progress, cancellationToken).ConfigureAwait(false);

                                // If delete failed because row already gone, that's OK
                                if (!operationComplete && exception == null)
                                {
                                    applied = false;
                                    conflictResolved = true;
                                }
                                else
                                {
                                    applied = operationComplete && exception == null;
                                    conflictResolved = operationComplete && exception == null;
                                }
                                break;

                            case ConflictType.ErrorsOccurred:
                                break;
                        }
                    }

                    break;
                case ConflictResolution.MergeRow:
                    // if merge, we update locally the row and let the sync_update_scope_id set to null
                    // We don't update metadatas so the row is updated locally and is marked as updated by the trigger
                    // and will be returned back to client if occurs on server
                    // and will be returned to the server on next sync if occurs on client
                    if (deferApply)
                        return (false, true, null, finalRow, null, false);

                    (_, operationComplete, exception) = await this.InternalApplyUpdateAsync(scopeInfo, context, batchInfo,
                        finalRow, schemaChangesTable, lastTimestamp, null, true, connection, transaction, progress, cancellationToken).ConfigureAwait(false);

                    if (!operationComplete && exception == null)
                        exception = new Exception("Can't update the merged row locally.");

                    applied = operationComplete && exception == null;
                    conflictResolved = operationComplete && exception == null;

                    break;
                case ConflictResolution.Throw:
                    throw new RollbackException("Rollback action taken on conflict");
            }

            return (applied, conflictResolved, exception, null, null, false);
        }
    }
}