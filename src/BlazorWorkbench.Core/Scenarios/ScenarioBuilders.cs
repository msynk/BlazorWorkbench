using System.Linq.Expressions;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BlazorWorkbench;

/// <summary>Builds the scenarios of one component. Passed to <see cref="ScenarioSet{TComponent}.Configure"/>.</summary>
public sealed class ScenarioSetBuilder<TComponent> where TComponent : IComponent
{
    private readonly Type? _declaringType;
    private readonly List<Action<ScenarioBuilder<TComponent>>> _setups = [];
    private readonly List<(string Name, Action<ScenarioBuilder<TComponent>> Configure, MatrixCell? Cell)> _scenarios = [];
    private readonly List<FixtureDefinition> _fixtures = [];
    private readonly List<DesignReference> _designs = [];
    private readonly List<string> _tags = [];
    private string? _title, _description, _category, _documentation, _deprecation;
    private ComponentStatus? _status;

    public ScenarioSetBuilder(Type? declaringType = null) => _declaringType = declaringType;

    /// <summary>Catalog path such as <c>Inputs/Button</c>. Defaults to the namespace (relative to the assembly) and component name.</summary>
    public ScenarioSetBuilder<TComponent> Title(string title) { _title = title; return this; }

    public ScenarioSetBuilder<TComponent> Description(string description) { _description = description; return this; }
    public ScenarioSetBuilder<TComponent> Category(string category) { _category = category; return this; }
    public ScenarioSetBuilder<TComponent> Tags(params string[] tags) { _tags.AddRange(tags); return this; }

    /// <summary>Markdown documentation shown on the component documentation page (rendered without raw HTML).</summary>
    public ScenarioSetBuilder<TComponent> Documentation(string markdown) { _documentation = markdown; return this; }

    public ScenarioSetBuilder<TComponent> Deprecated(string message) { _deprecation = message; return this; }

    /// <summary>Sets the lifecycle status (draft, experimental, beta, stable or deprecated) shown in the explorer and documentation.</summary>
    public ScenarioSetBuilder<TComponent> Status(ComponentStatus status) { _status = status; return this; }

    /// <summary>
    /// Links a design artifact to every scenario of the component: an image (shown in the Design panel and as a pixel overlay on
    /// the preview), a Figma file or frame (embedded), or any other page (linked). Relative URLs resolve against the host.
    /// </summary>
    public ScenarioSetBuilder<TComponent> Design(string url, string? title = null) { _designs.Add(DesignReference.Create(url, title)); return this; }

    /// <summary>Registers a named value factory that the parameter inspector offers for parameters of type <typeparamref name="TValue"/>.</summary>
    public ScenarioSetBuilder<TComponent> Fixture<TValue>(string name, Func<ScenarioContext, TValue> factory, string? description = null)
    {
        _fixtures.Add(new FixtureDefinition { Name = name, ValueType = typeof(TValue), Factory = ctx => factory(ctx), Description = description });
        return this;
    }

    /// <summary>Shared configuration applied to every scenario before the scenario's own configuration.</summary>
    public ScenarioSetBuilder<TComponent> Setup(Action<ScenarioBuilder<TComponent>> setup) { _setups.Add(setup); return this; }

    /// <summary>Adds a scenario. The key defaults to the kebab-case name; override with <see cref="ScenarioBuilder{TComponent}.Key"/>.</summary>
    public ScenarioSetBuilder<TComponent> Add(string name, Action<ScenarioBuilder<TComponent>>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _scenarios.Add((name, configure ?? (_ => { }), null));
        return this;
    }

    /// <summary>
    /// Adds one scenario per combination of parameter values, for example every <c>Variant</c> in every <c>Size</c>. Each
    /// combination is a regular scenario (previewed, screenshotted and scanned) named <c>{name} · value · value</c> and tagged
    /// <c>matrix</c>; the gallery shows a matrix as a table. Use <see cref="MatrixBuilder{TComponent}.Pairwise"/> to cover every
    /// pair of values with fewer scenarios.
    /// </summary>
    /// <param name="name">Matrix name, used as the prefix of the scenario names.</param>
    /// <param name="axes">The parameters to vary and their values.</param>
    /// <param name="configure">Configuration applied to every combination before its values (child content, services, ...).</param>
    public ScenarioSetBuilder<TComponent> Matrix(string name, Action<MatrixBuilder<TComponent>> axes, Action<ScenarioBuilder<TComponent>>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(axes);
        var matrix = new MatrixBuilder<TComponent>();
        axes(matrix);
        foreach (var combination in matrix.Combinations())
        {
            var values = combination.Select(c => new KeyValuePair<string, string>(c.Parameter, c.Display)).ToArray();
            var cell = new MatrixCell(name, values);
            var scenarioName = $"{name} · {string.Join(" · ", combination.Select(c => c.Label))}";
            _scenarios.Add((scenarioName, s =>
            {
                configure?.Invoke(s);
                foreach (var choice in combination)
                    s.Parameter(choice.Parameter, choice.Value);
                s.Tags("matrix");
            }, cell));
        }

        return this;
    }

    public ScenarioSetDefinition Build()
    {
        var scenarios = new List<ScenarioDefinition>(_scenarios.Count);
        foreach (var (name, configure, cell) in _scenarios)
        {
            var builder = new ScenarioBuilder<TComponent>(name, _declaringType) { InSetup = true, MatrixCell = cell };
            foreach (var setup in _setups)
                setup(builder);
            builder.InSetup = false;
            configure(builder);
            scenarios.Add(builder.Build(_fixtures));
        }

        return new ScenarioSetDefinition
        {
            ComponentType = typeof(TComponent),
            DeclaringType = _declaringType,
            Title = _title,
            Description = _description,
            Category = _category,
            Tags = _tags.ToArray(),
            Documentation = _documentation,
            DeprecationMessage = _deprecation,
            Status = _status,
            Designs = _designs.ToArray(),
            Fixtures = _fixtures.ToArray(),
            Scenarios = scenarios,
        };
    }
}

/// <summary>Configures one scenario. All methods return the builder for chaining.</summary>
public sealed class ScenarioBuilder<TComponent> where TComponent : IComponent
{
    private readonly string _name;
    private readonly Type? _declaringType;
    private readonly List<ParameterAssignment> _parameters = [];
    private readonly List<CascadingValueDefinition> _cascading = [];
    private readonly List<Action<IServiceCollection, ScenarioContext>> _services = [];
    private readonly List<HttpRule> _httpRules = [], _setupHttpRules = [];
    private readonly List<InteractionTest> _interactions = [];
    private readonly List<string> _tags = [];
    private readonly List<string> _ignore = [], _visualViewports = [], _visualThemes = [], _disabledRules = [];
    private readonly List<DesignReference> _designs = [];
    private readonly List<PseudoStateRule> _pseudoStates = [];
    private readonly List<StorageEntry> _storage = [];
    private string? _key, _description, _culture, _theme, _viewport, _readySelector, _skip, _visualSkip, _a11ySkip, _a11yJustification, _notes;
    private TextDirection _direction = TextDirection.Auto;
    private SimulatedUser? _user;
    private TimeSpan _httpLatency;
    private HttpStatusCode _unmatched = HttpStatusCode.NotFound;
    private bool _httpConfigured, _offline, _fullPage, _visualSkipSet, _a11ySkipSet, _forcedColors, _print;
    private string? _a11yTodo;
    private DateTimeOffset? _clock;
    private int? _seed;
    private ScenarioLayout _layout = ScenarioLayout.Padded;
    private double? _threshold, _maxDiffRatio;
    private TimeSpan? _readyWithin, _interactionWithin;
    private int? _maxDomUpdates;

    internal ScenarioBuilder(string name, Type? declaringType)
    {
        _name = name;
        _declaringType = declaringType;
    }

    internal bool InSetup { get; set; }

    internal MatrixCell? MatrixCell { get; init; }

    /// <summary>Overrides the scenario key (the part after <c>--</c> in the scenario id). Keep keys stable; they are used in URLs and baselines.</summary>
    public ScenarioBuilder<TComponent> Key(string key) { _key = Naming.ToKebabCase(key); return this; }

    public ScenarioBuilder<TComponent> Description(string description) { _description = description; return this; }
    public ScenarioBuilder<TComponent> Tags(params string[] tags) { _tags.AddRange(tags); return this; }

    /// <summary>Markdown notes shown under the scenario title and on the documentation page: usage hints, review notes, known issues.</summary>
    public ScenarioBuilder<TComponent> Notes(string markdown) { _notes = markdown; return this; }

    /// <summary>
    /// Links a design artifact to the scenario: an image (shown in the Design panel and available as a pixel overlay on the
    /// preview), a Figma file or frame (embedded), or any other page (linked). Relative URLs resolve against the host.
    /// </summary>
    public ScenarioBuilder<TComponent> Design(string url, string? title = null) { _designs.Add(DesignReference.Create(url, title)); return this; }

    /// <summary>
    /// Forces a CSS pseudo-class such as <c>:hover</c> or <c>:focus-visible</c> without user input, on every element of the
    /// preview or on the elements matching <paramref name="selector"/>. Screenshots and accessibility scans then cover the state.
    /// </summary>
    public ScenarioBuilder<TComponent> ForceState(PseudoState state, string? selector = null)
    {
        var rule = new PseudoStateRule(state, selector);
        if (!_pseudoStates.Contains(rule))
            _pseudoStates.Add(rule);
        return this;
    }

    /// <summary>
    /// Writes <paramref name="value"/> to <c>localStorage</c> before the component starts. Strings are stored as they are, other
    /// values as JSON. The preview clears its storage first, so every scenario starts from the same state.
    /// </summary>
    public ScenarioBuilder<TComponent> LocalStorage(string key, object? value) => Storage(StorageArea.Local, key, value);

    /// <summary>Writes <paramref name="value"/> to <c>sessionStorage</c> before the component starts (strings as they are, other values as JSON).</summary>
    public ScenarioBuilder<TComponent> SessionStorage(string key, object? value) => Storage(StorageArea.Session, key, value);

    private ScenarioBuilder<TComponent> Storage(StorageArea area, string key, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var text = value as string ?? JsonSerializer.Serialize(value, HttpRuleBuilder.WebJson);
        _storage.RemoveAll(e => e.Area == area && e.Key == key);
        _storage.Add(new StorageEntry(area, key, text));
        return this;
    }

    /// <summary>Sets a parameter to a literal value.</summary>
    public ScenarioBuilder<TComponent> Parameter<TValue>(Expression<Func<TComponent, TValue>> parameter, TValue value) =>
        SetParameter(new ParameterAssignment { Name = Naming.GetMemberName(parameter), Kind = ParameterValueKind.Literal, Value = value });

    /// <summary>Sets a parameter by name (useful for attributes captured by <c>CaptureUnmatchedValues</c>).</summary>
    public ScenarioBuilder<TComponent> Parameter(string name, object? value) =>
        SetParameter(new ParameterAssignment { Name = name, Kind = ParameterValueKind.Literal, Value = value });

    /// <summary>Sets a parameter from a factory evaluated on every activation (use it for mutable objects so scenarios never share state).</summary>
    public ScenarioBuilder<TComponent> ParameterFrom<TValue>(Expression<Func<TComponent, TValue>> parameter, Func<ScenarioContext, TValue> factory, string? display = null) =>
        SetParameter(new ParameterAssignment { Name = Naming.GetMemberName(parameter), Kind = ParameterValueKind.Factory, Factory = ctx => factory(ctx), Display = display });

    /// <summary>Sets a parameter from a fixture declared with <see cref="ScenarioSetBuilder{TComponent}.Fixture{TValue}"/>.</summary>
    public ScenarioBuilder<TComponent> Fixture<TValue>(Expression<Func<TComponent, TValue>> parameter, string fixtureName) =>
        SetParameter(new ParameterAssignment { Name = Naming.GetMemberName(parameter), Kind = ParameterValueKind.Factory, FixtureName = fixtureName, Display = fixtureName });

    /// <summary>Sets <c>ChildContent</c> to plain text (editable in the inspector).</summary>
    public ScenarioBuilder<TComponent> ChildContent(string text) =>
        SetParameter(new ParameterAssignment { Name = "ChildContent", Kind = ParameterValueKind.Text, Value = text });

    /// <summary>Sets <c>ChildContent</c> to a render fragment.</summary>
    public ScenarioBuilder<TComponent> ChildContent(RenderFragment fragment, string? display = null) =>
        SetParameter(new ParameterAssignment { Name = "ChildContent", Kind = ParameterValueKind.Factory, Factory = _ => fragment, Display = display ?? "RenderFragment" });

    /// <summary>Sets a <c>RenderFragment</c> parameter to plain text.</summary>
    public ScenarioBuilder<TComponent> Fragment(Expression<Func<TComponent, RenderFragment?>> parameter, string text) =>
        SetParameter(new ParameterAssignment { Name = Naming.GetMemberName(parameter), Kind = ParameterValueKind.Text, Value = text });

    /// <summary>Sets a <c>RenderFragment</c> parameter.</summary>
    public ScenarioBuilder<TComponent> Fragment(Expression<Func<TComponent, RenderFragment?>> parameter, RenderFragment fragment, string? display = null) =>
        SetParameter(new ParameterAssignment { Name = Naming.GetMemberName(parameter), Kind = ParameterValueKind.Factory, Factory = _ => fragment, Display = display ?? "RenderFragment" });

    /// <summary>Sets a templated (<c>RenderFragment&lt;T&gt;</c>) parameter.</summary>
    public ScenarioBuilder<TComponent> Template<TItem>(Expression<Func<TComponent, RenderFragment<TItem>?>> parameter, RenderFragment<TItem> template, string? display = null) =>
        SetParameter(new ParameterAssignment { Name = Naming.GetMemberName(parameter), Kind = ParameterValueKind.Factory, Factory = _ => template, Display = display ?? $"RenderFragment<{Naming.FormatTypeName(typeof(TItem))}>" });

    /// <summary>Configures scenario-specific services. The callback runs on every activation, so new instances are created each time.</summary>
    public ScenarioBuilder<TComponent> Services(Action<IServiceCollection> configure) { _services.Add((s, _) => configure(s)); return this; }

    /// <inheritdoc cref="Services(Action{IServiceCollection})"/>
    public ScenarioBuilder<TComponent> Services(Action<IServiceCollection, ScenarioContext> configure) { _services.Add(configure); return this; }

    /// <summary>Overrides <typeparamref name="TService"/> with an instance created per activation.</summary>
    public ScenarioBuilder<TComponent> Service<TService>(Func<ScenarioContext, TService> factory) where TService : class
    {
        _services.Add((s, ctx) => s.Replace(ServiceDescriptor.Scoped(_ => factory(ctx))));
        return this;
    }

    /// <summary>Overrides <typeparamref name="TService"/> with <typeparamref name="TImplementation"/> (constructed with dependency injection).</summary>
    public ScenarioBuilder<TComponent> Service<TService, TImplementation>() where TService : class where TImplementation : class, TService
    {
        _services.Add((s, _) => s.Replace(ServiceDescriptor.Scoped<TService, TImplementation>()));
        return this;
    }

    /// <summary>Provides a cascading value. The same instance is used for every activation; prefer <see cref="CascadeFrom{T}"/> for mutable objects.</summary>
    public ScenarioBuilder<TComponent> Cascade<T>(T value, string? name = null) =>
        CascadeFrom(_ => value, name);

    public ScenarioBuilder<TComponent> CascadeFrom<T>(Func<ScenarioContext, T> factory, string? name = null)
    {
        _cascading.RemoveAll(c => c.ValueType == typeof(T) && c.Name == name);
        _cascading.Add(new CascadingValueDefinition { ValueType = typeof(T), Name = name, Factory = ctx => factory(ctx) });
        return this;
    }

    /// <summary>Configures the fake HTTP backend used by the injected <see cref="HttpClient"/> and <c>IHttpClientFactory</c>.</summary>
    public ScenarioBuilder<TComponent> Http(Action<HttpSimulationBuilder> configure)
    {
        var builder = new HttpSimulationBuilder();
        configure(builder);
        (InSetup ? _setupHttpRules : _httpRules).AddRange(builder.BuildRules());
        if (builder.LatencyValue is { } latency) _httpLatency = latency;
        if (builder.UnmatchedValue is { } unmatched) _unmatched = unmatched;
        _httpConfigured = true;
        return this;
    }

    /// <summary>Fails every HTTP request with a connection error.</summary>
    public ScenarioBuilder<TComponent> Offline(bool offline = true) { _offline = offline; return this; }

    /// <summary>Simulates an authenticated user with the given roles.</summary>
    public ScenarioBuilder<TComponent> User(string name, params string[] roles) { _user = new SimulatedUser { Name = name, Roles = roles }; return this; }

    public ScenarioBuilder<TComponent> User(Action<SimulatedUserBuilder> configure)
    {
        var builder = new SimulatedUserBuilder();
        configure(builder);
        _user = builder.Build();
        return this;
    }

    public ScenarioBuilder<TComponent> Anonymous() { _user = SimulatedUser.Anonymous; return this; }

    /// <summary>Sets <c>CurrentCulture</c> and <c>CurrentUICulture</c> (for example <c>fa-IR</c>).</summary>
    public ScenarioBuilder<TComponent> Culture(string culture) { _culture = culture; return this; }

    public ScenarioBuilder<TComponent> Direction(TextDirection direction) { _direction = direction; return this; }
    public ScenarioBuilder<TComponent> Theme(string theme) { _theme = theme; return this; }
    public ScenarioBuilder<TComponent> Viewport(string viewport) { _viewport = viewport; return this; }

    /// <summary>Freezes the scenario clock (<see cref="TimeProvider"/>) at <paramref name="now"/>.</summary>
    public ScenarioBuilder<TComponent> Clock(DateTimeOffset now) { _clock = now; return this; }

    public ScenarioBuilder<TComponent> Seed(int seed) { _seed = seed; return this; }
    public ScenarioBuilder<TComponent> Layout(ScenarioLayout layout) { _layout = layout; return this; }

    /// <summary>The preview is ready only after an element matching <paramref name="cssSelector"/> exists.</summary>
    public ScenarioBuilder<TComponent> ReadyWhen(string cssSelector) { _readySelector = cssSelector; return this; }

    /// <summary>Adds an interaction test executed by <c>workbench test</c> and the Interactions panel.</summary>
    public ScenarioBuilder<TComponent> Interaction(string name, Action<InteractionBuilder> steps)
    {
        var builder = new InteractionBuilder();
        steps(builder);
        _interactions.Add(new InteractionTest(name, builder.Steps.ToArray()));
        return this;
    }

    public ScenarioBuilder<TComponent> Visual(Action<VisualOptionsBuilder> configure)
    {
        var b = new VisualOptionsBuilder();
        configure(b);
        if (b.SkipSet) { _visualSkipSet = true; _visualSkip = b.SkipReasonValue; }
        _threshold = b.ThresholdValue ?? _threshold;
        _maxDiffRatio = b.MaxDiffRatioValue ?? _maxDiffRatio;
        _ignore.AddRange(b.IgnoreValues);
        _visualViewports.AddRange(b.ViewportValues);
        _visualThemes.AddRange(b.ThemeValues);
        _fullPage |= b.FullPageValue;
        return this;
    }

    public ScenarioBuilder<TComponent> Accessibility(Action<AccessibilityOptionsBuilder> configure)
    {
        var b = new AccessibilityOptionsBuilder();
        configure(b);
        if (b.SkipSet) { _a11ySkipSet = true; _a11ySkip = b.SkipReasonValue; }
        _disabledRules.AddRange(b.DisabledRuleValues);
        _a11yJustification = b.JustificationValue ?? _a11yJustification;
        _a11yTodo = b.TodoValue ?? _a11yTodo;
        return this;
    }

    /// <summary>
    /// Emulates CSS media features in automated runs: <c>forced-colors: active</c> (Windows high contrast) and the
    /// <c>print</c> media type. Automated runs always emulate <c>prefers-reduced-motion: reduce</c>.
    /// </summary>
    public ScenarioBuilder<TComponent> Media(Action<MediaBuilder> configure)
    {
        var b = new MediaBuilder();
        configure(b);
        _forcedColors |= b.ForcedColorsValue;
        _print |= b.PrintValue;
        return this;
    }

    public ScenarioBuilder<TComponent> Budget(TimeSpan? readyWithin = null, TimeSpan? interactionWithin = null, int? maxDomUpdates = null)
    {
        _readyWithin = readyWithin ?? _readyWithin;
        _interactionWithin = interactionWithin ?? _interactionWithin;
        _maxDomUpdates = maxDomUpdates ?? _maxDomUpdates;
        return this;
    }

    /// <summary>Excludes the scenario from automated test runs (it stays available in the UI).</summary>
    public ScenarioBuilder<TComponent> SkipTests(string reason) { _skip = reason; return this; }

    private ScenarioBuilder<TComponent> SetParameter(ParameterAssignment assignment)
    {
        _parameters.RemoveAll(p => p.Name == assignment.Name);
        _parameters.Add(assignment);
        return this;
    }

    internal ScenarioDefinition Build(IReadOnlyList<FixtureDefinition> fixtures) => new()
    {
        Key = _key ?? Naming.ToKebabCase(_name),
        Name = _name,
        ComponentType = typeof(TComponent),
        DeclaringType = _declaringType,
        Description = _description,
        Tags = _tags.Distinct(StringComparer.Ordinal).ToArray(),
        Parameters = _parameters.ToArray(),
        CascadingValues = _cascading.ToArray(),
        ServiceConfigurations = _services.ToArray(),
        Fixtures = fixtures,
        Layout = _layout,
        ReadySelector = _readySelector,
        Interactions = _interactions.ToArray(),
        SkipReason = _skip,
        Notes = _notes,
        Designs = _designs.ToArray(),
        Matrix = MatrixCell,
        Environment = new EnvironmentDefinition
        {
            Culture = _culture,
            Direction = _direction,
            Theme = _theme,
            Viewport = _viewport,
            User = _user,
            Clock = _clock,
            Seed = _seed,
            Offline = _offline,
            PseudoStates = _pseudoStates.ToArray(),
            Storage = _storage.ToArray(),
            ForcedColors = _forcedColors,
            PrintMedia = _print,
            Http = _httpConfigured
                ? new HttpSimulation { Rules = [.. _httpRules, .. _setupHttpRules], Latency = _httpLatency, UnmatchedStatus = _unmatched }
                : null,
        },
        Visual = new VisualOptions
        {
            Skip = _visualSkipSet,
            SkipReason = _visualSkip,
            Threshold = _threshold,
            MaxDiffPixelRatio = _maxDiffRatio,
            IgnoreSelectors = _ignore.ToArray(),
            Viewports = _visualViewports.Distinct().ToArray(),
            Themes = _visualThemes.Distinct().ToArray(),
            FullPage = _fullPage,
        },
        Accessibility = new AccessibilityOptions
        {
            Skip = _a11ySkipSet,
            SkipReason = _a11ySkip,
            DisabledRules = _disabledRules.Distinct().ToArray(),
            Justification = _a11yJustification,
            TodoReason = _a11yTodo,
        },
        Budget = new PerformanceBudget { ReadyWithin = _readyWithin, InteractionWithin = _interactionWithin, MaxDomUpdates = _maxDomUpdates },
    };
}

/// <summary>Builds a <see cref="SimulatedUser"/>.</summary>
public sealed class SimulatedUserBuilder
{
    private readonly List<string> _roles = [];
    private readonly List<KeyValuePair<string, string>> _claims = [];
    private string? _name;
    private string _authenticationType = "Workbench";

    public SimulatedUserBuilder Name(string name) { _name = name; return this; }
    public SimulatedUserBuilder Role(params string[] roles) { _roles.AddRange(roles); return this; }
    public SimulatedUserBuilder Claim(string type, string value) { _claims.Add(new(type, value)); return this; }
    public SimulatedUserBuilder AuthenticationType(string type) { _authenticationType = type; return this; }

    internal SimulatedUser Build() => new() { Name = _name, Roles = _roles.ToArray(), Claims = _claims.ToArray(), AuthenticationType = _authenticationType };
}

/// <summary>Configures the fake HTTP backend.</summary>
public sealed class HttpSimulationBuilder
{
    private readonly List<HttpRuleBuilder> _rules = [];

    internal TimeSpan? LatencyValue { get; private set; }
    internal HttpStatusCode? UnmatchedValue { get; private set; }

    public HttpRuleBuilder Get(string pattern) => On("GET", pattern);
    public HttpRuleBuilder Post(string pattern) => On("POST", pattern);
    public HttpRuleBuilder Put(string pattern) => On("PUT", pattern);
    public HttpRuleBuilder Patch(string pattern) => On("PATCH", pattern);
    public HttpRuleBuilder Delete(string pattern) => On("DELETE", pattern);
    public HttpRuleBuilder Any(string pattern) => On("*", pattern);

    public HttpRuleBuilder On(string method, string pattern)
    {
        var rule = new HttpRuleBuilder(method.ToUpperInvariant(), pattern);
        _rules.Add(rule);
        return rule;
    }

    /// <summary>Latency for every rule that does not declare its own.</summary>
    public HttpSimulationBuilder Latency(TimeSpan latency) { LatencyValue = latency; return this; }

    /// <summary>Status returned for requests that match no rule (default 404). Unmatched requests are always reported as diagnostics.</summary>
    public HttpSimulationBuilder Unmatched(HttpStatusCode status) { UnmatchedValue = status; return this; }

    internal IEnumerable<HttpRule> BuildRules() => _rules.Select(r => r.Build());
}

/// <summary>Configures the response of one fake HTTP rule.</summary>
public sealed class HttpRuleBuilder
{
    internal static JsonSerializerOptions WebJson { get; } = new(JsonSerializerDefaults.Web);
    private readonly string _method, _pattern;
    private readonly List<KeyValuePair<string, string>> _headers = [];
    private HttpStatusCode _status = HttpStatusCode.OK;
    private string? _body, _contentType, _gate;
    private TimeSpan? _latency;
    private HttpFault _fault;
    private int? _times;
    private Func<HttpRequestMessage, ScenarioContext, Task<HttpResponseMessage>>? _handler;

    internal HttpRuleBuilder(string method, string pattern)
    {
        _method = method;
        _pattern = pattern;
    }

    /// <summary>Responds with <paramref name="value"/> serialized using web JSON defaults.</summary>
    public HttpRuleBuilder RespondJson<T>(T value, HttpStatusCode status = HttpStatusCode.OK)
    {
        _body = JsonSerializer.Serialize(value, WebJson);
        _contentType = "application/json";
        _status = status;
        return this;
    }

    public HttpRuleBuilder RespondText(string text, string contentType = "text/plain", HttpStatusCode status = HttpStatusCode.OK)
    {
        _body = text;
        _contentType = contentType;
        _status = status;
        return this;
    }

    public HttpRuleBuilder Respond(HttpStatusCode status, string? body = null, string? contentType = null)
    {
        _status = status;
        _body = body;
        _contentType = contentType;
        return this;
    }

    /// <summary>Responds with an error status (default 500).</summary>
    public HttpRuleBuilder Fail(HttpStatusCode status = HttpStatusCode.InternalServerError, string? body = null) =>
        Respond(status, body, body is null ? null : "text/plain");

    public HttpRuleBuilder NetworkError() { _fault = HttpFault.NetworkError; return this; }
    public HttpRuleBuilder Timeout() { _fault = HttpFault.Timeout; return this; }
    public HttpRuleBuilder Delay(TimeSpan latency) { _latency = latency; return this; }
    public HttpRuleBuilder Header(string name, string value) { _headers.Add(new(name, value)); return this; }

    /// <summary>Matches only the next <paramref name="times"/> requests, then falls through (useful for retry scenarios).</summary>
    public HttpRuleBuilder Times(int times) { _times = times; return this; }

    /// <summary>
    /// Holds matching requests until the gate is released from the UI, a test step, or code. The request does not count as
    /// pending activity, so the scenario becomes ready in its loading state. The gate name defaults to <c>METHOD pattern</c>.
    /// </summary>
    public HttpRuleBuilder Hold(string? gate = null) { _gate = gate ?? $"{_method} {_pattern}"; return this; }

    /// <summary>Produces the response with custom code.</summary>
    public HttpRuleBuilder Handle(Func<HttpRequestMessage, ScenarioContext, Task<HttpResponseMessage>> handler) { _handler = handler; return this; }

    internal HttpRule Build() => new()
    {
        Method = _method,
        Pattern = _pattern,
        Status = _status,
        Body = _body,
        ContentType = _contentType,
        Headers = _headers.ToArray(),
        Latency = _latency,
        Fault = _fault,
        Gate = _gate,
        Times = _times,
        Handler = _handler,
    };
}

public sealed class VisualOptionsBuilder
{
    internal bool SkipSet { get; private set; }
    internal string? SkipReasonValue { get; private set; }
    internal double? ThresholdValue { get; private set; }
    internal double? MaxDiffRatioValue { get; private set; }
    internal List<string> IgnoreValues { get; } = [];
    internal List<string> ViewportValues { get; } = [];
    internal List<string> ThemeValues { get; } = [];
    internal bool FullPageValue { get; private set; }

    public VisualOptionsBuilder Skip(string reason) { SkipSet = true; SkipReasonValue = reason; return this; }

    /// <summary>Per pixel color distance (0..1). Default 0.1.</summary>
    public VisualOptionsBuilder Threshold(double threshold) { ThresholdValue = threshold; return this; }

    public VisualOptionsBuilder MaxDiffPixelRatio(double ratio) { MaxDiffRatioValue = ratio; return this; }

    /// <summary>Masks elements matching <paramref name="cssSelector"/> during capture. Prefer deterministic fixtures over masks.</summary>
    public VisualOptionsBuilder Ignore(string cssSelector) { IgnoreValues.Add(cssSelector); return this; }

    public VisualOptionsBuilder Viewports(params string[] viewports) { ViewportValues.AddRange(viewports); return this; }
    public VisualOptionsBuilder Themes(params string[] themes) { ThemeValues.AddRange(themes); return this; }
    public VisualOptionsBuilder FullPage() { FullPageValue = true; return this; }
}

public sealed class AccessibilityOptionsBuilder
{
    internal bool SkipSet { get; private set; }
    internal string? SkipReasonValue { get; private set; }
    internal List<string> DisabledRuleValues { get; } = [];
    internal string? JustificationValue { get; private set; }
    internal string? TodoValue { get; private set; }

    public AccessibilityOptionsBuilder Skip(string reason) { SkipSet = true; SkipReasonValue = reason; return this; }

    /// <summary>Scans as usual but reports violations without failing the run; <paramref name="reason"/> says why (a ticket, a date).</summary>
    public AccessibilityOptionsBuilder Todo(string reason) { TodoValue = reason; return this; }

    /// <summary>Disables an axe-core rule for this scenario. A justification is required and shown in reports.</summary>
    public AccessibilityOptionsBuilder DisableRule(string ruleId, string justification)
    {
        DisabledRuleValues.Add(ruleId);
        JustificationValue = JustificationValue is null ? $"{ruleId}: {justification}" : $"{JustificationValue}; {ruleId}: {justification}";
        return this;
    }
}

/// <summary>CSS media features emulated by automated runs (<see cref="ScenarioBuilder{TComponent}.Media"/>).</summary>
public sealed class MediaBuilder
{
    internal bool ForcedColorsValue { get; private set; }
    internal bool PrintValue { get; private set; }

    /// <summary><c>forced-colors: active</c>, as with Windows high contrast themes.</summary>
    public MediaBuilder ForcedColors() { ForcedColorsValue = true; return this; }

    /// <summary>The <c>print</c> media type.</summary>
    public MediaBuilder Print() { PrintValue = true; return this; }
}
