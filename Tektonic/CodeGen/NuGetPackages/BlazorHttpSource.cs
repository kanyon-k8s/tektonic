using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Tektonic.CodeGen.NuGetPackages
{
    public class BlazorHttpSource : HttpSource
    {
        private readonly Func<Task<HttpHandlerResource>> _messageHandlerFactory;
        private readonly Uri _sourceUri;
        private HttpClient _httpClient;
        private readonly PackageSource _packageSource;
        private readonly IThrottle _throttle;
        private bool _disposed = false;

        // Only one thread may re-create the http client at a time.
        private readonly SemaphoreSlim _httpClientLock = new SemaphoreSlim(1, 1);

        /// <summary>The retry handler to use for all HTTP requests.</summary>
        /// <summary>This API is intended only for testing purposes and should not be used in product code.</summary>
        public new IHttpRetryHandler RetryHandler { get; set; } = new HttpRetryHandler();

        public new string PackageSource => _packageSource.Source;

        public BlazorHttpSource(
            PackageSource packageSource,
            Func<Task<HttpHandlerResource>> messageHandlerFactory,
            IThrottle throttle) : base(packageSource, messageHandlerFactory, throttle)
        {
            if (packageSource == null)
            {
                throw new ArgumentNullException(nameof(packageSource));
            }

            if (messageHandlerFactory == null)
            {
                throw new ArgumentNullException(nameof(messageHandlerFactory));
            }

            if (throttle == null)
            {
                throw new ArgumentNullException(nameof(throttle));
            }

            _packageSource = packageSource;
            _sourceUri = packageSource.SourceUri;
            _messageHandlerFactory = messageHandlerFactory;
            _throttle = throttle;
        }

        /// <summary>
        /// Get request.
        /// </summary>
        public override async Task<T> GetAsync<T>(
            HttpSourceCachedRequest request,
            Func<HttpSourceResult, Task<T>> processAsync,
            ILogger log,
            CancellationToken token)
        {

            Func<HttpRequestMessage> requestFactory = () =>
            {
                var requestMessage = HttpRequestMessageFactory.Create(HttpMethod.Get, request.Uri, log);

                foreach (var acceptHeaderValue in request.AcceptHeaderValues)
                {
                    requestMessage.Headers.Accept.Add(acceptHeaderValue);
                }

                return requestMessage;
            };

            Func<Task<ThrottledResponse>> throttledResponseFactory = () => GetThrottledResponse(
                requestFactory,
                request.RequestTimeout,
                request.DownloadTimeout,
                request.MaxTries,
                request.IsRetry,
                request.IsLastAttempt,
                request.CacheContext.SourceCacheContext.SessionId,
                log,
                token);

            using (var throttledResponse = await throttledResponseFactory())
            {
                if (request.IgnoreNotFounds && throttledResponse.Response.StatusCode == HttpStatusCode.NotFound)
                {
                    var httpSourceResult = new HttpSourceResult(HttpSourceResultStatus.NotFound);

                    return await processAsync(httpSourceResult);
                }

                if (throttledResponse.Response.StatusCode == HttpStatusCode.NoContent)
                {
                    // Ignore reading and caching the empty stream.
                    var httpSourceResult = new HttpSourceResult(HttpSourceResultStatus.NoContent);

                    return await processAsync(httpSourceResult);
                }

                throttledResponse.Response.EnsureSuccessStatusCode();

                {
                    // Note that we do not execute the content validator on the response stream when skipping
                    // the cache. We cannot seek on the network stream and it is not valuable to download the
                    // content twice just to validate the first time (considering that the second download could
                    // be different from the first thus rendering the first validation meaningless).
                    using (var stream = await throttledResponse.Response.Content.ReadAsStreamAsync())
                    using (var httpSourceResult = new HttpSourceResult(
                        HttpSourceResultStatus.OpenedFromNetwork,
                        cacheFileName: null,
                        stream: stream))
                    {
                        return await processAsync(httpSourceResult);
                    }
                }
            }
        }

        public new Task<T> ProcessStreamAsync<T>(
            HttpSourceRequest request,
            Func<Stream, Task<T>> processAsync,
            ILogger log,
            CancellationToken token)
        {
            return ProcessStreamAsync<T>(request, processAsync, cacheContext: null, log: log, token: token);
        }

        internal async Task<T> ProcessHttpStreamAsync<T>(
            HttpSourceRequest request,
            Func<HttpResponseMessage, Task<T>> processAsync,
            ILogger log,
            CancellationToken token)
        {
            return await ProcessResponseAsync(
                request,
                async response =>
                {
                    if ((request.IgnoreNotFounds && response.StatusCode == HttpStatusCode.NotFound) ||
                         response.StatusCode == HttpStatusCode.NoContent)
                    {
                        return await processAsync(null);
                    }

                    response.EnsureSuccessStatusCode();

                    return await processAsync(response);
                },
                cacheContext: null,
                log,
                token);
        }

        public new async Task<T> ProcessStreamAsync<T>(
            HttpSourceRequest request,
            Func<Stream, Task<T>> processAsync,
            SourceCacheContext cacheContext,
            ILogger log,
            CancellationToken token)
        {
            return await ProcessResponseAsync(
                request,
                async response =>
                {
                    if ((request.IgnoreNotFounds && response.StatusCode == HttpStatusCode.NotFound) ||
                         response.StatusCode == HttpStatusCode.NoContent)
                    {
                        return await processAsync(null);
                    }

                    response.EnsureSuccessStatusCode();

                    var networkStream = await response.Content.ReadAsStreamAsync();
                    return await processAsync(networkStream);
                },
                cacheContext,
                log,
                token);
        }

        public new Task<T> ProcessResponseAsync<T>(
            HttpSourceRequest request,
            Func<HttpResponseMessage, Task<T>> processAsync,
            ILogger log,
            CancellationToken token)
        {
            return ProcessResponseAsync(request, processAsync, cacheContext: null, log: log, token: token);
        }

        public new async Task<T> ProcessResponseAsync<T>(
            HttpSourceRequest request,
            Func<HttpResponseMessage, Task<T>> processAsync,
            SourceCacheContext cacheContext,
            ILogger log,
            CancellationToken token)
        {
            // Generate a new session id if no cache context was provided.
            var sessionId = cacheContext?.SessionId ?? Guid.NewGuid();

            Task<ThrottledResponse> throttledResponseFactory() => GetThrottledResponse(
                request.RequestFactory,
                request.RequestTimeout,
                request.DownloadTimeout,
                request.MaxTries,
                request.IsRetry,
                request.IsLastAttempt,
                sessionId,
                log,
                token);

            using (var throttledResponse = await throttledResponseFactory())
            {
                return await processAsync(throttledResponse.Response);
            }
        }

        public new async Task<JObject> GetJObjectAsync(HttpSourceRequest request, ILogger log, CancellationToken token)
        {
            return await ProcessStreamAsync<JObject>(
                request,
                processAsync: async stream =>
                {
                    if (stream == null)
                    {
                        return await Task.FromResult<JObject>(null);
                    }

                    return JObject.Load(new JsonTextReader(new StreamReader(stream)));
                },
                log: log,
                token: token);
        }

        private async Task<ThrottledResponse> GetThrottledResponse(
            Func<HttpRequestMessage> requestFactory,
            TimeSpan requestTimeout,
            TimeSpan downloadTimeout,
            int maxTries,
            bool isRetry,
            bool isLastAttempt,
            Guid sessionId,
            ILogger log,
            CancellationToken cancellationToken)
        {
            await EnsureHttpClientAsync();

            // Build the retriable request.
            var request = new HttpRetryHandlerRequest(_httpClient, requestFactory)
            {
                RequestTimeout = requestTimeout,
                DownloadTimeout = downloadTimeout,
                MaxTries = maxTries,
                IsRetry = isRetry,
                IsLastAttempt = isLastAttempt
            };

            // Add X-NuGet-Session-Id to all outgoing requests. This allows feeds to track nuget operations.
            request.AddHeaders.Add(new KeyValuePair<string, IEnumerable<string>>(ProtocolConstants.SessionId, new[] { sessionId.ToString() }));

            // Acquire the semaphore.
            await _throttle.WaitAsync();

            HttpResponseMessage response;
            try
            {
                response = await RetryHandler.SendAsync(request, _packageSource.SourceUri.OriginalString, log, cancellationToken);
            }
            catch
            {
                // If the request fails, release the semaphore. If no exception is thrown by
                // SendAsync, then the semaphore is released when the HTTP response message is
                // disposed.
                _throttle.Release();
                throw;
            }

            return new ThrottledResponse(_throttle, response);
        }

        private async Task EnsureHttpClientAsync()
        {
            // Create the http client on the first call
            if (_httpClient == null)
            {
                await _httpClientLock.WaitAsync();
                try
                {
                    // Double check
                    if (_httpClient == null)
                    {
                        _httpClient = await CreateHttpClientAsync();
                    }
                }
                finally
                {
                    _httpClientLock.Release();
                }
            }
        }

        private async Task<HttpClient> CreateHttpClientAsync()
        {
            var httpHandler = await _messageHandlerFactory();
            var httpClient = new HttpClient(httpHandler.MessageHandler)
            {
                Timeout = Timeout.InfiniteTimeSpan
            };

            // Set user agent
            UserAgent.SetUserAgent(httpClient);

            // Set accept-language header
            string acceptLanguage = CultureInfo.CurrentUICulture.ToString();
            if (!string.IsNullOrEmpty(acceptLanguage))
            {
                httpClient.DefaultRequestHeaders.AcceptLanguage.ParseAdd(acceptLanguage);
            }

            return httpClient;
        }

        public new static HttpSource Create(SourceRepository source)
        {
            return Create(source, NullThrottle.Instance);
        }

        public new static HttpSource Create(SourceRepository source, IThrottle throttle)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (throttle == null)
            {
                throw new ArgumentNullException(nameof(throttle));
            }

            Func<Task<HttpHandlerResource>> factory = () => source.GetResourceAsync<HttpHandlerResource>();

            return new BlazorHttpSource(source.PackageSource, factory, throttle);
        }

        public new void Dispose()
        {
            base.Dispose();
            DisposeMe(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void DisposeMe(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            if (disposing)
            {
                if (_httpClient != null)
                {
                    _httpClient.Dispose();
                }

                _httpClientLock.Dispose();
            }

            _disposed = true;
        }

        private class ThrottledResponse : IDisposable
        {
            private IThrottle _throttle;

            public ThrottledResponse(IThrottle throttle, HttpResponseMessage response)
            {
                if (throttle == null)
                {
                    throw new ArgumentNullException(nameof(throttle));
                }

                if (response == null)
                {
                    throw new ArgumentNullException(nameof(response));
                }

                _throttle = throttle;
                Response = response;
            }

            public HttpResponseMessage Response { get; }

            public void Dispose()
            {
                try
                {
                    Response.Dispose();
                }
                finally
                {
                    Interlocked.Exchange(ref _throttle, null)?.Release();
                }
            }
        }
    }
}
