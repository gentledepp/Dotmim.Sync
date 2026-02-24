using System;
using System.Linq;
using Wormhole.Sync.Web.Server;
using Xunit;

namespace Wormhole.Sync.Tests.UnitTests
{
   public class SyncMigrationOptionsTests
   {
      // -----------------------------------------------------------------------
      // SyncMigration constructor tests
      // -----------------------------------------------------------------------

      [Fact]
      public void SyncMigration_Constructor_ShouldSetProperties()
      {
         var setup = new SyncSetup("Product");
         var migration = new SyncMigration("20260101_initial", setup);

         Assert.Equal("20260101_initial", migration.MigrationId);
         Assert.Same(setup, migration.Setup);
      }

      [Fact]
      public void SyncMigration_Constructor_NullMigrationId_ShouldThrow()
      {
         var setup = new SyncSetup("Product");
         Assert.Throws<ArgumentNullException>(() => new SyncMigration(null, setup));
      }

      [Fact]
      public void SyncMigration_Constructor_EmptyMigrationId_ShouldBeAllowed()
      {
         var setup = new SyncSetup("Product");
         var migration = new SyncMigration("", setup);

         Assert.Equal("", migration.MigrationId);
         Assert.Same(setup, migration.Setup);
      }

      [Fact]
      public void SyncMigration_Constructor_NullSetup_ShouldThrow()
      {
         Assert.Throws<ArgumentNullException>(() => new SyncMigration("mig1", null));
      }

      // -----------------------------------------------------------------------
      // SyncMigrationOptions.AddMigration tests
      // -----------------------------------------------------------------------

      [Fact]
      public void AddMigration_ShouldAddMigration()
      {
         var options = new SyncMigrationOptions();
         var setup = new SyncSetup("Product");

         var result = options.AddMigration("mig1", setup);

         Assert.Same(options, result); // Fluent API returns self
         Assert.Single(options.GetMigrations());
         Assert.Equal("mig1", options.GetMigrations()[0].MigrationId);
      }

      [Fact]
      public void AddMigration_NullMigrationId_ShouldThrow()
      {
         var options = new SyncMigrationOptions();
         Assert.Throws<ArgumentNullException>(() => options.AddMigration(null, new SyncSetup("Product")));
      }

      [Fact]
      public void AddMigration_NullSetup_ShouldThrow()
      {
         var options = new SyncMigrationOptions();
         Assert.Throws<ArgumentNullException>(() => options.AddMigration("mig1", null));
      }

      [Fact]
      public void AddMigration_DuplicateId_ShouldThrow()
      {
         var options = new SyncMigrationOptions();
         options.AddMigration("mig1", new SyncSetup("Product"));

         Assert.Throws<ArgumentException>(() => options.AddMigration("mig1", new SyncSetup("ProductCategory")));
      }

      [Fact]
      public void AddMigration_DuplicateId_CaseSensitive()
      {
         var options = new SyncMigrationOptions();
         options.AddMigration("Mig1", new SyncSetup("Product"));

         // Different case — should succeed (ordinal comparison)
         options.AddMigration("mig1", new SyncSetup("ProductCategory"));

         Assert.Equal(2, options.GetMigrations().Count);
      }

      // -----------------------------------------------------------------------
      // AddInitialMigration tests
      // -----------------------------------------------------------------------

      [Fact]
      public void AddInitialMigration_ShouldUseEmptyStringId()
      {
         var options = new SyncMigrationOptions();
         var setup = new SyncSetup("Product");

         var result = options.AddInitialMigration(setup);

         Assert.Same(options, result); // Fluent API returns self
         Assert.Single(options.GetMigrations());
         Assert.Equal("", options.GetMigrations()[0].MigrationId);
      }

      [Fact]
      public void AddInitialMigration_ShouldAlwaysSortFirst()
      {
         var setupInitial = new SyncSetup("Product");
         var setupV2 = new SyncSetup("Product", "ProductCategory");
         var setupV3 = new SyncSetup("Product", "ProductCategory", "Employee");

         var options = new SyncMigrationOptions()
            .AddMigration("20260301_v3", setupV3)
            .AddInitialMigration(setupInitial)
            .AddMigration("20260201_v2", setupV2);

         var migrations = options.GetMigrations();

         Assert.Equal(3, migrations.Count);
         Assert.Equal("", migrations[0].MigrationId);
         Assert.Same(setupInitial, migrations[0].Setup);
         Assert.Equal("20260201_v2", migrations[1].MigrationId);
         Assert.Equal("20260301_v3", migrations[2].MigrationId);
      }

      [Fact]
      public void AddInitialMigration_CalledTwice_ShouldThrow()
      {
         var options = new SyncMigrationOptions();
         options.AddInitialMigration(new SyncSetup("Product"));

         Assert.Throws<ArgumentException>(() => options.AddInitialMigration(new SyncSetup("ProductCategory")));
      }

      [Fact]
      public void AddInitialMigration_NullSetup_ShouldThrow()
      {
         var options = new SyncMigrationOptions();
         Assert.Throws<ArgumentNullException>(() => options.AddInitialMigration(null));
      }

      [Fact]
      public void AddInitialMigration_FluentChaining_ShouldWork()
      {
         var setupV1 = new SyncSetup("Product");
         var setupV2 = new SyncSetup("Product", "ProductCategory");

         var options = new SyncMigrationOptions()
            .AddInitialMigration(setupV1)
            .AddMigration("20260201_v2", setupV2);

         Assert.Equal(2, options.GetMigrations().Count);
         Assert.Equal("", options.GetMigrations()[0].MigrationId);
         Assert.Same(setupV2, options.CurrentSetup);
      }

      // -----------------------------------------------------------------------
      // GetMigrations sorting tests
      // -----------------------------------------------------------------------

      [Fact]
      public void GetMigrations_ShouldReturnSortedByMigrationId()
      {
         var options = new SyncMigrationOptions();
         var setupC = new SyncSetup("TableC");
         var setupA = new SyncSetup("TableA");
         var setupB = new SyncSetup("TableB");

         // Add in non-sorted order
         options.AddMigration("20260301_c", setupC);
         options.AddMigration("20260101_a", setupA);
         options.AddMigration("20260201_b", setupB);

         var migrations = options.GetMigrations();

         Assert.Equal(3, migrations.Count);
         Assert.Equal("20260101_a", migrations[0].MigrationId);
         Assert.Equal("20260201_b", migrations[1].MigrationId);
         Assert.Equal("20260301_c", migrations[2].MigrationId);
         Assert.Same(setupA, migrations[0].Setup);
         Assert.Same(setupB, migrations[1].Setup);
         Assert.Same(setupC, migrations[2].Setup);
      }

      [Fact]
      public void GetMigrations_EmptyOptions_ShouldReturnEmptyList()
      {
         var options = new SyncMigrationOptions();
         var migrations = options.GetMigrations();

         Assert.NotNull(migrations);
         Assert.Empty(migrations);
      }

      [Fact]
      public void GetMigrations_SingleMigration_ShouldReturnSingleItem()
      {
         var options = new SyncMigrationOptions();
         var setup = new SyncSetup("Product");
         options.AddMigration("only_one", setup);

         var migrations = options.GetMigrations();

         Assert.Single(migrations);
         Assert.Equal("only_one", migrations[0].MigrationId);
      }

      // -----------------------------------------------------------------------
      // CurrentSetup tests
      // -----------------------------------------------------------------------

      [Fact]
      public void CurrentSetup_ShouldReturnLastSortedMigrationSetup()
      {
         var options = new SyncMigrationOptions();
         var setupV1 = new SyncSetup("Product");
         var setupV2 = new SyncSetup("Product", "ProductCategory");
         var setupV3 = new SyncSetup("Product", "ProductCategory", "Employee");

         // Add in reverse order
         options.AddMigration("20260301_v3", setupV3);
         options.AddMigration("20260101_v1", setupV1);
         options.AddMigration("20260201_v2", setupV2);

         Assert.Same(setupV3, options.CurrentSetup);
      }

      [Fact]
      public void CurrentSetup_EmptyOptions_ShouldReturnNull()
      {
         var options = new SyncMigrationOptions();
         Assert.Null(options.CurrentSetup);
      }

      [Fact]
      public void CurrentSetup_SingleMigration_ShouldReturnItsSetup()
      {
         var options = new SyncMigrationOptions();
         var setup = new SyncSetup("Product");
         options.AddMigration("mig1", setup);

         Assert.Same(setup, options.CurrentSetup);
      }

      [Fact]
      public void CurrentSetup_WithInitialMigration_ShouldReturnLastNamedMigration()
      {
         var setupInitial = new SyncSetup("Product");
         var setupV2 = new SyncSetup("Product", "ProductCategory");

         var options = new SyncMigrationOptions()
            .AddInitialMigration(setupInitial)
            .AddMigration("20260201_v2", setupV2);

         Assert.Same(setupV2, options.CurrentSetup);
      }

      // -----------------------------------------------------------------------
      // AutoProvision tests
      // -----------------------------------------------------------------------

      [Fact]
      public void AutoProvision_DefaultsToTrue()
      {
         var options = new SyncMigrationOptions();
         Assert.True(options.AutoProvision);
      }

      [Fact]
      public void AutoProvision_CanBeSetToFalse()
      {
         var options = new SyncMigrationOptions { AutoProvision = false };
         Assert.False(options.AutoProvision);
      }

      // -----------------------------------------------------------------------
      // Fluent API chaining tests
      // -----------------------------------------------------------------------

      [Fact]
      public void AddMigration_FluentChaining_ShouldWork()
      {
         var setupV1 = new SyncSetup("Product");
         var setupV2 = new SyncSetup("Product", "ProductCategory");

         var options = new SyncMigrationOptions()
            .AddMigration("20260101_v1", setupV1)
            .AddMigration("20260201_v2", setupV2);

         Assert.Equal(2, options.GetMigrations().Count);
         Assert.Same(setupV2, options.CurrentSetup);
      }

      // -----------------------------------------------------------------------
      // Idempotent sort tests
      // -----------------------------------------------------------------------

      [Fact]
      public void GetMigrations_CalledMultipleTimes_ShouldReturnSameResult()
      {
         var options = new SyncMigrationOptions();
         options.AddMigration("b", new SyncSetup("B"));
         options.AddMigration("a", new SyncSetup("A"));

         var first = options.GetMigrations();
         var second = options.GetMigrations();

         Assert.Equal(first.Count, second.Count);
         for (int i = 0; i < first.Count; i++)
         {
            Assert.Equal(first[i].MigrationId, second[i].MigrationId);
            Assert.Same(first[i].Setup, second[i].Setup);
         }
      }

      [Fact]
      public void AddMigration_AfterGetMigrations_ShouldResort()
      {
         var options = new SyncMigrationOptions();
         options.AddMigration("c", new SyncSetup("C"));
         options.AddMigration("a", new SyncSetup("A"));

         // Force sort
         var migrations = options.GetMigrations();
         Assert.Equal("a", migrations[0].MigrationId);
         Assert.Equal("c", migrations[1].MigrationId);

         // Add migration that sorts between existing ones
         options.AddMigration("b", new SyncSetup("B"));

         migrations = options.GetMigrations();
         Assert.Equal(3, migrations.Count);
         Assert.Equal("a", migrations[0].MigrationId);
         Assert.Equal("b", migrations[1].MigrationId);
         Assert.Equal("c", migrations[2].MigrationId);
      }
   }
}
