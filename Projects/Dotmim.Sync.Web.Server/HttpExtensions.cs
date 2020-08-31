using System;
using System.IO;
using System.Threading.Tasks;
#if NETSTANDARD
using System.Threading;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
#else
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Collections.Specialized;
using System.Net;
using System.Net.Http.Formatting;
using System.Text;
using System.Threading;

#endif

namespace Dotmim.Sync.Web.Server
{
   
    internal static class Extensions
    {
#if NETSTANDARD
        public static bool TryGetHeaderValue(this IHeaderDictionary n, string key, out string header)
        {
            if (n == null) throw new ArgumentNullException(nameof(n));
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (n.TryGetValue(key, out var vs))
            {
                header = vs[0];
                return true;
            }

            header = null;
            return false;
        }


        public static async Task ReadBodyAsync(this HttpRequest r, MemoryStream readableStream)
        {
            if (r == null) throw new ArgumentNullException(nameof(r));
            if (readableStream == null) throw new ArgumentNullException(nameof(readableStream));
            await r.Body.CopyToAsync(readableStream).ConfigureAwait(false);
            
            r.Body.Close();
            r.Body.Dispose();
        }


        public static HttpResponse CreateHttpResponse(this HttpRequest r)
        {
            if (r == null) throw new ArgumentNullException(nameof(r));
            return r.HttpContext.Response;
        }
        public static Task WriteBodyAsync(this HttpResponse r, byte[] body)
        {
            if (r == null) throw new ArgumentNullException(nameof(r));
            if (body == null) throw new ArgumentNullException(nameof(body));
            return r.Body.WriteAsync(body, 0, body.Length);
        }
        public static Task WriteAsync(this HttpResponse r, string body, CancellationToken token)
        {
            if (r == null) throw new ArgumentNullException(nameof(r));
            if (body == null) throw new ArgumentNullException(nameof(body));
            return r.WriteAsync(body, token);
        }
#else
        public static bool TryGetHeaderValue(this NameValueCollection n, string key, out string header)
        {
            if (n == null) throw new ArgumentNullException(nameof(n));

            if (n.AllKeys.Contains(key))
            {
                var vs = n[key];
                header = vs;
                return true;
            }

            header = null;
            return false;
        }
        
        public static bool TryGetHeaderValue(this HttpRequestHeaders n, string key, out string header)
        {
            if (n == null) throw new ArgumentNullException(nameof(n));
            if (n.TryGetValues(key, out var vs))
            {
                header = vs.First();
                return true;
            }

            header = null;
            return false;
        }

        public static async Task ReadBodyAsync(this HttpRequestMessage r, MemoryStream readableStream)
        {
            if (r == null) throw new ArgumentNullException(nameof(r));
            if (readableStream == null) throw new ArgumentNullException(nameof(readableStream));
            var stream = await r.Content.ReadAsStreamAsync().ConfigureAwait(false);
            await stream.CopyToAsync(readableStream).ConfigureAwait(false);
        }
        
        public static HttpResponseMessage CreateHttpResponse(this HttpRequestMessage r)
        {
            if (r == null) throw new ArgumentNullException(nameof(r));
            return r.CreateResponse(HttpStatusCode.OK);
        }

        public static Task WriteBodyAsync(this HttpResponseMessage r, byte[] body)
        {
            if (r == null) throw new ArgumentNullException(nameof(r));
            if (body == null) throw new ArgumentNullException(nameof(body));
            r.Content = new ByteArrayContent(body, 0, body.Length);
            return Task.CompletedTask;
        }
        public static Task WriteBodyAsync(this HttpResponseMessage r, WebSyncException x)
        {
            if (r == null) throw new ArgumentNullException(nameof(r));
            if (x == null) throw new ArgumentNullException(nameof(x));
            r.Content = new ObjectContent<WebSyncException>(x, new JsonMediaTypeFormatter());
            return Task.CompletedTask;
        }
        public static Task WriteAsync(this HttpResponseMessage r, string body, CancellationToken token)
        {
            if (r == null) throw new ArgumentNullException(nameof(r));
            if (body == null) throw new ArgumentNullException(nameof(body));
            r.Content = new StringContent(body, Encoding.UTF8);
            return Task.CompletedTask;
        }
#endif
    }
}
