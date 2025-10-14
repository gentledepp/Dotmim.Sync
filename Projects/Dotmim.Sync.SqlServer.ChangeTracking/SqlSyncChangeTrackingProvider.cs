using Wormhole.Sync.Builders;
using Wormhole.Sync.SqlServer.Builders;
using Wormhole.Sync.SqlServer.ChangeTracking.Builders;
using Microsoft.Data.SqlClient;
using System;

namespace Wormhole.Sync.SqlServer
{
    /// <inheritdoc />
    public class SqlSyncChangeTrackingProvider : SqlSyncProvider
    {
        private static string providerType;
        private static string shortProviderType;

        /// <inheritdoc />
        public SqlSyncChangeTrackingProvider()
            : base() { }

        /// <inheritdoc />
        public SqlSyncChangeTrackingProvider(string connectionString)
            : base(connectionString)
        {
        }

        /// <inheritdoc />
        public SqlSyncChangeTrackingProvider(SqlConnectionStringBuilder builder)
            : base(builder)
        {
        }

        /// <summary>
        /// Gets the provider type.
        /// </summary>
        public static new string ProviderType
        {
            get
            {
                if (!string.IsNullOrEmpty(providerType))
                    return providerType;

                Type type = typeof(SqlSyncChangeTrackingProvider);
                providerType = $"{type.Name}, {type}";

                return providerType;
            }
        }

        /// <inheritdoc />
        public override string GetProviderTypeName() => ProviderType;

        /// <summary>
        /// Gets the short provider type.
        /// </summary>
        public static new string ShortProviderType
        {
            get
            {
                if (!string.IsNullOrEmpty(shortProviderType))
                    return shortProviderType;

                var type = typeof(SqlSyncChangeTrackingProvider);
                shortProviderType = type.Name;

                return shortProviderType;
            }
        }

        /// <inheritdoc />
        public override DbScopeBuilder GetScopeBuilder(string scopeInfoTableName, string prefix = "", string suffix = "")
            => new SqlChangeTrackingScopeBuilder(scopeInfoTableName, prefix, suffix);

        /// <inheritdoc />
        public override DbSyncAdapter GetSyncAdapter(SyncTable tableDescription, ScopeInfo scopeInfo)
            => new SqlChangeTrackingSyncAdapter(tableDescription, scopeInfo, this.UseBulkOperations);

        /// <inheritdoc />
        public override DbDatabaseBuilder GetDatabaseBuilder() => new SqlChangeTrackingDatabaseBuilder();
    }
}