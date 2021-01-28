using NuGet.Common;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Protocol.Core.Types;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Tektonic.CodeGen.NuGetPackages
{
    public class PackageInstallationTraversal
    {
        public PackageInstallationTraversal(NuGetFramework supportedFramework)
        {
            frameworkReducer = new FrameworkReducer();
            SupportedFramework = supportedFramework;
            InstalledAssemblies = new List<Assembly>();
        }

        private HashSet<string> installedPackages = new HashSet<string>();
        private FrameworkReducer frameworkReducer;

        public NuGetFramework SupportedFramework { get; }

        public List<Assembly> InstalledAssemblies { get; }

        public async Task InstallPackages(SourceCacheContext sourceCacheContext, ILogger logger,
                                   IEnumerable<SourcePackageDependencyInfo> packagesToInstall, CancellationToken cancellationToken)
        {
            foreach (var package in packagesToInstall)
            {
                if (installedPackages.Contains(package.Id)) continue;

                var resource = await package.Source.GetResourceAsync<FindPackageByIdResource>(cancellationToken);

                using var downloadPackage = new MemoryStream();
                await resource.CopyNupkgToStreamAsync(package.Id, package.Version, downloadPackage, sourceCacheContext, logger, cancellationToken);
                var reader = new PackageArchiveReader(downloadPackage);

                var frameworks = reader.GetSupportedFrameworks();
                var candidateFramework = frameworkReducer.GetNearest(SupportedFramework, frameworks);
                if (candidateFramework == null) throw new UnsupportedLibraryException(package.Id);

                if (package.Dependencies.Any())
                {
                    foreach (var dependency in package.Dependencies)
                    {
                        var actualDependency = packagesToInstall.Where(spdi => spdi.Id == dependency.Id);
                        await InstallPackages(sourceCacheContext, logger, actualDependency, cancellationToken);
                    }
                }

                var libGroup = reader.GetReferenceItems().First(fsg => fsg.TargetFramework == candidateFramework);
                foreach (var file in libGroup.Items.Where(i => i.EndsWith(".dll")))
                {
                    var lib = reader.GetStream(file);
                    using var ms = new MemoryStream();
                    await lib.CopyToAsync(ms);
                    InstalledAssemblies.Add(Assembly.Load(ms.ToArray()));
                }

                installedPackages.Add(package.Id);
            }
        }
    }
}
