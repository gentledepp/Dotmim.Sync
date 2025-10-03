using Wormhole.Sync.DatabaseStringParsers;
using Wormhole.Sync.Enumerations;
using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Wormhole.Sync
{
    /// <summary>
    /// Represents a table to be synchronized.
    /// </summary>
    [DataContract(Name = "st"), Serializable]
    public class SetupTable : SyncNamedItem<SetupTable>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SetupTable"/> class.
        /// public ctor for serialization purpose.
        /// </summary>
        public SetupTable()
        {
        }

        /// <summary>
        /// Gets or Sets the table name.
        /// </summary>
        [DataMember(Name = "tn", IsRequired = true, Order = 1)]
        public string TableName { get; set; }

        /// <summary>
        /// Gets or Sets the schema name.
        /// </summary>
        [DataMember(Name = "sn", IsRequired = false, EmitDefaultValue = false, Order = 2)]
        public string SchemaName { get; set; }

        /// <summary>
        /// Gets or Sets the table columns collection.
        /// </summary>
        [DataMember(Name = "cols", IsRequired = false, EmitDefaultValue = false, Order = 3)]
        public SetupColumns Columns { get; set; }

        /// <summary>
        /// Gets or Sets the Sync direction (may be Bidirectional, DownloadOnly, UploadOnly)
        /// Default is Bidirectional.
        /// </summary>
        [DataMember(Name = "sd", IsRequired = false, EmitDefaultValue = false, Order = 4)]
        public SyncDirection SyncDirection { get; set; }

        /// <summary>
        /// Gets or sets a custom name for the INSERT trigger. If not specified, the default naming convention is used.
        /// </summary>
        [DataMember(Name = "citn", IsRequired = false, EmitDefaultValue = false, Order = 5)]
        public string CustomInsertTriggerName { get; set; }

        /// <summary>
        /// Gets or sets a custom name for the UPDATE trigger. If not specified, the default naming convention is used.
        /// </summary>
        [DataMember(Name = "cutn", IsRequired = false, EmitDefaultValue = false, Order = 6)]
        public string CustomUpdateTriggerName { get; set; }

        /// <summary>
        /// Gets or sets a custom name for the DELETE trigger. If not specified, the default naming convention is used.
        /// </summary>
        [DataMember(Name = "cdtn", IsRequired = false, EmitDefaultValue = false, Order = 7)]
        public string CustomDeleteTriggerName { get; set; }

        /// <summary>
        /// Gets or sets a custom name for the tracking table. If not specified, the default naming convention is used.
        /// </summary>
        [DataMember(Name = "cttn", IsRequired = false, EmitDefaultValue = false, Order = 8)]
        public string CustomTrackingTableName { get; set; }

        /// <summary>
        /// Gets a value indicating whether check if SetupTable has columns. If not columns specified, all the columns from server database are retrieved.
        /// </summary>
        [IgnoreDataMember]
        public bool HasColumns => this.Columns?.Count > 0;

        /// <summary>
        /// Initializes a new instance of the <see cref="SetupTable"/> class.
        /// Specify a table to add to the sync process
        /// If you don't specify any columns, all columns in the data source will be imported.
        /// </summary>
        public SetupTable(string tableName, string schemaName = null)
        {
            Guard.ThrowIfNull(tableName);

            var fullName = string.IsNullOrEmpty(schemaName) ? tableName : $"{schemaName}.{tableName}";

            // Potentially user can pass something like [SalesLT].[Product]
            // or SalesLT.Product or Product. TableParser will handle it
            var tableParser = new TableParser(fullName);

            this.TableName = tableParser.TableName;

            // https://github.com/Mimetis/Dotmim.Sync/issues/621#issuecomment-968369322
            this.SchemaName = string.IsNullOrEmpty(tableParser.SchemaName) ? string.Empty : tableParser.SchemaName;

            this.Columns = [];
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SetupTable"/> class.
        /// Specify a table and its columns, to add to the sync process
        /// If you're specifying some columns, all others columns in the data source will be ignored.
        /// </summary>
        public SetupTable(string tableName, IEnumerable<string> columnsName, string schemaName = null)
            : this(tableName, schemaName) => this.Columns.AddRange(columnsName);

        /// <summary>
        /// Specify a custom name for the INSERT trigger.
        /// </summary>
        /// <param name="name">The custom name for the INSERT trigger.</param>
        /// <returns>The current SetupTable instance for method chaining.</returns>
        public SetupTable WithInsertTriggerName(string name)
        {
            this.CustomInsertTriggerName = name;
            return this;
        }

        /// <summary>
        /// Specify a custom name for the UPDATE trigger.
        /// </summary>
        /// <param name="name">The custom name for the UPDATE trigger.</param>
        /// <returns>The current SetupTable instance for method chaining.</returns>
        public SetupTable WithUpdateTriggerName(string name)
        {
            this.CustomUpdateTriggerName = name;
            return this;
        }

        /// <summary>
        /// Specify a custom name for the DELETE trigger.
        /// </summary>
        /// <param name="name">The custom name for the DELETE trigger.</param>
        /// <returns>The current SetupTable instance for method chaining.</returns>
        public SetupTable WithDeleteTriggerName(string name)
        {
            this.CustomDeleteTriggerName = name;
            return this;
        }

        /// <summary>
        /// Specify a custom name for the tracking table.
        /// </summary>
        /// <param name="name">The custom name for the tracking table.</param>
        /// <returns>The current SetupTable instance for method chaining.</returns>
        public SetupTable WithTrackingTableName(string name)
        {
            this.CustomTrackingTableName = name;
            return this;
        }

        /// <summary>
        /// ToString override. Gets the full name + columns count.
        /// </summary>
        public override string ToString() => this.GetFullName() + (this.HasColumns ? $" ({this.Columns.Count} columns)" : string.Empty);

        /// <summary>
        /// Gets the full name of the table, based on schema name + "." + table name (if schema name exists).
        /// </summary>
        public string GetFullName()
            => string.IsNullOrEmpty(this.SchemaName) ? this.TableName : $"{this.SchemaName}.{this.TableName}";

        /// <inheritdoc cref="SyncNamedItem{T}.EqualsByProperties(T)"/>
        public override bool EqualsByProperties(SetupTable otherInstance)
        {
            if (otherInstance == null)
                return false;

            var sc = SyncGlobalization.DataSourceStringComparison;

            if (!this.EqualsByName(otherInstance))
                return false;

            // checking properties
            return this.SyncDirection == otherInstance.SyncDirection
                    && this.Columns.CompareWith(otherInstance.Columns, (c, oc) => string.Equals(c, oc, sc))
                    && string.Equals(this.CustomInsertTriggerName, otherInstance.CustomInsertTriggerName, sc)
                    && string.Equals(this.CustomUpdateTriggerName, otherInstance.CustomUpdateTriggerName, sc)
                    && string.Equals(this.CustomDeleteTriggerName, otherInstance.CustomDeleteTriggerName, sc)
                    && string.Equals(this.CustomTrackingTableName, otherInstance.CustomTrackingTableName, sc);
        }

        /// <inheritdoc cref="SyncNamedItem{T}.GetAllNamesProperties"/>
        public override IEnumerable<string> GetAllNamesProperties()
        {
            yield return this.TableName;
            yield return this.SchemaName;
        }
    }
}