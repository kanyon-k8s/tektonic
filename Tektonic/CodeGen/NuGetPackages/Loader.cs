﻿using Microsoft.Extensions.DependencyModel;
using NuGet;
using NuGet.Configuration;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Packaging.Signing;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Protocol.LocalRepositories;
using NuGet.Resolver;
using NuGet.Versioning;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Tektonic.CodeGen.NuGetPackages;
using ILogger = NuGet.Common.ILogger;

namespace Tektonic.CodeGen.Packages
{
    public class Loader
    {
        public async Task<IEnumerable<Assembly>> LoadPackage(NuGetPackage package)
        {
            // Define a source provider, with nuget, plus my own feed.
            var sourceProvider = new PackageSourceProvider(NullSettings.Instance, new[]
            {
                new PackageSource("https://api.nuget.org/v3/index.json") // TODO: Parameterize
            });

            List<Lazy<INuGetResourceProvider>> providers = new List<Lazy<INuGetResourceProvider>>();
            providers.AddRange(GetCoreV3());

            // Establish the source repository provider; the available providers come from our custom settings.
            var sourceRepositoryProvider = new SourceRepositoryProvider(sourceProvider, providers);

            // Get the list of repositories.
            var repositories = sourceRepositoryProvider.GetRepositories();

            using var sourceCacheContext = new NullSourceCacheContext();
            sourceCacheContext.DirectDownload = true;

            var logger = NuGet.Common.NullLogger.Instance;

            // Replace this with a proper cancellation token.
            var cancellationToken = CancellationToken.None;

            // The framework we're using.
            var targetFramework = NuGetFramework.ParseFolder("net5.0");
            var allPackages = new HashSet<SourcePackageDependencyInfo>();

            var dependencyContext = DependencyContext.Load(typeof(Loader).Assembly);

            var packageIdentity = await GetPackageIdentity(package, sourceCacheContext, logger, repositories, cancellationToken);

            if (packageIdentity is null)
            {
                throw new InvalidOperationException($"Cannot find package {package.Package}.");
            }

            package.Version = packageIdentity.Version.ToNormalizedString();

            await GetPackageDependencies(packageIdentity, sourceCacheContext, targetFramework, logger, repositories, dependencyContext, allPackages, cancellationToken);

            var packagesToInstall = GetPackagesToInstall(sourceRepositoryProvider, logger, new[] { package }, allPackages);

            // Where do we want to install our packages?
            // BLAZOR: This needs to be handled in-memory
            var traversal = new PackageInstallationTraversal(targetFramework);
            await traversal.InstallPackages(sourceCacheContext, logger, packagesToInstall, cancellationToken);

            return traversal.InstalledAssemblies;
        }

        private IEnumerable<SourcePackageDependencyInfo> GetPackagesToInstall(SourceRepositoryProvider sourceRepositoryProvider,
                                                                              ILogger logger, IEnumerable<NuGetPackage> packages,
                                                                              HashSet<SourcePackageDependencyInfo> allPackages)
        {
            // Create a package resolver context (this is used to help figure out which actual package versions to install).
            var resolverContext = new PackageResolverContext(
                   DependencyBehavior.Lowest,
                   packages.Select(x => x.Package),
                   Enumerable.Empty<string>(),
                   Enumerable.Empty<PackageReference>(),
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
          NuGetPackage package, SourceCacheContext cache, ILogger nugetLogger,
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
                var allVersions = await findPackageResource.GetAllVersionsAsync(package.Package, cache, nugetLogger, cancelToken);

                NuGetVersion selected;

                // Have we specified a version range?
                if (package.Version != null)
                {
                    if (!VersionRange.TryParse(package.Version, out var range))
                    {
                        throw new InvalidOperationException("Invalid version range provided.");
                    }

                    // Find the best package version match for the range.
                    // Consider pre-release versions, but only if the extension is configured to use them.
                    var bestVersion = range.FindBestMatch(allVersions.Where(v => package.AllowPrerelease || !v.IsPrerelease));

                    selected = bestVersion;
                }
                else
                {
                    // No version; choose the latest, allow pre-release if configured.
                    selected = allVersions.LastOrDefault(v => v.IsPrerelease == package.AllowPrerelease);
                }

                if (selected is object)
                {
                    return new PackageIdentity(package.Package, selected);
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

        private bool DependencySuppliedByHost(DependencyContext hostDependencies, PackageDependency dep)
        {
            if (RuntimeProvidedPackages.IsPackageProvidedByRuntime(dep.Id))
            {
                return true;
            }

            //// See if a runtime library with the same ID as the package is available in the host's runtime libraries.
            //var runtimeLib = hostDependencies.RuntimeLibraries.FirstOrDefault(r => r.Name == dep.Id);

            //if (runtimeLib is object)
            //{
            //    // What version of the library is the host using?
            //    var parsedLibVersion = NuGetVersion.Parse(runtimeLib.Version);

            //    if (parsedLibVersion.IsPrerelease)
            //    {
            //        // Always use pre-release versions from the host, otherwise it becomes
            //        // a nightmare to develop across multiple active versions.
            //        return true;
            //    }
            //    else
            //    {
            //        // Does the host version satisfy the version range of the requested package?
            //        // If so, we can provide it; otherwise, we cannot.
            //        return dep.VersionRange.Satisfies(parsedLibVersion);
            //    }
            //}

            return false;
        }

        // This is a copy of the Repository.Provider.GetCoreV3() function in the NuGet client. We are re-implementing it so that we can inject a Blazor WASM-safe HTTP pipeline in place of the HttpHandlerResourceV3Provider
        // TODO: Can this just be set before "HttpHandlerResourceV3Provider"?
        public virtual IEnumerable<Lazy<INuGetResourceProvider>> GetCoreV3()
        {
            yield return new Lazy<INuGetResourceProvider>(() => new FeedTypeResourceProvider());
            yield return new Lazy<INuGetResourceProvider>(() => new DependencyInfoResourceV3Provider());
            yield return new Lazy<INuGetResourceProvider>(() => new DownloadResourcePluginProvider());
            yield return new Lazy<INuGetResourceProvider>(() => new DownloadResourceV3Provider());
            yield return new Lazy<INuGetResourceProvider>(() => new MetadataResourceV3Provider());
#pragma warning disable CS0618 // Type or member is obsolete
            yield return new Lazy<INuGetResourceProvider>(() => new RawSearchResourceV3Provider());
#pragma warning restore CS0618 // Type or member is obsolete
            yield return new Lazy<INuGetResourceProvider>(() => new RegistrationResourceV3Provider());
            yield return new Lazy<INuGetResourceProvider>(() => new SymbolPackageUpdateResourceV3Provider());
            yield return new Lazy<INuGetResourceProvider>(() => new ReportAbuseResourceV3Provider());
            yield return new Lazy<INuGetResourceProvider>(() => new PackageDetailsUriResourceV3Provider());
            yield return new Lazy<INuGetResourceProvider>(() => new ServiceIndexResourceV3Provider());
            yield return new Lazy<INuGetResourceProvider>(() => new ODataServiceDocumentResourceV2Provider());
            yield return new Lazy<INuGetResourceProvider>(() => new BlazorHttpHandlerResourceV3Provider());
            yield return new Lazy<INuGetResourceProvider>(() => new BlazorHttpSourceResourceProvider());
            yield return new Lazy<INuGetResourceProvider>(() => new PluginFindPackageByIdResourceProvider());
            yield return new Lazy<INuGetResourceProvider>(() => new HttpFileSystemBasedFindPackageByIdResourceProvider());
            yield return new Lazy<INuGetResourceProvider>(() => new RemoteV3FindPackageByIdResourceProvider());
            yield return new Lazy<INuGetResourceProvider>(() => new RemoteV2FindPackageByIdResourceProvider());
            yield return new Lazy<INuGetResourceProvider>(() => new LocalV3FindPackageByIdResourceProvider());
            yield return new Lazy<INuGetResourceProvider>(() => new LocalV2FindPackageByIdResourceProvider());
            yield return new Lazy<INuGetResourceProvider>(() => new PackageUpdateResourceV2Provider());
            yield return new Lazy<INuGetResourceProvider>(() => new PackageUpdateResourceV3Provider());
            yield return new Lazy<INuGetResourceProvider>(() => new DependencyInfoResourceV2FeedProvider());
            yield return new Lazy<INuGetResourceProvider>(() => new DownloadResourceV2FeedProvider());
            yield return new Lazy<INuGetResourceProvider>(() => new MetadataResourceV2FeedProvider());
            yield return new Lazy<INuGetResourceProvider>(() => new V3FeedListResourceProvider());
            yield return new Lazy<INuGetResourceProvider>(() => new V2FeedListResourceProvider());
            yield return new Lazy<INuGetResourceProvider>(() => new LocalPackageListResourceProvider());
            yield return new Lazy<INuGetResourceProvider>(() => new PackageSearchResourceV2FeedProvider());
            yield return new Lazy<INuGetResourceProvider>(() => new PackageSearchResourceV3Provider());
            yield return new Lazy<INuGetResourceProvider>(() => new PackageMetadataResourceV2FeedProvider());
            yield return new Lazy<INuGetResourceProvider>(() => new PackageMetadataResourceV3Provider());
            yield return new Lazy<INuGetResourceProvider>(() => new AutoCompleteResourceV2FeedProvider());
            yield return new Lazy<INuGetResourceProvider>(() => new AutoCompleteResourceV3Provider());
            yield return new Lazy<INuGetResourceProvider>(() => new PluginResourceProvider());
            yield return new Lazy<INuGetResourceProvider>(() => new RepositorySignatureResourceProvider());

            // Local repository providers
            yield return new Lazy<INuGetResourceProvider>(() => new FindLocalPackagesResourceUnzippedProvider());
            yield return new Lazy<INuGetResourceProvider>(() => new FindLocalPackagesResourceV2Provider());
            yield return new Lazy<INuGetResourceProvider>(() => new FindLocalPackagesResourceV3Provider());
            yield return new Lazy<INuGetResourceProvider>(() => new FindLocalPackagesResourcePackagesConfigProvider());
            yield return new Lazy<INuGetResourceProvider>(() => new LocalAutoCompleteResourceProvider());
            yield return new Lazy<INuGetResourceProvider>(() => new LocalDependencyInfoResourceProvider());
            yield return new Lazy<INuGetResourceProvider>(() => new LocalDownloadResourceProvider());
            yield return new Lazy<INuGetResourceProvider>(() => new LocalMetadataResourceProvider());
            yield return new Lazy<INuGetResourceProvider>(() => new LocalPackageMetadataResourceProvider());
            yield return new Lazy<INuGetResourceProvider>(() => new LocalPackageSearchResourceProvider());
        }
    }
}
