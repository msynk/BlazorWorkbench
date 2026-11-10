using BlazorWorkbench.Catalog;
using BlazorWorkbench.Core.Tests.Components;
using BlazorWorkbench.Documentation;

namespace BlazorWorkbench.Core.Tests;

/// <summary>Matrices, statuses, design references, forced states, storage, pages and generated interaction code.</summary>
public class AuthoringTests
{
    private static IReadOnlyList<ScenarioDefinition> Build(Action<ScenarioSetBuilder<TestButton>> configure)
    {
        var set = new ScenarioSetBuilder<TestButton>();
        configure(set);
        return set.Build().Scenarios;
    }

    private sealed class Set(Action<ScenarioSetBuilder<TestButton>> configure) : ScenarioSet<TestButton>
    {
        protected override void Configure(ScenarioSetBuilder<TestButton> set) => configure(set);
    }

    private static WorkbenchCatalog Catalog(Action<ScenarioSetBuilder<TestButton>> configure, Action<WorkbenchOptions>? options = null)
    {
        var o = new WorkbenchOptions { IncludeComponentsWithoutScenarios = false };
        o.AddScenarios(new Set(configure));
        options?.Invoke(o);
        return CatalogBuilder.Build(o);
    }

    [Fact]
    public void Matrix_adds_one_scenario_per_combination()
    {
        var scenarios = Build(set => set.Matrix("Grid", m => m.Vary(b => b.Tone).Vary(b => b.Disabled), s => s.Parameter(b => b.Label, "Go")));

        Assert.Equal(["Grid · Neutral · Disabled: false", "Grid · Neutral · Disabled: true", "Grid · Danger · Disabled: false", "Grid · Danger · Disabled: true"],
            scenarios.Select(s => s.Name));
        var last = scenarios[^1];
        Assert.Equal("grid-danger-disabled-true", last.Key);
        Assert.Equal(Tone.Danger, last.FindParameter("Tone")!.Value);
        Assert.Equal(true, last.FindParameter("Disabled")!.Value);
        Assert.Equal("Go", last.FindParameter("Label")!.Value);
        Assert.Contains("matrix", last.Tags);
        Assert.Equal("Grid", last.Matrix!.Name);
        Assert.Equal([new("Tone", "Danger"), new KeyValuePair<string, string>("Disabled", "true")], last.Matrix.Values);
    }

    [Fact]
    public void Pairwise_matrix_covers_every_pair_with_fewer_scenarios()
    {
        var scenarios = Build(set => set.Matrix("Pairs", m => m
            .Vary(b => b.Tone)
            .Vary(b => b.Disabled)
            .Vary(b => b.Count, 1, 2, 3)
            .Vary(b => b.Value, "a", "b", "c")
            .Pairwise()));

        Assert.True(scenarios.Count < 2 * 2 * 3 * 3, $"Pairwise should need fewer than 36 scenarios, got {scenarios.Count}.");
        var rows = scenarios.Select(s => s.Matrix!.Values.Select(v => v.Value).ToArray()).ToList();
        var domains = new[] { new[] { "Neutral", "Danger" }, ["false", "true"], ["1", "2", "3"], ["a", "b", "c"] };
        for (var a = 0; a < domains.Length; a++)
        for (var b = a + 1; b < domains.Length; b++)
        foreach (var va in domains[a])
        foreach (var vb in domains[b])
            Assert.Contains(rows, r => r[a] == va && r[b] == vb);
    }

    [Fact]
    public void Matrix_values_can_carry_labels_and_nullable_values_include_null()
    {
        var scenarios = Build(set => set.Matrix("Labeled", m => m.Vary(b => b.Count, [("none", null), ("many", 99)])));
        Assert.Equal(["Labeled · none", "Labeled · many"], scenarios.Select(s => s.Name));
        Assert.Null(scenarios[0].FindParameter("Count")!.Value);

        Assert.Throws<ArgumentException>(() => Build(set => set.Matrix("Bad", m => m.Vary(b => b.Label))));
        Assert.Throws<InvalidOperationException>(() => Build(set => set.Matrix("Empty", _ => { })));
    }

    [Fact]
    public void Status_and_designs_reach_the_catalog()
    {
        var catalog = Catalog(set => set
            .Status(ComponentStatus.Beta)
            .Design("designs/button.png", "Button")
            .Add("Primary", s => s.Parameter(b => b.Label, "x").Design("https://www.figma.com/design/abc/Kit?node-id=1-2")));

        var component = catalog.Components.Single();
        Assert.Equal(ComponentStatus.Beta, component.Status);
        var designs = component.Scenarios.Single().Designs;
        Assert.Equal([DesignKind.Image, DesignKind.Figma], designs.Select(d => d.Kind));
        Assert.Equal("Button", designs[0].DisplayTitle);
        Assert.DoesNotContain(catalog.Issues, i => i.Code == "WB1023");
    }

    [Fact]
    public void Deprecated_components_get_the_deprecated_status()
    {
        var catalog = Catalog(set => set.Deprecated("Use NewButton.").Add("Old", s => s.Parameter(b => b.Label, "x")));
        Assert.Equal(ComponentStatus.Deprecated, catalog.Components.Single().Status);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:image/png;base64,AAAA")]
    [InlineData("//evil.example/x.png")]
    public void Unsafe_design_urls_are_reported(string url)
    {
        var catalog = Catalog(set => set.Add("Primary", s => s.Parameter(b => b.Label, "x").Design(url)));
        Assert.Contains(catalog.Issues, i => i.Code == "WB1023");
    }

    [Fact]
    public void Forced_states_and_storage_are_part_of_the_environment()
    {
        var scenario = Build(set => set.Add("States", s => s
            .ForceState(PseudoState.Hover)
            .ForceState(PseudoState.FocusVisible, ".btn")
            .ForceState(PseudoState.Hover)
            .LocalStorage("prefs", new { Dense = true })
            .LocalStorage("token", "abc")
            .SessionStorage("step", 2))).Single();

        Assert.Equal([":hover", ".btn:focus-visible"], scenario.Environment.PseudoStates.Select(p => p.ToString()));
        Assert.Equal(
            [new StorageEntry(StorageArea.Local, "prefs", """{"dense":true}"""), new StorageEntry(StorageArea.Local, "token", "abc"), new StorageEntry(StorageArea.Session, "step", "2")],
            scenario.Environment.Storage);
    }

    [Fact]
    public void Notes_are_kept_on_the_scenario()
    {
        var scenario = Build(set => set.Add("Noted", s => s.Notes("**Review** pending."))).Single();
        Assert.Equal("**Review** pending.", scenario.Notes);
    }

    [Fact]
    public void Directives_split_markdown_outside_code_fences()
    {
        var segments = MarkdownDirectives.Split("""
            Intro

            {{scenario:inputs-button--primary}}

            ```
            {{scenario:not-a-directive}}
            ```
            {{ tokens }}
            {{unknown:x}}
            """);

        Assert.Equal([null, "scenario", null, "tokens", null], segments.Select(s => s.Directive));
        Assert.Equal("inputs-button--primary", segments[1].Argument);
        Assert.Contains("{{scenario:not-a-directive}}", segments[2].Markdown);
        Assert.Contains("{{unknown:x}}", segments[4].Markdown);
    }

    [Fact]
    public void Pages_with_broken_embeds_are_reported()
    {
        var catalog = Catalog(
            set => set.Add("Primary", s => s.Parameter(b => b.Label, "x")),
            o => o.AddPage("Guides/Buttons", "{{scenario:components-test-button--primary}}\n\n{{scenario:nope--nothing}}\n\n{{parameters:missing}}"));

        var page = Assert.Single(catalog.Pages);
        Assert.Equal("guides-buttons", page.Id);
        Assert.Same(page, catalog.FindPage("GUIDES-BUTTONS"));
        var issues = catalog.Issues.Where(i => i.Code == "WB1025").ToList();
        Assert.Equal(2, issues.Count);
        Assert.Contains(issues, i => i.Message.Contains("nope--nothing", StringComparison.Ordinal));
    }

    [Fact]
    public void Users_and_network_profiles_are_found_by_name_or_id()
    {
        var options = new WorkbenchOptions();
        options.AddUser("Owner on Pro", u => u.Name("Grace").Role("Owner").Claim("plan", "pro"));

        var user = options.FindUser("owner-on-pro")!;
        Assert.Equal("Grace", user.User.Name);
        Assert.Same(user, options.FindUser("Owner on Pro"));
        Assert.True(options.FindNetworkProfile("slow-3g")!.Latency > TimeSpan.FromSeconds(1));
        Assert.True(options.FindNetworkProfile("Offline")!.Offline);
        Assert.Null(options.FindNetworkProfile("5g"));
    }

    [Fact]
    public void Recorded_steps_become_csharp()
    {
        InteractionStep[] steps =
        [
            new() { Action = StepAction.Fill, Target = By.Label("Email"), Value = "ada@example.com" },
            new() { Action = StepAction.Press, Value = "Tab" },
            new() { Action = StepAction.Press, Value = "Tab" },
            new() { Action = StepAction.Click, Target = By.Role("button", "Save \"now\"", exact: true) },
            new() { Action = StepAction.ExpectText, Target = By.TestId("status"), Expected = "Saved" },
        ];

        Assert.Equal("""
            .Interaction("recorded", i => i
                .Fill(By.Label("Email"), "ada@example.com")
                .Tab(2)
                .Click(By.Role("button", "Save \"now\"", exact: true))
                .ExpectText(By.TestId("status"), "Saved"))
            """.Replace("\r\n", "\n"), InteractionCode.ToCSharp("recorded", steps));
    }

    [Theory]
    [InlineData("What's new banner", "whats-new-banner")]
    [InlineData("Don’t: icon actions", "dont-icon-actions")]
    public void Apostrophes_do_not_split_ids(string name, string id) => Assert.Equal(id, Naming.ToKebabCase(name));
}
