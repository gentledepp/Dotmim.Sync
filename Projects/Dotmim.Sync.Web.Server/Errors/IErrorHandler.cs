using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
#if NET48
using System.Collections.Specialized;
using System.Net.Http;
using System.Web;
using HttpRequest = System.Net.Http.HttpRequestMessage;
using HttpResponse = System.Net.Http.HttpResponseMessage;
using HttpContext = System.Web.HttpContextBase;
#else
using Microsoft.AspNetCore.Http;
using HttpRequest = Microsoft.AspNetCore.Http.HttpRequest;
#endif

namespace Wormhole.Sync.Web.Server.Errors
{
    /// <summary>
    /// 
    /// </summary>
    public class ErrorHandlerArguments
    {
        /// <summary>
        /// 
        /// </summary>
        public Exception Exception { get; }

        /// <summary>
        /// 
        /// </summary>
        public HttpRequest Request { get; }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="exception"></param>
        /// <param name="request"></param>
        public ErrorHandlerArguments(Exception exception, HttpRequest request)
        {
            this.Exception = exception;
            this.Request = request;
        }
    }

    /// <summary>
    /// 
    /// </summary>
    public interface IErrorHandler
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="args"></param>
        /// <returns></returns>
        Task HandleAsync(ErrorHandlerArguments args);
    }
}
