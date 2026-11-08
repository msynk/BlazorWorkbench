using BlazorWorkbench;
using BlazorWorkbench.Demo.Components;
using BlazorWorkbench.Demo.Components.Display;

namespace BlazorWorkbench.Demo.Providers;

/// <summary>
/// A custom scenario provider: generates one Badge scenario per <see cref="Tone"/> value, so new tones are covered
/// (and visually tested) automatically. Providers with a public parameterless constructor are discovered.
/// </summary>
public sealed class ToneMatrixProvider : IScenarioProvider
{
    public IEnumerable<ScenarioSetDefinition> GetScenarioSets()
    {
        var set = new ScenarioSetBuilder<Badge>(typeof(ToneMatrixProvider));
        foreach (var tone in Enum.GetValues<Tone>())
        {
            set.Add($"Tone {tone}", s => s
                .Parameter(b => b.Text, tone.ToString())
                .Parameter(b => b.Tone, tone)
                .Tags("generated", "matrix"));
        }

        yield return set.Build();
    }
}
