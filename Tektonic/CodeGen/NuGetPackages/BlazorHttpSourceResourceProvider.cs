using NuGet.Configuration;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Tektonic.CodeGen.NuGetPackages
{
    // This was adapted from the NuGet.Client source available at https://github.com/NuGet/NuGet.Client/blob/c995174aaea8d55e1504e9afba83696922497712/src/NuGet.Core/NuGet.Protocol/HttpSource/HttpSourceResourceProvider.cs to allow in-browser execution
    public class BlazorHttpSourceResourceProvider : ResourceProvider
    {
        // Only one HttpSource per source should exist. This is to reduce the number of TCP connections.
        private readonly ConcurrentDictionary<PackageSource, HttpSourceResource> _cache
            = new ConcurrentDictionary<PackageSource, HttpSourceResource>();

        /// <summary>
        /// The throttle to apply to all <see cref="HttpSource"/> HTTP requests.
        /// </summary>
        public static IThrottle Throttle { get; set; }

        public BlazorHttpSourceResourceProvider()
            : base(typeof(HttpSourceResource),
                  nameof(HttpSourceResource),
                  NuGetResourceProviderPositions.Last)
        {
        }

        public override Task<Tuple<bool, INuGetResource>> TryCreate(SourceRepository source, CancellationToken token)
        {
            Debug.Assert(source.PackageSource.IsHttp, "HTTP source requested for a non-http source.");

            HttpSourceResource curResource = null;

            if (source.PackageSource.IsHttp)
            {
                IThrottle throttle = NullThrottle.Instance;

                if (Throttle != null)
                {
                    throttle = Throttle;
                }
                else if (source.PackageSource.MaxHttpRequestsPerSource > 0)
                {
                    throttle = SemaphoreSlimThrottle.CreateSemaphoreThrottle(source.PackageSource.MaxHttpRequestsPerSource);
                }

                curResource = _cache.GetOrAdd(
                    source.PackageSource,
                    packageSource => new HttpSourceResource(BlazorHttpSource.Create(source, throttle)));
            }

            return Task.FromResult(new Tuple<bool, INuGetResource>(curResource != null, curResource));
        }
    }
}
