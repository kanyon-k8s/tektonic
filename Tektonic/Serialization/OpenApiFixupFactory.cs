using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using YamlDotNet.Serialization;

namespace Tektonic.Serialization
{
    public class OpenApiFixupFactory : IObjectFactory
    {
        private readonly IObjectFactory fallback;

        public OpenApiFixupFactory(IObjectFactory fallback)
        {
            this.fallback = fallback;
        }
        public object Create(Type type)
        {
            var comparisonType = typeof(ISet<>);
            
            if (type.IsGenericType && type.GetGenericTypeDefinition() == comparisonType)
            {
                var itemType = type.GenericTypeArguments[0];
                var setType = typeof(HashSet<>).MakeGenericType(itemType);

                return Activator.CreateInstance(setType);
            }
            else return fallback.Create(type);
        }
    }
}
