#if NET48
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Web.SessionState;
using HttpContext = System.Web.HttpContextBase;
#else
using Microsoft.AspNetCore.Http;
#endif
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Wormhole.Sync.Web.Server
{
    /// <summary>
    /// Session cache store implementation using ASP.NET Session.
    /// This is the default implementation for backward compatibility.
    /// </summary>
    public class AspNetSessionCacheStore : ISessionCacheStore, IRequiresHttpContext
    {
        private HttpContext localContext;

#if !NET48
        
        private ISession GetSession()
        {
            var httpContext = this.localContext;
            if (httpContext == null)
                throw new InvalidOperationException("HttpContext is not available");

            return httpContext.Session;
        }
#else
        private HttpSessionStateBase GetSession()
        {
            var httpContext = this.localContext;
            if (httpContext == null)
                throw new InvalidOperationException("HttpContext.Current is not available");

            return httpContext.Session;
        }
#endif

        public async Task<SessionCache> GetAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            var session = GetSession();

#if NET48
            // In NET48, session is always loaded
            return session.Get<SessionCache>(sessionId);
#else
            await session.LoadAsync(cancellationToken).ConfigureAwait(false);
            return session.Get<SessionCache>(sessionId);
#endif
        }

        public async Task SetAsync(string sessionId, SessionCache cache, CancellationToken cancellationToken = default)
        {
            var session = GetSession();

#if NET48
            session.Set(sessionId, cache);
            // No need to commit in System.Web.SessionState - it's automatic
            await Task.CompletedTask.ConfigureAwait(false);
#else
            session.Set(sessionId, cache);
            await session.CommitAsync(cancellationToken).ConfigureAwait(false);
#endif
        }

        public async Task RemoveAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            var session = GetSession();

#if NET48
            session.Remove(sessionId);
            await Task.CompletedTask.ConfigureAwait(false);
#else
            session.Remove(sessionId);
            await session.CommitAsync(cancellationToken).ConfigureAwait(false);
#endif
        }

        public async Task<bool> ExistsAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            var cache = await GetAsync(sessionId, cancellationToken).ConfigureAwait(false);
            return cache != null;
        }

        public async Task<string> GetSessionIdAsync(string key, CancellationToken cancellationToken = default)
        {
            var session = GetSession();

#if NET48
            // In NET48, session is always loaded
            return session.GetString(key);
#else
            await session.LoadAsync(cancellationToken).ConfigureAwait(false);
            return session.GetString(key);
#endif
        }

        public async Task SetSessionIdAsync(string key, string sessionId, CancellationToken cancellationToken = default)
        {
            var session = GetSession();

#if NET48
            session.SetString(key, sessionId);
            await Task.CompletedTask.ConfigureAwait(false);
#else
            session.SetString(key, sessionId);
            await session.CommitAsync(cancellationToken).ConfigureAwait(false);
#endif
        }

        public void SetContext(HttpContext httpContext)
        {
            this.localContext = httpContext;
        }
    }
}
