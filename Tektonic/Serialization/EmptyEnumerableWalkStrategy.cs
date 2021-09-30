using System.Collections;
using System.Linq;
using System.Reflection;

namespace Tektonic.Serialization
{
    public class EmptyEnumerableWalkStrategy : IObjectWalkStrategy
    {
        public bool CanWalk(PropertyInfo info)
        {
            return info.PropertyType.IsAssignableTo(typeof(IEnumerable));
        }

        public void Walk(object o, PropertyInfo pi)
        {
            var value = pi.GetValue(o) as IEnumerable;

            if (value != null && !value.Cast<object>().Any())
            {
                pi.SetValue(o, null);
            }
        }
    }
}
