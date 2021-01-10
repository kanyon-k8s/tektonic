using System.Reflection;

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

            var initializer = new SingleAssemblyReflectionTypeMapInitializer(assembly);
            var newMap = initializer.GetTypeMap();

            typeManager.CurrentTypeMap.Add(newMap);
        }

        public void LoadAssemblyFromNuGet(string packageName, string feedUrl = "")
        {

        }
    }
}
