using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Wormhole.Sync.Web.Server
{
   /// <summary>
   /// Internal service that applies pending schema migrations at startup.
   /// Iterates over all registered <see cref="SyncScopeRegistration"/> instances,
   /// resolves their <see cref="SyncMigrationOptions"/> via named options, and applies
   /// any migrations not yet recorded in the server's ScopeInfo.
   /// </summary>
   internal class SyncMigrationService
   {
      private readonly IEnumerable<SyncScopeRegistration> registrations;
      private readonly IOptionsMonitor<SyncMigrationOptions> optionsMonitor;
      private readonly SyncSetupStore setupStore;

      public SyncMigrationService(
         IEnumerable<SyncScopeRegistration> registrations,
         IOptionsMonitor<SyncMigrationOptions> optionsMonitor,
         SyncSetupStore setupStore)
      {
         this.registrations = registrations;
         this.optionsMonitor = optionsMonitor;
         this.setupStore = setupStore;
      }

      /// <summary>
      /// Check whether any registered scope has pending migrations.
      /// </summary>
      public async Task<bool> HasPendingAsync(IServiceProvider serviceProvider, CoreProvider providerOverride = null, CancellationToken cancellationToken = default)
      {
         foreach (var (reg, options, migrations) in GetDistinctScopes())
         {
            if (migrations.Count == 0)
               continue;

            var provider = providerOverride ?? reg.ProviderFactory(serviceProvider);
            var syncOptions = reg.OptionsFactory(serviceProvider);
            var remoteOrchestrator = new RemoteOrchestrator(provider, syncOptions);

            var pending = await GetPendingMigrationsAsync(remoteOrchestrator, reg.ScopeName, migrations, cancellationToken).ConfigureAwait(false);
            if (pending.Count > 0)
               return true;
         }

         return false;
      }

      /// <summary>
      /// Apply all pending migrations for all registered scopes.
      /// </summary>
      public Task ApplyAsync(IServiceProvider serviceProvider, CoreProvider providerOverride = null, CancellationToken cancellationToken = default)
         => ApplyAsync(serviceProvider, providerOverride, null, cancellationToken);

      /// <summary>
      /// Apply all pending migrations for all registered scopes with progress reporting.
      /// </summary>
      public async Task ApplyAsync(IServiceProvider serviceProvider, CoreProvider providerOverride,
         IProgress<(string message, int percent)> progress, CancellationToken cancellationToken = default)
      {
         var scopes = GetDistinctScopes();

         // First pass: count total pending migrations across all scopes (for progress %)
         int totalPending = 0;
         var scopePendingList = new List<(SyncScopeRegistration reg, SyncMigrationOptions options, IReadOnlyList<SyncMigration> migrations, List<SyncMigration> pending)>();

         foreach (var (reg, options, migrations) in scopes)
         {
            if (migrations.Count == 0)
               continue;

            var provider = providerOverride ?? reg.ProviderFactory(serviceProvider);
            var syncOptions = reg.OptionsFactory(serviceProvider);
            var remoteOrchestrator = new RemoteOrchestrator(provider, syncOptions);

            var pending = await GetPendingMigrationsAsync(remoteOrchestrator, reg.ScopeName, migrations, cancellationToken).ConfigureAwait(false);
            if (pending.Count > 0)
            {
               scopePendingList.Add((reg, options, migrations, pending));
               totalPending += pending.Count;
            }
         }

         if (totalPending == 0)
         {
            progress?.Report(("No pending migrations", 100));
            return;
         }

         // Second pass: apply pending migrations with progress
         var counter = new int[] { 0 }; // single-element array to allow mutation in async

         foreach (var (reg, options, migrations, pending) in scopePendingList)
         {
            var provider = providerOverride ?? reg.ProviderFactory(serviceProvider);
            var syncOptions = reg.OptionsFactory(serviceProvider);
            var remoteOrchestrator = new RemoteOrchestrator(provider, syncOptions);

            await ApplyScopeAsync(remoteOrchestrator, reg.ScopeName, options, migrations, pending,
               progress, counter, totalPending, cancellationToken).ConfigureAwait(false);

            if (providerOverride == null)
               this.setupStore.Update(reg.ScopeName, reg.Identifier, options.CurrentSetup);
         }

         progress?.Report(($"Applied {totalPending} migration(s)", 100));
      }

      /// <summary>
      /// Returns distinct scopes (deduplicated by ScopeName + Identifier) with their options and migrations.
      /// </summary>
      private List<(SyncScopeRegistration reg, SyncMigrationOptions options, IReadOnlyList<SyncMigration> migrations)> GetDistinctScopes()
      {
         var result = new List<(SyncScopeRegistration, SyncMigrationOptions, IReadOnlyList<SyncMigration>)>();
         var processed = new HashSet<string>(StringComparer.Ordinal);

         foreach (var reg in this.registrations)
         {
            var key = SyncSetupStore.MakeKey(reg.ScopeName, reg.Identifier);
            if (!processed.Add(key))
               continue;

            var options = this.optionsMonitor.Get(reg.ScopeName);
            var migrations = options.GetMigrations();
            result.Add((reg, options, migrations));
         }

         return result;
      }

      /// <summary>
      /// Determine which migrations from the configured list have not been applied yet.
      /// </summary>
      private static async Task<List<SyncMigration>> GetPendingMigrationsAsync(
         RemoteOrchestrator remoteOrchestrator,
         string scopeName,
         IReadOnlyList<SyncMigration> migrations,
         CancellationToken cancellationToken)
      {
         cancellationToken.ThrowIfCancellationRequested();
            
         List<string> appliedMigrations;
         try
         {
            var scopeInfo = await remoteOrchestrator.GetScopeInfoAsync(scopeName).ConfigureAwait(false);

            // only include initial migration if scope does not yet exist
            // since the initial migration is "virtual" (is not stored as such)
            if (scopeInfo?.Schema is not null)
                migrations = migrations.Where(m => !string.IsNullOrEmpty(m.MigrationId)).ToArray();

            appliedMigrations = scopeInfo?.GetMigrationsList() ?? new List<string>();
         }
         catch
         {
            appliedMigrations = new List<string>();
         }
         
         var pending = new List<SyncMigration>();

         foreach (var m in migrations)
         {
            if (!appliedMigrations.Contains(m.MigrationId, StringComparer.Ordinal))
               pending.Add(m);
         }

         return pending;
      }

      private static async Task ApplyScopeAsync(
         RemoteOrchestrator remoteOrchestrator,
         string scopeName,
         SyncMigrationOptions options,
         IReadOnlyList<SyncMigration> allMigrations,
         List<SyncMigration> pendingMigrations,
         IProgress<(string message, int percent)> progress,
         int[] counter,
         int totalPending,
         CancellationToken cancellationToken)
      {
         // Load scope info once
         ScopeInfo scopeInfo = null;
         try
         {
            scopeInfo = await remoteOrchestrator.GetScopeInfoAsync(scopeName).ConfigureAwait(false);
         }
         catch
         {
            // Fresh DB
         }

         bool scopeExists = scopeInfo?.Setup != null && scopeInfo?.Schema != null;

         foreach (var migration in pendingMigrations)
         {
            cancellationToken.ThrowIfCancellationRequested();

            counter[0]++;
            int percent = (int)((counter[0] - 1) / (double)totalPending * 100);
            var migrationLabel = string.IsNullOrEmpty(migration.MigrationId) ? "(initial)" : migration.MigrationId;
            progress?.Report(($"Applying migration {counter[0]} of {totalPending}: {migrationLabel} (scope '{scopeName}')", percent));

            bool isFirstConfigured = allMigrations.Count > 0
               && string.Equals(allMigrations[0].MigrationId, migration.MigrationId, StringComparison.Ordinal);

            if (!scopeExists && isFirstConfigured)
            {
               if (options.AutoProvision)
               {
                  scopeInfo = await remoteOrchestrator.ProvisionAsync(
                     scopeName, migration.Setup, cancellationToken: cancellationToken).ConfigureAwait(false);
               }
               else
               {
                  scopeInfo = await remoteOrchestrator.GetScopeInfoAsync(scopeName, migration.Setup).ConfigureAwait(false);
               }

               // we do NOT want to store the initial migration in the appliedmigrations list
               //scopeInfo.AddMigration(migration.MigrationId);
               await remoteOrchestrator.SaveScopeInfoAsync(scopeInfo).ConfigureAwait(false);

               scopeExists = true;
            }
            else if (!scopeExists)
            {
               continue;
            }
            else
            {
               scopeInfo = await remoteOrchestrator.MigrateSchemaAsync(
                  migration.MigrationId, migration.Setup, scopeName,
                  cancellationToken: cancellationToken).ConfigureAwait(false);
            }
         }
      }
   }
}
