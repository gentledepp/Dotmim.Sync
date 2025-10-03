using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;

namespace Wormhole.Sync
{
    /// <summary>
    /// Client environment information for error reporting and diagnostics.
    /// </summary>
    [DataContract(Name = "clientEnvInfo"), Serializable]
    public class ClientEnvironmentInfo
    {
        /// <summary>
        /// Gets or sets the DotMim.Sync version.
        /// </summary>
        [DataMember(Name = "dsv", IsRequired = false, Order = 1)]
        public string DotMimSyncVersion { get; set; }

        /// <summary>
        /// Gets or sets the .NET Runtime version.
        /// </summary>
        [DataMember(Name = "rtv", IsRequired = false, Order = 2)]
        public string RuntimeVersion { get; set; }

        /// <summary>
        /// Gets or sets the operating system description.
        /// </summary>
        [DataMember(Name = "os", IsRequired = false, Order = 3)]
        public string OperatingSystem { get; set; }

        /// <summary>
        /// Gets or sets the machine architecture.
        /// </summary>
        [DataMember(Name = "arch", IsRequired = false, Order = 4)]
        public string Architecture { get; set; }

        /// <summary>
        /// Gets or sets the database provider being used.
        /// </summary>
        [DataMember(Name = "dbp", IsRequired = false, Order = 5)]
        public string DatabaseProvider { get; set; }

        /// <summary>
        /// Gets or sets the application name or process name.
        /// </summary>
        [DataMember(Name = "app", IsRequired = false, Order = 6)]
        public string ApplicationName { get; set; }

        /// <summary>
        /// Gets or sets additional environment properties.
        /// </summary>
        [DataMember(Name = "props", IsRequired = false, Order = 7)]
        public Dictionary<string, string> Properties { get; set; }

        /// <summary>
        /// Get current client environment information.
        /// </summary>
        public static ClientEnvironmentInfo GetCurrent()
        {
            try
            {
                var info = new ClientEnvironmentInfo
                {
                    RuntimeVersion = RuntimeInformation.FrameworkDescription,
                    OperatingSystem = RuntimeInformation.OSDescription,
                    Architecture = RuntimeInformation.OSArchitecture.ToString(),
                    ApplicationName = System.Diagnostics.Process.GetCurrentProcess().ProcessName,
                    Properties = new Dictionary<string, string>()
                };

                // Get DotMim.Sync version from assembly
                try
                {
                    var assembly = typeof(SyncContext).Assembly;
                    var version = assembly.GetName().Version;
                    info.DotMimSyncVersion = version?.ToString() ?? "Unknown";
                }
                catch
                {
                    info.DotMimSyncVersion = "Unknown";
                }

                // Add additional runtime info
                info.Properties["ProcessorCount"] = Environment.ProcessorCount.ToString();
                info.Properties["WorkingSet"] = Environment.WorkingSet.ToString();
                info.Properties["Is64BitProcess"] = Environment.Is64BitProcess.ToString();
                info.Properties["Is64BitOperatingSystem"] = Environment.Is64BitOperatingSystem.ToString();

                return info;
            }
            catch
            {
                // Return minimal info if anything fails
                return new ClientEnvironmentInfo
                {
                    RuntimeVersion = "Unknown",
                    OperatingSystem = "Unknown",
                    Architecture = "Unknown",
                    DotMimSyncVersion = "Unknown"
                };
            }
        }

        /// <summary>
        /// Set database provider information.
        /// </summary>
        public void SetDatabaseProvider(string provider)
        {
            DatabaseProvider = provider;
        }

        /// <summary>
        /// Add custom property.
        /// </summary>
        public void AddProperty(string key, string value)
        {
            if (Properties == null)
                Properties = new Dictionary<string, string>();

            Properties[key] = value;
        }
    }
}