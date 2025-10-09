using System;

namespace Wormhole.Sync.Enumerations
{
    /// <summary>
    /// Sync direction : Can be Bidirectional (default), DownloadOnly, UploadOnly.
    /// </summary>
    [Flags]
    public enum SyncDirection
    {
        /// <summary>
        /// Table will be sync from server to client and from client to server.
        /// </summary>
#pragma warning disable CA1008 // Enums should have zero value
        Bidirectional = 0,
#pragma warning restore CA1008 // Enums should have zero value

        /// <summary>
        /// Table will be sync from server to client only.
        /// All changes occured client won't be uploaded to server.
        /// </summary>
        DownloadOnly = 2,

        /// <summary>
        /// Table will be sync from client to server only
        /// All changes from server won't be downloaded to client.
        /// </summary>
        UploadOnly = 4,

        /// <summary>
        /// Table structure is replicated, but not the datas
        /// Note : The value should be 0, but for compatibility issue with previous version, we go for a new value.
        /// </summary>
        None = 8,
    }

    /// <summary>
    /// Sync way.
    /// </summary>
    public enum SyncRole
    {
        /// <summary>
        /// No sync engaged.
        /// </summary>
        None = 0,

        /// <summary>
        /// Sync is sending local changes, then retrieving remote ones
        /// </summary>
        Client = 1,

        /// <summary>
        /// Sync is receiving remote changes, then sending local (server) ones
        /// </summary>
        Server = 2,
    }
}