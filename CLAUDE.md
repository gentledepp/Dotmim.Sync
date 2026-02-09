# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## About Dotmim.Sync (Wormhole.Sync)

Dotmim.Sync (also known as Wormhole.Sync internally) is a framework for syncing relational databases across .NET platforms. It supports bidirectional synchronization between SQL Server, MySQL, MariaDB, PostgreSQL, and SQLite databases.

**Key Concepts:**
- **Client/Server sync**: SyncAgent orchestrates sync between LocalOrchestrator (client) and RemoteOrchestrator (server)
- **Web sync**: WebServerAgent handles HTTP-based sync for remote clients via WebRemoteOrchestrator
- **Providers**: CoreProvider implementations (SqlSyncProvider, SqliteSyncProvider, etc.) handle database-specific operations
- **Orchestrators**: BaseOrchestrator → LocalOrchestrator/RemoteOrchestrator contain sync logic
- **Scopes**: ScopeInfo (server) and ScopeInfoClient (client) track sync state
- **Batches**: Changes are serialized into batch files for transfer
- **Interceptors**: Event-based hooks for customizing sync behavior

## Development Commands

### Build
```bash
dotnet build Dotmim.Sync.sln
```

### Run All Tests
```bash
dotnet test Tests/Dotmim.Sync.Tests/Dotmim.Sync.Tests.csproj
```

### Run Specific Test
```bash
# Run a specific test class
dotnet test Tests/Dotmim.Sync.Tests/Dotmim.Sync.Tests.csproj --filter "FullyQualifiedName~SqlServerTcp"

# Run a specific test method
dotnet test Tests/Dotmim.Sync.Tests/Dotmim.Sync.Tests.csproj --filter "FullyQualifiedName~TestClassName.TestMethodName"
```

### Run Tests by Framework
```bash
# Run on .NET 6
dotnet test Tests/Dotmim.Sync.Tests/Dotmim.Sync.Tests.csproj --framework net6.0

# Run on .NET 8
dotnet test Tests/Dotmim.Sync.Tests/Dotmim.Sync.Tests.csproj --framework net8.0
```

### Restore Packages
```bash
dotnet restore Dotmim.Sync.sln
```

### Package/Publish
NuGet packages are generated on Release builds. Version is controlled in `Directory.Build.props`.

## Project Structure

```
Projects/
├── Dotmim.Sync.Core/              # Core sync engine (published as Wormhole.Sync.Core)
│   ├── SyncAgent.cs               # Main orchestrator for direct sync
│   ├── Orchestrators/             # LocalOrchestrator, RemoteOrchestrator, BaseOrchestrator
│   ├── Set/                       # Schema model (SyncTable, SyncColumn, SyncFilter, etc.)
│   ├── Scopes/                    # Scope tracking (ScopeInfo, ScopeInfoClient)
│   ├── Batch/                     # Batch file handling
│   ├── Serialization/             # Serializers (JSON, binary)
│   ├── Interceptors/              # Event hooks for customization
│   ├── Storage/                   # IBatchStorage abstraction
│   └── Async/                     # Async batch creation interfaces
│
├── Dotmim.Sync.SqlServer/         # SQL Server provider
├── Dotmim.Sync.SqlServer.ChangeTracking/  # SQL Server with Change Tracking
├── Dotmim.Sync.Sqlite/            # SQLite provider
├── Dotmim.Sync.MySql/             # MySQL provider
├── Dotmim.Sync.MariaDB/           # MariaDB provider
├── Dotmim.Sync.PostgreSql/        # PostgreSQL provider
│
├── Dotmim.Sync.Web.Server/        # ASP.NET Core/WebAPI server components
│   ├── WebServerAgent.cs          # HTTP endpoint handler
│   ├── Session/                   # Session management (ISession wrapper)
│   └── Async/                     # Default async batch implementation
│
├── Dotmim.Sync.Web.Client/        # HTTP client orchestrator
│   └── WebRemoteOrchestrator.cs   # Talks to WebServerAgent
│
├── Dotmim.Sync.Web.Azure/         # Azure Blob Storage for batch files (scale-out)
└── Dotmim.Sync.Web.Hangfire/      # Hangfire integration for distributed job processing

Tests/
├── Dotmim.Sync.Tests/             # Main test suite (xUnit)
│   ├── IntegrationTests/          # Full sync tests
│   ├── UnitTests/                 # Unit tests for orchestrators
│   └── HttpTests/                 # HTTP sync tests
└── Dotmim.Sync.Tests.WebApi2/     # .NET Framework 4.8 web tests

Samples/                            # Example projects
```

## Architecture Overview

### Core Sync Flow

1. **SyncAgent** coordinates between LocalOrchestrator (client) and RemoteOrchestrator (server)
2. **Setup**: Defines tables to sync (SyncSetup) with optional filters
3. **Provisioning**: Creates tracking tables, triggers, stored procedures on server
4. **GetChanges**: Server reads changes since last sync into BatchInfo
5. **ApplyChanges**: Client applies server changes, detects conflicts
6. **Upload**: Client uploads local changes to server
7. **Cleanup**: Batch files are deleted after successful sync

### Web Sync Flow

1. Client creates **WebRemoteOrchestrator** pointing to server URL
2. Server exposes **WebServerAgent** via ASP.NET Core controller
3. All communication uses HTTP POST with serialized messages
4. Session state can use ASP.NET Session, IDistributedCache, or in-memory

### Scale-Out Architecture (New in v1.7.0)

For multi-server deployments:
- **IBatchStorage**: Pluggable batch storage (Azure Blob, shared filesystem)
- **IBatchCreationJobService**: Distributed job processing (Hangfire implementation available)
- **ISessionCacheStore**: Shared session state (Redis, SQL Server)
- Async batch creation: Server enqueues batch creation, client polls for completion

## Database Providers

Each provider inherits from `CoreProvider` and implements:
- `CreateConnection()`: Create DbConnection
- `GetDatabaseBuilder()`: Create/drop databases
- `GetSyncAdapter()`: Execute sync commands (insert/update/delete/select changes)
- `GetMetadataBuilder()`: Create tracking tables/triggers/procedures

Provider-specific features:
- SQL Server: Bulk operations, Change Tracking option
- SQLite: In-memory mode, encryption support
- MySQL/MariaDB: Very similar, MariaDB has separate provider for compatibility

## Interceptors and Events

Interceptors allow hooking into sync lifecycle:
- **Interceptors**: Type-safe async hooks (e.g., `OnTableChangesApplying`)
- **Progress**: IProgress<ProgressArgs> for reporting progress
- Both available on SyncAgent, LocalOrchestrator, RemoteOrchestrator

Common interceptor use cases:
- Modify data before/after apply
- Custom conflict resolution
- Logging and telemetry
- Schema customization (rename tracking tables, add indexes)

## Testing

Tests use:
- **xUnit** as test framework
- **Verify.Xunit** for snapshot testing (provisioning scripts)
- **DatabaseServerFixture** for database setup
- Multiple database providers tested via traits/filters

Test patterns:
- Integration tests sync between two databases
- HTTP tests use TestWebServer (in-memory ASP.NET Core)
- UnitTests use mocks for isolated orchestrator testing

To run tests, databases must be available (SQL Server, MySQL, PostgreSQL). Connection strings in `appsettings.json` or `appsettings.local.json`.

## Code Style

- C# 12.0 language version
- .NET Standard 2.0 / .NET 6 / .NET 8 multi-targeting
- StyleCop analyzers enabled (strict analysis)
- **IMPORTANT**: Use untabified layouts (spaces not tabs) with 3-space indentation (per user's CLAUDE.md)
- Namespace: Internal namespace is `Wormhole.Sync`, external package name is `Dotmim.Sync`

## Common Patterns

### Direct Sync (In-Process)
```csharp
var serverProvider = new SqlSyncProvider(serverConnectionString);
var clientProvider = new SqliteSyncProvider("client.db");
var setup = new SyncSetup("Table1", "Table2");
var agent = new SyncAgent(clientProvider, serverProvider);
var result = await agent.SynchronizeAsync(setup);
```

### HTTP Sync
```csharp
// Server (ASP.NET Core)
services.AddDotmimSyncServer<SqlSyncProvider>(connectionString, "Table1", "Table2");

// Client
var remoteOrchestrator = new WebRemoteOrchestrator("https://server/api/sync");
var localOrchestrator = new LocalOrchestrator(new SqliteSyncProvider("client.db"));
var agent = new SyncAgent(localOrchestrator, remoteOrchestrator);
var result = await agent.SynchronizeAsync();
```

### Filters
```csharp
var setup = new SyncSetup("Customer", "Order");
setup.Filters.Add("Customer", "CustomerId");
var parameters = new SyncParameters(("CustomerId", 123));
await agent.SynchronizeAsync(setup, parameters);
```

## Key Files to Understand

- **SyncAgent.cs**: Entry point for sync operations
- **BaseOrchestrator.cs**: Core orchestrator logic (partial class split across multiple files)
- **LocalOrchestrator.cs**: Client-side operations
- **RemoteOrchestrator.cs**: Server-side operations
- **WebServerAgent.cs**: HTTP endpoint handler (partial class, see WebServerAgent.OptimizedFlow.cs)
- **SyncOptions.cs**: Configuration (batch size, conflict resolution, session management)
- **Setup/SyncSetup.cs**: Defines which tables to sync

## Important Notes

- The project uses `Wormhole.Sync` as internal namespace but publishes as `Dotmim.Sync` NuGet packages
- Version 1.7.0 introduced async batch creation and scale-out features (Azure, Hangfire)
- Breaking change in 1.6.1: ConflictResolutionPolicy is no longer reversed by SyncAgent
- The framework supports .NET Standard 2.0 for broad compatibility (Xamarin, UWP, IoT)
- Test databases can be initialized using SQL scripts in repo root (CreateAdventureWorks*.sql)
- CI/CD uses Azure Pipelines (pipelines/*.yml)
