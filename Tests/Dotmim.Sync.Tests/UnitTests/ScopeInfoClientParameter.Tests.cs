using Wormhole.Sync;
using System;
using System.Data;
using Xunit;

namespace Wormhole.Sync.Tests.UnitTests
{
    public class ScopeInfoClientParameterTests
    {
        [Fact]
        public void Constructor_ShouldInitializeWithDefaultValues()
        {
            // Arrange & Act
            var parameter = new ScopeInfoClientParameter();

            // Assert
            Assert.Null(parameter.Name);
            Assert.Equal(DbType.String, parameter.DbType);
            Assert.Equal(0, parameter.MaxLength);
        }

        [Fact]
        public void Clone_ShouldCreateDeepCopy()
        {
            // Arrange
            var parameter = new ScopeInfoClientParameter
            {
                Name = "UserId",
                DbType = DbType.Int64,
                MaxLength = 100
            };

            // Act
            var clone = parameter.Clone();

            // Assert
            Assert.NotSame(parameter, clone);
            Assert.Equal(parameter.Name, clone.Name);
            Assert.Equal(parameter.DbType, clone.DbType);
            Assert.Equal(parameter.MaxLength, clone.MaxLength);
        }

        [Fact]
        public void EqualsByProperties_WithSameProperties_ShouldReturnTrue()
        {
            // Arrange
            var parameter1 = new ScopeInfoClientParameter
            {
                Name = "UserId",
                DbType = DbType.Int64,
                MaxLength = 0
            };

            var parameter2 = new ScopeInfoClientParameter
            {
                Name = "UserId",
                DbType = DbType.Int64,
                MaxLength = 0
            };

            // Act
            var result = parameter1.EqualsByProperties(parameter2);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void EqualsByProperties_WithDifferentName_ShouldReturnFalse()
        {
            // Arrange
            var parameter1 = new ScopeInfoClientParameter
            {
                Name = "UserId",
                DbType = DbType.Int64
            };

            var parameter2 = new ScopeInfoClientParameter
            {
                Name = "TenantId",
                DbType = DbType.Int64
            };

            // Act
            var result = parameter1.EqualsByProperties(parameter2);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void EqualsByProperties_WithDifferentDbType_ShouldReturnFalse()
        {
            // Arrange
            var parameter1 = new ScopeInfoClientParameter
            {
                Name = "UserId",
                DbType = DbType.Int64
            };

            var parameter2 = new ScopeInfoClientParameter
            {
                Name = "UserId",
                DbType = DbType.Int32
            };

            // Act
            var result = parameter1.EqualsByProperties(parameter2);

            // Assert
            Assert.False(result);
        }

        [Theory]
        [InlineData(DbType.Int64, typeof(long), true)]
        [InlineData(DbType.Int64, typeof(int), true)]
        [InlineData(DbType.Int32, typeof(int), true)]
        [InlineData(DbType.Int32, typeof(long), false)]
        [InlineData(DbType.String, typeof(string), true)]
        [InlineData(DbType.String, typeof(int), true)]
        [InlineData(DbType.Guid, typeof(Guid), true)]
        [InlineData(DbType.Boolean, typeof(bool), true)]
        [InlineData(DbType.Boolean, typeof(int), false)]
        [InlineData(DbType.DateTime, typeof(DateTime), true)]
        [InlineData(DbType.DateTime, typeof(DateTimeOffset), true)]
        public void TryGetCompatibleValue_SyncParameter_ShouldCheckTypeCompatibility(DbType dbType, Type valueType, bool expectedResult)
        {
            // Arrange
            var parameter = new ScopeInfoClientParameter
            {
                Name = "TestParam",
                DbType = dbType
            };

            var syncParam = new SyncParameter("TestParam", GetDefaultValue(valueType));

            // Act
            var result = parameter.TryGetCompatibleValue(syncParam, out var convertedValue);

            // Assert
            Assert.Equal(expectedResult, result);
            if (expectedResult)
            {
                Assert.NotNull(convertedValue);
            }
            else
            {
                Assert.Null(convertedValue);
            }
        }

        [Fact]
        public void TryGetCompatibleValue_NullSyncParameter_ShouldReturnFalse()
        {
            // Arrange
            var parameter = new ScopeInfoClientParameter
            {
                Name = "TestParam",
                DbType = DbType.Int64
            };

            // Act
            var result = parameter.TryGetCompatibleValue(null, out var convertedValue);

            // Assert
            Assert.False(result);
            Assert.Null(convertedValue);
        }

        [Fact]
        public void TryGetCompatibleValue_NullValue_ShouldReturnTrue()
        {
            // Arrange
            var parameter = new ScopeInfoClientParameter
            {
                Name = "TestParam",
                DbType = DbType.Int64
            };

            var syncParam = new SyncParameter("TestParam", null);

            // Act
            var result = parameter.TryGetCompatibleValue(syncParam, out var convertedValue);

            // Assert
            Assert.True(result);
            Assert.Null(convertedValue);
        }

        [Fact]
        public void TryGetCompatibleValue_GuidParameter_WithGuidValue_ShouldReturnTrue()
        {
            // Arrange
            var parameter = new ScopeInfoClientParameter
            {
                Name = "DeviceIdentifier",
                DbType = DbType.Guid
            };

            var deviceId = Guid.NewGuid();
            var syncParam = new SyncParameter("DeviceIdentifier", deviceId);

            // Act
            var result = parameter.TryGetCompatibleValue(syncParam, out var convertedValue);

            // Assert
            Assert.True(result);
            Assert.NotNull(convertedValue);
            Assert.IsType<Guid>(convertedValue);
            Assert.Equal(deviceId, convertedValue);
        }

        [Fact]
        public void TryGetCompatibleValue_GuidParameter_WithStringValue_ShouldConvertAndReturnTrue()
        {
            // Arrange
            var parameter = new ScopeInfoClientParameter
            {
                Name = "DeviceIdentifier",
                DbType = DbType.Guid
            };

            var deviceId = Guid.NewGuid();
            var syncParam = new SyncParameter("DeviceIdentifier", deviceId.ToString());

            // Act
            var result = parameter.TryGetCompatibleValue(syncParam, out var convertedValue);

            // Assert
            Assert.True(result);
            Assert.NotNull(convertedValue);
            Assert.IsType<Guid>(convertedValue);
            Assert.Equal(deviceId, convertedValue);
        }

        [Fact]
        public void TryGetCompatibleValue_DateTimeParameter_WithStringValue_ShouldConvertAndReturnTrue()
        {
            // Arrange
            var parameter = new ScopeInfoClientParameter
            {
                Name = "CreatedAt",
                DbType = DbType.DateTime
            };

            var dateTime = new DateTime(2025, 1, 15, 10, 30, 0, DateTimeKind.Utc);
            var syncParam = new SyncParameter("CreatedAt", dateTime.ToString("o")); // ISO 8601 format

            // Act
            var result = parameter.TryGetCompatibleValue(syncParam, out var convertedValue);

            // Assert
            Assert.True(result);
            Assert.NotNull(convertedValue);
            Assert.IsType<DateTime>(convertedValue);
        }

        [Fact]
        public void TryGetCompatibleValue_TimeSpanParameter_WithStringValue_ShouldConvertAndReturnTrue()
        {
            // Arrange
            var parameter = new ScopeInfoClientParameter
            {
                Name = "Duration",
                DbType = DbType.Time
            };

            var timeSpan = TimeSpan.FromHours(2.5);
            var syncParam = new SyncParameter("Duration", timeSpan.ToString());

            // Act
            var result = parameter.TryGetCompatibleValue(syncParam, out var convertedValue);

            // Assert
            Assert.True(result);
            Assert.NotNull(convertedValue);
            Assert.IsType<TimeSpan>(convertedValue);
        }

        [Fact]
        public void TryGetCompatibleValue_Int32Parameter_WithStringValue_ShouldConvertAndReturnTrue()
        {
            // Arrange
            var parameter = new ScopeInfoClientParameter
            {
                Name = "Count",
                DbType = DbType.Int32
            };

            var syncParam = new SyncParameter("Count", "42");

            // Act
            var result = parameter.TryGetCompatibleValue(syncParam, out var convertedValue);

            // Assert
            Assert.True(result);
            Assert.NotNull(convertedValue);
            Assert.IsType<int>(convertedValue);
            Assert.Equal(42, convertedValue);
        }

        [Fact]
        public void TryGetCompatibleValue_BooleanParameter_WithStringValue_ShouldConvertAndReturnTrue()
        {
            // Arrange
            var parameter = new ScopeInfoClientParameter
            {
                Name = "IsActive",
                DbType = DbType.Boolean
            };

            var syncParam = new SyncParameter("IsActive", "true");

            // Act
            var result = parameter.TryGetCompatibleValue(syncParam, out var convertedValue);

            // Assert
            Assert.True(result);
            Assert.NotNull(convertedValue);
            Assert.IsType<bool>(convertedValue);
            Assert.True((bool)convertedValue);
        }

        [Fact]
        public void IsCompatibleWith_ShouldCallTryGetCompatibleValue()
        {
            // Arrange
            var parameter = new ScopeInfoClientParameter
            {
                Name = "TestParam",
                DbType = DbType.Int32
            };

            var syncParam = new SyncParameter("TestParam", 42);

            // Act
            var result = parameter.IsCompatibleWith(syncParam);

            // Assert
            Assert.True(result);
        }

        [Theory]
        [InlineData("true", true, true)]
        [InlineData("false", false, true)]
        [InlineData("True", true, true)]
        [InlineData("False", false, true)]
        [InlineData("TRUE", true, true)]
        [InlineData("FALSE", false, true)]
        [InlineData("0", false, true)]
        [InlineData("1", true, true)]
        [InlineData(" 0 ", false, true)]
        [InlineData(" 1 ", true, true)]
        public void TryGetCompatibleValue_BooleanParameter_ValidValues_ShouldConvert(string inputValue, bool expectedValue, bool shouldSucceed)
        {
            // Arrange
            var parameter = new ScopeInfoClientParameter
            {
                Name = "IsActive",
                DbType = DbType.Boolean
            };

            var syncParam = new SyncParameter("IsActive", inputValue);

            // Act
            var result = parameter.TryGetCompatibleValue(syncParam, out var convertedValue);

            // Assert
            Assert.Equal(shouldSucceed, result);
            if (shouldSucceed)
            {
                Assert.NotNull(convertedValue);
                Assert.IsType<bool>(convertedValue);
                Assert.Equal(expectedValue, convertedValue);
            }
        }

        [Theory]
        [InlineData("2")]
        [InlineData("-1")]
        [InlineData("10")]
        [InlineData("yes")]
        [InlineData("no")]
        [InlineData("Y")]
        [InlineData("N")]
        public void TryGetCompatibleValue_BooleanParameter_InvalidValues_ShouldFail(string inputValue)
        {
            // Arrange
            var parameter = new ScopeInfoClientParameter
            {
                Name = "IsActive",
                DbType = DbType.Boolean
            };

            var syncParam = new SyncParameter("IsActive", inputValue);

            // Act
            var result = parameter.TryGetCompatibleValue(syncParam, out var convertedValue);

            // Assert
            Assert.False(result);
            Assert.Null(convertedValue);
        }

        [Fact]
        public void TryGetCompatibleValue_BooleanParameter_WithIntegerTwo_ShouldFail()
        {
            // Arrange - Test that integers other than 0 and 1 fail
            var parameter = new ScopeInfoClientParameter
            {
                Name = "IsActive",
                DbType = DbType.Boolean
            };

            var syncParam = new SyncParameter("IsActive", 2);

            // Act
            var result = parameter.TryGetCompatibleValue(syncParam, out var convertedValue);

            // Assert
            Assert.False(result);
            Assert.Null(convertedValue);
        }

        [Fact]
        public void TryGetCompatibleValue_StringParameter_WithinMaxLength_ShouldSucceed()
        {
            // Arrange
            var parameter = new ScopeInfoClientParameter
            {
                Name = "DeviceName",
                DbType = DbType.String,
                MaxLength = 50
            };

            var syncParam = new SyncParameter("DeviceName", "iPhone 15 Pro Max");

            // Act
            var result = parameter.TryGetCompatibleValue(syncParam, out var convertedValue);

            // Assert
            Assert.True(result);
            Assert.NotNull(convertedValue);
            Assert.IsType<string>(convertedValue);
            Assert.Equal("iPhone 15 Pro Max", convertedValue);
        }

        [Fact]
        public void TryGetCompatibleValue_StringParameter_ExceedsMaxLength_ShouldFail()
        {
            // Arrange
            var parameter = new ScopeInfoClientParameter
            {
                Name = "DeviceName",
                DbType = DbType.String,
                MaxLength = 10
            };

            var syncParam = new SyncParameter("DeviceName", "This is a very long device name that exceeds the maximum length");

            // Act
            var result = parameter.TryGetCompatibleValue(syncParam, out var convertedValue);

            // Assert
            Assert.False(result);
            Assert.Null(convertedValue);
        }

        [Fact]
        public void TryGetCompatibleValue_StringParameter_ExactlyMaxLength_ShouldSucceed()
        {
            // Arrange
            var parameter = new ScopeInfoClientParameter
            {
                Name = "DeviceName",
                DbType = DbType.String,
                MaxLength = 10
            };

            var syncParam = new SyncParameter("DeviceName", "1234567890"); // Exactly 10 characters

            // Act
            var result = parameter.TryGetCompatibleValue(syncParam, out var convertedValue);

            // Assert
            Assert.True(result);
            Assert.NotNull(convertedValue);
            Assert.Equal("1234567890", convertedValue);
        }

        [Fact]
        public void TryGetCompatibleValue_StringParameter_NoMaxLength_ShouldSucceed()
        {
            // Arrange - MaxLength = 0 means no limit, MaxLength = -1 also means no limit
            var parameter = new ScopeInfoClientParameter
            {
                Name = "DeviceName",
                DbType = DbType.String,
                MaxLength = 0
            };

            var syncParam = new SyncParameter("DeviceName", new string('X', 10000)); // Very long string

            // Act
            var result = parameter.TryGetCompatibleValue(syncParam, out var convertedValue);

            // Assert
            Assert.True(result);
            Assert.NotNull(convertedValue);
            Assert.IsType<string>(convertedValue);
        }

        [Fact]
        public void TryGetCompatibleValue_StringParameter_WithIntegerValue_ShouldConvertAndValidateLength()
        {
            // Arrange - String representation of integer "12345" is 5 characters
            var parameter = new ScopeInfoClientParameter
            {
                Name = "Code",
                DbType = DbType.String,
                MaxLength = 3
            };

            var syncParam = new SyncParameter("Code", 12345);

            // Act
            var result = parameter.TryGetCompatibleValue(syncParam, out var convertedValue);

            // Assert - Should fail because "12345" has 5 characters, exceeding MaxLength of 3
            Assert.False(result);
            Assert.Null(convertedValue);
        }

        [Fact]
        public void ToString_ShouldReturnFormattedString()
        {
            // Arrange
            var parameter = new ScopeInfoClientParameter
            {
                Name = "UserId",
                DbType = DbType.Int64,
                MaxLength = 0
            };

            // Act
            var result = parameter.ToString();

            // Assert
            Assert.Contains("UserId", result);
            Assert.Contains("Int64", result);
        }

        private static object GetDefaultValue(Type type)
        {
            if (type == typeof(string))
                return "test";
            if (type == typeof(int))
                return int.MaxValue;
            if (type == typeof(long))
                return long.MaxValue;
            if (type == typeof(Guid))
                return Guid.NewGuid();
            if (type == typeof(bool))
                return true;
            if (type == typeof(DateTime))
                return DateTime.Now;
            if (type == typeof(DateTimeOffset))
                return DateTimeOffset.Now;

            return Activator.CreateInstance(type);
        }
    }

    public class ScopeInfoClientParametersTests
    {
        [Fact]
        public void Constructor_ShouldInitializeEmptyCollection()
        {
            // Arrange & Act
            var parameters = new ScopeInfoClientParameters();

            // Assert
            Assert.NotNull(parameters);
            Assert.Equal(0, parameters.Count);
        }

        [Fact]
        public void Add_ShouldAddParameter()
        {
            // Arrange
            var parameters = new ScopeInfoClientParameters();
            var parameter = new ScopeInfoClientParameter
            {
                Name = "UserId",
                DbType = DbType.Int64
            };

            // Act
            parameters.Add(parameter);

            // Assert
            Assert.Equal(1, parameters.Count);
            Assert.Contains(parameter, parameters);
        }

        [Fact]
        public void Indexer_ByName_ShouldReturnParameter()
        {
            // Arrange
            var parameters = new ScopeInfoClientParameters
            {
                new ScopeInfoClientParameter { Name = "UserId", DbType = DbType.Int64 },
                new ScopeInfoClientParameter { Name = "TenantId", DbType = DbType.Int64 }
            };

            // Act
            var userId = parameters["UserId"];

            // Assert
            Assert.NotNull(userId);
            Assert.Equal("UserId", userId.Name);
            Assert.Equal(DbType.Int64, userId.DbType);
        }

        [Fact]
        public void Indexer_ByName_CaseInsensitive_ShouldReturnParameter()
        {
            // Arrange
            var parameters = new ScopeInfoClientParameters
            {
                new ScopeInfoClientParameter { Name = "UserId", DbType = DbType.Int64 }
            };

            // Act
            var userId = parameters["userid"];

            // Assert
            Assert.NotNull(userId);
            Assert.Equal("UserId", userId.Name);
        }

        [Fact]
        public void Indexer_ByName_NotFound_ShouldReturnNull()
        {
            // Arrange
            var parameters = new ScopeInfoClientParameters
            {
                new ScopeInfoClientParameter { Name = "UserId", DbType = DbType.Int64 }
            };

            // Act
            var notFound = parameters["NonExistent"];

            // Assert
            Assert.Null(notFound);
        }

        [Fact]
        public void Clone_ShouldCreateDeepCopy()
        {
            // Arrange
            var parameters = new ScopeInfoClientParameters
            {
                new ScopeInfoClientParameter { Name = "UserId", DbType = DbType.Int64 },
                new ScopeInfoClientParameter { Name = "TenantId", DbType = DbType.Int64 }
            };

            // Act
            var clone = parameters.Clone();

            // Assert
            Assert.NotSame(parameters, clone);
            Assert.Equal(parameters.Count, clone.Count);
            Assert.NotSame(parameters[0], clone[0]);
            Assert.Equal(parameters[0].Name, clone[0].Name);
        }

        [Fact]
        public void CompareWith_SameParameters_ShouldReturnTrue()
        {
            // Arrange
            var parameters1 = new ScopeInfoClientParameters
            {
                new ScopeInfoClientParameter { Name = "UserId", DbType = DbType.Int64 },
                new ScopeInfoClientParameter { Name = "TenantId", DbType = DbType.Int64 }
            };

            var parameters2 = new ScopeInfoClientParameters
            {
                new ScopeInfoClientParameter { Name = "UserId", DbType = DbType.Int64 },
                new ScopeInfoClientParameter { Name = "TenantId", DbType = DbType.Int64 }
            };

            // Act
            var result = parameters1.CompareWith(parameters2);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void CompareWith_DifferentParameterCount_ShouldReturnFalse()
        {
            // Arrange
            var parameters1 = new ScopeInfoClientParameters
            {
                new ScopeInfoClientParameter { Name = "UserId", DbType = DbType.Int64 }
            };

            var parameters2 = new ScopeInfoClientParameters
            {
                new ScopeInfoClientParameter { Name = "UserId", DbType = DbType.Int64 },
                new ScopeInfoClientParameter { Name = "TenantId", DbType = DbType.Int64 }
            };

            // Act
            var result = parameters1.CompareWith(parameters2);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void CompareWith_DifferentParameterTypes_ShouldReturnFalse()
        {
            // Arrange
            var parameters1 = new ScopeInfoClientParameters
            {
                new ScopeInfoClientParameter { Name = "UserId", DbType = DbType.Int64 }
            };

            var parameters2 = new ScopeInfoClientParameters
            {
                new ScopeInfoClientParameter { Name = "UserId", DbType = DbType.Int32 }
            };

            // Act
            var result = parameters1.CompareWith(parameters2);

            // Assert
            Assert.False(result);
        }
    }
}
