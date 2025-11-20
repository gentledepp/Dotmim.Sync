# .NET 9 AOT/Trimming Compatibility Fix

## Original Problem

When using the Wormhole.Sync library in a .NET 9 Android application with AOT (Ahead-of-Time) compilation and IL trimming enabled, the application would throw runtime exceptions:

```
System.NotSupportedException: ConstructorContainsNullParameterNames, Wormhole.Sync.SyncParameter
```

### Android Project Configuration
The issue occurred specifically with these settings:
```xml
<PublishTrimmed>true</PublishTrimmed>
<RunAOTCompilation>true</RunAOTCompilation>
<AndroidEnableProfiledAot>true</AndroidEnableProfiledAot>
<AndroidLinkMode>SdkOnly</AndroidLinkMode>
```

The library worked perfectly in Debug mode but failed in Release mode with AOT enabled.

## Root Cause

The issue stemmed from two main problems:

### 1. Missing Constructor Selection
When System.Text.Json encounters a class with multiple constructors (e.g., a parameterless constructor and a parameterized constructor), it attempts to automatically choose which one to use for deserialization. In AOT/trimmed scenarios:
- Constructor parameter names are stripped during IL trimming
- System.Text.Json couldn't determine which constructor to use
- Without parameter names, it would throw `ConstructorContainsNullParameterNames`

### 2. Incomplete Source Generation Support
The library was using reflection-based JSON serialization, which doesn't work well with:
- IL trimming (removes metadata needed for reflection)
- AOT compilation (requires compile-time code generation)

## Solution Overview

We implemented a **hybrid serialization strategy** that combines:
1. **JSON Source Generation** for simple, frequently-used types
2. **JsonConstructor Attributes** to explicitly mark which constructor to use
3. **Updated DataContractResolver** to honor the `[JsonConstructor]` attribute

This approach provides trim/AOT compatibility while maintaining backward compatibility and avoiding code generation issues with complex types.

## Changes Made

### 1. Core Project (Dotmim.Sync.Core)

#### A. Added `[JsonConstructor]` Attributes
Marked parameterless constructors in 30+ classes to explicitly tell System.Text.Json which constructor to use:

**Files Modified:**
- `Parameter/SyncParameter.cs`
- `Parameter/SyncParameters.cs`
- `Serialization/SerializerInfo.cs`
- `Setup/SyncSetup.cs`
- `Set/SyncSet.cs`
- `Batch/BatchInfo.cs`
- `Batch/BatchPartInfo.cs`
- `SyncContext.cs`
- `Set/ContainerTable.cs`
- `Messages/TableChangesSelected.cs`
- `Setup/ScopeInfoClientParameter.cs`

**Example Change:**
```csharp
[DataContract(Name = "par"), Serializable]
public class SyncParameter : SyncNamedItem<SyncParameter>
{
    [JsonConstructor]  // <-- Added this
    public SyncParameter() { }

    public SyncParameter(string name, object value) { ... }

    [DataMember(Name = "pn", IsRequired = true, Order = 1)]
    [JsonPropertyName("pn")]  // <-- Added this
    public string Name { get; set; }

    [DataMember(Name = "v", IsRequired = true, Order = 2)]
    [JsonPropertyName("v")]  // <-- Added this
    public object Value { get; set; }
}
```

#### B. Added `[JsonPropertyName]` Attributes
Added explicit JSON property name mappings to ensure consistent serialization/deserialization in trimmed scenarios.

#### C. Created Source Generation Context
Created `Serialization/SyncJsonSerializerContext.cs` with JSON source generation for simple types:

```csharp
[JsonSerializable(typeof(SyncParameter))]
[JsonSerializable(typeof(SyncParameters))]
[JsonSerializable(typeof(SerializerInfo))]
[JsonSourceGenerationOptions(
   PropertyNameCaseInsensitive = true,
   DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
internal partial class SyncJsonSerializerContext : JsonSerializerContext
{
}
```

**Why only simple types?**
- Complex types like `SyncSetup` and `SyncSet` have deep dependency graphs
- Source generator had compilation errors with these complex types
- Simple types with `[JsonConstructor]` work fine with DataContractResolver

#### D. Updated JsonObjectSerializer
Modified `Serialization/JsonObjectSerializer.cs` to use the hybrid approach:

```csharp
private static readonly JsonSerializerOptions Options = new()
{
    // Use source generation context first for trim/AOT compatibility,
    // fall back to DataContractResolver for other types
    TypeInfoResolver = JsonTypeInfoResolver.Combine(
        SyncJsonSerializerContext.Default,
        new DataContractResolver()),
    Converters = { new ArrayJsonConverter(), new ObjectToInferredTypesConverter() },
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
};
```

#### E. **Critical Fix: Updated DataContractResolver**
This was the key fix that resolved the issue. Updated `Serialization/DataContractResolver.cs` to honor `[JsonConstructor]` attributes:

```csharp
public override JsonTypeInfo GetTypeInfo(Type type, JsonSerializerOptions options)
{
    var jsonTypeInfo = base.GetTypeInfo(type, options);

    if (jsonTypeInfo.Kind != JsonTypeInfoKind.Object)
        return jsonTypeInfo;

    jsonTypeInfo.Properties.Clear();

    // Check for [JsonConstructor] attribute to use the correct constructor
    var constructors = type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
    var jsonConstructor = constructors.FirstOrDefault(c => c.GetCustomAttribute<JsonConstructorAttribute>() != null);

    if (jsonConstructor != null)
    {
        // Use the constructor marked with [JsonConstructor]
        // Pass empty array instead of null for parameterless constructors (AOT-safe)
        jsonTypeInfo.CreateObject = () => jsonConstructor.Invoke(Array.Empty<object>());
    }

    var ti = GetTypeInfo(jsonTypeInfo, options);

    return ti;
}
```

**Why this fix was critical:**
- The original `DataContractResolver` only handled property serialization
- It didn't specify which constructor to use
- System.Text.Json would try to infer the constructor, failing in AOT scenarios
- Now it explicitly uses the constructor marked with `[JsonConstructor]`
- **Important:** Using `Array.Empty<object>()` instead of `null` as the parameter is crucial for AOT - passing `null` was causing `ConstructorContainsNullParameterNames` errors because it's ambiguous (could mean "no arguments" or "one null argument")

#### F. **Fix for Object-Type Properties in Source Generation**
Added explicit converter attribute to `SyncParameter.Value` property to support source generation with `System.Object` type:

```csharp
[DataMember(Name = "v", IsRequired = true, Order = 2)]
[JsonPropertyName("v")]
[JsonConverter(typeof(Serialization.ObjectToInferredTypesConverter))]
public object Value { get; set; }
```

**Why this fix was necessary:**
- `SyncParameter` is included in `SyncJsonSerializerContext` for source generation
- Source generation cannot handle `System.Object` properties without explicit converter attributes
- While `ObjectToInferredTypesConverter` is registered globally in `JsonSerializerOptions`, source-generated code doesn't automatically use global converters
- The explicit `[JsonConverter]` attribute tells the source generator how to handle the object-typed property
- This fixes the `SerializationNotSupportedParentType, System.Object` error

### 2. Web.Client Project (Dotmim.Sync.Web.Client)

#### A. Added `[JsonConstructor]` to All HttpMessage Classes
Updated `HttpMessage.cs` with `[JsonConstructor]` and `[JsonPropertyName]` attributes for all 17+ HttpMessage classes:

**Classes Modified:**
- `HttpMessageEnsureScopesRequest`
- `HttpMessageEnsureScopesResponse`
- `HttpMessageEnsureSchemaResponse`
- `HttpMessageSendChangesRequest`
- `HttpMessageSendChangesResponse`
- `HttpMessageGetMoreChangesRequest`
- `HttpMessageOperationRequest`
- `HttpMessageOperationResponse`
- `HttpMessageRemoteTimestampRequest`
- `HttpMessageRemoteTimestampResponse`
- `HttpMessageSummaryResponse`
- `HttpMessageEndSessionRequest`
- `HttpMessageEndSessionResponse`
- `HttpMessageSendChangesIncrementalRequest`
- `HttpMessageSendChangesIncrementalResponse`
- `HttpMessageSendSyncErrorsRequest`
- `HttpMessageSendSyncErrorsResponse`

**Example Change:**
```csharp
[DataContract(Name = "ensurescopesreq"), Serializable]
public class HttpMessageEnsureScopesRequest : IScopeMessage
{
    [JsonConstructor]  // <-- Added this
    public HttpMessageEnsureScopesRequest() { }

    public HttpMessageEnsureScopesRequest(SyncContext context) => this.SyncContext = context ?? throw new ArgumentNullException(nameof(context));

    [DataMember(Name = "sc", IsRequired = true, Order = 1)]
    [JsonPropertyName("sc")]  // <-- Added this
    public SyncContext SyncContext { get; set; }
}
```

#### B. Created WebJsonObjectSerializer (Later Removed)
Initially created a specialized serializer for Web.Client, but this became unnecessary after fixing the `DataContractResolver`. The fix in the Core project's `DataContractResolver` handles all types correctly, including HttpMessage types.

**File removed:**
- `Serialization/WebJsonObjectSerializer.cs` (created then removed)

The Core's `JsonObjectSerializer` now works for both Core and Web.Client types.

## Technical Details

### Why the Hybrid Approach?

1. **Source Generation Benefits:**
   - Compile-time code generation (AOT-compatible)
   - No reflection overhead
   - Trim-safe (no metadata required at runtime)

2. **Source Generation Limitations:**
   - Can't handle very complex type hierarchies
   - Compilation errors with deep dependency graphs
   - Not practical for 100+ types

3. **DataContractResolver + JsonConstructor:**
   - Works with existing DataContract/DataMember attributes
   - Handles complex types that source generation can't
   - Trim-safe when using `[JsonConstructor]` (no parameter name reflection needed)
   - Maintains backward compatibility

### Type Resolver Chain

The serializer uses this resolution order:
1. **SyncJsonSerializerContext** - Source-generated code for simple types (SyncParameter, SyncParameters, SerializerInfo)
2. **DataContractResolver** - Reflection-based (but trim-safe with `[JsonConstructor]`) for all other types

### Why Constructor Invocation Works in AOT

The key line in the fix:
```csharp
jsonTypeInfo.CreateObject = () => jsonConstructor.Invoke(Array.Empty<object>());
```

This works in AOT/trimmed scenarios because:
- We're using `GetConstructors()` to verify `[JsonConstructorAttribute]` exists, which is metadata preserved at runtime
- We're explicitly invoking the specific constructor marked with `[JsonConstructor]`
- `Array.Empty<object>()` is unambiguous - it clearly means "parameterless constructor" (unlike `null` which is ambiguous)
- The lambda is compiled ahead-of-time
- No parameter name reflection is required for parameterless constructors

## Testing

The changes have been tested with:
- ✅ .NET 9 Android with AOT compilation
- ✅ PublishTrimmed=true
- ✅ RunAOTCompilation=true
- ✅ Debug mode (existing functionality)
- ✅ Release mode with AOT

## Backward Compatibility

All changes are backward compatible:
- `[JsonConstructor]` and `[JsonPropertyName]` attributes are only hints to System.Text.Json
- Existing DataContract/DataMember attributes still work
- Serialized JSON format remains unchanged
- No breaking API changes

## Performance Impact

Minimal to positive:
- Source-generated code for simple types is faster than reflection
- Complex types use the same DataContractResolver as before
- Constructor selection is now explicit (no inference overhead)

## Future Improvements

Potential enhancements for future versions:
1. Expand source generation to more simple types as needed
2. Consider using interceptors (C# 12+) for even better AOT performance
3. Add automated AOT/trimming compatibility tests to CI/CD

## Files Changed Summary

### Core Project
- `Parameter/SyncParameter.cs` - Added JsonConstructor, JsonPropertyName, and JsonConverter for object-typed Value property
- `Parameter/SyncParameters.cs` - Added JsonConstructor and JsonPropertyName
- `Serialization/SerializerInfo.cs` - Added JsonConstructor
- `Setup/SyncSetup.cs` - Added JsonConstructor and JsonPropertyName
- `Set/SyncSet.cs` - Added JsonConstructor and JsonPropertyName
- `Batch/BatchInfo.cs` - Added JsonConstructor and JsonPropertyName
- `Batch/BatchPartInfo.cs` - Added JsonConstructor and JsonPropertyName
- `SyncContext.cs` - Added JsonConstructor and JsonPropertyName
- `Set/ContainerTable.cs` - Added JsonConstructor and JsonPropertyName (both ContainerTable and ContainerTableColumn)
- `Messages/TableChangesSelected.cs` - Added JsonConstructor and JsonPropertyName
- `Setup/ScopeInfoClientParameter.cs` - Added JsonConstructor and JsonPropertyName
- `Serialization/SyncJsonSerializerContext.cs` - **NEW FILE** - Source generation context
- `Serialization/JsonObjectSerializer.cs` - Updated to use source generation context
- `Serialization/DataContractResolver.cs` - **CRITICAL FIX** - Added JsonConstructor support using Array.Empty<object>() for parameterless constructor invocation (AOT-safe)

### Web.Client Project
- `HttpMessage.cs` - Added JsonConstructor and JsonPropertyName to all 17+ HttpMessage classes

## References

- [System.Text.Json source generation](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/source-generation)
- [Prepare .NET libraries for trimming](https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/prepare-libraries-for-trimming)
- [JsonConstructorAttribute](https://learn.microsoft.com/en-us/dotnet/api/system.text.json.serialization.jsonconstructorattribute)
- [Native AOT deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)
