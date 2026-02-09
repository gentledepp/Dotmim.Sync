using Wormhole.Sync.Builders;
using Wormhole.Sync.Enumerations;
using Wormhole.Sync.SqlServer;
using Wormhole.Sync.Tests.Core;
using Wormhole.Sync.Tests.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;


namespace Wormhole.Sync.Tests.UnitTests
{
    public partial class InterceptorsTests
    {
        [Fact]
        public async Task LocalTimestamp()
        {
            var localOrchestrator = new LocalOrchestrator(serverProvider, options);

            var onLTLoading = 0;
            var onLTLoaded = 0;

            localOrchestrator.OnLocalTimestampLoading(tca => onLTLoading++);
            localOrchestrator.OnLocalTimestampLoaded(tca => onLTLoaded++);

            var ts = await localOrchestrator.GetLocalTimestampAsync();

            Assert.Equal(1, onLTLoading);
            Assert.Equal(1, onLTLoaded);
        }
    }
}
