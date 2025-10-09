using System;
using System.Collections.Generic;

namespace Wormhole.Sync.Tests.Models
{
    public class PriceList
    {
        public PriceList()
        {
            Categories = new List<PriceListCategory>();
        }
#if NET48
        // Since EF 6 uses System.Data.SQLite (not Microsoft.Data.Sqlite), Guids are stored as blobs
        // But dotmim.sync uses Microsoft.Data.Sqlite and therefore, when it searches for Guids, it passes them as STRING
        // So EF 6 can _never_ find them, because it compares a BLOB to a STRING
        // So for the sake of tests, we just fake them as strings
        public string PriceListId { get; set; }
#else
        public Guid PriceListId { get; set; }
#endif
        public string Description { get; set; }
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
        public IList<PriceListCategory> Categories { get; set; }
    }
}
