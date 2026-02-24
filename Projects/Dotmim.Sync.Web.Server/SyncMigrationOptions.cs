using System;
using System.Collections.Generic;
using System.Linq;

namespace Wormhole.Sync.Web.Server
{
   /// <summary>
   /// Represents a single schema migration step.
   /// </summary>
   public class SyncMigration
   {
      /// <summary>
      /// Gets the unique, sortable identifier for this migration (e.g., "20260101_v1_initial").
      /// An empty string is used for the initial migration and always sorts first.
      /// </summary>
      public string MigrationId { get; }

      /// <summary>
      /// Gets the SyncSetup snapshot for this migration step.
      /// </summary>
      public SyncSetup Setup { get; }

      /// <summary>
      /// Initializes a new instance of the <see cref="SyncMigration"/> class.
      /// </summary>
      /// <param name="migrationId">
      /// A unique, sortable migration name. Use <see cref="string.Empty"/> for the initial migration.
      /// </param>
      /// <param name="setup">The SyncSetup for this migration step.</param>
      public SyncMigration(string migrationId, SyncSetup setup)
      {
         Guard.ThrowIfNull(migrationId);
         Guard.ThrowIfNull(setup);
         MigrationId = migrationId;
         Setup = setup;
      }
   }

   /// <summary>
   /// Configuration for schema migrations within a single scope.
   /// Use with named options pattern — each scope name maps to its own instance.
   /// </summary>
   public class SyncMigrationOptions
   {
      private readonly List<SyncMigration> migrations = new();
      private bool sorted;

      /// <summary>
      /// When true (default), the first migration auto-provisions the scope on a fresh DB
      /// via ProvisionAsync. Set to false if you handle provisioning externally
      /// (e.g., via EF Code First migrations with custom stored procedures).
      /// </summary>
      public bool AutoProvision { get; set; } = true;

      /// <summary>
      /// Add the initial migration. Uses an empty string as the migration ID,
      /// which always sorts before any named migration.
      /// </summary>
      public SyncMigrationOptions AddInitialMigration(SyncSetup setup)
         => AddMigration(string.Empty, setup);

      /// <summary>
      /// Add a migration step. Duplicate IDs throw. Order of calls doesn't matter
      /// (migrations are sorted internally by MigrationId using ordinal comparison).
      /// </summary>
      public SyncMigrationOptions AddMigration(string migrationId, SyncSetup setup)
      {
         Guard.ThrowIfNull(migrationId);
         Guard.ThrowIfNull(setup);

         if (this.migrations.Any(m => string.Equals(m.MigrationId, migrationId, StringComparison.Ordinal)))
            throw new ArgumentException($"A migration with ID '{migrationId}' has already been added.", nameof(migrationId));

         this.migrations.Add(new SyncMigration(migrationId, setup));
         this.sorted = false;
         return this;
      }

      /// <summary>
      /// Returns migrations sorted by MigrationId (ordinal string comparison).
      /// </summary>
      public IReadOnlyList<SyncMigration> GetMigrations()
      {
         EnsureSorted();
         return this.migrations.AsReadOnly();
      }

      /// <summary>
      /// Returns the Setup from the last sorted migration, or null if no migrations are configured.
      /// </summary>
      public SyncSetup CurrentSetup
      {
         get
         {
            EnsureSorted();
            return this.migrations.Count > 0 ? this.migrations[this.migrations.Count - 1].Setup : null;
         }
      }

      private void EnsureSorted()
      {
         if (!this.sorted)
         {
             // This pins any migration with an empty MigrationId to the front of the list
             // while preserving ordinal sorting for everything else
             this.migrations.Sort((a, b) =>
             {
                 bool aEmpty = string.IsNullOrEmpty(a.MigrationId);
                 bool bEmpty = string.IsNullOrEmpty(b.MigrationId);

                 if (aEmpty && bEmpty) return 0;
                 if (aEmpty) return -1;
                 if (bEmpty) return 1;

                 return string.Compare(a.MigrationId, b.MigrationId, StringComparison.Ordinal);
             });
                this.sorted = true;
         }
      }
   }
}
