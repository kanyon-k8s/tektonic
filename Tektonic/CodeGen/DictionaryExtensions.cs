using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Tektonic.CodeGen
{
    public static class DictionaryExtensions
    {
        public static void Add<K, V>(this Dictionary<K, V> d, Dictionary<K, V> other)
        {
            foreach (var kvp in other)
            {
                if (!d.ContainsKey(kvp.Key))
                {
                    d.Add(kvp.Key, kvp.Value);
                }
            }
        }
    }
}
