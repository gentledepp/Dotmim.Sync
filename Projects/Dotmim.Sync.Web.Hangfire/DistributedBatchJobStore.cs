using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Wormhole.Sync.Async;
using Wormhole.Sync.Serialization;

namespace Wormhole.Sync.Web.Hangfire
{
    /// <summary>
    /// Distributed batch job store implementation using IDistributedCache.
    /// Suitable for scale-out deployments where multiple server instances need to share job state.
    /// Works with any IDistributedCache implementation (Redis, SQL Server, NCache, etc.).
    /// </summary>
    public class DistributedBatchJobStore : IBatchJobStore
    {
        private readonly IDistributedCache cache;
        private readonly DistributedBatchJobStoreOptions options;
        private readonly string statusKeyPrefix;
        private readonly string parametersKeyPrefix;
        private readonly string jobIndexKey;

        /// <summary>
        /// Initializes a new instance of the <see cref="DistributedBatchJobStore"/> class.
        /// </summary>
        /// <param name="cache">The distributed cache implementation.</param>
        /// <param name="options">The configuration options.</param>
        public DistributedBatchJobStore(IDistributedCache cache, DistributedBatchJobStoreOptions options = null)
        {
            this.cache = cache ?? throw new ArgumentNullException(nameof(cache));
            this.options = options ?? new DistributedBatchJobStoreOptions();

            this.statusKeyPrefix = $"{this.options.KeyPrefix}:status:";
            this.parametersKeyPrefix = $"{this.options.KeyPrefix}:params:";
            this.jobIndexKey = $"{this.options.KeyPrefix}:jobs";
        }

        /// <inheritdoc />
        public async Task SetParametersAsync(string jobId, BatchCreationJobParameters jobParameters, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(jobId))
                throw new ArgumentNullException(nameof(jobId));

            var key = this.parametersKeyPrefix + jobId;
            var json = JsonSerializer.Serialize(jobParameters, GetJsonOptions());

            var cacheOptions = new DistributedCacheEntryOptions
            {
                SlidingExpiration = this.options.SlidingExpiration,
                AbsoluteExpirationRelativeToNow = this.options.AbsoluteExpiration
            };

            await this.cache.SetStringAsync(key, json, cacheOptions, cancellationToken).ConfigureAwait(false);
            await this.AddToJobIndexAsync(jobId, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<BatchCreationJobParameters> GetParametersAsync(string jobId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(jobId))
                return null;

            var key = this.parametersKeyPrefix + jobId;
            var json = await this.cache.GetStringAsync(key, cancellationToken).ConfigureAwait(false);

            if (string.IsNullOrEmpty(json))
                return null;

            var parameters = JsonSerializer.Deserialize<BatchCreationJobParameters>(json, GetJsonOptions());

            parameters?.ServerScopeInfo?.Schema?.EnsureSchema();

            return parameters;
        }

        /// <inheritdoc />
        public async Task SetStatusAsync(string jobId, BatchCreationJobStatus status, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(jobId))
                throw new ArgumentNullException(nameof(jobId));

            var key = this.statusKeyPrefix + jobId;
            var json = JsonSerializer.Serialize(status, GetJsonOptions());

            var cacheOptions = new DistributedCacheEntryOptions
            {
                SlidingExpiration = this.options.SlidingExpiration,
                AbsoluteExpirationRelativeToNow = this.options.AbsoluteExpiration
            };

            await this.cache.SetStringAsync(key, json, cacheOptions, cancellationToken).ConfigureAwait(false);
            await this.AddToJobIndexAsync(jobId, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<BatchCreationJobStatus> GetStatusAsync(string jobId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(jobId))
                return null;

            var key = this.statusKeyPrefix + jobId;
            var json = await this.cache.GetStringAsync(key, cancellationToken).ConfigureAwait(false);

            if (string.IsNullOrEmpty(json))
                return null;

            return JsonSerializer.Deserialize<BatchCreationJobStatus>(json, GetJsonOptions());
        }

        /// <inheritdoc />
        public async Task RemoveJobAsync(string jobId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(jobId))
                return;

            var statusKey = this.statusKeyPrefix + jobId;
            var paramsKey = this.parametersKeyPrefix + jobId;

            await this.cache.RemoveAsync(statusKey, cancellationToken).ConfigureAwait(false);
            await this.cache.RemoveAsync(paramsKey, cancellationToken).ConfigureAwait(false);
            await this.RemoveFromJobIndexAsync(jobId, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<IEnumerable<string>> GetExpiredJobIdsAsync(TimeSpan maxAge, CancellationToken cancellationToken = default)
        {
            var jobIds = await this.GetAllJobIdsAsync(cancellationToken).ConfigureAwait(false);
            var expiredJobs = new List<string>();
            var cutoff = DateTime.UtcNow - maxAge;

            foreach (var jobId in jobIds)
            {
                var status = await this.GetStatusAsync(jobId, cancellationToken).ConfigureAwait(false);
                if (status != null &&
                    status.EnqueuedAt < cutoff &&
                    (status.State == BatchCreationJobState.Completed ||
                     status.State == BatchCreationJobState.Failed ||
                     status.State == BatchCreationJobState.Cancelled))
                {
                    expiredJobs.Add(jobId);
                }
            }

            return expiredJobs;
        }

        /// <inheritdoc />
        public async Task<int> GetCountAsync(CancellationToken cancellationToken = default)
        {
            var jobIds = await this.GetAllJobIdsAsync(cancellationToken).ConfigureAwait(false);
            return jobIds.Count;
        }

        private async Task<List<string>> GetAllJobIdsAsync(CancellationToken cancellationToken)
        {
            var json = await this.cache.GetStringAsync(this.jobIndexKey, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(json))
                return new List<string>();

            return JsonSerializer.Deserialize<List<string>>(json, GetJsonOptions()) ?? new List<string>();
        }

        private async Task AddToJobIndexAsync(string jobId, CancellationToken cancellationToken)
        {
            var jobIds = await this.GetAllJobIdsAsync(cancellationToken).ConfigureAwait(false);
            if (!jobIds.Contains(jobId))
            {
                jobIds.Add(jobId);
                var json = JsonSerializer.Serialize(jobIds, GetJsonOptions());

                var cacheOptions = new DistributedCacheEntryOptions
                {
                    // Job index should not expire
                    AbsoluteExpirationRelativeToNow = null,
                    SlidingExpiration = null
                };

                await this.cache.SetStringAsync(this.jobIndexKey, json, cacheOptions, cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task RemoveFromJobIndexAsync(string jobId, CancellationToken cancellationToken)
        {
            var jobIds = await this.GetAllJobIdsAsync(cancellationToken).ConfigureAwait(false);
            if (jobIds.Remove(jobId))
            {
                var json = JsonSerializer.Serialize(jobIds, GetJsonOptions());

                var cacheOptions = new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = null,
                    SlidingExpiration = null
                };

                await this.cache.SetStringAsync(this.jobIndexKey, json, cacheOptions, cancellationToken).ConfigureAwait(false);
            }
        }

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            TypeInfoResolver = new DataContractResolver(),
            Converters = { new ArrayJsonConverter(), new ObjectToInferredTypesConverter() },
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        private static JsonSerializerOptions GetJsonOptions() => JsonOptions;
    }

    /// <summary>
    /// Configuration options for the distributed batch job store.
    /// </summary>
    public class DistributedBatchJobStoreOptions
    {
        /// <summary>
        /// Gets or sets the key prefix used for all cache entries.
        /// Default is "wormhole:batchjobs".
        /// </summary>
        public string KeyPrefix { get; set; } = "wormhole:batchjobs";

        /// <summary>
        /// Gets or sets the sliding expiration for job entries.
        /// Default is 1 hour.
        /// </summary>
        public TimeSpan? SlidingExpiration { get; set; } = TimeSpan.FromHours(1);

        /// <summary>
        /// Gets or sets the absolute expiration for job entries.
        /// Default is 24 hours.
        /// </summary>
        public TimeSpan? AbsoluteExpiration { get; set; } = TimeSpan.FromHours(24);
    }
}
