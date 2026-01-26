using System;
using Hangfire;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Wormhole.Sync.Async;
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
        public static IServiceCollection AddDotmimSyncHangfire(this IServiceCollection services)
        {
            return services.AddDotmimSyncHangfire(null, null);
        }

        /// <summary>
        /// Adds Hangfire-based batch job services for Wormhole.Sync with custom options.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="configureJobStore">Optional action to configure the distributed batch job store options.</param>
        /// <param name="configureJobService">Optional action to configure the Hangfire job service options.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddDotmimSyncHangfire(
            this IServiceCollection services,
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


            services.TryAddTransient<IBatchCreationExecutor, BatchCreationExecutor>();

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

        /// <summary>
        /// Adds only the distributed batch job store without the Hangfire job service.
        /// Use this if you want to use a different job execution mechanism.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="configure">Optional action to configure the distributed batch job store options.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddDistributedBatchJobStore(
            this IServiceCollection services,
            Action<DistributedBatchJobStoreOptions> configure = null)
        {
            var options = new DistributedBatchJobStoreOptions();
            configure?.Invoke(options);

            services.TryAddSingleton<IBatchJobStore>(sp =>
            {
                var cache = sp.GetRequiredService<IDistributedCache>();
                return new DistributedBatchJobStore(cache, options);
            });

            return services;
        }

        /// <summary>
        /// Adds only the Hangfire batch creation job service.
        /// Use this if you have already registered IBatchJobStore separately.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="configure">Optional action to configure the Hangfire job service options.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddHangfireBatchCreationJobService(
            this IServiceCollection services,
            Action<HangfireBatchCreationJobServiceOptions> configure = null)
        {
            var options = new HangfireBatchCreationJobServiceOptions();
            configure?.Invoke(options);

            // Register the Hangfire job executor
            services.TryAddTransient<HangfireBatchCreationJob>();

            // Register the Hangfire job service
            services.TryAddSingleton<IBatchCreationJobService>(sp =>
            {
                var backgroundJobClient = sp.GetRequiredService<IBackgroundJobClient>();
                var jobStore = sp.GetRequiredService<IBatchJobStore>();
                var logger = sp.GetRequiredService<ILogger<HangfireBatchCreationJobService>>();
                return new HangfireBatchCreationJobService(backgroundJobClient, jobStore, logger, options);
            });

            return services;
        }
    }
}
