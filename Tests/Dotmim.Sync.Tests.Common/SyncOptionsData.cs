using System.Collections;
using System.Collections.Generic;
using Dotmim.Sync.Tests.Core;

namespace Dotmim.Sync.Tests
{
    public class SyncClientsWithBatching : IEnumerable<object[]>
    {
        public IEnumerator<object[]> GetEnumerator()
        {
            yield return new object[] { new SyncWithClient(ProviderType.Sqlite, new SyncOptions { BatchSize = 5 }) };
            yield return new object[] { new SyncWithClient(ProviderType.Sqlite, new SyncOptions { BatchSize = 0 , UseBulkOperations = false}) };
            
            yield return new object[] { new SyncWithClient(ProviderType.Sql, new SyncOptions { BatchSize = 5 }) };
            yield return new object[] { new SyncWithClient(ProviderType.Sql, new SyncOptions { BatchSize = 0 , UseBulkOperations = false}) };
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
    public class SyncClientsSimple : IEnumerable<object[]>
    {
        public IEnumerator<object[]> GetEnumerator()
        {
            yield return new object[] { new SyncWithClient(ProviderType.Sqlite, new SyncOptions { BatchSize = 0 , UseBulkOperations = false}) };
            yield return new object[] { new SyncWithClient(ProviderType.Sql, new SyncOptions { BatchSize = 0 , UseBulkOperations = false}) };
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public class SyncWithClient
    {
        public SyncWithClient(ProviderType clientType, SyncOptions options)
        {
            this.ClientType = clientType;
            this.Options = options;
        }

        public ProviderType ClientType { get; }
        public SyncOptions Options { get; }
    }
}
