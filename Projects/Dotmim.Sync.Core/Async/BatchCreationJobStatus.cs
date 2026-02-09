using Wormhole.Sync.Batch;
using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Wormhole.Sync.Async
{
    /// <summary>
    /// Represents the current state of a batch creation job.
    /// </summary>
    public enum BatchCreationJobState
    {
        /// <summary>
        /// Job has been queued but not yet started.
        /// </summary>
        Queued = 0,

        /// <summary>
        /// Job is currently being processed.
        /// </summary>
        Processing = 1,

        /// <summary>
        /// First batch is ready - can be used for streaming early results.
        /// </summary>
        FirstBatchReady = 2,

        /// <summary>
        /// Job completed successfully.
        /// </summary>
        Completed = 3,

        /// <summary>
        /// Job failed with an error.
        /// </summary>
        Failed = 4,

        /// <summary>
        /// Job was cancelled.
        /// </summary>
        Cancelled = 5,
    }

    /// <summary>
    /// Represents the status and result of a batch creation job.
    /// </summary>
    [DataContract(Name = "jobstatus"), Serializable]
    public class BatchCreationJobStatus
    {
        /// <summary>
        /// Gets or sets the unique job identifier.
        /// </summary>
        [DataMember(Name = "jid", IsRequired = true, Order = 1)]
        public string JobId { get; set; }

        /// <summary>
        /// Gets or sets the current job state.
        /// </summary>
        [DataMember(Name = "st", IsRequired = true, Order = 2)]
        public BatchCreationJobState State { get; set; }

        /// <summary>
        /// Gets or sets when the job was enqueued.
        /// </summary>
        [DataMember(Name = "eq", IsRequired = false, EmitDefaultValue = false, Order = 3)]
        public DateTime EnqueuedAt { get; set; }

        /// <summary>
        /// Gets or sets when the job started processing.
        /// </summary>
        [DataMember(Name = "sa", IsRequired = false, EmitDefaultValue = false, Order = 4)]
        public DateTime? StartedAt { get; set; }

        /// <summary>
        /// Gets or sets when the job completed (successfully or with error).
        /// </summary>
        [DataMember(Name = "ca", IsRequired = false, EmitDefaultValue = false, Order = 5)]
        public DateTime? CompletedAt { get; set; }

        /// <summary>
        /// Gets or sets the progress percentage (0-100).
        /// </summary>
        [DataMember(Name = "prg", IsRequired = false, EmitDefaultValue = false, Order = 6)]
        public int ProgressPercentage { get; set; }

        /// <summary>
        /// Gets or sets the number of tables processed so far.
        /// </summary>
        [DataMember(Name = "tp", IsRequired = false, EmitDefaultValue = false, Order = 7)]
        public int TablesProcessed { get; set; }

        /// <summary>
        /// Gets or sets the total number of tables to process.
        /// </summary>
        [DataMember(Name = "tt", IsRequired = false, EmitDefaultValue = false, Order = 8)]
        public int TotalTables { get; set; }

        /// <summary>
        /// Gets or sets the remote client timestamp after sync completion.
        /// </summary>
        [DataMember(Name = "rct", IsRequired = false, EmitDefaultValue = false, Order = 9)]
        public long? RemoteClientTimestamp { get; set; }

        /// <summary>
        /// Gets or sets the batch info containing the created batches.
        /// </summary>
        [DataMember(Name = "bi", IsRequired = false, EmitDefaultValue = false, Order = 10)]
        public BatchInfo BatchInfo { get; set; }

        /// <summary>
        /// Gets or sets the server changes selected summary.
        /// </summary>
        [DataMember(Name = "cs", IsRequired = false, EmitDefaultValue = false, Order = 11)]
        public DatabaseChangesSelected ChangesSelected { get; set; }

        /// <summary>
        /// Gets or sets the client changes applied summary.
        /// </summary>
        [DataMember(Name = "cap", IsRequired = false, EmitDefaultValue = false, Order = 12)]
        public DatabaseChangesApplied ChangesApplied { get; set; }

        /// <summary>
        /// Gets or sets the error message if the job failed.
        /// </summary>
        [DataMember(Name = "err", IsRequired = false, EmitDefaultValue = false, Order = 13)]
        public string ErrorMessage { get; set; }

        /// <summary>
        /// Gets or sets the error stack trace if the job failed.
        /// </summary>
        [DataMember(Name = "stk", IsRequired = false, EmitDefaultValue = false, Order = 14)]
        public string ErrorStackTrace { get; set; }

        /// <summary>
        /// Gets or sets the number of batch parts created so far.
        /// Used for progressive batch streaming.
        /// </summary>
        [DataMember(Name = "bpc", IsRequired = false, EmitDefaultValue = false, Order = 15)]
        public int TotalBatchPartsCreated { get; set; }

        /// <summary>
        /// Gets or sets the list of batch parts that are ready for download.
        /// Updated incrementally as batches are created.
        /// Used for progressive batch streaming.
        /// </summary>
        [DataMember(Name = "abp", IsRequired = false, EmitDefaultValue = false, Order = 16)]
        public List<BatchPartInfo> AvailableBatchParts { get; set; } = new List<BatchPartInfo>();

        /// <summary>
        /// Gets or sets the index of the last batch part acknowledged by the client.
        /// Used to send only new batches on subsequent polls.
        /// </summary>
        [DataMember(Name = "lac", IsRequired = false, EmitDefaultValue = false, Order = 17)]
        public int LastAcknowledgedIndex { get; set; } = -1;
    }
}
