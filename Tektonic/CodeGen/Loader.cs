using Microsoft.Extensions.DependencyModel;
using NuGet;
using NuGet.Configuration;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Packaging.Signing;
using NuGet.Protocol.Core.Types;
using NuGet.Resolver;
using NuGet.Versioning;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using ILogger = NuGet.Common.ILogger;

namespace Tektonic.CodeGen
{
    public class Loader
    {
        /// <summary>
        /// Represents the configuration for a single extension to install.
        /// </summary>
        public class ExtensionConfiguration
        {
            public string Package { get; set; }
            public string Version { get; set; }
            public bool PreRelease { get; set; }
        }

        public async Task LoadExtension(string packageName, bool allowPrerelease)
        {
            // Define a source provider, with nuget, plus my own feed.
            var sourceProvider = new NuGet.Configuration.PackageSourceProvider(NuGet.Configuration.NullSettings.Instance, new[]
            {
                new NuGet.Configuration.PackageSource("https://api.nuget.org/v3/index.json") // TODO: Parameterize
            });

            // Establish the source repository provider; the available providers come from our custom settings.
            var sourceRepositoryProvider = new SourceRepositoryProvider(sourceProvider, Repository.Provider.GetCoreV3());

            // Get the list of repositories.
            var repositories = sourceRepositoryProvider.GetRepositories();

            // Disposable source cache.
            using var sourceCacheContext = new SourceCacheContext();

            // You should use an actual logger here, this is a NuGet ILogger instance.
            var logger = new NuGet.Common.NullLogger();

            // My extension configuration:
            var extensions = new[]
            {
                new ExtensionConfiguration
                {
                    Package = packageName,
                    PreRelease = allowPrerelease // Allow pre-release versions.
                }
            };

            // Replace this with a proper cancellation token.
            var cancellationToken = CancellationToken.None;

            // The framework we're using.
            var targetFramework = NuGetFramework.ParseFolder("net5.0");
            var allPackages = new HashSet<SourcePackageDependencyInfo>();

            var dependencyContext = DependencyContext.Default;

            foreach (var ext in extensions)
            {
                var packageIdentity = await GetPackageIdentity(ext, sourceCacheContext, logger, repositories, cancellationToken);

                if (packageIdentity is null)
                {
                    throw new InvalidOperationException($"Cannot find package {ext.Package}.");
                }

                await GetPackageDependencies(packageIdentity, sourceCacheContext, targetFramework, logger, repositories, dependencyContext, allPackages, cancellationToken);
            }

            var packagesToInstall = GetPackagesToInstall(sourceRepositoryProvider, logger, extensions, allPackages);

            // Where do we want to install our packages?
            // BLAZOR: This needs to be handled in-memory
            //var packageDirectory = Path.Combine(Environment.CurrentDirectory, ".extensions");
            //var nugetSettings = NuGet.Configuration.Settings.LoadDefaultSettings(packageDirectory);

            await InstallPackages(sourceCacheContext, logger, packagesToInstall, packageDirectory, nugetSettings, cancellationToken);
        }

        private async Task InstallPackages(SourceCacheContext sourceCacheContext, ILogger logger,
                                           IEnumerable<SourcePackageDependencyInfo> packagesToInstall, string rootPackagesDirectory,
                                           NuGet.Configuration.ISettings nugetSettings, CancellationToken cancellationToken)
        {
            var packagePathResolver = new PackagePathResolver(rootPackagesDirectory, true);
            var packageExtractionContext = new PackageExtractionContext(
                PackageSaveMode.Defaultv3,
                XmlDocFileSaveMode.Skip,
                ClientPolicyContext.GetClientPolicy(nugetSettings, logger),
                logger);

            foreach (var package in packagesToInstall)
            {
                var downloadResource = await package.Source.GetResourceAsync<DownloadResource>(cancellationToken);

                // Download the package (might come from the shared package cache).
                var downloadResult = await downloadResource.GetDownloadResourceResultAsync(
                    package,
                    new PackageDownloadContext(sourceCacheContext),
                    SettingsUtility.GetGlobalPackagesFolder(nugetSettings),
                    logger,
                    cancellationToken);

                // Extract the package into the target directory.
                await PackageExtractor.ExtractPackageAsync(
                    downloadResult.PackageSource,
                    downloadResult.PackageStream,
                    packagePathResolver,
                    packageExtractionContext,
                    cancellationToken);
            }
        }

        private IEnumerable<SourcePackageDependencyInfo> GetPackagesToInstall(SourceRepositoryProvider sourceRepositoryProvider,
                                                                              ILogger logger, IEnumerable<ExtensionConfiguration> extensions,
                                                                              HashSet<SourcePackageDependencyInfo> allPackages)
        {
            // Create a package resolver context (this is used to help figure out which actual package versions to install).
            var resolverContext = new PackageResolverContext(
                   DependencyBehavior.Lowest,
                   extensions.Select(x => x.Package),
                   Enumerable.Empty<string>(),
                   Enumerable.Empty<NuGet.Packaging.PackageReference>(),
                   Enumerable.Empty<PackageIdentity>(),
                   allPackages,
                   sourceRepositoryProvider.GetRepositories().Select(s => s.PackageSource),
                   logger);

            var resolver = new PackageResolver();

            // Work out the actual set of packages to install.
            var packagesToInstall = resolver.Resolve(resolverContext, CancellationToken.None)
                                            .Select(p => allPackages.Single(x => PackageIdentityComparer.Default.Equals(x, p)));
            return packagesToInstall;
        }

        private async Task<PackageIdentity> GetPackageIdentity(
          ExtensionConfiguration extConfig, SourceCacheContext cache, ILogger nugetLogger,
          IEnumerable<SourceRepository> repositories, CancellationToken cancelToken)
        {
            // Go through each repository.
            // If a repository contains only pre-release packages (e.g. AutoStep CI), and 
            // the configuration doesn't permit pre-release versions,
            // the search will look at other ones (e.g. NuGet).
            foreach (var sourceRepository in repositories)
            {
                // Get a 'resource' from the repository.
                var findPackageResource = await sourceRepository.GetResourceAsync<FindPackageByIdResource>();

                // Get the list of all available versions of the package in the repository.
                var allVersions = await findPackageResource.GetAllVersionsAsync(extConfig.Package, cache, nugetLogger, cancelToken);

                NuGetVersion selected;

                // Have we specified a version range?
                if (extConfig.Version != null)
                {
                    if (!VersionRange.TryParse(extConfig.Version, out var range))
                    {
                        throw new InvalidOperationException("Invalid version range provided.");
                    }

                    // Find the best package version match for the range.
                    // Consider pre-release versions, but only if the extension is configured to use them.
                    var bestVersion = range.FindBestMatch(allVersions.Where(v => extConfig.PreRelease || !v.IsPrerelease));

                    selected = bestVersion;
                }
                else
                {
                    // No version; choose the latest, allow pre-release if configured.
                    selected = allVersions.LastOrDefault(v => v.IsPrerelease == extConfig.PreRelease);
                }

                if (selected is object)
                {
                    return new PackageIdentity(extConfig.Package, selected);
                }
            }

            return null;
        }

        /// <summary>
        /// Searches the package dependency graph for the chain of all packages to install.
        /// </summary>
        private async Task GetPackageDependencies(PackageIdentity package, SourceCacheContext cacheContext, NuGetFramework framework,
                                                  ILogger logger, IEnumerable<SourceRepository> repositories, DependencyContext hostDependencies,
                                                  ISet<SourcePackageDependencyInfo> availablePackages, CancellationToken cancelToken)
        {
            // Don't recurse over a package we've already seen.
            if (availablePackages.Contains(package))
            {
                return;
            }

            foreach (var sourceRepository in repositories)
            {
                // Get the dependency info for the package.
                var dependencyInfoResource = await sourceRepository.GetResourceAsync<DependencyInfoResource>();
                var dependencyInfo = await dependencyInfoResource.ResolvePackage(
                    package,
                    framework,
                    cacheContext,
                    logger,
                    cancelToken);

                // No info for the package in this repository.
                if (dependencyInfo == null)
                {
                    continue;
                }


                // Filter the dependency info.
                // Don't bring in any dependencies that are provided by the host.
                var actualSourceDep = new SourcePackageDependencyInfo(
                    dependencyInfo.Id,
                    dependencyInfo.Version,
                    dependencyInfo.Dependencies.Where(dep => !DependencySuppliedByHost(hostDependencies, dep)),
                    dependencyInfo.Listed,
                    dependencyInfo.Source);

                availablePackages.Add(actualSourceDep);

                // Recurse through each package.
                foreach (var dependency in actualSourceDep.Dependencies)
                {
                    await GetPackageDependencies(
                        new PackageIdentity(dependency.Id, dependency.VersionRange.MinVersion),
                        cacheContext,
                        framework,
                        logger,
                        repositories,
                        hostDependencies,
                        availablePackages,
                        cancelToken);
                }

                break;
            }
        }

        private bool DependencySuppliedByHost(DependencyContext hostDependencies, NuGet.Packaging.Core.PackageDependency dep)
        {
            if (RuntimeProvidedPackages.IsPackageProvidedByRuntime(dep.Id))
            {
                return true;
            }

            // See if a runtime library with the same ID as the package is available in the host's runtime libraries.
            var runtimeLib = hostDependencies.RuntimeLibraries.FirstOrDefault(r => r.Name == dep.Id);

            if (runtimeLib is object)
            {
                // What version of the library is the host using?
                var parsedLibVersion = NuGetVersion.Parse(runtimeLib.Version);

                if (parsedLibVersion.IsPrerelease)
                {
                    // Always use pre-release versions from the host, otherwise it becomes
                    // a nightmare to develop across multiple active versions.
                    return true;
                }
                else
                {
                    // Does the host version satisfy the version range of the requested package?
                    // If so, we can provide it; otherwise, we cannot.
                    return dep.VersionRange.Satisfies(parsedLibVersion);
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Contains a pre-determined list of NuGet packages that are provided by the run-time, and
    /// therefore should not be restored from an extensions dependency list.
    /// </summary>
    internal static class RuntimeProvidedPackages
    {
        /// <summary>
        /// Checks whether the set of known runtime packages contains the given package ID.
        /// </summary>
        /// <param name="packageId">The package ID.</param>
        /// <returns>True if the package is provided by the framework, otherwise false.</returns>
        public static bool IsPackageProvidedByRuntime(string packageId)
        {
            return ProvidedPackages.Contains(packageId);
        }

        /// <summary>
        /// This list comes from the package overrides for the .NET SDK,
        /// at https://github.com/dotnet/sdk/blob/v3.1.201/src/Tasks/Common/targets/Microsoft.NET.DefaultPackageConflictOverrides.targets.
        /// If the executing binaries ever change to a newer version, this project must update as well, and refresh this list.
        /// </summary>
        private static readonly ISet<string> ProvidedPackages = new HashSet<string>
        {
            "Microsoft.CSharp",
            "Microsoft.Win32.Primitives",
            "Microsoft.Win32.Registry",
            "runtime.debian.8-x64.runtime.native.System.Security.Cryptography.OpenSsl",
            "runtime.fedora.23-x64.runtime.native.System.Security.Cryptography.OpenSsl",
            "runtime.fedora.24-x64.runtime.native.System.Security.Cryptography.OpenSsl",
            "runtime.opensuse.13.2-x64.runtime.native.System.Security.Cryptography.OpenSsl",
            "runtime.opensuse.42.1-x64.runtime.native.System.Security.Cryptography.OpenSsl",
            "runtime.osx.10.10-x64.runtime.native.System.Security.Cryptography.Apple",
            "runtime.osx.10.10-x64.runtime.native.System.Security.Cryptography.OpenSsl",
            "runtime.rhel.7-x64.runtime.native.System.Security.Cryptography.OpenSsl",
            "runtime.ubuntu.14.04-x64.runtime.native.System.Security.Cryptography.OpenSsl",
            "runtime.ubuntu.16.04-x64.runtime.native.System.Security.Cryptography.OpenSsl",
            "runtime.ubuntu.16.10-x64.runtime.native.System.Security.Cryptography.OpenSsl",
            "System.AppContext",
            "System.Buffers",
            "System.Collections",
            "System.Collections.Concurrent",
            "System.Collections.Immutable",
            "System.Collections.NonGeneric",
            "System.Collections.Specialized",
            "System.ComponentModel",
            "System.ComponentModel.EventBasedAsync",
            "System.ComponentModel.Primitives",
            "System.ComponentModel.TypeConverter",
            "System.Console",
            "System.Data.Common",
            "System.Diagnostics.Contracts",
            "System.Diagnostics.Debug",
            "System.Diagnostics.DiagnosticSource",
            "System.Diagnostics.FileVersionInfo",
            "System.Diagnostics.Process",
            "System.Diagnostics.StackTrace",
            "System.Diagnostics.TextWriterTraceListener",
            "System.Diagnostics.Tools",
            "System.Diagnostics.TraceSource",
            "System.Diagnostics.Tracing",
            "System.Dynamic.Runtime",
            "System.Globalization",
            "System.Globalization.Calendars",
            "System.Globalization.Extensions",
            "System.IO",
            "System.IO.Compression",
            "System.IO.Compression.ZipFile",
            "System.IO.FileSystem",
            "System.IO.FileSystem.AccessControl",
            "System.IO.FileSystem.DriveInfo",
            "System.IO.FileSystem.Primitives",
            "System.IO.FileSystem.Watcher",
            "System.IO.IsolatedStorage",
            "System.IO.MemoryMappedFiles",
            "System.IO.Pipes",
            "System.IO.UnmanagedMemoryStream",
            "System.Linq",
            "System.Linq.Expressions",
            "System.Linq.Queryable",
            "System.Net.Http",
            "System.Net.NameResolution",
            "System.Net.Primitives",
            "System.Net.Requests",
            "System.Net.Security",
            "System.Net.Sockets",
            "System.Net.WebHeaderCollection",
            "System.ObjectModel",
            "System.Private.DataContractSerialization",
            "System.Reflection",
            "System.Reflection.Emit",
            "System.Reflection.Emit.ILGeneration",
            "System.Reflection.Emit.Lightweight",
            "System.Reflection.Extensions",
            "System.Reflection.Metadata",
            "System.Reflection.Primitives",
            "System.Reflection.TypeExtensions",
            "System.Resources.ResourceManager",
            "System.Runtime",
            "System.Runtime.Extensions",
            "System.Runtime.Handles",
            "System.Runtime.InteropServices",
            "System.Runtime.InteropServices.RuntimeInformation",
            "System.Runtime.Loader",
            "System.Runtime.Numerics",
            "System.Runtime.Serialization.Formatters",
            "System.Runtime.Serialization.Json",
            "System.Runtime.Serialization.Primitives",
            "System.Security.AccessControl",
            "System.Security.Claims",
            "System.Security.Cryptography.Algorithms",
            "System.Security.Cryptography.Cng",
            "System.Security.Cryptography.Csp",
            "System.Security.Cryptography.Encoding",
            "System.Security.Cryptography.OpenSsl",
            "System.Security.Cryptography.Primitives",
            "System.Security.Cryptography.X509Certificates",
            "System.Security.Cryptography.Xml",
            "System.Security.Principal",
            "System.Security.Principal.Windows",
            "System.Text.Encoding",
            "System.Text.Encoding.Extensions",
            "System.Text.RegularExpressions",
            "System.Threading",
            "System.Threading.Overlapped",
            "System.Threading.Tasks",
            "System.Threading.Tasks.Extensions",
            "System.Threading.Tasks.Parallel",
            "System.Threading.Thread",
            "System.Threading.ThreadPool",
            "System.Threading.Timer",
            "System.ValueTuple",
            "System.Xml.ReaderWriter",
            "System.Xml.XDocument",
            "System.Xml.XmlDocument",
            "System.Xml.XmlSerializer",
            "System.Xml.XPath",
            "System.Xml.XPath.XDocument",
        };
    }
}
