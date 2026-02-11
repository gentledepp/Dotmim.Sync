using System.Threading.Tasks;

namespace Wormhole.Sync.Web.Server.Errors
{
    internal class NullErrorHandler : IErrorHandler
    {
        public static readonly IErrorHandler Instance = new NullErrorHandler();
        
        private NullErrorHandler(){}

        public Task HandleAsync(ErrorHandlerArguments args)
        {
            return Task.CompletedTask;
        }
    }
}