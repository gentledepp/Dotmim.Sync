using Wormhole.Sync.Web.Server;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Web.Http.Controllers;
using System.Web.Http.Dispatcher;

namespace Wormhole.Sync.Tests
{
    public class TestControllerActivator : IHttpControllerActivator
    {
        private readonly Func<IEnumerable<WebServerAgent>> _factory;
        private readonly Action<WebServerAgent> _configureAgent;
        private TestSessionState session = new TestSessionState();

        public TestControllerActivator(Func<IEnumerable<WebServerAgent>> factory, Action<WebServerAgent> configureAgent = null)
        {
            this._factory = factory;
            this._configureAgent = configureAgent;
        }

        public IHttpController Create(HttpRequestMessage request, HttpControllerDescriptor controllerDescriptor, Type controllerType)
        {
            var agents = this._factory().ToList();
            foreach(var agent in agents)
                this._configureAgent?.Invoke(agent);
            return new TestSyncController(agents, this.session);
        }
    }
}