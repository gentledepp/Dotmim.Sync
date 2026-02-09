using System;
using Hangfire;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Wormhole.Sync.Async;
using Wormhole.Sync.Storage;
using Wormhole.Sync.Web.Server.Async;

namespace Wormhole.Sync.Web.Hangfire
{
    /// <summary>
    /// Extension methods for configuring Hangfire-based batch job services for Wormhole.Sync.
    /// </summary>
    public static class DependencyInjection
    {
        /// <summary>
        /// Adds Hangfire-based batch job services for Wormhole.Sync.
        /// This enables scale-out scenarios where multiple server instances share job state
        /// and process batch creation jobs via Hangfire.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <returns>The service collection for chaining.</returns>
        /// <remarks>
        /// This method registers:
        /// - <see cref="DistributedBatchJobStore"/> as <see cref="IBatchJobStore"/> (requires IDistributedCache)
        /// - <see cref="HangfireBatchCreationJobService"/> as <see cref="IBatchCreationJobService"/>
        /// - <see cref="HangfireBatchCreationJob"/> for Hangfire job execution
        ///
        /// Prerequisites:
        /// - IDistributedCache must be registered (e.g., AddStackExchangeRedisCache, AddDistributedSqlServerCache)
        /// - Hangfire must be configured with AddHangfire() and UseHangfireServer()
        /// </remarks>
        public static IServiceCollection AddDotmimSyncHangfire<TCoreProvider>(this IServiceCollection services)
            where TCoreProvider : CoreProvider
        {
            return services.AddDotmimSyncHangfire<TCoreProvider>(null, null, null);
        }

        /// <summary>
        /// Adds Hangfire-based batch job services for Wormhole.Sync with custom options.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="configureJobStore">Optional action to configure the distributed batch job store options.</param>
        /// <param name="configureJobService">Optional action to configure the Hangfire job service options.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddDotmimSyncHangfire<TCoreProvider>(
            this IServiceCollection services,
            Action<DistributedBatchJobStoreOptions> configureJobStore,
            Action<HangfireBatchCreationJobServiceOptions> configureJobService)
            where TCoreProvider : CoreProvider
        {
            return services.AddDotmimSyncHangfire<TCoreProvider>(null, configureJobStore, configureJobService);
        }

        /// <summary>
        /// Adds Hangfire-based batch job services for Wormhole.Sync with custom options.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="options"></param>
        /// <param name="configureJobStore">Optional action to configure the distributed batch job store options.</param>
        /// <param name="configureJobService">Optional action to configure the Hangfire job service options.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddDotmimSyncHangfire<TCoreProvider>(
            this IServiceCollection services,
            SyncOptions options,
            Action<DistributedBatchJobStoreOptions> configureJobStore,
            Action<HangfireBatchCreationJobServiceOptions> configureJobService)
        where TCoreProvider: CoreProvider
        {
            var jobStoreOptions = new DistributedBatchJobStoreOptions();
            configureJobStore?.Invoke(jobStoreOptions);

            var jobServiceOptions = new HangfireBatchCreationJobServiceOptions();
            configureJobService?.Invoke(jobServiceOptions);

            // Register the distributed batch job store
            services.AddSingleton<IBatchJobStore>(sp =>
            {
                var cache = sp.GetRequiredService<IDistributedCache>();
                return new DistributedBatchJobStore(cache, jobStoreOptions);
            });

            // register dummy contextloggerprovider
            services.AddSingleton<IHangfireContextLoggerProvider<HangfireBatchCreationJob>, NullHangfireContextLoggerProvider<HangfireBatchCreationJob>>();

            // Register the Hangfire job executor
            services.AddTransient<HangfireBatchCreationJob>(sp =>
                new HangfireBatchCreationJob(sp.GetRequiredService<IBatchJobStore>(),
                    sp.GetRequiredService<IBatchCreationExecutor>(),
                    sp.GetRequiredService<ILogger<HangfireBatchCreationJob>>(),
                    sp.GetRequiredService<IHangfireContextLoggerProvider<HangfireBatchCreationJob>>()));

            // register job client
            services.TryAddTransient<IBackgroundJobClient, BackgroundJobClient>();

            services.AddTransient<IBatchCreationExecutor>((sp) => 
                new BatchCreationExecutor(options ?? sp.GetRequiredService<SyncOptions>(), 
                    sp.GetRequiredService<TCoreProvider>(),
                    sp.GetRequiredService<IBatchStorage>(),
                    sp.GetRequiredService<ILogger<BatchCreationExecutor>>()));

            // Register the Hangfire job service
            services.AddSingleton<IBatchCreationJobService>(sp =>
            {
                var backgroundJobClient = sp.GetRequiredService<IBackgroundJobClient>();
                var jobStore = sp.GetRequiredService<IBatchJobStore>();
                var logger = sp.GetRequiredService<ILogger<HangfireBatchCreationJobService>>();
                return new HangfireBatchCreationJobService(backgroundJobClient, jobStore, logger, jobServiceOptions);
            });

            return services;
        }

        /// <summary>
        /// Adds Hangfire-based batch job services for Wormhole.Sync with custom options.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="options"></param>
        /// <param name="configureJobStore">Optional action to configure the distributed batch job store options.</param>
        /// <param name="configureJobService">Optional action to configure the Hangfire job service options.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddDotmimSyncHangfire<TCoreProvider, TBatchCreationProvider>(
            this IServiceCollection services,
            SyncOptions options,
            Action<DistributedBatchJobStoreOptions> configureJobStore,
            Action<HangfireBatchCreationJobServiceOptions> configureJobService)
        where TCoreProvider : CoreProvider
        where TBatchCreationProvider : class, IBatchCreationExecutorProvider
        {
            var jobStoreOptions = new DistributedBatchJobStoreOptions();
            configureJobStore?.Invoke(jobStoreOptions);

            var jobServiceOptions = new HangfireBatchCreationJobServiceOptions();
            configureJobService?.Invoke(jobServiceOptions);

            // Register the distributed batch job stored
            services.AddSingleton<IBatchJobStore>(sp =>
            {
                var cache = sp.GetRequiredService<IDistributedCache>();
                return new DistributedBatchJobStore(cache, jobStoreOptions);
            });

            // register dummy contextloggerprovider
            services.AddSingleton<IHangfireContextLoggerProvider<HangfireBatchCreationJob>, NullHangfireContextLoggerProvider<HangfireBatchCreationJob>>();

            services.TryAddTransient<IBatchCreationExecutorProvider, TBatchCreationProvider>();

            // Register the Hangfire job executor
            services.AddTransient<HangfireBatchCreationJob>(sp =>
                new HangfireBatchCreationJob(sp.GetRequiredService<IBatchJobStore>(),
                    sp.GetRequiredService<IBatchCreationExecutorProvider>(),
                    sp.GetRequiredService<ILogger<HangfireBatchCreationJob>>(),
                    sp.GetRequiredService<IHangfireContextLoggerProvider<HangfireBatchCreationJob>>()));

            // register job client
            services.TryAddTransient<IBackgroundJobClient, BackgroundJobClient>();

            services.AddTransient<IBatchCreationExecutor>((sp) =>
                new BatchCreationExecutor(options ?? sp.GetRequiredService<SyncOptions>(),
                    sp.GetRequiredService<TCoreProvider>(),
                    sp.GetRequiredService<IBatchStorage>(),
                    sp.GetRequiredService<ILogger<BatchCreationExecutor>>()));

            // Register the Hangfire job service
            services.AddSingleton<IBatchCreationJobService>(sp =>
            {
                var backgroundJobClient = sp.GetRequiredService<IBackgroundJobClient>();
                var jobStore = sp.GetRequiredService<IBatchJobStore>();
                var logger = sp.GetRequiredService<ILogger<HangfireBatchCreationJobService>>();
                return new HangfireBatchCreationJobService(backgroundJobClient, jobStore, logger, jobServiceOptions);
            });

            return services;
        }


        /// <summary>
        /// Adds Hangfire-based batch job services for Wormhole.Sync with custom options.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="options">the sync options</param>
        /// <param name="provider">the provider to use</param>
        /// <param name="configureJobStore">Optional action to configure the distributed batch job store options.</param>
        /// <param name="configureJobService">Optional action to configure the Hangfire job service options.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddDotmimSyncHangfire(
            this IServiceCollection services,
            SyncOptions options,
            CoreProvider provider,
            Action<DistributedBatchJobStoreOptions> configureJobStore,
            Action<HangfireBatchCreationJobServiceOptions> configureJobService)
        {
            var jobStoreOptions = new DistributedBatchJobStoreOptions();
            configureJobStore?.Invoke(jobStoreOptions);

            var jobServiceOptions = new HangfireBatchCreationJobServiceOptions();
            configureJobService?.Invoke(jobServiceOptions);

            // Register the distributed batch job store
            services.TryAddSingleton<IBatchJobStore>(sp =>
            {
                var cache = sp.GetRequiredService<IDistributedCache>();
                return new DistributedBatchJobStore(cache, jobStoreOptions);
            });

            // Register the Hangfire job executor
            services.TryAddTransient<HangfireBatchCreationJob>();

            // register job client
            services.TryAddTransient<IBackgroundJobClient, BackgroundJobClient>();

            // batch creation executor
            services.TryAddTransient<IBatchCreationExecutor, BatchCreationExecutor>();

            services.TryAddTransient<IBatchCreationExecutor>((sp) =>
                new BatchCreationExecutor(options ?? sp.GetRequiredService<SyncOptions>(),
                    provider,
                    sp.GetRequiredService<IBatchStorage>(),
                    sp.GetRequiredService<ILogger<BatchCreationExecutor>>()));

            // Register the Hangfire job service
            services.TryAddSingleton<IBatchCreationJobService>(sp =>
            {
                var backgroundJobClient = sp.GetRequiredService<IBackgroundJobClient>();
                var jobStore = sp.GetRequiredService<IBatchJobStore>();
                var logger = sp.GetRequiredService<ILogger<HangfireBatchCreationJobService>>();
                return new HangfireBatchCreationJobService(backgroundJobClient, jobStore, logger, jobServiceOptions);
            });

            return services;
        }

    }
}
