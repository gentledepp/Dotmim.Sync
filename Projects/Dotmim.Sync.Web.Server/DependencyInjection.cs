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
            options ??= new SyncOptions();
            setup = setup ?? throw new ArgumentNullException(nameof(setup));
            scopeName ??= SyncOptions.DefaultScopeName;

            // Create provider
            var provider = (CoreProvider)Activator.CreateInstance(providerType);
            provider.ConnectionString = connectionString;

            // Create orchestrator
            serviceCollection.AddScoped(sp => new WebServerAgent(provider, setup, options, webServerOptions, scopeName, identifier));

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
            options ??= new SyncOptions();
            setup = setup ?? throw new ArgumentNullException(nameof(setup));
            scopeName ??= SyncOptions.DefaultScopeName;

            serviceCollection.AddSingleton<IBatchCleanupService, BatchCleanupService>();

            // Register async batch creation services if enabled
            if (webServerOptions.EnableAsyncBatchCreation)
            {
                serviceCollection.AddSingleton<InMemoryBatchJobStore>();
                serviceCollection.AddSingleton<DefaultBatchCreationJobService>();
                serviceCollection.AddSingleton<IBatchCreationJobService>(sp =>
                    sp.GetRequiredService<DefaultBatchCreationJobService>());
                serviceCollection.AddSingleton<IHostedService>(sp =>
                    new BatchCreationWorkerService(
                        sp.GetRequiredService<DefaultBatchCreationJobService>(),
                        sp.GetRequiredService<ILogger<BatchCreationWorkerService>>(),
                        webServerOptions.AsyncBatchWorkerCount));
            }

            // Create orchestrator with async service if enabled
            serviceCollection.AddScoped(sp => new WebServerAgent(
                provider, setup, options, webServerOptions, scopeName, identifier,
                sp.GetRequiredService<IBatchCleanupService>(),
                webServerOptions.EnableAsyncBatchCreation ? sp.GetService<IBatchCreationJobService>() : null));

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
            options ??= new SyncOptions();
            setup = setup ?? throw new ArgumentNullException(nameof(setup));
            scopeName ??= SyncOptions.DefaultScopeName;

            var isRegistered = serviceCollection.Any(descriptor => descriptor.ServiceType == typeof(TCoreProvider));
            if (!isRegistered)
                serviceCollection.AddScoped<TCoreProvider>();

            serviceCollection.AddSingleton<IBatchCleanupService, BatchCleanupService>();

            // Register async batch creation services if enabled
            if (webServerOptions.EnableAsyncBatchCreation)
            {
                serviceCollection.AddSingleton<InMemoryBatchJobStore>();
                serviceCollection.AddSingleton<DefaultBatchCreationJobService>();
                serviceCollection.AddSingleton<IBatchCreationJobService>(sp =>
                    sp.GetRequiredService<DefaultBatchCreationJobService>());
                serviceCollection.AddSingleton<IHostedService>(sp =>
                    new BatchCreationWorkerService(
                        sp.GetRequiredService<DefaultBatchCreationJobService>(),
                        sp.GetRequiredService<ILogger<BatchCreationWorkerService>>(),
                        webServerOptions.AsyncBatchWorkerCount));
            }

            // Create orchestrator with async service if enabled
            serviceCollection.AddScoped(sp => new WebServerAgent(
                sp.GetRequiredService<TCoreProvider>(), setup, options, webServerOptions, scopeName, identifier,
                sp.GetRequiredService<IBatchCleanupService>(),
                webServerOptions.EnableAsyncBatchCreation ? sp.GetService<IBatchCreationJobService>() : null));

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
            options ??= new SyncOptions();
            setup = setup ?? throw new ArgumentNullException(nameof(setup));
            scopeName ??= SyncOptions.DefaultScopeName;

            var isRegistered = serviceCollection.Any(descriptor => descriptor.ServiceType == typeof(TCoreProvider) && descriptor.IsKeyedService && descriptor.ServiceKey == providerKey);
            if (!isRegistered)
                serviceCollection.AddScoped<TCoreProvider>();

            serviceCollection.AddSingleton<IBatchCleanupService, BatchCleanupService>();

            // Register async batch creation services if enabled
            if (webServerOptions.EnableAsyncBatchCreation)
            {
                serviceCollection.AddSingleton<InMemoryBatchJobStore>();
                serviceCollection.AddSingleton<DefaultBatchCreationJobService>();
                serviceCollection.AddSingleton<IBatchCreationJobService>(sp =>
                    sp.GetRequiredService<DefaultBatchCreationJobService>());
                serviceCollection.AddSingleton<IHostedService>(sp =>
                    new BatchCreationWorkerService(
                        sp.GetRequiredService<DefaultBatchCreationJobService>(),
                        sp.GetRequiredService<ILogger<BatchCreationWorkerService>>(),
                        webServerOptions.AsyncBatchWorkerCount));
            }

            // Create orchestrator with async service if enabled
            serviceCollection.AddScoped(sp => new WebServerAgent(
                sp.GetRequiredKeyedService<TCoreProvider>(providerKey), setup, options, webServerOptions, scopeName, identifier,
                sp.GetRequiredService<IBatchCleanupService>(),
                webServerOptions.EnableAsyncBatchCreation ? sp.GetService<IBatchCreationJobService>() : null));

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
            options ??= new SyncOptions();
            setup = setup ?? throw new ArgumentNullException(nameof(setup));
            scopeName ??= SyncOptions.DefaultScopeName;

            // Force enable async batch creation when using custom job service
            webServerOptions.EnableAsyncBatchCreation = true;

            serviceCollection.AddSingleton<IBatchCleanupService, BatchCleanupService>();

            // Register the custom job service
            var isJobServiceRegistered = serviceCollection.Any(d => d.ServiceType == typeof(IBatchCreationJobService));
            if (!isJobServiceRegistered)
                serviceCollection.AddSingleton<IBatchCreationJobService, TJobService>();

            // Create orchestrator with custom async service
            serviceCollection.AddScoped(sp => new WebServerAgent(
                provider, setup, options, webServerOptions, scopeName, identifier,
                sp.GetRequiredService<IBatchCleanupService>(),
                sp.GetRequiredService<IBatchCreationJobService>()));

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
            options ??= new SyncOptions();
            setup = setup ?? throw new ArgumentNullException(nameof(setup));
            scopeName ??= SyncOptions.DefaultScopeName;

            // Force enable async batch creation when using custom job service
            webServerOptions.EnableAsyncBatchCreation = true;

            var isProviderRegistered = serviceCollection.Any(descriptor => descriptor.ServiceType == typeof(TCoreProvider));
            if (!isProviderRegistered)
                serviceCollection.AddScoped<TCoreProvider>();

            serviceCollection.AddSingleton<IBatchCleanupService, BatchCleanupService>();

            // Register the custom job service
            var isJobServiceRegistered = serviceCollection.Any(d => d.ServiceType == typeof(IBatchCreationJobService));
            if (!isJobServiceRegistered)
                serviceCollection.AddSingleton<IBatchCreationJobService, TJobService>();

            // Create orchestrator with custom async service
            serviceCollection.AddScoped(sp => new WebServerAgent(
                sp.GetRequiredService<TCoreProvider>(), setup, options, webServerOptions, scopeName, identifier,
                sp.GetRequiredService<IBatchCleanupService>(),
                sp.GetRequiredService<IBatchCreationJobService>()));

            return serviceCollection;
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

    }
}