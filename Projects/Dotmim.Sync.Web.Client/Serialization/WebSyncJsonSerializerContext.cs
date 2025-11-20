using System.Text.Json.Serialization;
using Wormhole.Sync.Web.Client;

namespace Wormhole.Sync.Web.Client.Serialization
{
   /// <summary>
   /// JSON serializer context for Web.Client HTTP message types - trim/AOT compatible.
   /// Provides source-generated serialization metadata for all HttpMessage types.
   /// </summary>

   // HttpMessage Types
   [JsonSerializable(typeof(HttpMessageSendChangesResponse))]
   [JsonSerializable(typeof(HttpMessageGetMoreChangesRequest))]
   [JsonSerializable(typeof(HttpMessageSendChangesRequest))]
   [JsonSerializable(typeof(HttpMessageEnsureSchemaResponse))]
   [JsonSerializable(typeof(HttpMessageEnsureScopesResponse))]
   [JsonSerializable(typeof(HttpMessageEnsureScopesRequest))]
   [JsonSerializable(typeof(HttpMessageOperationRequest))]
   [JsonSerializable(typeof(HttpMessageOperationResponse))]
   [JsonSerializable(typeof(HttpMessageRemoteTimestampResponse))]
   [JsonSerializable(typeof(HttpMessageRemoteTimestampRequest))]
   [JsonSerializable(typeof(HttpMessageSummaryResponse))]
   [JsonSerializable(typeof(HttpMessageEndSessionRequest))]
   [JsonSerializable(typeof(HttpMessageEndSessionResponse))]
   [JsonSerializable(typeof(HttpMessageSendChangesIncrementalRequest))]
   [JsonSerializable(typeof(HttpMessageSendChangesIncrementalResponse))]
   [JsonSerializable(typeof(HttpMessageSendSyncErrorsRequest))]
   [JsonSerializable(typeof(HttpMessageSendSyncErrorsResponse))]

   // Backward Compatibility Types
   [JsonSourceGenerationOptions(
      PropertyNameCaseInsensitive = true,
      DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
   internal partial class WebSyncJsonSerializerContext : JsonSerializerContext
   {
   }
}
