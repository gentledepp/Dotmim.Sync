using Wormhole.Sync.Enumerations;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Wormhole.Sync
{
    /// <summary>
    /// Comprehensive exception information for error reporting.
    /// </summary>
    [DataContract(Name = "serializableExceptionInfo"), Serializable]
    public class SerializableExceptionInfo
    {
        /// <summary>
        /// Gets or sets the exception type name.
        /// </summary>
        [DataMember(Name = "et", IsRequired = true, Order = 1)]
        public string ExceptionType { get; set; }

        /// <summary>
        /// Gets or sets the exception message.
        /// </summary>
        [DataMember(Name = "msg", IsRequired = true, Order = 2)]
        public string Message { get; set; }

        /// <summary>
        /// Gets or sets the stack trace.
        /// </summary>
        [DataMember(Name = "st", IsRequired = false, Order = 3)]
        public string StackTrace { get; set; }

        /// <summary>
        /// Gets or sets the source.
        /// </summary>
        [DataMember(Name = "src", IsRequired = false, Order = 4)]
        public string Source { get; set; }

        /// <summary>
        /// Gets or sets the help link.
        /// </summary>
        [DataMember(Name = "hl", IsRequired = false, Order = 5)]
        public string HelpLink { get; set; }

        /// <summary>
        /// Gets or sets the HResult.
        /// </summary>
        [DataMember(Name = "hr", IsRequired = false, Order = 6)]
        public int HResult { get; set; }

        /// <summary>
        /// Gets or sets the exception data dictionary.
        /// </summary>
        [DataMember(Name = "data", IsRequired = false, Order = 7)]
        public Dictionary<string, object> Data { get; set; }

        /// <summary>
        /// Gets or sets the inner exception.
        /// </summary>
        [DataMember(Name = "inner", IsRequired = false, Order = 8)]
        public SerializableExceptionInfo InnerException { get; set; }

        /// <summary>
        /// Gets or sets the sync stage (for SyncException).
        /// </summary>
        [DataMember(Name = "stage", IsRequired = false, Order = 9)]
        public SyncStage? SyncStage { get; set; }

        /// <summary>
        /// Gets or sets the type name (for SyncException).
        /// </summary>
        [DataMember(Name = "tn", IsRequired = false, Order = 10)]
        public string TypeName { get; set; }

        /// <summary>
        /// Gets or sets the schema name (for SyncException).
        /// </summary>
        [DataMember(Name = "sn", IsRequired = false, Order = 11)]
        public string SchemaName { get; set; }

        /// <summary>
        /// Gets or sets the table name (for SyncException).
        /// </summary>
        [DataMember(Name = "tab", IsRequired = false, Order = 12)]
        public string TableName { get; set; }

        /// <summary>
        /// Create SerializableExceptionInfo from any Exception.
        /// </summary>
        public static SerializableExceptionInfo FromException(Exception exception)
        {
            if (exception == null)
                return null;

            var info = new SerializableExceptionInfo
            {
                ExceptionType = exception.GetType().FullName,
                Message = exception.Message,
                StackTrace = exception.StackTrace,
                Source = exception.Source,
                HelpLink = exception.HelpLink,
                HResult = exception.HResult,
                Data = SerializeExceptionData(exception.Data),
                InnerException = FromException(exception.InnerException)
            };

            // Add SyncException specific properties
            if (exception is SyncException syncEx)
            {
                info.SyncStage = syncEx.SyncStage;
                info.TypeName = syncEx.TypeName;
            }

            return info;
        }

        /// <summary>
        /// Serialize Exception.Data dictionary safely.
        /// </summary>
        private static Dictionary<string, object> SerializeExceptionData(IDictionary data)
        {
            if (data == null || data.Count == 0)
                return null;

            var result = new Dictionary<string, object>();

            foreach (DictionaryEntry entry in data)
            {
                try
                {
                    var key = entry.Key?.ToString();
                    if (string.IsNullOrEmpty(key))
                        continue;

                    // Only serialize safe, serializable values
                    var value = entry.Value;
                    if (IsSerializable(value))
                    {
                        result[key] = value;
                    }
                    else
                    {
                        // Convert non-serializable objects to string representation
                        result[key] = value?.ToString() ?? "<null>";
                    }
                }
                catch
                {
                    // Skip problematic entries
                }
            }

            return result.Count > 0 ? result : null;
        }

        /// <summary>
        /// Check if value is safely serializable.
        /// </summary>
        private static bool IsSerializable(object value)
        {
            if (value == null)
                return true;

            var type = value.GetType();

            // Safe primitive types
            if (type.IsPrimitive || type == typeof(string) || type == typeof(DateTime) ||
                type == typeof(DateTimeOffset) || type == typeof(TimeSpan) ||
                type == typeof(Guid) || type == typeof(decimal))
                return true;

            // Check for Serializable attribute
            return type.IsSerializable;
        }

        /// <summary>
        /// Convert back to Exception (for server-side reconstruction).
        /// </summary>
        public SyncException ToException()
        {
            Exception innerEx = InnerException?.ToException();

            // Try to create the original exception type if possible
            if (ExceptionType == typeof(SyncException).FullName)
            {
                var syncEx = new SyncException(Message, innerEx);
                if (SyncStage.HasValue)
                    syncEx.SyncStage = SyncStage.Value;
                if (!string.IsNullOrEmpty(TypeName))
                    syncEx.TypeName = TypeName;
                
                // Restore Data dictionary
                if (Data != null)
                {
                    foreach (var kvp in Data)
                    {
                        syncEx.Data[kvp.Key] = kvp.Value;
                    }
                }

                return syncEx;
            }

            // For other exception types, create generic Exception with detailed message
            var detailedMessage = $"[{ExceptionType}] {Message}";
            if (!string.IsNullOrEmpty(StackTrace))
                detailedMessage += $"\n\nOriginal StackTrace:\n{StackTrace}";

            var genericEx = new Exception(detailedMessage, innerEx);

            // Restore Data dictionary
            if (Data != null)
            {
                foreach (var kvp in Data)
                {
                    genericEx.Data[kvp.Key] = kvp.Value;
                }
            }

            if (genericEx is SyncException s)
                return s;

            return new SyncException("unknown exception occurred", genericEx);
        }
    }
}