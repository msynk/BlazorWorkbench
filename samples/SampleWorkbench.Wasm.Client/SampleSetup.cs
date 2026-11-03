using BlazorWorkbench;
using SampleComponents;
using SampleComponents.Inputs;
using SampleWorkbench.Editors;

namespace SampleWorkbench.Wasm.Client;

/// <summary>Workbench configuration shared by the WebAssembly client and its host.</summary>
public static class SampleSetup
{
    public static void Configure(WorkbenchOptions options)
    {
        options.Title = "Sample Components (WebAssembly)";
        options.AddAssemblyOf<Button>();
        options.ScenarioAssemblies.Add(typeof(SampleSetup).Assembly);
        options.AddParameterEditor<HexColor, HexColorEditor>();
    }
}
