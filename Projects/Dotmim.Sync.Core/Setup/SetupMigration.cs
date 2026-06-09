using Wormhole.Sync.Enumerations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;

namespace Wormhole.Sync
{

    /// <summary>
    /// Describes what kind of migration action to take.
    /// </summary>
    public enum MigrationAction
    {
        /// <summary>No action.</summary>
        None,

        /// <summary>Alter the object.</summary>
        Alter,

        /// <summary>Create the object.</summary>
        Create,

        /// <summary>Drop the object.</summary>
        Drop,

        /// <summary>Rename the object.</summary>
        Rename,
    }

    /// <summary>
    /// Results of comparing two schemas for migration.
    /// </summary>
    public class MigrationResults
    {
        /// <summary>
        /// Gets or Sets a value indicating that all tables should recreate their own stored procedures.
        /// </summary>
        public MigrationAction AllStoredProcedures { get; set; }

        /// <summary>
        /// Gets or Sets a value indicating that all tables should recreate their own triggers.
        /// </summary>
        public MigrationAction AllTriggers { get; set; }

        /// <summary>
        /// Gets or Sets a value indicating that all tables should recreate their tracking table.
        /// </summary>
        public MigrationAction AllTrackingTables { get; set; }

        /// <summary>
        /// Tables involved in the migration.
        /// </summary>
        public List<MigrationSetupTable> Tables { get; set; } = new List<MigrationSetupTable>();

        /// <summary>
        /// Gets a value indicating whether the migration contains only additive changes.
        /// Additive means: new nullable/default columns added to existing tables, or entirely new tables.
        /// </summary>
        public bool IsAdditiveOnly()
        {
            foreach (var table in Tables)
            {
                // Dropped tables are not additive
                if (table.Table == MigrationAction.Drop)
                    return false;

                // Dropped tracking tables, triggers, SPs are needed for reprovisioning but are still "additive"
                // since we recreate them with the new schema

                // If columns were removed, it's not additive
                if (table.RemovedColumns != null && table.RemovedColumns.Count > 0)
                    return false;

                // If columns were modified (type changed), it's not additive
                if (table.ModifiedColumns != null && table.ModifiedColumns.Count > 0)
                    return false;

                // Added columns must be nullable or have defaults
                if (table.AddedColumns != null)
                {
                    foreach (var col in table.AddedColumns)
                    {
                        if (!col.AllowDBNull && string.IsNullOrEmpty(col.DefaultValue))
                            return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Gets a value indicating whether there are any changes at all.
        /// </summary>
        public bool HasChanges => Tables.Count > 0;

        /// <summary>
        /// Gets all table names that have schema changes (new columns or are new tables).
        /// </summary>
        public IReadOnlyList<string> GetTablesWithSchemaChanges()
        {
            var result = new List<string>();
            foreach (var table in Tables)
            {
                if (table.Table == MigrationAction.Create ||
                    table.Table == MigrationAction.Alter ||
                    (table.AddedColumns != null && table.AddedColumns.Count > 0))
                {
                    var fullName = string.IsNullOrEmpty(table.SetupTable.SchemaName)
                       ? table.SetupTable.TableName
                       : $"{table.SetupTable.SchemaName}.{table.SetupTable.TableName}";
                    result.Add(fullName);
                }
            }

            return result;
        }
    }

    /// <summary>
    /// Represents a table that needs migration.
    /// </summary>
    public class MigrationSetupTable
    {
        private MigrationAction table;

        /// <inheritdoc cref="MigrationSetupTable"/>
        public MigrationSetupTable(SetupTable table)
        {
            this.SetupTable = table;
        }

        /// <summary>
        /// Table to migrate.
        /// </summary>
        public SetupTable SetupTable { get; set; }

        /// <summary>
        /// Gets or Sets a value indicating that this table should recreate the stored procedures.
        /// </summary>
        public MigrationAction StoredProcedures { get; set; }

        /// <summary>
        /// Gets or Sets a value indicating that this table should recreate triggers.
        /// </summary>
        public MigrationAction Triggers { get; set; }

        /// <summary>
        /// Gets or Sets a value indicating that this table should recreate the tracking table.
        /// </summary>
        public MigrationAction TrackingTable { get; set; }

        /// <summary>
        /// Gets a value indicating if the table should be migrated.
        /// </summary>
        public bool ShouldMigrate => this.TrackingTable != MigrationAction.None ||
                                     this.Triggers != MigrationAction.None ||
                                     this.StoredProcedures != MigrationAction.None ||
                                     this.Table != MigrationAction.None;

        /// <summary>
        /// Gets or Sets a value indicating that this table should be recreated.
        /// </summary>
        public MigrationAction Table
        {
            get => table;
            set
            {
                if (value == MigrationAction.Drop)
                    throw new Exception("Dropping tables during migration is not allowed.");

                table = value;
            }
        }

        /// <summary>
        /// Columns added to the table (new nullable/default columns).
        /// </summary>
        public List<MigrationColumnInfo> AddedColumns { get; set; }

        /// <summary>
        /// Columns removed from the table (not allowed for additive migrations).
        /// </summary>
        public List<string> RemovedColumns { get; set; }

        /// <summary>
        /// Columns whose type was modified (not allowed for additive migrations).
        /// </summary>
        public List<string> ModifiedColumns { get; set; }
    }

    /// <summary>
    /// Information about a column involved in a migration.
    /// </summary>
    public class MigrationColumnInfo
    {
        /// <summary>
        /// The column name.
        /// </summary>
        public string ColumnName { get; set; }

        /// <summary>
        /// Whether the column allows null values.
        /// </summary>
        public bool AllowDBNull { get; set; }

        /// <summary>
        /// The default value expression for the column.
        /// </summary>
        public string DefaultValue { get; set; }

        /// <summary>
        /// The column's data type.
        /// </summary>
        public string DataType { get; set; }
    }

    /// <summary>
    /// Computes migration diff between two ScopeInfo instances.
    /// </summary>
    public class Migration
    {
        private readonly ScopeInfo oldScopeInfo;
        private readonly ScopeInfo newScopeInfo;

        /// <inheritdoc cref="Migration"/>
        public Migration(ScopeInfo oldScopeInfo, ScopeInfo newScopeInfo)
        {
            this.oldScopeInfo = oldScopeInfo;
            this.newScopeInfo = newScopeInfo;
        }

        /// <summary>
        /// Compare old and new scopes and return a detailed migration result.
        /// </summary>
        public MigrationResults Compare()
        {
            if (oldScopeInfo?.Setup == null)
                throw new InvalidOperationException(
                    "Migration requires a non-null Setup on the old scope. " +
                    "Guard at the caller when comparing against a never-synced client.");

            var migrationSetup = new MigrationResults();
            var sc = SyncGlobalization.DataSourceStringComparison;

            if (newScopeInfo.Setup.EqualsByProperties(oldScopeInfo.Setup) &&
                string.Equals(newScopeInfo.Name, oldScopeInfo.Name, sc))
                return migrationSetup;

            // if we change the prefix / suffix, we should recreate all stored procedures
            if (!string.Equals(newScopeInfo.Setup.StoredProceduresPrefix, oldScopeInfo.Setup.StoredProceduresPrefix, sc)
                || !string.Equals(newScopeInfo.Setup.StoredProceduresSuffix, oldScopeInfo.Setup.StoredProceduresSuffix, sc)
                || !string.Equals(newScopeInfo.Name, oldScopeInfo.Name, sc))
                migrationSetup.AllStoredProcedures = MigrationAction.Create;

            // if we change the prefix / suffix, we should recreate all triggers
            if (!string.Equals(newScopeInfo.Setup.TriggersPrefix, oldScopeInfo.Setup.TriggersPrefix, sc) ||
                !string.Equals(newScopeInfo.Setup.TriggersSuffix, oldScopeInfo.Setup.TriggersSuffix, sc))
                migrationSetup.AllTriggers = MigrationAction.Create;

            // If we change tracking tables prefix and suffix
            if (!string.Equals(newScopeInfo.Setup.TrackingTablesPrefix, oldScopeInfo.Setup.TrackingTablesPrefix, sc) ||
                !string.Equals(newScopeInfo.Setup.TrackingTablesSuffix, oldScopeInfo.Setup.TrackingTablesSuffix, sc))
            {
                migrationSetup.AllStoredProcedures = MigrationAction.Create;
                migrationSetup.AllTriggers = MigrationAction.Create;
                migrationSetup.AllTrackingTables = MigrationAction.Rename;
            }

            // Search for deleted tables
            var deletedTables = oldScopeInfo.Setup.Tables
               .Where(oldt => newScopeInfo.Setup.Tables[oldt.TableName, oldt.SchemaName] == null);

            foreach (var deletedTable in deletedTables)
            {
                var migrationDeletedSetupTable = new MigrationSetupTable(deletedTable)
                {
                    StoredProcedures = MigrationAction.Drop,
                    TrackingTable = MigrationAction.Drop,
                    Triggers = MigrationAction.Drop,
                    Table = MigrationAction.None,
                };
                migrationSetup.Tables.Add(migrationDeletedSetupTable);
            }

            // Search for new tables
            var newTables = newScopeInfo.Setup.Tables
               .Where(newdt => oldScopeInfo.Setup.Tables[newdt.TableName, newdt.SchemaName] == null);

            foreach (var newTable in newTables)
            {
                var migrationAddedSetupTable = new MigrationSetupTable(newTable)
                {
                    StoredProcedures = MigrationAction.Create,
                    TrackingTable = MigrationAction.Create,
                    Triggers = MigrationAction.Create,
                    Table = MigrationAction.Create,
                };
                migrationSetup.Tables.Add(migrationAddedSetupTable);
            }

            // Compare existing tables
            foreach (var newTable in newScopeInfo.Setup.Tables)
            {
                var oldTable = oldScopeInfo.Setup.Tables[newTable.TableName, newTable.SchemaName];

                if (oldTable == null)
                    continue;

                var migrationSetupTable = new MigrationSetupTable(newTable);

                // Compare columns using schema-level detail
                var columnDiff = CompareTableColumns(oldTable, newTable);

                if (columnDiff.hasChanges)
                {
                    migrationSetupTable.StoredProcedures = MigrationAction.Create;
                    migrationSetupTable.TrackingTable = MigrationAction.None;
                    migrationSetupTable.Triggers = MigrationAction.Create;
                    migrationSetupTable.Table = MigrationAction.Alter;
                    migrationSetupTable.AddedColumns = columnDiff.addedColumns;
                    migrationSetupTable.RemovedColumns = columnDiff.removedColumns;
                    migrationSetupTable.ModifiedColumns = columnDiff.modifiedColumns;
                }
                else
                {
                    migrationSetupTable.StoredProcedures = migrationSetup.AllStoredProcedures;
                    migrationSetupTable.TrackingTable = migrationSetup.AllTrackingTables;
                    migrationSetupTable.Triggers = migrationSetup.AllTriggers;
                    migrationSetupTable.Table = MigrationAction.None;
                }

                if (migrationSetupTable.ShouldMigrate)
                    migrationSetup.Tables.Add(migrationSetupTable);
            }

            // Handle filters
            if (newScopeInfo.Setup.Filters != null && newScopeInfo.Setup.Filters.Count > 0)
            {
                foreach (var filter in newScopeInfo.Setup.Filters)
                {
                    var setupTable = newScopeInfo.Setup.Tables[filter.TableName, filter.SchemaName];

                    if (setupTable == null)
                        continue;

                    var migrationTable = migrationSetup.Tables.FirstOrDefault(ms => ms.SetupTable.EqualsByName(setupTable));

                    if (migrationTable == null)
                    {
                        migrationTable = new MigrationSetupTable(setupTable)
                        {
                            StoredProcedures = MigrationAction.Create,
                            Table = MigrationAction.None,
                            TrackingTable = MigrationAction.None,
                            Triggers = MigrationAction.None,
                        };
                        migrationSetup.Tables.Add(migrationTable);
                    }

                    migrationTable.StoredProcedures = MigrationAction.Create;
                }
            }

            return migrationSetup;
        }

        /// <summary>
        /// Compare columns between old and new setup tables using schema-level detail.
        /// </summary>
        public SchemaMigration CompareSchemaTables(SyncTable oldSchemaTable, SyncTable newSchemaTable)
        {
            var result = new SchemaMigration();
            var sc = SyncGlobalization.DataSourceStringComparison;

            if (oldSchemaTable == null || newSchemaTable == null)
                return result;

            // Find added columns
            foreach (var newCol in newSchemaTable.Columns)
            {
                var oldCol = oldSchemaTable.Columns[newCol.ColumnName];
                if (oldCol == null)
                {
                    result.AddedColumns.Add(new MigrationColumnInfo
                    {
                        ColumnName = newCol.ColumnName,
                        AllowDBNull = newCol.AllowDBNull,
                        DefaultValue = newCol.DefaultValue,
                        DataType = newCol.DataType,
                    });
                    result.HasChanges = true;
                }
            }

            // Find removed columns
            foreach (var oldCol in oldSchemaTable.Columns)
            {
                var newCol = newSchemaTable.Columns[oldCol.ColumnName];
                if (newCol == null)
                {
                    result.RemovedColumns.Add(oldCol.ColumnName);
                    result.HasChanges = true;
                }
            }

            return result;
        }

        private (bool hasChanges, List<MigrationColumnInfo> addedColumns, List<string> removedColumns, List<string> modifiedColumns)
           CompareTableColumns(SetupTable oldTable, SetupTable newTable)
        {
            var sc = SyncGlobalization.DataSourceStringComparison;
            var addedColumns = new List<MigrationColumnInfo>();
            var removedColumns = new List<string>();
            var modifiedColumns = new List<string>();

            ICollection<string> oldColumns = oldTable.Columns != null && oldTable.Columns.Count > 0
               ? (ICollection<string>)oldTable.Columns : Array.Empty<string>();
            ICollection<string> newColumns = newTable.Columns != null && newTable.Columns.Count > 0
               ? (ICollection<string>)newTable.Columns : Array.Empty<string>();

            // If both have no columns specified (means all columns), check by count won't work
            // Need schema-level comparison for full detail
            if (oldColumns.Count == 0 && newColumns.Count == 0)
                return (false, addedColumns, removedColumns, modifiedColumns);

            // Find added columns
            foreach (var newCol in newColumns)
            {
                if (!oldColumns.Any(oc => string.Equals(oc, newCol, sc)))
                {
                    addedColumns.Add(new MigrationColumnInfo
                    {
                        ColumnName = newCol,
                        AllowDBNull = true, // Assumed nullable at setup level
                    });
                }
            }

            // Find removed columns
            foreach (var oldCol in oldColumns)
            {
                if (!newColumns.Any(nc => string.Equals(nc, oldCol, sc)))
                {
                    removedColumns.Add(oldCol);
                }
            }

            var hasChanges = addedColumns.Count > 0 || removedColumns.Count > 0 || modifiedColumns.Count > 0;
            return (hasChanges, addedColumns, removedColumns, modifiedColumns);
        }
    }

    /// <summary>
    /// Schema-level migration results for comparing SyncTable column sets.
    /// </summary>
    public class SchemaMigration
    {
        /// <summary>
        /// Whether any changes were detected.
        /// </summary>
        public bool HasChanges { get; set; }

        /// <summary>
        /// Columns added in the new schema.
        /// </summary>
        public List<MigrationColumnInfo> AddedColumns { get; set; } = new List<MigrationColumnInfo>();

        /// <summary>
        /// Columns removed in the new schema.
        /// </summary>
        public List<string> RemovedColumns { get; set; } = new List<string>();
    }
}
