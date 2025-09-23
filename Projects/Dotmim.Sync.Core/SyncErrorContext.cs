using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Dotmim.Sync
{
    /// <summary>
    /// Context information for sync errors, providing additional diagnostic data.
    /// </summary>
    [DataContract(Name = "syncErrorCtx"), Serializable]
    public class SyncErrorContext
    {
        /// <summary>
        /// Gets or sets the sync phase when the error occurred.
        /// </summary>
        [DataMember(Name = "phase", IsRequired = false, Order = 1)]
        public string Phase { get; set; }

        /// <summary>
        /// Gets or sets the client environment information.
        /// </summary>
        [DataMember(Name = "clientInfo", IsRequired = false, Order = 2)]
        public ClientEnvironmentInfo ClientInfo { get; set; }

        /// <summary>
        /// Gets or sets additional contextual information about the error.
        /// </summary>
        [DataMember(Name = "additionalInfo", IsRequired = false, Order = 3)]
        public Dictionary<string, string> AdditionalInfo { get; set; }

        /// <summary>
        /// Gets or sets the table name associated with the error (if applicable).
        /// </summary>
        [DataMember(Name = "tableName", IsRequired = false, Order = 4)]
        public string TableName { get; set; }

        /// <summary>
        /// Gets or sets the operation being performed when the error occurred.
        /// </summary>
        [DataMember(Name = "operation", IsRequired = false, Order = 5)]
        public string Operation { get; set; }

        /// <summary>
        /// Gets or sets the number of retry attempts made before the error.
        /// </summary>
        [DataMember(Name = "retryCount", IsRequired = false, Order = 6)]
        public int? RetryCount { get; set; }

        /// <summary>
        /// Gets or sets the duration of the operation before the error (in milliseconds).
        /// </summary>
        [DataMember(Name = "duration", IsRequired = false, Order = 7)]
        public long? DurationMs { get; set; }

        /// <summary>
        /// Gets or sets custom user data associated with the error.
        /// </summary>
        [DataMember(Name = "userData", IsRequired = false, Order = 8)]
        public Dictionary<string, object> UserData { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="SyncErrorContext"/> class.
        /// </summary>
        public SyncErrorContext()
        {
            AdditionalInfo = new Dictionary<string, string>();
            UserData = new Dictionary<string, object>();
        }

        /// <summary>
        /// Add additional information to the error context.
        /// </summary>
        public void AddInfo(string key, string value)
        {
            if (AdditionalInfo == null)
                AdditionalInfo = new Dictionary<string, string>();

            AdditionalInfo[key] = value;
        }

        /// <summary>
        /// Add user data to the error context.
        /// </summary>
        public void AddUserData(string key, object value)
        {
            if (UserData == null)
                UserData = new Dictionary<string, object>();

            UserData[key] = value;
        }

        /// <summary>
        /// Create error context from sync operation details.
        /// </summary>
        public static SyncErrorContext FromOperation(string operation, string tableName = null, long? durationMs = null)
        {
            return new SyncErrorContext
            {
                Operation = operation,
                TableName = tableName,
                DurationMs = durationMs,
                ClientInfo = ClientEnvironmentInfo.GetCurrent()
            };
        }

        /// <summary>
        /// Create error context with phase and client info.
        /// </summary>
        public static SyncErrorContext FromPhase(string phase, ClientEnvironmentInfo clientInfo = null)
        {
            return new SyncErrorContext
            {
                Phase = phase,
                ClientInfo = clientInfo ?? ClientEnvironmentInfo.GetCurrent()
            };
        }
    }
}