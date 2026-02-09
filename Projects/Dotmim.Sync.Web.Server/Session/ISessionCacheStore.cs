using System.Threading;
using System.Threading.Tasks;

namespace Wormhole.Sync.Web.Server
{
    /// <summary>
    /// Abstraction for storing and retrieving SessionCache data.
    /// Implementations can use ASP.NET Session, IMemoryCache, or IDistributedCache.
    /// </summary>
    public interface ISessionCacheStore
    {
        /// <summary>
        /// Retrieves session cache for the given session ID.
        /// Returns null if not found.
        /// </summary>
        Task<SessionCache> GetAsync(string sessionId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Stores session cache for the given session ID.
        /// </summary>
        Task SetAsync(string sessionId, SessionCache cache, CancellationToken cancellationToken = default);

        /// <summary>
        /// Removes session cache for the given session ID.
        /// </summary>
        Task RemoveAsync(string sessionId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Checks if session cache exists for the given session ID.
        /// </summary>
        Task<bool> ExistsAsync(string sessionId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Optional: Validate session ID matches stored session ID.
        /// Used for session validation in WebServerAgent.
        /// </summary>
        Task<string> GetSessionIdAsync(string key, CancellationToken cancellationToken = default);

        /// <summary>
        /// Optional: Store session ID for validation.
        /// </summary>
        Task SetSessionIdAsync(string key, string sessionId, CancellationToken cancellationToken = default);
    }
}
