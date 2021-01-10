using BlazorDownloadFile;
using BlazorFluentUI;
using Microsoft.AspNetCore.Components;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Tektonic.CodeGen;
using Tektonic.Shared;

namespace Tektonic.Pages
{
    public partial class Index
    {
        private Editor yamlEditor;
        private Editor csharpEditor;

        private bool IsSettingsOpen { get; set; }
        private bool IsTypeMapOpen { get; set; }

        private string ManifestName { get; set; }
        private IBFUDropdownOption ManifestBaseClass { get; set; }

        private List<IBFUDropdownOption> ManifestBaseClasses { get; set; } = new List<IBFUDropdownOption> { new BFUDropdownOption() { Text = "Manifest", Key = "Manifest" }, new BFUDropdownOption { Text = "ConfiguredManifest<>", Key = "ConfiguredManifest" } };

        [Inject] IBlazorDownloadFileService BlazorDownloadFileService { get; set; }

        public Index()
        {
            GenerateCsxCommand = new RelayCommand(GenerateCsx);
            GenerateCsCommand = new RelayCommand(GenerateCs);
            GenerateCsProjCommand = new RelayCommand(GenerateCsProj);
            OpenSettingsCommand = new RelayCommand(_ => { IsSettingsOpen = true; StateHasChanged(); });
            OpenTypeMapCommand = new RelayCommand(_ => { IsTypeMapOpen = true; StateHasChanged(); });

            Items = new List<BFUCommandBarItem>()
            {
                new BFUCommandBarItem() { IconName = "Save", Text = "Download", Items = new List<BFUCommandBarItem>
                    {
                        new BFUCommandBarItem() { Text = "C# Script (.csx)", IconName = "Script", Command = GenerateCsxCommand },
                        new BFUCommandBarItem() { Text = "C# Class (.cs)", IconName = "CSharpLanguage", Command = GenerateCsCommand },
                        new BFUCommandBarItem() { Text = "C# Project (.csproj)", IconName = "VSTSLogo", Command = GenerateCsProjCommand }
                    }
                },
                new BFUCommandBarItem() { IconName = "Settings", Text = "Settings", IconOnly = true, Command = OpenSettingsCommand }
            };

            FarItems = new List<BFUCommandBarItem>
            {
                new BFUCommandBarItem() { IconName = "HighlightMappedShapes", Text = "Type Map", IconOnly = true, Command = OpenTypeMapCommand },
                new BFUCommandBarItem() { IconName = "Add", Text = "Add Kanyon TypeLib", IconOnly = true }
            };


            ManifestName = "Manifest";
            ManifestBaseClass = ManifestBaseClasses.First();
        }

        private List<BFUCommandBarItem> Items { get; set; }
        private List<BFUCommandBarItem> FarItems { get; set; }

        private ManifestOptions BuildManifestOptions()
        {
            return new ManifestOptions
            {
                ManifestName = ManifestName,
                ManifestBaseClass = ManifestBaseClass.Key
            };
        }

        private async Task OnContentChanged(BlazorMonaco.Bridge.ModelContentChangedEvent ev)
        {
            try
            {
                string manifest = await GenerateManifest();

                await csharpEditor.SetContent(manifest);
            }
            catch (Exception ex)
            {
                // Write to Snackbar
                Console.WriteLine(ex);
            }
        }

        private async Task<string> GenerateManifest()
        {
            var yaml = await yamlEditor.GetContent();
            var manifest = generator.GenerateManifest(yaml, BuildManifestOptions());
            return manifest;
        }

        public RelayCommand GenerateCsxCommand { get; private set; }

        private async void GenerateCsx(object parameter)
        {
            try
            {
                string yaml = await yamlEditor.GetContent();
                string csxFile = generator.GenerateCsx(yaml, BuildManifestOptions());

                await BlazorDownloadFileService.DownloadFileFromText("manifest.csx", csxFile, "text/plain");

            }
            catch (Exception ex)
            {
                // Write to Snackbar
                Console.WriteLine(ex);
            }
        }

        public RelayCommand GenerateCsCommand { get; private set; }

        private async void GenerateCs(object parameter)
        {
            try
            {
                string yaml = await yamlEditor.GetContent();
                string csFile = generator.GenerateCs(yaml, BuildManifestOptions());

                await BlazorDownloadFileService.DownloadFileFromText("Manifest.generated.cs", csFile, "text/plain");

            }
            catch (Exception ex)
            {
                // Write to Snackbar
                Console.WriteLine(ex);
            }
        }

        public RelayCommand GenerateCsProjCommand { get; private set; }

        private async void GenerateCsProj(object parameter)
        {
            try
            {
                string yaml = await yamlEditor.GetContent();
                var zip = generator.GenerateCsProj(yaml, BuildManifestOptions());

                await BlazorDownloadFileService.DownloadFile("Manifest.csproj.zip", zip, "application/zip");
            }
            catch (Exception ex)
            {
                // Write to Snackbar
                Console.WriteLine(ex);
            }
        }

        public RelayCommand OpenSettingsCommand { get; private set; }
        public RelayCommand OpenTypeMapCommand { get; private set; }
    }
}
