using Wormhole.Sync.Enumerations;
using Wormhole.Sync.SqlServer;
using Wormhole.Sync.Tests.Core;
using Wormhole.Sync.Web.Client;
using Wormhole.Sync.Web.Server;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Wormhole.Sync.Tests.IntegrationTests
{
    public abstract partial class HttpTests
    {
        [Fact]
        public async Task ProvisioningCache_ShouldReduceDatabaseCalls()
        {
            var options = new SyncOptions { DisableConstraintsOnApplyChanges = true };

            // Stop Kestrel to reconfigure with custom cache tracking
            await this.Kestrel.StopAsync();

            var cacheHitCount = 0;
            var cacheMissCount = 0;

            // Create a HybridCache and tracking cache that tracks hits/misses
            var services = new ServiceCollection();
            services.AddHybridCache();
            var serviceProvider = services.BuildServiceProvider();
            var hybridCache = serviceProvider.GetRequiredService<HybridCache>();

            var trackingCache = new TrackingScopeProvisioningCache(
                new HybridScopeProvisioningCache(hybridCache),
                () => cacheHitCount++,
                () => cacheMissCount++);

            // Register the tracking cache
            this.Kestrel.ConfigureServices(s => s.AddSingleton<IScopeProvisioningCache>(trackingCache));
            this.Kestrel.AddSyncServer(serverProvider, setup, options);

            var serviceUri = this.Kestrel.Run();

            // Execute first sync - should be a cache miss (provisioning happens)
            var clientProvider = clientsProvider.First();
            var agent = new SyncAgent(clientProvider, new WebRemoteOrchestrator(serviceUri), options);

            var s1 = await agent.SynchronizeAsync();

            Assert.True(s1.TotalChangesDownloadedFromServer >= 0);
            Assert.Equal(0, cacheHitCount); // First sync, no cache hit yet
            Assert.True(cacheMissCount > 0); // Should have checked cache and missed

            // Reset counters
            var previousMissCount = cacheMissCount;
            cacheHitCount = 0;
            cacheMissCount = 0;

            // Execute second sync - should hit the cache (no provisioning checks)
            var agent2 = new SyncAgent(clientProvider, new WebRemoteOrchestrator(serviceUri), options);
            ;
            var s2 = await agent2.SynchronizeAsync();

            Assert.True(s2.TotalChangesDownloadedFromServer >= 0);
            Assert.True(cacheHitCount > 0); // Second sync, should hit cache
            Assert.Equal(0, cacheMissCount); // Should not miss cache on second sync
        }

        [Fact]
        public async Task ProvisioningCache_ShouldBeInvalidatedAfterDeprovision()
        {
            var options = new SyncOptions { DisableConstraintsOnApplyChanges = true };

            // Stop Kestrel to reconfigure
            await this.Kestrel.StopAsync();

            var services = new ServiceCollection();
            services.AddHybridCache();
            var serviceProvider = services.BuildServiceProvider();
            var hybridCache = serviceProvider.GetRequiredService<HybridCache>();

            var provisioningCache = new HybridScopeProvisioningCache(hybridCache);

            this.Kestrel.ConfigureServices(s => s.AddSingleton<IScopeProvisioningCache>(provisioningCache));
            this.Kestrel.AddSyncServer(serverProvider, setup, options);

            var serviceUri = this.Kestrel.Run();

            var clientProvider = clientsProvider.First();
            var agent = new SyncAgent(clientProvider, new WebRemoteOrchestrator(serviceUri), options);

            // First sync - provisions and caches
            var s1 = await agent.SynchronizeAsync();
            Assert.True(s1.TotalChangesDownloadedFromServer >= 0);

            // Verify cache has an entry
            var connectionString = serverProvider.ConnectionString;
            var (hasCachedValue, isProvisioned) = await provisioningCache.TryGetProvisioningStateAsync(
                connectionString, setup, setup.ScopeInfoClientParameters, SyncOptions.DefaultScopeName);

            Assert.True(hasCachedValue);
            Assert.True(isProvisioned);

            // Deprovision
            var remoteOrchestrator = new RemoteOrchestrator(serverProvider, options);
            remoteOrchestrator.ProvisioningCache = provisioningCache;
            await remoteOrchestrator.DeprovisionAsync(SyncProvision.TrackingTable | SyncProvision.StoredProcedures | SyncProvision.Triggers);

            // Verify cache was invalidated
            (hasCachedValue, isProvisioned) = await provisioningCache.TryGetProvisioningStateAsync(
                connectionString, setup, setup.ScopeInfoClientParameters, SyncOptions.DefaultScopeName);

            Assert.False(hasCachedValue); // Cache should be cleared after deprovision
        }

        [Fact]
        public async Task ProvisioningCache_ShouldHandleMultipleScopes()
        {
            var options = new SyncOptions { DisableConstraintsOnApplyChanges = true };

            // Stop Kestrel to reconfigure
            await this.Kestrel.StopAsync();

            var services = new ServiceCollection();
            services.AddHybridCache();
            var serviceProvider = services.BuildServiceProvider();
            var hybridCache = serviceProvider.GetRequiredService<HybridCache>();

            var provisioningCache = new HybridScopeProvisioningCache(hybridCache);

            // Add two different scopes
            var scope1 = "Scope1";
            var scope2 = "Scope2";

            this.Kestrel.ConfigureServices(s => s.AddSingleton<IScopeProvisioningCache>(provisioningCache));
            this.Kestrel.AddSyncServer(serverProvider, setup, options, scopeName: scope1);
            this.Kestrel.AddSyncServer(serverProvider, setup, options, scopeName: scope2);

            var serviceUri = this.Kestrel.Run();

            var clientProvider = clientsProvider.First();

            // Sync with scope1
            var agent1 = new SyncAgent(clientProvider, new WebRemoteOrchestrator(serviceUri), options);
            var s1 = await agent1.SynchronizeAsync(scopeName:scope1);
            Assert.True(s1.TotalChangesDownloadedFromServer >= 0);

            // Sync with scope2
            var agent2 = new SyncAgent(clientProvider, new WebRemoteOrchestrator(serviceUri), options);
            var s2 = await agent2.SynchronizeAsync(scopeName:scope2);
            Assert.True(s2.TotalChangesDownloadedFromServer >= 0);

            // Verify both scopes are cached separately
            var connectionString = serverProvider.ConnectionString;

            var (hasScope1, isProvisioned1) = await provisioningCache.TryGetProvisioningStateAsync(
                connectionString, setup, setup.ScopeInfoClientParameters, scope1);
            var (hasScope2, isProvisioned2) = await provisioningCache.TryGetProvisioningStateAsync(
                connectionString, setup, setup.ScopeInfoClientParameters, scope2);

            Assert.True(hasScope1);
            Assert.True(isProvisioned1);
            Assert.True(hasScope2);
            Assert.True(isProvisioned2);
        }

        [Fact]
        public async Task ProvisioningCache_CanBeDisabledBySettingNull()
        {
            var options = new SyncOptions { DisableConstraintsOnApplyChanges = true };

            // Stop Kestrel to reconfigure
            await this.Kestrel.StopAsync();

            // Don't register a cache - it should work without caching
            this.Kestrel.ConfigureServices(s => s.AddSingleton<IScopeProvisioningCache>(NullScopeProvisioningCache.Instance));
            this.Kestrel.AddSyncServer(serverProvider, setup, options);

            var serviceUri = this.Kestrel.Run();

            var clientProvider = clientsProvider.First();
            var agent = new SyncAgent(clientProvider, new WebRemoteOrchestrator(serviceUri), options);

            // Should work without cache
            var s = await agent.SynchronizeAsync();

            Assert.True(s.TotalChangesDownloadedFromServer >= 0);
        }

        /// <summary>
        /// Helper class to track cache hits and misses
        /// </summary>
        private class TrackingScopeProvisioningCache : IScopeProvisioningCache
        {
            private readonly IScopeProvisioningCache innerCache;
            private readonly Action onCacheHit;
            private readonly Action onCacheMiss;

            public TrackingScopeProvisioningCache(IScopeProvisioningCache innerCache, Action onCacheHit, Action onCacheMiss)
            {
                this.innerCache = innerCache;
                this.onCacheHit = onCacheHit;
                this.onCacheMiss = onCacheMiss;
            }

            public async Task<(bool Found, bool IsProvisioned)> TryGetProvisioningStateAsync(string connectionString, SyncSetup setup,
                ScopeInfoClientParameters clientParameters, string scopeName, CancellationToken cancellationToken = default)
            {
                var result = await innerCache.TryGetProvisioningStateAsync(connectionString, setup, clientParameters, scopeName, cancellationToken).ConfigureAwait(false);
                if (result.Found)
                    onCacheHit?.Invoke();
                else
                    onCacheMiss?.Invoke();
                return result;
            }

            public Task SetProvisioningStateAsync(string connectionString, SyncSetup setup, ScopeInfoClientParameters clientParameters,
                string scopeName, bool isProvisioned, CancellationToken cancellationToken = default)
            {
                return innerCache.SetProvisioningStateAsync(connectionString, setup, clientParameters, scopeName, isProvisioned, cancellationToken);
            }

            public Task InvalidateProvisioningStateAsync(string connectionString, SyncSetup setup, ScopeInfoClientParameters clientParameters,
                string scopeName, CancellationToken cancellationToken = default)
            {
                return innerCache.InvalidateProvisioningStateAsync(connectionString, setup, clientParameters, scopeName, cancellationToken);
            }

            public Task InvalidateAllAsync(CancellationToken cancellationToken = default)
            {
                return innerCache.InvalidateAllAsync(cancellationToken);
            }
        }

    }
}
