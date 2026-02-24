![DMS](docs/assets/Smallicon.svg)

[![NuGet version (Dotmim.Sync.Core)](https://img.shields.io/nuget/v/Dotmim.Sync.Core.svg)](https://www.nuget.org/packages?q=dotmim.sync)
[![Build Status](https://dev.azure.com/dotmim/Dotmim.Sync/_apis/build/status/Tests)](https://dev.azure.com/dotmim/Dotmim.Sync/_build/latest?definitionId=9)
[![Documentation Status](https://readthedocs.org/projects/dotmimsync/badge/?version=master)](https://dotmimsync.readthedocs.io/?badge=master)

## Documentation

Read the full documentation on [https://dotmimsync.readthedocs.io/](https://dotmimsync.readthedocs.io/)

## Dotmim.Sync

**DotMim.Sync** (**DMS**) is a straightforward framework for syncing relational databases, developed on top of **.Net 8** (and compatible with **.Net Standard 2.0**), available and ready to use within  **IOT**, **Xamarin**, **.NET**, **UWP** and so on :)  

Multi Databases | Cross Plaform |  .Net 8 / .Net Standard 2.0
-------------|---------------------|--------------------
![](docs/assets/CrossPlatform.png) | ![](docs/assets/MultiOS.png) | ![](docs/assets/NetCore.png)

![](docs/assets/Architecture01.svg)

## TL;DR

Here is the easiest way to create a first sync, from scratch :

* Create a **.Net 8 or 6** project or a **.Net Standard 2.0** compatible project (like a **.Net Core 3.1** or **.Net Fx 4.8** console application).  
* Add the **nugets** packages [Dotmim.Sync.SqlServer](https://www.nuget.org/packages/Dotmim.Sync.SqlServer/) (or [Dotmim.Sync.MySql](https://www.nuget.org/packages/Dotmim.Sync.MySql/) if you want to tests MySql) and [Dotmim.Sync.Sqlite](https://www.nuget.org/packages/Dotmim.Sync.Sqlite/)
* Choose one database for testing:
  * Either **SQL Server** test database : [AdventureWorks lightweight script for SQL Server](/CreateAdventureWorks.sql)  
  * Or **MySql** test database :  [AdventureWorks lightweight script for MySQL Server](/CreateMySqlAdventureWorks.sql)  
* Add this code :

``` csharp
// Sql Server provider, the "server" or "hub".
SqlSyncProvider serverProvider = new SqlSyncProvider(
    @"Data Source=.;Initial Catalog=AdventureWorks;Integrated Security=true;");

// Sqlite Client provider acting as the "client"
SqliteSyncProvider clientProvider = new SqliteSyncProvider("advworks.db");

// Tables involved in the sync process:
var setup = new SyncSetup("ProductCategory", "ProductDescription", "ProductModel", 
                          "Product", "ProductModelProductDescription", "Address", 
                          "Customer", "CustomerAddress", "SalesOrderHeader", "SalesOrderDetail");

// Sync agent
SyncAgent agent = new SyncAgent(clientProvider, serverProvider);

do
{
    var result = await agent.SynchronizeAsync(setup);
    Console.WriteLine(result);

} while (Console.ReadKey().Key != ConsoleKey.Escape);
```

And here is the result you should have, after a few seconds:

``` cmd
Synchronization done.
        Total changes  uploaded: 0
        Total changes  downloaded: 2752
        Total changes  applied: 2752
        Total resolved conflicts: 0
        Total duration :0:0:3.776
```

You're done !

Now try to update a row in your client or server database, then hit enter again.
You should see something like that:

``` cmd
Synchronization done.
        Total changes  uploaded: 0
        Total changes  downloaded: 1
        Total changes  applied: 1
        Total resolved conflicts: 0
        Total duration :0:0:0.045
```

Yes it's blazing fast !

## Scale-Out Packages

For production deployments with multiple server instances (load-balanced, containerized, etc.), DMS provides additional packages that enable distributed batch storage and job processing.

### Dotmim.Sync.Web.Azure

[![NuGet](https://img.shields.io/nuget/v/Dotmim.Sync.Web.Azure.svg)](https://www.nuget.org/packages/Dotmim.Sync.Web.Azure/)

Azure Blob Storage implementation for batch file storage. Use this when running multiple server instances that need to share batch files.

**When to use:**
- Multiple API server instances behind a load balancer
- Kubernetes/container deployments where local disk is ephemeral
- When clients may hit different server instances between requests

**Installation:**
```bash
dotnet add package Dotmim.Sync.Web.Azure
```

**Configuration:**
```csharp
// In Startup.cs / Program.cs
services.AddDotmimSyncAzure(
    connectionString: "DefaultEndpointsProtocol=https;AccountName=...;AccountKey=...;",
    containerName: "sync-batches");

// Or with options
services.AddDotmimSyncAzure(options => {
    options.ConnectionString = Configuration["Azure:StorageConnectionString"];
    options.ContainerName = "sync-batches";
});
```

The `IBatchStorage` will be automatically injected and used for all batch operations.

---

### Dotmim.Sync.Web.Hangfire

[![NuGet](https://img.shields.io/nuget/v/Dotmim.Sync.Web.Hangfire.svg)](https://www.nuget.org/packages/Dotmim.Sync.Web.Hangfire/)

Hangfire-based distributed batch job processing for async batch creation. Use this when you need reliable, distributed background job processing with automatic retries.

**When to use:**
- Async batch creation where the client polls for completion
- Scale-out deployments where any server instance should be able to process jobs
- When you need job persistence, retries, and monitoring via Hangfire Dashboard

**Installation:**
```bash
dotnet add package Dotmim.Sync.Web.Hangfire
```

**Configuration:**
```csharp
// In Startup.cs / Program.cs

// 1. Configure a distributed cache (required for job state storage)
//    Choose one: Redis, SQL Server, or any IDistributedCache implementation
//    for example for Redis, use [Microsoft.Extensions.Caching.StackExchangeRedis](https://www.nuget.org/packages/Microsoft.Extensions.Caching.StackExchangeRedis)
services.AddStackExchangeRedisCache(options => {
    options.Configuration = "localhost:6379";
});

// Or use SQL Server distributed cache [Microsoft.Extensions.Caching.SqlServer](https://www.nuget.org/packages/Microsoft.Extensions.Caching.SqlServer)
// services.AddDistributedSqlServerCache(options => {
//     options.ConnectionString = connectionString;
//     options.SchemaName = "dbo";
//     options.TableName = "SyncJobCache";
// });

// 2. Configure Hangfire with persistent storage
services.AddHangfire(config => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseSqlServerStorage(connectionString)); // Or UseRedisStorage, etc.

services.AddHangfireServer();

// 3. Register Wormhole.Sync Hangfire services
services.AddDotmimSyncHangfire(
    jobStoreOptions => {
        jobStoreOptions.KeyPrefix = "myapp:sync:jobs";
        jobStoreOptions.SlidingExpiration = TimeSpan.FromHours(2);
        jobStoreOptions.AbsoluteExpiration = TimeSpan.FromHours(24);
    },
    jobServiceOptions => {
        jobServiceOptions.QueueName = "sync-batches";
    });

// 4. Register the batch creation executor
services.AddTransient<IBatchCreationExecutor, BatchCreationExecutor>();
```

**What gets registered:**
- `IBatchJobStore` - Distributed job state storage using `IDistributedCache`
- `IBatchCreationJobService` - Service for enqueueing and tracking batch creation jobs
- `HangfireBatchCreationJob` - The actual Hangfire job with automatic retry (3 attempts)

**Using both Azure and Hangfire together:**

For a fully distributed setup, combine both packages:

```csharp
// Distributed batch FILE storage (Azure Blob)
services.AddDotmimSyncAzure(
    Configuration["Azure:StorageConnectionString"],
    "sync-batches");

// Distributed batch JOB processing (Hangfire + Redis)
services.AddStackExchangeRedisCache(options => {
    options.Configuration = Configuration["Redis:ConnectionString"];
});

services.AddHangfire(config => config
    .UseSqlServerStorage(Configuration["Hangfire:ConnectionString"]));
services.AddHangfireServer();

services.AddDotmimSyncHangfire();
services.AddTransient<IBatchCreationExecutor, BatchCreationExecutor>();
```

This setup ensures:
- Batch files are stored in Azure Blob Storage (accessible by all server instances)
- Job state is stored in Redis (shared across all server instances)
- Jobs are processed by Hangfire (any server can pick up and process jobs)

## Schema Evolution

DMS supports **additive schema evolution** — you can add new nullable columns or new tables to your server database, and old clients (running with the previous schema) continue to sync without errors or redeployment. New clients can upgrade at their own pace.

### How It Works

1. **Server migrates** using `MigrateSchemaAsync` — records the migration, reprovisions stored procedures with CASE/COALESCE logic
2. **Old clients keep syncing** — the server detects the additive difference and allows it; the SP preserves new column values when old clients upload partial data
3. **Clients upgrade when ready** — add the column, set `SupportedMigrations`, and the framework auto-reprovisions on the next sync.

### Server-Side Migration

On the server, call `MigrateSchemaAsync` with a new setup that includes the added columns or tables. Only **additive** changes are allowed (new nullable columns, new tables). Removing or renaming columns will throw.

```csharp
var remoteOrchestrator = new RemoteOrchestrator(serverProvider);

// Original setup
var setupV1 = new SyncSetup("ProductCategory");
setupV1.Tables["ProductCategory"].Columns.AddRange(
    "ProductCategoryId", "Name", "rowguid", "ModifiedDate");

// New setup with an additional column
var setupV2 = new SyncSetup("ProductCategory");
setupV2.Tables["ProductCategory"].Columns.AddRange(
    "ProductCategoryId", "Name", "rowguid", "ModifiedDate", "Description");

// Migrate — reprovisiones SPs/triggers, records migration in scope_info
await remoteOrchestrator.MigrateSchemaAsync("20260220_add_description", setupV2);
```

After migration, the server's stored procedures use `@sync_columns_present` to detect which columns the client actually sent. Columns not sent by an old client are preserved via `COALESCE([base].[col], [changes].[col])`.

### Old Clients Sync Without Changes

Old clients (still using V1 setup) continue to sync normally. The server detects the setup difference is additive and allows the sync:

```csharp
// Old client — no code changes needed, keeps using setupV1
var agent = new SyncAgent(clientProvider, serverProvider);
var result = await agent.SynchronizeAsync(setupV1);
// Works! Downloads new rows (without the new column), uploads as usual.
// When uploading, server preserves existing values for "Description".
```

### HTTP Sync

Schema evolution works over HTTP too. After migrating, restart the web server with the new setup:

```csharp
// Server-side: migrate, then reconfigure the web server
var remoteOrchestrator = new RemoteOrchestrator(serverProvider);
await remoteOrchestrator.MigrateSchemaAsync("20260220_add_description", setupV2);

// Restart Kestrel/IIS with the new setup
services.AddSyncServer(serverProvider, setupV2, options);

// Old clients (using WebRemoteOrchestrator) sync without changes
var agent = new SyncAgent(clientProvider, new WebRemoteOrchestrator(serverUri));
var result = await agent.SynchronizeAsync(setupV1); // still works
```

### Client Upgrade Path

When a client app is updated to use the new columns, two things are needed:

1. **Add the column** to the local database (your app's migration logic)
2. **Register the migration** via `SupportedMigrations`

The framework handles deprovisioning and reprovisioning automatically during the next sync — once, not on every call. If the server hasn't migrated yet, nothing happens; the client stays on the old schema until it connects to a server that has the migration.

```csharp
// 1. Add the column (your app's own migration/update logic)
using var connection = clientProvider.CreateConnection();
connection.Open();
var cmd = connection.CreateCommand();
cmd.CommandText = "ALTER TABLE ProductCategory ADD [Description] text NULL;";
await cmd.ExecuteNonQueryAsync();
connection.Close();

// 2. Tell the framework this client supports the migration, then sync normally
var agent = new SyncAgent(clientProvider, serverProvider);
agent.SupportedMigrations = new List<string> { "20260220_add_description" };

var result = await agent.SynchronizeAsync(setupV2);
// Framework auto-deprovisions old triggers, reprovisions with new schema,
// records the migration so it doesn't repeat, and syncs.
```

**What happens under the hood:**

- On sync, the framework compares `agent.SupportedMigrations` with the server's `ScopeInfo.Migrations`
- If the server has a migration the client supports but hasn't provisioned for yet, it automatically deprovisions old triggers/SPs and reprovisions with the server's updated schema
- The provisioned migrations are recorded in `ScopeInfoClient.SupportedMigrations` so this only happens once
- If the server hasn't migrated yet (e.g., connecting to a different server still on V1), nothing happens — the client keeps syncing with the old schema
- Incremental sync continues normally; new/changed rows will include the new column. To backfill existing rows, the framework uses `SyncType.Reinitialize` for the tables, that have new columns.

### Mixed Client Scenario

Old and new clients can coexist. When an old client updates a row, the server's SP logic preserves column values that the old client doesn't know about:

| Action | `Name` | `Description` |
|--------|--------|---------------|
| Server inserts row | "Bikes" | "All bikes" |
| Old client (V1) updates `Name` to "Bicycles" | "Bicycles" | "All bikes" (preserved) |
| New client (V2) syncs | "Bicycles" | "All bikes" |

### Declarative Migration Configuration (IOptions-based)

Instead of manually calling `MigrateSchemaAsync` and keeping your `AddSyncServer` setup in sync, you can declare all migrations during DI setup and apply them at startup. This uses the standard .NET **named options** pattern — each scope gets its own migration history.

#### Basic Usage

```csharp
// In Program.cs / Startup.cs

// 1. Register sync server with inline migration configuration
services.AddSyncServer(serverProvider, migrations =>
{
    migrations.AddInitialMigration(setupV1);                        // baseline — always first
    migrations.AddMigration("20260220_add_description", setupV2);   // additive change
});

// 2. At startup — apply pending migrations to the database
await app.ApplySyncMigrationsAsync();
```

No need to pass a `SyncSetup` — the `WebServerAgent` automatically resolves the latest setup from the last configured migration.

#### How It Works

1. **`AddInitialMigration(setup)`** registers the baseline schema with an empty migration ID (sorts first, never needs to be sent as a `SupportedMigration` by clients)
2. **`AddMigration(id, setup)`** registers subsequent additive changes with sortable IDs (e.g. date-prefixed)
3. **`ApplySyncMigrationsAsync()`** at startup iterates all registered scopes and:
   - On a **fresh database**: provisions the scope using the first migration's setup
   - For each **pending migration**: calls `MigrateSchemaAsync` to reprovision SPs/triggers
   - Already-applied migrations are **skipped** (idempotent)
4. The `WebServerAgent` resolves its `SyncSetup` from the latest migration — no manual hot-swap needed

#### Multiple Scopes

Each scope has its own migration history, using .NET named options:

```csharp
// Configure migrations per scope
services.Configure<SyncMigrationOptions>("SalesScope", o =>
{
    o.AddInitialMigration(salesSetupV1);
    o.AddMigration("20260301_add_discount", salesSetupV2);
});

services.Configure<SyncMigrationOptions>("InventoryScope", o =>
{
    o.AddInitialMigration(inventorySetupV1);
});

// Register each scope (reads setup from options automatically)
services.AddSyncServerWithMigrations(serverProvider, scopeName: "SalesScope");
services.AddSyncServerWithMigrations(serverProvider, scopeName: "InventoryScope");

await app.ApplySyncMigrationsAsync();
```

#### Multi-Tenant Deployments

Apply the same migration list to multiple tenant databases using the provider override:

```csharp
// Apply migrations to the "main" database (provider from AddSyncServer)
await app.ApplySyncMigrationsAsync();

// Apply the same migration list to each tenant database
foreach (var tenantCs in tenantConnectionStrings)
{
    await app.ApplySyncMigrationsAsync(new SqlSyncProvider(tenantCs));
}
```

#### External Provisioning

If you handle provisioning externally (e.g. via EF Code First migrations), set `AutoProvision = false`. The migration service will only record migration IDs without calling `ProvisionAsync`:

```csharp
services.AddSyncServer(serverProvider, migrations =>
{
    migrations.AutoProvision = false;
    migrations.AddInitialMigration(setupV1);
    migrations.AddMigration("20260220_v2", setupV2);
});
```

#### DI-Resolved Providers

For providers registered in DI (e.g. with connection strings from configuration):

```csharp
services.AddSyncServerWithMigrations<SqlSyncProvider>(migrations =>
{
    migrations.AddInitialMigration(setupV1);
    migrations.AddMigration("20260220_v2", setupV2);
});
```

#### Checking for Pending Migrations

Use `HasPendingSyncMigrationsAsync` to check whether any registered scope has unapplied migrations — useful for health checks, startup gates, or admin dashboards:

```csharp
bool hasPending = await app.Services.HasPendingSyncMigrationsAsync();

// Or check against a specific database (multi-tenant)
bool tenantPending = await app.Services.HasPendingSyncMigrationsAsync(
    new SqlSyncProvider(tenantConnectionString));
```

#### Progress Reporting

`ApplySyncMigrationsAsync` accepts an optional `IProgress<(string message, int percent)>` to report migration progress — useful for splash screens, admin UIs, or structured logging:

```csharp
var progress = new Progress<(string message, int percent)>(p =>
{
    Console.WriteLine($"[{p.percent}%] {p.message}");
});

await app.ApplySyncMigrationsAsync(progress);

// Output:
// [0%] Applying migration 1 of 3: (initial) [scope 'DefaultScope']
// [33%] Applying migration 2 of 3: 20260220_v2 [scope 'DefaultScope']
// [66%] Applying migration 3 of 3: 20260301_v3 [scope 'DefaultScope']
// [100%] Applied 3 migration(s)
```

The provider override variant also supports progress:

```csharp
foreach (var tenantCs in tenantConnectionStrings)
{
    await app.ApplySyncMigrationsAsync(new SqlSyncProvider(tenantCs), progress);
}
```

#### SyncOptions via IOptions Pattern

`SyncOptions` can be configured using the standard .NET options pattern. This is resolved automatically by all `AddSyncServer` overloads, `WebServerAgent`, `BatchCreationExecutor`, and the migration service:

```csharp
services.Configure<SyncOptions>(o =>
{
    o.ScopeInfoTableSuffix = "_v3";
    o.UseOptimizedFlow = true;
    o.BatchSize = 5000;
    o.BatchDirectory = Path.Combine(storageRoot, "SyncBatchTemp");
    o.AutoUpgrade = false;
});
```

Resolution precedence (first non-null wins):
1. Explicitly passed `SyncOptions` instance (e.g. `AddSyncServer(provider, setup, options: myOptions)`)
2. Direct singleton registration (`services.AddSingleton<SyncOptions>(...)`) — backwards compatible
3. `IOptions<SyncOptions>` pattern (`services.Configure<SyncOptions>(...)`)
4. Default `new SyncOptions()`

## Star History

[![Star History Chart](https://api.star-history.com/svg?repos=Mimetis/Dotmim.Sync&type=Date)](https://star-history.com/#Mimetis/Dotmim.Sync&Date)

## Need Help

* Check the full documentation, available here : [https://dotmimsync.readthedocs.io/](https://dotmimsync.readthedocs.io/)
* Feel free to ping me: [@sebpertus](http://www.twitter.com/sebpertus)
* DMS font is created from the awesome **Cubic** font from [https://www.dafont.com/cubic.font](https://www.dafont.com/cubic.font)


## Icon
Wormhole icon from https://www.flaticon.com/free-icon/wormhole_8967306?related_id=8966927&origin=search