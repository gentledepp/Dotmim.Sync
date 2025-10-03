using Wormhole.Sync.Web.Server;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web.Http;

namespace Wormhole.Sync.Tests
{
    [RoutePrefix("api/sync")]
    public class TestSyncController : ApiController
    {
        private readonly IEnumerable<WebServerAgent> allWebServerAgents;
        private readonly TestSessionState session;

        public TestSyncController(IEnumerable<WebServerAgent> webServerAgents, TestSessionState session)
        {
            this.allWebServerAgents = webServerAgents;
            this.session = session;
        }

        [HttpPost]
        [Route("")]
        public async Task<HttpResponseMessage> Post()
        {
            var context = new TestHttpContext(this.Request, this.session);


            var identifier = context.GetIdentifier();
            var scopeName = context.GetScopeName();

            IEnumerable<WebServerAgent> webServerAgents = null;

            if (string.IsNullOrEmpty(identifier))
                webServerAgents = this.allWebServerAgents.Where(wsa => string.IsNullOrEmpty(wsa.Identifier));
            else
                webServerAgents = this.allWebServerAgents.Where(wsa => wsa.Identifier == identifier);

            var webServerAgent = webServerAgents.FirstOrDefault(wsa => wsa.ScopeName == scopeName);

            if (webServerAgent == null)
            {
                var response = this.Request.CreateResponse(HttpStatusCode.BadRequest);
                response.Content = new StringContent(
                    $"There is no web server agent configured for this scope name {scopeName} and identifier {identifier}.",
                    Encoding.UTF8);
                return response;

            }
            else
            {
                return await webServerAgent.HandleRequestAsync(this.Request, context);
            }
        }
    }
}