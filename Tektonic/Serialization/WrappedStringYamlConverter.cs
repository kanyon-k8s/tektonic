using Kanyon.Core;
using System;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Tektonic.Serialization
{
    public class WrappedStringYamlConverter : IYamlTypeConverter
    {
        public bool Accepts(Type type)
        {
            return typeof(WrappedString).IsAssignableFrom(type);
        }

        public object ReadYaml(IParser parser, Type type)
        {
            if (parser.Current is Scalar scalar)
            {
                try
                {
                    return Activator.CreateInstance(type, scalar.Value);
                }
                finally
                {
                    parser.MoveNext();
                }
            }

            throw new InvalidOperationException(parser.Current?.ToString());
        }

        public void WriteYaml(IEmitter emitter, object value, Type type)
        {
            var obj = (WrappedString)value;
            emitter.Emit(new Scalar(obj.Value));
        }
    }
}