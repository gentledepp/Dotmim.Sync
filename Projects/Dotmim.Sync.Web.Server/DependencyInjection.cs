using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Wormhole.Sync;
using Wormhole.Sync.Async;
using Wormhole.Sync.Web.Client;
using Wormhole.Sync.Web.Server;
using Wormhole.Sync.Web.Server.Async;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
#if NET48
using System.Web;
using HttpContext = System.Web.HttpContextBase;
#else
using Microsoft.AspNetCore.Http;
#endif
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Wormhole.Sync.Storage;
using Wormhole.Sync.Web.Server.Errors;

[assembly: InternalsVisibleTo("Wormhole.Sync.Tests")]

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Dependency injection extensions for the Dotmim.Sync.Web.Server library.
    /// </summary>
    public static class DependencyInjection
    {
        /// <summary>
        /// Add the server provider (inherited from CoreProvider) and register in the DI a WebServerAgent.
        /// Use the WebServerAgent in your controller, by inject it.
        /// </summary>
        /// <param name="serviceCollection">services collections.</param>
        /// <param name="providerType">Provider inherited from CoreProvider (SqlSyncProvider, MySqlSyncProvider, OracleSyncProvider) Should have [CanBeServerProvider=true]. </param>
        /// <param name="connectionString">Provider connection string.</param>
        /// <param name="setup">Configuration server side. Adding at least tables to be synchronized.</param>
        /// <param name="options">Options, not shared with client, but only applied locally. Can be null.</param>
        /// <param name="webServerOptions">Specific web server options.</param>
        /// <param name="scopeName">Scope name.</param>
        /// <param name="identifier">Can be use to differentiate configuration where you are using the same provider in a multiple databases scenario.</param>
        [Obsolete("Use AddSyncServer(CoreProvider provider) instead, as it offers more possibilities to configure your provider, if needed.")]
        public static IServiceCollection AddSyncServer(this IServiceCollection serviceCollection, Type providerType,
                                                        string connectionString, SyncSetup setup = null, SyncOptions options = null,
                                                        WebServerOptions webServerOptions = null, string scopeName = null, string identifier = null)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentNullException(nameof(connectionString));

            webServerOptions ??= new WebServerOptions();

            setup = setup ?? throw new ArgumentNullException(nameof(setup));
            scopeName ??= SyncOptions.DefaultScopeName;

            // Create provider
            var provider = (CoreProvider)Activator.CreateInstance(providerType);
            provider.ConnectionString = connectionString;

            // Register setup store for hot-swap support
            serviceCollection.TryAddSingleton<SyncSetupStore>();

            // Create orchestrator — resolves current setup from store (supports hot-swap)
            serviceCollection.AddScoped(sp =>
            {
                var setupStore = sp.GetRequiredService<SyncSetupStore>();
                setupStore.Register(scopeName, identifier, setup);
                var currentSetup = setupStore.Resolve(scopeName, identifier);
                return new WebServerAgent(provider, currentSetup, ResolveSyncOptions(sp, options), webServerOptions, scopeName, identifier);
            });

            return serviceCollection;
        }

        /// <inheritdoc cref="AddSyncServer(IServiceCollection, CoreProvider, string[], SyncOptions, WebServerOptions, string, string)" />
        [Obsolete("Use AddSyncServer(CoreProvider provider) instead, as it offers you to configure your provider, if needed.")]
        public static IServiceCollection AddSyncServer<TProvider>(this IServiceCollection serviceCollection, string connectionString, SyncSetup setup = null, SyncOptions options = null, WebServerOptions webServerOptions = null, string identifier = null)
            where TProvider : CoreProvider, new()
            => serviceCollection.AddSyncServer(typeof(TProvider), connectionString, setup, options, webServerOptions, identifier);

        /// <inheritdoc cref="AddSyncServer(IServiceCollection, CoreProvider, string[], SyncOptions, WebServerOptions, string, string)" />
        [Obsolete("Use AddSyncServer(CoreProvider provider) instead, as it offers you to configure your provider, if needed.")]
        public static IServiceCollection AddSyncServer<TProvider>(this IServiceCollection serviceCollection, string connectionString, string[] tables = default, SyncOptions options = null, WebServerOptions webServerOptions = null, string identifier = null)
            where TProvider : CoreProvider, new()
            => serviceCollection.AddSyncServer(typeof(TProvider), connectionString, new SyncSetup(tables), options, webServerOptions, identifier);

        /// <summary>
        /// Add the server provider (inherited from CoreProvider) and register in the DI as a new WebServerAgent.
        /// In Your controller, inject a WebServerAgent to get your agent.
        /// </summary>
        /// <param name="serviceCollection">services collections.</param>
        /// <param name="provider">Provider inherited from CoreProvider (SqlSyncProvider, MySqlSyncProvider, OracleSyncProvider) Should have [CanBeServerProvider=true]. </param>
        /// <param name="setup">Configuration server side. Adding at least tables to be synchronized.</param>
        /// <param name="options">Options, not shared with client, but only applied locally. Can be null.</param>
        /// <param name="webServerOptions">Specific web server options.</param>
        /// <param name="scopeName">scope name.</param>
        /// <param name="identifier">Can be use to differentiate configuration where you are using the same provider in a multiple databases scenario.</param>
        public static IServiceCollection AddSyncServer(this IServiceCollection serviceCollection, CoreProvider provider,
                                                        SyncSetup setup = null, SyncOptions options = null,
                                                        WebServerOptions webServerOptions = null, string scopeName = null, string identifier = null)
        {
            Guard.ThrowIfNull(provider);

            webServerOptions ??= new WebServerOptions();

            setup = setup ?? throw new ArgumentNullException(nameof(setup));
            scopeName ??= SyncOptions.DefaultScopeName;

            serviceCollection.AddSingleton<IBatchCleanupService, BatchCleanupService>();

            serviceCollection.AddTransient<ISessionCacheStore, AspNetSessionCacheStore>();

            // Register session cache store based on configuration
            RegisterSessionCacheStore(serviceCollection, webServerOptions);

            // Register async batch creation services if enabled
            if (webServerOptions.EnableAsyncBatchCreation)
            {
                serviceCollection.AddSingleton<InMemoryBatchJobStore>();
                serviceCollection.AddSingleton<IBatchJobStore>(s => s.GetRequiredService<InMemoryBatchJobStore>());
                serviceCollection.TryAddTransient<IBatchStorage, LocalFileSystemBatchStorage>();
                serviceCollection.AddSingleton<DefaultBatchCreationJobService>();
                serviceCollection.AddSingleton<IBatchCreationJobService>(sp =>
                    sp.GetRequiredService<DefaultBatchCreationJobService>());

                // Register batch creation executor
                serviceCollection.TryAddTransient<IBatchCreationExecutor>(sp =>
                    new BatchCreationExecutor(
                        ResolveSyncOptions(sp, options),
                        provider,
                        sp.GetRequiredService<IBatchStorage>(),
                        sp.GetRequiredService<ILogger<BatchCreationExecutor>>()));

#if !NET48
                // Note: BatchCreationWorkerService is not available in NET48 because it requires
                // IHost infrastructure (IHostedService) which is not available in OWIN-based
                // WebApi2 applications. For NET48 environments, use Hangfire integration instead:
                // services.AddSyncServer<HangfireBatchCreationJobService>(...)
                serviceCollection.AddSingleton<IHostedService>(sp =>
                    new BatchCreationWorkerService(
                        sp.GetRequiredService<DefaultBatchCreationJobService>(),
                        sp.GetRequiredService<IBatchCreationExecutor>(),
                        sp.GetRequiredService<ILogger<BatchCreationWorkerService>>(),
                        webServerOptions.AsyncBatchWorkerCount));
#endif
            }

            // Register setup store for hot-swap support
            serviceCollection.TryAddSingleton<SyncSetupStore>();

            // Create orchestrator with async service if enabled — resolves current setup from store
            serviceCollection.AddScoped(sp =>
            {
                var setupStore = sp.GetRequiredService<SyncSetupStore>();
                setupStore.Register(scopeName, identifier, setup);
                var currentSetup = setupStore.Resolve(scopeName, identifier);
                return new WebServerAgent(
                    provider, currentSetup, ResolveSyncOptions(sp, options), webServerOptions, scopeName, identifier,
                    sp.GetRequiredService<IBatchCleanupService>(),
                    webServerOptions.EnableAsyncBatchCreation ? sp.GetService<IBatchCreationJobService>() : null,
                    sp.GetService<IBatchStorage>(),
                    sp.GetService<ISessionCacheStore>(),
                    sp.GetService<IErrorHandler>());
            });

            return serviceCollection;
        }

        /// <summary>
        /// Add the server provider (inherited from CoreProvider) and register in the DI as a new WebServerAgent.
        /// In Your controller, inject a WebServerAgent to get your agent.
        /// </summary>
        /// <param name="serviceCollection">services collections.</param>
        /// <param name="setup">Configuration server side. Adding at least tables to be synchronized.</param>
        /// <param name="options">Options, not shared with client, but only applied locally. Can be null.</param>
        /// <param name="webServerOptions">Specific web server options.</param>
        /// <param name="scopeName">scope name.</param>
        /// <param name="identifier">Can be use to differentiate configuration where you are using the same provider in a multiple databases scenario.</param>
        public static IServiceCollection AddSyncServer<TCoreProvider>(this IServiceCollection serviceCollection,
                                                        SyncSetup setup = null, SyncOptions options = null,
                                                        WebServerOptions webServerOptions = null, string scopeName = null, string identifier = null)
            where TCoreProvider : CoreProvider
        {
            webServerOptions ??= new WebServerOptions();

            setup = setup ?? throw new ArgumentNullException(nameof(setup));
            scopeName ??= SyncOptions.DefaultScopeName;

            var isRegistered = serviceCollection.Any(descriptor => descriptor.ServiceType == typeof(TCoreProvider));
            if (!isRegistered)
                serviceCollection.AddScoped<TCoreProvider>();

            serviceCollection.AddSingleton<IBatchCleanupService, BatchCleanupService>();

            // Register session cache store based on configuration
            RegisterSessionCacheStore(serviceCollection, webServerOptions);

            // Register async batch creation services if enabled
            if (webServerOptions.EnableAsyncBatchCreation)
            {
                serviceCollection.AddSingleton<InMemoryBatchJobStore>();
                serviceCollection.TryAddTransient<IBatchStorage, LocalFileSystemBatchStorage>();
                serviceCollection.AddSingleton<DefaultBatchCreationJobService>();
                serviceCollection.AddSingleton<IBatchCreationJobService>(sp =>
                    sp.GetRequiredService<DefaultBatchCreationJobService>());

                // Register batch creation executor
                serviceCollection.TryAddTransient<IBatchCreationExecutor>(sp =>
                    new BatchCreationExecutor(
                        ResolveSyncOptions(sp, options),
                        sp.GetRequiredService<TCoreProvider>(),
                        sp.GetRequiredService<IBatchStorage>(),
                        sp.GetRequiredService<ILogger<BatchCreationExecutor>>()));

#if !NET48
                // Note: BatchCreationWorkerService is not available in NET48 because it requires
                // IHost infrastructure (IHostedService) which is not available in OWIN-based
                // WebApi2 applications. For NET48 environments, use Hangfire integration instead:
                // services.AddSyncServer<HangfireBatchCreationJobService>(...)
                serviceCollection.AddSingleton<IHostedService>(sp =>
                    new BatchCreationWorkerService(
                        sp.GetRequiredService<DefaultBatchCreationJobService>(),
                        sp.GetRequiredService<IBatchCreationExecutor>(),
                        sp.GetRequiredService<ILogger<BatchCreationWorkerService>>(),
                        webServerOptions.AsyncBatchWorkerCount));
#endif
            }

            // Register setup store for hot-swap support
            serviceCollection.TryAddSingleton<SyncSetupStore>();

            // Create orchestrator with async service if enabled — resolves current setup from store
            serviceCollection.AddScoped(sp =>
            {
                var setupStore = sp.GetRequiredService<SyncSetupStore>();
                setupStore.Register(scopeName, identifier, setup);
                var currentSetup = setupStore.Resolve(scopeName, identifier);
                return new WebServerAgent(
                    sp.GetRequiredService<TCoreProvider>(), currentSetup, ResolveSyncOptions(sp, options), webServerOptions, scopeName, identifier,
                    sp.GetRequiredService<IBatchCleanupService>(),
                    webServerOptions.EnableAsyncBatchCreation ? sp.GetService<IBatchCreationJobService>() : null,
                    sp.GetService<IBatchStorage>(),
                    sp.GetService<ISessionCacheStore>(),
                    sp.GetService<IErrorHandler>());
            });

            return serviceCollection;
        }


        /// <summary>
        /// Add the server provider (inherited from CoreProvider) and register in the DI as a new WebServerAgent.
        /// In Your controller, inject a WebServerAgent to get your agent.
        /// </summary>
        /// <param name="serviceCollection">services collections.</param>
        /// <param name="providerKey">Allows to resolve a keyed coreprovider</param>
        /// <param name="setup">Configuration server side. Adding at least tables to be synchronized.</param>
        /// <param name="options">Options, not shared with client, but only applied locally. Can be null.</param>
        /// <param name="webServerOptions">Specific web server options.</param>
        /// <param name="scopeName">scope name.</param>
        /// <param name="identifier">Can be use to differentiate configuration where you are using the same provider in a multiple databases scenario.</param>
        public static IServiceCollection AddSyncServer<TCoreProvider>(this IServiceCollection serviceCollection, object providerKey,
                                                        SyncSetup setup = null, SyncOptions options = null,
                                                        WebServerOptions webServerOptions = null, string scopeName = null, string identifier = null)
            where TCoreProvider : CoreProvider
        {
            if (providerKey == null)
                throw new ArgumentNullException(nameof(providerKey));

            webServerOptions ??= new WebServerOptions();

            setup = setup ?? throw new ArgumentNullException(nameof(setup));
            scopeName ??= SyncOptions.DefaultScopeName;

            var isRegistered = serviceCollection.Any(descriptor => descriptor.ServiceType == typeof(TCoreProvider) && descriptor.IsKeyedService && descriptor.ServiceKey == providerKey);
            if (!isRegistered)
                serviceCollection.AddScoped<TCoreProvider>();

            serviceCollection.AddSingleton<IBatchCleanupService, BatchCleanupService>();

            // Register session cache store based on configuration
            RegisterSessionCacheStore(serviceCollection, webServerOptions);

            // Register async batch creation services if enabled
            if (webServerOptions.EnableAsyncBatchCreation)
            {
                serviceCollection.AddSingleton<InMemoryBatchJobStore>();
                serviceCollection.TryAddTransient<IBatchStorage, LocalFileSystemBatchStorage>();
                serviceCollection.AddSingleton<DefaultBatchCreationJobService>();
                serviceCollection.AddSingleton<IBatchCreationJobService>(sp =>
                    sp.GetRequiredService<DefaultBatchCreationJobService>());

                // Register batch creation executor
                serviceCollection.TryAddTransient<IBatchCreationExecutor>(sp =>
                    new BatchCreationExecutor(
                        ResolveSyncOptions(sp, options),
                        sp.GetRequiredService<TCoreProvider>(),
                        sp.GetRequiredService<IBatchStorage>(),
                        sp.GetRequiredService<ILogger<BatchCreationExecutor>>()));

#if !NET48
                // Note: BatchCreationWorkerService is not available in NET48 because it requires
                // IHost infrastructure (IHostedService) which is not available in OWIN-based
                // WebApi2 applications. For NET48 environments, use Hangfire integration instead:
                // services.AddSyncServer<HangfireBatchCreationJobService>(...)
                serviceCollection.AddHostedService(sp =>
                    new BatchCreationWorkerService(
                        sp.GetRequiredService<DefaultBatchCreationJobService>(),
                        sp.GetRequiredService<IBatchCreationExecutor>(),
                        sp.GetRequiredService<ILogger<BatchCreationWorkerService>>(),
                        webServerOptions.AsyncBatchWorkerCount));
#endif
            }

            // Register setup store for hot-swap support
            serviceCollection.TryAddSingleton<SyncSetupStore>();

            // Create orchestrator with async service if enabled — resolves current setup from store
            serviceCollection.AddScoped(sp =>
            {
                var setupStore = sp.GetRequiredService<SyncSetupStore>();
                setupStore.Register(scopeName, identifier, setup);
                var currentSetup = setupStore.Resolve(scopeName, identifier);
                return new WebServerAgent(
                    sp.GetRequiredKeyedService<TCoreProvider>(providerKey), currentSetup, ResolveSyncOptions(sp, options), webServerOptions, scopeName, identifier,
                    sp.GetRequiredService<IBatchCleanupService>(),
                    webServerOptions.EnableAsyncBatchCreation ? sp.GetService<IBatchCreationJobService>() : null,
                    sp.GetService<IBatchStorage>(),
                    sp.GetService<ISessionCacheStore>(),
                    sp.GetService<IErrorHandler>());
            });

            return serviceCollection;
        }

        /// <inheritdoc cref="AddSyncServer(IServiceCollection, CoreProvider, SyncSetup, SyncOptions, WebServerOptions, string, string)" />
        public static IServiceCollection AddSyncServer(this IServiceCollection serviceCollection, CoreProvider provider, string[] tables = default, SyncOptions options = null, WebServerOptions webServerOptions = null, string scopeName = null, string identifier = null)
                => serviceCollection.AddSyncServer(provider, new SyncSetup(tables), options, webServerOptions, scopeName, identifier);

        /// <summary>
        /// Add the server provider with a custom batch creation job service for scale-out deployments.
        /// Use this overload to plug in distributed job systems like Hangfire, Azure Functions, etc.
        /// </summary>
        /// <typeparam name="TJobService">The custom IBatchCreationJobService implementation type.</typeparam>
        /// <param name="serviceCollection">Services collection.</param>
        /// <param name="provider">Provider inherited from CoreProvider.</param>
        /// <param name="setup">Configuration server side.</param>
        /// <param name="options">Options, not shared with client, but only applied locally.</param>
        /// <param name="webServerOptions">Specific web server options. EnableAsyncBatchCreation will be automatically set to true.</param>
        /// <param name="scopeName">Scope name.</param>
        /// <param name="identifier">Optional identifier for multi-provider scenarios.</param>
        /// <remarks>
        /// When using this overload, the <see cref="WebServerOptions.EnableAsyncBatchCreation"/> is automatically
        /// enabled. The custom job service is responsible for:
        /// 1. Storing job parameters in a distributed store (Redis, SQL, etc.)
        /// 2. Enqueueing jobs to a distributed queue (Hangfire, Azure Service Bus, etc.)
        /// 3. Tracking job status across multiple server instances
        ///
        /// Example with Hangfire:
        /// <code>
        /// services.AddSyncServer&lt;MyHangfireBatchJobService&gt;(provider, setup, options);
        /// </code>
        /// </remarks>
        public static IServiceCollection AddSyncServer<TJobService>(
            this IServiceCollection serviceCollection,
            CoreProvider provider,
            SyncSetup setup = null,
            SyncOptions options = null,
            WebServerOptions webServerOptions = null,
            string scopeName = null,
            string identifier = null)
            where TJobService : class, IBatchCreationJobService
        {
            Guard.ThrowIfNull(provider);

            webServerOptions ??= new WebServerOptions();

            setup = setup ?? throw new ArgumentNullException(nameof(setup));
            scopeName ??= SyncOptions.DefaultScopeName;

            // Force enable async batch creation when using custom job service
            webServerOptions.EnableAsyncBatchCreation = true;

            serviceCollection.AddSingleton<IBatchCleanupService, BatchCleanupService>();

            // Register session cache store based on configuration
            RegisterSessionCacheStore(serviceCollection, webServerOptions);

            // Register the custom job service
            var isJobServiceRegistered = serviceCollection.Any(d => d.ServiceType == typeof(IBatchCreationJobService));
            if (!isJobServiceRegistered)
                serviceCollection.AddSingleton<IBatchCreationJobService, TJobService>();

            // Register setup store for hot-swap support
            serviceCollection.TryAddSingleton<SyncSetupStore>();

            // Create orchestrator with custom async service — resolves current setup from store
            serviceCollection.TryAddTransient<IBatchStorage, LocalFileSystemBatchStorage>();
            serviceCollection.AddScoped(sp =>
            {
                var setupStore = sp.GetRequiredService<SyncSetupStore>();
                setupStore.Register(scopeName, identifier, setup);
                var currentSetup = setupStore.Resolve(scopeName, identifier);
                return new WebServerAgent(
                    provider, currentSetup, ResolveSyncOptions(sp, options), webServerOptions, scopeName, identifier,
                    sp.GetRequiredService<IBatchCleanupService>(),
                    sp.GetRequiredService<IBatchCreationJobService>(),
                    sp.GetService<IBatchStorage>(),
                    sp.GetService<ISessionCacheStore>(),
                    sp.GetService<IErrorHandler>());
            });

            return serviceCollection;
        }

        /// <summary>
        /// Add the server provider with a custom batch creation job service for scale-out deployments.
        /// Use this overload to plug in distributed job systems like Hangfire, Azure Functions, etc.
        /// </summary>
        /// <typeparam name="TCoreProvider">The CoreProvider type to use.</typeparam>
        /// <typeparam name="TJobService">The custom IBatchCreationJobService implementation type.</typeparam>
        /// <param name="serviceCollection">Services collection.</param>
        /// <param name="setup">Configuration server side.</param>
        /// <param name="options">Options, not shared with client, but only applied locally.</param>
        /// <param name="webServerOptions">Specific web server options. EnableAsyncBatchCreation will be automatically set to true.</param>
        /// <param name="scopeName">Scope name.</param>
        /// <param name="identifier">Optional identifier for multi-provider scenarios.</param>
        public static IServiceCollection AddSyncServer<TCoreProvider, TJobService>(
            this IServiceCollection serviceCollection,
            SyncSetup setup = null,
            SyncOptions options = null,
            WebServerOptions webServerOptions = null,
            string scopeName = null,
            string identifier = null)
            where TCoreProvider : CoreProvider
            where TJobService : class, IBatchCreationJobService
        {
            webServerOptions ??= new WebServerOptions();

            setup = setup ?? throw new ArgumentNullException(nameof(setup));
            scopeName ??= SyncOptions.DefaultScopeName;

            // Force enable async batch creation when using custom job service
            webServerOptions.EnableAsyncBatchCreation = true;

            var isProviderRegistered = serviceCollection.Any(descriptor => descriptor.ServiceType == typeof(TCoreProvider));
            if (!isProviderRegistered)
                serviceCollection.AddScoped<TCoreProvider>();

            serviceCollection.AddSingleton<IBatchCleanupService, BatchCleanupService>();

            // Register session cache store based on configuration
            RegisterSessionCacheStore(serviceCollection, webServerOptions);

            // Register the custom job service
            var isJobServiceRegistered = serviceCollection.Any(d => d.ServiceType == typeof(IBatchCreationJobService));
            if (!isJobServiceRegistered)
                serviceCollection.AddSingleton<IBatchCreationJobService, TJobService>();

            // Register setup store for hot-swap support
            serviceCollection.TryAddSingleton<SyncSetupStore>();

            // Create orchestrator with custom async service — resolves current setup from store
            serviceCollection.TryAddTransient<IBatchStorage, LocalFileSystemBatchStorage>();
            serviceCollection.AddScoped(sp =>
            {
                var setupStore = sp.GetRequiredService<SyncSetupStore>();
                setupStore.Register(scopeName, identifier, setup);
                var currentSetup = setupStore.Resolve(scopeName, identifier);
                return new WebServerAgent(
                    sp.GetRequiredService<TCoreProvider>(), currentSetup, ResolveSyncOptions(sp, options), webServerOptions, scopeName, identifier,
                    sp.GetRequiredService<IBatchCleanupService>(),
                    sp.GetRequiredService<IBatchCreationJobService>(),
                    sp.GetService<IBatchStorage>(),
                    sp.GetService<ISessionCacheStore>(),
                    sp.GetService<IErrorHandler>());
            });

            return serviceCollection;
        }

        /// <summary>
        /// Add Wormhole.Sync server with Hangfire for async batch creation (single-server deployment).
        /// This method automatically configures MemoryCache for reliable session storage since Hangfire
        /// runs batch creation in background workers outside the HTTP request context.
        /// </summary>
        /// <typeparam name="TJobService">The Hangfire-based IBatchCreationJobService implementation.</typeparam>
        /// <param name="serviceCollection">Services collection.</param>
        /// <param name="provider">Provider inherited from CoreProvider.</param>
        /// <param name="setup">Configuration server side.</param>
        /// <param name="options">Options, not shared with client.</param>
        /// <param name="webServerOptions">Specific web server options. EnableAsyncBatchCreation and SessionStorageMode will be overridden.</param>
        /// <param name="scopeName">Scope name.</param>
        /// <param name="identifier">Optional identifier for multi-provider scenarios.</param>
        /// <remarks>
        /// This method is designed for single-server Hangfire deployments. It automatically:
        /// - Enables async batch creation
        /// - Configures MemoryCache for session storage (reliable, fast, single-server only)
        /// - Registers your Hangfire job service
        ///
        /// For multi-server deployments, use AddSyncServerWithHangfireScaleout instead.
        /// </remarks>
        public static IServiceCollection AddSyncServerWithHangfire<TJobService>(
            this IServiceCollection serviceCollection,
            CoreProvider provider,
            SyncSetup setup = null,
            SyncOptions options = null,
            WebServerOptions webServerOptions = null,
            string scopeName = null,
            string identifier = null)
            where TJobService : class, IBatchCreationJobService
        {
            Guard.ThrowIfNull(provider);

            webServerOptions ??= new WebServerOptions();

            setup = setup ?? throw new ArgumentNullException(nameof(setup));
            scopeName ??= SyncOptions.DefaultScopeName;

            // Force optimal configuration for single-server Hangfire
            webServerOptions.EnableAsyncBatchCreation = true;
            webServerOptions.SessionStorageMode = SessionCacheStorageMode.MemoryCache;

            serviceCollection.AddSingleton<IBatchCleanupService, BatchCleanupService>();

            // Register session cache store
            RegisterSessionCacheStore(serviceCollection, webServerOptions);

            // Register the Hangfire job service
            var isJobServiceRegistered = serviceCollection.Any(d => d.ServiceType == typeof(IBatchCreationJobService));
            if (!isJobServiceRegistered)
                serviceCollection.AddSingleton<IBatchCreationJobService, TJobService>();

            // Register setup store for hot-swap support
            serviceCollection.TryAddSingleton<SyncSetupStore>();

            // Register batch storage
            serviceCollection.TryAddTransient<IBatchStorage, LocalFileSystemBatchStorage>();

            // Create orchestrator — resolves current setup from store
            serviceCollection.AddScoped(sp =>
            {
                var setupStore = sp.GetRequiredService<SyncSetupStore>();
                setupStore.Register(scopeName, identifier, setup);
                var currentSetup = setupStore.Resolve(scopeName, identifier);
                return new WebServerAgent(
                    provider, currentSetup, ResolveSyncOptions(sp, options), webServerOptions, scopeName, identifier,
                    sp.GetRequiredService<IBatchCleanupService>(),
                    sp.GetRequiredService<IBatchCreationJobService>(),
                    sp.GetService<IBatchStorage>(),
                    sp.GetService<ISessionCacheStore>(),
                    sp.GetService<IErrorHandler>());
            });

            return serviceCollection;
        }

        /// <summary>
        /// Add Wormhole.Sync server with Hangfire for async batch creation (multi-server scale-out deployment).
        /// This method automatically configures DistributedCache for reliable session storage across multiple servers.
        /// You must register an IDistributedCache implementation (e.g., Redis) before calling this method.
        /// </summary>
        /// <typeparam name="TJobService">The Hangfire-based IBatchCreationJobService implementation.</typeparam>
        /// <param name="serviceCollection">Services collection.</param>
        /// <param name="provider">Provider inherited from CoreProvider.</param>
        /// <param name="setup">Configuration server side.</param>
        /// <param name="options">Options, not shared with client.</param>
        /// <param name="webServerOptions">Specific web server options. EnableAsyncBatchCreation and SessionStorageMode will be overridden.</param>
        /// <param name="scopeName">Scope name.</param>
        /// <param name="identifier">Optional identifier for multi-provider scenarios.</param>
        /// <remarks>
        /// This method is designed for multi-server Hangfire deployments. It automatically:
        /// - Enables async batch creation
        /// - Configures DistributedCache for session storage (scalable, shared across servers)
        /// - Registers your Hangfire job service
        ///
        /// IMPORTANT: You must register an IDistributedCache implementation before calling this method.
        /// Example with Redis:
        /// <code>
        /// services.AddStackExchangeRedisCache(options => {
        ///     options.Configuration = "localhost:6379";
        /// });
        /// services.AddSyncServerWithHangfireScaleout&lt;MyHangfireJobService&gt;(provider, setup);
        /// </code>
        ///
        /// For single-server deployments, use AddSyncServerWithHangfire instead (more performant).
        /// </remarks>
        public static IServiceCollection AddSyncServerWithHangfireScaleout<TJobService>(
            this IServiceCollection serviceCollection,
            CoreProvider provider,
            SyncSetup setup = null,
            SyncOptions options = null,
            WebServerOptions webServerOptions = null,
            string scopeName = null,
            string identifier = null)
            where TJobService : class, IBatchCreationJobService
        {
            Guard.ThrowIfNull(provider);

            webServerOptions ??= new WebServerOptions();

            setup = setup ?? throw new ArgumentNullException(nameof(setup));
            scopeName ??= SyncOptions.DefaultScopeName;

            // Force optimal configuration for multi-server Hangfire
            webServerOptions.EnableAsyncBatchCreation = true;
            webServerOptions.SessionStorageMode = SessionCacheStorageMode.DistributedCache;

            serviceCollection.AddSingleton<IBatchCleanupService, BatchCleanupService>();

            // Register session cache store (will use IDistributedCache registered by user)
            RegisterSessionCacheStore(serviceCollection, webServerOptions);

            // Register the Hangfire job service
            var isJobServiceRegistered = serviceCollection.Any(d => d.ServiceType == typeof(IBatchCreationJobService));
            if (!isJobServiceRegistered)
                serviceCollection.AddSingleton<IBatchCreationJobService, TJobService>();

            // Register setup store for hot-swap support
            serviceCollection.TryAddSingleton<SyncSetupStore>();

            // Register batch storage
            serviceCollection.TryAddTransient<IBatchStorage, LocalFileSystemBatchStorage>();

            // Create orchestrator — resolves current setup from store
            serviceCollection.AddScoped(sp =>
            {
                var setupStore = sp.GetRequiredService<SyncSetupStore>();
                setupStore.Register(scopeName, identifier, setup);
                var currentSetup = setupStore.Resolve(scopeName, identifier);
                return new WebServerAgent(
                    provider, currentSetup, ResolveSyncOptions(sp, options), webServerOptions, scopeName, identifier,
                    sp.GetRequiredService<IBatchCleanupService>(),
                    sp.GetRequiredService<IBatchCreationJobService>(),
                    sp.GetService<IBatchStorage>(),
                    sp.GetService<ISessionCacheStore>(),
                    sp.GetService<IErrorHandler>());
            });

            return serviceCollection;
        }

        // -----------------------------------------------------------------------
        // Migration-based overloads (no SyncSetup parameter — reads from IOptionsMonitor)
        // -----------------------------------------------------------------------

        /// <summary>
        /// Add the server provider and register a WebServerAgent that resolves its SyncSetup
        /// from <see cref="SyncMigrationOptions"/> configured via named options.
        /// Migrations are applied at startup via <see cref="ApplySyncMigrationsAsync(IServiceProvider, CancellationToken)"/>.
        /// </summary>
        /// <param name="serviceCollection">Services collection.</param>
        /// <param name="provider">Provider inherited from CoreProvider.</param>
        /// <param name="configureMigrations">Action to configure migrations inline.</param>
        /// <param name="options">Options, not shared with client.</param>
        /// <param name="webServerOptions">Specific web server options.</param>
        /// <param name="scopeName">Scope name.</param>
        /// <param name="identifier">Optional identifier for multi-provider scenarios.</param>
        public static IServiceCollection AddSyncServer(this IServiceCollection serviceCollection, CoreProvider provider,
                                                        Action<SyncMigrationOptions> configureMigrations,
                                                        SyncOptions options = null,
                                                        WebServerOptions webServerOptions = null, string scopeName = null, string identifier = null)
        {
            Guard.ThrowIfNull(provider);
            Guard.ThrowIfNull(configureMigrations);

            scopeName ??= SyncOptions.DefaultScopeName;

            // Register migration configuration for this scope
            serviceCollection.Configure<SyncMigrationOptions>(scopeName, configureMigrations);

            return serviceCollection.AddSyncServerWithMigrations(provider, options, webServerOptions, scopeName, identifier);
        }

        /// <summary>
        /// Add the server provider and register a WebServerAgent that resolves its SyncSetup
        /// from <see cref="SyncMigrationOptions"/> configured via named options.
        /// Configure migrations separately via <c>services.Configure&lt;SyncMigrationOptions&gt;(scopeName, ...)</c>.
        /// Migrations are applied at startup via <see cref="ApplySyncMigrationsAsync(IServiceProvider, CancellationToken)"/>.
        /// </summary>
        /// <param name="serviceCollection">Services collection.</param>
        /// <param name="provider">Provider inherited from CoreProvider.</param>
        /// <param name="options">Options, not shared with client.</param>
        /// <param name="webServerOptions">Specific web server options.</param>
        /// <param name="scopeName">Scope name.</param>
        /// <param name="identifier">Optional identifier for multi-provider scenarios.</param>
        public static IServiceCollection AddSyncServerWithMigrations(this IServiceCollection serviceCollection, CoreProvider provider,
                                                        SyncOptions options = null,
                                                        WebServerOptions webServerOptions = null, string scopeName = null, string identifier = null)
        {
            Guard.ThrowIfNull(provider);

            webServerOptions ??= new WebServerOptions();
            scopeName ??= SyncOptions.DefaultScopeName;

            serviceCollection.AddSingleton<IBatchCleanupService, BatchCleanupService>();

            serviceCollection.AddTransient<ISessionCacheStore, AspNetSessionCacheStore>();

            // Register session cache store based on configuration
            RegisterSessionCacheStore(serviceCollection, webServerOptions);

            // Register async batch creation services if enabled
            if (webServerOptions.EnableAsyncBatchCreation)
            {
                serviceCollection.AddSingleton<InMemoryBatchJobStore>();
                serviceCollection.AddSingleton<IBatchJobStore>(s => s.GetRequiredService<InMemoryBatchJobStore>());
                serviceCollection.TryAddTransient<IBatchStorage, LocalFileSystemBatchStorage>();
                serviceCollection.AddSingleton<DefaultBatchCreationJobService>();
                serviceCollection.AddSingleton<IBatchCreationJobService>(sp =>
                    sp.GetRequiredService<DefaultBatchCreationJobService>());

                // Register batch creation executor
                serviceCollection.TryAddTransient<IBatchCreationExecutor>(sp =>
                    new BatchCreationExecutor(
                        ResolveSyncOptions(sp, options),
                        provider,
                        sp.GetRequiredService<IBatchStorage>(),
                        sp.GetRequiredService<ILogger<BatchCreationExecutor>>()));

#if !NET48
                serviceCollection.AddSingleton<IHostedService>(sp =>
                    new BatchCreationWorkerService(
                        sp.GetRequiredService<DefaultBatchCreationJobService>(),
                        sp.GetRequiredService<IBatchCreationExecutor>(),
                        sp.GetRequiredService<ILogger<BatchCreationWorkerService>>(),
                        webServerOptions.AsyncBatchWorkerCount));
#endif
            }

            // Register setup store for hot-swap support
            serviceCollection.TryAddSingleton<SyncSetupStore>();

            // Register the scope registration marker for migration discovery
            serviceCollection.AddSingleton(new SyncScopeRegistration(scopeName, identifier, _ => provider,
               sp => ResolveSyncOptions(sp, options)));

            // Register migration infrastructure (idempotent via TryAdd)
            serviceCollection.TryAddSingleton<SyncMigrationService>();

            // Add Options infrastructure
            serviceCollection.AddOptions();

            // Create orchestrator — resolves current setup from store,
            // falling back to options if store not yet populated
            var capturedScopeName = scopeName;
            var capturedIdentifier = identifier;
            serviceCollection.AddScoped(sp =>
            {
                var setupStore = sp.GetRequiredService<SyncSetupStore>();
                var currentSetup = setupStore.Resolve(capturedScopeName, capturedIdentifier);

                // Fallback: read from options if store not yet populated
                // (e.g., before ApplySyncMigrationsAsync runs, or if it was skipped)
                if (currentSetup == null)
                {
                    var opts = sp.GetRequiredService<IOptionsMonitor<SyncMigrationOptions>>();
                    currentSetup = opts.Get(capturedScopeName).CurrentSetup;
                    if (currentSetup != null)
                        setupStore.Register(capturedScopeName, capturedIdentifier, currentSetup);
                }

                if (currentSetup == null)
                    throw new InvalidOperationException(
                        $"No SyncSetup found for scope '{capturedScopeName}'. " +
                        "Either configure migrations via SyncMigrationOptions or call ApplySyncMigrationsAsync at startup.");

                return new WebServerAgent(
                    provider, currentSetup,
                    ResolveSyncOptions(sp, options), 
                    webServerOptions, 
                    capturedScopeName, 
                    capturedIdentifier,
                    sp.GetRequiredService<IBatchCleanupService>(),
                    webServerOptions.EnableAsyncBatchCreation ? sp.GetService<IBatchCreationJobService>() : null,
                    sp.GetService<IBatchStorage>(),
                    sp.GetService<ISessionCacheStore>(),
                    sp.GetService<IErrorHandler>());
            });

            return serviceCollection;
        }

        /// <summary>
        /// Add the server provider (generic, DI-resolved) with migration-based configuration.
        /// </summary>
        public static IServiceCollection AddSyncServerWithMigrations<TCoreProvider>(this IServiceCollection serviceCollection,
                                                        Action<SyncMigrationOptions> configureMigrations,
                                                        SyncOptions options = null,
                                                        WebServerOptions webServerOptions = null, string scopeName = null, string identifier = null)
            where TCoreProvider : CoreProvider
        {
            Guard.ThrowIfNull(configureMigrations);

            webServerOptions ??= new WebServerOptions();
            scopeName ??= SyncOptions.DefaultScopeName;

            // Register migration configuration for this scope
            serviceCollection.Configure<SyncMigrationOptions>(scopeName, configureMigrations);

            var isRegistered = serviceCollection.Any(descriptor => descriptor.ServiceType == typeof(TCoreProvider));
            if (!isRegistered)
                serviceCollection.AddScoped<TCoreProvider>();

            serviceCollection.AddSingleton<IBatchCleanupService, BatchCleanupService>();

            // Register session cache store based on configuration
            RegisterSessionCacheStore(serviceCollection, webServerOptions);

            // Register async batch creation services if enabled
            if (webServerOptions.EnableAsyncBatchCreation)
            {
                serviceCollection.AddSingleton<InMemoryBatchJobStore>();
                serviceCollection.TryAddTransient<IBatchStorage, LocalFileSystemBatchStorage>();
                serviceCollection.AddSingleton<DefaultBatchCreationJobService>();
                serviceCollection.AddSingleton<IBatchCreationJobService>(sp =>
                    sp.GetRequiredService<DefaultBatchCreationJobService>());

                serviceCollection.TryAddTransient<IBatchCreationExecutor>(sp =>
                    new BatchCreationExecutor(
                        ResolveSyncOptions(sp, options),
                        sp.GetRequiredService<TCoreProvider>(),
                        sp.GetRequiredService<IBatchStorage>(),
                        sp.GetRequiredService<ILogger<BatchCreationExecutor>>()));

#if !NET48
                serviceCollection.AddSingleton<IHostedService>(sp =>
                    new BatchCreationWorkerService(
                        sp.GetRequiredService<DefaultBatchCreationJobService>(),
                        sp.GetRequiredService<IBatchCreationExecutor>(),
                        sp.GetRequiredService<ILogger<BatchCreationWorkerService>>(),
                        webServerOptions.AsyncBatchWorkerCount));
#endif
            }

            // Register setup store for hot-swap support
            serviceCollection.TryAddSingleton<SyncSetupStore>();

            // Register scope registration marker (factory-based for DI-resolved providers)
            serviceCollection.AddSingleton(new SyncScopeRegistration(scopeName, identifier,
                sp => sp.GetRequiredService<TCoreProvider>(),
                sp => ResolveSyncOptions(sp, options)));

            // Register migration infrastructure
            serviceCollection.TryAddSingleton<SyncMigrationService>();
            serviceCollection.AddOptions();

            var capturedScopeName = scopeName;
            var capturedIdentifier = identifier;
            serviceCollection.AddScoped(sp =>
            {
                var setupStore = sp.GetRequiredService<SyncSetupStore>();
                var currentSetup = setupStore.Resolve(capturedScopeName, capturedIdentifier);

                if (currentSetup == null)
                {
                    var opts = sp.GetRequiredService<IOptionsMonitor<SyncMigrationOptions>>();
                    currentSetup = opts.Get(capturedScopeName).CurrentSetup;
                    if (currentSetup != null)
                        setupStore.Register(capturedScopeName, capturedIdentifier, currentSetup);
                }

                if (currentSetup == null)
                    throw new InvalidOperationException(
                        $"No SyncSetup found for scope '{capturedScopeName}'. " +
                        "Either configure migrations via SyncMigrationOptions or call ApplySyncMigrationsAsync at startup.");

                return new WebServerAgent(
                    sp.GetRequiredService<TCoreProvider>(), 
                    currentSetup,
                    ResolveSyncOptions(sp, options),
                    webServerOptions, capturedScopeName, capturedIdentifier,
                    sp.GetRequiredService<IBatchCleanupService>(),
                    webServerOptions.EnableAsyncBatchCreation ? sp.GetService<IBatchCreationJobService>() : null,
                    sp.GetService<IBatchStorage>(),
                    sp.GetService<ISessionCacheStore>(),
                    sp.GetService<IErrorHandler>());
            });

            return serviceCollection;
        }

        // -----------------------------------------------------------------------
        // HasPendingSyncMigrationsAsync extension methods
        // -----------------------------------------------------------------------

#if !NET48
        /// <summary>
        /// Check whether any registered scope has pending migrations using the registered providers.
        /// </summary>
        public static Task<bool> HasPendingSyncMigrationsAsync(this IHost host, CancellationToken cancellationToken = default)
            => host.Services.HasPendingSyncMigrationsAsync(cancellationToken);

        /// <summary>
        /// Check whether any registered scope has pending migrations against an explicit provider (multi-tenant).
        /// </summary>
        public static Task<bool> HasPendingSyncMigrationsAsync(this IHost host, CoreProvider provider, CancellationToken cancellationToken = default)
            => host.Services.HasPendingSyncMigrationsAsync(provider, cancellationToken);
#endif

        /// <summary>
        /// Check whether any registered scope has pending migrations using the registered providers.
        /// </summary>
        public static async Task<bool> HasPendingSyncMigrationsAsync(this IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
        {
            var migrationService = serviceProvider.GetService<SyncMigrationService>();
            if (migrationService == null)
                return false;

            return await migrationService.HasPendingAsync(serviceProvider, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Check whether any registered scope has pending migrations against an explicit provider (multi-tenant).
        /// </summary>
        public static async Task<bool> HasPendingSyncMigrationsAsync(this IServiceProvider serviceProvider, CoreProvider provider, CancellationToken cancellationToken = default)
        {
            Guard.ThrowIfNull(provider);

            var migrationService = serviceProvider.GetService<SyncMigrationService>();
            if (migrationService == null)
                return false;

            return await migrationService.HasPendingAsync(serviceProvider, provider, cancellationToken).ConfigureAwait(false);
        }

        // -----------------------------------------------------------------------
        // ApplySyncMigrationsAsync extension methods
        // -----------------------------------------------------------------------

#if !NET48
        /// <summary>
        /// Apply pending schema migrations using the providers registered with AddSyncServer.
        /// Call this at application startup before handling requests.
        /// </summary>
        public static Task ApplySyncMigrationsAsync(this IHost host, CancellationToken cancellationToken = default)
            => host.Services.ApplySyncMigrationsAsync(cancellationToken);

        /// <summary>
        /// Apply pending schema migrations using an explicit provider (multi-tenant scenario).
        /// The migration list comes from <see cref="SyncMigrationOptions"/>, but targets a different database.
        /// </summary>
        public static Task ApplySyncMigrationsAsync(this IHost host, CoreProvider provider, CancellationToken cancellationToken = default)
            => host.Services.ApplySyncMigrationsAsync(provider, cancellationToken);

        /// <summary>
        /// Apply pending schema migrations using an explicit provider with progress reporting.
        /// </summary>
        public static Task ApplySyncMigrationsAsync(this IHost host, CoreProvider provider,
            IProgress<(string message, int percent)> progress, CancellationToken cancellationToken = default)
            => host.Services.ApplySyncMigrationsAsync(provider, progress, cancellationToken);

        /// <summary>
        /// Apply pending schema migrations using the registered providers with progress reporting.
        /// </summary>
        public static Task ApplySyncMigrationsAsync(this IHost host,
            IProgress<(string message, int percent)> progress, CancellationToken cancellationToken = default)
            => host.Services.ApplySyncMigrationsAsync(progress, cancellationToken);
#endif

        /// <summary>
        /// Apply pending schema migrations using the providers registered with AddSyncServer.
        /// Works on all target frameworks.
        /// </summary>
        public static async Task ApplySyncMigrationsAsync(this IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
        {
            var migrationService = serviceProvider.GetService<SyncMigrationService>();
            if (migrationService == null)
                return; // No migration-based scopes registered — no-op

            await migrationService.ApplyAsync(serviceProvider, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Apply pending schema migrations using an explicit provider (multi-tenant scenario).
        /// The migration list comes from <see cref="SyncMigrationOptions"/>, but targets a different database.
        /// </summary>
        public static async Task ApplySyncMigrationsAsync(this IServiceProvider serviceProvider, CoreProvider provider, CancellationToken cancellationToken = default)
        {
            Guard.ThrowIfNull(provider);

            var migrationService = serviceProvider.GetService<SyncMigrationService>();
            if (migrationService == null)
                return;

            await migrationService.ApplyAsync(serviceProvider, provider, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Apply pending schema migrations using the registered providers with progress reporting.
        /// Reports progress as <c>(string message, int percent)</c> — e.g. ("Applying migration 1 of 3: 20260220_v2 [scope 'DefaultScope']", 33).
        /// </summary>
        public static async Task ApplySyncMigrationsAsync(this IServiceProvider serviceProvider,
            IProgress<(string message, int percent)> progress, CancellationToken cancellationToken = default)
        {
            var migrationService = serviceProvider.GetService<SyncMigrationService>();
            if (migrationService == null)
            {
                progress?.Report(("No pending migrations", 100));
                return;
            }

            await migrationService.ApplyAsync(serviceProvider, null, progress, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Apply pending schema migrations using an explicit provider with progress reporting.
        /// Reports progress as <c>(string message, int percent)</c> — e.g. ("Applying migration 1 of 3: 20260220_v2 [scope 'DefaultScope']", 33).
        /// </summary>
        public static async Task ApplySyncMigrationsAsync(this IServiceProvider serviceProvider, CoreProvider provider,
            IProgress<(string message, int percent)> progress, CancellationToken cancellationToken = default)
        {
            Guard.ThrowIfNull(provider);

            var migrationService = serviceProvider.GetService<SyncMigrationService>();
            if (migrationService == null)
            {
                progress?.Report(("No pending migrations", 100));
                return;
            }

            await migrationService.ApplyAsync(serviceProvider, provider, progress, cancellationToken).ConfigureAwait(false);
        }

#if !NET48
        /// <inheritdoc cref="WebServerAgent.WriteHelloAsync(HttpContext, CancellationToken)"/>
        public static Task WriteHelloAsync(this HttpContext context, WebServerAgent webServerAgent, CancellationToken cancellationToken = default)
            => webServerAgent.WriteHelloAsync(context, cancellationToken);

        /// <inheritdoc cref="WebServerAgent.WriteHelloAsync(HttpContext, IEnumerable{WebServerAgent}, CancellationToken)"/>
        public static Task WriteHelloAsync(this HttpContext context, IEnumerable<WebServerAgent> webServerAgents, CancellationToken cancellationToken = default)
            => WebServerAgent.WriteHelloAsync(context, webServerAgents, cancellationToken);
#endif
        /// <summary>
        /// Get Scope Name sent by the client.
        /// </summary>
        public static string GetScopeName(this HttpContext httpContext) => WebServerAgent.TryGetHeaderValue(httpContext.Request.Headers, "dotmim-sync-scope-name", out var val) ? val : null;

        /// <summary>
        /// Get the DMS version used by the Client.
        /// </summary>
        public static string GetVersion(this HttpContext httpContext) => WebServerAgent.TryGetHeaderValue(httpContext.Request.Headers, "dotmim-sync-version", out var val) ? val : null;

        /// <summary>
        /// Get Scope Name sent by the client.
        /// </summary>
        public static Guid? GetClientScopeId(this HttpContext httpContext) => WebServerAgent.TryGetHeaderValue(httpContext.Request.Headers, "dotmim-sync-scope-id", out var val) ? string.IsNullOrEmpty(val) ? null : new Guid(val) : null;

        /// <summary>
        /// Get the current client session id.
        /// </summary>
        public static string GetClientSessionId(this HttpContext httpContext) => WebServerAgent.TryGetHeaderValue(httpContext.Request.Headers, "dotmim-sync-session-id", out var val) ? val : null;

        /// <summary>
        /// Get the current Step.
        /// </summary>
        public static HttpStep GetCurrentStep(this HttpContext httpContext) => WebServerAgent.TryGetHeaderValue(httpContext.Request.Headers, "dotmim-sync-step", out var val) ? string.IsNullOrEmpty(val) ? HttpStep.None : (HttpStep)SyncTypeConverter.TryConvertTo<int>(val) : HttpStep.None;

        /// <summary>
        /// Get the identifier that can be used in multi sync providers.
        /// </summary>
        public static string GetIdentifier(this HttpContext httpContext) => WebServerAgent.TryGetHeaderValue(httpContext.Request.Headers, "dotmim-sync-identifier", out var val) ? val : null;

        /// <summary>
        /// Resolve SyncOptions using the following precedence:
        /// 1. Explicitly passed instance (if not null)
        /// 2. Direct singleton registration (backwards compatibility with services.AddSingleton&lt;SyncOptions&gt;)
        /// 3. IOptions&lt;SyncOptions&gt; pattern (services.Configure&lt;SyncOptions&gt;)
        /// 4. Default SyncOptions
        /// </summary>
        internal static SyncOptions ResolveSyncOptions(IServiceProvider sp, SyncOptions explicitOptions)
        {
            if (explicitOptions != null)
                return explicitOptions;

            // Try direct singleton registration (backwards compat)
            var direct = sp.GetService<SyncOptions>();
            if (direct != null)
                return direct;

            // Try IOptions<SyncOptions> pattern
            var ioptions = sp.GetService<IOptions<SyncOptions>>();
            if (ioptions != null)
                return ioptions.Value;

            return new SyncOptions();
        }

        /// <summary>
        /// Register session cache store based on configuration.
        /// </summary>
        private static void RegisterSessionCacheStore(IServiceCollection serviceCollection, WebServerOptions webServerOptions)
        {
            switch (webServerOptions.SessionStorageMode)
            {
                case SessionCacheStorageMode.AspNetSession:
                    // Default - use ASP.NET Session via AspNetSessionCacheStore
#if NET6_0_OR_GREATER
                    serviceCollection.TryAddSingleton<IHttpContextAccessor, HttpContextAccessor>();
                    serviceCollection.AddTransient<ISessionCacheStore, AspNetSessionCacheStore>();
#elif NETSTANDARD2_0
                    // For netstandard2.0, IHttpContextAccessor should be registered by the consuming application
                    serviceCollection.AddTransient<ISessionCacheStore, AspNetSessionCacheStore>();
#else
                    // NET48
                    serviceCollection.AddTransient<ISessionCacheStore, AspNetSessionCacheStore>();
#endif
                    break;

                case SessionCacheStorageMode.MemoryCache:
                    // Use IMemoryCache (requires AddMemoryCache)
                    serviceCollection.AddMemoryCache();
                    serviceCollection.AddSingleton(webServerOptions.SessionStoreOptions);
                    serviceCollection.AddSingleton<ISessionCacheStore, MemoryCacheSessionStore>();
                    break;

                case SessionCacheStorageMode.DistributedCache:
                    // Use IDistributedCache (must be registered by user)
                    // Example: services.AddStackExchangeRedisCache(...)
                    serviceCollection.AddSingleton(webServerOptions.SessionStoreOptions);
                    serviceCollection.AddSingleton<ISessionCacheStore, DistributedCacheSessionStore>();
                    break;
            }
        }

    }
}