using System.Threading;
using System.Threading.Tasks;

namespace Wormhole.Sync
{
    public class NullScopeProvisioningCache : IScopeProvisioningCache
    {
        public static readonly IScopeProvisioningCache Instance = new NullScopeProvisioningCache();

        private NullScopeProvisioningCache()
        {
        }

        public Task<(bool Found, bool IsProvisioned)> TryGetProvisioningStateAsync(string connectionString, SyncSetup setup,
            ScopeInfoClientParameters clientParameters, string scopeName, CancellationToken cancellationToken = default)
        {
            return Task.FromResult((false, false));
        }

        public Task SetProvisioningStateAsync(string connectionString, SyncSetup setup, ScopeInfoClientParameters clientParameters,
            string scopeName, bool isProvisioned, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task InvalidateProvisioningStateAsync(string connectionString, SyncSetup setup, ScopeInfoClientParameters clientParameters,
            string scopeName, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task InvalidateAllAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}