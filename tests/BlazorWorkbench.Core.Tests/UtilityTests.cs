using System.Text.Json;
using BlazorWorkbench.Catalog;
using BlazorWorkbench.Contracts;
using BlazorWorkbench.Core.Tests.Components;
using BlazorWorkbench.Diagnostics;
using BlazorWorkbench.Documentation;
using BlazorWorkbench.Values;

namespace BlazorWorkbench.Core.Tests;

public class NamingTests
{
    [Theory]
    [InlineData("Primary Button", "primary-button")]
    [InlineData("PrimaryButton", "primary-button")]
    [InlineData("DataGrid<Person>", "data-grid-person")]
    [InlineData("HTMLParser", "html-parser")]
    [InlineData("Inputs/Text field", "inputs-text-field")]
    [InlineData("  --weird__name--  ", "weird-name")]
    [InlineData("Persian (RTL)", "persian-rtl")]
    public void Kebab_case(string input, string expected) => Assert.Equal(expected, Naming.ToKebabCase(input));

    [Fact]
    public void Formats_type_names_as_csharp()
    {
        Assert.Equal("int?", Naming.FormatTypeName(typeof(int?)));
        Assert.Equal("List<string>", Naming.FormatTypeName(typeof(List<string>)));
        Assert.Equal("Dictionary<string, int[]>", Naming.FormatTypeName(typeof(Dictionary<string, int[]>)));
    }

    [Theory]
    [InlineData("inputs-button--primary", "inputs-*", true)]
    [InlineData("inputs-button--primary", "*--primary", true)]
    [InlineData("inputs-button--primary", "data-*", false)]
    [InlineData("inputs-button--primary", "INPUTS-BUTTON--PRIMARY", true)]
    [InlineData("abc", "a?c", true)]
    public void Glob_matching(string text, string pattern, bool expected) => Assert.Equal(expected, Glob.IsMatch(text, pattern));

    [Fact]
    public void Stable_hash_is_deterministic() => Assert.Equal(StableHash.Compute("abc"), StableHash.Compute("abc"));
}

public class ValueConversionTests
{
    private static JsonElement J(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Theory]
    [InlineData("\"42\"", typeof(int), 42)]
    [InlineData("42", typeof(int), 42)]
    [InlineData("\"true\"", typeof(bool), true)]
    [InlineData("\"Danger\"", typeof(Tone), Tone.Danger)]
    [InlineData("\"danger\"", typeof(Tone), Tone.Danger)]
    [InlineData("\"Bold, Italic\"", typeof(Styles), Styles.Bold | Styles.Italic)]
    [InlineData("\"1.5\"", typeof(double), 1.5)]
    public void Converts_supported_values(string json, Type type, object expected)
    {
        Assert.True(ParameterValueConverter.TryConvert(J(json), type, out var value, out var error), error);
        Assert.Equal(expected, value);
    }

    [Fact]
    public void Decimal_uses_invariant_culture()
    {
        Assert.True(ParameterValueConverter.TryConvert(J("\"1.25\""), typeof(decimal), out var value, out _));
        Assert.Equal(1.25m, value);
    }

    [Theory]
    [InlineData("\"abc\"", typeof(int), "is not a valid int")]
    [InlineData("\"99999999999\"", typeof(int), "outside the range")]
    [InlineData("\"Purple\"", typeof(Tone), "is not a value of Tone")]
    [InlineData("null", typeof(int), "does not accept null")]
    [InlineData("\"ab\"", typeof(char), "exactly one character")]
    public void Rejects_invalid_values_with_explanations(string json, Type type, string message)
    {
        Assert.False(ParameterValueConverter.TryConvert(J(json), type, out _, out var error));
        Assert.Contains(message, error);
    }

    [Fact]
    public void Null_is_distinct_for_nullable_types()
    {
        Assert.True(ParameterValueConverter.TryConvert(J("null"), typeof(int?), out var value, out _));
        Assert.Null(value);
    }

    [Fact]
    public void Dates_round_trip()
    {
        var date = new DateTimeOffset(2026, 3, 1, 10, 0, 0, TimeSpan.FromHours(3.5));
        Assert.True(ParameterValueConverter.TrySerialize(date, out var json));
        Assert.True(ParameterValueConverter.TryConvert(json, typeof(DateTimeOffset), out var back, out _));
        Assert.Equal(date, back);
        Assert.False(ParameterValueConverter.TrySerialize(new List<int>(), out _));
    }

    [Fact]
    public void Args_codec_preserves_null_unset_fixture_and_values()
    {
        var args = new Dictionary<string, ArgOverride>
        {
            ["Text"] = ArgOverride.FromValue(J("\"Hi\"")),
            ["Count"] = ArgOverride.FromValue(J("3")),
            ["Icon"] = ArgOverride.Null,
            ["Size"] = ArgOverride.Unset,
            ["Items"] = ArgOverride.FromFixture("Many"),
        };
        var encoded = ArgsCodec.Encode(args);
        Assert.Equal("{\"Count\":3,\"Icon\":null,\"Items\":{\"$fixture\":\"Many\"},\"Size\":{\"$unset\":true},\"Text\":\"Hi\"}", encoded);

        var decoded = ArgsCodec.Decode(encoded);
        Assert.Equal(ArgKind.Null, decoded["Icon"].Kind);
        Assert.Equal(ArgKind.Unset, decoded["Size"].Kind);
        Assert.Equal("Many", decoded["Items"].Fixture);
        Assert.Equal("Hi", decoded["Text"].Value!.Value.GetString());
        Assert.Throws<FormatException>(() => ArgsCodec.Decode("[1]"));
    }
}

public class RedactionTests
{
    private static readonly Redactor R = Redactor.Default;

    [Fact]
    public void Redacts_bearer_tokens_and_jwts()
    {
        Assert.Equal("Authorization: Bearer [REDACTED]", R.RedactText("Authorization: Bearer abc.def-123"));
        Assert.DoesNotContain("eyJ", R.RedactText("token eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.dozjgNryP4J3jVmNHl0w5N_XgL0n3I9PlFUP0THsR8U"));
    }

    [Fact]
    public void Redacts_key_value_secrets_but_keeps_ordinary_text()
    {
        Assert.Equal("password=[REDACTED]&user=ada", R.RedactText("password=hunter2&user=ada"));
        Assert.Equal("{\"apiKey\": \"[REDACTED]\"}", R.RedactText("{\"apiKey\": \"sk-123\"}"));
        Assert.Equal("Server=x;Password=[REDACTED];", R.RedactText("Server=x;Password=p@ss;"));
        Assert.Equal("Response status code: 404", R.RedactText("Response status code: 404"));
    }

    [Fact]
    public void Redacts_urls()
    {
        Assert.Equal("https://api.example.com/x?access_token=%5BREDACTED%5D&page=2", R.RedactUrl("https://api.example.com/x?access_token=abc&page=2"));
        Assert.Equal("https://[REDACTED]@host/x", R.RedactUrl("https://user:pw@host/x"));
        Assert.Equal("/api/items?code=%5BREDACTED%5D", R.RedactUrl("/api/items?code=xyz"));
    }

    [Fact]
    public void Redacts_sensitive_headers() => Assert.Equal("[REDACTED]", R.RedactHeader("Cookie", "session=1"));

    [Fact]
    public void Custom_patterns_apply()
    {
        var options = new RedactionOptions();
        options.Patterns.Add(new System.Text.RegularExpressions.Regex(@"\d{4}-\d{4}-\d{4}-\d{4}"));
        Assert.Equal("card [REDACTED]", new Redactor(options).RedactText("card 4111-1111-1111-1111"));
    }

    [Fact]
    public void Diagnostic_log_is_bounded_and_redacted()
    {
        var log = new DiagnosticLog(10, R);
        for (var i = 0; i < 25; i++)
            log.Add(DiagnosticSource.Console, DiagnosticSeverity.Info, "s", $"message {i} token=secret{i}");
        var items = log.Snapshot();
        Assert.Equal(10, items.Count);
        Assert.Equal(15, log.Dropped);
        Assert.All(items, d => Assert.DoesNotContain("secret", d.Message));
    }
}

public class MarkdownTests
{
    [Fact]
    public void Renders_common_markdown()
    {
        var html = SafeMarkdown.ToHtml("# Title\n\nSome **bold** and *italic* with `code`.\n\n- one\n- two\n\n```csharp\nvar x = 1;\n```");
        Assert.Contains("<h1 id=\"title\">Title</h1>", html);
        Assert.Contains("<strong>bold</strong>", html);
        Assert.Contains("<em>italic</em>", html);
        Assert.Contains("<code>code</code>", html);
        Assert.Contains("<ul><li>one</li><li>two</li></ul>", html);
        Assert.Contains("<pre><code class=\"language-csharp\">var x = 1;</code></pre>", html);
    }

    [Fact]
    public void Renders_tables() =>
        Assert.Contains("<table><thead><tr><th>A</th><th>B</th></tr></thead><tbody><tr><td>1</td><td>2</td></tr></tbody></table>", SafeMarkdown.ToHtml("| A | B |\n| --- | --- |\n| 1 | 2 |"));

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("<img src=x onerror=alert(1)>")]
    [InlineData("[click](javascript:alert(1))")]
    [InlineData("[click](data:text/html,<script>alert(1)</script>)")]
    [InlineData("```\n</code><script>alert(1)</script>\n```")]
    public void Never_emits_executable_html(string markdown)
    {
        var html = SafeMarkdown.ToHtml(markdown);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("href=\"javascript", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("href=\"data", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Allows_safe_links() =>
        Assert.Contains("<a href=\"https://example.com\" rel=\"noopener noreferrer\" target=\"_blank\">site</a>", SafeMarkdown.ToHtml("[site](https://example.com)"));
}

public class HttpRuleMatcherTests
{
    private static readonly Uri Base = new(HttpSimulation.DefaultBaseAddress);

    [Theory]
    [InlineData("/api/items", "api/items", true)]
    [InlineData("/api/items/", "api/items", true)]
    [InlineData("/api/items", "api/items/1", false)]
    [InlineData("/api/items/*", "api/items/1", true)]
    [InlineData("/api/*", "api/items/1", false)]
    [InlineData("/api/**", "api/items/1", true)]
    [InlineData("/api/weather?city=Tehran", "api/weather?city=Tehran&units=c", true)]
    [InlineData("/api/weather?city=Paris", "api/weather?city=Tehran", false)]
    [InlineData("/api/weather?city=*", "api/weather?city=Tehran", true)]
    [InlineData("https://other.example/api/x", "api/x", false)]
    public void Matches_patterns(string pattern, string request, bool expected) =>
        Assert.Equal(expected, HttpRuleMatcher.IsMatch(pattern, new Uri(Base, request)));

    [Fact]
    public void Absolute_patterns_match_host() =>
        Assert.True(HttpRuleMatcher.IsMatch("https://api.example.com/v1/*", new Uri("https://api.example.com/v1/users")));

    [Fact]
    public void Method_must_match()
    {
        var rule = new HttpRule { Method = "POST", Pattern = "/x" };
        Assert.False(HttpRuleMatcher.IsMatch(rule, "GET", new Uri(Base, "x")));
        Assert.True(HttpRuleMatcher.IsMatch(rule, "post", new Uri(Base, "x")));
    }
}

public class GateTests
{
    [Fact]
    public async Task Waiters_complete_when_released_and_gates_latch()
    {
        var gates = new ScenarioGates();
        var wait = gates.WaitAsync("load");
        Assert.False(wait.IsCompleted);
        Assert.Equal(GateStatus.Pending, gates.Snapshot().Single().Status);

        Assert.True(gates.Release("load"));
        await wait;
        await gates.WaitAsync("load"); // latched
        Assert.False(gates.Release("load"));
        Assert.Equal(GateStatus.Released, gates.Snapshot().Single().Status);
    }

    [Fact]
    public async Task Failed_gate_throws_and_wait_honors_cancellation()
    {
        var gates = new ScenarioGates();
        var wait = gates.WaitAsync("x");
        gates.Fail("x", "offline");
        var ex = await Assert.ThrowsAsync<GateFailedException>(() => wait);
        Assert.Equal("x", ex.Gate);

        using var cts = new CancellationTokenSource();
        var pending = gates.WaitAsync("y", cts.Token);
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(0, gates.Snapshot().Single(g => g.Name == "y").Waiters);
    }

    [Fact]
    public async Task Activity_tracker_counts_pending_operations()
    {
        var tracker = new ScenarioActivityTracker();
        var tcs = new TaskCompletionSource<int>();
        var tracked = tracker.Track(tcs.Task);
        Assert.Equal(1, tracker.Pending);
        tcs.SetResult(5);
        Assert.Equal(5, await tracked);
        Assert.Equal(0, tracker.Pending);

        var handle = tracker.Begin();
        handle.Dispose();
        handle.Dispose(); // idempotent
        Assert.Equal(0, tracker.Pending);
    }
}

public class DocumentationTests
{
    private static (ScenarioDefinition Scenario, ComponentMetadata Metadata) Get(string id)
    {
        var options = new WorkbenchOptions();
        options.AddAssemblyOf<TestButton>();
        var catalog = CatalogBuilder.Build(options);
        var scenario = catalog.FindScenario(id)!;
        return (scenario, catalog.GetComponentOf(scenario)!.Metadata);
    }

    [Fact]
    public void Razor_snippet_reflects_parameters()
    {
        var (scenario, metadata) = Get("inputs-test-button--danger");
        Assert.Equal("<TestButton Label=\"Save\" Tone=\"Tone.Danger\" />", RazorSnippetGenerator.Generate(scenario, metadata));
    }

    [Fact]
    public void Razor_snippet_applies_overrides_and_generic_arguments()
    {
        var (scenario, metadata) = Get("inputs-test-button--primary");
        var snippet = RazorSnippetGenerator.Generate(scenario, metadata, new Dictionary<string, ArgOverride>
        {
            ["Disabled"] = ArgOverride.FromValue(JsonSerializer.SerializeToElement(true)),
            ["Label"] = ArgOverride.FromValue(JsonSerializer.SerializeToElement("Say \"hi\" @me")),
        });
        Assert.Contains("Disabled=\"true\"", snippet);
        Assert.Contains("Label=\"Say &quot;hi&quot; @@me\"", snippet);

        var (generic, genericMetadata) = Get("components-generic-int--numbers");
        Assert.Equal("<Generic TItem=\"int\" Items=\"@items\" />", RazorSnippetGenerator.Generate(generic, genericMetadata));
    }

    [Fact]
    public void Exports_overrides_as_csharp_scenario()
    {
        var (scenario, metadata) = Get("inputs-test-button--primary");
        var code = ScenarioExporter.ToCSharp(scenario, metadata, new Dictionary<string, ArgOverride>
        {
            ["Tone"] = ArgOverride.FromValue(JsonSerializer.SerializeToElement("Danger")),
            ["Count"] = ArgOverride.Null,
            ["Items"] = ArgOverride.FromFixture("Three"),
        }, "Edited");
        Assert.Contains("set.Add(\"Edited\", s => s", code);
        Assert.Contains(".Parameter(c => c.Label, \"Save\")", code);
        Assert.Contains(".Parameter(c => c.Tone, Tone.Danger)", code);
        Assert.Contains(".Parameter(c => c.Count, null)", code);
        Assert.Contains(".Fixture(c => c.Items, \"Three\")", code);
    }

    [Fact]
    public void Xml_documentation_is_normalized()
    {
        var docs = XmlDocumentation.Parse("""
            <doc><members>
              <member name="T:A.B"><summary>Uses <see cref="T:System.String"/> and <c>code</c>, or <see langword="null"/>.</summary></member>
            </members></doc>
            """);
        Assert.Equal("Uses `String` and `code`, or `null`.", docs.GetSummary("T:A.B"));
        Assert.Null(docs.GetSummary("T:Missing"));
    }

    [Fact]
    public void Skeleton_generator_suggests_boundaries()
    {
        var options = new WorkbenchOptions();
        options.AddAssemblyOf<TestButton>();
        var dto = CatalogMapper.ToDto(CatalogBuilder.Build(options)).Components.Single(c => c.Name == "TestButton");
        var suggestions = ScenarioSkeletonGenerator.Suggest(dto);
        Assert.Contains(suggestions, s => s.Name == "Disabled enabled");
        Assert.Contains(suggestions, s => s.Name == "Long Label");
        Assert.Contains(suggestions, s => s.Name == "Right to left");
        Assert.DoesNotContain(suggestions, s => s.Name == "Danger");
        var code = ScenarioSkeletonGenerator.GenerateSet(dto, suggestions);
        Assert.Contains("public sealed class TestButtonScenarios : ScenarioSet<TestButton>", code);
    }

    [Fact]
    public void Catalog_contract_round_trips_through_json()
    {
        var options = new WorkbenchOptions();
        options.AddAssemblyOf<TestButton>();
        var dto = CatalogMapper.ToDto(CatalogBuilder.Build(options), version: "1.2.3");
        var json = JsonSerializer.Serialize(dto, WorkbenchJson.Options);
        var back = JsonSerializer.Deserialize<CatalogDto>(json, WorkbenchJson.Options)!;
        Assert.Equal(dto.Components.Count, back.Components.Count);
        var scenario = back.Scenarios.Single(s => s.Id == "inputs-test-button--primary");
        Assert.Equal("Save", scenario.Args["Label"].GetString());
        Assert.Equal(StepAction.Click, scenario.Interactions[0].Steps[0].Action);
        Assert.Equal(LocatorKind.Role, scenario.Interactions[0].Steps[0].Target!.Kind);
    }

    [Fact]
    public void Workbench_config_resolves_relative_paths()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            File.WriteAllText(Path.Combine(dir.FullName, "workbench.json"), "{ // comment\n \"project\": \"app/App.csproj\", \"workers\": 3, }");
            var nested = Directory.CreateDirectory(Path.Combine(dir.FullName, "a", "b"));
            var config = WorkbenchConfig.Find(nested.FullName)!;
            Assert.Equal(3, config.Workers);
            Assert.Equal(Path.Combine(dir.FullName, "app", "App.csproj"), config.Resolve(config.Project!));
            Assert.Equal(Path.Combine(dir.FullName, "workbench-baselines"), config.Resolve(config.Baselines));
        }
        finally
        {
            dir.Delete(true);
        }
    }
}
