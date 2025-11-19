using System.Text.Json.Serialization;
using Wormhole.Sync.Serialization;

namespace Wormhole.Sync
{
   /// <summary>
   /// JSON serializer context for trim/AOT compatibility.
   /// Provides source-generated serialization metadata for simple, frequently-serialized sync types.
   /// Complex types rely on DataContractResolver with JsonConstructor attributes for trim safety.
   /// </summary>
   [JsonSerializable(typeof(SyncParameter))]
   [JsonSerializable(typeof(SyncParameters))]
   [JsonSerializable(typeof(SerializerInfo))]
   [JsonSourceGenerationOptions(
      PropertyNameCaseInsensitive = true,
      DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
   internal partial class SyncJsonSerializerContext : JsonSerializerContext
   {
   }
}
