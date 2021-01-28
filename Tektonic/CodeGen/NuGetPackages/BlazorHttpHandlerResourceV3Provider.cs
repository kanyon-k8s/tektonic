using NuGet.Configuration;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Tektonic.CodeGen.NuGetPackages
{
    // This was adapted from the NuGet.Client source available at https://github.com/NuGet/NuGet.Client/blob/788bc01a1b063a37841cdd6d035feb320e90e475/src/NuGet.Core/NuGet.Protocol/HttpSource/HttpHandlerResourceV3Provider.cs to allow in-browser execution
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
