using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Tasks;
using Wormhole.Sync.Serialization;

namespace Wormhole.Sync.Web.Client.Serialization
{
   /// <summary>
   /// Web-specific JSON serializer that uses source generation for simple types and DataContract resolver for complex types.
   /// </summary>
   public class WebJsonObjectSerializer : ISerializer
   {
      private static readonly JsonSerializerOptions Options = new()
      {
         // Use source generation context for simple types, fall back to DataContract resolver for complex types
         TypeInfoResolver = JsonTypeInfoResolver.Combine(
            SyncJsonSerializerContext.Default,
            new DataContractResolver()),
         Converters = { new ArrayJsonConverter(), new ObjectToInferredTypesConverter() },
         PropertyNameCaseInsensitive = true,
         PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
      };

      /// <summary>
      /// Deserialize an object from a stream.
      /// </summary>
      public async Task<T> DeserializeAsync<T>(Stream ms) => await JsonSerializer.DeserializeAsync<T>(ms, Options).ConfigureAwait(false);

      /// <summary>
      /// Deserialize an object from a string.
      /// </summary>
      public T Deserialize<T>(string value) => JsonSerializer.Deserialize<T>(value, Options);

      /// <summary>
      /// Serialize an object to a stream.
      /// </summary>
      public Task<byte[]> SerializeAsync<T>(T obj) => Task.FromResult(JsonSerializer.SerializeToUtf8Bytes(obj, Options));

      /// <summary>
      /// Serialize an object to a stream.
      /// </summary>
      public Task<byte[]> SerializeAsync(object obj, Type type) => Task.FromResult(JsonSerializer.SerializeToUtf8Bytes(obj, type, Options));

      /// <summary>
      /// Serialize an object to a byte array.
      /// </summary>
      public byte[] Serialize<T>(T obj) => JsonSerializer.SerializeToUtf8Bytes(obj, Options);

      /// <summary>
      /// Serialize an object to a byte array.
      /// </summary>
      public byte[] Serialize(object obj, Type type) => JsonSerializer.SerializeToUtf8Bytes(obj, type, Options);

      /// <summary>
      /// Deserialize an object from a stream.
      /// </summary>
      public async Task<object> DeserializeAsync(Stream ms, Type type) => await JsonSerializer.DeserializeAsync(ms, type, Options).ConfigureAwait(false);
   }
}
