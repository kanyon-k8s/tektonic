using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace Tektonic.Serialization
{
    public class ObjectWalker
    {
        public List<IObjectWalkStrategy> Strategies { get; set; }

        public void Walk(object o)
        {
            var properties = o.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (var property in properties)
            {
                if (property.PropertyType.IsPrimitive || property.PropertyType == typeof(string)) continue;

                foreach (var strategy in Strategies)
                {
                    if (strategy.CanWalk(property))
                    {
                        strategy.Walk(o, property);
                    }
                }

                var nextStep = property.GetValue(o);
                if (nextStep is IEnumerable list)
                {
                    foreach (var item in list)
                    {
                        Walk(item);
                    }
                }
                else if (nextStep != null)
                {
                    Walk(nextStep);
                }
            }
        }
    }

    public interface IObjectWalkStrategy
    {
        bool CanWalk(PropertyInfo info);
        void Walk(Object o, PropertyInfo pi);
    }

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
