using Microsoft.Extensions.DependencyInjection;
using System;

namespace Wormhole.Sync.Web.Server
{
   /// <summary>
   /// Internal marker registered per AddSyncServer call (migration-based overloads).
   /// The migration service resolves IEnumerable&lt;SyncScopeRegistration&gt; to enumerate
   /// all scopes that need migration processing.
   /// </summary>
   internal class SyncScopeRegistration
   {
      /// <summary>
      /// Gets the scope name for this registration.
      /// </summary>
      public string ScopeName { get; }

      /// <summary>
      /// Gets the optional identifier for multi-provider scenarios.
      /// </summary>
      public string Identifier { get; }

      /// <summary>
      /// Gets the factory that creates the CoreProvider for this scope.
      /// </summary>
      public Func<IServiceProvider, CoreProvider> ProviderFactory { get; }

      /// <summary>
      /// Gets the factory that resolves SyncOptions for this scope.
      /// Returns the explicitly configured options, or falls back to DI-registered SyncOptions.
      /// </summary>
      public Func<IServiceProvider, SyncOptions> OptionsFactory { get; }

      public SyncScopeRegistration(string scopeName, string identifier,
         Func<IServiceProvider, CoreProvider> providerFactory,
         Func<IServiceProvider, SyncOptions> optionsFactory = null)
      {
         ScopeName = scopeName ?? SyncOptions.DefaultScopeName;
         Identifier = identifier;
         ProviderFactory = providerFactory ?? throw new ArgumentNullException(nameof(providerFactory));
         OptionsFactory = optionsFactory ?? (sp => DependencyInjection.ResolveSyncOptions(sp, null));
      }
   }
}
