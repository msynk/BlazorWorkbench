using System.Text;
using System.Text.RegularExpressions;

namespace BlazorWorkbench;

/// <summary>
/// Planned answers for JavaScript interop calls (<c>IJSRuntime</c> and modules imported through it). Calls that match a
/// rule never reach the browser; the others do, unless <see cref="Strict"/> is set. Every call made by the component is
/// recorded either way.
/// </summary>
public sealed class JSInteropSimulation
{
    /// <summary>Rules evaluated in declaration order. The first rule that matches answers the call.</summary>
    public IReadOnlyList<JSInteropRule> Rules { get; init; } = [];

    /// <summary>
    /// When true, calls without a matching rule fail with a <c>JSException</c> and an error diagnostic, like bUnit's strict
    /// mode. Calls into the Blazor framework (identifiers starting with <c>Blazor.</c>) always reach the browser.
    /// </summary>
    public bool Strict { get; init; }

    /// <summary>True when a rule targets functions of an imported module whose path matches <paramref name="modulePath"/>.</summary>
    public bool HasModuleRules(string modulePath) =>
        Rules.Any(r => r.Module is not null && JSInteropRule.GlobMatch(JSInteropRule.NormalizeModule(r.Module), JSInteropRule.NormalizeModule(modulePath)));
}

/// <summary>A JavaScript interop call made by a component.</summary>
/// <param name="Identifier">The function, for example <c>localStorage.getItem</c> or a module export such as <c>render</c>.</param>
/// <param name="Module">The path of the imported module for calls on an <c>IJSObjectReference</c> from <c>import</c>; otherwise null.</param>
/// <param name="Arguments">The arguments as passed by the component.</param>
public sealed record JSCall(string Identifier, string? Module, IReadOnlyList<object?> Arguments);

/// <summary>A planned answer for JavaScript interop calls. Built with <see cref="JSInteropBuilder"/>.</summary>
public sealed class JSInteropRule
{
    /// <summary>The function identifier. <c>*</c> matches any sequence of characters (<c>chart.*</c>, <c>*</c>).</summary>
    public required string Identifier { get; init; }

    /// <summary>The module path (same wildcards) for exports of a module imported with <c>import</c>; null for global functions.</summary>
    public string? Module { get; init; }

    /// <summary>The value returned to the component. Converted through JSON when its type differs from the requested type.</summary>
    public object? Result { get; init; }

    /// <summary>When set, the call fails with a <c>JSException</c> carrying this message.</summary>
    public string? Error { get; init; }

    /// <summary>Delay before the answer. The call counts as pending activity, so the preview is ready only afterwards.</summary>
    public TimeSpan? Delay { get; init; }

    /// <summary>When set, the call waits until the named gate is released (or fails when the gate is failed).</summary>
    public string? Gate { get; init; }

    /// <summary>When set, the rule answers only this many calls and then falls through to later rules.</summary>
    public int? Times { get; init; }

    /// <summary>Additional condition on the arguments.</summary>
    public Func<JSCall, bool>? When { get; init; }

    /// <summary>Computes the answer in code (not serializable; shown as "custom").</summary>
    public Func<JSCall, ScenarioContext, object?>? Handler { get; init; }

    /// <summary>True when the rule applies to <paramref name="call"/> (ignoring <see cref="Times"/>).</summary>
    public bool IsMatch(JSCall call)
    {
        ArgumentNullException.ThrowIfNull(call);
        if ((Module is null) != (call.Module is null))
            return false;
        if (Module is not null && !GlobMatch(NormalizeModule(Module), NormalizeModule(call.Module!)))
            return false;
        return GlobMatch(Identifier, call.Identifier) && (When is null || When(call));
    }

    /// <summary>A short description of the answer for reports and the Environment panel.</summary>
    public string Describe()
    {
        var parts = new List<string>();
        if (Gate is not null) parts.Add($"held at gate '{Gate}'");
        if (Delay is { } delay) parts.Add($"after {delay.TotalMilliseconds:0} ms");
        if (Error is not null) parts.Add($"throws \"{Error}\"");
        else if (Handler is not null) parts.Add("custom");
        else if (Result is not null) parts.Add("returns " + Summarize(Result));
        else parts.Add("returns nothing");
        if (Times is { } times) parts.Add($"{times} time(s)");
        return string.Join(", ", parts);
    }

    public override string ToString() => Module is null ? Identifier : $"{Module} → {Identifier}";

    /// <summary>Module paths are compared without a leading <c>./</c> or <c>/</c>.</summary>
    public static string NormalizeModule(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var value = path.Trim();
        while (value.StartsWith("./", StringComparison.Ordinal))
            value = value[2..];
        return value.TrimStart('/');
    }

    /// <summary>Case-sensitive match where <c>*</c> matches any sequence of characters.</summary>
    public static bool GlobMatch(string pattern, string value)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(value);
        if (!pattern.Contains('*', StringComparison.Ordinal))
            return string.Equals(pattern, value, StringComparison.Ordinal);
        var regex = "^" + Regex.Escape(pattern).Replace(@"\*", ".*", StringComparison.Ordinal) + "$";
        return Regex.IsMatch(value, regex, RegexOptions.CultureInvariant | RegexOptions.Singleline, TimeSpan.FromMilliseconds(100));
    }

    internal static string Summarize(object value)
    {
        var text = value switch
        {
            string s => "\"" + s + "\"",
            bool b => b ? "true" : "false",
            _ => System.Text.Json.JsonSerializer.Serialize(value, HttpRuleBuilder.WebJson),
        };
        return text.Length > 60 ? text[..57] + "…" : text;
    }
}

/// <summary>Configures planned JavaScript interop answers (<see cref="ScenarioBuilder{TComponent}.JSInterop"/>).</summary>
public sealed class JSInteropBuilder
{
    private readonly List<JSInteropRuleBuilder> _rules = [];

    internal bool? StrictValue { get; private set; }

    /// <summary>
    /// Answers calls to <paramref name="identifier"/> (for example <c>localStorage.getItem</c>, <c>chart.*</c>). Without
    /// <see cref="JSInteropRuleBuilder.Returns"/> or <see cref="JSInteropRuleBuilder.Throws"/> the call completes with the
    /// default value, which suits <c>InvokeVoidAsync</c>.
    /// </summary>
    public JSInteropRuleBuilder Setup(string identifier) => Add(identifier, null);

    /// <summary>
    /// Fakes the exports of a JavaScript module imported with <c>JS.InvokeAsync&lt;IJSObjectReference&gt;("import", path)</c>.
    /// The component receives a .NET stand-in for the module; exports without a rule are called on the real module (unless
    /// the simulation is strict). The stand-in cannot be passed back to JavaScript as an argument.
    /// </summary>
    /// <param name="path">The module path as imported (a leading <c>./</c> is ignored; <c>*</c> is a wildcard).</param>
    /// <param name="configure">The rules for the module's exports.</param>
    public JSInteropBuilder Module(string path, Action<JSModuleBuilder> configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(configure);
        configure(new JSModuleBuilder(this, path));
        return this;
    }

    /// <summary>Fails calls that no rule answers (framework calls excepted), like bUnit's strict mode.</summary>
    public JSInteropBuilder Strict(bool strict = true) { StrictValue = strict; return this; }

    internal JSInteropRuleBuilder Add(string identifier, string? module)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        var rule = new JSInteropRuleBuilder(identifier, module);
        _rules.Add(rule);
        return rule;
    }

    internal IEnumerable<JSInteropRule> BuildRules() => _rules.Select(r => r.Build());
}

/// <summary>Configures the exports of one faked module (<see cref="JSInteropBuilder.Module"/>).</summary>
public sealed class JSModuleBuilder
{
    private readonly JSInteropBuilder _owner;
    private readonly string _path;

    internal JSModuleBuilder(JSInteropBuilder owner, string path)
    {
        _owner = owner;
        _path = path;
    }

    /// <summary>Answers calls to the module export <paramref name="identifier"/>.</summary>
    public JSInteropRuleBuilder Setup(string identifier) => _owner.Add(identifier, _path);
}

/// <summary>Configures the answer of one JavaScript interop rule.</summary>
public sealed class JSInteropRuleBuilder
{
    private readonly string _identifier;
    private readonly string? _module;
    private object? _result;
    private string? _error, _gate;
    private TimeSpan? _delay;
    private int? _times;
    private Func<JSCall, bool>? _when;
    private Func<JSCall, ScenarioContext, object?>? _handler;

    internal JSInteropRuleBuilder(string identifier, string? module)
    {
        _identifier = identifier;
        _module = module;
    }

    /// <summary>Returns <paramref name="value"/>. It is converted through JSON when the component asks for another type.</summary>
    public JSInteropRuleBuilder Returns<T>(T value) { _result = value; _error = null; return this; }

    /// <summary>Fails the call with a <c>JSException</c>, as when the browser API throws (permissions, quota, unsupported).</summary>
    public JSInteropRuleBuilder Throws(string message) { ArgumentException.ThrowIfNullOrWhiteSpace(message); _error = message; return this; }

    /// <summary>Answers after <paramref name="delay"/>; the call counts as pending activity until then.</summary>
    public JSInteropRuleBuilder Delay(TimeSpan delay) { _delay = delay; return this; }

    /// <summary>
    /// Holds the call until the gate is released from the UI, a test step or code, like <see cref="HttpRuleBuilder.Hold"/>.
    /// The gate name defaults to <c>js identifier</c>.
    /// </summary>
    public JSInteropRuleBuilder Hold(string? gate = null) { _gate = gate ?? "js " + _identifier; return this; }

    /// <summary>Answers only the next <paramref name="times"/> calls, then falls through to later rules.</summary>
    public JSInteropRuleBuilder Times(int times) { ArgumentOutOfRangeException.ThrowIfNegativeOrZero(times); _times = times; return this; }

    /// <summary>Answers only calls whose arguments satisfy <paramref name="predicate"/>.</summary>
    public JSInteropRuleBuilder When(Func<JSCall, bool> predicate) { _when = predicate; return this; }

    /// <summary>Computes the answer in code, for example from the arguments.</summary>
    public JSInteropRuleBuilder Handle(Func<JSCall, ScenarioContext, object?> handler) { _handler = handler; return this; }

    internal JSInteropRule Build() => new()
    {
        Identifier = _identifier,
        Module = _module,
        Result = _result,
        Error = _error,
        Delay = _delay,
        Gate = _gate,
        Times = _times,
        When = _when,
        Handler = _handler,
    };
}

/// <summary>Text helpers for recorded JavaScript interop calls.</summary>
public static class JSInteropFormat
{
    /// <summary>Formats call arguments for logs: JSON where possible, type names for references and streams.</summary>
    public static string Arguments(IReadOnlyList<object?> arguments, int maxLength = 300)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var builder = new StringBuilder();
        for (var i = 0; i < arguments.Count; i++)
        {
            if (i > 0) builder.Append(", ");
            builder.Append(Argument(arguments[i]));
            if (builder.Length > maxLength)
                break;
        }

        var text = builder.ToString();
        return text.Length > maxLength ? text[..(maxLength - 1)] + "…" : text;
    }

    private static string Argument(object? value)
    {
        if (value is null)
            return "null";
        var type = value.GetType();
        if (value is string s)
            return System.Text.Json.JsonSerializer.Serialize(s);
        if (value is Stream || value is Delegate || type.FullName?.StartsWith("Microsoft.JSInterop.", StringComparison.Ordinal) == true ||
            type.FullName?.StartsWith("Microsoft.AspNetCore.Components.", StringComparison.Ordinal) == true)
            return Naming.FormatTypeName(type);
        try
        {
            return System.Text.Json.JsonSerializer.Serialize(value, HttpRuleBuilder.WebJson);
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or System.Text.Json.JsonException or ArgumentException)
        {
            return Naming.FormatTypeName(type);
        }
    }
}
