using Microsoft.Extensions.Caching.Hybrid;
using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Wormhole.Sync.Serialization;

namespace Wormhole.Sync
{
    /// <summary>
    /// Default implementation of IScopeProvisioningCache using HybridCache.
    /// Supports both in-memory and distributed caching with automatic serialization.
    /// Thread-safe and suitable for both single-server and multi-server scenarios.
    /// </summary>
    public class HybridScopeProvisioningCache : IScopeProvisioningCache
    {
        private readonly HybridCache hybridCache;
        private readonly HybridCacheEntryOptions cacheOptions;

        /// <summary>
        /// Initializes a new instance of the <see cref="HybridScopeProvisioningCache"/> class.
        /// </summary>
        /// <param name="hybridCache">The HybridCache instance to use for caching.</param>
        /// <param name="cacheDuration">Optional cache duration. If null, entries never expire (default: 1 hour).</param>
        public HybridScopeProvisioningCache(HybridCache hybridCache, TimeSpan? cacheDuration = null)
        {
            this.hybridCache = hybridCache ?? throw new ArgumentNullException(nameof(hybridCache));

            // Default to 1 hour if not specified
            var duration = cacheDuration ?? TimeSpan.FromHours(1);

            this.cacheOptions = new HybridCacheEntryOptions
            {
                Expiration = duration,
                LocalCacheExpiration = duration
            };
        }

        /// <inheritdoc/>
        public async Task<(bool Found, bool IsProvisioned)> TryGetProvisioningStateAsync(string connectionString, SyncSetup setup, ScopeInfoClientParameters clientParameters, string scopeName, CancellationToken cancellationToken = default)
        {
            var cacheKey = this.GenerateCacheKey(connectionString, setup, clientParameters, scopeName);

            try
            {
                var result = await this.hybridCache.GetOrCreateAsync<bool?>(
                    cacheKey,
                    _ => new ValueTask<bool?>((bool?)null), // Returns null if not found
                    cancellationToken: cancellationToken
                ).ConfigureAwait(false);

                if (result.HasValue)
                {
                    return (true, result.Value);
                }
            }
            catch (Exception x)
            {
                // If cache fails, fall back to no caching
            }

            return (false, false);
        }

        /// <inheritdoc/>
        public async Task SetProvisioningStateAsync(string connectionString, SyncSetup setup, ScopeInfoClientParameters clientParameters, string scopeName, bool isProvisioned, CancellationToken cancellationToken = default)
        {
            var cacheKey = this.GenerateCacheKey(connectionString, setup, clientParameters, scopeName);

            try
            {
                await this.hybridCache.SetAsync(cacheKey, isProvisioned, this.cacheOptions, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (Exception x)
            {
                // If cache fails, fall back to no caching
            }
        }

        /// <inheritdoc/>
        public async Task InvalidateProvisioningStateAsync(string connectionString, SyncSetup setup, ScopeInfoClientParameters clientParameters, string scopeName, CancellationToken cancellationToken = default)
        {
            var cacheKey = this.GenerateCacheKey(connectionString, setup, clientParameters, scopeName);

            try
            {
                await this.hybridCache.RemoveAsync(cacheKey, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception x)
            {
                // If cache fails, fall back to no caching
            }
        }

        /// <inheritdoc/>
        public Task InvalidateAllAsync(CancellationToken cancellationToken = default)
        {
            // HybridCache doesn't support clearing all entries directly
            // This is a limitation of distributed caching in general
            // Users can implement custom tracking if needed, or rely on expiration
            throw new NotSupportedException(
                "HybridScopeProvisioningCache does not support clearing all entries. " +
                "Cache entries will expire based on the configured duration. " +
                "To clear the cache, restart the application or implement a custom IScopeProvisioningCache " +
                "that tracks all cache keys.");
        }

        /// <summary>
        /// Generates a cache key from the connection string, setup, client parameters, and scope name.
        /// </summary>
        private string GenerateCacheKey(string connectionString, SyncSetup setup, ScopeInfoClientParameters clientParameters, string scopeName)
        {
            // Hash connection string for security (don't store plain connection strings in cache keys)
            var connHash = this.ComputeHash(connectionString ?? string.Empty);

            // Serialize and hash setup to detect configuration changes
            var setupJson = setup != null ? JsonSerializer.Serialize(setup) : string.Empty;
            var setupHash = this.ComputeHash(setupJson);

            // Serialize and hash client parameters to detect custom column changes
            var paramsJson = clientParameters != null && clientParameters.Count > 0
                ? JsonSerializer.Serialize(clientParameters)
                : string.Empty;
            var paramsHash = this.ComputeHash(paramsJson);

            // Combine all components into a single cache key
            return $"ScopeProvisioning:{connHash}:{setupHash}:{paramsHash}:{scopeName ?? string.Empty}";
        }

        /// <summary>
        /// Computes SHA256 hash of a string.
        /// </summary>
        private string ComputeHash(string input)
        {
            if (string.IsNullOrEmpty(input))
                return "empty";

            using var sha256 = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(input);
            var hashBytes = sha256.ComputeHash(bytes);

            // Convert to hex string
            var sb = new StringBuilder();
            foreach (var b in hashBytes)
            {
                sb.Append(b.ToString("x2"));
            }

            return sb.ToString();
        }
    }
}
