using Wormhole.Sync.Enumerations;

namespace Wormhole.Sync
{
    /// <summary>
    /// Progress event args for when server batch creation is in progress during async batch creation.
    /// This event is raised when the server returns an InProgress response and the client is polling for completion.
    /// </summary>
    public class HttpBatchCreationInProgressArgs : ProgressArgs
    {
        /// <summary>
        /// Gets the progress percentage reported by the server (0-100).
        /// </summary>
        public int ProgressPercentage { get; }

        /// <summary>
        /// Gets the number of retry attempts made so far.
        /// </summary>
        public int RetryCount { get; }

        /// <summary>
        /// Gets the service host URL.
        /// </summary>
        public string Host { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="HttpBatchCreationInProgressArgs"/> class.
        /// </summary>
        /// <param name="context">The sync context.</param>
        /// <param name="progressPercentage">The progress percentage from the server.</param>
        /// <param name="retryCount">The number of retry attempts made.</param>
        /// <param name="host">The service host URL.</param>
        public HttpBatchCreationInProgressArgs(SyncContext context, int progressPercentage, int retryCount, string host)
            : base(context, null, null)
        {
            this.ProgressPercentage = progressPercentage;
            this.RetryCount = retryCount;
            this.Host = host;
        }

        /// <inheritdoc />
        public override SyncProgressLevel ProgressLevel => SyncProgressLevel.Information;

        /// <inheritdoc />
        public override string Source => this.Host;

        /// <inheritdoc />
        public override string Message => $"Server creating batches: {this.ProgressPercentage}% (retry {this.RetryCount})";

        /// <inheritdoc />
        public override int EventId => 25000;
    }
}
