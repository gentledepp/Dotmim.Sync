using Wormhole.Sync.Serialization;
using System;
using System.Collections.ObjectModel;

namespace Wormhole.Sync.Web.Server
{
    /// <summary>
    /// Specifies options for the Web Server.
    /// </summary>
    public class WebServerOptions
    {
        /// <summary>
        /// Gets converters used by different clients.
        /// </summary>
        public Collection<IConverter> Converters { get; }

        /// <summary>
        /// Gets the serializer factories.
        /// </summary>
        public Collection<ISerializerFactory> SerializerFactories { get; }

        /// <summary>
        /// Gets or sets whether async batch creation is enabled for initial syncs.
        /// When enabled, batch creation for initial syncs (IsNewScope=true or SyncType=Reinitialize)
        /// is performed in a background worker, allowing the HTTP request to return immediately
        /// and poll for completion.
        /// Default: false
        /// </summary>
        public bool EnableAsyncBatchCreation { get; set; }

        /// <summary>
        /// Gets or sets the timeout for waiting on batch completion during async creation.
        /// If the batch is not ready within this time, the server returns HTTP 202 for client retry.
        /// Default: 30 seconds
        /// </summary>
        public TimeSpan AsyncBatchTimeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Gets or sets the polling interval for checking job status during async batch creation.
        /// Default: 500ms
        /// </summary>
        public TimeSpan AsyncBatchPollingInterval { get; set; } = TimeSpan.FromMilliseconds(500);

        /// <summary>
        /// Gets or sets the number of background workers for batch creation.
        /// Default: 2
        /// </summary>
        public int AsyncBatchWorkerCount { get; set; } = 2;

        /// <summary>
        /// Gets or sets the session cache storage mode.
        /// Default: AspNetSession (uses ASP.NET Session for backward compatibility).
        /// </summary>
        public SessionCacheStorageMode SessionStorageMode { get; set; } = SessionCacheStorageMode.AspNetSession;

        /// <summary>
        /// Gets or sets options for session cache store (expiration, etc.).
        /// Only applies when SessionStorageMode is MemoryCache or DistributedCache.
        /// </summary>
        public SessionCacheStoreOptions SessionStoreOptions { get; set; } = new SessionCacheStoreOptions();

        /// <summary>
        /// Initializes a new instance of the <see cref="WebServerOptions"/> class.
        /// Create a new instance of options with default values.
        /// </summary>
        public WebServerOptions()
            : base()
        {
            this.Converters = [];
            this.SerializerFactories =
            [
                SerializersFactory.JsonSerializerFactory,
            ];
        }
    }

    /// <summary>
    /// Specifies the storage mode for session cache data.
    /// </summary>
    public enum SessionCacheStorageMode
    {
        /// <summary>
        /// Use ASP.NET Session (default, backward compatible).
        /// Subject to session eviction issues.
        /// </summary>
        AspNetSession,

        /// <summary>
        /// Use IMemoryCache for single-server reliable storage.
        /// Recommended for tests and single-server deployments.
        /// </summary>
        MemoryCache,

        /// <summary>
        /// Use IDistributedCache for multi-server scale-out.
        /// Requires Redis, SQL Server, or other distributed cache.
        /// </summary>
        DistributedCache
    }
}