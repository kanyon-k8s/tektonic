using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Linq;
using System.Reflection;
using Tektonic.CodeGen.Packages;

namespace Tektonic.CodeGen
{
    public class TypeLoader
    {
        private readonly TypeMapManager typeManager;

        public TypeLoader(TypeMapManager typeManager)
        {
            this.typeManager = typeManager;
        }

        public void LoadAssemblyFromDll(byte[] dll, byte[] pdb = null)
        {
            var assembly = Assembly.Load(dll, pdb);
            LoadAssemblyIntoTypeMap(assembly);
        }

        private void LoadAssemblyIntoTypeMap(Assembly assembly)
        {
            var initializer = new SingleAssemblyReflectionTypeMapInitializer(assembly);
            var newMap = initializer.GetTypeMap();

            if (newMap.Any()) 
                typeManager.CurrentTypeMap.Add(newMap);
        }

        public async System.Threading.Tasks.Task LoadAssemblyFromNuGetAsync(string packageName, string feedUrl = "")
        {
            if (typeManager.NuGetPackages.Any(p => p.Package == packageName))
            {
                throw new PackageAlreadyInstalledException();
            }

            var loader = new Loader();
            var package = new NuGetPackage { Package = packageName };
            var assemblies = await loader.LoadPackage(package);

            foreach (var assembly in assemblies)
            {
                LoadAssemblyIntoTypeMap(assembly);
            }

            typeManager.NuGetPackages.Add(package);
        }
    }
}
