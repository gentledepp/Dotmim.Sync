using Wormhole.Sync.Web.Server;
using Microsoft.Owin.Hosting;
using Owin;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.Remoting.Contexts;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Dispatcher;

namespace Wormhole.Sync.Tests
{
    public class TestWebServer : IDisposable
    {
        private readonly bool useFiddler;
        private IDisposable webApp;
        private Action<WebServerAgent> configureAgent;

        public TestWebServer(bool useFiddler = false)
        {
            this.useFiddler = useFiddler;
        }
        public void AddSyncServer(CoreProvider provider, SyncSetup setup = null, SyncOptions options = null,
            WebServerOptions webServerOptions = null, string scopeName = null, string identifier = null)
        {
            scopeName = string.IsNullOrEmpty(scopeName) ? SyncOptions.DefaultScopeName : scopeName;
            
            this.WebServerAgents.RemoveAll(wsa => wsa.ScopeName == scopeName);

            this.WebServerAgents.Add(new WebServerAgent(provider, setup, options, webServerOptions, scopeName, identifier));
        }

        public List<WebServerAgent> WebServerAgents { get; private set; } = new();

        public string Run(Action<WebServerAgent> configureWebServerAgent = null)
        {
            this.configureAgent = configureWebServerAgent;

            var randomPort = new Random().Next(8900, 10000);

            for (int i = 0; i < 1000; i++)
            {
                if(IsPortAvailable(randomPort))
                    break;
                
                randomPort = new Random().Next(8900, 10000);
            }

            string serviceUrl = $"http://localhost:{randomPort}/";

            this.webApp = WebApp.Start(serviceUrl, (appBuilder) =>
            {
                HttpConfiguration config = new HttpConfiguration();
                config.Routes.MapHttpRoute(
                    name: "SyncApi",
                    routeTemplate: "api/sync",
                    defaults: new { controller = "TestSync" }
                );
                config.Services.Replace(typeof(IHttpControllerActivator),
                    new TestControllerActivator(
                        () => this.WebServerAgents.Count == 0 
                            ? throw new NotSupportedException("You must set the WebApi2TestServer.WebServerAgent property first. Call AddSyncServer before the test!")
                            : this.WebServerAgents,
                        this.configureAgent));
                appBuilder.UseWebApi(config);
            });

            if (this.useFiddler)
                serviceUrl = $"http://localhost.fiddler:{randomPort}/";

            return new Uri(new Uri(serviceUrl), "/api/sync").ToString();
        }

        /// <summary>
        /// Checks if the specified port is available for binding.
        /// </summary>
        /// <param name="port">The port number to check.</param>
        /// <returns>True if the port is available; otherwise, false.</returns>
        public static bool IsPortAvailable(int port)
        {
            var ipGlobalProperties = IPGlobalProperties.GetIPGlobalProperties();
            var listeners = ipGlobalProperties.GetActiveTcpListeners();
        
            return !listeners.Any(l => l.Port == port);
        }

        public Task StopAsync()
        {
            this.webApp?.Dispose();
            this.WebServerAgents.Clear();
            this.configureAgent = null;
            return Task.CompletedTask;
        }

        public void Dispose() => this.webApp?.Dispose();
    }
}
