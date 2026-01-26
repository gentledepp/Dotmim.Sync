using Wormhole.Sync.Batch;
using System;
using System.Runtime.Serialization;

namespace Wormhole.Sync.Async
{
    /// <summary>
    /// Contains all parameters needed to execute a batch creation job.
    /// This class is serializable so it can be stored and passed to background workers.
    /// </summary>
    [DataContract(Name = "jobparams"), Serializable]
    public class BatchCreationJobParameters
    {
        /// <summary>
        /// Gets or sets the scope name for this sync operation.
        /// </summary>
        [DataMember(Name = "sn", IsRequired = true, Order = 1)]
        public string ScopeName { get; set; }

        /// <summary>
        /// Gets or sets the server scope info.
        /// </summary>
        [DataMember(Name = "ssi", IsRequired = true, Order = 2)]
        public ScopeInfo ServerScopeInfo { get; set; }

        /// <summary>
        /// Gets or sets the client scope info.
        /// </summary>
        [DataMember(Name = "csic", IsRequired = true, Order = 3)]
        public ScopeInfoClient ClientScopeInfoClient { get; set; }

        /// <summary>
        /// Gets or sets the sync context for this operation.
        /// </summary>
        [DataMember(Name = "ctx", IsRequired = true, Order = 4)]
        public SyncContext Context { get; set; }

        /// <summary>
        /// Gets or sets the client batch info containing changes to apply.
        /// </summary>
        [DataMember(Name = "cbi", IsRequired = false, EmitDefaultValue = false, Order = 5)]
        public BatchInfo ClientBatchInfo { get; set; }

        /// <summary>
        /// Gets or sets the batch directory path.
        /// </summary>
        [DataMember(Name = "bd", IsRequired = true, Order = 6)]
        public string BatchDirectory { get; set; }

        /// <summary>
        /// Gets or sets the batch size in KB.
        /// </summary>
        [DataMember(Name = "bs", IsRequired = true, Order = 7)]
        public int BatchSize { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the provider supports multiple active result sets.
        /// </summary>
        [DataMember(Name = "mars", IsRequired = false, EmitDefaultValue = false, Order = 8)]
        public bool SupportsMultipleActiveResultSets { get; set; }

        /// <summary>
        /// Gets or sets the provider type name for reconstruction.
        /// </summary>
        [DataMember(Name = "ptn", IsRequired = true, Order = 9)]
        public string ProviderTypeName { get; set; }

        /// <summary>
        /// Gets or sets the connection string for the provider.
        /// </summary>
        [DataMember(Name = "cs", IsRequired = true, Order = 10)]
        public string ConnectionString { get; set; }

        /// <summary>
        /// Gets or sets the batch storage type name for reconstruction.
        /// If null, uses default LocalFileSystemBatchStorage.
        /// </summary>
        [DataMember(Name = "bst", IsRequired = false, EmitDefaultValue = false, Order = 11)]
        public string BatchStorageTypeName { get; set; }

        /// <summary>
        /// Gets or sets whether to use unified batching.
        /// </summary>
        [DataMember(Name = "ub", IsRequired = false, EmitDefaultValue = false, Order = 12)]
        public bool UseUnifiedBatching { get; set; }
    }
}
