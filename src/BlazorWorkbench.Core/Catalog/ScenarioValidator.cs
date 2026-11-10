using System.Globalization;
using Microsoft.AspNetCore.Components;

namespace BlazorWorkbench.Catalog;

/// <summary>
/// Validates scenario definitions without rendering them. Runs during catalog build, from <c>workbench validate</c>,
/// and before automated execution. Codes are stable (see <c>docs/diagnostics-codes.md</c>).
/// </summary>
public static class ScenarioValidator
{
    private static readonly HashSet<string> s_httpMethods = ["*", "GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS"];

    /// <summary>Service types the preview host always provides through environment simulation.</summary>
    public static IReadOnlySet<string> SimulatedServiceTypeNames { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "System.Net.Http.HttpClient",
        "System.Net.Http.IHttpClientFactory",
        "System.TimeProvider",
        "Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider",
        "BlazorWorkbench.ScenarioGates",
        "BlazorWorkbench.ScenarioContext",
    };

    public static IEnumerable<CatalogIssue> Validate(ScenarioDefinition scenario, ComponentEntry component, WorkbenchOptions options, Func<Type, bool>? isServiceAvailable = null)
    {
        var issues = new List<CatalogIssue>();
        var metadata = component.Metadata;
        void Add(IssueSeverity severity, string code, string message, string? remedy = null) =>
            issues.Add(new CatalogIssue(severity, code, message, component.Id, scenario.Id, metadata.FullName, remedy));

        // Parameters.
        foreach (var assignment in scenario.Parameters)
        {
            var parameter = metadata.FindParameter(assignment.Name);
            if (parameter is null || parameter.Kind == ParameterKind.Cascading)
            {
                if (!metadata.CapturesUnmatchedValues)
                    Add(IssueSeverity.Error, "WB1001", $"'{assignment.Name}' is not a parameter of {metadata.Name}.",
                        $"Known parameters: {string.Join(", ", metadata.Parameters.Where(p => p.Kind != ParameterKind.Cascading).Select(p => p.Name))}.");
                continue;
            }

            switch (assignment.Kind)
            {
                case ParameterValueKind.Literal when !IsAssignable(assignment.Value, parameter.Type):
                    Add(IssueSeverity.Error, "WB1002",
                        $"Value {Naming.FormatValue(assignment.Value)} ({(assignment.Value is null ? "null" : Naming.FormatTypeName(assignment.Value.GetType()))}) cannot be assigned to {parameter.Name} ({parameter.TypeName}).");
                    break;
                case ParameterValueKind.Text when parameter.Type != typeof(RenderFragment):
                    Add(IssueSeverity.Error, "WB1002", $"Text content can only be assigned to RenderFragment parameters; {parameter.Name} is {parameter.TypeName}.");
                    break;
                case ParameterValueKind.Factory when assignment.FixtureName is { } fixtureName:
                    var fixture = scenario.Fixtures.FirstOrDefault(f => f.Name == fixtureName);
                    if (fixture is null)
                        Add(IssueSeverity.Error, "WB1014", $"Fixture '{fixtureName}' used by {parameter.Name} is not declared.",
                            "Declare it with set.Fixture<T>(name, factory).");
                    else if (!parameter.Type.IsAssignableFrom(fixture.ValueType))
                        Add(IssueSeverity.Error, "WB1002", $"Fixture '{fixtureName}' produces {Naming.FormatTypeName(fixture.ValueType)}, not {parameter.TypeName}.");
                    break;
            }

            if (parameter.IsObsolete)
                Add(IssueSeverity.Warning, "WB1019", $"Parameter {parameter.Name} is obsolete: {parameter.ObsoleteMessage}");
        }

        foreach (var required in metadata.Parameters.Where(p => p.IsRequired && scenario.FindParameter(p.Name) is null))
        {
            Add(IssueSeverity.Warning, "WB1003", $"Required parameter {required.Name} ({required.TypeName}) is not provided.",
                $"Set it with .Parameter(c => c.{required.Name}, ...).");
        }

        // Cascading parameters.
        foreach (var cascading in metadata.Parameters.Where(p => p.Kind == ParameterKind.Cascading))
        {
            var supplied = scenario.CascadingValues.Any(c => cascading.Type.IsAssignableFrom(c.ValueType) &&
                                                             (cascading.CascadingName is null || string.Equals(cascading.CascadingName, c.Name, StringComparison.Ordinal)));
            var builtIn = cascading.Type.FullName?.StartsWith("System.Threading.Tasks.Task`1[[Microsoft.AspNetCore.Components.Authorization.AuthenticationState", StringComparison.Ordinal) == true
                          || cascading.Type.Name is "EditContext" or "HttpContext";
            if (!supplied && !builtIn)
                Add(IssueSeverity.Warning, "WB1004", $"Cascading parameter {cascading.Name} ({cascading.TypeName}) has no value in this scenario.",
                    $"Provide one with .Cascade<{cascading.TypeName}>(value{(cascading.CascadingName is null ? "" : $", \"{cascading.CascadingName}\"")}).");
        }

        // Environment.
        var env = scenario.Environment;
        if (env.Culture is { } culture && !IsValidCulture(culture))
            Add(IssueSeverity.Error, "WB1007", $"Culture '{culture}' is not a known culture.", "Use a predefined culture name such as 'en-US' or 'fa-IR'.");
        if (env.Viewport is { } viewport && options.FindViewport(viewport) is null)
            Add(IssueSeverity.Error, "WB1008", $"Viewport '{viewport}' is not defined.", $"Defined viewports: {string.Join(", ", options.Viewports.Select(v => v.Name))}.");
        if (env.Theme is { } theme && options.FindTheme(theme) is null)
            Add(IssueSeverity.Error, "WB1009", $"Theme '{theme}' is not defined.", $"Defined themes: {string.Join(", ", options.Themes.Select(t => t.Name))}.");
        foreach (var v in scenario.Visual.Viewports.Where(v => options.FindViewport(v) is null))
            Add(IssueSeverity.Error, "WB1008", $"Visual viewport '{v}' is not defined.");
        foreach (var t in scenario.Visual.Themes.Where(t => options.FindTheme(t) is null))
            Add(IssueSeverity.Error, "WB1009", $"Visual theme '{t}' is not defined.");

        foreach (var rule in env.Http?.Rules ?? [])
        {
            if (string.IsNullOrWhiteSpace(rule.Pattern))
                Add(IssueSeverity.Error, "WB1010", "An HTTP rule has an empty pattern.");
            if (!s_httpMethods.Contains(rule.Method))
                Add(IssueSeverity.Error, "WB1010", $"HTTP rule '{rule}' uses unsupported method '{rule.Method}'.");
            if (rule.Times is <= 0)
                Add(IssueSeverity.Error, "WB1010", $"HTTP rule '{rule}' has Times({rule.Times}); it must be positive.");
        }

        // Interactions.
        var callbacks = metadata.Parameters.Where(p => p.Kind == ParameterKind.EventCallback).Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var test in scenario.Interactions)
        {
            if (test.Steps.Count == 0)
                Add(IssueSeverity.Warning, "WB1012", $"Interaction '{test.Name}' has no steps.");
            foreach (var step in test.Steps)
            {
                var problem = ValidateStep(step, callbacks);
                if (problem is not null)
                    Add(IssueSeverity.Error, problem.Value.Code, $"Interaction '{test.Name}', step {step}: {problem.Value.Message}");
            }
        }

        if (scenario.Interactions.GroupBy(i => i.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1) is { } duplicate)
            Add(IssueSeverity.Error, "WB1012", $"Interaction name '{duplicate.Key}' is used more than once.");

        foreach (var design in scenario.Designs.Where(d => !IsSafeDesignUrl(d.Url)))
            Add(IssueSeverity.Error, "WB1023", $"Design reference '{design.Url}' is not an http(s) URL or a relative path.",
                "Use an absolute https:// URL or a path served by the host, such as designs/button.png.");

        foreach (var rule in env.PseudoStates.Where(p => p.Selector is not null && string.IsNullOrWhiteSpace(p.Selector)))
            Add(IssueSeverity.Error, "WB1024", $"Forced state {rule.PseudoClass} has an empty selector.", "Pass null to force the state on every element.");

        foreach (var width in scenario.Responsive.Widths.Where(w => w is < BreakpointPreset.MinViewportWidth or > BreakpointPreset.MaxViewportSize))
            Add(IssueSeverity.Error, "WB1026", $"Responsive width {width} is outside {BreakpointPreset.MinViewportWidth}–{BreakpointPreset.MaxViewportSize} px.");
        if (scenario.Responsive.AllowOverflow.Any(string.IsNullOrWhiteSpace))
            Add(IssueSeverity.Error, "WB1026", "Responsive AllowOverflow(...) was given an empty selector.", "Pass the CSS selector of the element that may overflow, such as \".orders-table\".");

        if (scenario.Visual.Threshold is < 0 or > 1 || scenario.Visual.MaxDiffPixelRatio is < 0 or > 1)
            Add(IssueSeverity.Error, "WB1020", "Visual thresholds must be between 0 and 1.");
        if (scenario.Accessibility.DisabledRules.Count > 0 && string.IsNullOrWhiteSpace(scenario.Accessibility.Justification))
            Add(IssueSeverity.Warning, "WB1021", "Accessibility rules are disabled without a justification.");

        // Services.
        var (services, error) = CatalogBuilder.DryRunServices(scenario);
        if (error is not null)
        {
            Add(IssueSeverity.Error, "WB1006", $"Service configuration threw {error.GetType().Name}: {error.Message}",
                "Service callbacks run on every activation; they must not depend on external state.");
        }

        var provided = new HashSet<Type>();
        foreach (var descriptor in services)
        {
            if (descriptor.ServiceType == typeof(TimeProvider))
                Add(IssueSeverity.Error, "WB1022", "TimeProvider cannot be overridden with Services(...).", "Use .Clock(...) to freeze the scenario clock.");
            else if (descriptor.IsKeyedService)
                // A keyed registration must not be treated as an override of the unkeyed service of the same type.
                Add(IssueSeverity.Error, "WB1013", $"Keyed service {Naming.FormatTypeName(descriptor.ServiceType)} (key '{descriptor.ServiceKey}') cannot be overridden per scenario.",
                    "Override the unkeyed service, or register the keyed service in the Workbench host.");
            else if (descriptor.ServiceType.ContainsGenericParameters)
                Add(IssueSeverity.Error, "WB1013", $"Open generic service {Naming.FormatTypeName(descriptor.ServiceType)} cannot be overridden per scenario.",
                    "Register the closed service type instead.");
            else
                provided.Add(descriptor.ServiceType);
        }

        scenario.ServiceTypes = provided.OrderBy(t => t.FullName, StringComparer.Ordinal).ToArray();

        if (isServiceAvailable is not null)
            issues.AddRange(ValidateDependencies(scenario, component, isServiceAvailable));

        return issues;
    }

    /// <summary>Reports component dependencies that neither the host nor the scenario provides (WB1005).</summary>
    public static IEnumerable<CatalogIssue> ValidateDependencies(ScenarioDefinition scenario, ComponentEntry component, Func<Type, bool> isServiceAvailable)
    {
        var metadata = component.Metadata;
        var provided = scenario.ServiceTypes.ToHashSet();
        foreach (var injection in metadata.Injections)
        {
            if (injection.Key is not null || provided.Contains(injection.ServiceType) ||
                SimulatedServiceTypeNames.Contains(injection.ServiceType.FullName ?? ""))
                continue;

            bool available;
            try { available = isServiceAvailable(injection.ServiceType); }
            catch (InvalidOperationException) { available = false; }

            if (!available)
                yield return new CatalogIssue(IssueSeverity.Error, "WB1005",
                    $"{metadata.Name} injects {injection.ServiceTypeName} ({injection.PropertyName}), which is neither registered by the host nor provided by the scenario.",
                    component.Id, scenario.Id, metadata.FullName,
                    $"Provide it with .Service<{injection.ServiceTypeName}>(ctx => ...) or set.Setup(s => s.Services(...)).");
        }
    }

    private static (string Code, string Message)? ValidateStep(InteractionStep step, HashSet<string> callbacks)
    {
        var needsTarget = step.Action is StepAction.Click or StepAction.DoubleClick or StepAction.Hover or StepAction.Focus or StepAction.Blur
            or StepAction.Fill or StepAction.Type or StepAction.Check or StepAction.Uncheck or StepAction.Select or StepAction.WaitFor
            or StepAction.ExpectVisible or StepAction.ExpectHidden or StepAction.ExpectText or StepAction.ExpectValue
            or StepAction.ExpectAttribute or StepAction.ExpectFocused or StepAction.ExpectCount or StepAction.DragTo or StepAction.Upload
            or StepAction.ScrollIntoView or StepAction.ExpectEnabled or StepAction.ExpectDisabled or StepAction.ExpectChecked or StepAction.ExpectUnchecked;
        if (needsTarget && (step.Target is null || string.IsNullOrWhiteSpace(step.Target.Value)))
            return ("WB1012", "a target locator is required.");

        var needsValue = step.Action is StepAction.Fill or StepAction.Type or StepAction.Press or StepAction.Select
            or StepAction.ReleaseGate or StepAction.FailGate or StepAction.ExpectCallback or StepAction.ExpectAttribute or StepAction.Checkpoint;
        if (needsValue && step.Value is null)
            return ("WB1012", "a value is required.");

        if (step.Action is StepAction.ExpectText or StepAction.ExpectValue or StepAction.ExpectAttribute && step.Expected is null)
            return ("WB1012", "an expected value is required.");

        if (step.Action == StepAction.DragTo && (step.Destination is null || string.IsNullOrWhiteSpace(step.Destination.Value)))
            return ("WB1012", "a destination locator is required.");

        if (step.Action == StepAction.Upload && (string.IsNullOrWhiteSpace(step.Value) || step.FileContent is null))
            return ("WB1012", "a file name and content are required.");

        if (step.Action == StepAction.ExpectCallback && step.Value is not null && !callbacks.Contains(step.Value))
            return ("WB1011", $"'{step.Value}' is not an EventCallback parameter.");

        return null;
    }

    /// <summary>Absolute http(s) URLs and relative paths are allowed; other schemes (javascript:, data:, file:) are not.</summary>
    internal static bool IsSafeDesignUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && !url.StartsWith('/'))
            return uri.Scheme is "http" or "https";
        return !url.Contains(':') && !url.StartsWith("//", StringComparison.Ordinal);
    }

    private static bool IsValidCulture(string name)
    {
        try
        {
            CultureInfo.GetCultureInfo(name, predefinedOnly: true);
            return true;
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
    }

    internal static bool IsAssignable(object? value, Type target)
    {
        if (value is null)
            return !target.IsValueType || Nullable.GetUnderlyingType(target) is not null;
        return target.IsInstanceOfType(value) || (Nullable.GetUnderlyingType(target) is { } u && u.IsInstanceOfType(value));
    }
}
