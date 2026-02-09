using Hangfire;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using Wormhole.Sync;
using Wormhole.Sync.Async;
using Wormhole.Sync.Storage;
using Wormhole.Sync.Web.Hangfire;
using Wormhole.Sync.Web.Server.Async;

namespace Wormhole.Sync.Tests.Core
{
   /// <summary>
   /// Job activator for Hangfire that resolves dependencies for batch creation jobs in tests.
   /// Manually constructs HangfireBatchCreationJob with required dependencies.
   /// </summary>
   public class TestJobActivator : JobActivator
   {
      private readonly IBatchJobStore jobStore;
      private readonly IBatchStorage batchStorage;
      private readonly CoreProvider provider;
      private readonly SyncOptions options;

      public TestJobActivator(IBatchJobStore jobStore, IBatchStorage batchStorage, CoreProvider provider, SyncOptions options)
      {
         this.jobStore = jobStore;
         this.batchStorage = batchStorage;
         this.provider = provider;
         this.options = options;
      }

      public override object ActivateJob(Type jobType)
      {
         if (jobType == typeof(HangfireBatchCreationJob))
         {
            var executor = new BatchCreationExecutor(
               this.options,
               this.provider,
               this.batchStorage,
               NullLogger<BatchCreationExecutor>.Instance);

            return new HangfireBatchCreationJob(
               this.jobStore,
               executor,
               NullLogger<HangfireBatchCreationJob>.Instance,
               new NullHangfireContextLoggerProvider<HangfireBatchCreationJob>());
         }

         return base.ActivateJob(jobType);
      }
   }
}
