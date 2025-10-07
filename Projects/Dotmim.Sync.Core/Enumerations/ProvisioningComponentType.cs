using System;

namespace Wormhole.Sync.Enumerations
{
    /// <summary>
    /// Represents the type of provisioning component to be generated.
    /// </summary>
    public enum ProvisioningComponentType
    {
        /// <summary>
        /// Represents a table-level check to determine if the entire table should be included.
        /// </summary>
        Table,

        /// <summary>
        /// Represents the tracking table for a sync table.
        /// </summary>
        TrackingTable,

        /// <summary>
        /// Represents a trigger (Insert, Update, or Delete).
        /// </summary>
        Trigger,

        /// <summary>
        /// Represents a stored procedure.
        /// </summary>
        StoredProcedure,

        /// <summary>
        /// Represents custom provisioning SQL.
        /// </summary>
        CustomSql,
    }
}
