using Hangfire.Server;
using Microsoft.Extensions.Logging;

namespace Wormhole.Sync.Web.Hangfire
{
    public interface IHangfireContextLoggerProvider<TType>
    {
        ILogger<TType> Provide<TType>(ILogger<TType> logger, PerformContext context);
    }
}