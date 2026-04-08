using Wormhole.Sync.Async;
using Wormhole.Sync.Batch;
using Wormhole.Sync.Enumerations;
using Wormhole.Sync.Extensions;
using Wormhole.Sync.Serialization;
using Wormhole.Sync.Web.Client;
#if NET48
using System.Collections.Specialized;
using System.Net.Http;
using System.Web;
using HttpRequest = System.Net.Http.HttpRequestMessage;
using HttpResponse = System.Net.Http.HttpResponseMessage;
using HttpContext = System.Web.HttpContextBase;
#else
using Microsoft.AspNetCore.Http;
#endif
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Wormhole.Sync.Storage;
using Wormhole.Sync.Web.Server.Errors;

namespace Wormhole.Sync.Web.Server
{
    /// <summary>
    /// Web server agent.
    /// </summary>
    public partial class WebServerAgent
    {
        private readonly IErrorHandler errorHandler;
        private static readonly ISerializer JsonSerializer = SerializersFactory.JsonSerializerFactory.GetSerializer();

        private static bool checkUpgradeDone;

        /// <inheritdoc cref="WebServerAgent"/>
        public WebServerAgent(CoreProvider provider, SyncSetup setup, SyncOptions options = null, WebServerOptions webServerOptions = null,
            string scopeName = null,
            string identifier = null,
            IBatchCleanupService cleanupService = null,
            IBatchCreationJobService batchCreationJobService = null,
            IBatchStorage batchStore = null,
            ISessionCacheStore sessionCacheStore = null,
            IErrorHandler errorHandler = null)
        {
            this.errorHandler = errorHandler ?? NullErrorHandler.Instance;
            this.Setup = setup;
            this.WebServerOptions = webServerOptions ?? new WebServerOptions();
            this.Provider = provider;
            this.ScopeName = string.IsNullOrEmpty(scopeName) ? SyncOptions.DefaultScopeName : scopeName;
            this.RemoteOrchestrator = new RemoteOrchestrator(this.Provider, options ?? new SyncOptions())
            {
                BatchCleanupService = cleanupService ?? new BatchCleanupService(),
                BatchStorage = batchStore ?? new LocalFileSystemBatchStorage()
            };
            this.Identifier = identifier;
            this.BatchCreationJobService = batchCreationJobService;
            this.SessionCacheStore = sessionCacheStore;
        }

        /// <inheritdoc cref="WebServerAgent"/>
        public WebServerAgent(CoreProvider provider, string[] tables, SyncOptions options = null, WebServerOptions webServerOptions = null,
            string scopeName = null,
            string identifier = null,
            IBatchCleanupService cleanupService = null,
            IBatchCreationJobService batchCreationJobService = null,
            ISessionCacheStore sessionCacheStore = null,
            IErrorHandler errorHandler = null)
        {
            this.errorHandler = errorHandler ?? NullErrorHandler.Instance;
            this.Setup = new SyncSetup(tables);
            this.WebServerOptions = webServerOptions ?? new WebServerOptions();
            this.Provider = provider;
            this.RemoteOrchestrator = new RemoteOrchestrator(this.Provider, options ?? new SyncOptions())
            {
                BatchCleanupService = cleanupService ?? new BatchCleanupService(),
            };
            this.ScopeName = string.IsNullOrEmpty(scopeName) ? SyncOptions.DefaultScopeName : scopeName;
            this.Identifier = identifier;
            this.BatchCreationJobService = batchCreationJobService;
            this.SessionCacheStore = sessionCacheStore;
        }

        /// <summary>
        /// Client Converter.
        /// </summary>
        private IConverter clientConverter;

#if NET48
        /// <summary>
        /// Helper method to get session for NET48.
        /// </summary>
        private static HttpSessionStateBase GetSession(HttpContext httpContext)
        {
            return httpContext.Session;
        }

        /// <summary>
        /// Helper method to get host for NET48.
        /// </summary>
        private static string GetRequestHost(HttpRequest httpRequest)
        {
            return httpRequest.RequestUri?.Host ?? string.Empty;
        }
#else
        /// <summary>
        /// Helper method to get session for ASP.NET Core.
        /// </summary>
        private static Microsoft.AspNetCore.Http.ISession GetSession(HttpContext httpContext)
        {
            return httpContext.Session;
        }

        /// <summary>
        /// Helper method to get host for ASP.NET Core.
        /// </summary>
        private static string GetRequestHost(HttpContext httpContext)
        {
            return httpContext.Request.Host.Host;
        }
#endif

        /// <summary>
        /// Gets or Sets the setup used in this webServerAgent.
        /// </summary>
        public SyncSetup Setup { get; set; }

        /// <summary>
        /// Gets the options used in this webServerAgent.
        /// </summary>
        public SyncOptions Options => this.RemoteOrchestrator?.Options;

        /// <summary>
        /// Gets ts the options used in this webServerAgent.
        /// </summary>
        public CoreProvider Provider { get; private set; }

        /// <summary>
        /// Gets ts Web server options parameters.
        /// </summary>
        public WebServerOptions WebServerOptions { get; private set; }

        /// <summary>
        /// Gets ts an identifier used to identify your webServerAgent.
        /// Can be really usefull in multi sync scenarios.
        /// </summary>
        public string Identifier { get; private set; }

        /// <summary>
        /// Gets ts a scope name used when multiple SyncSetup in one server.
        /// Can be really usefull in multi sync scenarios.
        /// </summary>
        public string ScopeName { get; private set; }

        /// <summary>
        /// Gets the session cache store used for storing session data.
        /// If null, falls back to direct ASP.NET Session access for backward compatibility.
        /// </summary>
        internal ISessionCacheStore SessionCacheStore { get; private set; }

        /// <summary>
        /// Gets the RemoteOrchestrator used in this webServerAgent.
        /// </summary>
        public RemoteOrchestrator RemoteOrchestrator { get; private set; }

        /// <summary>
        /// Gets or sets the batch creation job service for async batch creation.
        /// When set together with <see cref="WebServerOptions.EnableAsyncBatchCreation"/>,
        /// batch creation for initial syncs is performed in the background.
        /// </summary>
        public IBatchCreationJobService BatchCreationJobService { get; set; }

        /// <summary>
        /// Get Scope Name sent by the client.
        /// </summary>
#if NET48
        public static Guid? GetClientScopeId(HttpRequest httpRequest) => httpRequest.Headers.TryGetHeaderValue("dotmim-sync-scope-id", out var val) ? new Guid(val) : null;
#else
        public static Guid? GetClientScopeId(HttpContext httpContext) => TryGetHeaderValue(httpContext.Request.Headers, "dotmim-sync-scope-id", out var val) ? new Guid(val) : null;
#endif

        /// <summary>
        /// Get Scope Name sent by the client.
        /// </summary>
#if NET48
        public static string GetScopeName(HttpRequest httpRequest) => httpRequest.Headers.TryGetHeaderValue("dotmim-sync-scope-name", out var val) ? val : null;
#else
        public static string GetScopeName(HttpContext httpContext) => TryGetHeaderValue(httpContext.Request.Headers, "dotmim-sync-scope-name", out var val) ? val : null;
#endif

        /// <summary>
        /// Get the DMS Version used by the client.
        /// </summary>
#if NET48
        public static string GetVersion(HttpRequest httpRequest) => httpRequest.Headers.TryGetHeaderValue("dotmim-sync-version", out var v) ? v : null;
#else
        public static string GetVersion(HttpContext httpContext) => TryGetHeaderValue(httpContext.Request.Headers, "dotmim-sync-version", out var v) ? v : null;
#endif

        /// <summary>
        /// Get the current client session id.
        /// </summary>
#if NET48
        public static string GetClientSessionId(HttpRequest httpRequest) => httpRequest.Headers.TryGetHeaderValue("dotmim-sync-session-id", out var val) ? val : null;
#else
        public static string GetClientSessionId(HttpContext httpContext) => TryGetHeaderValue(httpContext.Request.Headers, "dotmim-sync-session-id", out var val) ? val : null;
#endif

        /// <summary>
        /// Get the current Step.
        /// </summary>
#if NET48
        public static HttpStep GetCurrentStep(HttpRequest httpRequest) => httpRequest.Headers.TryGetHeaderValue("dotmim-sync-step", out var val) ? (HttpStep)SyncTypeConverter.TryConvertTo<int>(val) : HttpStep.None;
#else
        public static HttpStep GetCurrentStep(HttpContext httpContext) => TryGetHeaderValue(httpContext.Request.Headers, "dotmim-sync-step", out var val) ? (HttpStep)SyncTypeConverter.TryConvertTo<int>(val) : HttpStep.None;
#endif

        /// <summary>
        /// Get an header value.
        /// </summary>
#if NET48

        public static bool TryGetHeaderValue(NameValueCollection n, string key, out string header)
        {
            
            if (n.AllKeys.Contains(key))
            {
                header = n.Get(key);
                return true;
            }

            header = null;
            return false;
        }

#else
        public static bool TryGetHeaderValue(IHeaderDictionary n, string key, out string header)
        {
            if (n.TryGetValue(key, out var vs))
            {
                header = vs[0];
                return true;
            }

            header = null;
            return false;
        }
#endif

        /// <summary>
        /// Write server debug information.
        /// </summary>
        public static async Task WriteHelloAsync(HttpContext httpContext, IEnumerable<WebServerAgent> webServerAgents, CancellationToken cancellationToken = default)
        {
            var httpResponse = httpContext.Response;
            var stringBuilder = new StringBuilder();

            stringBuilder.AppendLine("<!doctype html>");
            stringBuilder.AppendLine("<html>");
            stringBuilder.AppendLine("<head>");
            stringBuilder.AppendLine("<meta charset='utf-8'>");
            stringBuilder.AppendLine("<meta name='viewport' content='width=device-width, initial-scale=1, shrink-to-fit=no'>");
            stringBuilder.AppendLine("<script src='https://cdn.jsdelivr.net/gh/google/code-prettify@master/loader/run_prettify.js'></script>");
            stringBuilder.AppendLine("<link rel='stylesheet' href='https://stackpath.bootstrapcdn.com/bootstrap/4.4.1/css/bootstrap.min.css' integrity='sha384-Vkoo8x4CGsO3+Hhxv8T/Q5PaXtkKtu6ug5TOeNV6gBiFeWPGFN9MuhOf23Q9Ifjh' crossorigin='anonymous'>");
            stringBuilder.AppendLine("</head>");
            stringBuilder.AppendLine("<title>Web Server properties</title>");
            stringBuilder.AppendLine("<body>");

            stringBuilder.AppendLine("<div class='container'>");
            stringBuilder.AppendLine("<h2>Web Server properties</h2>");

            foreach (var webServerAgent in webServerAgents)
            {
                string dbName = null;
                string version = null;
                string exceptionMessage = null;
                SyncContext context;
                bool hasException = false;

                try
                {
                    (context, dbName, version) = await webServerAgent.RemoteOrchestrator.GetHelloAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    exceptionMessage = ex.Message;
                    hasException = true;
                }

                stringBuilder.AppendLine("<ul class='list-group mb-2'>");
                stringBuilder.AppendLine($"<li class='list-group-item active'>Trying to reach database</li>");
                stringBuilder.AppendLine("</ul>");
                if (hasException)
                {
                    stringBuilder.AppendLine("<ul class='list-group mb-2'>");
                    stringBuilder.AppendLine($"<li class='list-group-item list-group-item-primary'>Exception occured</li>");
                    stringBuilder.AppendLine($"<li class='list-group-item list-group-item-danger'>");
                    stringBuilder.AppendLine(exceptionMessage);
                    stringBuilder.AppendLine("</li>");
                    stringBuilder.AppendLine("</ul>");
                }
                else
                {
                    stringBuilder.AppendLine("<ul class='list-group mb-2'>");
                    stringBuilder.AppendLine($"<li class='list-group-item list-group-item-primary'>Database</li>");
                    stringBuilder.AppendLine($"<li class='list-group-item list-group-item-light'>");
                    stringBuilder.AppendLine(CultureInfo.InvariantCulture, $"Check database {dbName}: Done.");
                    stringBuilder.AppendLine("</li>");
                    stringBuilder.AppendLine("</ul>");

                    stringBuilder.AppendLine("<ul class='list-group mb-2'>");
                    stringBuilder.AppendLine($"<li class='list-group-item list-group-item-primary'>Engine version</li>");
                    stringBuilder.AppendLine($"<li class='list-group-item list-group-item-light'>");
                    stringBuilder.AppendLine(version);
                    stringBuilder.AppendLine("</li>");
                    stringBuilder.AppendLine("</ul>");
                }

                var setup = await JsonSerializer.SerializeAsync(webServerAgent.Setup).ConfigureAwait(false);
                stringBuilder.AppendLine("<ul class='list-group mb-2'>");
                stringBuilder.AppendLine($"<li class='list-group-item list-group-item-primary'>Setup</li>");
                stringBuilder.AppendLine($"<li class='list-group-item list-group-item-light'>");
                stringBuilder.AppendLine("<pre class='prettyprint' style='border:0px;font-size:75%'>");
                stringBuilder.AppendLine(setup.ToUtf8String());
                stringBuilder.AppendLine("</pre>");
                stringBuilder.AppendLine("</li>");
                stringBuilder.AppendLine("</ul>");

                var provider = await JsonSerializer.SerializeAsync(webServerAgent.Provider).ConfigureAwait(false);
                stringBuilder.AppendLine("<ul class='list-group mb-2'>");
                stringBuilder.AppendLine($"<li class='list-group-item list-group-item-primary'>Provider</li>");
                stringBuilder.AppendLine($"<li class='list-group-item list-group-item-light'>");
                stringBuilder.AppendLine("<pre class='prettyprint' style='border:0px;font-size:75%'>");
                stringBuilder.AppendLine(provider.ToUtf8String());
                stringBuilder.AppendLine("</pre>");
                stringBuilder.AppendLine("</li>");
                stringBuilder.AppendLine("</ul>");

                var options = await JsonSerializer.SerializeAsync(webServerAgent.Options).ConfigureAwait(false);
                stringBuilder.AppendLine("<ul class='list-group mb-2'>");
                stringBuilder.AppendLine($"<li class='list-group-item list-group-item-primary'>Options</li>");
                stringBuilder.AppendLine($"<li class='list-group-item list-group-item-light'>");
                stringBuilder.AppendLine("<pre class='prettyprint' style='border:0px;font-size:75%'>");
                stringBuilder.AppendLine(options.ToUtf8String());
                stringBuilder.AppendLine("</pre>");
                stringBuilder.AppendLine("</li>");
                stringBuilder.AppendLine("</ul>");

                var webServerOptions = await JsonSerializer.SerializeAsync(webServerAgent.WebServerOptions).ConfigureAwait(false);
                stringBuilder.AppendLine("<ul class='list-group mb-2'>");
                stringBuilder.AppendLine($"<li class='list-group-item list-group-item-primary'>Web Server Options</li>");
                stringBuilder.AppendLine($"<li class='list-group-item list-group-item-light'>");
                stringBuilder.AppendLine("<pre class='prettyprint' style='border:0px;font-size:75%'>");
                stringBuilder.AppendLine(webServerOptions.ToUtf8String());
                stringBuilder.AppendLine("</pre>");
                stringBuilder.AppendLine("</li>");
                stringBuilder.AppendLine("</ul>");
            }

            stringBuilder.AppendLine("</div>");
            stringBuilder.AppendLine("</body>");
            stringBuilder.AppendLine("</html>");

#if NET48
            var content = stringBuilder.ToString();
            var bytes = System.Text.Encoding.UTF8.GetBytes(content);
            await httpResponse.OutputStream.WriteAsync(bytes, 0, bytes.Length, cancellationToken).ConfigureAwait(false);
#else
            await httpResponse.WriteAsync(stringBuilder.ToString(), cancellationToken).ConfigureAwait(false);
#endif
        }

        /// <summary>
        /// Call this method to handle requests on the server, sent by the client.
        /// </summary>
#if NET48
        public virtual Task<HttpResponse> HandleRequestAsync(HttpRequest request, HttpContext context, IProgress<ProgressArgs> progress = null, CancellationToken token = default) =>
            this.HandleRequestAsync(request, context, null, progress, token);

        /// <summary>
        /// Call this method to handle requests on the server, sent by the client.
        /// </summary>
        public virtual async Task<HttpResponse> HandleRequestAsync(HttpRequest httpRequest, HttpContext httpContext, Action<RemoteOrchestrator> action,
            IProgress<ProgressArgs> progress, CancellationToken cancellationToken)
#else
        public virtual Task HandleRequestAsync(HttpContext context, IProgress<ProgressArgs> progress = null, CancellationToken token = default) =>
            this.HandleRequestAsync(context, null, progress, token);

        /// <summary>
        /// Call this method to handle requests on the server, sent by the client.
        /// </summary>
        public virtual async Task HandleRequestAsync(HttpContext httpContext, Action<RemoteOrchestrator> action,
            IProgress<ProgressArgs> progress, CancellationToken cancellationToken)
#endif
        {

            if (this.SessionCacheStore is IRequiresHttpContext requiresContext)
                requiresContext.SetContext(httpContext);

#if !NET48
            var httpRequest = httpContext.Request;
            var httpResponse = httpContext.Response;
#else
            var httpResponse = httpRequest.CreateHttpResponse();
#endif

#if NET48
            httpRequest.Headers.TryGetHeaderValue("dotmim-sync-serialization-format", out var serializerInfoString);
            httpRequest.Headers.TryGetHeaderValue("dotmim-sync-converter", out var cliConverterKey);
            httpRequest.Headers.TryGetHeaderValue("dotmim-sync-version", out string version);

            if (!httpRequest.Headers.TryGetHeaderValue("dotmim-sync-session-id", out var sessionId))
                throw new HttpHeaderMissingException("dotmim-sync-session-id");

            if (!httpRequest.Headers.TryGetHeaderValue("dotmim-sync-scope-name", out var scopeName))
                throw new HttpHeaderMissingException("dotmim-sync-scope-name");

            if (!httpRequest.Headers.TryGetHeaderValue("dotmim-sync-step", out string iStep))
                throw new HttpHeaderMissingException("dotmim-sync-step");
#else
            TryGetHeaderValue(httpContext.Request.Headers, "dotmim-sync-serialization-format", out var serializerInfoString);
            TryGetHeaderValue(httpContext.Request.Headers, "dotmim-sync-converter", out var cliConverterKey);
            TryGetHeaderValue(httpContext.Request.Headers, "dotmim-sync-version", out string version);

            if (!TryGetHeaderValue(httpContext.Request.Headers, "dotmim-sync-session-id", out var sessionId))
                throw new HttpHeaderMissingException("dotmim-sync-session-id");

            if (!TryGetHeaderValue(httpContext.Request.Headers, "dotmim-sync-scope-name", out var scopeName))
                throw new HttpHeaderMissingException("dotmim-sync-scope-name");

            if (!TryGetHeaderValue(httpContext.Request.Headers, "dotmim-sync-step", out string iStep))
                throw new HttpHeaderMissingException("dotmim-sync-step");
#endif

            var step = (HttpStep)SyncTypeConverter.TryConvertTo<int>(iStep);
            var readableStream = new MemoryStream();

            try
            {
                // check if we need to upgrade
                await UpgradeAsync(this.RemoteOrchestrator).ConfigureAwait(false);

                // Copty stream to a readable and seekable stream
                // HttpRequest.Body is a HttpRequestStream that is readable but can't be Seek
#if NET48
                await httpRequest.ReadBodyAsync(readableStream).ConfigureAwait(false);
#elif NET6_0_OR_GREATER
                await httpRequest.Body.CopyToAsync(readableStream, cancellationToken).ConfigureAwait(false);
                httpRequest.Body.Close();
                await httpRequest.Body.DisposeAsync().ConfigureAwait(false);
#else
                await httpRequest.Body.CopyToAsync(readableStream).ConfigureAwait(false);
                httpRequest.Body.Close();
                httpRequest.Body.Dispose();
#endif

                // if Hash is present in header, check hash
#if NET48
                if (httpRequest.Headers.TryGetHeaderValue("dotmim-sync-hash", out string hashStringRequest))
#else
                if (TryGetHeaderValue(httpContext.Request.Headers, "dotmim-sync-hash", out string hashStringRequest))
#endif
                    HashAlgorithm.SHA256.EnsureHash(readableStream, hashStringRequest);
                else
                    readableStream.Seek(0, SeekOrigin.Begin);

                if (!string.Equals(scopeName, this.ScopeName, SyncGlobalization.DataSourceStringComparison))
                    throw new HttpScopeNameFromClientIsInvalidException(scopeName, this.ScopeName);

                SessionCache sessionCache;

                if (this.SessionCacheStore != null)
                {
                    // Use abstracted store
                    sessionCache = await this.SessionCacheStore.GetAsync(sessionId, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    // Fall back to direct Session access (backward compatibility)
#if NET48
                    // In NET48, session is accessed via System.Web.HttpContext.Current.Session
                    var session = httpContext.Session;
                    sessionCache = session.Get<SessionCache>(sessionId);
#else
                    // load session
                    await httpContext.Session.LoadAsync(cancellationToken).ConfigureAwait(false);

                    // Get schema and clients batch infos / summaries, from session
                    // var schema = httpContext.Session.Get<SyncSet>(scopeName);
                    sessionCache = httpContext.Session.Get<SessionCache>(sessionId);
#endif
                }

                // HttpStep.EnsureSchema is the first call from client when client is new
                // HttpStep.EnsureScopes is the first call from client when client is not new
                // This is the only moment where we are initializing the sessionCache and store it in session
                if (sessionCache == null &&
                    (step == HttpStep.EnsureSchema || step == HttpStep.EnsureScopes || step == HttpStep.GetRemoteClientTimestamp || step == HttpStep.SendChangesIncremental))
                {
                    sessionCache = new SessionCache();

                    if (this.SessionCacheStore != null)
                    {
                        await this.SessionCacheStore.SetAsync(sessionId, sessionCache, cancellationToken).ConfigureAwait(false);
                        await this.SessionCacheStore.SetSessionIdAsync("session_id", sessionId, cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
#if NET48
                        var session = GetSession(httpContext);
                        session.Set(sessionId, sessionCache);
                        session.SetString("session_id", sessionId);
#else
                        httpContext.Session.Set(sessionId, sessionCache);
                        httpContext.Session.SetString("session_id", sessionId);
#endif
                    }
                }

                // if sessionCache is still null, then we are in a step where it should not be null.
                // Probably because of a weird server restart or something...
                if (sessionCache == null)
                    throw new HttpSessionLostException(sessionId);

                // check session id
                string tempSessionId;

                if (this.SessionCacheStore != null)
                {
                    tempSessionId = await this.SessionCacheStore.GetSessionIdAsync("session_id", cancellationToken).ConfigureAwait(false);
                }
                else
                {
#if NET48
                    var session = GetSession(httpContext);
                    tempSessionId = session.GetString("session_id");
#else
                    tempSessionId = httpContext.Session.GetString("session_id");
#endif
                }

                // check session
                var requiresSession = step != HttpStep.SendSyncErrors;
                
                if (requiresSession && (string.IsNullOrEmpty(tempSessionId) || tempSessionId != sessionId))
                    throw new HttpSessionLostException(sessionId);

                // Get the serializer and batchsize
                (var clientBatchSize, var clientSerializerFactory) = this.GetClientSerializer(serializerInfoString);

                // Get converter used by client
                // Can be null
                this.clientConverter = this.GetClientConverter(cliConverterKey);

                byte[] binaryData = null;
                Type responseSerializerType = null;
                Type requestSerializerType = null;
                IScopeMessage messageResponse = null;

                switch (step)
                {
                    case HttpStep.None:
                        break;
                    case HttpStep.EnsureSchema:
                        requestSerializerType = typeof(HttpMessageEnsureScopesRequest);
                        responseSerializerType = typeof(HttpMessageEnsureSchemaResponse);
                        break;
                    case HttpStep.EnsureScopes:
                        requestSerializerType = typeof(HttpMessageEnsureScopesRequest);
                        responseSerializerType = typeof(HttpMessageEnsureScopesResponse);
                        break;
                    case HttpStep.SendChanges:
                        break;
                    case HttpStep.SendChangesInProgress:
                        requestSerializerType = typeof(HttpMessageSendChangesRequest);
                        responseSerializerType = typeof(HttpMessageSummaryResponse);
                        break;
                    case HttpStep.GetChanges:
                        break;
                    case HttpStep.GetEstimatedChangesCount:
                        requestSerializerType = typeof(HttpMessageSendChangesRequest);
                        responseSerializerType = typeof(HttpMessageSendChangesResponse);
                        break;
                    case HttpStep.GetMoreChanges:
                        requestSerializerType = typeof(HttpMessageGetMoreChangesRequest);
                        responseSerializerType = typeof(HttpMessageSendChangesResponse);
                        break;
                    case HttpStep.GetChangesInProgress:
                        break;
                    case HttpStep.GetSnapshot:
                        requestSerializerType = typeof(HttpMessageSendChangesRequest);
                        responseSerializerType = typeof(HttpMessageSendChangesResponse);
                        break;
                    case HttpStep.GetSummary:
                        requestSerializerType = typeof(HttpMessageSendChangesRequest);
                        responseSerializerType = typeof(HttpMessageSummaryResponse);
                        break;
                    case HttpStep.SendEndDownloadChanges:
                        requestSerializerType = typeof(HttpMessageGetMoreChangesRequest);
                        responseSerializerType = typeof(HttpMessageSendChangesResponse);
                        break;
                    case HttpStep.GetRemoteClientTimestamp:
                        requestSerializerType = typeof(HttpMessageRemoteTimestampRequest);
                        responseSerializerType = typeof(HttpMessageRemoteTimestampResponse);
                        break;
                    case HttpStep.GetOperation:
                        requestSerializerType = typeof(HttpMessageOperationRequest);
                        responseSerializerType = typeof(HttpMessageOperationResponse);
                        break;
                    case HttpStep.EndSession:
                        requestSerializerType = typeof(HttpMessageEndSessionRequest);
                        responseSerializerType = typeof(HttpMessageEndSessionResponse);
                        break;
                    case HttpStep.SendChangesIncremental:
                        requestSerializerType = typeof(HttpMessageSendChangesIncrementalRequest);
                        responseSerializerType = typeof(HttpMessageSendChangesIncrementalResponse);
                        break;
                    case HttpStep.SendSyncErrors:
                        requestSerializerType = typeof(HttpMessageSendSyncErrorsRequest);
                        responseSerializerType = typeof(HttpMessageSendSyncErrorsResponse);
                        break;
                }

                IScopeMessage messsageRequest = await clientSerializerFactory.GetSerializer().DeserializeAsync(readableStream, requestSerializerType).ConfigureAwait(false) as IScopeMessage;
                await this.RemoteOrchestrator.InterceptAsync(new HttpGettingRequestArgs(httpContext, messsageRequest.SyncContext, sessionCache, messsageRequest, requestSerializerType, step), progress, cancellationToken).ConfigureAwait(false);

                switch (step)
                {
                    case HttpStep.EnsureScopes:
                        messageResponse = await this.EnsureScopesAsync(httpContext, (HttpMessageEnsureScopesRequest)messsageRequest, sessionCache, progress, cancellationToken).ConfigureAwait(false);
                        break;
                    case HttpStep.EnsureSchema: // pre v 0.9.6
                        var s11 = await this.EnsureScopesAsync(httpContext, (HttpMessageEnsureScopesRequest)messsageRequest, sessionCache, progress, cancellationToken).ConfigureAwait(false);
                        messageResponse = new HttpMessageEnsureSchemaResponse(s11.SyncContext, s11.ServerScopeInfo);
                        break;
                    case HttpStep.SendChangesInProgress:
                        var sendChangesRequest = (HttpMessageSendChangesRequest)messsageRequest;
#if NET48
                        await this.RemoteOrchestrator.InterceptAsync(new HttpGettingClientChangesArgs(sendChangesRequest, GetRequestHost(httpRequest), sessionCache), progress, cancellationToken).ConfigureAwait(false);
#else
                        await this.RemoteOrchestrator.InterceptAsync(new HttpGettingClientChangesArgs(sendChangesRequest, GetRequestHost(httpContext), sessionCache), progress, cancellationToken).ConfigureAwait(false);
#endif
                        messageResponse = await this.ApplyThenGetChangesAsync2(httpContext, sendChangesRequest, sessionCache, clientBatchSize, progress, cancellationToken).ConfigureAwait(false);
                        break;
                    case HttpStep.GetMoreChanges:
                        messageResponse = await this.GetMoreChangesAsync(httpContext, (HttpMessageGetMoreChangesRequest)messsageRequest, sessionCache, progress, cancellationToken).ConfigureAwait(false);
                        break;
                    case HttpStep.GetSnapshot:
                        messageResponse = await this.GetSnapshotAsync(httpContext, (HttpMessageSendChangesRequest)messsageRequest, sessionCache, progress, cancellationToken).ConfigureAwait(false);
                        break;

                    // version >= 0.8
                    case HttpStep.GetSummary:
                        messageResponse = await this.GetSnapshotSummaryAsync(httpContext, (HttpMessageSendChangesRequest)messsageRequest, sessionCache, progress, cancellationToken).ConfigureAwait(false);
                        break;
                    case HttpStep.SendEndDownloadChanges:
                        messageResponse = await this.SendEndDownloadChangesAsync(httpContext, (HttpMessageGetMoreChangesRequest)messsageRequest, sessionCache, progress, cancellationToken).ConfigureAwait(false);
                        break;
                    case HttpStep.GetEstimatedChangesCount:
                        messageResponse = await this.GetEstimatedChangesCountAsync(httpContext, (HttpMessageSendChangesRequest)messsageRequest, progress, cancellationToken).ConfigureAwait(false);
                        break;
                    case HttpStep.GetRemoteClientTimestamp:
                        messageResponse = await this.GetRemoteClientTimestampAsync(httpContext, (HttpMessageRemoteTimestampRequest)messsageRequest, progress, cancellationToken).ConfigureAwait(false);
                        break;
                    case HttpStep.GetOperation:
                        messageResponse = await this.GetOperationAsync(httpContext, (HttpMessageOperationRequest)messsageRequest, progress, cancellationToken).ConfigureAwait(false);
                        break;
                    case HttpStep.EndSession:
                        messageResponse = await this.EndSessionAsync(httpContext, (HttpMessageEndSessionRequest)messsageRequest, progress, cancellationToken).ConfigureAwait(false);
                        break;
                    case HttpStep.SendChangesIncremental:
                        var sendChangesRequest2 = (HttpMessageSendChangesRequest)messsageRequest;
#if NET48
                        await this.RemoteOrchestrator.InterceptAsync(new HttpGettingClientChangesArgs(sendChangesRequest2, GetRequestHost(httpRequest), sessionCache), progress, cancellationToken).ConfigureAwait(false);
#else
                        await this.RemoteOrchestrator.InterceptAsync(new HttpGettingClientChangesArgs(sendChangesRequest2, GetRequestHost(httpContext), sessionCache), progress, cancellationToken).ConfigureAwait(false);
#endif
                        messageResponse = await this.SendChangesIncrementalAsync(httpContext, (HttpMessageSendChangesIncrementalRequest)messsageRequest, sessionCache, clientBatchSize, progress, cancellationToken).ConfigureAwait(false);
                        break;
                    case HttpStep.SendSyncErrors:
                        messageResponse = await this.SendSyncErrorsAsync(httpContext, (HttpMessageSendSyncErrorsRequest)messsageRequest, progress, cancellationToken).ConfigureAwait(false);
                        break;
                }

                if (this.SessionCacheStore != null)
                {
                    // Use abstracted store
                    await this.SessionCacheStore.SetAsync(sessionId, sessionCache, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    // Fall back to direct Session access (backward compatibility)
#if NET48
                    var session = GetSession(httpContext);
                    session.Set(sessionId, sessionCache);
                    // No need to commit in System.Web.SessionState - it's automatic
#else
                    httpContext.Session.Set(sessionId, sessionCache);
                    await httpContext.Session.CommitAsync(cancellationToken).ConfigureAwait(false);
#endif
                }

                if (messageResponse is HttpMessageSendChangesResponse httpMessageSendChangesResponse)
#if NET48
                    await this.RemoteOrchestrator.InterceptAsync(new HttpSendingServerChangesArgs(httpMessageSendChangesResponse, GetRequestHost(httpRequest), sessionCache, false), progress, cancellationToken).ConfigureAwait(false);
#else
                    await this.RemoteOrchestrator.InterceptAsync(new HttpSendingServerChangesArgs(httpMessageSendChangesResponse, GetRequestHost(httpContext), sessionCache, false), progress, cancellationToken).ConfigureAwait(false);
#endif

                await this.RemoteOrchestrator.InterceptAsync(new HttpSendingResponseArgs(httpContext, messageResponse.SyncContext, sessionCache, messageResponse, responseSerializerType, step), progress, cancellationToken).ConfigureAwait(false);

                binaryData = await clientSerializerFactory.GetSerializer().SerializeAsync(messageResponse, responseSerializerType).ConfigureAwait(false);

                // Adding the serialization format used and session id
#if NET48
                httpResponse.Headers.Add("dotmim-sync-session-id", sessionId.ToString());
                httpResponse.Headers.Add("dotmim-sync-serialization-format", clientSerializerFactory.Key);
#else
                httpResponse.Headers.Append("dotmim-sync-session-id", sessionId.ToString());
                httpResponse.Headers.Append("dotmim-sync-serialization-format", clientSerializerFactory.Key);
#endif

                // calculate hash
                var hash = HashAlgorithm.SHA256.Create(binaryData);
                var hashString = Convert.ToBase64String(hash);

                // Add hash to header
#if NET48
                httpResponse.Headers.Add("dotmim-sync-hash", hashString);
#else
                httpResponse.Headers.Append("dotmim-sync-hash", hashString);
#endif

                // data to send back, as the response
                byte[] data = this.EnsureCompression(httpRequest, httpResponse, binaryData);

#if NET48

#elif NET6_0_OR_GREATER
                await httpResponse.Body.WriteAsync(data.AsMemory(0, data.Length), cancellationToken).ConfigureAwait(false);
#else
                await httpResponse.Body.WriteAsync(data, 0, data.Length, cancellationToken).ConfigureAwait(false);
#endif

            }
            catch (Exception ex)
            {
                await this.errorHandler.HandleAsync(new(ex, httpRequest));
                await this.WriteExceptionAsync(httpRequest, httpResponse, ex).ConfigureAwait(false);
            }
            finally
            {
                await readableStream.FlushAsync(cancellationToken).ConfigureAwait(false);
                readableStream.Close();
#if NET6_0_OR_GREATER
                await readableStream.DisposeAsync().ConfigureAwait(false);
#else
                readableStream.Dispose();

#endif
            }

#if NET48
            return httpResponse;
#endif
        }

        /// <summary>
        /// Write exception to output message.
        /// </summary>
        public virtual async Task WriteExceptionAsync(HttpRequest httpRequest, HttpResponse httpResponse, Exception exception)
        {

            string message;

            if (this.Options.UseVerboseErrors)
            {
                message = exception is SyncException se && se.BaseMessage != null ? se.BaseMessage : exception.Message;
                var innerException = exception.InnerException;
                int cpt = 1;
                if (innerException != null)
                {
                    message += Environment.NewLine;
                    message += "  -----------------------" + Environment.NewLine;
                }

                while (innerException != null)
                {
                    message += Environment.NewLine;
                    var sign = innerException.InnerException != null ? "  ├" : "  └";
                    message += sign;

                    for (int i = 0; i < cpt; i++)
                        message += "  ─";

                    message += $" {innerException.Message}";

                    innerException = innerException.InnerException;
                    cpt++;
                }
            }
            else
            {
                message = "Synchronization failed on the server side. Please contact your admin.";
            }

            var syncException = new SyncException(exception, message);

            var webException = new WebSyncException
            {
                Message = message,
                SyncStage = syncException.SyncStage,
                TypeName = syncException.TypeName,
                DataSource = syncException.DataSource,
                InitialCatalog = syncException.InitialCatalog,
                Number = syncException.Number,
            };

            var data = await JsonSerializer.SerializeAsync(webException).ConfigureAwait(false);

            // data to send back, as the response
            byte[] compressedData = this.EnsureCompression(httpRequest, httpResponse, data);

#if NET48
            httpResponse.Headers.Add("dotmim-sync-error", syncException.TypeName);
            httpResponse.StatusCode = System.Net.HttpStatusCode.BadRequest;
#else
            httpResponse.Headers.Append("dotmim-sync-error", syncException.TypeName);
            httpResponse.StatusCode = StatusCodes.Status400BadRequest;
            httpResponse.ContentLength = compressedData.Length;
#if NET6_0_OR_GREATER
            await httpResponse.Body.WriteAsync(compressedData).ConfigureAwait(false);
#else
            await httpResponse.Body.WriteAsync(compressedData, 0, compressedData.Length).ConfigureAwait(false);
#endif
#endif
        }

        /// <summary>
        /// Write server debug information.
        /// </summary>
        public Task WriteHelloAsync(HttpContext context, CancellationToken cancellationToken = default)
            => WriteHelloAsync(context, [this], cancellationToken);

#if NET48
        /// <summary>
        /// Ensure we have a Compression setting or not (NET48 version).
        /// </summary>
        public virtual byte[] EnsureCompression(System.Net.Http.HttpRequestMessage httpRequest, System.Net.Http.HttpResponseMessage httpResponse, byte[] binaryData)
        {
            // Compress data if client accept Gzip / Deflate
            if (httpRequest.Headers.TryGetValue("Accept-Encoding", out var encoding) && (encoding.Contains("gzip") || encoding.Contains("deflate")))
            {
                using var writeSteam = new MemoryStream();

                using (var compress = new GZipStream(writeSteam, CompressionMode.Compress))
                {
                    compress.Write(binaryData, 0, binaryData.Length);
                    compress.Flush();
                }

                var b = writeSteam.ToArray();
                writeSteam.Flush();
                
                httpResponse.Content = new System.Net.Http.ByteArrayContent(b);

                if (!httpResponse.Content.Headers.Contains("Content-Encoding"))
                    httpResponse.Content.Headers.ContentEncoding.Add("gzip");

                return b;

            }
            
            httpResponse.Content = new System.Net.Http.ByteArrayContent(binaryData);

            return binaryData;
        }
#else
        /// <summary>
        /// Ensure we have a Compression setting or not.
        /// </summary>
        public virtual byte[] EnsureCompression(HttpRequest httpRequest, HttpResponse httpResponse, byte[] binaryData)
        {
            // Compress data if client accept Gzip / Deflate
            if (httpRequest.Headers.TryGetValue("Accept-Encoding", out var encoding) && (encoding.Contains("gzip") || encoding.Contains("deflate")))
            {
                if (!httpResponse.Headers.ContainsKey("Content-Encoding"))
                    httpResponse.Headers.Append("Content-Encoding", "gzip");

                using var writeSteam = new MemoryStream();

                using (var compress = new GZipStream(writeSteam, CompressionMode.Compress))
                {
                    compress.Write(binaryData, 0, binaryData.Length);
                    compress.Flush();
                }

                var b = writeSteam.ToArray();
                writeSteam.Flush();
                return b;
            }

            return binaryData;
        }
#endif

        /// <summary>
        /// Returns the serializer used by the client, that should be used on the server.
        /// </summary>
        public virtual (int ClientBatchSize, ISerializerFactory ClientSerializer) GetClientSerializer(string serializerInfoString)
        {
            try
            {
                if (string.IsNullOrEmpty(serializerInfoString))
                    throw new Exception("Serializer header is null, coming from http header");

                var serializerInfo = JsonSerializer.Deserialize<SerializerInfo>(serializerInfoString);

                var clientSerializerFactory = this.WebServerOptions.SerializerFactories.FirstOrDefault(sf => sf.Key == serializerInfo.SerializerKey);
                clientSerializerFactory ??= SerializersFactory.JsonSerializerFactory;

                var clientBatchSize = serializerInfo.ClientBatchSize;

                if (clientBatchSize < 100)
                    clientBatchSize = Math.Max(100, this.Options.BatchSize);

                return (clientBatchSize, clientSerializerFactory);
            }
            catch
            {
                throw new Exception("Serializer header is incorrect, coming from http header");
            }
        }

        /// <summary>
        /// Returns the converter used by the client, that should be used on the server.
        /// </summary>
        public virtual IConverter GetClientConverter(string cliConverterKey)
        {
            try
            {
                if (string.IsNullOrEmpty(cliConverterKey))
                    return null;

                var clientConverter = this.WebServerOptions.Converters.First(c => c.Key.Equals(cliConverterKey, StringComparison.OrdinalIgnoreCase));

                return clientConverter;
            }
            catch
            {
                throw new HttpConverterNotConfiguredException(this.WebServerOptions.Converters.Select(sf => sf.Key));
            }
        }

        /// <summary>
        /// Ensure we have the latest version of the server side.
        /// </summary>
        protected internal virtual async Task<HttpMessageEnsureScopesResponse> EnsureScopesAsync(HttpContext httpContext, HttpMessageEnsureScopesRequest httpMessage, SessionCache sessionCache,
                                            IProgress<ProgressArgs> progress, CancellationToken cancellationToken = default)
        {
            if (httpMessage == null)
                throw new ArgumentException("EnsureScopesAsync message could not be null");

            if (this.Setup == null)
                throw new ArgumentException("You need to set the tables to sync on server side");

            var context = httpMessage.SyncContext;

            ScopeInfo serverScopeInfo;
            bool shouldProvision;
            (context, serverScopeInfo, shouldProvision) = await this.RemoteOrchestrator.InternalEnsureScopeInfoAsync(context, this.Setup, false, default, default, progress, cancellationToken).ConfigureAwait(false);

            // TODO : Is it used ?
            GetSession(httpContext).Set(httpMessage.SyncContext.ScopeName, serverScopeInfo.Schema);

            // Provision if needed
            if (shouldProvision)
            {
                // 2) Provision
                var provision = SyncProvision.TrackingTable | SyncProvision.StoredProcedures | SyncProvision.Triggers;
                (context, serverScopeInfo) = await this.RemoteOrchestrator.InternalProvisionServerAsync(serverScopeInfo, context, provision, false, default, default, progress, cancellationToken).ConfigureAwait(false);
            }

            // Create http response
            var httpResponse = new HttpMessageEnsureScopesResponse(context, serverScopeInfo);

            return httpResponse;
        }

        /// <summary>
        /// Get estimated changes count only.
        /// </summary>
        protected internal virtual async Task<HttpMessageSendChangesResponse> GetEstimatedChangesCountAsync(HttpContext httpContext, HttpMessageSendChangesRequest httpMessage,
                        IProgress<ProgressArgs> progress = null, CancellationToken cancellationToken = default)
        {
            var changes = await this.RemoteOrchestrator.GetEstimatedChangesCountAsync(httpMessage.ScopeInfoClient).ConfigureAwait(false);

            var changesResponse = new HttpMessageSendChangesResponse(httpMessage.SyncContext)
            {
                ServerChangesSelected = changes.ServerChangesSelected,
                ClientChangesApplied = new DatabaseChangesApplied(),
                ServerStep = HttpStep.GetMoreChanges,
                ConflictResolutionPolicy = this.Options.ConflictResolutionPolicy,
                IsLastBatch = true,
                RemoteClientTimestamp = changes.RemoteClientTimestamp,
                ServerScopeId = changes.ServerScopeId,
            };

            return changesResponse;
        }

        /// <summary>
        /// Get remote client timestamp.
        /// </summary>
        protected internal virtual async Task<HttpMessageRemoteTimestampResponse> GetRemoteClientTimestampAsync(HttpContext httpContext, HttpMessageRemoteTimestampRequest httpMessage,
                IProgress<ProgressArgs> progress = null, CancellationToken cancellationToken = default)
        {
            var ts = await this.RemoteOrchestrator.GetLocalTimestampAsync(httpMessage.SyncContext.ScopeName).ConfigureAwait(false);

            return new HttpMessageRemoteTimestampResponse(httpMessage.SyncContext, ts);
        }

        /// <summary>
        /// Get overriden operation to send to the client.
        /// </summary>
        protected internal virtual async Task<HttpMessageOperationResponse> GetOperationAsync(HttpContext httpContext, HttpMessageOperationRequest httpMessage,
             IProgress<ProgressArgs> progress = null, CancellationToken cancellationToken = default)
        {
            var context = httpMessage.SyncContext;

            ScopeInfo serverScopeInfo;

            (context, serverScopeInfo, _) = await this.RemoteOrchestrator.InternalEnsureScopeInfoAsync(context, this.Setup, false, default, default, progress, cancellationToken).ConfigureAwait(false);

            SyncOperation operation;
            (context, operation) = await this.RemoteOrchestrator.InternalGetOperationAsync(serverScopeInfo, httpMessage.ScopeInfoFromClient, httpMessage.ScopeInfoClient, context, default, default, progress, cancellationToken).ConfigureAwait(false);

            return new HttpMessageOperationResponse(context, operation);
        }

        /// <summary>
        /// End the session.
        /// </summary>
        protected internal virtual async Task<HttpMessageEndSessionResponse> EndSessionAsync(HttpContext httpContext, HttpMessageEndSessionRequest httpMessage,
             IProgress<ProgressArgs> progress = null, CancellationToken cancellationToken = default)
        {
            var context = httpMessage.SyncContext;

            var result = new SyncResult(context.SessionId)
            {
                ChangesAppliedOnClient = httpMessage.ChangesAppliedOnClient,
                ChangesAppliedOnServer = httpMessage.ChangesAppliedOnServer,
                ClientChangesSelected = httpMessage.ClientChangesSelected,
                CompleteTime = httpMessage.CompleteTime,
                ScopeName = context.ScopeName,
                ServerChangesSelected = httpMessage.ServerChangesSelected,
                SnapshotChangesAppliedOnClient = httpMessage.SnapshotChangesAppliedOnClient,
                StartTime = httpMessage.StartTime,
            };

            SyncException syncException = null;
            if (httpMessage.SyncExceptionMessage != null)
                syncException = new SyncException(httpMessage.SyncExceptionMessage);

            context = await this.RemoteOrchestrator.InternalEndSessionAsync(context, result, null, syncException, progress, cancellationToken).ConfigureAwait(false);

            return new HttpMessageEndSessionResponse(context);
        }

        /// <summary>
        /// Gets the snapshot summary.
        /// </summary>
        protected internal virtual async Task<HttpMessageSummaryResponse> GetSnapshotSummaryAsync(HttpContext httpContext, HttpMessageSendChangesRequest httpMessage, SessionCache sessionCache,
                        IProgress<ProgressArgs> progress = null, CancellationToken cancellationToken = default)
        {
            // Get context from request message
            var context = httpMessage.SyncContext;

            ScopeInfo sScopeInfo;
            (context, sScopeInfo, _) = await this.RemoteOrchestrator.InternalEnsureScopeInfoAsync(context, this.Setup, false, default, default, progress, cancellationToken).ConfigureAwait(false);

            // TODO : Is it used ?
            GetSession(httpContext).Set(httpMessage.SyncContext.ScopeName, sScopeInfo.Schema);

            // get snapshot info
            ServerSyncChanges serverSyncChanges;
            (context, serverSyncChanges) =
                 await this.RemoteOrchestrator.InternalGetSnapshotAsync(sScopeInfo, context, default, default, progress, cancellationToken).ConfigureAwait(false);

            var summaryResponse = new HttpMessageSummaryResponse(context)
            {
                BatchInfo = serverSyncChanges.ServerBatchInfo,
                RemoteClientTimestamp = serverSyncChanges.RemoteClientTimestamp,
                ClientChangesApplied = new DatabaseChangesApplied(),
                ServerChangesSelected = serverSyncChanges.ServerChangesSelected,
                ConflictResolutionPolicy = this.Options.ConflictResolutionPolicy,
                Step = HttpStep.GetSummary,
            };

            // Save the server batch info object to cache
            sessionCache.RemoteClientTimestamp = serverSyncChanges.RemoteClientTimestamp;
            sessionCache.ServerBatchInfo = serverSyncChanges.ServerBatchInfo;
            sessionCache.ServerChangesSelected = serverSyncChanges.ServerChangesSelected;

            return summaryResponse;
        }

        /// <summary>
        /// Gets the snapshot.
        /// </summary>
        protected internal virtual async Task<HttpMessageSendChangesResponse> GetSnapshotAsync(HttpContext httpContext, HttpMessageSendChangesRequest httpMessage, SessionCache sessionCache,
                            IProgress<ProgressArgs> progress = null, CancellationToken cancellationToken = default)
        {
            ScopeInfo sScopeInfo;

            (_, sScopeInfo, _) = await this.RemoteOrchestrator.InternalEnsureScopeInfoAsync(httpMessage.SyncContext, this.Setup, false, default, default, progress, cancellationToken).ConfigureAwait(false);

            // TODO : Is it used ?
            GetSession(httpContext).Set(httpMessage.SyncContext.ScopeName, sScopeInfo.Schema);

            // get changes
            var snap = await this.RemoteOrchestrator.GetSnapshotAsync(sScopeInfo).ConfigureAwait(false);

            // Save the server batch info object to cache
            sessionCache.RemoteClientTimestamp = snap.RemoteClientTimestamp;
            sessionCache.ServerBatchInfo = snap.ServerBatchInfo;
            sessionCache.ServerChangesSelected = snap.ServerChangesSelected;

            // httpContext.Session.Set(sessionId, sessionCache);

            // if no snapshot, return empty response
            if (snap.ServerBatchInfo == null)
            {
                var changesResponse = new HttpMessageSendChangesResponse(httpMessage.SyncContext)
                {
                    ServerStep = HttpStep.GetSnapshot,
                    BatchIndex = 0,
                    BatchCount = 0,
                    IsLastBatch = true,
                    RemoteClientTimestamp = 0,
                    Changes = null,
                    ServerScopeId = sScopeInfo.Id,
                };
                return changesResponse;
            }

            sessionCache.RemoteClientTimestamp = snap.RemoteClientTimestamp;
            sessionCache.ServerBatchInfo = snap.ServerBatchInfo;

            // Get the firt response to send back to client
            return await this.GetChangesResponseAsync(httpContext, httpMessage.SyncContext, snap.RemoteClientTimestamp, snap.ServerBatchInfo, null, snap.ServerChangesSelected, 0).ConfigureAwait(false);
        }

        /// <summary>
        /// Apply changes to the server and then get the changes to send back to the client.
        /// </summary>
        protected internal virtual async Task<HttpMessageSummaryResponse> ApplyThenGetChangesAsync2(HttpContext httpContext, HttpMessageSendChangesRequest httpMessage, SessionCache sessionCache,
                        int clientBatchSize, IProgress<ProgressArgs> progress = null, CancellationToken cancellationToken = default)
        {
            // Overriding batch size options value, coming from client
            // having changes from server in batch size or not is decided by the client.
            // Basically this options is not used on the server, since it's always overriden by the client
            this.Options.BatchSize = clientBatchSize;

            var context = httpMessage.SyncContext;
            ScopeInfo sScopeInfo;
            (context, sScopeInfo, _) = await this.RemoteOrchestrator.InternalEnsureScopeInfoAsync(
                context, this.Setup, false, default, default, progress, cancellationToken).ConfigureAwait(false);

            // TODO : Is it used ?
            GetSession(httpContext).Set(context.ScopeName, sScopeInfo.Schema);

            // if we already applied all changes successfully, but the client retried uploading the last batch, simple return
            if(sessionCache?.AppliedBatchesSuccessfully == true)
                return new HttpMessageSummaryResponse(httpMessage.SyncContext)
                {
                    BatchInfo = sessionCache.ServerBatchInfo,
                    Step = HttpStep.GetSummary,
                    RemoteClientTimestamp = sessionCache.RemoteClientTimestamp,
                    ClientChangesApplied = sessionCache.ClientChangesApplied,
                    ServerChangesSelected = sessionCache.ServerChangesSelected,
                    ConflictResolutionPolicy = this.Options.ConflictResolutionPolicy,
                };
            
            // ------------------------------------------------------------
            // FIRST STEP : receive client changes
            // ------------------------------------------------------------

            // We are receiving changes from client
            // BatchInfo containing all BatchPartInfo objects
            // Retrieve batchinfo instance if exists
            // Get batch info from session cache if exists, otherwise create it
            sessionCache.ClientBatchInfo ??= new BatchInfo(this.Options.BatchDirectory, info: "REMOTEGETCHANGES");

            if (httpMessage.Changes != null && httpMessage.Changes.HasRows)
            {
                // Check if this is a unified batch from the client
                if (context.UseUnifiedBatching)
                {
                    // Serialize the entire ContainerSet to one unified file
                    var serializer = SerializersFactory.JsonSerializerFactory.GetSerializer();
                    // Handle unified batch - serialize the entire ContainerSet as one unified file
                    var fileName = BatchInfo.GenerateNewFileName(
                        httpMessage.BatchIndex.ToString(CultureInfo.InvariantCulture),
                        "UNIFIED", "json", "CLICHANGES");
                    var fullPath = Path.Combine(sessionCache.ClientBatchInfo.GetDirectoryFullPath(), fileName);

                    // Apply converter if needed before serialization
                    if (this.clientConverter != null)
                    {
                        foreach (var containerTable in httpMessage.Changes.Tables)
                        {
                            if (containerTable.HasRows)
                            {
                                var schemaTable = BaseOrchestrator.CreateChangesTable(
                                    sScopeInfo.Schema.Tables[containerTable.TableName, containerTable.SchemaName]);

                                foreach (var row in containerTable.Rows)
                                {
                                    // Row format is always: [state, col1, col2, ..., colN]
                                    var syncRow = new SyncRow(schemaTable, row);
                                    this.clientConverter.AfterDeserialized(syncRow, schemaTable);
                                }
                            }
                        }
                    }
                    var batchDirectoryPath = Path.GetDirectoryName(fullPath);
                    var batchFileName = Path.GetFileName(fullPath);
                    await this.RemoteOrchestrator.BatchStorage.EnsureDirectoryExistsAsync(batchDirectoryPath, cancellationToken).ConfigureAwait(false);
                    var data = await serializer.SerializeAsync(httpMessage.Changes).ConfigureAwait(false);
                    using (var dataStream = new MemoryStream(data))
                    {
                        await this.RemoteOrchestrator.BatchStorage.WriteBatchPartAsync(batchDirectoryPath, batchFileName, dataStream, cancellationToken).ConfigureAwait(false);
                    }

                    // Create single BatchPartInfo for the unified batch
                    var totalRowsCount = httpMessage.Changes.Tables.Sum(t => t.Rows?.Count ?? 0);
                    var tableRowCounts = new Dictionary<string, int>();
                    foreach (var table in httpMessage.Changes.Tables)
                    {
                        var tableKey = $"{table.SchemaName}.{table.TableName}";
                        tableRowCounts[tableKey] = table.Rows?.Count ?? 0;
                    }
                    var bpi = new BatchPartInfo
                    {
                        FileName = fileName,
                        TableName = "UNIFIED",
                        SchemaName = null,
                        RowsCount = totalRowsCount,
                        IsLastBatch = httpMessage.IsLastBatch,
                        Index = httpMessage.BatchIndex,
                        TableRowCounts = tableRowCounts,
                    };

                    sessionCache.ClientBatchInfo.RowsCount += bpi.RowsCount;
                    sessionCache.ClientBatchInfo.BatchPartsInfo.Add(bpi);
                }
                else
                {
                    // Traditional single-table batch
                    await using var localSerializer = new LocalJsonSerializer(this.RemoteOrchestrator.BatchStorage, this.RemoteOrchestrator, context);

                    // we have only one table here
                    var containerTable = httpMessage.Changes.Tables[0];
                    var schemaTable = BaseOrchestrator.CreateChangesTable(sScopeInfo.Schema.Tables[containerTable.TableName, containerTable.SchemaName]);

                var setupTable = new SetupTable(containerTable.TableName, containerTable.SchemaName);

                var tableName = setupTable.GetFullName().Replace(".", "_").Replace(" ", "_");

                var fileName = BatchInfo.GenerateNewFileName(httpMessage.BatchIndex.ToString(CultureInfo.InvariantCulture), tableName, localSerializer.FileExtension, "CLICHANGES");
                var directoryPath = sessionCache.ClientBatchInfo.GetDirectoryFullPath();

                SyncRowState syncRowState = SyncRowState.None;
                if (containerTable.Rows != null && containerTable.Rows.Count > 0)
                {
                    var sr = new SyncRow(schemaTable, containerTable.Rows[0]);
                    syncRowState = sr.RowState;
                }

                // open the file and write table header
                await localSerializer.OpenFileAsync(directoryPath, fileName, schemaTable, syncRowState).ConfigureAwait(false);

                foreach (var row in containerTable.Rows)
                {
                    var syncRow = new SyncRow(schemaTable, row);

                    if (this.clientConverter != null && syncRow.Length > 0)
                        this.clientConverter.AfterDeserialized(syncRow, schemaTable);

                    await localSerializer.WriteRowToFileAsync(syncRow, schemaTable).ConfigureAwait(false);
                }

                var bpi = new BatchPartInfo
                {
                    FileName = fileName,
                    TableName = containerTable.TableName,
                    SchemaName = containerTable.SchemaName,
                    RowsCount = containerTable.Rows.Count,
                    IsLastBatch = httpMessage.IsLastBatch,
                    Index = httpMessage.BatchIndex,
                };

                    sessionCache.ClientBatchInfo.RowsCount += bpi.RowsCount;
                    sessionCache.ClientBatchInfo.BatchPartsInfo.Add(bpi);
                }
            }

            // Clear the httpMessage set
            if (httpMessage.Changes != null)
                httpMessage.Changes.Clear();

            // Until we don't have received all the batches, wait for more
            if (!httpMessage.IsLastBatch)
                return new HttpMessageSummaryResponse(httpMessage.SyncContext) { Step = HttpStep.SendChangesInProgress };

            // ------------------------------------------------------------
            // ASYNC BATCH CREATION : Check if we should use async processing
            // ------------------------------------------------------------
            var isInitialSync = httpMessage.ScopeInfoClient?.IsNewScope == true ||
                               context.SyncType == Enumerations.SyncType.Reinitialize ||
                               context.SyncType == Enumerations.SyncType.ReinitializeWithUpload;

            if (this.WebServerOptions.EnableAsyncBatchCreation &&
                isInitialSync &&
                this.BatchCreationJobService != null)
            {
                // Generate deterministic job ID based on session and client scope
                var jobId = $"{context.SessionId}_{httpMessage.ScopeInfoClient?.Id ?? Guid.Empty}";
                sessionCache.AsyncBatchJobId = jobId;

                // Check for existing job (retry scenario)
                var existingStatus = await this.BatchCreationJobService.GetJobStatusAsync(jobId).ConfigureAwait(false);

                if (existingStatus != null)
                {
                    // Try to handle immediately (Completed/FirstBatchReady/Failed)
                    var response = await this.TryHandleBatchJobStatusAsync(httpContext, context, sessionCache, existingStatus, cancellationToken);
                    if (response != null)
                        return response;

                    // Still processing — poll with timeout
                    return await this.PollBatchCreationJobAsync(httpContext, context, sessionCache, jobId, cancellationToken);
                }

                // No existing job — enqueue a new one
                var jobParameters = new BatchCreationJobParameters
                {
                    ScopeName = context.ScopeName,
                    ServerScopeInfo = sScopeInfo,
                    ClientScopeInfoClient = httpMessage.ScopeInfoClient,
                    Context = context,
                    ClientBatchInfo = sessionCache.ClientBatchInfo,
                    BatchDirectory = this.Options.BatchDirectory,
                    BatchSize = this.Options.BatchSize,
                    UseUnifiedBatching = context.UseUnifiedBatching,
                    ProviderTypeName = this.Provider.GetType().AssemblyQualifiedName,
                    ConnectionString = this.Provider.ConnectionString
                };

                await this.BatchCreationJobService.EnqueueBatchCreationAsync(jobId, jobParameters, cancellationToken).ConfigureAwait(false);

                // Poll for results
                return await this.PollBatchCreationJobAsync(httpContext, context, sessionCache, jobId, cancellationToken);
            }

            // ------------------------------------------------------------
            // SECOND STEP : apply then return server changes (synchronous)
            // ------------------------------------------------------------
            ServerSyncChanges serverSyncChanges;
            context = httpMessage.SyncContext;
            var clientSyncChanges = new ClientSyncChanges(httpMessage.ClientLastSyncTimestamp, sessionCache.ClientBatchInfo, null, null);

            // get changes
            (context, serverSyncChanges, _) = await this.RemoteOrchestrator.InternalApplyThenGetChangesAsync(
                                               httpMessage.ScopeInfoClient,
                                               sScopeInfo,
                                               context,
                                               clientSyncChanges,
                                               default, default, progress, cancellationToken).ConfigureAwait(false);

            // Set session cache infos
            sessionCache.RemoteClientTimestamp = serverSyncChanges.RemoteClientTimestamp;
            sessionCache.ServerBatchInfo = serverSyncChanges.ServerBatchInfo;
            sessionCache.ServerChangesSelected = serverSyncChanges.ServerChangesSelected;
            sessionCache.ClientChangesApplied = serverSyncChanges.ServerChangesApplied;
            sessionCache.AppliedBatchesSuccessfully = true; // mark session as already applied => that way any intermittent error causing a client to retry will not be applied to the server anymore

            // delete the folder (not the BatchPartInfo, because we have a reference on it)
            var cleanFolder = this.Options.CleanFolder;

            if (cleanFolder)
                cleanFolder = await this.RemoteOrchestrator.InternalCanCleanFolderAsync(httpMessage.SyncContext.ScopeName, context.Parameters, sessionCache.ClientBatchInfo, default, cancellationToken).ConfigureAwait(false);

            if (cleanFolder)
                await sessionCache.ClientBatchInfo.TryRemoveDirectoryAsync().ConfigureAwait(false);

            // Retro compatiblité to version < 0.9.3
            if (serverSyncChanges.ServerBatchInfo.BatchPartsInfo == null)
                serverSyncChanges.ServerBatchInfo.BatchPartsInfo = [];

            var summaryResponse = new HttpMessageSummaryResponse(httpMessage.SyncContext)
            {
                BatchInfo = sessionCache.ServerBatchInfo,
                Step = HttpStep.GetSummary,
                RemoteClientTimestamp = sessionCache.RemoteClientTimestamp,
                ClientChangesApplied = sessionCache.ClientChangesApplied,
                ServerChangesSelected = sessionCache.ServerChangesSelected,
                ConflictResolutionPolicy = this.Options.ConflictResolutionPolicy,
            };

            // Get the firt response to send back to client
            return summaryResponse;
        }

        private async Task UpdateSession(HttpContext httpContext, SessionCache sessionCache,
            CancellationToken cancellationToken, SyncContext context)
        {
            var sessionId = context.SessionId.ToString();

            if (this.SessionCacheStore != null)
            {
                await this.SessionCacheStore.SetAsync(sessionId, sessionCache, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var session = GetSession(httpContext);
#if NET48
                session.Set(sessionId, sessionCache);
#else
                session.Set(sessionId, sessionCache);
                await session.CommitAsync(cancellationToken).ConfigureAwait(false);
#endif
            }
        }

        /// <summary>
        /// Handles a single <see cref="BatchCreationJobStatus"/> and returns a response for terminal states,
        /// or null when polling should continue.
        /// </summary>
        private async Task<HttpMessageSummaryResponse> TryHandleBatchJobStatusAsync(
           HttpContext httpContext, SyncContext context, SessionCache sessionCache,
           BatchCreationJobStatus status, CancellationToken cancellationToken)
        {
            if (status == null)
                return null;

            switch (status.State)
            {
                case BatchCreationJobState.Completed:
                    sessionCache.RemoteClientTimestamp = status.RemoteClientTimestamp ?? 0;
                    sessionCache.ServerBatchInfo = status.BatchInfo;
                    sessionCache.ServerChangesSelected = status.ChangesSelected;
                    sessionCache.ClientChangesApplied = status.ChangesApplied;
                    sessionCache.AppliedBatchesSuccessfully = true;

                    await this.UpdateSession(httpContext, sessionCache, cancellationToken, context);

                    var cleanFolder = this.Options.CleanFolder;
                    if (cleanFolder)
                        cleanFolder = await this.RemoteOrchestrator.InternalCanCleanFolderAsync(
                            context.ScopeName, context.Parameters, sessionCache.ClientBatchInfo, default, cancellationToken).ConfigureAwait(false);
                    if (cleanFolder)
                        await sessionCache.ClientBatchInfo.TryRemoveDirectoryAsync().ConfigureAwait(false);

                    return new HttpMessageSummaryResponse(context)
                    {
                        BatchInfo = sessionCache.ServerBatchInfo,
                        Step = HttpStep.GetSummary,
                        RemoteClientTimestamp = sessionCache.RemoteClientTimestamp,
                        ClientChangesApplied = sessionCache.ClientChangesApplied,
                        ServerChangesSelected = sessionCache.ServerChangesSelected,
                        ConflictResolutionPolicy = this.Options.ConflictResolutionPolicy,
                    };

                case BatchCreationJobState.Failed:
                    throw new SyncException(status.ErrorMessage ?? "Async batch creation failed");

                case BatchCreationJobState.FirstBatchReady:
                    if (status.AvailableBatchParts != null && status.AvailableBatchParts.Count > 0)
                    {
                        var partialBatchInfo = new BatchInfo
                        {
                            DirectoryRoot = status.BatchInfo?.DirectoryRoot ?? this.Options.BatchDirectory,
                            DirectoryName = status.BatchInfo?.DirectoryName,
                        };
                        partialBatchInfo.BatchPartsInfo = new List<BatchPartInfo>(status.AvailableBatchParts);

                        sessionCache.RemoteClientTimestamp = status.RemoteClientTimestamp ?? 0;
                        sessionCache.ServerBatchInfo = partialBatchInfo;
                        sessionCache.ServerChangesSelected = status.ChangesSelected;
                        sessionCache.ClientChangesApplied = status.ChangesApplied;

                        await this.UpdateSession(httpContext, sessionCache, cancellationToken, context);

                        var lastIndex = Math.Max(0, status.AvailableBatchParts.Max(bpi => bpi.Index));

                        return new HttpMessageSummaryResponse(context)
                        {
                            BatchInfo = partialBatchInfo,
                            Step = HttpStep.GetSummary,
                            RemoteClientTimestamp = status.RemoteClientTimestamp ?? 0,
                            ClientChangesApplied = status.ChangesApplied,
                            ServerChangesSelected = status.ChangesSelected,
                            ConflictResolutionPolicy = this.Options.ConflictResolutionPolicy,
                            MoreBatchesPending = true,
                            LastBatchIndex = lastIndex,
                            AsyncProgress = status.ProgressPercentage,
                        };
                    }

                    // FirstBatchReady but no parts available yet — continue polling
                    return null;

                default:
                    // Queued / Processing — continue polling
                    return null;
            }
        }

        /// <summary>
        /// Polls <see cref="IBatchCreationJobService"/> until a terminal response is available or timeout is reached.
        /// </summary>
        private async Task<HttpMessageSummaryResponse> PollBatchCreationJobAsync(
           HttpContext httpContext, SyncContext context, SessionCache sessionCache,
           string jobId, CancellationToken cancellationToken)
        {
            var timeout = this.WebServerOptions.AsyncBatchTimeout;
            var pollingInterval = this.WebServerOptions.AsyncBatchPollingInterval;
            var startTime = DateTime.UtcNow;

            BatchCreationJobStatus lastStatus = null;

            while (DateTime.UtcNow - startTime < timeout)
            {
                await Task.Delay(pollingInterval, cancellationToken).ConfigureAwait(false);

                lastStatus = await this.BatchCreationJobService.GetJobStatusAsync(jobId).ConfigureAwait(false);

                var response = await this.TryHandleBatchJobStatusAsync(httpContext, context, sessionCache, lastStatus, cancellationToken);
                if (response != null)
                    return response;
            }

            // Timeout — return InProgress response for client retry
            return new HttpMessageSummaryResponse(context)
            {
                Step = HttpStep.SendChangesInProgress,
                InProgress = true,
                AsyncProgress = lastStatus?.ProgressPercentage ?? 0,
                RetryAfterSeconds = (int)pollingInterval.TotalSeconds + 1,
            };
        }

        /// <summary>
        /// Get batch changes.
        /// </summary>
        protected internal virtual async Task<HttpMessageSendChangesResponse> GetMoreChangesAsync(HttpContext httpContext, HttpMessageGetMoreChangesRequest httpMessage,
            SessionCache sessionCache, IProgress<ProgressArgs> progress = null, CancellationToken cancellationToken = default)
        {
            // Refresh sessionCache from async batch job if one is active
            if (!string.IsNullOrEmpty(sessionCache.AsyncBatchJobId) && this.BatchCreationJobService != null)
            {
                try
                {
                    var status = await this.BatchCreationJobService.GetJobStatusAsync(sessionCache.AsyncBatchJobId).ConfigureAwait(false);
                    if (status != null)
                        await this.TryHandleBatchJobStatusAsync(httpContext, httpMessage.SyncContext, sessionCache, status, cancellationToken);
                }
                catch
                {
                    // Don't fail batch part downloads due to job check errors
                }
            }

            var response = await this.GetChangesResponseAsync(httpContext, httpMessage.SyncContext, sessionCache.RemoteClientTimestamp,
                sessionCache.ServerBatchInfo, sessionCache.ClientChangesApplied,
                sessionCache.ServerChangesSelected, httpMessage.BatchIndexRequested);

            if(response.IsLastBatch)
                await this.AutomaticallyEndSession(httpContext, httpMessage.SyncContext, sessionCache, progress, cancellationToken);

            return response;
        }

        private async Task AutomaticallyEndSession(HttpContext httpContext, SyncContext context,
            SessionCache sessionCache, IProgress<ProgressArgs> progress, CancellationToken cancellationToken)
        {
            // Handle session close integration
            if (TryGetHeaderValue(httpContext.Request.Headers, "dotmim-sync-optimized", out string autoEnd) && bool.TryParse(autoEnd, out var b) & b)
            {
                // Create EndSession request to reuse existing logic
                var endSessionRequest = new HttpMessageEndSessionRequest(context)
                {
                    ChangesAppliedOnClient = sessionCache.ClientChangesApplied,
                    ServerChangesSelected = sessionCache.ServerChangesSelected
                };

                // Execute EndSession logic
                var _ = await this.EndSessionAsync(httpContext, endSessionRequest, progress, cancellationToken);
            }
        }

        /// <summary>
        /// Get changes from server.
        /// </summary>
        protected internal virtual async Task<HttpMessageSendChangesResponse> GetChangesResponseAsync(HttpContext httpContext, SyncContext context, long remoteClientTimestamp, BatchInfo serverBatchInfo,
                              DatabaseChangesApplied clientChangesApplied, DatabaseChangesSelected serverChangesSelected, int batchIndexRequested)
        {
            ScopeInfo sScopeInfo;

            (context, sScopeInfo, _) = await this.RemoteOrchestrator.InternalEnsureScopeInfoAsync(
                context, this.Setup, false, default, default, default, default).ConfigureAwait(false);

            // TODO : Is it used ?
            GetSession(httpContext).Set(context.ScopeName, sScopeInfo.Schema);

            // 1) Create the http message content response
            var changesResponse = new HttpMessageSendChangesResponse(context)
            {
                ServerChangesSelected = serverChangesSelected,
                ClientChangesApplied = clientChangesApplied,
                ServerStep = HttpStep.GetMoreChanges,
                ConflictResolutionPolicy = this.Options.ConflictResolutionPolicy,
                ServerScopeId = sScopeInfo.Id,
            };

            if (serverBatchInfo == null)
                throw new Exception("serverBatchInfo is Null and should not be ....");

            // If nothing to do, just send back
            if (serverBatchInfo.BatchPartsInfo == null || serverBatchInfo.BatchPartsInfo.Count <= 0)
            {
                changesResponse.Changes = new ContainerSet();
                changesResponse.BatchIndex = 0;
                changesResponse.BatchCount = serverBatchInfo.BatchPartsInfo == null ? 0 : serverBatchInfo.BatchPartsInfo.Count;
                changesResponse.IsLastBatch = true;
                changesResponse.RemoteClientTimestamp = remoteClientTimestamp;
                return changesResponse;
            }

            // Get the batch part index requested
            var batchPartInfo = serverBatchInfo.BatchPartsInfo.First(d => d.Index == batchIndexRequested);

            // Generate the ContainerSet containing rows to send to the user
            var containerSet = new ContainerSet();
            var fullPath = Path.Combine(serverBatchInfo.GetDirectoryFullPath(), batchPartInfo.FileName);

            // Check if this is a unified batch file (indicated by table name "UNIFIED")
            if (batchPartInfo.TableName == "UNIFIED")
            {
                // Handle unified batch file - deserialize the entire ContainerSet
                var serializer = SerializersFactory.JsonSerializerFactory.GetSerializer();
                var batchDirectoryPath = Path.GetDirectoryName(fullPath);
                var batchFileName = Path.GetFileName(fullPath);
                using (var stream = await this.RemoteOrchestrator.BatchStorage.ReadBatchPartAsync(batchDirectoryPath, batchFileName).ConfigureAwait(false))
                {
                    containerSet = await serializer.DeserializeAsync<ContainerSet>(stream).ConfigureAwait(false);
                }

                // Apply converter if needed after deserialization
                if (this.clientConverter != null && containerSet.HasRows)
                {
                    foreach (var containerTable in containerSet.Tables)
                    {
                        if (containerTable.HasRows)
                        {
                            var schemaTable = BaseOrchestrator.CreateChangesTable(sScopeInfo.Schema.Tables[containerTable.TableName, containerTable.SchemaName]);

                            for (int i = 0; i < containerTable.Rows.Count; i++)
                            {
                                var row = containerTable.Rows[i];
                                // Row format is always: [state, col1, col2, ..., colN]
                                var syncRow = new SyncRow(schemaTable, row);
                                this.clientConverter.BeforeSerialize(syncRow, schemaTable);
                                // Note: row is a reference to the same array in SyncRow, so modifications are reflected
                            }
                        }
                    }
                }
            }
            else
            {
                // Handle traditional single-table batch file
                // Get the updatable schema for the only table contained in the batchpartinfo
                var schemaTable = BaseOrchestrator.CreateChangesTable(sScopeInfo.Schema.Tables[batchPartInfo.TableName, batchPartInfo.SchemaName]);
                var containerTable = new ContainerTable(schemaTable);
                containerSet.Tables.Add(containerTable);

                // read rows from file
                var directoryPath = Path.GetDirectoryName(fullPath);
                var fileName = Path.GetFileName(fullPath);
                await using var localSerializer = new LocalJsonSerializer(this.RemoteOrchestrator.BatchStorage, this.RemoteOrchestrator, context);
                foreach (var row in await localSerializer.GetRowsFromFileAsync(directoryPath, fileName, schemaTable))
                {
                    if (row != null && row.Length > 0 && this.clientConverter != null)
                        this.clientConverter.BeforeSerialize(row, schemaTable);

                    containerTable.Rows.Add(row.ToArray());
                }
            }

            // generate the response
            changesResponse.Changes = containerSet;
            changesResponse.BatchIndex = batchIndexRequested;
            changesResponse.BatchCount = serverBatchInfo.BatchPartsInfo.Count;
            changesResponse.IsLastBatch = batchPartInfo.IsLastBatch;
            changesResponse.RemoteClientTimestamp = remoteClientTimestamp;
            changesResponse.ServerStep = batchPartInfo.IsLastBatch ? HttpStep.GetMoreChanges : HttpStep.GetChangesInProgress;

            return changesResponse;
        }

        /// <summary>
        /// Send an end download changes message - combines batch data retrieval with cleanup for optimized clients.
        /// </summary>
        protected internal virtual async Task<HttpMessageSendChangesResponse> SendEndDownloadChangesAsync(
            HttpContext httpContext, HttpMessageGetMoreChangesRequest httpMessage,
            SessionCache sessionCache, IProgress<ProgressArgs> progress = null, CancellationToken cancellationToken = default)
        {
            // Check if client is using optimized protocol
            var isOptimizedClient = TryGetHeaderValue(httpContext.Request.Headers, "dotmim-sync-optimized", out var optimizedValue) &&
                                   bool.TryParse(optimizedValue, out var isOptimized) && isOptimized;

            HttpMessageSendChangesResponse response;

            if (isOptimizedClient)
            {
                // New optimized protocol: return batch data + cleanup
                response = await this.GetChangesResponseAsync(httpContext, httpMessage.SyncContext, sessionCache.RemoteClientTimestamp,
                    sessionCache.ServerBatchInfo, sessionCache.ClientChangesApplied,
                    sessionCache.ServerChangesSelected, httpMessage.BatchIndexRequested);
            }
            else
            {
                // Legacy protocol: return empty response (cleanup only)
                // Get server scope info to provide ServerScopeId
                ScopeInfo sScopeInfo;
                (_, sScopeInfo, _) = await this.RemoteOrchestrator.InternalEnsureScopeInfoAsync(
                    httpMessage.SyncContext, this.Setup, false, default, default, progress, cancellationToken).ConfigureAwait(false);

                response = new HttpMessageSendChangesResponse(httpMessage.SyncContext)
                {
                    ServerScopeId = sScopeInfo.Id,
                };
            }

            // Perform cleanup logic for both protocols
            var batchPartInfo = sessionCache.ServerBatchInfo?.BatchPartsInfo?.FirstOrDefault(d => d.Index == httpMessage.BatchIndexRequested);

            // we can try to clean if batchinfo is empty or if we found the last one AND we have the option.
            var cleanFolder = (batchPartInfo == null || batchPartInfo.IsLastBatch) && this.Options.CleanFolder;

            if (cleanFolder)
                cleanFolder = await this.RemoteOrchestrator.InternalCanCleanFolderAsync(httpMessage.SyncContext.ScopeName, httpMessage.SyncContext.Parameters, sessionCache.ServerBatchInfo, default, cancellationToken).ConfigureAwait(false);

            if (cleanFolder)
                await sessionCache.ServerBatchInfo.TryRemoveDirectoryAsync().ConfigureAwait(false);

            // Update the response to indicate this was the end download step
            response.ServerStep = HttpStep.SendEndDownloadChanges;

            // Handle automatic session end for optimized clients
            if(isOptimizedClient && response.IsLastBatch)
                await this.AutomaticallyEndSession(httpContext, httpMessage.SyncContext, sessionCache, progress, cancellationToken);

            return response;
        }

        private static async Task UpgradeAsync(RemoteOrchestrator remoteOrchestrator)
        {
            if (checkUpgradeDone)
                return;

            var context = new SyncContext(Guid.NewGuid(), SyncOptions.DefaultScopeName);
            var needToUpgrade = await remoteOrchestrator.NeedsToUpgradeAsync(context).ConfigureAwait(false);

            if (needToUpgrade)
                await remoteOrchestrator.InternalUpgradeAsync(context).ConfigureAwait(false);

            checkUpgradeDone = true;
        }
    }

}