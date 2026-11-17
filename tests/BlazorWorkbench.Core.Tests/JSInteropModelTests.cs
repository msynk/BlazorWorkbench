using BlazorWorkbench.Catalog;
using BlazorWorkbench.Contracts;
using BlazorWorkbench.Core.Tests.Components;
using BlazorWorkbench.Documentation;

namespace BlazorWorkbench.Core.Tests;

public class JSInteropModelTests
{
    private sealed class Set : ScenarioSet<TestButton>
    {
        protected override void Configure(ScenarioSetBuilder<TestButton> set)
        {
            set.Setup(s => s.JSInterop(js => js.Setup("shared").Returns(1)));
            set.Add("Planned", s => s.JSInterop(js =>
            {
                js.Setup("localStorage.getItem").Returns("v").Times(2);
                js.Setup("clipboard.*").Throws("denied");
                js.Module("./_content/Lib/map.js", m => m.Setup("init").Hold().Delay(TimeSpan.FromMilliseconds(20)));
                js.Strict();
            }).Interaction("calls", i => i.ExpectJSCall("localStorage.getItem", times: 1)));
            set.Add("Plain");
        }
    }

    private static WorkbenchCatalog Catalog()
    {
        var options = new WorkbenchOptions();
        options.AddScenarios(new Set());
        return CatalogBuilder.Build(options);
    }

    [Fact]
    public void Builder_collects_scenario_rules_before_setup_rules()
    {
        var scenario = Catalog().Scenarios.Single(s => s.Key == "planned");
        var js = scenario.Environment.JSInterop!;
        Assert.True(js.Strict);
        Assert.Equal(["localStorage.getItem", "clipboard.*", "init", "shared"], js.Rules.Select(r => r.Identifier));
        Assert.Equal("./_content/Lib/map.js", js.Rules[2].Module);
        Assert.Equal("js init", js.Rules[2].Gate);
        Assert.True(js.HasModuleRules("_content/Lib/map.js"));
        Assert.False(js.HasModuleRules("_content/Lib/other.js"));

        var plain = Catalog().Scenarios.Single(s => s.Key == "plain");
        Assert.Equal(["shared"], plain.Environment.JSInterop!.Rules.Select(r => r.Identifier));
        Assert.False(plain.Environment.JSInterop.Strict);
    }

    [Theory]
    [InlineData("clipboard.*", "clipboard.writeText", true)]
    [InlineData("clipboard.*", "navigator.clipboard.writeText", false)]
    [InlineData("*", "anything", true)]
    [InlineData("a.b", "a.b", true)]
    [InlineData("a.b", "a.bc", false)]
    public void Identifiers_match_with_wildcards(string pattern, string identifier, bool expected) =>
        Assert.Equal(expected, new JSInteropRule { Identifier = pattern }.IsMatch(new JSCall(identifier, null, [])));

    [Fact]
    public void Module_rules_match_only_module_calls()
    {
        var rule = new JSInteropRule { Identifier = "init", Module = "/_content/*/map.js" };
        Assert.True(rule.IsMatch(new JSCall("init", "./_content/Lib/map.js", [])));
        Assert.False(rule.IsMatch(new JSCall("init", null, [])));
        Assert.False(new JSInteropRule { Identifier = "init" }.IsMatch(new JSCall("init", "_content/Lib/map.js", [])));
    }

    [Fact]
    public void Rules_describe_their_answer()
    {
        var js = Catalog().Scenarios.Single(s => s.Key == "planned").Environment.JSInterop!;
        Assert.Equal("returns \"v\", 2 time(s)", js.Rules[0].Describe());
        Assert.Equal("throws \"denied\"", js.Rules[1].Describe());
        Assert.Equal("held at gate 'js init', after 20 ms, returns nothing", js.Rules[2].Describe());
        Assert.Equal("./_content/Lib/map.js → init", js.Rules[2].ToString());
    }

    [Fact]
    public void Arguments_are_formatted_for_logs()
    {
        Assert.Equal("\"key\", 3, {\"a\":1}, null", JSInteropFormat.Arguments(["key", 3, new { a = 1 }, null]));
        Assert.Equal("MemoryStream", JSInteropFormat.Arguments([new MemoryStream()]));
        Assert.EndsWith("…", JSInteropFormat.Arguments([new string('x', 500)], 50), StringComparison.Ordinal);
    }

    [Fact]
    public void Expect_js_call_is_validated_exported_and_generated()
    {
        var catalog = Catalog();
        var scenario = catalog.Scenarios.Single(s => s.Key == "planned");
        Assert.DoesNotContain(catalog.IssuesFor(scenario), i => i.Severity == IssueSeverity.Error);
        var step = scenario.Interactions[0].Steps[0];
        Assert.Equal(StepAction.ExpectJSCall, step.Action);
        Assert.Equal(".ExpectJSCall(\"localStorage.getItem\", times: 1)", InteractionCode.Step(step));
        Assert.Equal(".ExpectJSCall(\"x\")", InteractionCode.Step(new InteractionStep { Action = StepAction.ExpectJSCall, Value = "x" }));

        var issues = CatalogBuilder.Build(Options(i => i.Interaction("t", b => b.ExpectJSCall(null!)))).Issues;
        Assert.Contains(issues, i => i.Code == "WB1012");
    }

    [Fact]
    public void Catalog_contract_lists_planned_calls()
    {
        var catalog = Catalog();
        var dto = CatalogMapper.ToDto(catalog, _ => null, "1.0", "Server");
        var env = dto.Scenarios.Single(s => s.Id.EndsWith("--planned", StringComparison.Ordinal)).Environment;
        Assert.True(env.JSInteropStrict);
        Assert.Equal(4, env.JSInterop.Count);
        Assert.Equal("./_content/Lib/map.js", env.JSInterop[2].Module);
        Assert.Equal("js init", env.JSInterop[2].Gate);
    }

    private static WorkbenchOptions Options(Action<ScenarioBuilder<TestButton>> configure)
    {
        var options = new WorkbenchOptions();
        options.AddScenarios(new InlineSet(configure));
        return options;
    }

    private sealed class InlineSet(Action<ScenarioBuilder<TestButton>> configure) : ScenarioSet<TestButton>
    {
        protected override void Configure(ScenarioSetBuilder<TestButton> set) => set.Add("S", configure);
    }
}
