using BlazorWorkbench;
using SampleWorkbench;

namespace SampleWorkbench.Wasm.Client;

/// <summary>Workbench configuration shared by the WebAssembly client and its host.</summary>
public static class SampleSetup
{
    public static void Configure(WorkbenchOptions options)
    {
        options.Title = "Sample Components (WebAssembly)";
        SampleCatalog.Configure(options);
        // The scenarios are compiled into this client (linked from the Interactive Server sample).
        options.ScenarioAssemblies.Add(typeof(SampleSetup).Assembly);
    }
}
