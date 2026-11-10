using System.Net;
using System.Security.Claims;

namespace BlazorWorkbench;

/// <summary>Text direction applied to the preview document.</summary>
public enum TextDirection
{
    /// <summary>Derived from the culture (right-to-left cultures use <see cref="Rtl"/>).</summary>
    Auto,
    Ltr,
    Rtl,
}

/// <summary>Simulated runtime conditions for a scenario. All members are optional; unset members use the preview defaults.</summary>
public sealed class EnvironmentDefinition
{
    /// <summary>Culture name such as <c>fa-IR</c>. Applies to both <c>CurrentCulture</c> and <c>CurrentUICulture</c>.</summary>
    public string? Culture { get; init; }

    public TextDirection Direction { get; init; } = TextDirection.Auto;

    /// <summary>Theme name defined in <see cref="WorkbenchOptions.Themes"/>.</summary>
    public string? Theme { get; init; }

    /// <summary>Viewport preset name defined in <see cref="WorkbenchOptions.Viewports"/>.</summary>
    public string? Viewport { get; init; }

    /// <summary>Simulated user. Null leaves authentication unspecified (anonymous).</summary>
    public SimulatedUser? User { get; init; }

    /// <summary>Fake HTTP backend. Null still installs a fake backend that answers 404 to every request.</summary>
    public HttpSimulation? Http { get; init; }

    /// <summary>Fixed start time for the scenario's <see cref="TimeProvider"/>. Null uses the real clock.</summary>
    public DateTimeOffset? Clock { get; init; }

    /// <summary>Seed for <see cref="ScenarioContext.Random"/>. Defaults to a stable hash of the scenario id.</summary>
    public int? Seed { get; init; }

    /// <summary>When true, the fake HTTP backend fails every request with a connection error.</summary>
    public bool Offline { get; init; }

    /// <summary>CSS pseudo-classes forced in the preview, such as <c>:hover</c> on every element or on a selector.</summary>
    public IReadOnlyList<PseudoStateRule> PseudoStates { get; init; } = [];

    /// <summary>Browser storage entries written before the component starts (the preview's storage is cleared first).</summary>
    public IReadOnlyList<StorageEntry> Storage { get; init; } = [];

    /// <summary>
    /// Emulates <c>forced-colors: active</c> (Windows high contrast) in automated runs. Browsers cannot emulate it inside the
    /// Workbench's preview frame, so the interactive preview is unchanged.
    /// </summary>
    public bool ForcedColors { get; init; }

    /// <summary>Emulates the <c>print</c> media type in automated runs (screenshots of the printed layout).</summary>
    public bool PrintMedia { get; init; }
}

/// <summary>A CSS pseudo-class that the preview can force without user input.</summary>
public enum PseudoState
{
    /// <summary><c>:hover</c></summary>
    Hover,

    /// <summary><c>:focus</c></summary>
    Focus,

    /// <summary><c>:focus-visible</c></summary>
    FocusVisible,

    /// <summary><c>:focus-within</c></summary>
    FocusWithin,

    /// <summary><c>:active</c></summary>
    Active,
}

/// <summary>Forces <see cref="State"/> on the elements matching <see cref="Selector"/> (every element of the preview when null).</summary>
public sealed record PseudoStateRule(PseudoState State, string? Selector = null)
{
    /// <summary>The CSS pseudo-class, for example <c>:focus-visible</c>.</summary>
    public string PseudoClass => ToPseudoClass(State);

    public static string ToPseudoClass(PseudoState state) => state switch
    {
        PseudoState.Hover => ":hover",
        PseudoState.Focus => ":focus",
        PseudoState.FocusVisible => ":focus-visible",
        PseudoState.FocusWithin => ":focus-within",
        PseudoState.Active => ":active",
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

    public override string ToString() => Selector is null ? PseudoClass : $"{Selector}{PseudoClass}";
}

/// <summary>Browser storage area.</summary>
public enum StorageArea
{
    /// <summary><c>window.localStorage</c></summary>
    Local,

    /// <summary><c>window.sessionStorage</c></summary>
    Session,
}

/// <summary>A key and string value written to browser storage before the scenario renders.</summary>
public sealed record StorageEntry(StorageArea Area, string Key, string Value);

/// <summary>A simulated principal for authentication and authorization scenarios.</summary>
public sealed class SimulatedUser
{
    public static SimulatedUser Anonymous { get; } = new() { IsAuthenticated = false };

    public bool IsAuthenticated { get; init; } = true;
    public string? Name { get; init; }
    public IReadOnlyList<string> Roles { get; init; } = [];
    public IReadOnlyList<KeyValuePair<string, string>> Claims { get; init; } = [];
    public string AuthenticationType { get; init; } = "Workbench";

    /// <summary>Creates a principal. Claims are simulated and never derived from real identity providers.</summary>
    public ClaimsPrincipal ToPrincipal()
    {
        if (!IsAuthenticated)
            return new ClaimsPrincipal(new ClaimsIdentity());

        var claims = new List<Claim>();
        if (Name is not null)
        {
            claims.Add(new Claim(ClaimTypes.Name, Name));
            claims.Add(new Claim(ClaimTypes.NameIdentifier, Name));
        }

        claims.AddRange(Roles.Select(r => new Claim(ClaimTypes.Role, r)));
        claims.AddRange(Claims.Select(c => new Claim(c.Key, c.Value)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, AuthenticationType, ClaimTypes.Name, ClaimTypes.Role));
    }

    public override string ToString() => IsAuthenticated
        ? $"{Name ?? "(unnamed)"}{(Roles.Count > 0 ? $" [{string.Join(", ", Roles)}]" : "")}"
        : "Anonymous";
}

/// <summary>Fake HTTP backend configuration. Requests never leave the process.</summary>
public sealed class HttpSimulation
{
    /// <summary>Base address of the injected <see cref="System.Net.Http.HttpClient"/>. Uses the reserved <c>.invalid</c> TLD.</summary>
    public const string DefaultBaseAddress = "https://workbench.invalid/";

    public IReadOnlyList<HttpRule> Rules { get; init; } = [];

    /// <summary>Latency applied to every matched rule without its own latency.</summary>
    public TimeSpan Latency { get; init; }

    public HttpStatusCode UnmatchedStatus { get; init; } = HttpStatusCode.NotFound;
    public string BaseAddress { get; init; } = DefaultBaseAddress;
}

/// <summary>How a matched HTTP rule responds.</summary>
public enum HttpFault
{
    None,

    /// <summary>Throws <see cref="System.Net.Http.HttpRequestException"/> with <c>ConnectionError</c>.</summary>
    NetworkError,

    /// <summary>Throws <see cref="TaskCanceledException"/> wrapping a <see cref="TimeoutException"/>, like <c>HttpClient.Timeout</c>.</summary>
    Timeout,
}

/// <summary>A single fake HTTP response rule. Rules are evaluated in declaration order.</summary>
public sealed class HttpRule
{
    /// <summary>HTTP method or <c>*</c>.</summary>
    public string Method { get; init; } = "*";

    /// <summary>
    /// Path pattern. Supports exact paths (<c>/api/items</c>), <c>*</c> (one segment) and <c>**</c> (any suffix) wildcards,
    /// an optional query (<c>/api/items?page=2</c> matches when the listed query values match), and absolute URLs.
    /// </summary>
    public required string Pattern { get; init; }

    public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;
    public string? Body { get; init; }
    public string? ContentType { get; init; }
    public IReadOnlyList<KeyValuePair<string, string>> Headers { get; init; } = [];
    public TimeSpan? Latency { get; init; }
    public HttpFault Fault { get; init; }

    /// <summary>When set, the request waits until the named gate is released (or fails when the gate is failed).</summary>
    public string? Gate { get; init; }

    /// <summary>When set, the rule matches only this many requests and then falls through to later rules.</summary>
    public int? Times { get; init; }

    /// <summary>Custom response factory (not serializable; shown as "custom" in reports).</summary>
    public Func<HttpRequestMessage, ScenarioContext, Task<HttpResponseMessage>>? Handler { get; init; }

    public override string ToString() => $"{Method} {Pattern}";
}
