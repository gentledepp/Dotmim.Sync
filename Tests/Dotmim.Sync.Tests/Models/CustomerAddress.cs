using System;
using System.Collections.Generic;

namespace Wormhole.Sync.Tests.Models
{
    public partial class CustomerAddress
    {
#if NET48
        public string CustomerId { get; set; }
#else
        public Guid CustomerId { get; set; }
#endif
        public int AddressId { get; set; }
        public string AddressType { get; set; }
        public Guid? Rowguid { get; set; }
        public DateTime? ModifiedDate { get; set; }

        public Address Address { get; set; }
        public Customer Customer { get; set; }
    }
}
