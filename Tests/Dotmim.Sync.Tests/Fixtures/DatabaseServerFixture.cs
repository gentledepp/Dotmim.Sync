using Wormhole.Sync.SqlServer;
using Wormhole.Sync.Tests.Core;
using Wormhole.Sync.Tests.Models;
#if !NET48
using Microsoft.AspNetCore.Hosting.Server;
#endif
using Microsoft.Data.SqlClient;
#if !NET48
using Microsoft.EntityFrameworkCore;
#endif
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using System.Transactions;
#if !NET48
using static Microsoft.EntityFrameworkCore.DbLoggerCategory.Database;
#endif

namespace Wormhole.Sync.Tests.Fixtures
{

    public class DatabaseServerFixture : IDisposable
    {
        public Stopwatch OverallStopwatch { get; }

        public DatabaseServerFixture() => this.OverallStopwatch = Stopwatch.StartNew();

        public void Dispose() => this.OverallStopwatch.Stop();

    }
}
