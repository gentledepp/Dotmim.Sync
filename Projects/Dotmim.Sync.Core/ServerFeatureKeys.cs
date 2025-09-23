namespace Dotmim.Sync
{
    /// <summary>
    /// Server feature keys for capability negotiation.
    /// </summary>
    public static class ServerFeatureKeys
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
        /// Maximum batch size supported by the server.
        /// </summary>
        public const string MaxBatchSize = "maxBatchSize";

        /// <summary>
        /// Array of supported server versions.
        /// </summary>
        public const string SupportedVersions = "supportedVersions";
    }
}