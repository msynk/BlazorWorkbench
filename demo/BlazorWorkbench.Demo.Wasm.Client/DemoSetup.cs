using BlazorWorkbench;
using BlazorWorkbench.Demo;

namespace BlazorWorkbench.Demo.Wasm.Client;

/// <summary>Workbench configuration shared by the WebAssembly client and its host.</summary>
public static class DemoSetup
{
    public static void Configure(WorkbenchOptions options)
    {
        DemoCatalog.Configure(options);
        options.Title = $"{DemoCatalog.Title} (WebAssembly)";
        // The scenarios are compiled into this client (linked from the Interactive Server demo).
        options.ScenarioAssemblies.Add(typeof(DemoSetup).Assembly);
    }
}
