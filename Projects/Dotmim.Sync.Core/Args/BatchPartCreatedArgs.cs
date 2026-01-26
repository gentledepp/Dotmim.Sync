using Wormhole.Sync.Batch;
using Wormhole.Sync.Enumerations;
using System;
using System.Threading.Tasks;

namespace Wormhole.Sync
{
    /// <summary>
    /// Event args raised when a batch part has been created during async batch creation.
    /// Used for progressive batch streaming to notify when batches are available for download.
    /// </summary>
    public class BatchPartCreatedArgs : ProgressArgs
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="BatchPartCreatedArgs"/> class.
        /// </summary>
        /// <param name="context">The sync context.</param>
        /// <param name="batchPartInfo">The batch part info that was created.</param>
        /// <param name="tablesProcessed">Number of tables processed so far.</param>
        /// <param name="totalTables">Total number of tables to process.</param>
        public BatchPartCreatedArgs(SyncContext context, BatchPartInfo batchPartInfo, int tablesProcessed, int totalTables)
            : base(context, null, null)
        {
            this.BatchPartInfo = batchPartInfo ?? throw new ArgumentNullException(nameof(batchPartInfo));
            this.TablesProcessed = tablesProcessed;
            this.TotalTables = totalTables;
        }

        /// <summary>
        /// Gets the batch part info that was created.
        /// </summary>
        public BatchPartInfo BatchPartInfo { get; }

        /// <summary>
        /// Gets the number of tables processed so far.
        /// </summary>
        public int TablesProcessed { get; }

        /// <summary>
        /// Gets the total number of tables to process.
        /// </summary>
        public int TotalTables { get; }

        /// <inheritdoc />
        public override SyncProgressLevel ProgressLevel => SyncProgressLevel.Debug;

        /// <inheritdoc />
        public override string Source => "BatchCreation";

        /// <inheritdoc />
        public override string Message => $"Batch part {this.BatchPartInfo.Index} created ({this.BatchPartInfo.FileName})";

        /// <inheritdoc />
        public override int EventId => 25010;
    }

    /// <summary>
    /// Interceptors extensions.
    /// </summary>
    public partial class InterceptorsExtensions
    {
        /// <summary>
        /// Occurs when a batch part has been created during async batch creation.
        /// Used for progressive batch streaming.
        /// <example>
        /// <code>
        /// orchestrator.OnBatchPartCreated(args =>
        /// {
        ///     Console.WriteLine($"Batch {args.BatchPartInfo.Index} created: {args.BatchPartInfo.FileName}");
        /// });
        /// </code>
        /// </example>
        /// </summary>
        public static Guid OnBatchPartCreated(this BaseOrchestrator orchestrator, Action<BatchPartCreatedArgs> action)
            => orchestrator.AddInterceptor(action);

        /// <inheritdoc cref="OnBatchPartCreated(BaseOrchestrator, Action{BatchPartCreatedArgs})"/>
        public static Guid OnBatchPartCreated(this BaseOrchestrator orchestrator, Func<BatchPartCreatedArgs, Task> action)
            => orchestrator.AddInterceptor(action);
    }
}
