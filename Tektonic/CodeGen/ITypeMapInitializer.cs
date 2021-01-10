using System;
using System.Collections.Generic;

namespace Tektonic.CodeGen
{
    public interface ITypeMapInitializer
    {
        Type GetFallbackType();
        Dictionary<string, Type> GetTypeMap();
    }
}