using System;
using System.Runtime.Serialization;

namespace Wormhole.Sync
{
   /// <summary>
   /// Represents a named schema migration with a snapshot of the schema at that version.
   /// Migration names must be naturally sortable (date prefix convention like EF Code First,
   /// e.g., "20260217_titlecolumns").
   /// </summary>
   [DataContract(Name = "schema_migration"), Serializable]
   public class SyncSchemaMigration
   {
      /// <summary>
      /// Initializes a new instance of the <see cref="SyncSchemaMigration"/> class.
      /// </summary>
      public SyncSchemaMigration()
      {
      }

      /// <summary>
      /// Initializes a new instance of the <see cref="SyncSchemaMigration"/> class.
      /// </summary>
      public SyncSchemaMigration(string name, string scopeName, string schemaHash, string schemaJson, string setupJson)
      {
         this.Name = name;
         this.ScopeName = scopeName;
         this.SchemaHash = schemaHash;
         this.SchemaJson = schemaJson;
         this.SetupJson = setupJson;
         this.CreatedAt = DateTime.UtcNow;
      }

      /// <summary>
      /// Gets or sets the migration name (PK). Must be naturally sortable,
      /// e.g., "20260217_titlecolumns".
      /// </summary>
      [DataMember(Name = "n", IsRequired = true, Order = 1)]
      public string Name { get; set; }

      /// <summary>
      /// Gets or sets the scope name this migration belongs to.
      /// </summary>
      [DataMember(Name = "sn", IsRequired = true, Order = 2)]
      public string ScopeName { get; set; }

      /// <summary>
      /// Gets or sets the SHA256 hash of the schema at this version.
      /// </summary>
      [DataMember(Name = "sh", IsRequired = true, Order = 3)]
      public string SchemaHash { get; set; }

      /// <summary>
      /// Gets or sets the full schema snapshot as serialized JSON.
      /// </summary>
      [DataMember(Name = "sj", IsRequired = true, Order = 4)]
      public string SchemaJson { get; set; }

      /// <summary>
      /// Gets or sets the full setup snapshot as serialized JSON.
      /// </summary>
      [DataMember(Name = "stj", IsRequired = true, Order = 5)]
      public string SetupJson { get; set; }

      /// <summary>
      /// Gets or sets the timestamp when this migration was created.
      /// </summary>
      [DataMember(Name = "ca", IsRequired = false, Order = 6)]
      public DateTime CreatedAt { get; set; }

      /// <inheritdoc/>
      public override string ToString() => $"Migration: {this.Name} (Scope: {this.ScopeName}, Hash: {this.SchemaHash})";
   }
}
