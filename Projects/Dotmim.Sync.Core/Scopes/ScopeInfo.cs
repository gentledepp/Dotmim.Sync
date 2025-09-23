using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dotmim.Sync.Serialization;

namespace Dotmim.Sync
{

    /// <summary>
    /// Mapping sur la table ScopeInfo.
    /// </summary>
    [DataContract(Name = "scope"), Serializable]
    public class ScopeInfo
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ScopeInfo"/> class.
        /// For serialization purpose.
        /// </summary>
        public ScopeInfo()
        {
        }

        /// <summary>
        /// Gets or sets scope name. Shared by all clients and the server.
        /// </summary>
        [DataMember(Name = "n", IsRequired = true, Order = 1)]
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets scope schema. stored locally on the client.
        /// </summary>
        [DataMember(Name = "sch", IsRequired = true, Order = 2)]
        public SyncSet Schema { get; set; }

        /// <summary>
        /// Gets or sets setup. stored locally on the client.
        /// </summary>
        [DataMember(Name = "s", IsRequired = true, Order = 3)]
        public SyncSetup Setup { get; set; }

        /// <summary>
        /// Gets or Sets the schema version.
        /// </summary>
        [DataMember(Name = "v", IsRequired = false, EmitDefaultValue = false, Order = 4)]
        public string Version { get; set; }

        /// <summary>
        /// Gets or Sets the last timestamp a sync has occured. This timestamp is set just 'before' sync start.
        /// </summary>
        [DataMember(Name = "lst", IsRequired = false, Order = 5)]
        public long? LastCleanupTimestamp { get; set; }

        /// <summary>
        /// Gets or Sets the additional properties.
        /// </summary>
        [IgnoreDataMember]
        public string Properties { get; set; }

        /// <summary>
        /// Server capabilities discovered during initial sync.
        /// Stored as JSON string for database compatibility.
        /// </summary>
        [DataMember(Name = "sc", IsRequired = false, EmitDefaultValue = false, Order = 6)]
        public string ServerCapabilities { get; set; }

        /// <summary>
        /// SHA256 hash of the schema for fast validation.
        /// Calculated once and stored to avoid recalculation overhead.
        /// </summary>
        [DataMember(Name = "sh", IsRequired = false, EmitDefaultValue = false, Order = 7)]
        public string SchemaHash { get; set; }

        /// <summary>
        /// Server version from last successful sync.
        /// Used to detect server upgrades that might change capabilities.
        /// </summary>
        [DataMember(Name = "sv", IsRequired = false, EmitDefaultValue = false, Order = 8)]
        public string ServerVersion { get; set; }

        /// <summary>
        /// Timestamp when capabilities were last updated.
        /// Allows for periodic capability refresh if needed.
        /// </summary>
        [DataMember(Name = "clu", IsRequired = false, EmitDefaultValue = false, Order = 9)]
        public DateTime? CapabilitiesLastUpdated { get; set; }

        /// <summary>
        /// Get server capabilities as strongly-typed object.
        /// </summary>
        public Dictionary<string, object> GetServerCapabilities()
        {
            if (string.IsNullOrEmpty(ServerCapabilities))
                return null;

            try
            {
                return JsonSerializer.Deserialize<Dictionary<string, object>>(ServerCapabilities);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Set server capabilities from dictionary.
        /// </summary>
        public void SetServerCapabilities(Dictionary<string, object> capabilities)
        {
            if (capabilities == null)
            {
                ServerCapabilities = null;
                return;
            }

            try
            {
                ServerCapabilities = JsonSerializer.Serialize(capabilities);
                CapabilitiesLastUpdated = DateTime.UtcNow;
            }
            catch
            {
                ServerCapabilities = null;
            }
        }

        /// <summary>
        /// Check if server supports a specific capability.
        /// </summary>
        public bool SupportsCapability(string capabilityKey)
        {
            var capabilities = GetServerCapabilities();
            if (capabilities?.TryGetValue(capabilityKey, out var value) == true)
            {
                return value is bool boolValue ? boolValue : false;
            }
            return false;
        }

        /// <summary>
        /// Update schema hash when schema changes.
        /// </summary>
        public void UpdateSchemaHash(SyncSet schema)
        {
            if (schema == null)
            {
                SchemaHash = null;
                return;
            }

            SchemaHash = GenerateSchemaHash(schema);
        }

        /// <summary>
        /// Generate consistent schema hash.
        /// </summary>
        private static string GenerateSchemaHash(SyncSet schema)
        {
            try
            {
                // Use consistent serialization options for reproducible hash
                var options = new JsonSerializerOptions
                {
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
                    WriteIndented = false
                };

                var schemaJson = JsonSerializer.Serialize(schema, options);
                var hash = HashAlgorithm.SHA256.Create(schemaJson);
                return Convert.ToBase64String(hash);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Validate if current schema hash matches provided schema.
        /// </summary>
        public bool ValidateSchemaHash(SyncSet schema)
        {
            if (string.IsNullOrEmpty(SchemaHash) || schema == null)
                return false;

            var currentHash = GenerateSchemaHash(schema);
            return SchemaHash.Equals(currentHash, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Get the scope name / last cleanup / setup tables count.
        /// </summary>
        public override string ToString() => $"Scope Name:{this.Name}({this.Version}). Last cleanup:{this.LastCleanupTimestamp}. Setup tables:{this.Setup?.Tables?.Count}. Schema tables:{this.Schema?.Tables?.Count}";
    }
}