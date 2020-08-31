using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;
using System.Web.Http;
using System.Web.Http.Controllers;
using System.Web.Http.Dispatcher;
using Dotmim.Sync.Tests.Core;
using Dotmim.Sync.Web.Server;
using Microsoft.Owin.Hosting;
using Owin;

namespace Dotmim.Sync.WebApi2.Tests.Core
{
    public class TestControllerActivator : IHttpControllerActivator
    {
        private readonly Func<WebServerOrchestrator> _factory;

        public TestControllerActivator(Func<WebServerOrchestrator> factory)
        {
            this._factory = factory;
        }

        public IHttpController Create(HttpRequestMessage request, HttpControllerDescriptor controllerDescriptor, Type controllerType)
        {
            return new TestSyncController(this._factory());
        }
    }

    [RoutePrefix("api/testsync")]
    public class TestSyncController : ApiController
    {
        // proxy to handle requests and send them to SqlSyncProvider
        private readonly WebServerOrchestrator orchestrator;

        // Injected thanks to Dependency Injection
        public TestSyncController(WebServerOrchestrator orchestrator) => this.orchestrator = orchestrator;

        // POST api/values
        [HttpPost]
        [Route("")]
        public Task<HttpResponseMessage> Post() => this.orchestrator.HandleRequestAsync(this.Request, new NullHttpContext(), default, null);

        public class NullHttpContext : HttpContextBase
        {
        }

    }

    internal class WebApi2TestServer : ITestServer
    {
        private readonly WebServerOrchestrator webServerOrchestrator;
        private readonly bool useFiddler;
        private IDisposable webApp;

        public WebApi2TestServer(WebServerOrchestrator webServerOrchestrator, bool useFiddler = false)
        {
            this.webServerOrchestrator = webServerOrchestrator;
            this.useFiddler = useFiddler;
        }

        public string Run()
        {
            var randomPort = new Random().Next(8900, 10000);
            
            string serviceUrl = $"http://localhost:{randomPort}/";

            this.webApp = WebApp.Start(serviceUrl, (appBuilder) =>
            {
                // Configure Web API for self-host. 
                HttpConfiguration config = new HttpConfiguration();
                config.Routes.MapHttpRoute(
                    name: "DefaultApi",
                    routeTemplate: "api/{controller}/{actionid}/{id}",
                    defaults: new { actionid = RouteParameter.Optional, id = RouteParameter.Optional }
                );
                config.Services.Replace(typeof(IHttpControllerActivator), 
                    new TestControllerActivator(() => this.webServerOrchestrator));
                appBuilder.UseWebApi(config);
            });

            if(this.useFiddler)
                serviceUrl = $"http://localhost.fiddler:{randomPort}/";

            return new Uri(new Uri(serviceUrl), "/api/testsync").ToString();
        }

        public void Dispose() => this.webApp.Dispose();
    }
}
