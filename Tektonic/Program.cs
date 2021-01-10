using BlazorDownloadFile;
using BlazorFluentUI;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Tektonic.CodeGen;

namespace Tektonic
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var builder = WebAssemblyHostBuilder.CreateDefault(args);
            builder.RootComponents.Add<App>("#app");

            builder.Services.AddBlazorFluentUI();

            builder.Services.AddBlazorDownloadFile();

            builder.Services.AddSingleton<ITypeMapInitializer, SingleAssemblyReflectionTypeMapInitializer>();
            builder.Services.AddSingleton<TypeMapManager>();
            builder.Services.AddSingleton<ManifestGenerator>();

            builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

            await builder.Build().RunAsync();
        }
    }
}
