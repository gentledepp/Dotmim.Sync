using System;
using System.Collections.Generic;

namespace Wormhole.Sync.Tests.Models
{
    public partial class Product
    {
        public Product()
        {
            SalesOrderDetail = new HashSet<SalesOrderDetail>();
        }

#if NET48
        // Since EF 6 uses System.Data.SQLite (not Microsoft.Data.Sqlite), Guids are stored as blobs
        // But dotmim.sync uses Microsoft.Data.Sqlite and therefore, when it searches for Guids, it passes them as STRING
        // So EF 6 can _never_ find them, because it compares a BLOB to a STRING
        // So for the sake of tests, we just fake them as strings
        public string ProductId { get; set; }
#else
        public Guid ProductId { get; set; }
#endif
        public string Name { get; set; }
        public string ProductNumber { get; set; }
        public string Color { get; set; }
        public decimal? StandardCost { get; set; }
        public decimal? ListPrice { get; set; }
        public string Size { get; set; }
        public decimal? Weight { get; set; }
        public string ProductCategoryId { get; set; }
        public int? ProductModelId { get; set; }
        public DateTime? SellStartDate { get; set; }
        public DateTime? SellEndDate { get; set; }
        public DateTime? DiscontinuedDate { get; set; }
        public byte[] ThumbNailPhoto { get; set; }
        public string ThumbnailPhotoFileName { get; set; }
        public Guid? Rowguid { get; set; }
        public DateTime? ModifiedDate { get; set; }

        public ProductCategory ProductCategory { get; set; }
        public ProductModel ProductModel { get; set; }
        public ICollection<SalesOrderDetail> SalesOrderDetail { get; set; }
    }
}
