using Wormhole.Sync.Extensions;
using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Wormhole.Sync.Serialization;
using System.Collections.ObjectModel;
using System.Linq;

namespace Wormhole.Sync
{

    /// <summary>
    /// Mapping sur la table ScopeInfo.
    /// </summary>
    [DataContract(Name = "scope"), Serializable]
    public class ScopeInfo
    {
        internal static ISerializer Serializer => SerializersFactory.JsonSerializerFactory.GetSerializer();
        
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
        /// Get server capabilities as strongly-typed object.
        /// </summary>
        public ReadOnlyDictionary<string, object> GetServerCapabilities()
        {
            if (string.IsNullOrEmpty(ServerCapabilities))
                return new (new Dictionary<string, object>());

            try
            {
                return new(JsonSerializer.Deserialize<Dictionary<string, object>>(ServerCapabilities));
            }
            catch
            {
                return new (new Dictionary<string, object>());
            }
        }

        /// <summary>
        /// Set server capabilities from dictionary.
        /// </summary>
        public void SetServerCapabilities(IDictionary<string, object> capabilities)
        {
            if (capabilities == null)
            {
                ServerCapabilities = null;
                return;
            }
            var changed = HaveCapabilitiesChanged(capabilities);

            if (changed)
            {
                ServerCapabilities = JsonSerializer.Serialize(capabilities);
            }
        }

        private bool HaveCapabilitiesChanged(IDictionary<string, object> capabilities)
        {
            var oldCapabilities = this.GetServerCapabilities() ??  new(new Dictionary<string, object>());
            
            // compare capabilities to only update the "last udpated" if it actually changed
            var changed = false;
            if (capabilities.Keys.OrderBy(k => k).SequenceEqual(oldCapabilities.Keys.OrderBy(k => k)))
            {
                foreach (var k in capabilities.Keys)
                {
                    if (!object.Equals(oldCapabilities[k], capabilities[k]))
                    {
                        changed = true;
                        break;
                    }
                }
            }
            else
                changed = true;

            return changed;
        }

        /// <summary>
        /// Check if server supports a specific capability.
        /// </summary>
        public bool SupportsCapability(string capabilityKey)
        {
            var capabilities = GetServerCapabilities();
            return capabilities?.ContainsKey(capabilityKey) ?? false;
        }

        /// <summary>
        /// Update schema hash when schema changes.
        /// </summary>
        public void UpdateSchemaHash(string schemaJson)
        {
            if (string.IsNullOrEmpty(schemaJson))
            {
                SchemaHash = null;
                return;
            }

            if (!schemaJson.StartsWith("{"))
                throw new ArgumentException($"{nameof(schemaJson)} must be valid json");

            SchemaHash = GenerateSchemaHash(schemaJson);
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
            var schemaJson = Serializer.Serialize(schema).ToUtf8String();
            return GenerateSchemaHash(schemaJson);
        }
    
        /// <summary>
        /// Generate consistent schema hash.
        /// </summary>
        private static string GenerateSchemaHash(string schemaJson)
        {
            
            var hash = HashAlgorithm.SHA256.Create(Encoding.UTF8.GetBytes(schemaJson));
            return Convert.ToBase64String(hash);
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