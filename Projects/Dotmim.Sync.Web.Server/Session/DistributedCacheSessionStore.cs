using Wormhole.Sync.Serialization;
using Microsoft.Extensions.Caching.Distributed;
using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Wormhole.Sync.Web.Server
{
    /// <summary>
    /// Session cache store implementation using IDistributedCache.
    /// Recommended for multi-server scale-out deployments.
    /// Supports Redis, SQL Server, NCache, and other distributed cache providers.
    /// </summary>
    public class DistributedCacheSessionStore : ISessionCacheStore
    {
        private readonly IDistributedCache cache;
        private readonly SessionCacheStoreOptions options;
        private readonly ISerializer serializer;

        public DistributedCacheSessionStore(IDistributedCache cache, SessionCacheStoreOptions options = null)
        {
            this.cache = cache ?? throw new ArgumentNullException(nameof(cache));
            this.options = options ?? new SessionCacheStoreOptions();
            this.serializer = SerializersFactory.JsonSerializerFactory.GetSerializer();
        }

        public async Task<SessionCache> GetAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            var bytes = await cache.GetAsync(sessionId, cancellationToken).ConfigureAwait(false);
            if (bytes == null || bytes.Length == 0)
                return null;

            var json = Encoding.UTF8.GetString(bytes);
            return serializer.Deserialize<SessionCache>(json);
        }

        public async Task SetAsync(string sessionId, SessionCache sessionCache, CancellationToken cancellationToken = default)
        {
            var bytes = serializer.Serialize(sessionCache);

            var entryOptions = new DistributedCacheEntryOptions
            {
                SlidingExpiration = options.SlidingExpiration,
                AbsoluteExpirationRelativeToNow = options.AbsoluteExpiration
            };

            await cache.SetAsync(sessionId, bytes, entryOptions, cancellationToken).ConfigureAwait(false);
        }

        public async Task RemoveAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            await cache.RemoveAsync(sessionId, cancellationToken).ConfigureAwait(false);
        }

        public async Task<bool> ExistsAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            var bytes = await cache.GetAsync(sessionId, cancellationToken).ConfigureAwait(false);
            return bytes != null && bytes.Length > 0;
        }

        public async Task<string> GetSessionIdAsync(string key, CancellationToken cancellationToken = default)
        {
            var bytes = await cache.GetAsync($"sessionid_{key}", cancellationToken).ConfigureAwait(false);
            if (bytes == null || bytes.Length == 0)
                return null;

            return Encoding.UTF8.GetString(bytes);
        }

        public async Task SetSessionIdAsync(string key, string sessionId, CancellationToken cancellationToken = default)
        {
            var bytes = Encoding.UTF8.GetBytes(sessionId);

            var entryOptions = new DistributedCacheEntryOptions
            {
                SlidingExpiration = options.SlidingExpiration,
                AbsoluteExpirationRelativeToNow = options.AbsoluteExpiration
            };

            await cache.SetAsync($"sessionid_{key}", bytes, entryOptions, cancellationToken).ConfigureAwait(false);
        }
    }
}
