using System.Collections.Specialized;
using System.Net.Http;
using System.Web;

namespace Dotmim.Sync.Tests
{
    public class TestHttpRequest : HttpRequestBase
    {
        private readonly HttpRequestMessage inner;
        private NameValueCollection headers;

        public TestHttpRequest(HttpRequestMessage inner)
        {
            this.inner = inner;

            this.headers = new NameValueCollection();
            foreach (var h in inner.Headers)
            {
                foreach (var v in h.Value)
                    this.headers.Add(h.Key, v);
            }
        }

        public override NameValueCollection Headers => this.headers;
    }
}