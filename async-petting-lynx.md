# Async Batch Creation for Initial Synchronization - Implementation Complete

## Implementation Status

| Phase | Description | Status |
|-------|-------------|--------|
| Phase 1 | Batch Storage Abstraction | **COMPLETED** |
| Phase 2 | Async Job Service Foundation | **COMPLETED** |
| Phase 3 | Default Implementation (In-Memory + Channel) | **COMPLETED** |
| Phase 4 | Client Integration | **COMPLETED** |
| Phase 5 | Azure Blob Storage Implementation | **COMPLETED** |
| Phase 6 | Scale-Out Abstraction | **COMPLETED** |
| Phase 7 | Progressive Batch Streaming | **COMPLETED** |
| Phase 8 | Storage-Agnostic Batch Cleanup Service | **COMPLETED** |

All 8 phases have been implemented. The async batch creation feature is fully functional with storage-agnostic cleanup.

---

## Overview

Background batch creation for initial syncs in Dotmim.Sync with full scale-out support. When `IsNewScope=true` and async batching is enabled, the server enqueues batch creation to a background worker, allowing the HTTP request to poll for completion.

## Key Design Decisions

1. **Server-controlled**: Opt-in via `WebServerOptions.EnableAsyncBatchCreation` (default: false)
2. **Initial sync only**: Activates when `IsNewScope=true` or `SyncType == Reinitialize`
3. **IBatchStorage abstraction**: Pluggable batch file storage (local filesystem, Azure Blob, network drive)
4. **Two implementations**:
   - **Default**: In-memory state + Channel-based worker (single-server)
   - **Hangfire**: HybridCache state + distributed job queue (scale-out)
5. **Blocking with timeout**: HTTP blocks up to 30s, then returns 202 for client retry
6. **Progressive streaming**: Client can download batches while server creates more

## Architecture

```
Default (Single-Server):
┌─────────────────────────────────────────────────────────────┐
│ Client Request → WebServerAgent                              │
│      │                                                       │
│      ├── InMemoryBatchJobStore (ConcurrentDictionary)       │
│      │           │                                           │
│      │           v                                           │
│      │   Channel<string> → BatchCreationWorkerService       │
│      │                              │                        │
│      │                              v                        │
│      │                    InternalApplyThenGetChangesAsync   │
│      │                              │                        │
│      +──── Poll Status ←────────────┘                       │
└─────────────────────────────────────────────────────────────┘

Scale-Out (Hangfire + HybridCache + Shared Storage):
┌─────────────────────────────────────────────────────────────┐
│ Server A: Client Request → Enqueue Job                      │
│                  │                                           │
│                  v                                           │
│         HybridCache (Redis L2) ← Job Status                 │
│                  │                                           │
│                  v                                           │
│         Hangfire Queue (SQL/Redis)                          │
│                  │                                           │
│ Server B: ───────┘                                          │
│         BatchCreationWorkerService picks up job             │
│                  │                                           │
│                  v                                           │
│         IBatchStorage (Azure Blob/Shared Drive)             │
│                  │                                           │
│ Server C: Poll Status → Return BatchInfo to Client          │
└─────────────────────────────────────────────────────────────┘
```

---

## Implemented Files Summary

### Phase 1: Batch Storage Abstraction (Core) - COMPLETED

| File | Action | Location |
|------|--------|----------|
| `IBatchStorage.cs` | Created | `Dotmim.Sync.Core/Storage/` |
| `LocalFileSystemBatchStorage.cs` | Created | `Dotmim.Sync.Core/Storage/` |
| `AzureBlobBatchStorage.cs` | Created | `Dotmim.Sync.Core/Storage/` |
| `SyncOptions.cs` | Modified | Added `IBatchStorage BatchStorage` property |
| `BatchInfo.cs` | Modified | Added `IBatchStorage Storage` property |

### Phase 2: Async Job Service Foundation (Core) - COMPLETED

| File | Action | Location |
|------|--------|----------|
| `IBatchCreationJobService.cs` | Created | `Dotmim.Sync.Core/Async/` |
| `BatchCreationJobParameters.cs` | Created | `Dotmim.Sync.Core/Async/` |
| `BatchCreationJobStatus.cs` | Created | `Dotmim.Sync.Core/Async/` |

### Phase 3: Default Implementation (Web.Server) - COMPLETED

| File | Action | Location |
|------|--------|----------|
| `InMemoryBatchJobStore.cs` | Created | `Dotmim.Sync.Web.Server/Async/` |
| `DefaultBatchCreationJobService.cs` | Created | `Dotmim.Sync.Web.Server/Async/` |
| `BatchCreationWorkerService.cs` | Created | `Dotmim.Sync.Web.Server/Async/` |
| `WebServerOptions.cs` | Modified | Added async configuration properties |
| `WebServerAgent.cs` | Modified | Added async batching logic |
| `DependencyInjection.cs` | Modified | Registered async services |

### Phase 4: Client Integration (Web.Client) - COMPLETED

| File | Action | Location |
|------|--------|----------|
| `HttpMessage.cs` | Modified | Added `InProgress`, `AsyncProgress`, `RetryAfterSeconds`, `MoreBatchesPending`, `LastBatchIndex` |
| `WebRemoteOrchestrator.ApplyChanges.cs` | Modified | Added retry logic for async responses and progressive streaming |
| `HttpBatchCreationInProgressArgs.cs` | Created | `Dotmim.Sync.Core/Args/` |

### Phase 5: Azure Blob Storage (Core) - COMPLETED

| File | Action | Location |
|------|--------|----------|
| `AzureBlobBatchStorage.cs` | Created | `Dotmim.Sync.Core/Storage/` |

### Phase 6: Scale-Out Abstraction - COMPLETED

| File | Action | Location |
|------|--------|----------|
| `DependencyInjection.cs` | Modified | Added generic overload for custom `IBatchCreationJobService` |

### Phase 7: Progressive Batch Streaming - COMPLETED

| File | Action | Location |
|------|--------|----------|
| `BatchCreationJobStatus.cs` | Modified | Added `TotalBatchPartsCreated`, `AvailableBatchParts`, `LastAcknowledgedIndex` |
| `HttpMessage.cs` | Modified | Added `MoreBatchesPending`, `LastBatchIndex` |
| `BatchPartCreatedArgs.cs` | Created | `Dotmim.Sync.Core/Args/` |
| `WebServerAgent.cs` | Modified | Added `FirstBatchReady` state handling |
| `WebRemoteOrchestrator.ApplyChanges.cs` | Modified | Added progressive streaming loop |

### Tests - COMPLETED

| File | Action | Location |
|------|--------|----------|
| `HttpTests.AsyncBatchCreation.cs` | Created | `Dotmim.Sync.Tests/HttpTests/` |

---

## Key Implementation Details

### BatchCreationJobStatus.cs

The job status tracks progressive batch creation:

```csharp
public class BatchCreationJobStatus
{
    public string JobId { get; set; }
    public BatchCreationJobState State { get; set; }
    public DateTime EnqueuedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int ProgressPercentage { get; set; }
    public int TablesProcessed { get; set; }
    public int TotalTables { get; set; }
    public long? RemoteClientTimestamp { get; set; }
    public BatchInfo BatchInfo { get; set; }
    public DatabaseChangesSelected ChangesSelected { get; set; }
    public DatabaseChangesApplied ChangesApplied { get; set; }
    public string ErrorMessage { get; set; }
    public string ErrorStackTrace { get; set; }

    // Progressive streaming fields
    public int TotalBatchPartsCreated { get; set; }
    public List<BatchPartInfo> AvailableBatchParts { get; set; } = new List<BatchPartInfo>();
    public int LastAcknowledgedIndex { get; set; } = -1;
}
```

### HttpMessageSummaryResponse Properties

```csharp
// Async batch creation
public bool InProgress { get; set; }
public int? AsyncProgress { get; set; }
public int? RetryAfterSeconds { get; set; }

// Progressive streaming
public bool MoreBatchesPending { get; set; }
public int? LastBatchIndex { get; set; }
```

### WebServerOptions Configuration

```csharp
public class WebServerOptions
{
    public bool EnableAsyncBatchCreation { get; set; } = false;
    public TimeSpan AsyncBatchTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan AsyncBatchPollingInterval { get; set; } = TimeSpan.FromMilliseconds(500);
    public int AsyncBatchWorkerCount { get; set; } = 2;
}
```

### Client Retry Logic

The client handles async responses with retry polling:

```csharp
// Handle async batch creation (InProgress response from server)
if (summaryResponseContent.InProgress)
{
    const int maxRetries = 120; // Max ~10 minutes with 5-second default intervals
    var retryCount = 0;

    while (summaryResponseContent.InProgress && retryCount < maxRetries)
    {
        var retryDelay = TimeSpan.FromSeconds(summaryResponseContent.RetryAfterSeconds ?? 5);
        await Task.Delay(retryDelay, cancellationToken);

        // Poll for status
        response = await this.ProcessRequestAsync(...);
        summaryResponseContent = await DeserializeResponse(response);
        retryCount++;
    }
}
```

### Progressive Batch Streaming

The client polls for more batches while `MoreBatchesPending` is true:

```csharp
// Handle progressive batch streaming
if (summaryResponseContent.MoreBatchesPending)
{
    var lastReceivedBatchIndex = summaryResponseContent.LastBatchIndex ?? -1;

    while (summaryResponseContent.MoreBatchesPending)
    {
        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);

        // Request more batches
        response = await this.ProcessRequestAsync(...);
        var moreBatchesResponse = await DeserializeResponse(response);

        // Download newly available batches
        var newBatchParts = moreBatchesResponse.BatchInfo.BatchPartsInfo
            .Where(bpi => bpi.Index > lastReceivedBatchIndex);

        foreach (var bpi in newBatchParts)
        {
            await DownloadBatchPartAsync(bpi, ...);
            lastReceivedBatchIndex = Math.Max(lastReceivedBatchIndex, bpi.Index);
        }

        summaryResponseContent = moreBatchesResponse;
    }
}
```

---

## Configuration Examples

### Basic Async Batch Creation

```csharp
var webServerOptions = new WebServerOptions
{
    EnableAsyncBatchCreation = true,
    AsyncBatchTimeout = TimeSpan.FromSeconds(60),
    AsyncBatchPollingInterval = TimeSpan.FromMilliseconds(200),
    AsyncBatchWorkerCount = 2,
};

services.AddSyncServer(provider, setup, options, webServerOptions);
```

### With Azure Blob Storage

```csharp
var options = new SyncOptions
{
    BatchStorage = new AzureBlobBatchStorage(connectionString, containerName)
};

var webServerOptions = new WebServerOptions
{
    EnableAsyncBatchCreation = true,
};

services.AddSyncServer(provider, setup, options, webServerOptions);
```

### Scale-Out with Custom Job Service

```csharp
// Implement your own distributed job service
public class MyHangfireBatchJobService : IBatchCreationJobService
{
    // Implementation using Hangfire + Redis/SQL
}

// Register
services.AddSyncServer<MyHangfireBatchJobService>(provider, setup, options, webServerOptions);
```

---

## Tests

The following HTTP tests verify async batch creation functionality:

1. **AsyncBatchCreation_InitialSync_ShouldComplete** - Basic async sync completion
2. **AsyncBatchCreation_WithShortTimeout_ShouldHandleInProgressResponse** - Handles 202 responses
3. **AsyncBatchCreation_ClientRetry_ShouldBeIdempotent** - Retry idempotency
4. **AsyncBatchCreation_Reinitialize_ShouldUseAsyncProcessing** - Works with reinitialize
5. **AsyncBatchCreation_WithClientChanges_ShouldApplyToServer** - Client changes still work
6. **AsyncBatchCreation_DisabledByDefault_ShouldUseSyncProcessing** - Default behavior unchanged

---

## Scale-Out Deployment Options

| Configuration | Job State | Batch Storage | Scale-Out | Sticky Sessions |
|--------------|-----------|---------------|-----------|-----------------|
| Default (no config) | In-Memory | Local Filesystem | No | N/A |
| Default + Azure Blob | In-Memory | Azure Blob | No* | N/A |
| Custom + Distributed Cache | Redis/SQL | Azure Blob | Yes | Not Required |

*Same server must process the job, but any server can serve the batch files.

---

## File Count Summary

**Total: 11 new files + 18 modified files = 29 files** (including Phase 8)

### New Files (11)
- `IBatchStorage.cs`
- `LocalFileSystemBatchStorage.cs`
- `AzureBlobBatchStorage.cs`
- `IBatchCreationJobService.cs`
- `BatchCreationJobParameters.cs`
- `BatchCreationJobStatus.cs`
- `InMemoryBatchJobStore.cs`
- `DefaultBatchCreationJobService.cs`
- `BatchCreationWorkerService.cs`
- `HttpBatchCreationInProgressArgs.cs`
- `BatchPartCreatedArgs.cs`

### Modified Files (18)
- `SyncOptions.cs`
- `BatchInfo.cs`
- `LocalJsonSerializer.cs`
- `UnifiedBatchSerializer.cs`
- `WebServerOptions.cs`
- `WebServerAgent.cs`
- `DependencyInjection.cs`
- `HttpMessage.cs`
- `WebRemoteOrchestrator.ApplyChanges.cs`
- `HttpTests.AsyncBatchCreation.cs` (tests)
- `IBatchStorage.cs` (Phase 8 - add `GetSubdirectoriesAsync`)
- `LocalFileSystemBatchStorage.cs` (Phase 8 - implement `GetSubdirectoriesAsync`)
- `AzureBlobBatchStorage.cs` (Phase 8 - implement `GetSubdirectoriesAsync`)
- `BatchCleanupService.cs` (Phase 8 - constructor injection + use `IBatchStorage`)
- `BatchCleanupServiceTests.cs` (Phase 8 - parameterized tests for both backends)

---

## Phase 8: Storage-Agnostic Batch Cleanup Service

### Overview

The `BatchCleanupService` currently uses direct filesystem operations (`Directory.Exists()`, `Directory.EnumerateDirectories()`, `Directory.Delete()`), which prevents it from cleaning up batches stored in Azure Blob Storage or other backends. This phase refactors the cleanup service to use the `IBatchStorage` abstraction.

### Implementation Steps

#### Step 1: Extend IBatchStorage Interface

**File:** `Dotmim.Sync.Core/Storage/IBatchStorage.cs`

Add new method to enumerate subdirectories (batch directories):

```csharp
/// <summary>
/// Gets all subdirectories (batch directories) under the specified root path.
/// </summary>
/// <param name="rootPath">The root directory/container path.</param>
/// <param name="cancellationToken">Cancellation token.</param>
/// <returns>Enumerable of subdirectory names (not full paths).</returns>
Task<IEnumerable<string>> GetSubdirectoriesAsync(
    string rootPath,
    CancellationToken cancellationToken = default);
```

#### Step 2: Implement in LocalFileSystemBatchStorage

**File:** `Dotmim.Sync.Core/Storage/LocalFileSystemBatchStorage.cs`

```csharp
public Task<IEnumerable<string>> GetSubdirectoriesAsync(
    string rootPath,
    CancellationToken cancellationToken = default)
{
    if (!Directory.Exists(rootPath))
        return Task.FromResult(Enumerable.Empty<string>());

    var directories = Directory.EnumerateDirectories(rootPath)
        .Select(Path.GetFileName);
    return Task.FromResult(directories);
}
```

#### Step 3: Implement in AzureBlobBatchStorage

**File:** `Dotmim.Sync.Core/Storage/AzureBlobBatchStorage.cs`

```csharp
public async Task<IEnumerable<string>> GetSubdirectoriesAsync(
    string rootPath,
    CancellationToken cancellationToken = default)
{
    var prefix = string.IsNullOrEmpty(rootPath) ? "" : NormalizePath(rootPath) + "/";
    var directories = new HashSet<string>();

    await foreach (var blobItem in containerClient.GetBlobsAsync(
        prefix: prefix, cancellationToken: cancellationToken).ConfigureAwait(false))
    {
        // Extract first path segment after prefix as "directory" name
        var relativePath = blobItem.Name.Substring(prefix.Length);
        var slashIndex = relativePath.IndexOf('/');
        if (slashIndex > 0)
        {
            var dirName = relativePath.Substring(0, slashIndex);
            directories.Add(dirName);
        }
    }

    return directories;
}
```

#### Step 4: Refactor BatchCleanupService

**File:** `Dotmim.Sync.Core/BatchCleanupService.cs`

1. Add constructor injection for `IBatchStorage`
2. Replace direct filesystem calls with storage abstraction methods
3. Keep backward-compatible default constructor

```csharp
public class BatchCleanupService : IBatchCleanupService
{
    private readonly IBatchStorage storage;

    public BatchCleanupService() : this(new LocalFileSystemBatchStorage()) { }

    public BatchCleanupService(IBatchStorage storage)
    {
        this.storage = storage ?? throw new ArgumentNullException(nameof(storage));
    }

    // Methods use this.storage for all operations:
    // - Directory.Exists() -> await storage.DirectoryExistsAsync()
    // - Directory.EnumerateDirectories() -> await storage.GetSubdirectoriesAsync()
    // - Directory.Delete() -> await storage.DeleteBatchDirectoryAsync()
}
```

#### Step 5: Update Tests

**File:** `Tests/Dotmim.Sync.Tests/UnitTests/BatchCleanup/BatchCleanupServiceTests.cs`

1. Create parameterized tests that run against both local filesystem and Azurite
2. Use existing Azurite patterns from `AzureBlobBatchStorageTests.cs`
3. Add `[Trait("Category", "Azurite")]` for Azure-specific tests

### Files to Modify

| File | Action |
|------|--------|
| `IBatchStorage.cs` | Add `GetSubdirectoriesAsync` method |
| `LocalFileSystemBatchStorage.cs` | Implement `GetSubdirectoriesAsync` |
| `AzureBlobBatchStorage.cs` | Implement `GetSubdirectoriesAsync` |
| `BatchCleanupService.cs` | Add constructor injection, use `IBatchStorage` |
| `BatchCleanupServiceTests.cs` | Add parameterized tests for both backends |

### Verification

1. Run existing tests to ensure no regressions:
   ```bash
   dotnet test --filter "FullyQualifiedName~BatchCleanupServiceTests"
   ```

2. Start Azurite for Azure tests:
   ```bash
   docker run -p 10000:10000 mcr.microsoft.com/azure-storage/azurite azurite-blob --blobHost 0.0.0.0
   ```

3. Run storage tests:
   ```bash
   dotnet test --filter "Category=Azurite"
   ```
