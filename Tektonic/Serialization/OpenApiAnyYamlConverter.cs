using Microsoft.OpenApi.Any;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Tektonic.Serialization
{
    public class OpenApiAnyYamlConverter : IYamlTypeConverter
    {
        public bool Accepts(Type type)
        {
            return type.IsAssignableTo(typeof(IOpenApiAny));
        }

        public object ReadYaml(IParser parser, Type type)
        {
            if (parser.Current is Scalar scalar)
            {
                try
                {
                    return new OpenApiString(scalar.Value);
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
            emitter.Emit(new Scalar(value.ToString()));
        }
    }
}
