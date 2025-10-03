using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Wormhole.Sync
{
    /// <summary>
    /// Server feature keys for capability negotiation.
    /// </summary>
    public static class ServerCapabilities
    {
        /// <summary>
        /// Indicates if the server supports incremental sync protocol optimization.
        /// </summary>
        public const string OptimizedSync = "optimized-sync";

        /// <summary>
        /// Indicates if the server supports detailed error reporting.
        /// </summary>
        public const string ErrorReporting = "error-reporting";

        /// <summary>
        /// Contains all currently supported capabilities of the server.
        /// Add new capabilities here, so they get added to the scope-infos correctly (updating the date the capabilities were changed)
        /// </summary>
        /// <returns></returns>
        public static ReadOnlyDictionary<string, object> GetServerCapabilities()
        {
            return Capabilities;
        }
        
        private static readonly ReadOnlyDictionary<string,object> Capabilities = new(new Dictionary<string, object>
        {
            { ServerCapabilities.ErrorReporting, true }, 
            { ServerCapabilities.OptimizedSync, true },
        });
    }
}