using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using BlazorWorkbench.Catalog;
using BlazorWorkbench.Components.Runtime;
using BlazorWorkbench.Contracts;
using BlazorWorkbench.Diagnostics;
using BlazorWorkbench.Values;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.JSInterop;
using DiagnosticSource = BlazorWorkbench.Diagnostics.DiagnosticSource;

namespace BlazorWorkbench.Components.Preview;

/// <summary>
/// Renders one scenario: activates the <see cref="ScenarioSession"/>, applies parameters, inspector overrides, cascading
/// values and automatic callback instrumentation, and isolates failures in an error boundary. Used by the preview page
/// and by bUnit integration (with <see cref="EnableBridge"/> off).
/// </summary>
public sealed class ScenarioHost : ComponentBase, IDisposable
{
    public const string RootElementId = "workbench-preview-root";

    private readonly Dictionary<string, ArgOverride> _overrides = new(StringComparer.Ordinal);
    private readonly Dictionary<string, object?> _rawValues = new(StringComparer.Ordinal);
    private readonly Dictionary<string, object?> _factoryCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, object> _holders = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _argErrors = new(StringComparer.Ordinal);
    private readonly List<(Type Type, string? Name, object? Value)> _cascades = [];
    private DotNetObjectReference<ScenarioHost>? _self;
    private IDisposable? _navigationRegistration;
    private WorkbenchErrorBoundary? _boundary;
    private Exception? _activationError;
    private string? _reportedSetupError;
    private int _componentKey;
    private bool _bridgeAttached;
    private bool _disposed;

    [Inject] private ScenarioSession Session { get; set; } = default!;
    [Inject] private IServiceProvider Services { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;

    [Parameter, EditorRequired] public ScenarioDefinition Scenario { get; set; } = default!;

    /// <summary>Inspector overrides applied on first render (for example from a deep link).</summary>
    [Parameter] public IReadOnlyDictionary<string, ArgOverride>? InitialArgs { get; set; }

    [Parameter] public ActivationOptions? Activation { get; set; }

    /// <summary>Connects to the browser bridge (<c>workbench-preview.js</c>). Off in bUnit.</summary>
    [Parameter] public bool EnableBridge { get; set; }

    /// <summary>Validation errors of the current inspector overrides.</summary>
    public IReadOnlyDictionary<string, string> ArgErrors => _argErrors;

    public IReadOnlyDictionary<string, ArgOverride> Overrides => _overrides;

    protected override void OnInitialized()
    {
        try
        {
            if (!Session.IsActive)
                Session.Activate(Scenario, Activation, Services);
            PrepareCascades();
        }
        catch (Exception ex)
        {
            _activationError = ex;
            Session.Diagnostics.Add(DiagnosticSource.Scenario, DiagnosticSeverity.Error, Scenario.Id, $"Scenario setup failed: {ex.Message}", ex.ToString());
        }

        if (InitialArgs is not null)
        {
            foreach (var (name, arg) in InitialArgs)
                _overrides[name] = arg;
        }

        try
        {
            _navigationRegistration = Navigation.RegisterLocationChangingHandler(context =>
            {
                Session.Diagnostics.Add(DiagnosticSource.Navigation, DiagnosticSeverity.Info, Scenario.Id,
                    $"Navigation to {Session.Redactor.RedactUrl(context.TargetLocation)} was intercepted; previews never leave the scenario.");
                context.PreventNavigation();
                return ValueTask.CompletedTask;
            });
        }
        catch (NotSupportedException)
        {
            // Some navigation managers (static rendering) do not support interception.
        }

        Session.Diagnostics.Added += OnDiagnostic;
        Session.Callbacks.Added += OnCallback;
        Session.Gates.Changed += OnGatesChanged;
        Session.Activity.Changed += OnActivityChanged;
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        // BuildRenderTree is synchronous, so these settings flow to the components rendered in the same batch
        // (the circuit handler applies them to later activities).
        ScenarioSessionContext.Current = Session;
        ApplyCulture();

        var layout = Scenario.Layout.ToString().ToLowerInvariant();
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "id", RootElementId);
        builder.AddAttribute(2, "class", $"wb-preview-root wb-layout-{layout}");
        builder.AddAttribute(3, "data-scenario", Scenario.Id);

        if (_activationError is not null)
        {
            RenderSetupError(builder, _activationError);
        }
        else
        {
            builder.OpenComponent<WorkbenchErrorBoundary>(10);
            builder.AddComponentParameter(11, nameof(WorkbenchErrorBoundary.OnError), (Action<Exception>)OnRenderError);
            builder.AddComponentParameter(12, nameof(WorkbenchErrorBoundary.ChildContent), (RenderFragment)RenderScenario);
            builder.AddComponentReferenceCapture(13, r => _boundary = (WorkbenchErrorBoundary)r);
            builder.CloseComponent();
        }

        builder.CloseElement();
    }

    private void RenderScenario(RenderTreeBuilder builder)
    {
        Dictionary<string, object?> parameters;
        try
        {
            parameters = BuildParameters();
        }
        catch (Exception ex)
        {
            ReportSetupError(ex);
            RenderSetupError(builder, ex);
            return;
        }

        _reportedSetupError = null;
        RenderFragment content = b =>
        {
            b.OpenComponent<DynamicComponent>(0);
            b.SetKey(_componentKey);
            b.AddComponentParameter(1, nameof(DynamicComponent.Type), Scenario.ComponentType);
            b.AddComponentParameter(2, nameof(DynamicComponent.Parameters), parameters);
            b.CloseComponent();
        };

        foreach (var (type, name, value) in Enumerable.Reverse(_cascades))
        {
            var inner = content;
            content = b =>
            {
                b.OpenComponent(0, typeof(CascadingValue<>).MakeGenericType(type));
                b.AddComponentParameter(1, "Value", value);
                if (name is not null) b.AddComponentParameter(2, "Name", name);
                b.AddComponentParameter(3, "IsFixed", true);
                b.AddComponentParameter(4, "ChildContent", inner);
                b.CloseComponent();
            };
        }

        builder.AddContent(0, content);
    }

    /// <summary>
    /// Records a parameter/fixture setup failure as an error diagnostic and reports it to the bridge so readiness checks,
    /// render checks and visual runs fail instead of treating the error panel as a successful render. Re-renders with the
    /// same failure are reported once.
    /// </summary>
    private void ReportSetupError(Exception ex)
    {
        var message = $"{ex.GetType().Name}: {ex.Message}";
        if (_reportedSetupError == message)
            return;
        _reportedSetupError = message;
        Session.Diagnostics.Add(DiagnosticSource.Scenario, DiagnosticSeverity.Error, Scenario.Id, $"Scenario setup failed: {ex.Message}", ex.ToString());
        if (EnableBridge)
            Bridge("BlazorWorkbenchPreview.setError", message);
    }

    private static void RenderSetupError(RenderTreeBuilder builder, Exception ex)
    {
        builder.OpenElement(20, "div");
        builder.AddAttribute(21, "class", "wb-preview-error");
        builder.AddAttribute(22, "role", "alert");
        builder.AddAttribute(23, "data-workbench-error", "true");
        builder.OpenElement(24, "strong");
        builder.AddContent(25, "Scenario setup failed");
        builder.CloseElement();
        builder.OpenElement(26, "p");
        builder.AddContent(27, $"{ex.GetType().Name}: {ex.Message}");
        builder.CloseElement();
        builder.CloseElement();
    }

    /// <summary>Builds the parameter dictionary from scenario values, overrides, fixtures and instrumentation.</summary>
    internal Dictionary<string, object?> BuildParameters()
    {
        var metadata = Session.Component?.Metadata ?? throw new InvalidOperationException($"Component metadata for {Scenario.Id} is missing.");
        var context = Session.Context!;
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        _argErrors.Clear();

        foreach (var assignment in Scenario.Parameters)
        {
            if (_overrides.ContainsKey(assignment.Name))
                continue;
            result[assignment.Name] = assignment.Kind switch
            {
                ParameterValueKind.Literal => assignment.Value,
                ParameterValueKind.Text => TextFragment(assignment.Value as string),
                _ => GetFactoryValue(assignment, context),
            };
        }

        foreach (var (name, arg) in _overrides)
        {
            var parameter = metadata.FindParameter(name);
            if (parameter is null && !metadata.CapturesUnmatchedValues)
            {
                _argErrors[name] = $"'{name}' is not a parameter of {metadata.Name}.";
                continue;
            }

            var type = parameter?.Type ?? typeof(object);
            switch (arg.Kind)
            {
                case ArgKind.Unset:
                    break;
                case ArgKind.Null:
                    if (type.IsValueType && Nullable.GetUnderlyingType(type) is null)
                        _argErrors[name] = $"{Naming.FormatTypeName(type)} does not accept null.";
                    else
                        result[name] = null;
                    break;
                case ArgKind.Fixture:
                    var fixture = Scenario.Fixtures.FirstOrDefault(f => f.Name == arg.Fixture);
                    if (fixture is null)
                        _argErrors[name] = $"Fixture '{arg.Fixture}' does not exist.";
                    else
                        result[name] = GetCached($"fixture:{name}:{fixture.Name}", () => fixture.Factory(context));
                    break;
                default:
                    if (_rawValues.TryGetValue(name, out var raw))
                    {
                        result[name] = raw;
                    }
                    else if (parameter?.Type == typeof(RenderFragment))
                    {
                        result[name] = TextFragment(arg.Value?.ValueKind == JsonValueKind.String ? arg.Value.Value.GetString() : arg.Value?.GetRawText());
                    }
                    else if (arg.Value is { } json && ParameterValueConverter.TryConvert(json, type, out var converted, out _))
                    {
                        result[name] = converted;
                    }
                    else if (arg.Value is { } customJson && Session.Catalogs.Options.ParameterEditors.ContainsKey(Nullable.GetUnderlyingType(type) ?? type) &&
                             TryDeserialize(customJson, type, out var custom))
                    {
                        // Values produced by custom editors travel as JSON.
                        result[name] = custom;
                    }
                    else
                    {
                        ParameterValueConverter.TryConvert(arg.Value ?? default, type, out _, out var conversionError);
                        _argErrors[name] = conversionError ?? $"Invalid value for {name}.";
                        if (Scenario.FindParameter(name) is { } original)
                            result[name] = original.Kind == ParameterValueKind.Literal ? original.Value : GetFactoryValue(original, context);
                    }

                    break;
            }
        }

        // Instrument unhandled event callbacks and wire two-way bindings so interactive components work in isolation.
        foreach (var parameter in metadata.Parameters)
        {
            if (parameter.Kind == ParameterKind.EventCallback && !result.ContainsKey(parameter.Name))
                result[parameter.Name] = CreateCallback(parameter);
            else if (parameter.Kind == ParameterKind.Expression && !result.ContainsKey(parameter.Name) &&
                     parameter.Name.EndsWith("Expression", StringComparison.Ordinal) &&
                     metadata.FindParameter(parameter.Name[..^"Expression".Length]) is { Kind: ParameterKind.Value } bound)
                result[parameter.Name] = CreateValueExpression(bound, parameter.Type, result.GetValueOrDefault(bound.Name));
        }

        return result;
    }

    private static bool TryDeserialize(JsonElement json, Type type, out object? value)
    {
        try
        {
            value = json.Deserialize(type, WorkbenchJson.Options);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            value = null;
            return false;
        }
    }

    private object? GetFactoryValue(ParameterAssignment assignment, ScenarioContext context)
    {
        if (assignment.FixtureName is { } fixtureName)
        {
            var fixture = Scenario.Fixtures.FirstOrDefault(f => f.Name == fixtureName)
                          ?? throw new InvalidOperationException($"Fixture '{fixtureName}' is not declared.");
            return GetCached($"fixture:{assignment.Name}:{fixtureName}", () => fixture.Factory(context));
        }

        return GetCached($"param:{assignment.Name}", () => assignment.Factory?.Invoke(context));
    }

    private object? GetCached(string key, Func<object?> factory)
    {
        if (!_factoryCache.TryGetValue(key, out var value))
            _factoryCache[key] = value = factory();
        return value;
    }

    private static RenderFragment TextFragment(string? text) => b => b.AddContent(0, text);

    private object CreateCallback(ParameterDescriptor parameter)
    {
        if (parameter.Type == typeof(EventCallback))
            return new EventCallback(this, (Action)(() => OnCallbackInvoked(parameter, null, hasValue: false)));

        var argType = parameter.Type.GetGenericArguments()[0];
        return s_createTypedCallback.MakeGenericMethod(argType).Invoke(this, [parameter])!;
    }

    private static readonly MethodInfo s_createTypedCallback = typeof(ScenarioHost).GetMethod(nameof(CreateTypedCallback), BindingFlags.Instance | BindingFlags.NonPublic)!;

    private EventCallback<T> CreateTypedCallback<T>(ParameterDescriptor parameter) =>
        new(this, (Action<T>)(value => OnCallbackInvoked(parameter, value, hasValue: true)));

    private void OnCallbackInvoked(ParameterDescriptor parameter, object? value, bool hasValue)
    {
        var summary = hasValue ? Summarize(value) : "";
        Session.Callbacks.Add(parameter.Name, summary);
        Session.Diagnostics.Add(DiagnosticSource.Callback, DiagnosticSeverity.Info, Scenario.Id,
            $"{parameter.Name}({summary})", data: new Dictionary<string, string> { ["callback"] = parameter.Name });

        // Two-way binding: XChanged updates X, like @bind-X would in a real parent.
        if (parameter.BoundTo is { } target && hasValue)
        {
            if (ParameterValueConverter.TrySerialize(value, out var json))
            {
                _rawValues.Remove(target);
                _overrides[target] = ArgOverride.FromValue(json);
            }
            else
            {
                _rawValues[target] = value;
                _overrides[target] = new ArgOverride(ArgKind.Value);
            }

            if (_holders.TryGetValue(target, out var holder))
                holder.GetType().GetProperty("Value")!.SetValue(holder, value);
            NotifyArgsChanged();
        }
    }

    private object CreateValueExpression(ParameterDescriptor bound, Type expressionType, object? currentValue)
    {
        var valueType = bound.Type;
        if (!_holders.TryGetValue(bound.Name, out var holder))
        {
            holder = Activator.CreateInstance(typeof(ValueHolder<>).MakeGenericType(valueType))!;
            _holders[bound.Name] = holder;
        }

        holder.GetType().GetProperty("Value")!.SetValue(holder, currentValue);
        var lambda = Expression.Lambda(typeof(Func<>).MakeGenericType(valueType), Expression.Property(Expression.Constant(holder), "Value"));
        return expressionType.IsInstanceOfType(lambda) ? lambda : throw new InvalidOperationException($"Cannot create {Naming.FormatTypeName(expressionType)}.");
    }

    private void PrepareCascades()
    {
        var context = Session.Context!;
        foreach (var cascade in Scenario.CascadingValues)
            _cascades.Add((cascade.ValueType, cascade.Name, cascade.Factory(context)));

        var metadata = Session.Component?.Metadata;
        var needsEditContext = metadata?.Parameters.Any(p => p.Kind == ParameterKind.Cascading && p.Type == typeof(EditContext)) == true;
        if (needsEditContext && !_cascades.Any(c => c.Type == typeof(EditContext)))
            _cascades.Add((typeof(EditContext), null, new EditContext(new object())));
    }

    private void ApplyCulture()
    {
        if (Session.Culture is { } culture)
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }
    }

    private void OnRenderError(Exception ex)
    {
        Session.Diagnostics.Add(DiagnosticSource.Render, DiagnosticSeverity.Error, Scenario.Id,
            $"Unhandled {ex.GetType().Name}: {ex.Message}", ex.ToString());
        if (EnableBridge)
            Bridge("BlazorWorkbenchPreview.setError", ex.GetType().Name + ": " + ex.Message);
    }

    // ---- Inspector API (used by the browser bridge and tests) ----

    /// <summary>Replaces the inspector overrides and re-renders. Removing or unsetting a parameter remounts the component.</summary>
    public void SetOverrides(IReadOnlyDictionary<string, ArgOverride> overrides)
    {
        var remount = _overrides.Keys.Except(overrides.Keys).Any() ||
                      overrides.Any(o => o.Value.Kind is ArgKind.Unset or ArgKind.Fixture &&
                                         (!_overrides.TryGetValue(o.Key, out var previous) || previous != o.Value));
        foreach (var key in overrides.Keys)
            _rawValues.Remove(key);
        _overrides.Clear();
        foreach (var (name, arg) in overrides)
            _overrides[name] = arg;
        if (remount)
            _componentKey++;
        _boundary?.Recover();
        StateHasChanged();
    }

    [JSInvokable]
    public async Task<string> ApplyArgs(string json)
    {
        Dictionary<string, ArgOverride> overrides;
        try
        {
            overrides = ArgsCodec.Decode(json);
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            return JsonSerializer.Serialize(new Dictionary<string, string> { ["$args"] = ex.Message }, WorkbenchJson.Options);
        }

        await InvokeAsync(() => SetOverrides(overrides));
        return JsonSerializer.Serialize(_argErrors, WorkbenchJson.Options);
    }

    [JSInvokable]
    public bool ReleaseGate(string name) => Session.Gates.Release(name);

    [JSInvokable]
    public bool FailGate(string name, string? message) => Session.Gates.Fail(name, message);

    [JSInvokable]
    public bool AdvanceClock(double milliseconds)
    {
        if (Session.Clock is null)
            return false;
        Session.Clock.Advance(TimeSpan.FromMilliseconds(milliseconds));
        Session.Diagnostics.Add(DiagnosticSource.Scenario, DiagnosticSeverity.Info, Scenario.Id, $"Clock advanced to {Session.Clock.GetUtcNow():O}.");
        _ = InvokeAsync(StateHasChanged);
        return true;
    }

    [JSInvokable]
    public int GetCallbackCount(string name) => Session.Callbacks.Count(name);

    [JSInvokable]
    public string GetMetrics() => JsonSerializer.Serialize(new
    {
        sessionId = Session.SessionId,
        server = Session.ServerMetrics.Snapshot(),
        http = Session.Http.Exchanges.Count,
        diagnostics = Session.Diagnostics.Snapshot().Count,
    }, WorkbenchJson.Options);

    [JSInvokable]
    public string GetSessionInfo() => JsonSerializer.Serialize(new
    {
        scenarioId = Scenario.Id,
        services = Session.DescribeServices(),
        gates = Session.Gates.Snapshot(),
        http = Session.Http.Exchanges,
        clock = Session.Clock?.GetUtcNow(),
        culture = Session.Culture?.Name,
    }, WorkbenchJson.Options);

    // ---- Bridge ----

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender || !EnableBridge || _disposed)
            return;

        _self = DotNetObjectReference.Create(this);
        var options = Session.Catalogs.Options;
        var themeName = Activation?.Theme ?? Scenario.Environment.Theme ?? options.Themes.FirstOrDefault()?.Name;
        var theme = options.FindTheme(themeName);
        var direction = Activation?.Direction ?? Scenario.Environment.Direction;
        if (direction == TextDirection.Auto)
            direction = Session.Culture?.TextInfo.IsRightToLeft == true ? TextDirection.Rtl : TextDirection.Ltr;

        var snapshot = Session.Diagnostics.Snapshot();
        var snapshotTime = snapshot.Count > 0 ? snapshot[^1].Timestamp : DateTimeOffset.MinValue;
        try
        {
            await JS.InvokeVoidAsync("BlazorWorkbenchPreview.attach", _self, new
            {
                scenarioId = Scenario.Id,
                sessionId = Session.SessionId,
                readySelector = Scenario.ReadySelector,
                theme = theme?.Name,
                colorScheme = theme?.ColorScheme.ToString().ToLowerInvariant(),
                themeClass = theme?.CssClass,
                themeStylesheets = theme?.Stylesheets ?? [],
                dir = direction.ToString().ToLowerInvariant(),
                lang = Session.Culture?.Name,
                pending = Session.Activity.Pending,
                error = _activationError?.Message,
                diagnostics = snapshot.Select(ToDto),
                argErrors = _argErrors,
            });
            _bridgeAttached = true;

            // Events raised while attaching were not forwarded; replay current state so readiness cannot stall.
            foreach (var late in Session.Diagnostics.Snapshot().Where(d => d.Timestamp > snapshotTime))
                OnDiagnostic(late);
            OnActivityChanged(Session.Activity.Pending);
            OnGatesChanged();
        }
        catch (JSDisconnectedException)
        {
        }
    }

    private void OnDiagnostic(DiagnosticEvent e)
    {
        if (_bridgeAttached)
            Bridge("BlazorWorkbenchPreview.diagnostic", ToDto(e));
    }

    private void OnCallback(CallbackEntry entry)
    {
        if (_bridgeAttached)
            Bridge("BlazorWorkbenchPreview.callback", entry);
    }

    private void OnGatesChanged()
    {
        if (_bridgeAttached)
            Bridge("BlazorWorkbenchPreview.gates", Session.Gates.Snapshot());
    }

    private void OnActivityChanged(int _)
    {
        // Activity changes are raised on the threads of concurrent requests, so their dispatches can run in a different
        // order than the values were produced (3, 2, 0, 1), leaving the preview with a stale non-zero count that blocks
        // readiness. Read the count when the message goes out instead: the last message always carries the latest value.
        if (_bridgeAttached)
            BridgeCurrent("BlazorWorkbenchPreview.setPending", () => Session.Activity.Pending);
    }

    private void NotifyArgsChanged()
    {
        if (_bridgeAttached)
            Bridge("BlazorWorkbenchPreview.argsChanged", ArgsCodec.Encode(_overrides.Where(o => !_rawValues.ContainsKey(o.Key)).ToDictionary()));
    }

    private void Bridge(string identifier, object? payload) => BridgeCurrent(identifier, () => payload);

    /// <summary>Sends a message to the preview script; <paramref name="payload"/> is evaluated on the renderer's thread right before sending.</summary>
    private void BridgeCurrent(string identifier, Func<object?> payload)
    {
        if (_disposed)
            return;
        _ = InvokeAsync(async () =>
        {
            try
            {
                await JS.InvokeVoidAsync(identifier, payload());
            }
            catch (Exception ex) when (ex is JSDisconnectedException or JSException or TaskCanceledException or ObjectDisposedException or InvalidOperationException)
            {
                // The preview is closing or the bridge is unavailable; diagnostics remain in the session log.
            }
        });
    }

    private static DiagnosticDto ToDto(DiagnosticEvent e) =>
        new(e.Timestamp.ToString("O", CultureInfo.InvariantCulture), e.Source, e.Severity, e.ScenarioId, e.Message, e.Details, e.SessionId);

    private string Summarize(object? value)
    {
        if (value is null)
            return "null";
        try
        {
            var json = JsonSerializer.Serialize(value, value.GetType(), s_summaryJson);
            json = Session.Redactor.RedactText(json);
            return json.Length > 300 ? json[..300] + "…" : json;
        }
        catch (Exception ex) when (ex is NotSupportedException or JsonException or InvalidOperationException)
        {
            return Naming.FormatTypeName(value.GetType());
        }
    }

    private static readonly JsonSerializerOptions s_summaryJson = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = 4,
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        Converters = { new JsonStringEnumConverter() },
    };

    public void Dispose()
    {
        _disposed = true;
        Session.Diagnostics.Added -= OnDiagnostic;
        Session.Callbacks.Added -= OnCallback;
        Session.Gates.Changed -= OnGatesChanged;
        Session.Activity.Changed -= OnActivityChanged;
        _navigationRegistration?.Dispose();
        _self?.Dispose();
    }

    /// <summary>Holds the current value of a bound parameter so generated <c>ValueExpression</c>s resolve to a real member.</summary>
    public sealed class ValueHolder<T>
    {
        public T? Value { get; set; }
    }
}

/// <summary>Error boundary that reports exceptions to the scenario session and renders a recoverable error panel.</summary>
public sealed class WorkbenchErrorBoundary : ErrorBoundaryBase
{
    [Parameter] public Action<Exception>? OnError { get; set; }

    protected override Task OnErrorAsync(Exception exception)
    {
        OnError?.Invoke(exception);
        return Task.CompletedTask;
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        if (CurrentException is null)
        {
            builder.AddContent(0, ChildContent);
            return;
        }

        builder.OpenElement(1, "div");
        builder.AddAttribute(2, "class", "wb-preview-error");
        builder.AddAttribute(3, "role", "alert");
        builder.AddAttribute(4, "data-workbench-error", "true");
        builder.OpenElement(5, "strong");
        builder.AddContent(6, $"Unhandled {CurrentException.GetType().Name}");
        builder.CloseElement();
        builder.OpenElement(7, "p");
        builder.AddContent(8, CurrentException.Message);
        builder.CloseElement();
        builder.OpenElement(9, "details");
        builder.OpenElement(10, "summary");
        builder.AddContent(11, "Stack trace");
        builder.CloseElement();
        builder.OpenElement(12, "pre");
        builder.AddContent(13, CurrentException.StackTrace);
        builder.CloseElement();
        builder.CloseElement();
        builder.OpenElement(14, "button");
        builder.AddAttribute(15, "type", "button");
        builder.AddAttribute(16, "onclick", EventCallback.Factory.Create(this, Recover));
        builder.AddContent(17, "Recover");
        builder.CloseElement();
        builder.CloseElement();
    }
}
