using Wormhole.Sync.Builders;
using Wormhole.Sync.Enumerations;

namespace Wormhole.Sync
{
    /// <summary>
    /// Arguments for provisioning component filtering.
    /// Used to determine whether a specific provisioning component should be included in the generated SQL scripts.
    /// </summary>
    public class ProvisioningComponentArgs
    {
        /// <summary>
        /// Gets or sets the sync table being processed.
        /// </summary>
        public SyncTable Table { get; set; }

        /// <summary>
        /// Gets or sets the type of component being processed.
        /// </summary>
        public ProvisioningComponentType ComponentType { get; set; }

        /// <summary>
        /// Gets or sets the trigger type when ComponentType is Trigger.
        /// Null for other component types.
        /// </summary>
        public DbTriggerType? TriggerType { get; set; }

        /// <summary>
        /// Gets or sets the stored procedure type when ComponentType is StoredProcedure.
        /// Null for other component types.
        /// </summary>
        public DbStoredProcedureType? StoredProcedureType { get; set; }
    }
}
