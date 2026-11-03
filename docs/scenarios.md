# Scenario authoring

Scenarios are reusable, reproducible definitions of component state. One definition drives the interactive preview, documentation, bUnit tests, Playwright interaction tests, accessibility scans and visual baselines.

## Scenario sets

```csharp
public sealed class ButtonScenarios : ScenarioSet<Button>
{
    protected override void Configure(ScenarioSetBuilder<Button> set)
    {
        set.Title("Inputs/Button")           // catalog path; default: namespace (relative to the assembly) + name
           .Description("Triggers actions.") // overrides the XML <summary> on the docs page
           .Category("Inputs")
           .Tags("inputs")
           .Documentation("Use **one** primary button per view.")   // Markdown, rendered safely
           .Deprecated("Use ActionButton.");                         // optional

        set.Setup(s => s.Parameter(b => b.Size, Size.Medium));     // applied to every scenario first
        set.Add("Primary", s => s.ChildContent("Save"));
        set.Add("Danger", s => s.Parameter(b => b.Variant, ButtonVariant.Danger).ChildContent("Delete"));
    }
}
```

Discovery rules:

- Workbench discovers **public** non-abstract `ScenarioSet<T>` and `IScenarioProvider` types that have a public parameterless constructor. It scans the host's entry assembly and the assemblies you register with `AddAssembly*`. Private or internal sets (for example, test helpers) must be registered with `options.AddScenarios(...)`.
- Several sets for the same component are merged.
- `[WorkbenchIgnore]` excludes a component or a set.
- Closed generic components work: `ScenarioSet<DataTable<Person>>`. Open generic types are skipped with an explanation (`WB0004`).

### Identifiers

| Item | Format | Example |
| --- | --- | --- |
| Component id | kebab-case title | `inputs-button` |
| Scenario key | kebab-case name, or `.Key("...")` | `primary` |
| Scenario id | `{component}--{key}` | `inputs-button--primary` |

Ids appear in URLs, reports and baseline paths. Keep them stable: renaming a scenario changes its id. To rename only the display name, add `.Key("old-key")`.

## Parameters

```csharp
set.Add("Examples", s => s
    .Parameter(c => c.Title, "Hello")                       // literal (editable in Controls when serializable)
    .Parameter(c => c.Count, null)                          // explicit null
    .Parameter("aria-label", "Close")                       // by name (CaptureUnmatchedValues)
    .ParameterFrom(c => c.Items, ctx => MakeItems(ctx.Random), "Generated items") // new instance per activation
    .ChildContent("Text content")                           // RenderFragment from text (editable)
    .ChildContent(builder => builder.AddMarkupContent(0, "<b>Bold</b>"), "Bold text")
    .Fragment(c => c.Footer, "Footer text")                 // other RenderFragment parameters
    .Template(c => c.RowTemplate, person => b => b.AddContent(0, person.Name), "Name cell"));
```

- Mutable objects must come from `ParameterFrom` or fixtures, so scenarios never share state.
- `EventCallback` parameters that the scenario does not set are instrumented automatically. Their calls appear in **Actions** and `ExpectCallback` can assert on them.
- Bound pairs (`Value` + `ValueChanged`) are wired like `@bind-Value`: changes inside the component update the value. `ValueExpression` is generated, so `InputBase` components work.
- Components that need a cascading `EditContext` (form inputs) receive one automatically.

### Fixtures

Fixtures are named factories for complex types. They can be selected in the Controls panel:

```csharp
set.Fixture<IReadOnlyList<Person>>("Three people", ctx => SampleData.People(ctx.Random, 3), "Seeded sample");
set.Fixture<IReadOnlyList<Person>>("Nobody", _ => []);
set.Add("Few rows", s => s.Fixture(t => t.Items, "Three people"));
```

## Scenario context

Factories receive a `ScenarioContext` that is created for each activation:

| Member | Purpose |
| --- | --- |
| `Random` | Seeded with `.Seed(n)` or a stable hash of the scenario id, so data is the same on every run. |
| `Time` | The scenario clock (fixed with `.Clock(...)`). |
| `Gates` | Named latches for controlled async completion. |
| `Activity` | Tracks pending work for readiness. |
| `Culture` | The scenario culture. |
| `Services` | The preview's service provider (available while rendering). |

## Services and cascading values

```csharp
s.Service<IProductService>(ctx => new FakeProductService(SampleData.Products))  // instance per activation
 .Service<IClock, FakeClock>()                                                   // type activation (with DI)
 .Services((services, ctx) => services.AddScoped<ICart, MemoryCart>())          // full control
 .Cascade(new ThemeInfo("dark"))                                                 // immutable value
 .CascadeFrom(ctx => new Session(ctx.Random), name: "session");                  // per-activation value
```

Overrides apply only to the preview session that activates the scenario; see [environment](environment.md#services). `TimeProvider` cannot be replaced this way. Use `.Clock(...)` instead (`WB1022`).

## Environment

```csharp
s.Http(h => h.Get("/api/items").RespondJson(items).Delay(TimeSpan.FromMilliseconds(200)))
 .User("Ada", "Admin")   // or .Anonymous() or .User(u => u.Name("Ada").Role("Admin").Claim("tenant", "x"))
 .Culture("fa-IR")       // CurrentCulture + CurrentUICulture; direction follows unless set
 .Direction(TextDirection.Rtl)
 .Theme("dark")
 .Viewport("mobile")
 .Clock(new DateTimeOffset(2026, 3, 20, 9, 30, 0, TimeSpan.Zero))
 .Seed(42)
 .Offline();
```

For details, see [environment simulation](environment.md).

## Layout and readiness

- `.Layout(ScenarioLayout.Padded | Centered | Fullscreen)`. Use `Fullscreen` for overlays and dialogs. Visual checks then capture the viewport instead of the root element.
- `.ReadyWhen(".chart[data-loaded]")` adds a selector that must match before the preview counts as ready. You need it rarely: readiness already waits for tracked work (fake HTTP), DOM quiescence, fonts and images.

## Interaction tests

```csharp
s.Interaction("submits valid data", i => i
    .Fill(By.Label("Email"), "ada@example.com")
    .Check(By.Label("I accept the terms"))
    .Click(By.Role("button", "Create account"))
    .ExpectVisible(By.Text("Thanks"))
    .ExpectCallback("OnSubmit", times: 1)
    .ExpectNoErrors()
    .Checkpoint("submitted"));     // extra visual baseline in --visual runs
```

| Locators | Actions | Assertions |
| --- | --- | --- |
| `By.Role(role, name)`, `By.Label`, `By.Text`, `By.Placeholder`, `By.TestId`, `By.Css` | `Click`, `DoubleClick`, `Hover`, `Focus`, `Blur`, `Fill`, `Type`, `Press`, `Tab(n)`, `Check`, `Uncheck`, `Select`, `ReleaseGate`, `FailGate`, `WaitFor`, `Checkpoint` | `ExpectVisible`, `ExpectHidden`, `ExpectText` (contains), `ExpectValue`, `ExpectAttribute`, `ExpectFocused`, `ExpectCount`, `ExpectCallback`, `ExpectNoErrors` |

Locators are strict: an action fails when a locator matches more than one element. Each interaction test starts from a freshly activated scenario.

## Test options

```csharp
s.Visual(v => v.Viewports("mobile", "laptop").Themes("light", "dark").Threshold(0.15).Ignore(".timestamp").FullPage())
 .Accessibility(a => a.DisableRule("color-contrast", "Brand palette approved by design review #123"))
 .Budget(readyWithin: TimeSpan.FromSeconds(1), interactionWithin: TimeSpan.FromSeconds(2), maxDomUpdates: 20)
 .SkipTests("Intentionally failing demo");
```

## Custom scenario providers

Use a provider for data-driven or generated scenarios:

```csharp
public sealed class ToneMatrixProvider : IScenarioProvider
{
    public IEnumerable<ScenarioSetDefinition> GetScenarioSets()
    {
        var set = new ScenarioSetBuilder<Badge>(typeof(ToneMatrixProvider));
        foreach (var tone in Enum.GetValues<Tone>())
            set.Add($"Tone {tone}", s => s.Parameter(b => b.Text, tone.ToString()).Parameter(b => b.Tone, tone));
        yield return set.Build();
    }
}
```

## Validation

`workbench validate`, the overview page and every test run validate scenarios without rendering them. Validation reports unknown parameters, type mismatches, missing required parameters, missing cascading values and services, invalid cultures, viewports and themes, malformed HTTP rules, unknown callbacks in interactions, and service configuration failures. See the [diagnostic codes](diagnostics-codes.md).

## Hot reload

With `dotnet watch`, editing a scenario rebuilds the catalog and reloads the preview. One exception: a scenario that overrides a service type that no scenario overrode at startup needs a host restart, and the preview reports this.
