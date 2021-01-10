using Kanyon.Kubernetes;
using Microsoft.OpenApi.Any;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tektonic.Serialization;

namespace Tektonic.CodeGen
{
    public class ManifestGenerator
    {
        private readonly TypeMapManager typeMapManager;
        private readonly DumpOptions options;

        public ManifestGenerator(TypeMapManager typeMapManager)
        {
            this.typeMapManager = typeMapManager;
            options = new DumpOptions()
            {
                DumpStyle = DumpStyle.CSharp,
                ExcludeProperties = new List<string>() { "ApiVersion", "Kind" },
                IgnoreDefaultValues = true,
                UseTypeFullName = true,
                TrimInitialVariableName = true,
                TrimTrailingColonName = true
            };

            options.CustomInstanceFormatters.AddFormatter<IntOrString>(ios => $"\"{ios.Value}\"");
            options.CustomInstanceFormatters.AddFormatter<OpenApiString>(s => $"new Microsoft.OpenApi.Any.OpenApiString(\"{s.Value}\")");
        }

        public string GenerateManifest(string yaml, ManifestOptions manifestOptions)
        {
            var typeMap = typeMapManager.CurrentTypeMap;
            var manifests = YamlConverter.LoadAllFromString(yaml, typeMap, typeMapManager.FallbackType);

            if (manifests.Any())
            {
                var stringBuilder = new StringBuilder();
                var manifestBaseClass = GetBaseClass(manifestOptions);
                var functionHeader = GetFunctionHeader(manifestOptions);
                stringBuilder.Append($"public class {manifestOptions.ManifestName} : Kanyon.Core.{manifestBaseClass}");
                stringBuilder.Append(@"
{
    public ");
                stringBuilder.Append(functionHeader);
                stringBuilder.Append(@"
    {
");
                stringBuilder.AppendJoin("\n", manifests.Select(manifest => $"       Add({ObjectDumper.Dump(manifest, options)});"));

                stringBuilder.Append(@"
    }
}");
                return stringBuilder.ToString().Trim();
            }

            return null;
        }

        private object GetFunctionHeader(ManifestOptions options)
        {
            if (string.IsNullOrEmpty(options.ManifestBaseClass) || options.ManifestBaseClass == "Manifest") return $"{options.ManifestName}()";
            else return $"override void ConfigureItems({options.ManifestName}Configuration configuration)";
        }

        private object GetBaseClass(ManifestOptions options)
        {
            if (string.IsNullOrEmpty(options.ManifestBaseClass) || options.ManifestBaseClass == "Manifest") return "Manifest";
            else return $"ConfiguredManifest<{options.ManifestName}Configuration>";
        }

        public string GenerateCsx(string yaml, ManifestOptions manifestOptions)
        {
            StringBuilder result = new StringBuilder();
            var manifest = GenerateManifest(yaml, manifestOptions);

            if (manifest != null)
            {
                result.Append(@"
#r ""nuget:Kanyon.Kubernetes, 3.1.2""


");
                result.AppendLine(manifest);
                result.Append(@"
new Manifest()
");

                return result.ToString().TrimStart();
            }
            else return null;
        }

        public string GenerateCs(string yaml, ManifestOptions manifestOptions)
        {
            StringBuilder result = new StringBuilder();
            var manifest = GenerateManifest(yaml, manifestOptions);

            if (manifest != null)
            {
                result.Append(@"
using System;
using System.Collections.Generic;
using System.Linq;

namespace Tektonic.GeneratedManifests {

");
                result.AppendLine(manifest);
                result.Append(@"
}
");

                return result.ToString().TrimStart();
            }
            else return null;
        }

        public string GenerateCsProjFile()
        {
            return @"
<Project Sdk=""Microsoft.NET.Sdk"">

  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include=""Kanyon.Kubernetes"" Version=""3.1.2"" />
  </ItemGroup>

</Project>";
        }

        public byte[] GenerateCsProj(string yaml, ManifestOptions manifestOptions)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
                {
                    CreateZipArchiveEntry(zip, $"{manifestOptions.ManifestName}.csproj", GenerateCsProjFile());
                    CreateZipArchiveEntry(zip, $"{manifestOptions.ManifestName}.cs", GenerateCs(yaml, manifestOptions));
                }

                return stream.ToArray();
            }
        }

        public void CreateZipArchiveEntry(ZipArchive archive, string name, string content)
        {
            ZipArchiveEntry entry = archive.CreateEntry(name);
            using (var writer = new StreamWriter(entry.Open()))
            {
                writer.Write(content);
            }
        }
    }
}
