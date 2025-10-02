using System.Net.Http;
using System.Web;

namespace Dotmim.Sync.Tests
{
    public class TestHttpContext : HttpContextBase
    {
        private readonly TestSessionState session;
        private readonly HttpRequestBase request;

        public TestHttpContext(HttpRequestMessage request, TestSessionState session)
        {
            this.session = session;
            this.request = new TestHttpRequest(request);
        }

        public override HttpRequestBase Request => this.request;

        public override HttpSessionStateBase Session => this.session;
    }
}