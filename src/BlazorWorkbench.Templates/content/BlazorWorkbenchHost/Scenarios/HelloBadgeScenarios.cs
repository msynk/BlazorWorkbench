using BlazorWorkbench;
using Company.Workbench.Components;

namespace Company.Workbench.Scenarios;

/// <summary>Example scenarios. See https://github.com/msynk/BlazorWorkbench/blob/main/docs/scenarios.md.</summary>
public sealed class HelloBadgeScenarios : ScenarioSet<HelloBadge>
{
    protected override void Configure(ScenarioSetBuilder<HelloBadge> set)
    {
        set.Title("Examples/Hello badge");
        set.Add("World", s => s
            .Parameter(b => b.Name, "World")
            .Interaction("greets", i => i.ExpectText(By.Css(".hello-badge"), "Hello, World!")));
        set.Add("Long name", s => s.Parameter(b => b.Name, "Ada Augusta King, Countess of Lovelace"));
        set.Add("Right to left", s => s.Parameter(b => b.Name, "جهان").Culture("fa-IR"));
    }
}
