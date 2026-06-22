using System;
using System.Data.Common;
using System.Threading.Tasks;
using Wormhole.Sync.Enumerations;

namespace Wormhole.Sync
{
    /// <summary>
    /// Event args fired when the per-table <c>DeleteMetadata</c> command is being built,
    /// just before it is sent to the database during a metadata cleanup.
    /// <para>
    /// The default text is <c>"DELETE [side] FROM {tracking} [side] WHERE [side].[timestamp] &lt;= @sync_row_timestamp"</c>.
    /// A subscriber may rewrite <see cref="Command"/>.CommandText to a custom statement (for example, a
    /// parent-aware DELETE that preserves a sub-table's tracking row while its parent's tracking row
    /// is still alive) as long as the resulting SQL still references the <c>@sync_row_timestamp</c>
    /// parameter, which is bound by the standard parameter setup.
    /// </para>
    /// </summary>
    public class DeleteMetadataCreatingArgs : ProgressArgs
    {
        /// <inheritdoc cref="DeleteMetadataCreatingArgs" />
        public DeleteMetadataCreatingArgs(SyncContext context, ScopeInfo scopeInfo, SyncTable table, DbCommand command,
            DbConnection connection = null, DbTransaction transaction = null)
            : base(context, connection, transaction)
        {
            this.ScopeInfo = scopeInfo;
            this.Table = table;
            this.Command = command;
        }

        /// <summary>
        /// Gets the scope info this DeleteMetadata command belongs to.
        /// </summary>
        public ScopeInfo ScopeInfo { get; }

        /// <summary>
        /// Gets the table whose tracking rows are about to be cleaned.
        /// </summary>
        public SyncTable Table { get; }

        /// <summary>
        /// Gets or sets the DELETE command. Subscribers may replace <see cref="DbCommand.CommandText"/>
        /// to customize the cleanup query for this table.
        /// </summary>
        public DbCommand Command { get; set; }

        /// <inheritdoc cref="ProgressArgs.ProgressLevel"/>
        public override SyncProgressLevel ProgressLevel => SyncProgressLevel.Trace;

        /// <inheritdoc cref="ProgressArgs.Message"/>
        public override string Message => $"[{this.Table.GetFullName()}] DeleteMetadata command being built.";

        /// <inheritdoc cref="ProgressArgs.EventId"/>
        public override int EventId => 11250;
    }

    /// <summary>
    /// Partial Interceptors extensions.
    /// </summary>
    public partial class InterceptorsExtensions
    {
        /// <summary>
        /// Intercept the provider when a per-table DeleteMetadata command is being built.
        /// Use this to override the cleanup SQL globally; per-table customization is also
        /// available via <see cref="SetupTable.OnDeleteMetadataCreating(Action{DeleteMetadataCreatingArgs})"/>.
        /// </summary>
        public static Guid OnDeleteMetadataCreating(this BaseOrchestrator orchestrator, Action<DeleteMetadataCreatingArgs> action)
            => orchestrator.AddInterceptor(action);

        /// <inheritdoc cref="OnDeleteMetadataCreating(BaseOrchestrator, Action{DeleteMetadataCreatingArgs})"/>
        public static Guid OnDeleteMetadataCreating(this BaseOrchestrator orchestrator, Func<DeleteMetadataCreatingArgs, Task> action)
            => orchestrator.AddInterceptor(action);
    }
}
