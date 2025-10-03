using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace Wormhole.Sync
{
    /// <summary>
    /// Interface for providers that need to perform actions after applying changes.
    /// This allows providers to implement custom logic such as marking rows as synced.
    /// </summary>
    public interface IAfterApplyChangesProvider
    {
        /// <summary>
        /// Called after successfully applying changes to a table.
        /// </summary>
        /// <param name="scopeInfo">The scope information.</param>
        /// <param name="context">The synchronization context.</param>
        /// <param name="syncTable">The table being synchronized.</param>
        /// <param name="connection">The database connection.</param>
        /// <param name="transaction">The database transaction.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        Task OnAfterApplyChangesAsync(
            ScopeInfo scopeInfo,
            SyncContext context,
            SyncTable syncTable,
            DbConnection connection,
            DbTransaction transaction,
            CancellationToken cancellationToken);
    }
}
