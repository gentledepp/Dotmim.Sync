using System.Text.Json.Serialization;
using Wormhole.Sync.Serialization;
using Wormhole.Sync.Batch;
using Wormhole.Sync.Setup;

namespace Wormhole.Sync
{
   /// <summary>
   /// JSON serializer context for trim/AOT compatibility.
   /// Provides source-generated serialization metadata for simple sync types.
   /// Complex types with custom collection properties are handled by DataContractResolver.
   /// </summary>

   // Core Parameter Types
   [JsonSerializable(typeof(SyncParameter))]
   // SyncParameters - custom collection with dual indexers (string/int), handled by DataContractResolver

   // Serialization Types
   [JsonSerializable(typeof(SerializerInfo))]

   // Scope Types - Complex types with Setup/Set/Parameters properties handled by DataContractResolver
   // ScopeInfo - has SyncSet and SyncSetup properties (which have custom collections)
   // ScopeInfoClient - has SyncParameters property (custom collection)
   [JsonSerializable(typeof(ScopeInfoClientParameter))]
   [JsonSerializable(typeof(ScopeInfoClientParameters))]

   // Context Types
   [JsonSerializable(typeof(SyncContext))]
   [JsonSerializable(typeof(SyncErrorContext))]
   [JsonSerializable(typeof(ClientEnvironmentInfo))]

   // Setup Types - Complex types with collection properties handled by DataContractResolver
   // SyncSetup - has SetupTables, SetupFilters (custom collections)
   // SetupTable - has SetupColumns (custom collection)
   // SetupFilter - has collection properties
   [JsonSerializable(typeof(SetupFilterParameter))]
   [JsonSerializable(typeof(SetupFilterJoin))]
   [JsonSerializable(typeof(SetupFilterWhere))]

   // Set Types - Complex types with collection properties handled by DataContractResolver
   // SyncSet - has SyncTables, SyncRelations, SyncFilters (custom collections)
   // SyncTable - has SyncColumns (custom collection)
   // SyncFilter - has SyncFilterParameters, SyncFilterWhereSideItems, SyncFilterJoins (custom collections)
   [JsonSerializable(typeof(SyncColumn))]
   [JsonSerializable(typeof(SyncRelation))]
   [JsonSerializable(typeof(SyncFilterParameter))]
   [JsonSerializable(typeof(SyncFilterJoin))]
   [JsonSerializable(typeof(SyncFilterWhereSideItem))]
   [JsonSerializable(typeof(SyncColumnIdentifier))]
   [JsonSerializable(typeof(ContainerTable))]
   [JsonSerializable(typeof(ContainerTableColum))]
   [JsonSerializable(typeof(ContainerSet))]

   // Batch Types
   [JsonSerializable(typeof(BatchInfo))]
   [JsonSerializable(typeof(BatchPartInfo))]

   // Message Types
   [JsonSerializable(typeof(TableChangesSelected))]
   [JsonSerializable(typeof(TableChangesApplied))]
   [JsonSerializable(typeof(TableMetadatasCleaned))]
   [JsonSerializable(typeof(DatabaseChangesSelected))]
   [JsonSerializable(typeof(DatabaseChangesApplied))]
   [JsonSerializable(typeof(DatabaseMetadatasCleaned))]

   // Exception Types
   [JsonSerializable(typeof(SerializableExceptionInfo))]

   [JsonSourceGenerationOptions(
      PropertyNameCaseInsensitive = true,
      DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
   internal partial class SyncJsonSerializerContext : JsonSerializerContext
   {
   }
}
