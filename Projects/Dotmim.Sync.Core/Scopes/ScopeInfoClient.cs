using Wormhole.Sync.Extensions;
using Wormhole.Sync.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace Wormhole.Sync
{
    /// <summary>
    /// Mapping sur la table ScopeInfo.
    /// </summary>
    [DataContract(Name = "scope_client"), Serializable]
    public class ScopeInfoClient
    {
        private static readonly ISerializer JsonSerializer = SerializersFactory.JsonSerializerFactory.GetSerializer();

        /// <summary>
        /// Initializes a new instance of the <see cref="ScopeInfoClient"/> class.
        /// For serialization purpose.
        /// </summary>
        public ScopeInfoClient()
        {
        }

        /// <summary>
        /// Gets or sets id of the scope owner.
        /// </summary>
        [DataMember(Name = "id", IsRequired = true, Order = 1)]
        public Guid Id { get; set; }

        /// <summary>
        /// Gets or sets scope name. Shared by all clients and the server.
        /// </summary>
        [DataMember(Name = "n", IsRequired = true, Order = 2)]
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets scope Hash: Filters hash or null.
        /// </summary>
        [DataMember(Name = "h", IsRequired = true, Order = 3)]
        public string Hash { get; set; }

        /// <summary>
        /// Gets or Sets the last timestamp a sync has occured. This timestamp is set just 'before' sync start.
        /// </summary>
        [DataMember(Name = "lst", IsRequired = true, Order = 4)]
        public long? LastSyncTimestamp { get; set; }

        /// <summary>
        /// Gets or Sets the last server timestamp a sync has occured for this scope client.
        /// </summary>
        [DataMember(Name = "lsst", IsRequired = true, Order = 5)]
        public long? LastServerSyncTimestamp { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether gets or Sets if the client scope is new in the local datasource.
        /// If new, we will override timestamp for first synchronisation to be sure to get all datas from server.
        /// </summary>
        [DataMember(Name = "in", IsRequired = true, Order = 6)]
        public bool IsNewScope { get; set; }

        /// <summary>
        /// Gets or Sets the parameters.
        /// </summary>
        [DataMember(Name = "p", IsRequired = false, Order = 7)]
        public SyncParameters Parameters { get; set; }

        /// <summary>
        /// Gets or Sets the comma-separated list of table names that need full re-sync
        /// (e.g., "dbo.Customer,dbo.Product"). Set when a client upgrades and needs
        /// new column data populated for specific tables.
        /// </summary>
        [DataMember(Name = "rt", IsRequired = false, EmitDefaultValue = false, Order = 8)]
        public string ReinitTables { get; set; }

        /// <summary>
        /// Gets or Sets the JSON array of migration names that the client last reported as supported.
        /// Stored server-side in scope_info_client to detect when a client upgrades.
        /// </summary>
        [DataMember(Name = "sm", IsRequired = false, EmitDefaultValue = false, Order = 9)]
        public string SupportedMigrations { get; set; }

        /// <summary>
        /// Gets or Sets the cached JSON array of migration names from the server's last known scope.
        /// Used client-side to avoid forcing traditional flow on every sync while the server hasn't migrated.
        /// Null means "never cached" (first sync); empty JSON array means "server has no migrations".
        /// </summary>
        [IgnoreDataMember]
        public string LastKnownServerMigrations { get; set; }

        /// <summary>
        /// Gets or Sets the last datetime when a sync has successfully ended.
        /// </summary>
        [IgnoreDataMember]
        public DateTime? LastSync { get; set; }

        /// <summary>
        /// Gets or Sets the last duration a sync has occured.
        /// </summary>
        [IgnoreDataMember]
        public long LastSyncDuration { get; set; }

        /// <summary>
        /// Gets or Sets the additional properties.
        /// </summary>
        [IgnoreDataMember]
        public string Properties { get; set; }

        /// <summary>
        /// Gets or Sets the errors batch info occured on last sync.
        /// </summary>
        [IgnoreDataMember]
        public string Errors { get; set; }

        /// <summary>
        /// Gets a readable version of LastSyncDuration.
        /// </summary>
        [IgnoreDataMember]
        public string LastSyncDurationString
        {
            get
            {
                var durationTs = new TimeSpan(this.LastSyncDuration);
                return $"{durationTs.Hours}:{durationTs.Minutes}:{durationTs.Seconds}.{durationTs.Milliseconds}";
            }
        }

        /// <summary>
        /// Make a shadow copy of an old scope to get the last sync information copied on this scope.
        /// </summary>
        public void ShadowScope(ScopeInfoClient oldScopeInfoClient)
        {
            Guard.ThrowIfNull(oldScopeInfoClient);

            this.LastServerSyncTimestamp = oldScopeInfoClient.LastServerSyncTimestamp;
            this.LastSyncTimestamp = oldScopeInfoClient.LastSyncTimestamp;
            this.LastSync = oldScopeInfoClient.LastSync;
            this.LastSyncDuration = oldScopeInfoClient.LastSyncDuration;
        }

        /// <summary>
        /// Get the set of table names that need re-initialization.
        /// </summary>
        public HashSet<string> GetReinitTablesSet()
        {
            if (string.IsNullOrEmpty(ReinitTables))
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            return new HashSet<string>(
               ReinitTables.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries),
               StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Set the re-init tables from a collection of table names.
        /// </summary>
        public void SetReinitTables(IEnumerable<string> tableNames)
        {
            if (tableNames == null)
            {
                ReinitTables = null;
                return;
            }

            var list = new List<string>(tableNames);
            ReinitTables = list.Count > 0 ? string.Join(",", list) : null;
        }

        /// <summary>
        /// Add a table name to the re-init set.
        /// </summary>
        public void AddReinitTable(string tableName)
        {
            var set = GetReinitTablesSet();
            set.Add(tableName);
            SetReinitTables(set);
        }

        /// <summary>
        /// Get the list of supported migration names.
        /// </summary>
        public List<string> GetSupportedMigrationsList()
        {
            if (string.IsNullOrEmpty(SupportedMigrations))
                return new List<string>();

            try
            {
                return System.Text.Json.JsonSerializer.Deserialize<List<string>>(SupportedMigrations);
            }
            catch
            {
                return new List<string>();
            }
        }

        /// <summary>
        /// Set the list of supported migration names.
        /// </summary>
        public void SetSupportedMigrationsList(List<string> migrations)
        {
            if (migrations == null || migrations.Count == 0)
            {
                SupportedMigrations = null;
                return;
            }

            migrations.Sort(StringComparer.Ordinal);
            SupportedMigrations = System.Text.Json.JsonSerializer.Serialize(migrations);
        }

        /// <summary>
        /// Get the cached list of server migration names. Returns null if never cached.
        /// </summary>
        public List<string> GetLastKnownServerMigrationsList()
        {
            if (string.IsNullOrEmpty(LastKnownServerMigrations))
                return null;

            try
            {
                return System.Text.Json.JsonSerializer.Deserialize<List<string>>(LastKnownServerMigrations);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Set the cached list of server migration names.
        /// </summary>
        public void SetLastKnownServerMigrationsList(List<string> migrations)
        {
            LastKnownServerMigrations = System.Text.Json.JsonSerializer.Serialize(migrations ?? new List<string>());
        }

        /// <summary>
        /// Gets the scope info as a string.
        /// </summary>
        public override string ToString()
        {
            var p = this.Parameters != null ? JsonSerializer.Serialize(this.Parameters).ToUtf8String() : null;

            return $"Scope Name:{this.Name}. Id:{this.Id}. Hash:{this.Hash}. LastSyncTimestamp:{this.LastSyncTimestamp}. LastServerSyncTimestamp:{this.LastServerSyncTimestamp}. LastSync:{this.LastSync}. Parameters:{p} ";
        }
    }
}