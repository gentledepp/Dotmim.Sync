using System.Collections.Concurrent;

namespace Wormhole.Sync.Web.Server
{
   /// <summary>
   /// Thread-safe store for SyncSetup instances, allowing hot-swap without server restart.
   /// Registered as a singleton in DI so that scoped WebServerAgent instances always
   /// resolve the latest setup for their scope/identifier combination.
   /// </summary>
   public class SyncSetupStore
   {
      private readonly ConcurrentDictionary<string, SyncSetup> setups = new();

      /// <summary>
      /// Build a composite key from scope name and identifier.
      /// </summary>
      public static string MakeKey(string scopeName, string identifier)
         => $"{scopeName ?? SyncOptions.DefaultScopeName}|{identifier ?? ""}";

      /// <summary>
      /// Register a setup for the given scope/identifier if not already present.
      /// </summary>
      public void Register(string scopeName, string identifier, SyncSetup setup)
         => this.setups.TryAdd(MakeKey(scopeName, identifier), setup);

      /// <summary>
      /// Resolve the current setup for the given scope/identifier.
      /// </summary>
      public SyncSetup Resolve(string scopeName, string identifier)
         => this.setups.TryGetValue(MakeKey(scopeName, identifier), out var s) ? s : null;

      /// <summary>
      /// Update (overwrite) the setup for the given scope/identifier.
      /// </summary>
      public void Update(string scopeName, string identifier, SyncSetup setup)
         => this.setups[MakeKey(scopeName, identifier)] = setup;
   }
}
