using System;
using System.Threading;
using System.Threading.Tasks;

namespace Wormhole.Sync
{
    /// <summary>
    /// Represents a cache for storing provisioning state to avoid repeated database existence checks.
    /// This interface allows custom caching strategies (in-memory, distributed, Redis, etc.).
    /// </summary>
    public interface IScopeProvisioningCache
    {
        /// <summary>
        /// Tries to get the provisioning state from cache asynchronously.
        /// </summary>
        /// <param name="connectionString">The connection string to the database.</param>
        /// <param name="setup">The sync setup configuration.</param>
        /// <param name="clientParameters">Optional custom scope info client parameters (for custom columns).</param>
        /// <param name="scopeName">The scope name.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A tuple with (Found, IsProvisioned) where Found indicates if the state was in cache, and IsProvisioned indicates the provisioning state.</returns>
        Task<(bool Found, bool IsProvisioned)> TryGetProvisioningStateAsync(string connectionString, SyncSetup setup, ScopeInfoClientParameters clientParameters, string scopeName, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sets the provisioning state in cache asynchronously.
        /// </summary>
        /// <param name="connectionString">The connection string to the database.</param>
        /// <param name="setup">The sync setup configuration.</param>
        /// <param name="clientParameters">Optional custom scope info client parameters (for custom columns).</param>
        /// <param name="scopeName">The scope name.</param>
        /// <param name="isProvisioned">True if the scope is provisioned, false otherwise.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task SetProvisioningStateAsync(string connectionString, SyncSetup setup, ScopeInfoClientParameters clientParameters, string scopeName, bool isProvisioned, CancellationToken cancellationToken = default);

        /// <summary>
        /// Invalidates the provisioning state for a specific scope in cache asynchronously.
        /// Call this when the scope is deprovisioned or modified.
        /// </summary>
        /// <param name="connectionString">The connection string to the database.</param>
        /// <param name="setup">The sync setup configuration.</param>
        /// <param name="clientParameters">Optional custom scope info client parameters (for custom columns).</param>
        /// <param name="scopeName">The scope name.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task InvalidateProvisioningStateAsync(string connectionString, SyncSetup setup, ScopeInfoClientParameters clientParameters, string scopeName, CancellationToken cancellationToken = default);

        /// <summary>
        /// Invalidates all cached provisioning states asynchronously.
        /// Call this to clear the entire cache.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task InvalidateAllAsync(CancellationToken cancellationToken = default);
    }
}
