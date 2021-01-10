using Kanyon.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Tektonic.Serialization;

namespace Tektonic.CodeGen
{
    public class SingleAssemblyReflectionTypeMapInitializer : ITypeMapInitializer
    {
        private readonly Assembly assembly;

        public SingleAssemblyReflectionTypeMapInitializer(Assembly assembly)
        {
            this.assembly = assembly;
        }

        public SingleAssemblyReflectionTypeMapInitializer(Type referenceType) : this(Assembly.GetAssembly(referenceType)) { }

        public Dictionary<string, Type> GetTypeMap()
        {
            var assembly = Assembly.GetAssembly(typeof(Kanyon.Kubernetes.Core.V1.ObjectMeta));
            var types = assembly.GetTypes().Where(t => t.IsAssignableTo(typeof(IManifestObject)));

            return types.ToDictionary(t =>
            {
                var manifestObject = Activator.CreateInstance(t) as IManifestObject;
                return $"{manifestObject.ApiVersion}/{manifestObject.Kind}";
            });
        }

        public Type GetFallbackType()
        {
            return typeof(ManifestObject);
        }
    }
}
