using Wormhole.Sync.Batch;
using Wormhole.Sync.Enumerations;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading.Tasks;

namespace Wormhole.Sync
{
    /// <summary>
    /// Event args before validating a batch of changes before they are applied to a datasource.
    /// This validation phase runs BEFORE the RowsChangesApplying phase and allows marking rows as conflicts
    /// without triggering the validation again during conflict resolution (preventing infinite loops).
    /// </summary>
    public class RowsChangesValidatingArgs : ProgressArgs
    {

        /// <inheritdoc cref="RowsChangesValidatingArgs"/>
        public RowsChangesValidatingArgs(SyncContext context, BatchInfo batchInfo, List<SyncRow> syncRows, SyncTable schemaTable, SyncRowState state, DbConnection connection, DbTransaction transaction)
            : base(context, connection, transaction)
        {
            this.State = state;
            this.BatchInfo = batchInfo;
            this.SyncRows = syncRows;
            this.SchemaTable = schemaTable;
            this.RejectedRows = new Dictionary<SyncRow, ConflictResolution?>();
        }

        /// <summary>
        /// Gets or sets a value indicating whether the validation should be canceled.
        /// </summary>
        public bool Cancel { get; set; }

        /// <summary>
        /// Gets the dictionary of rows that have been marked as conflicts and should not be applied to the database.
        /// The key is the SyncRow, and the value is the optional ConflictResolution to use (null means use normal conflict policy).
        /// </summary>
        public Dictionary<SyncRow, ConflictResolution?> RejectedRows { get; }

        /// <summary>
        /// Marks a row as a conflict. The row will NOT be applied to the database and will be handled as a conflict.
        /// The normal conflict resolution policy will be used to determine the final state.
        /// </summary>
        /// <param name="row">The row to mark as a conflict.</param>
        public void MarkAsConflict(SyncRow row)
        {
            if (row != null && !this.RejectedRows.ContainsKey(row))
                this.RejectedRows[row] = null;
        }

        /// <summary>
        /// Marks a row as a conflict with a pre-determined resolution. The row will NOT be applied to the database.
        /// The conflict resolution is already decided and will be applied directly WITHOUT calling conflict handlers.
        /// Exception: If resolution is MergeRow, this behaves like MarkAsConflict(row) and the conflict handler WILL be called.
        /// </summary>
        /// <param name="row">The row to mark as a conflict.</param>
        /// <param name="resolution">The conflict resolution to apply (ServerWins, ClientWins, or MergeRow).</param>
        public void MarkAsResolvedConflict(SyncRow row, ConflictResolution resolution)
        {
            if (row != null)
            {
                // MergeRow requires conflict handler to decide what to merge, so store null
                if (resolution == ConflictResolution.MergeRow)
                    this.RejectedRows[row] = null;
                else
                    this.RejectedRows[row] = resolution;  // ServerWins or ClientWins - pre-resolved
            }
        }

        /// <summary>
        /// Gets the RowState of the rows being validated.
        /// </summary>
        public SyncRowState State { get; }

        /// <summary>
        /// Gets batchinfo serialized on disk, containing the rows to be validated.
        /// </summary>
        public BatchInfo BatchInfo { get; }

        /// <summary>
        /// Gets the rows to be validated.
        /// </summary>
        public List<SyncRow> SyncRows { get; }

        /// <summary>
        /// Gets the schema of the rows to be validated.
        /// </summary>
        public SyncTable SchemaTable { get; }

        /// <inheritdoc cref="ProgressArgs.ProgressLevel"/>
        public override SyncProgressLevel ProgressLevel => SyncProgressLevel.Debug;

        /// <inheritdoc cref="ProgressArgs.Message"/>
        public override string Message => $"Validating [{this.SchemaTable.GetFullName()}] batch rows. State:{this.State}. Count:{this.SyncRows.Count}";

        /// <inheritdoc cref="ProgressArgs.EventId"/>
        public override int EventId => 13099;
    }

    /// <summary>
    /// Interceptor called before validating a batch of rows.
    /// This validation phase allows marking rows as conflicts without triggering validation during conflict resolution.
    /// </summary>
    public partial class InterceptorsExtensions
    {
        /// <summary>
        /// Occurs just before validating a batch of rows before they are applied to the local (client or server) database.
        /// This validation phase runs BEFORE the RowsChangesApplying phase and is NOT called during conflict resolution,
        /// preventing infinite loops when conflicts are being resolved.
        /// <example>
        /// <code>
        /// localOrchestrator.OnRowsChangesValidating(async args =>
        /// {
        ///     // Validate rows and mark conflicts BEFORE any DB operations
        ///     foreach (var row in args.SyncRows)
        ///     {
        ///         if (ShouldReject(row))
        ///             args.MarkAsConflict(row);
        ///     }
        /// });
        /// </code>
        /// </example>
        /// </summary>
        public static Guid OnRowsChangesValidating(this BaseOrchestrator orchestrator, Action<RowsChangesValidatingArgs> action)
            => orchestrator.AddInterceptor(action);

        /// <inheritdoc cref="OnRowsChangesValidating(BaseOrchestrator, Action{RowsChangesValidatingArgs})"/>
        public static Guid OnRowsChangesValidating(this BaseOrchestrator orchestrator, Func<RowsChangesValidatingArgs, Task> action)
            => orchestrator.AddInterceptor(action);
    }
}
