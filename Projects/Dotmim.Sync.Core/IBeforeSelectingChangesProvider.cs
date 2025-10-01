using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace Dotmim.Sync
{
    /// <summary>
    /// Interface for providers that need to perform actions before selecting changes.
    /// This allows providers to implement custom logic such as marking rows as syncing.
    /// </summary>
    public interface IBeforeSelectingChangesProvider
    {
        /// <summary>
        /// Called before selecting changes from a table.
        /// </summary>
        /// <param name="scopeInfo">The scope information.</param>
        /// <param name="context">The synchronization context.</param>
        /// <param name="syncTable">The table being synchronized.</param>
        /// <param name="connection">The database connection.</param>
        /// <param name="transaction">The database transaction.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        Task OnBeforeSelectingChangesAsync(
            ScopeInfo scopeInfo,
            SyncContext context,
            SyncTable syncTable,
            DbConnection connection,
            DbTransaction transaction,
            CancellationToken cancellationToken);
    }
}
