using Microsoft.Extensions.Caching.Memory;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Wormhole.Sync.Web.Server
{
    /// <summary>
    /// Session cache store implementation using IMemoryCache.
    /// Recommended for single-server deployments and testing scenarios.
    /// </summary>
    public class MemoryCacheSessionStore : ISessionCacheStore
    {
        private readonly IMemoryCache cache;
        private readonly SessionCacheStoreOptions options;
        private readonly ConcurrentDictionary<string, string> sessionIdStore;

        public MemoryCacheSessionStore(IMemoryCache cache, SessionCacheStoreOptions options = null)
        {
            this.cache = cache ?? throw new ArgumentNullException(nameof(cache));
            this.options = options ?? new SessionCacheStoreOptions();
            this.sessionIdStore = new ConcurrentDictionary<string, string>();
        }

        public Task<SessionCache> GetAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            var sessionCache = cache.Get<SessionCache>(sessionId);
            return Task.FromResult(sessionCache);
        }

        public Task SetAsync(string sessionId, SessionCache sessionCache, CancellationToken cancellationToken = default)
        {
            var entryOptions = new MemoryCacheEntryOptions()
                .SetSlidingExpiration(options.SlidingExpiration);

            if (options.AbsoluteExpiration.HasValue)
                entryOptions.SetAbsoluteExpiration(options.AbsoluteExpiration.Value);

            cache.Set(sessionId, sessionCache, entryOptions);
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            cache.Remove(sessionId);
            return Task.CompletedTask;
        }

        public Task<bool> ExistsAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            var exists = cache.TryGetValue(sessionId, out _);
            return Task.FromResult(exists);
        }

        public Task<string> GetSessionIdAsync(string key, CancellationToken cancellationToken = default)
        {
            sessionIdStore.TryGetValue(key, out var sessionId);
            return Task.FromResult(sessionId);
        }

        public Task SetSessionIdAsync(string key, string sessionId, CancellationToken cancellationToken = default)
        {
            sessionIdStore[key] = sessionId;
            return Task.CompletedTask;
        }
    }
}
