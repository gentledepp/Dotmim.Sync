using System;
using System.Collections.Generic;
using System.Text;

namespace Dotmim.Sync.Tests.Core
{
    public interface ITestServer : IDisposable
    {
        string Run();
    }
}
