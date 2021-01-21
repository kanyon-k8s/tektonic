using NuGet.Configuration;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Tektonic.CodeGen.Packages
{
    public class BlazorHttpHandlerResourceV3Provider : ResourceProvider
    {
        public BlazorHttpHandlerResourceV3Provider()
            : base(typeof(HttpHandlerResource),
                  nameof(BlazorHttpHandlerResourceV3Provider),
                  NuGetResourceProviderPositions.Last)
        {
        }

        public override Task<Tuple<bool, INuGetResource>> TryCreate(SourceRepository source, CancellationToken token)
        {
            Debug.Assert(source.PackageSource.IsHttp, "HTTP handler requested for a non-http source.");

            HttpHandlerResourceV3 curResource = null;

            if (source.PackageSource.IsHttp)
            {
                curResource = CreateResource(source.PackageSource);
            }

            return Task.FromResult(new Tuple<bool, INuGetResource>(curResource != null, curResource));
        }

        private static HttpHandlerResourceV3 CreateResource(PackageSource packageSource)
        {
            var sourceUri = packageSource.SourceUri;

            // replace the handler with the proxy aware handler
            var clientHandler = new HttpClientHandler();

            // HTTP handler pipeline can be injected here, around the client handler
            HttpMessageHandler messageHandler = new ServerWarningLogHandler(clientHandler);

            var resource = new HttpHandlerResourceV3(clientHandler, messageHandler);

            return resource;
        }
    }
}
