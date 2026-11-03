using System.Text.Json.Serialization;

namespace BlazorWorkbench;

/// <summary>Strategy used to find an element. Mirrors Playwright's user-facing locators.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<LocatorKind>))]
public enum LocatorKind
{
    /// <summary>ARIA role with an optional accessible name (<c>By.Role("button", "Save")</c>).</summary>
    Role,
    Text,
    Label,
    Placeholder,
    TestId,
    Css,
}

/// <summary>A serializable element locator.</summary>
public sealed record Locator(LocatorKind Kind, string Value, string? Name = null, bool Exact = false)
{
    public override string ToString() => Kind switch
    {
        LocatorKind.Role when Name is not null => $"role={Value}[name=\"{Name}\"]",
        LocatorKind.Role => $"role={Value}",
        _ => $"{Kind.ToString().ToLowerInvariant()}={Value}",
    };
}

/// <summary>Factory methods for <see cref="Locator"/>. Prefer role, label and text locators; they reflect what users perceive.</summary>
public static class By
{
    public static Locator Role(string role, string? name = null, bool exact = false) => new(LocatorKind.Role, role, name, exact);
    public static Locator Text(string text, bool exact = false) => new(LocatorKind.Text, text, null, exact);
    public static Locator Label(string label, bool exact = false) => new(LocatorKind.Label, label, null, exact);
    public static Locator Placeholder(string placeholder, bool exact = false) => new(LocatorKind.Placeholder, placeholder, null, exact);
    public static Locator TestId(string testId) => new(LocatorKind.TestId, testId, null, true);
    public static Locator Css(string selector) => new(LocatorKind.Css, selector, null, true);
}

/// <summary>Interaction and assertion step kinds.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<StepAction>))]
public enum StepAction
{
    Click,
    DoubleClick,
    Hover,
    Focus,
    Blur,
    Fill,
    Type,
    Press,
    Check,
    Uncheck,
    Select,
    ReleaseGate,
    FailGate,
    WaitFor,
    ExpectVisible,
    ExpectHidden,
    ExpectText,
    ExpectValue,
    ExpectAttribute,
    ExpectFocused,
    ExpectCount,
    ExpectCallback,
    ExpectNoErrors,
    Checkpoint,
    DragTo,
    Upload,
    ScrollIntoView,
    ExpectEnabled,
    ExpectDisabled,
    ExpectChecked,
    ExpectUnchecked,
}

/// <summary>
/// One serializable interaction step. Steps are executed by the Playwright runner (authoritative, used by the CLI),
/// and by the in-browser runner of the Workbench UI (best effort; see the testing guide for differences).
/// </summary>
public sealed record InteractionStep
{
    public required StepAction Action { get; init; }
    public Locator? Target { get; init; }

    /// <summary>Text to fill/type, key to press, option to select, gate name, callback name, attribute name or checkpoint name.</summary>
    public string? Value { get; init; }

    /// <summary>Expected text/value/attribute value.</summary>
    public string? Expected { get; init; }

    /// <summary>Expected count (elements or callback invocations).</summary>
    public int? Count { get; init; }

    /// <summary>Drop target of <see cref="StepAction.DragTo"/>.</summary>
    public Locator? Destination { get; init; }

    /// <summary>Base64 file content of <see cref="StepAction.Upload"/> (<see cref="Value"/> is the file name).</summary>
    public string? FileContent { get; init; }

    /// <summary>MIME type of <see cref="StepAction.Upload"/>.</summary>
    public string? ContentType { get; init; }

    public override string ToString()
    {
        var parts = new List<string> { Action.ToString() };
        if (Target is not null) parts.Add(Target.ToString());
        if (Destination is not null) parts.Add("→ " + Destination);
        if (Value is not null) parts.Add($"\"{Value}\"");
        if (Expected is not null) parts.Add($"=> \"{Expected}\"");
        if (Count is not null) parts.Add($"x{Count}");
        return string.Join(' ', parts);
    }
}

/// <summary>A named sequence of interaction steps executed against a freshly activated scenario.</summary>
public sealed record InteractionTest(string Name, IReadOnlyList<InteractionStep> Steps);

/// <summary>Fluent builder for <see cref="InteractionTest"/> steps.</summary>
public sealed class InteractionBuilder
{
    private readonly List<InteractionStep> _steps = [];

    public IReadOnlyList<InteractionStep> Steps => _steps;

    public InteractionBuilder Click(Locator target) => Add(StepAction.Click, target);
    public InteractionBuilder DoubleClick(Locator target) => Add(StepAction.DoubleClick, target);
    public InteractionBuilder Hover(Locator target) => Add(StepAction.Hover, target);
    public InteractionBuilder Focus(Locator target) => Add(StepAction.Focus, target);
    public InteractionBuilder Blur(Locator target) => Add(StepAction.Blur, target);

    /// <summary>Replaces the value of an input and raises <c>input</c> and <c>change</c> events.</summary>
    public InteractionBuilder Fill(Locator target, string text) => Add(StepAction.Fill, target, text);

    /// <summary>Types text key by key into the focused element or <paramref name="target"/>.</summary>
    public InteractionBuilder Type(Locator target, string text) => Add(StepAction.Type, target, text);

    /// <summary>Presses a key (Playwright key names: <c>Enter</c>, <c>Tab</c>, <c>Shift+Tab</c>, <c>ArrowDown</c>, ...).</summary>
    public InteractionBuilder Press(string key, Locator? target = null) => Add(StepAction.Press, target, key);

    /// <summary>Presses <c>Tab</c> <paramref name="times"/> times.</summary>
    public InteractionBuilder Tab(int times = 1)
    {
        for (var i = 0; i < times; i++)
            Press("Tab");
        return this;
    }

    public InteractionBuilder Check(Locator target) => Add(StepAction.Check, target);
    public InteractionBuilder Uncheck(Locator target) => Add(StepAction.Uncheck, target);
    public InteractionBuilder Select(Locator target, string option) => Add(StepAction.Select, target, option);
    public InteractionBuilder ReleaseGate(string gate) => Add(StepAction.ReleaseGate, null, gate);
    public InteractionBuilder FailGate(string gate) => Add(StepAction.FailGate, null, gate);
    public InteractionBuilder WaitFor(Locator target) => Add(StepAction.WaitFor, target);
    public InteractionBuilder ExpectVisible(Locator target) => Add(StepAction.ExpectVisible, target);
    public InteractionBuilder ExpectHidden(Locator target) => Add(StepAction.ExpectHidden, target);

    /// <summary>Expects the element text to contain <paramref name="text"/>.</summary>
    public InteractionBuilder ExpectText(Locator target, string text) => Add(StepAction.ExpectText, target, expected: text);

    public InteractionBuilder ExpectValue(Locator target, string value) => Add(StepAction.ExpectValue, target, expected: value);

    public InteractionBuilder ExpectAttribute(Locator target, string attribute, string value) =>
        Add(StepAction.ExpectAttribute, target, attribute, value);

    public InteractionBuilder ExpectFocused(Locator target) => Add(StepAction.ExpectFocused, target);
    public InteractionBuilder ExpectCount(Locator target, int count) => Add(StepAction.ExpectCount, target, count: count);

    /// <summary>Expects an <c>EventCallback</c> parameter to have been invoked (exactly <paramref name="times"/> times when given).</summary>
    public InteractionBuilder ExpectCallback(string parameterName, int? times = null) =>
        Add(StepAction.ExpectCallback, null, parameterName, count: times);

    /// <summary>Expects no error diagnostics (exceptions, console errors, failed resources) so far.</summary>
    public InteractionBuilder ExpectNoErrors() => Add(StepAction.ExpectNoErrors);

    /// <summary>Captures a named visual checkpoint (compared against its own baseline in visual runs).</summary>
    public InteractionBuilder Checkpoint(string name) => Add(StepAction.Checkpoint, null, name);

    /// <summary>Drags <paramref name="source"/> onto <paramref name="destination"/> (HTML5 drag and drop).</summary>
    public InteractionBuilder DragTo(Locator source, Locator destination)
    {
        _steps.Add(new InteractionStep { Action = StepAction.DragTo, Target = source, Destination = destination });
        return this;
    }

    /// <summary>Selects a file in a file input (works with <c>InputFile</c>). The content is embedded in the step.</summary>
    public InteractionBuilder Upload(Locator input, string fileName, byte[] content, string contentType = "application/octet-stream")
    {
        _steps.Add(new InteractionStep { Action = StepAction.Upload, Target = input, Value = fileName, FileContent = Convert.ToBase64String(content), ContentType = contentType });
        return this;
    }

    /// <summary>Selects a text file in a file input.</summary>
    public InteractionBuilder Upload(Locator input, string fileName, string text, string contentType = "text/plain") =>
        Upload(input, fileName, System.Text.Encoding.UTF8.GetBytes(text), contentType);

    public InteractionBuilder ScrollIntoView(Locator target) => Add(StepAction.ScrollIntoView, target);
    public InteractionBuilder ExpectEnabled(Locator target) => Add(StepAction.ExpectEnabled, target);
    public InteractionBuilder ExpectDisabled(Locator target) => Add(StepAction.ExpectDisabled, target);
    public InteractionBuilder ExpectChecked(Locator target) => Add(StepAction.ExpectChecked, target);
    public InteractionBuilder ExpectUnchecked(Locator target) => Add(StepAction.ExpectUnchecked, target);

    private InteractionBuilder Add(StepAction action, Locator? target = null, string? value = null, string? expected = null, int? count = null)
    {
        _steps.Add(new InteractionStep { Action = action, Target = target, Value = value, Expected = expected, Count = count });
        return this;
    }
}
