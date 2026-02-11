
namespace Wormhole.Sync.Web.Server
{
    public interface IRequiresHttpContext
    {
#if NET48
        void SetContext(System.Web.HttpContextBase httpContext);
#else
        void SetContext(Microsoft.AspNetCore.Http.HttpContext httpContext);
#endif
    }
}