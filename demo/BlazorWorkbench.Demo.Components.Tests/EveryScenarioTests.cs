using System.Reflection;
using BlazorWorkbench.Testing.Bunit;
using SampleComponents.Inputs;
using SampleWorkbench.Scenarios;

namespace SampleComponents.Tests;

/// <summary>
/// One smoke test per scenario: every scenario must render without exceptions or error diagnostics. New scenarios are
/// covered the moment they are declared, without writing a test.
/// </summary>
public class EveryScenarioTests : SampleTestContext
{
    private static readonly Assembly[] Assemblies = [typeof(CheckoutScenarios).Assembly, typeof(Button).Assembly];

    public static IEnumerable<object[]> ScenarioIds => WorkbenchBunit.ScenarioIds(Assemblies);

    [Theory]
    [MemberData(nameof(ScenarioIds))]
    public void Renders_without_errors(string scenarioId)
    {
        var cut = this.RenderScenario(scenarioId, Assemblies);
        cut.WaitForScenarioIdle();
        cut.AssertNoScenarioErrors();
    }
}
