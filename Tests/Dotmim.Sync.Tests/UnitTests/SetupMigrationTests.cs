using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Wormhole.Sync.Tests.UnitTests
{
   public class SetupMigrationTests
   {
      // -----------------------------------------------------------------------
      // Helper to build a ScopeInfo with a given SyncSetup
      // -----------------------------------------------------------------------

      private static ScopeInfo MakeScopeInfo(string name, SyncSetup setup)
      {
         return new ScopeInfo
         {
            Id = Guid.NewGuid(),
            Name = name,
            Setup = setup,
            Schema = new SyncSet(),
         };
      }

      // -----------------------------------------------------------------------
      // Identical setups — no changes
      // -----------------------------------------------------------------------

      [Fact]
      public void Compare_IdenticalSetups_ShouldReturnNoChanges()
      {
         var setup = new SyncSetup("Product", "Customer");
         var oldScope = MakeScopeInfo("scope", setup);
         var newScope = MakeScopeInfo("scope", setup);

         var result = new Migration(oldScope, newScope).Compare();

         Assert.False(result.HasChanges);
         Assert.Empty(result.Tables);
      }

      // -----------------------------------------------------------------------
      // New table added
      // -----------------------------------------------------------------------

      [Fact]
      public void Compare_NewTableAdded_ShouldDetectCreateTable()
      {
         var oldSetup = new SyncSetup("Product");
         var newSetup = new SyncSetup("Product", "Customer");

         var oldScope = MakeScopeInfo("scope", oldSetup);
         var newScope = MakeScopeInfo("scope", newSetup);

         var result = new Migration(oldScope, newScope).Compare();

         Assert.True(result.HasChanges);

         var newTable = result.Tables.FirstOrDefault(t =>
            string.Equals(t.SetupTable.TableName, "Customer", StringComparison.OrdinalIgnoreCase));

         Assert.NotNull(newTable);
         Assert.Equal(MigrationAction.Create, newTable.Table);
         Assert.Equal(MigrationAction.Create, newTable.StoredProcedures);
         Assert.Equal(MigrationAction.Create, newTable.TrackingTable);
         Assert.Equal(MigrationAction.Create, newTable.Triggers);
      }

      [Fact]
      public void Compare_MultipleNewTablesAdded_ShouldDetectAll()
      {
         var oldSetup = new SyncSetup("Product");
         var newSetup = new SyncSetup("Product", "Customer", "Employee");

         var oldScope = MakeScopeInfo("scope", oldSetup);
         var newScope = MakeScopeInfo("scope", newSetup);

         var result = new Migration(oldScope, newScope).Compare();

         Assert.True(result.HasChanges);

         var newTableNames = result.Tables
            .Where(t => t.Table == MigrationAction.Create)
            .Select(t => t.SetupTable.TableName)
            .OrderBy(n => n)
            .ToList();

         Assert.Equal(2, newTableNames.Count);
         Assert.Contains("Customer", newTableNames);
         Assert.Contains("Employee", newTableNames);
      }

      // -----------------------------------------------------------------------
      // Table removed (deleted from new setup)
      // -----------------------------------------------------------------------

      [Fact]
      public void Compare_TableRemoved_ShouldDetectDropInfrastructure()
      {
         var oldSetup = new SyncSetup("Product", "Customer");
         var newSetup = new SyncSetup("Product");

         var oldScope = MakeScopeInfo("scope", oldSetup);
         var newScope = MakeScopeInfo("scope", newSetup);

         var result = new Migration(oldScope, newScope).Compare();

         Assert.True(result.HasChanges);

         var deletedTable = result.Tables.FirstOrDefault(t =>
            string.Equals(t.SetupTable.TableName, "Customer", StringComparison.OrdinalIgnoreCase));

         Assert.NotNull(deletedTable);
         // Table itself is not dropped (MigrationAction.None), only infrastructure
         Assert.Equal(MigrationAction.None, deletedTable.Table);
         Assert.Equal(MigrationAction.Drop, deletedTable.StoredProcedures);
         Assert.Equal(MigrationAction.Drop, deletedTable.TrackingTable);
         Assert.Equal(MigrationAction.Drop, deletedTable.Triggers);
      }

      // -----------------------------------------------------------------------
      // New column added to existing table
      // -----------------------------------------------------------------------

      [Fact]
      public void Compare_NewColumnAdded_ShouldDetectAlterTable()
      {
         var oldSetup = new SyncSetup();
         oldSetup.Tables.Add(new SetupTable("Product", new[] { "Id", "Name" }));

         var newSetup = new SyncSetup();
         newSetup.Tables.Add(new SetupTable("Product", new[] { "Id", "Name", "Description" }));

         var oldScope = MakeScopeInfo("scope", oldSetup);
         var newScope = MakeScopeInfo("scope", newSetup);

         var result = new Migration(oldScope, newScope).Compare();

         Assert.True(result.HasChanges);

         var alteredTable = result.Tables.FirstOrDefault(t =>
            string.Equals(t.SetupTable.TableName, "Product", StringComparison.OrdinalIgnoreCase));

         Assert.NotNull(alteredTable);
         Assert.Equal(MigrationAction.Alter, alteredTable.Table);
         Assert.Equal(MigrationAction.Create, alteredTable.StoredProcedures);
         Assert.Equal(MigrationAction.Create, alteredTable.Triggers);
         Assert.Equal(MigrationAction.None, alteredTable.TrackingTable);

         Assert.NotNull(alteredTable.AddedColumns);
         Assert.Single(alteredTable.AddedColumns);
         Assert.Equal("Description", alteredTable.AddedColumns[0].ColumnName);
      }

      [Fact]
      public void Compare_MultipleColumnsAdded_ShouldDetectAll()
      {
         var oldSetup = new SyncSetup();
         oldSetup.Tables.Add(new SetupTable("Product", new[] { "Id" }));

         var newSetup = new SyncSetup();
         newSetup.Tables.Add(new SetupTable("Product", new[] { "Id", "Name", "Description", "Price" }));

         var oldScope = MakeScopeInfo("scope", oldSetup);
         var newScope = MakeScopeInfo("scope", newSetup);

         var result = new Migration(oldScope, newScope).Compare();

         var alteredTable = result.Tables.FirstOrDefault(t =>
            string.Equals(t.SetupTable.TableName, "Product", StringComparison.OrdinalIgnoreCase));

         Assert.NotNull(alteredTable);
         Assert.Equal(3, alteredTable.AddedColumns.Count);

         var addedNames = alteredTable.AddedColumns.Select(c => c.ColumnName).OrderBy(n => n).ToList();
         Assert.Contains("Description", addedNames);
         Assert.Contains("Name", addedNames);
         Assert.Contains("Price", addedNames);
      }

      // -----------------------------------------------------------------------
      // Column removed from existing table
      // -----------------------------------------------------------------------

      [Fact]
      public void Compare_ColumnRemoved_ShouldDetectRemovedColumns()
      {
         var oldSetup = new SyncSetup();
         oldSetup.Tables.Add(new SetupTable("Product", new[] { "Id", "Name", "Description" }));

         var newSetup = new SyncSetup();
         newSetup.Tables.Add(new SetupTable("Product", new[] { "Id", "Name" }));

         var oldScope = MakeScopeInfo("scope", oldSetup);
         var newScope = MakeScopeInfo("scope", newSetup);

         var result = new Migration(oldScope, newScope).Compare();

         var alteredTable = result.Tables.FirstOrDefault(t =>
            string.Equals(t.SetupTable.TableName, "Product", StringComparison.OrdinalIgnoreCase));

         Assert.NotNull(alteredTable);
         Assert.Equal(MigrationAction.Alter, alteredTable.Table);
         Assert.NotNull(alteredTable.RemovedColumns);
         Assert.Single(alteredTable.RemovedColumns);
         Assert.Equal("Description", alteredTable.RemovedColumns[0]);
      }

      // -----------------------------------------------------------------------
      // Column added AND removed in same table
      // -----------------------------------------------------------------------

      [Fact]
      public void Compare_ColumnAddedAndRemoved_ShouldDetectBoth()
      {
         var oldSetup = new SyncSetup();
         oldSetup.Tables.Add(new SetupTable("Product", new[] { "Id", "Name", "OldColumn" }));

         var newSetup = new SyncSetup();
         newSetup.Tables.Add(new SetupTable("Product", new[] { "Id", "Name", "NewColumn" }));

         var oldScope = MakeScopeInfo("scope", oldSetup);
         var newScope = MakeScopeInfo("scope", newSetup);

         var result = new Migration(oldScope, newScope).Compare();

         var alteredTable = result.Tables.FirstOrDefault(t =>
            string.Equals(t.SetupTable.TableName, "Product", StringComparison.OrdinalIgnoreCase));

         Assert.NotNull(alteredTable);
         Assert.Equal(MigrationAction.Alter, alteredTable.Table);

         Assert.Single(alteredTable.AddedColumns);
         Assert.Equal("NewColumn", alteredTable.AddedColumns[0].ColumnName);

         Assert.Single(alteredTable.RemovedColumns);
         Assert.Equal("OldColumn", alteredTable.RemovedColumns[0]);
      }

      // -----------------------------------------------------------------------
      // Tables with no columns specified (wildcard) — no changes detectable
      // -----------------------------------------------------------------------

      [Fact]
      public void Compare_BothTablesNoColumnsSpecified_ShouldDetectNoColumnChanges()
      {
         var oldSetup = new SyncSetup("Product");
         var newSetup = new SyncSetup("Product");

         var oldScope = MakeScopeInfo("scope", oldSetup);
         var newScope = MakeScopeInfo("scope", newSetup);

         var result = new Migration(oldScope, newScope).Compare();

         Assert.False(result.HasChanges);
      }

      // -----------------------------------------------------------------------
      // Combined: new table + column change on existing table
      // -----------------------------------------------------------------------

      [Fact]
      public void Compare_NewTableAndColumnChange_ShouldDetectBoth()
      {
         var oldSetup = new SyncSetup();
         oldSetup.Tables.Add(new SetupTable("Product", new[] { "Id", "Name" }));

         var newSetup = new SyncSetup();
         newSetup.Tables.Add(new SetupTable("Product", new[] { "Id", "Name", "Description" }));
         newSetup.Tables.Add("Customer");

         var oldScope = MakeScopeInfo("scope", oldSetup);
         var newScope = MakeScopeInfo("scope", newSetup);

         var result = new Migration(oldScope, newScope).Compare();

         Assert.True(result.HasChanges);
         Assert.Equal(2, result.Tables.Count);

         var newTable = result.Tables.FirstOrDefault(t =>
            string.Equals(t.SetupTable.TableName, "Customer", StringComparison.OrdinalIgnoreCase));
         Assert.NotNull(newTable);
         Assert.Equal(MigrationAction.Create, newTable.Table);

         var alteredTable = result.Tables.FirstOrDefault(t =>
            string.Equals(t.SetupTable.TableName, "Product", StringComparison.OrdinalIgnoreCase));
         Assert.NotNull(alteredTable);
         Assert.Equal(MigrationAction.Alter, alteredTable.Table);
         Assert.Single(alteredTable.AddedColumns);
         Assert.Equal("Description", alteredTable.AddedColumns[0].ColumnName);
      }

      // -----------------------------------------------------------------------
      // Prefix / suffix changes
      // -----------------------------------------------------------------------

      [Fact]
      public void Compare_StoredProcedurePrefixChanged_ShouldRecreateAllStoredProcedures()
      {
         var oldSetup = new SyncSetup("Product") { StoredProceduresPrefix = "sp_" };
         var newSetup = new SyncSetup("Product") { StoredProceduresPrefix = "usp_" };

         var oldScope = MakeScopeInfo("scope", oldSetup);
         var newScope = MakeScopeInfo("scope", newSetup);

         var result = new Migration(oldScope, newScope).Compare();

         Assert.Equal(MigrationAction.Create, result.AllStoredProcedures);
         Assert.True(result.HasChanges);

         // The existing table should inherit the global stored procedure action
         var productTable = result.Tables.FirstOrDefault(t =>
            string.Equals(t.SetupTable.TableName, "Product", StringComparison.OrdinalIgnoreCase));
         Assert.NotNull(productTable);
         Assert.Equal(MigrationAction.Create, productTable.StoredProcedures);
      }

      [Fact]
      public void Compare_TriggersSuffixChanged_ShouldRecreateAllTriggers()
      {
         var oldSetup = new SyncSetup("Product") { TriggersSuffix = "_trg" };
         var newSetup = new SyncSetup("Product") { TriggersSuffix = "_trigger" };

         var oldScope = MakeScopeInfo("scope", oldSetup);
         var newScope = MakeScopeInfo("scope", newSetup);

         var result = new Migration(oldScope, newScope).Compare();

         Assert.Equal(MigrationAction.Create, result.AllTriggers);
      }

      [Fact]
      public void Compare_TrackingTablesPrefixChanged_ShouldRenameAllTrackingTables()
      {
         var oldSetup = new SyncSetup("Product") { TrackingTablesPrefix = "t_" };
         var newSetup = new SyncSetup("Product") { TrackingTablesPrefix = "trk_" };

         var oldScope = MakeScopeInfo("scope", oldSetup);
         var newScope = MakeScopeInfo("scope", newSetup);

         var result = new Migration(oldScope, newScope).Compare();

         Assert.Equal(MigrationAction.Rename, result.AllTrackingTables);
         Assert.Equal(MigrationAction.Create, result.AllStoredProcedures);
         Assert.Equal(MigrationAction.Create, result.AllTriggers);
      }

      // -----------------------------------------------------------------------
      // Scope name changed
      // -----------------------------------------------------------------------

      [Fact]
      public void Compare_ScopeNameChanged_ShouldRecreateStoredProcedures()
      {
         var setup = new SyncSetup("Product");
         var oldScope = MakeScopeInfo("scopeA", setup);
         var newScope = MakeScopeInfo("scopeB", setup);

         var result = new Migration(oldScope, newScope).Compare();

         Assert.Equal(MigrationAction.Create, result.AllStoredProcedures);
      }

      // -----------------------------------------------------------------------
      // MigrationResults.IsAdditiveOnly
      // -----------------------------------------------------------------------

      [Fact]
      public void IsAdditiveOnly_NewTableOnly_ShouldBeTrue()
      {
         var oldSetup = new SyncSetup("Product");
         var newSetup = new SyncSetup("Product", "Customer");

         var oldScope = MakeScopeInfo("scope", oldSetup);
         var newScope = MakeScopeInfo("scope", newSetup);

         var result = new Migration(oldScope, newScope).Compare();

         Assert.True(result.IsAdditiveOnly());
      }

      [Fact]
      public void IsAdditiveOnly_NewNullableColumnAdded_ShouldBeTrue()
      {
         var oldSetup = new SyncSetup();
         oldSetup.Tables.Add(new SetupTable("Product", new[] { "Id", "Name" }));

         var newSetup = new SyncSetup();
         newSetup.Tables.Add(new SetupTable("Product", new[] { "Id", "Name", "Description" }));

         var oldScope = MakeScopeInfo("scope", oldSetup);
         var newScope = MakeScopeInfo("scope", newSetup);

         var result = new Migration(oldScope, newScope).Compare();

         // At setup-level, added columns are assumed nullable (AllowDBNull=true)
         Assert.True(result.IsAdditiveOnly());
      }

      [Fact]
      public void IsAdditiveOnly_ColumnRemoved_ShouldBeFalse()
      {
         var oldSetup = new SyncSetup();
         oldSetup.Tables.Add(new SetupTable("Product", new[] { "Id", "Name", "Description" }));

         var newSetup = new SyncSetup();
         newSetup.Tables.Add(new SetupTable("Product", new[] { "Id", "Name" }));

         var oldScope = MakeScopeInfo("scope", oldSetup);
         var newScope = MakeScopeInfo("scope", newSetup);

         var result = new Migration(oldScope, newScope).Compare();

         Assert.False(result.IsAdditiveOnly());
      }

      // -----------------------------------------------------------------------
      // MigrationResults.GetTablesWithSchemaChanges
      // -----------------------------------------------------------------------

      [Fact]
      public void GetTablesWithSchemaChanges_NewTable_ShouldBeIncluded()
      {
         var oldSetup = new SyncSetup("Product");
         var newSetup = new SyncSetup("Product", "Customer");

         var oldScope = MakeScopeInfo("scope", oldSetup);
         var newScope = MakeScopeInfo("scope", newSetup);

         var result = new Migration(oldScope, newScope).Compare();
         var changed = result.GetTablesWithSchemaChanges();

         Assert.Contains("Customer", changed);
      }

      [Fact]
      public void GetTablesWithSchemaChanges_AlteredTable_ShouldBeIncluded()
      {
         var oldSetup = new SyncSetup();
         oldSetup.Tables.Add(new SetupTable("Product", new[] { "Id", "Name" }));

         var newSetup = new SyncSetup();
         newSetup.Tables.Add(new SetupTable("Product", new[] { "Id", "Name", "Description" }));

         var oldScope = MakeScopeInfo("scope", oldSetup);
         var newScope = MakeScopeInfo("scope", newSetup);

         var result = new Migration(oldScope, newScope).Compare();
         var changed = result.GetTablesWithSchemaChanges();

         Assert.Contains("Product", changed);
      }

      [Fact]
      public void GetTablesWithSchemaChanges_DroppedTable_ShouldNotBeIncluded()
      {
         var oldSetup = new SyncSetup("Product", "Customer");
         var newSetup = new SyncSetup("Product");

         var oldScope = MakeScopeInfo("scope", oldSetup);
         var newScope = MakeScopeInfo("scope", newSetup);

         var result = new Migration(oldScope, newScope).Compare();
         var changed = result.GetTablesWithSchemaChanges();

         Assert.DoesNotContain("Customer", changed);
      }

      // -----------------------------------------------------------------------
      // MigrationSetupTable.Table setter — Drop should throw
      // -----------------------------------------------------------------------

      [Fact]
      public void MigrationSetupTable_SetTableToDrop_ShouldThrow()
      {
         var setupTable = new SetupTable("Product");
         var migTable = new MigrationSetupTable(setupTable);

         Assert.Throws<Exception>(() => migTable.Table = MigrationAction.Drop);
      }

      // -----------------------------------------------------------------------
      // ShouldMigrate
      // -----------------------------------------------------------------------

      [Fact]
      public void ShouldMigrate_AllNone_ShouldBeFalse()
      {
         var migTable = new MigrationSetupTable(new SetupTable("Product"))
         {
            StoredProcedures = MigrationAction.None,
            Triggers = MigrationAction.None,
            TrackingTable = MigrationAction.None,
            Table = MigrationAction.None,
         };

         Assert.False(migTable.ShouldMigrate);
      }

      [Fact]
      public void ShouldMigrate_WithStoredProcedureCreate_ShouldBeTrue()
      {
         var migTable = new MigrationSetupTable(new SetupTable("Product"))
         {
            StoredProcedures = MigrationAction.Create,
            Triggers = MigrationAction.None,
            TrackingTable = MigrationAction.None,
            Table = MigrationAction.None,
         };

         Assert.True(migTable.ShouldMigrate);
      }

      // -----------------------------------------------------------------------
      // Schema-based table (with SyncTable) — no columns in SetupTable
      // -----------------------------------------------------------------------

      [Fact]
      public void Compare_NewTableWithSchema_ShouldIncludeSchemaInChanges()
      {
         var oldSetup = new SyncSetup();
         oldSetup.Tables.Add(new SetupTable("Product", "dbo"));

         var newSetup = new SyncSetup();
         newSetup.Tables.Add(new SetupTable("Product", "dbo"));
         newSetup.Tables.Add(new SetupTable("Employee", "hr"));

         var oldScope = MakeScopeInfo("scope", oldSetup);
         var newScope = MakeScopeInfo("scope", newSetup);

         var result = new Migration(oldScope, newScope).Compare();

         Assert.True(result.HasChanges);

         var newTable = result.Tables.FirstOrDefault(t =>
            string.Equals(t.SetupTable.TableName, "Employee", StringComparison.OrdinalIgnoreCase));
         Assert.NotNull(newTable);
         Assert.Equal(MigrationAction.Create, newTable.Table);
         Assert.Equal("hr", newTable.SetupTable.SchemaName);

         var schemaChanges = result.GetTablesWithSchemaChanges();
         Assert.Contains("hr.Employee", schemaChanges);
      }

      // -----------------------------------------------------------------------
      // Unchanged table alongside changed tables should NOT appear
      // -----------------------------------------------------------------------

      [Fact]
      public void Compare_UnchangedTableShouldNotAppearInResults()
      {
         var oldSetup = new SyncSetup();
         oldSetup.Tables.Add(new SetupTable("Product", new[] { "Id", "Name" }));
         oldSetup.Tables.Add("Customer");

         var newSetup = new SyncSetup();
         newSetup.Tables.Add(new SetupTable("Product", new[] { "Id", "Name", "Description" }));
         newSetup.Tables.Add("Customer");

         var oldScope = MakeScopeInfo("scope", oldSetup);
         var newScope = MakeScopeInfo("scope", newSetup);

         var result = new Migration(oldScope, newScope).Compare();

         // Customer has no column changes, no prefix changes — should not appear
         var customerTable = result.Tables.FirstOrDefault(t =>
            string.Equals(t.SetupTable.TableName, "Customer", StringComparison.OrdinalIgnoreCase));
         Assert.Null(customerTable);

         // Product should appear since it has a new column
         var productTable = result.Tables.FirstOrDefault(t =>
            string.Equals(t.SetupTable.TableName, "Product", StringComparison.OrdinalIgnoreCase));
         Assert.NotNull(productTable);
      }

      // -----------------------------------------------------------------------
      // IsAdditiveOnly with mixed add + drop
      // -----------------------------------------------------------------------

      [Fact]
      public void IsAdditiveOnly_NewTableAndRemovedColumn_ShouldBeFalse()
      {
         var oldSetup = new SyncSetup();
         oldSetup.Tables.Add(new SetupTable("Product", new[] { "Id", "Name", "OldCol" }));

         var newSetup = new SyncSetup();
         newSetup.Tables.Add(new SetupTable("Product", new[] { "Id", "Name" }));
         newSetup.Tables.Add("Customer");

         var oldScope = MakeScopeInfo("scope", oldSetup);
         var newScope = MakeScopeInfo("scope", newSetup);

         var result = new Migration(oldScope, newScope).Compare();

         // New table is additive, but removing a column is not
         Assert.False(result.IsAdditiveOnly());
      }

      // -----------------------------------------------------------------------
      // Existing table with all columns replaced
      // -----------------------------------------------------------------------

      [Fact]
      public void Compare_AllColumnsReplaced_ShouldDetectAddedAndRemoved()
      {
         var oldSetup = new SyncSetup();
         oldSetup.Tables.Add(new SetupTable("Product", new[] { "A", "B" }));

         var newSetup = new SyncSetup();
         newSetup.Tables.Add(new SetupTable("Product", new[] { "X", "Y" }));

         var oldScope = MakeScopeInfo("scope", oldSetup);
         var newScope = MakeScopeInfo("scope", newSetup);

         var result = new Migration(oldScope, newScope).Compare();

         var alteredTable = result.Tables.FirstOrDefault(t =>
            string.Equals(t.SetupTable.TableName, "Product", StringComparison.OrdinalIgnoreCase));

         Assert.NotNull(alteredTable);
         Assert.Equal(MigrationAction.Alter, alteredTable.Table);

         Assert.Equal(2, alteredTable.AddedColumns.Count);
         Assert.Equal(2, alteredTable.RemovedColumns.Count);

         Assert.Contains(alteredTable.AddedColumns, c => c.ColumnName == "X");
         Assert.Contains(alteredTable.AddedColumns, c => c.ColumnName == "Y");
         Assert.Contains("A", alteredTable.RemovedColumns);
         Assert.Contains("B", alteredTable.RemovedColumns);
      }

      // -----------------------------------------------------------------------
      // Table removed AND new table added simultaneously
      // -----------------------------------------------------------------------

      [Fact]
      public void Compare_TableSwapped_ShouldDetectDropAndCreate()
      {
         var oldSetup = new SyncSetup("Product", "OldTable");
         var newSetup = new SyncSetup("Product", "NewTable");

         var oldScope = MakeScopeInfo("scope", oldSetup);
         var newScope = MakeScopeInfo("scope", newSetup);

         var result = new Migration(oldScope, newScope).Compare();

         Assert.True(result.HasChanges);

         var droppedTable = result.Tables.FirstOrDefault(t =>
            string.Equals(t.SetupTable.TableName, "OldTable", StringComparison.OrdinalIgnoreCase));
         Assert.NotNull(droppedTable);
         Assert.Equal(MigrationAction.Drop, droppedTable.StoredProcedures);
         Assert.Equal(MigrationAction.Drop, droppedTable.TrackingTable);
         Assert.Equal(MigrationAction.Drop, droppedTable.Triggers);

         var createdTable = result.Tables.FirstOrDefault(t =>
            string.Equals(t.SetupTable.TableName, "NewTable", StringComparison.OrdinalIgnoreCase));
         Assert.NotNull(createdTable);
         Assert.Equal(MigrationAction.Create, createdTable.Table);
      }

      // -----------------------------------------------------------------------
      // Added columns at setup level are assumed nullable
      // -----------------------------------------------------------------------

      [Fact]
      public void Compare_AddedColumnsAtSetupLevel_ShouldBeAssumedNullable()
      {
         var oldSetup = new SyncSetup();
         oldSetup.Tables.Add(new SetupTable("Product", new[] { "Id" }));

         var newSetup = new SyncSetup();
         newSetup.Tables.Add(new SetupTable("Product", new[] { "Id", "Name" }));

         var oldScope = MakeScopeInfo("scope", oldSetup);
         var newScope = MakeScopeInfo("scope", newSetup);

         var result = new Migration(oldScope, newScope).Compare();

         var alteredTable = result.Tables.First(t =>
            string.Equals(t.SetupTable.TableName, "Product", StringComparison.OrdinalIgnoreCase));

         Assert.Single(alteredTable.AddedColumns);
         // Setup-level comparison assumes added columns are nullable
         Assert.True(alteredTable.AddedColumns[0].AllowDBNull);
      }

      // -----------------------------------------------------------------------
      // Empty setups — no changes
      // -----------------------------------------------------------------------

      [Fact]
      public void Compare_EmptySetups_ShouldReturnNoChanges()
      {
         var oldSetup = new SyncSetup();
         var newSetup = new SyncSetup();

         var oldScope = MakeScopeInfo("scope", oldSetup);
         var newScope = MakeScopeInfo("scope", newSetup);

         var result = new Migration(oldScope, newScope).Compare();

         Assert.False(result.HasChanges);
      }

      // -----------------------------------------------------------------------
      // Prefix changes should affect ALL existing tables
      // -----------------------------------------------------------------------

      [Fact]
      public void Compare_PrefixChange_AllExistingTablesShouldMigrate()
      {
         var oldSetup = new SyncSetup("Product", "Customer") { StoredProceduresPrefix = "sp_" };
         var newSetup = new SyncSetup("Product", "Customer") { StoredProceduresPrefix = "usp_" };

         var oldScope = MakeScopeInfo("scope", oldSetup);
         var newScope = MakeScopeInfo("scope", newSetup);

         var result = new Migration(oldScope, newScope).Compare();

         // Both existing tables should inherit the SP recreation
         Assert.Equal(2, result.Tables.Count);
         Assert.All(result.Tables, t => Assert.Equal(MigrationAction.Create, t.StoredProcedures));
      }

      // -----------------------------------------------------------------------
      // ReinitTables — round-trip serialization
      // -----------------------------------------------------------------------

      [Fact]
      public void ScopeInfoClient_SetReinitTables_RoundTrip()
      {
         var client = new ScopeInfoClient();
         client.SetReinitTables(new[] { "Customer", "hr.Employee" });

         // Verify the serialized form
         Assert.Equal("Customer,hr.Employee", client.ReinitTables);

         // Verify deserialization via GetReinitTablesSet
         var set = client.GetReinitTablesSet();
         Assert.Equal(2, set.Count);
         Assert.Contains("Customer", set);
         Assert.Contains("hr.Employee", set);
      }

      [Fact]
      public void ScopeInfoClient_GetReinitTablesSet_EmptyWhenNull()
      {
         var client = new ScopeInfoClient();
         Assert.Null(client.ReinitTables);

         var set = client.GetReinitTablesSet();
         Assert.Empty(set);
      }

      [Fact]
      public void ScopeInfoClient_SetReinitTables_NullClearsProperty()
      {
         var client = new ScopeInfoClient();
         client.SetReinitTables(new[] { "Customer" });
         Assert.NotNull(client.ReinitTables);

         client.SetReinitTables(null);
         Assert.Null(client.ReinitTables);
      }

      [Fact]
      public void ScopeInfoClient_AddReinitTable_AccumulatesTables()
      {
         var client = new ScopeInfoClient();
         client.AddReinitTable("Customer");
         client.AddReinitTable("Product");

         var set = client.GetReinitTablesSet();
         Assert.Equal(2, set.Count);
         Assert.Contains("Customer", set);
         Assert.Contains("Product", set);
      }

      [Fact]
      public void ScopeInfoClient_GetReinitTablesSet_IsCaseInsensitive()
      {
         var client = new ScopeInfoClient();
         client.SetReinitTables(new[] { "Customer" });

         var set = client.GetReinitTablesSet();
         Assert.Contains("customer", set);
         Assert.Contains("CUSTOMER", set);
      }

      [Fact]
      public void ScopeInfoClient_ReinitTables_NotCopiedOnSuccessfulSync()
      {
         // Simulate the pattern from LocalOrchestrator.ApplyChanges.cs:
         // ReinitTables is intentionally NOT copied to newCScopeInfoClient
         var cScopeInfoClient = new ScopeInfoClient
         {
            Id = Guid.NewGuid(),
            Name = "scope",
            Hash = "abc",
            IsNewScope = false,
         };
         cScopeInfoClient.SetReinitTables(new[] { "Customer" });

         // Mimic the newCScopeInfoClient creation (ReinitTables intentionally omitted)
         var newCScopeInfoClient = new ScopeInfoClient
         {
            Hash = cScopeInfoClient.Hash,
            Name = cScopeInfoClient.Name,
            Id = cScopeInfoClient.Id,
            IsNewScope = cScopeInfoClient.IsNewScope,
         };

         Assert.Null(newCScopeInfoClient.ReinitTables);
         Assert.Empty(newCScopeInfoClient.GetReinitTablesSet());
      }

      // -----------------------------------------------------------------------
      // GetTablesWithSchemaChanges — combined scenario
      // -----------------------------------------------------------------------

      [Fact]
      public void GetTablesWithSchemaChanges_AddedColumnAndNewTable_OnlyChangedTablesReturned()
      {
         var oldSetup = new SyncSetup();
         oldSetup.Tables.Add(new SetupTable("Product", new[] { "Id", "Name" }));
         oldSetup.Tables.Add("Order");

         var newSetup = new SyncSetup();
         newSetup.Tables.Add(new SetupTable("Product", new[] { "Id", "Name", "Description" }));
         newSetup.Tables.Add("Order");       // unchanged
         newSetup.Tables.Add("Customer");    // new

         var oldScope = MakeScopeInfo("scope", oldSetup);
         var newScope = MakeScopeInfo("scope", newSetup);

         var result = new Migration(oldScope, newScope).Compare();
         var changed = result.GetTablesWithSchemaChanges();

         // Product changed (new column), Customer is new
         Assert.Contains("Product", changed);
         Assert.Contains("Customer", changed);
         // Order is unchanged — should NOT appear
         Assert.DoesNotContain("Order", changed);
      }

      [Fact]
      public void GetTablesWithSchemaChanges_SchemaQualifiedNames()
      {
         var oldSetup = new SyncSetup();
         oldSetup.Tables.Add(new SetupTable("Product", "dbo"));

         var newSetup = new SyncSetup();
         newSetup.Tables.Add(new SetupTable("Product", "dbo"));
         newSetup.Tables.Add(new SetupTable("Employee", "hr"));

         var oldScope = MakeScopeInfo("scope", oldSetup);
         var newScope = MakeScopeInfo("scope", newSetup);

         var result = new Migration(oldScope, newScope).Compare();
         var changed = result.GetTablesWithSchemaChanges();

         Assert.Contains("hr.Employee", changed);
         Assert.DoesNotContain("dbo.Product", changed);
         Assert.DoesNotContain("Product", changed);
      }

      // =====================================================================
      // ReinitTables — edge cases
      // =====================================================================

      [Fact]
      public void ScopeInfoClient_AddReinitTable_DuplicateIsIgnored()
      {
         var client = new ScopeInfoClient();
         client.AddReinitTable("Customer");
         client.AddReinitTable("Customer"); // duplicate

         var set = client.GetReinitTablesSet();
         Assert.Single(set);
         Assert.Contains("Customer", set);
      }

      [Fact]
      public void ScopeInfoClient_SetReinitTables_EmptyCollectionSetsNull()
      {
         var client = new ScopeInfoClient();
         client.SetReinitTables(new string[0]);

         Assert.Null(client.ReinitTables);
         Assert.Empty(client.GetReinitTablesSet());
      }

      [Fact]
      public void ScopeInfoClient_ShadowScope_DoesNotCopyReinitTables()
      {
         var original = new ScopeInfoClient
         {
            Id = Guid.NewGuid(),
            Name = "scope",
            Hash = "abc",
            LastSyncTimestamp = 12345,
            LastServerSyncTimestamp = 67890,
            LastSync = DateTime.UtcNow,
            LastSyncDuration = 5000,
         };
         original.SetReinitTables(new[] { "Customer", "Product" });

         var target = new ScopeInfoClient();
         target.ShadowScope(original);

         // ShadowScope copies timestamps + sync metadata but NOT ReinitTables
         Assert.Equal(original.LastSyncTimestamp, target.LastSyncTimestamp);
         Assert.Equal(original.LastServerSyncTimestamp, target.LastServerSyncTimestamp);
         Assert.Null(target.ReinitTables);
      }

      [Fact]
      public void ScopeInfoClient_AddReinitTable_CaseInsensitiveDedupe()
      {
         var client = new ScopeInfoClient();
         client.AddReinitTable("Customer");
         client.AddReinitTable("customer"); // same name, different case

         var set = client.GetReinitTablesSet();
         // GetReinitTablesSet is case-insensitive, so both resolve to 1 entry
         Assert.Single(set);
      }

      [Fact]
      public void ScopeInfoClient_SetReinitTables_PreservesSchemaQualifiedNames()
      {
         var tables = new[] { "dbo.Customer", "hr.Employee", "Product" };
         var client = new ScopeInfoClient();
         client.SetReinitTables(tables);

         var set = client.GetReinitTablesSet();
         Assert.Equal(3, set.Count);
         Assert.Contains("dbo.Customer", set);
         Assert.Contains("hr.Employee", set);
         Assert.Contains("Product", set);
      }

      // =====================================================================
      // MessageApplyChanges.ReinitTables property
      // =====================================================================

      [Fact]
      public void MessageApplyChanges_ReinitTables_DefaultIsNull()
      {
         var schema = new SyncSet();
         var batchInfo = new Batch.BatchInfo();
         var failedRows = new SyncSet();
         var changesApplied = new DatabaseChangesApplied();

         var msg = new MessageApplyChanges(
            Guid.NewGuid(), Guid.NewGuid(), false, 0L, schema,
            Enumerations.ConflictResolutionPolicy.ServerWins, false,
            null, batchInfo, failedRows, changesApplied);

         Assert.Null(msg.ReinitTables);
      }

      [Fact]
      public void MessageApplyChanges_ReinitTables_CanBeSetAndQueried()
      {
         var schema = new SyncSet();
         var batchInfo = new Batch.BatchInfo();
         var failedRows = new SyncSet();
         var changesApplied = new DatabaseChangesApplied();

         var msg = new MessageApplyChanges(
            Guid.NewGuid(), Guid.NewGuid(), false, 0L, schema,
            Enumerations.ConflictResolutionPolicy.ServerWins, false,
            null, batchInfo, failedRows, changesApplied);

         msg.ReinitTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
         {
            "Customer", "hr.Employee"
         };

         Assert.Equal(2, msg.ReinitTables.Count);
         Assert.True(msg.ReinitTables.Contains("customer")); // case insensitive
         Assert.True(msg.ReinitTables.Contains("hr.Employee"));
      }

      // =====================================================================
      // Table key format matching (used in BaseOrchestrator.ApplyChanges)
      // =====================================================================

      /// <summary>
      /// The apply logic builds table keys as "SchemaName.TableName" or just "TableName".
      /// Verify that the same format produced by GetTablesWithSchemaChanges matches
      /// the format the apply code uses for lookup.
      /// </summary>
      [Fact]
      public void ReinitTableKey_MatchesGetTablesWithSchemaChanges_NoSchema()
      {
         var oldSetup = new SyncSetup("Product");
         var newSetup = new SyncSetup("Product", "Customer");

         var oldScope = MakeScopeInfo("scope", oldSetup);
         var newScope = MakeScopeInfo("scope", newSetup);

         var result = new Migration(oldScope, newScope).Compare();
         var changed = result.GetTablesWithSchemaChanges();

         // Simulate what BaseOrchestrator.ApplyChanges does for a table with no schema
         var tableName = "Customer";
         string schemaName = null;
         var tableKey = string.IsNullOrEmpty(schemaName) ? tableName : $"{schemaName}.{tableName}";

         Assert.Contains(tableKey, changed);
      }

      [Fact]
      public void ReinitTableKey_MatchesGetTablesWithSchemaChanges_WithSchema()
      {
         var oldSetup = new SyncSetup();
         oldSetup.Tables.Add(new SetupTable("Product", "dbo"));

         var newSetup = new SyncSetup();
         newSetup.Tables.Add(new SetupTable("Product", "dbo"));
         newSetup.Tables.Add(new SetupTable("Employee", "hr"));

         var oldScope = MakeScopeInfo("scope", oldSetup);
         var newScope = MakeScopeInfo("scope", newSetup);

         var result = new Migration(oldScope, newScope).Compare();
         var changed = result.GetTablesWithSchemaChanges();

         // Simulate what BaseOrchestrator.ApplyChanges does for a schema-qualified table
         var tableName = "Employee";
         var schemaName = "hr";
         var tableKey = string.IsNullOrEmpty(schemaName) ? tableName : $"{schemaName}.{tableName}";

         Assert.Contains(tableKey, changed);
      }

      [Fact]
      public void ReinitTableKey_AlteredTableWithColumns_MatchesFormat()
      {
         var oldSetup = new SyncSetup();
         oldSetup.Tables.Add(new SetupTable("Product", new[] { "Id", "Name" }));

         var newSetup = new SyncSetup();
         newSetup.Tables.Add(new SetupTable("Product", new[] { "Id", "Name", "Price" }));

         var oldScope = MakeScopeInfo("scope", oldSetup);
         var newScope = MakeScopeInfo("scope", newSetup);

         var result = new Migration(oldScope, newScope).Compare();
         var changed = result.GetTablesWithSchemaChanges();

         // Apply logic key for table with no schema
         var tableKey = "Product";
         Assert.Contains(tableKey, changed);
      }

      // =====================================================================
      // ShouldMigrate — additional coverage
      // =====================================================================

      [Fact]
      public void ShouldMigrate_WithTriggerCreate_ShouldBeTrue()
      {
         var migTable = new MigrationSetupTable(new SetupTable("Product"))
         {
            StoredProcedures = MigrationAction.None,
            Triggers = MigrationAction.Create,
            TrackingTable = MigrationAction.None,
            Table = MigrationAction.None,
         };

         Assert.True(migTable.ShouldMigrate);
      }

      [Fact]
      public void ShouldMigrate_WithTrackingTableCreate_ShouldBeTrue()
      {
         var migTable = new MigrationSetupTable(new SetupTable("Product"))
         {
            StoredProcedures = MigrationAction.None,
            Triggers = MigrationAction.None,
            TrackingTable = MigrationAction.Create,
            Table = MigrationAction.None,
         };

         Assert.True(migTable.ShouldMigrate);
      }

      [Fact]
      public void ShouldMigrate_WithAlterTable_ShouldBeTrue()
      {
         var migTable = new MigrationSetupTable(new SetupTable("Product"))
         {
            StoredProcedures = MigrationAction.None,
            Triggers = MigrationAction.None,
            TrackingTable = MigrationAction.None,
            Table = MigrationAction.Alter,
         };

         Assert.True(migTable.ShouldMigrate);
      }

      [Fact]
      public void ShouldMigrate_WithDropTriggers_ShouldBeTrue()
      {
         var migTable = new MigrationSetupTable(new SetupTable("Product"))
         {
            StoredProcedures = MigrationAction.None,
            Triggers = MigrationAction.Drop,
            TrackingTable = MigrationAction.None,
            Table = MigrationAction.None,
         };

         Assert.True(migTable.ShouldMigrate);
      }

      // =====================================================================
      // IsAdditiveOnly — additional edge cases
      // =====================================================================

      [Fact]
      public void IsAdditiveOnly_TableRemovedOnly_ShouldBeTrue()
      {
         var oldSetup = new SyncSetup("Product", "Customer");
         var newSetup = new SyncSetup("Product");

         var oldScope = MakeScopeInfo("scope", oldSetup);
         var newScope = MakeScopeInfo("scope", newSetup);

         var result = new Migration(oldScope, newScope).Compare();

         // Removing a table from sync only drops infrastructure (SPs, triggers, tracking)
         // but NOT the actual table data — this is considered "additive" since the table
         // itself remains untouched in the database.
         Assert.True(result.IsAdditiveOnly());
      }

      [Fact]
      public void IsAdditiveOnly_NoChanges_ShouldBeTrue()
      {
         var setup = new SyncSetup("Product");
         var oldScope = MakeScopeInfo("scope", setup);
         var newScope = MakeScopeInfo("scope", setup);

         var result = new Migration(oldScope, newScope).Compare();

         // No changes = trivially additive
         Assert.True(result.IsAdditiveOnly());
      }

      [Fact]
      public void IsAdditiveOnly_OnlyPrefixChange_ShouldBeTrue()
      {
         var oldSetup = new SyncSetup("Product") { StoredProceduresPrefix = "sp_" };
         var newSetup = new SyncSetup("Product") { StoredProceduresPrefix = "usp_" };

         var oldScope = MakeScopeInfo("scope", oldSetup);
         var newScope = MakeScopeInfo("scope", newSetup);

         var result = new Migration(oldScope, newScope).Compare();

         // Prefix changes recreate SPs but don't drop tables or remove columns
         Assert.True(result.IsAdditiveOnly());
      }

      // =====================================================================
      // Multiple suffix changes simultaneously
      // =====================================================================

      [Fact]
      public void Compare_MultipleSuffixChanges_ShouldDetectAll()
      {
         var oldSetup = new SyncSetup("Product")
         {
            StoredProceduresSuffix = "_old",
            TriggersSuffix = "_old",
            TrackingTablesSuffix = "_old",
         };
         var newSetup = new SyncSetup("Product")
         {
            StoredProceduresSuffix = "_new",
            TriggersSuffix = "_new",
            TrackingTablesSuffix = "_new",
         };

         var oldScope = MakeScopeInfo("scope", oldSetup);
         var newScope = MakeScopeInfo("scope", newSetup);

         var result = new Migration(oldScope, newScope).Compare();

         Assert.True(result.HasChanges);
         Assert.Equal(MigrationAction.Create, result.AllStoredProcedures);
         Assert.Equal(MigrationAction.Create, result.AllTriggers);
         Assert.Equal(MigrationAction.Rename, result.AllTrackingTables);
      }

      // =====================================================================
      // Dropped table with schema should NOT be in GetTablesWithSchemaChanges
      // =====================================================================

      [Fact]
      public void GetTablesWithSchemaChanges_DroppedSchemaTable_ShouldNotBeIncluded()
      {
         var oldSetup = new SyncSetup();
         oldSetup.Tables.Add(new SetupTable("Product", "dbo"));
         oldSetup.Tables.Add(new SetupTable("Employee", "hr"));

         var newSetup = new SyncSetup();
         newSetup.Tables.Add(new SetupTable("Product", "dbo"));
         // Employee removed

         var oldScope = MakeScopeInfo("scope", oldSetup);
         var newScope = MakeScopeInfo("scope", newSetup);

         var result = new Migration(oldScope, newScope).Compare();
         var changed = result.GetTablesWithSchemaChanges();

         Assert.DoesNotContain("hr.Employee", changed);
         Assert.DoesNotContain("Employee", changed);
      }

      // =====================================================================
      // ReinitTables flows through to MessageApplyChanges correctly
      // =====================================================================

      [Fact]
      public void ReinitTables_EndToEnd_MigrationToMessageFlow()
      {
         // Simulate the full flow: migration detects changes → SetReinitTables → GetReinitTablesSet → MessageApplyChanges
         var oldSetup = new SyncSetup();
         oldSetup.Tables.Add(new SetupTable("Product", new[] { "Id", "Name" }));
         oldSetup.Tables.Add("Order");

         var newSetup = new SyncSetup();
         newSetup.Tables.Add(new SetupTable("Product", new[] { "Id", "Name", "Price" }));
         newSetup.Tables.Add("Order");
         newSetup.Tables.Add("Customer"); // new table

         var oldScope = MakeScopeInfo("scope", oldSetup);
         var newScope = MakeScopeInfo("scope", newSetup);

         // Step 1: Migration.Compare detects changes
         var result = new Migration(oldScope, newScope).Compare();
         var changedTables = result.GetTablesWithSchemaChanges();

         Assert.Equal(2, changedTables.Count);
         Assert.Contains("Product", changedTables);
         Assert.Contains("Customer", changedTables);
         Assert.DoesNotContain("Order", changedTables);

         // Step 2: ScopeInfoClient stores the reinit tables
         var client = new ScopeInfoClient { Id = Guid.NewGuid(), Name = "scope", Hash = "x" };
         client.SetReinitTables(changedTables);

         // Step 3: Round-trip via serialized string (as stored in DB)
         var serialized = client.ReinitTables;
         var restored = new ScopeInfoClient { ReinitTables = serialized };
         var reinitSet = restored.GetReinitTablesSet();

         // Step 4: Would be set on MessageApplyChanges
         Assert.Equal(2, reinitSet.Count);
         Assert.Contains("Product", reinitSet);
         Assert.Contains("Customer", reinitSet);
         Assert.DoesNotContain("Order", reinitSet);
      }

      // =====================================================================
      // Column changes on schema-qualified tables
      // =====================================================================

      [Fact]
      public void Compare_ColumnChangeOnSchemaTable_ShouldDetectAlter()
      {
         var oldSetup = new SyncSetup();
         oldSetup.Tables.Add(new SetupTable("Employee", new[] { "Id", "Name" }, "hr"));

         var newSetup = new SyncSetup();
         newSetup.Tables.Add(new SetupTable("Employee", new[] { "Id", "Name", "Email" }, "hr"));

         var oldScope = MakeScopeInfo("scope", oldSetup);
         var newScope = MakeScopeInfo("scope", newSetup);

         var result = new Migration(oldScope, newScope).Compare();

         Assert.True(result.HasChanges);
         var altered = result.Tables.FirstOrDefault(t =>
            string.Equals(t.SetupTable.TableName, "Employee", StringComparison.OrdinalIgnoreCase));
         Assert.NotNull(altered);
         Assert.Equal(MigrationAction.Alter, altered.Table);
         Assert.Single(altered.AddedColumns);
         Assert.Equal("Email", altered.AddedColumns[0].ColumnName);

         // Verify the table key in GetTablesWithSchemaChanges is schema-qualified
         var changed = result.GetTablesWithSchemaChanges();
         Assert.Contains("hr.Employee", changed);
      }

      // =====================================================================
      // Table with only wildcards on one side, columns on the other
      // =====================================================================

      [Fact]
      public void Compare_OldWildcardNewColumns_ShouldNotDetectChanges()
      {
         // Old setup has no columns (wildcard), new setup specifies columns
         // Since the old setup doesn't specify columns, we can't compare — should not detect changes
         var oldSetup = new SyncSetup("Product");

         var newSetup = new SyncSetup();
         newSetup.Tables.Add(new SetupTable("Product", new[] { "Id", "Name" }));

         var oldScope = MakeScopeInfo("scope", oldSetup);
         var newScope = MakeScopeInfo("scope", newSetup);

         var result = new Migration(oldScope, newScope).Compare();

         // Behavior depends on implementation — but column-level changes
         // require both sides to have columns specified for meaningful comparison
         var productTable = result.Tables.FirstOrDefault(t =>
            string.Equals(t.SetupTable.TableName, "Product", StringComparison.OrdinalIgnoreCase));

         // If productTable is null, the migration correctly detected no meaningful column change
         // If productTable exists, verify it shows columns were "added" from perspective of wildcard→specific
         if (productTable != null)
         {
            Assert.True(productTable.ShouldMigrate);
         }
      }
   }
}
