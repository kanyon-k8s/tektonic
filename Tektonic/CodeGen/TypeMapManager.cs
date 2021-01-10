using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Tektonic.CodeGen
{
    public class TypeMapManager
    {
        private ITypeMapInitializer initializer;

        public Dictionary<string, Type> CurrentTypeMap { get; set; }

        public Type FallbackType { get; set; }

        public TypeMapManager(ITypeMapInitializer initializer)
        {
            this.initializer = initializer;
            Initialize();
        }

        public void Initialize()
        {
            CurrentTypeMap = initializer.GetTypeMap();
            FallbackType = initializer.GetFallbackType();
        }
    }
}
