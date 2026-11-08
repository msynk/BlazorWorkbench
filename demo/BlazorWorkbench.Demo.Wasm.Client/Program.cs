using BlazorWorkbench.Maui.Essentials;
using BlazorWorkbench.WebAssembly;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using BlazorWorkbench.Demo.Components;
using BlazorWorkbench.Demo.Wasm.Client;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.Services.AddDemoComponents();
builder.Services.AddSimulatedDevice();
await builder.AddBlazorWorkbenchAsync(DemoSetup.Configure);
await builder.Build().RunAsync();
