using Kanyon.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using YamlDotNet.Serialization.ObjectFactories;

namespace Tektonic.Serialization
{
    public static class YamlConverter
    {
        public static string SerializeObject(object value)
        {
            var stringBuilder = new StringBuilder();
            var writer = new StringWriter(stringBuilder);
            var emitter = new Emitter(writer);

            var serializer =
                new SerializerBuilder()
                    .DisableAliases()
                    .WithNamingConvention(CamelCaseNamingConvention.Instance)
                    .WithTypeInspector(ti => new AutoRestTypeInspector(ti))
                    .WithTypeConverter(new WrappedStringYamlConverter())
                    .WithEventEmitter(e => new StringQuotingEmitter(e))
                    .BuildValueSerializer();
            emitter.Emit(new StreamStart());
            emitter.Emit(new DocumentStart());
            serializer.SerializeValue(emitter, value, value.GetType());

            return stringBuilder.ToString();
        }

        /// <summary>
        /// Load a collection of objects from a string
        /// </summary>
        /// <param name="content">
        /// The string to load the objects from.
        /// </param>
        /// <param name="typeMap">
        /// A map from apiVersion/kind to Type. For example "v1/Pod" -> typeof(V1Pod)
        /// </param>
        /// <returns>collection of objects</returns>
        public static List<object> LoadAllFromString(string content, Dictionary<string, Type> typeMap, Type fallbackType)
        {
            if (typeMap == null)
            {
                throw new ArgumentNullException(nameof(typeMap));
            }
            IDeserializer deserializer = BuildDeserializer();
            var types = new List<Type>();
            var parser = new Parser(new StringReader(content));
            parser.Consume<StreamStart>();
            while (parser.Accept<DocumentStart>(out _))
            {
                var obj = deserializer.Deserialize<ManifestObject>(parser);

                string key = obj.apiVersion + "/" + obj.kind;
                if (!typeMap.ContainsKey(key))
                {
                    types.Add(fallbackType);
                }

                types.Add(typeMap[key]);
            }

            // Reinitialize the deserializer
            deserializer = BuildDeserializer();
            parser = new Parser(new StringReader(content));
            parser.Consume<StreamStart>();
            var ix = 0;
            var results = new List<object>();
            while (parser.Accept<DocumentStart>(out _))
            {
                var objType = types[ix++];
                var obj = deserializer.Deserialize(parser, objType);
                results.Add(obj);
            }

            return results;
        }

        private static IDeserializer BuildDeserializer()
        {
            return new DeserializerBuilder()
                    .WithNamingConvention(CamelCaseNamingConvention.Instance)
                    .WithTypeInspector(ti => new AutoRestTypeInspector(ti))
                    .WithTypeConverter(new WrappedStringYamlConverter())
                    .WithTypeConverter(new OpenApiAnyYamlConverter())
                    .WithObjectFactory(new OpenApiFixupFactory(new DefaultObjectFactory()))
                    .IgnoreUnmatchedProperties()
                    .Build();
        }

    }
}
