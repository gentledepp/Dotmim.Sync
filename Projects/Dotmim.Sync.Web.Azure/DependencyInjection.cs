using System;
using Microsoft.Extensions.DependencyInjection;
using Wormhole.Sync.Storage;

namespace Wormhole.Sync.Web.Azure
{
    /// <summary>
    /// Extension methods for configuring Azure Blob Storage for Wormhole.Sync.
    /// </summary>
    public static class DependencyInjection
    {
        /// <summary>
        /// Adds Azure Blob Storage as the batch storage provider for Wormhole.Sync.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="connectionString">The Azure Storage connection string.</param>
        /// <param name="containerName">The name of the blob container to use for batch storage.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddDotmimSyncAzure(
            this IServiceCollection services,
            string connectionString,
            string containerName)
        {
            if (string.IsNullOrEmpty(connectionString))
                throw new ArgumentNullException(nameof(connectionString));
            if (string.IsNullOrEmpty(containerName))
                throw new ArgumentNullException(nameof(containerName));

            services.AddSingleton<IBatchStorage>(sp =>
                new AzureBlobBatchStorage(connectionString, containerName));

            return services;
        }

        /// <summary>
        /// Adds Azure Blob Storage as the batch storage provider for Wormhole.Sync using a factory function.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="factory">A factory function that creates the AzureBlobBatchStorage instance.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddDotmimSyncAzure(
            this IServiceCollection services,
            Func<IServiceProvider, AzureBlobBatchStorage> factory)
        {
            if (factory == null)
                throw new ArgumentNullException(nameof(factory));

            services.AddSingleton<IBatchStorage>(factory);

            return services;
        }

        /// <summary>
        /// Adds Azure Blob Storage as the batch storage provider for Wormhole.Sync using configuration.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="configure">An action to configure the Azure Blob Storage options.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddDotmimSyncAzure(
            this IServiceCollection services,
            Action<AzureBlobStorageOptions> configure)
        {
            if (configure == null)
                throw new ArgumentNullException(nameof(configure));

            var options = new AzureBlobStorageOptions();
            configure(options);

            if (string.IsNullOrEmpty(options.ConnectionString))
                throw new ArgumentException("ConnectionString must be configured.", nameof(configure));
            if (string.IsNullOrEmpty(options.ContainerName))
                throw new ArgumentException("ContainerName must be configured.", nameof(configure));

            services.AddSingleton<IBatchStorage>(sp =>
                new AzureBlobBatchStorage(options.ConnectionString, options.ContainerName));

            return services;
        }
    }

    /// <summary>
    /// Options for configuring Azure Blob Storage for Wormhole.Sync.
    /// </summary>
    public class AzureBlobStorageOptions
    {
        /// <summary>
        /// Gets or sets the Azure Storage connection string.
        /// </summary>
        public string ConnectionString { get; set; }

        /// <summary>
        /// Gets or sets the name of the blob container to use for batch storage.
        /// </summary>
        public string ContainerName { get; set; }
    }
}
