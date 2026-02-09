using System;

namespace Wormhole.Sync.Web.Server
{
    /// <summary>
    /// Options for configuring session cache store behavior.
    /// </summary>
    public class SessionCacheStoreOptions
    {
        /// <summary>
        /// Sliding expiration for session cache entries.
        /// Default: 30 minutes.
        /// </summary>
        public TimeSpan SlidingExpiration { get; set; } = TimeSpan.FromMinutes(30);

        /// <summary>
        /// Absolute expiration for session cache entries.
        /// Default: 2 hours.
        /// </summary>
        public TimeSpan? AbsoluteExpiration { get; set; } = TimeSpan.FromHours(2);
    }
}
