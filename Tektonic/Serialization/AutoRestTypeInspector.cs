﻿using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace Tektonic.Serialization
{
    internal class AutoRestTypeInspector : ITypeInspector
    {
        private readonly ITypeInspector _inner;

        public AutoRestTypeInspector(ITypeInspector inner)
        {
            _inner = inner;
        }

        public IEnumerable<IPropertyDescriptor> GetProperties(Type type, object container)
        {
            var pds = _inner.GetProperties(type, container);
            return pds.Select(pd => TrimPropertySuffix(pd, type)).ToList();
        }

        public IPropertyDescriptor GetProperty(Type type, object container, string name, bool ignoreUnmatched)
        {
            try
            {
                var prop = _inner.GetProperty(type, container, name, ignoreUnmatched);
                if (prop == null)
                {
                    prop = GetProperties(type, container).Where(p => p.Name == name).FirstOrDefault();
                }
                return prop;
            }
            catch (System.Runtime.Serialization.SerializationException)
            {
                return _inner.GetProperty(type, container, name + "Property", ignoreUnmatched);
            }
        }

        private IPropertyDescriptor TrimPropertySuffix(IPropertyDescriptor pd, Type type)
        {
            // This might have been renamed by AutoRest.  See if there is a
            // JsonPropertyAttribute.PropertyName and use that instead if there is.
            var jpa = pd.GetCustomAttribute<JsonPropertyAttribute>();
            if (jpa == null || String.IsNullOrEmpty(jpa.PropertyName))
            {
                return pd;
            }

            return new RenamedPropertyDescriptor(pd, jpa.PropertyName);
        }

        private class RenamedPropertyDescriptor : IPropertyDescriptor
        {
            private readonly IPropertyDescriptor _inner;
            private readonly string _name;

            public RenamedPropertyDescriptor(IPropertyDescriptor inner, string name)
            {
                _inner = inner;
                _name = name;
            }

            public string Name => _name;

            public bool CanWrite => _inner.CanWrite;

            public Type Type => _inner.Type;

            public Type TypeOverride
            {
                get => _inner.TypeOverride;
                set => _inner.TypeOverride = value;
            }

            public int Order
            {
                get => _inner.Order;
                set => _inner.Order = value;
            }

            public ScalarStyle ScalarStyle
            {
                get => _inner.ScalarStyle;
                set => _inner.ScalarStyle = value;
            }

            public T GetCustomAttribute<T>() where T : Attribute
            {
                return _inner.GetCustomAttribute<T>();
            }

            public IObjectDescriptor Read(object target)
            {
                return _inner.Read(target);
            }

            public void Write(object target, object value)
            {
                _inner.Write(target, value);
            }
        }
    }
}