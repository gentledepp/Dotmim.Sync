using Wormhole.Sync.Extensions;
using Wormhole.Sync.Tests.Core;
using Wormhole.Sync.Tests.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Wormhole.Sync.Tests.UnitTests
{
    public class ScopeInfoTests
    {
        [Fact]
        public void SetServerCapabilities_WithValidCapabilities_ShouldSerializeToJson()
        {
            // Arrange
            var scopeInfo = new ScopeInfo();
            var capabilities = new Dictionary<string, object>
            {
                { ServerCapabilities.OptimizedSync, true },
                { ServerCapabilities.ErrorReporting, false },
                { "custom-capability", "test-value" }
            };

            // Act
            scopeInfo.SetServerCapabilities(capabilities);

            // Assert
            Assert.NotNull(scopeInfo.ServerCapabilities);
            var deserializedCapabilities = scopeInfo.GetServerCapabilities();
            Assert.NotNull(deserializedCapabilities);
            Assert.Equal(3, deserializedCapabilities.Count);
            Assert.True(deserializedCapabilities.ContainsKey(ServerCapabilities.OptimizedSync));
            Assert.True(deserializedCapabilities.ContainsKey(ServerCapabilities.ErrorReporting));
            Assert.True(deserializedCapabilities.ContainsKey("custom-capability"));
        }

        [Fact]
        public void SetServerCapabilities_WithNullCapabilities_ShouldSetServerCapabilitiesToNull()
        {
            // Arrange
            var scopeInfo = new ScopeInfo();
            scopeInfo.ServerCapabilities = "existing data";

            // Act
            scopeInfo.SetServerCapabilities(null);

            // Assert
            Assert.Null(scopeInfo.ServerCapabilities);
        }

        [Fact]
        public void SetServerCapabilities_WithEmptyCapabilities_ShouldSerializeEmptyDictionary()
        {
            // Arrange
            var scopeInfo = new ScopeInfo();
            var capabilities = new Dictionary<string, object>();

            // Act
            scopeInfo.SetServerCapabilities(capabilities);

            // Assert
            Assert.Null(scopeInfo.ServerCapabilities);
            var deserializedCapabilities = scopeInfo.GetServerCapabilities();
            Assert.NotNull(deserializedCapabilities);
            Assert.Empty(deserializedCapabilities);
        }

        [Fact]
        public void SetServerCapabilities_WithSameCapabilities_ShouldNotChangeServerCapabilities()
        {
            // Arrange
            var scopeInfo = new ScopeInfo();
            var capabilities = new Dictionary<string, object>
            {
                { ServerCapabilities.OptimizedSync, true },
                { ServerCapabilities.ErrorReporting, true }
            };

            // Act - Set capabilities twice
            scopeInfo.SetServerCapabilities(capabilities);
            var firstCapabilities = scopeInfo.ServerCapabilities;
            scopeInfo.SetServerCapabilities(capabilities);
            var secondCapabilities = scopeInfo.ServerCapabilities;

            // Assert
            Assert.Equal(firstCapabilities, secondCapabilities);
        }

        [Fact]
        public void SetServerCapabilities_WithDifferentCapabilities_ShouldUpdateServerCapabilities()
        {
            // Arrange
            var scopeInfo = new ScopeInfo();
            var initialCapabilities = new Dictionary<string, object>
            {
                { ServerCapabilities.OptimizedSync, true }
            };
            var updatedCapabilities = new Dictionary<string, object>
            {
                { ServerCapabilities.OptimizedSync, false },
                { ServerCapabilities.ErrorReporting, true }
            };

            // Act
            scopeInfo.SetServerCapabilities(initialCapabilities);
            var firstCapabilities = scopeInfo.ServerCapabilities;
            scopeInfo.SetServerCapabilities(updatedCapabilities);
            var secondCapabilities = scopeInfo.ServerCapabilities;

            // Assert
            Assert.NotEqual(firstCapabilities, secondCapabilities);
            var deserializedCapabilities = scopeInfo.GetServerCapabilities();
            Assert.Equal(2, deserializedCapabilities.Count);
            Assert.False((bool)((JsonElement)deserializedCapabilities[ServerCapabilities.OptimizedSync]).GetBoolean());
            Assert.True((bool)((JsonElement)deserializedCapabilities[ServerCapabilities.ErrorReporting]).GetBoolean());
        }

        [Fact]
        public void SetServerCapabilities_WithActualServerCapabilities_ShouldWork()
        {
            // Arrange
            var scopeInfo = new ScopeInfo();
            var actualCapabilities = ServerCapabilities.GetServerCapabilities();

            // Act
            scopeInfo.SetServerCapabilities(actualCapabilities);

            // Assert
            Assert.NotNull(scopeInfo.ServerCapabilities);
            var deserializedCapabilities = scopeInfo.GetServerCapabilities();
            Assert.NotNull(deserializedCapabilities);
            Assert.Equal(2, deserializedCapabilities.Count);
            Assert.True(deserializedCapabilities.ContainsKey(ServerCapabilities.OptimizedSync));
            Assert.True(deserializedCapabilities.ContainsKey(ServerCapabilities.ErrorReporting));
        }

        [Fact]
        public void SupportsCapability_WithExistingCapability_ShouldReturnTrue()
        {
            // Arrange
            var scopeInfo = new ScopeInfo();
            var capabilities = new Dictionary<string, object>
            {
                { ServerCapabilities.OptimizedSync, true }
            };
            scopeInfo.SetServerCapabilities(capabilities);

            // Act
            var result = scopeInfo.SupportsCapability(ServerCapabilities.OptimizedSync);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void SupportsCapability_WithNonExistingCapability_ShouldReturnFalse()
        {
            // Arrange
            var scopeInfo = new ScopeInfo();
            var capabilities = new Dictionary<string, object>
            {
                { ServerCapabilities.OptimizedSync, true }
            };
            scopeInfo.SetServerCapabilities(capabilities);

            // Act
            var result = scopeInfo.SupportsCapability("non-existing-capability");

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void SupportsCapability_WithNullServerCapabilities_ShouldReturnFalse()
        {
            // Arrange
            var scopeInfo = new ScopeInfo();

            // Act
            var result = scopeInfo.SupportsCapability(ServerCapabilities.OptimizedSync);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void UpdateSchemaHash_WithValidJsonString_ShouldSetSchemaHash()
        {
            // Arrange
            var scopeInfo = new ScopeInfo();
            var schemaJson = "{\"tables\":[{\"name\":\"Customer\"}]}";

            // Act
            scopeInfo.UpdateSchemaHash(schemaJson);

            // Assert
            Assert.NotNull(scopeInfo.SchemaHash);
            Assert.NotEmpty(scopeInfo.SchemaHash);
        }

        [Fact]
        public void UpdateSchemaHash_WithNullJsonString_ShouldSetSchemaHashToNull()
        {
            // Arrange
            var scopeInfo = new ScopeInfo();
            scopeInfo.SchemaHash = "existing hash";

            // Act
            scopeInfo.UpdateSchemaHash((string)null);

            // Assert
            Assert.Null(scopeInfo.SchemaHash);
        }

        [Fact]
        public void UpdateSchemaHash_WithEmptyJsonString_ShouldSetSchemaHashToNull()
        {
            // Arrange
            var scopeInfo = new ScopeInfo();
            scopeInfo.SchemaHash = "existing hash";

            // Act
            scopeInfo.UpdateSchemaHash("");

            // Assert
            Assert.Null(scopeInfo.SchemaHash);
        }

        [Fact]
        public void UpdateSchemaHash_WithInvalidJsonString_ShouldThrowArgumentException()
        {
            // Arrange
            var scopeInfo = new ScopeInfo();
            var invalidJson = "not valid json";

            // Act & Assert
            var exception = Assert.Throws<ArgumentException>(() => scopeInfo.UpdateSchemaHash(invalidJson));
            Assert.Contains("must be valid json", exception.Message);
        }

        [Fact]
        public void UpdateSchemaHash_WithSyncSet_ShouldSetSchemaHash()
        {
            // Arrange
            var scopeInfo = new ScopeInfo();
            var schema = new SyncSet();
            schema.Tables.Add(new SyncTable("Customer"));

            // Act
            scopeInfo.UpdateSchemaHash(schema);

            // Assert
            Assert.NotNull(scopeInfo.SchemaHash);
            Assert.NotEmpty(scopeInfo.SchemaHash);
        }

        [Fact]
        public void UpdateSchemaHash_WithNullSyncSet_ShouldSetSchemaHashToNull()
        {
            // Arrange
            var scopeInfo = new ScopeInfo();
            scopeInfo.SchemaHash = "existing hash";

            // Act
            scopeInfo.UpdateSchemaHash((SyncSet)null);

            // Assert
            Assert.Null(scopeInfo.SchemaHash);
        }

        [Fact]
        public void UpdateSchemaHash_WithSameSchema_ShouldGenerateSameHash()
        {
            // Arrange
            var scopeInfo1 = new ScopeInfo();
            var scopeInfo2 = new ScopeInfo();
            var schema = new SyncSet();
            schema.Tables.Add(new SyncTable("Customer"));

            // Act
            scopeInfo1.UpdateSchemaHash(schema);
            scopeInfo2.UpdateSchemaHash(schema);

            // Assert
            Assert.Equal(scopeInfo1.SchemaHash, scopeInfo2.SchemaHash);
        }

        [Fact]
        public void UpdateSchemaHash_WithDifferentSchemas_ShouldGenerateDifferentHashes()
        {
            // Arrange
            var scopeInfo1 = new ScopeInfo();
            var scopeInfo2 = new ScopeInfo();
            var schema1 = new SyncSet();
            schema1.Tables.Add(new SyncTable("Customer"));
            var schema2 = new SyncSet();
            schema2.Tables.Add(new SyncTable("Product"));

            // Act
            scopeInfo1.UpdateSchemaHash(schema1);
            scopeInfo2.UpdateSchemaHash(schema2);

            // Assert
            Assert.NotEqual(scopeInfo1.SchemaHash, scopeInfo2.SchemaHash);
        }

        [Fact]
        public void ValidateSchemaHash_WithMatchingSchema_ShouldReturnTrue()
        {
            // Arrange
            var scopeInfo = new ScopeInfo();
            var schema = new SyncSet();
            schema.Tables.Add(new SyncTable("Customer"));
            scopeInfo.UpdateSchemaHash(schema);

            // Act
            var result = scopeInfo.ValidateSchemaHash(schema);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void ValidateSchemaHash_WithDifferentSchema_ShouldReturnFalse()
        {
            // Arrange
            var scopeInfo = new ScopeInfo();
            var originalSchema = new SyncSet();
            originalSchema.Tables.Add(new SyncTable("Customer"));
            scopeInfo.UpdateSchemaHash(originalSchema);

            var differentSchema = new SyncSet();
            differentSchema.Tables.Add(new SyncTable("Product"));

            // Act
            var result = scopeInfo.ValidateSchemaHash(differentSchema);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void ValidateSchemaHash_WithNullSchemaHash_ShouldReturnFalse()
        {
            // Arrange
            var scopeInfo = new ScopeInfo();
            var schema = new SyncSet();
            schema.Tables.Add(new SyncTable("Customer"));

            // Act
            var result = scopeInfo.ValidateSchemaHash(schema);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void ValidateSchemaHash_WithNullSchema_ShouldReturnFalse()
        {
            // Arrange
            var scopeInfo = new ScopeInfo();
            scopeInfo.SchemaHash = "some hash";

            // Act
            var result = scopeInfo.ValidateSchemaHash(null);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void UpdateSchemaHash_WithJsonAndSyncSet_ShouldGenerateConsistentHashes()
        {
            // Arrange
            var scopeInfo1 = new ScopeInfo();
            var scopeInfo2 = new ScopeInfo();
            var schema = new SyncSet();
            schema.Tables.Add(new SyncTable("Customer"));

            // Serialize the schema to JSON manually for comparison
            var serializer = ScopeInfo.Serializer;
            var schemaJson = serializer.Serialize(schema).ToUtf8String();

            // Act
            scopeInfo1.UpdateSchemaHash(schema);
            scopeInfo2.UpdateSchemaHash(schemaJson);

            // Assert
            Assert.Equal(scopeInfo1.SchemaHash, scopeInfo2.SchemaHash);
        }
    }
}