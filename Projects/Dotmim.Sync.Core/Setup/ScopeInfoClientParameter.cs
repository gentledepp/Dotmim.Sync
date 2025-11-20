using System;
using System.Collections.Generic;
using System.Data;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace Wormhole.Sync
{
    /// <summary>
    /// Represents a custom parameter definition for the scope_info_client table.
    /// These parameters are stored as nullable columns on the server-side scope_info_client table.
    /// </summary>
    [DataContract(Name = "sicp"), Serializable]
    public class ScopeInfoClientParameter : SyncNamedItem<ScopeInfoClientParameter>
    {
        /// <summary>
        /// Gets or sets the name of the parameter (will be used as column name).
        /// </summary>
        [DataMember(Name = "n", IsRequired = true, Order = 1)]
        [JsonPropertyName("n")]
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets the database type for the parameter.
        /// </summary>
        [DataMember(Name = "dt", IsRequired = true, Order = 2)]
        [JsonPropertyName("dt")]
        public DbType DbType { get; set; }

        /// <summary>
        /// Gets or sets the max length for string/binary types (-1 for MAX).
        /// </summary>
        [DataMember(Name = "ml", IsRequired = false, Order = 3)]
        [JsonPropertyName("ml")]
        public int MaxLength { get; set; }

        /// <summary>
        /// Gets or sets whether this parameter column should be indexed in the database.
        /// </summary>
        [DataMember(Name = "idx", IsRequired = false, Order = 4)]
        [JsonPropertyName("idx")]
        public bool IsIndexed { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ScopeInfoClientParameter"/> class.
        /// </summary>
        [JsonConstructor]
        public ScopeInfoClientParameter()
        {
            // No initialization - all properties set via deserialization
            // DbType defaults to DbType.Object (0) if not present in JSON
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ScopeInfoClientParameter"/> class.
        /// </summary>
        public ScopeInfoClientParameter(string name, DbType dbType, int maxLength = -1, bool isIndexed = false)
        {
            this.Name = name;
            this.DbType = dbType;
            this.MaxLength = maxLength;
            this.IsIndexed = isIndexed;
        }

        /// <summary>
        /// Clone the parameter definition.
        /// </summary>
        public ScopeInfoClientParameter Clone()
        {
            return new ScopeInfoClientParameter
            {
                Name = this.Name,
                DbType = this.DbType,
                MaxLength = this.MaxLength,
                IsIndexed = this.IsIndexed,
            };
        }

        /// <inheritdoc/>
        public override IEnumerable<string> GetAllNamesProperties()
        {
            yield return this.Name;
        }

        /// <inheritdoc/>
        public override bool EqualsByProperties(ScopeInfoClientParameter other)
        {
            if (other == null)
                return false;

            var sc = SyncGlobalization.DataSourceStringComparison;

            return string.Equals(this.Name, other.Name, sc) &&
                   this.DbType == other.DbType &&
                   this.MaxLength == other.MaxLength &&
                   this.IsIndexed == other.IsIndexed;
        }

        /// <summary>
        /// Returns a string that represents the current object.
        /// </summary>
        public override string ToString() => $"{this.Name} ({this.DbType})";

        /// <summary>
        /// Checks if a SyncParameter is compatible with this parameter definition and attempts to convert the value.
        /// This method handles JSON-deserialized values where types like Guid, DateTime, TimeSpan, etc. are transmitted as strings.
        /// Also validates string length against MaxLength property if set.
        /// </summary>
        /// <param name="syncParameter">The sync parameter to validate and convert.</param>
        /// <param name="convertedValue">The converted value if successful, null otherwise.</param>
        /// <returns>True if the parameter is compatible and can be converted; false otherwise.</returns>
        public bool TryGetCompatibleValue(SyncParameter syncParameter, out object convertedValue)
        {
            convertedValue = null;

            // Null parameter is not compatible (different from null value)
            if (syncParameter == null)
                return false;

            // Null values are always compatible
            if (syncParameter.Value == null)
            {
                convertedValue = null;
                return true;
            }

            try
            {
                // Attempt to convert the value using the robust SyncTypeConverter
                convertedValue = SyncTypeConverter.TryConvertFromDbType(syncParameter.Value, this.DbType);

                // For string types, validate length against MaxLength if specified
                if (convertedValue is string stringValue && this.MaxLength > 0)
                {
                    if (stringValue.Length > this.MaxLength)
                    {
                        // String is too long - incompatible
                        convertedValue = null;
                        return false;
                    }
                }

                return true;
            }
            catch
            {
                // Conversion failed - value is not compatible
                convertedValue = null;
                return false;
            }
        }

        /// <summary>
        /// Checks if a SyncParameter is compatible with this parameter definition (without conversion).
        /// This is a convenience method that calls TryGetCompatibleValue and discards the converted value.
        /// </summary>
        /// <param name="syncParameter">The sync parameter to validate.</param>
        /// <returns>True if the parameter is compatible; false otherwise.</returns>
        public bool IsCompatibleWith(SyncParameter syncParameter)
        {
            return TryGetCompatibleValue(syncParameter, out _);
        }
    }
}
