using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Dotmim.Sync
{
    /// <summary>
    /// Server feature keys for capability negotiation.
    /// </summary>
    public static class ServerCapabilities
    {
        /// <summary>
        /// Indicates if the server supports incremental sync protocol optimization.
        /// </summary>
        public const string SupportsIncrementalSync = "supportsIncrementalSync";

        /// <summary>
        /// Indicates if the server supports schema hash validation.
        /// </summary>
        public const string SupportsSchemaHashing = "supportsSchemaHashing";

        /// <summary>
        /// Indicates if the server supports session close integration in GetMoreChanges.
        /// </summary>
        public const string SupportsSessionClose = "supportsSessionClose";

        /// <summary>
        /// Indicates if the server supports detailed error reporting.
        /// </summary>
        public const string SupportsErrorReporting = "supportsErrorReporting";

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
            { ServerCapabilities.SupportsErrorReporting, true }, 
            { ServerCapabilities.SupportsIncrementalSync, true }, 
            { ServerCapabilities.SupportsSchemaHashing, true }, 
            { ServerCapabilities.SupportsSessionClose, true }
        });
    }
}