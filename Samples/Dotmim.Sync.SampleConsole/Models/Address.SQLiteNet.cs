using System;
using SQLite;

namespace Dotmim.Sync.SampleConsole.Models.SQLiteNet
{
    
    
    [Table("Address")]
    public class AddressSQlite
    {
        [PrimaryKey, AutoIncrement]
        [Column("AddressID")]
        public int AddressId { get; set; }
        
        public string AddressLine1 { get; set; }
        public string AddressLine2 { get; set; }
        public string City { get; set; }
        public string StateProvince { get; set; }
        public string CountryRegion { get; set; }
        public string PostalCode { get; set; }
        
        [Column("rowguid")]
        public Guid? Rowguid { get; set; }
        public DateTime? ModifiedDate { get; set; }
    }

    [Table("Address_tracking")]
    public class AddressTrackingSQlite
    {
        [PrimaryKey]
        [Column("AddressID")]
        public int AddressId { get; set; }
        
        [Column("update_scope_id")]
        public Guid UpdateScopeId { get; set; }
        [Column("timestamp")]
        public int Timestamp { get; set; }
        [Column("sync_row_is_tombstone")]
        public bool SyncRowIsTombstone{ get; set; }
        [Column("last_change_datetime")]
        public DateTime LastChangeDateTime { get; set; }
    }
}
