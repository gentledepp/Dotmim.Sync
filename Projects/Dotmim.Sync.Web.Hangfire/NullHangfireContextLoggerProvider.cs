using Hangfire.Server;
using Microsoft.Extensions.Logging;

namespace Wormhole.Sync.Web.Hangfire
{
    public class NullHangfireContextLoggerProvider<TType> : IHangfireContextLoggerProvider<TType>
    {
        public ILogger<TType1> Provide<TType1>(ILogger<TType1> logger, PerformContext context)
        {
            return logger;
        }
    }
}