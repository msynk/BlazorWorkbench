using BlazorWorkbench.WebAssembly;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using SampleWorkbench.Wasm.Client;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
await builder.AddBlazorWorkbenchAsync(SampleSetup.Configure);
await builder.Build().RunAsync();
