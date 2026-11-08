using BlazorWorkbench.Maui.Essentials;
using BlazorWorkbench.WebAssembly;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using SampleComponents;
using SampleWorkbench.Wasm.Client;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.Services.AddSampleComponents();
builder.Services.AddSimulatedDevice();
await builder.AddBlazorWorkbenchAsync(SampleSetup.Configure);
await builder.Build().RunAsync();
