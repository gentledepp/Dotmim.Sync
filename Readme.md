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

## Star History

[![Star History Chart](https://api.star-history.com/svg?repos=Mimetis/Dotmim.Sync&type=Date)](https://star-history.com/#Mimetis/Dotmim.Sync&Date)

## Need Help

* Check the full documentation, available here : [https://dotmimsync.readthedocs.io/](https://dotmimsync.readthedocs.io/)
* Feel free to ping me: [@sebpertus](http://www.twitter.com/sebpertus)
* DMS font is created from the awesome **Cubic** font from [https://www.dafont.com/cubic.font](https://www.dafont.com/cubic.font)


## Icon
Wormhole icon from https://www.flaticon.com/free-icon/wormhole_8967306?related_id=8966927&origin=search