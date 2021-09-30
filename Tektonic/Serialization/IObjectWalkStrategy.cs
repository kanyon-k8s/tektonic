using System;
using System.Reflection;

namespace Tektonic.Serialization
{
    public interface IObjectWalkStrategy
    {
        bool CanWalk(PropertyInfo info);
        void Walk(Object o, PropertyInfo pi);
    }
}
