namespace BlazorWorkbench.Components.Shell;

/// <summary>One browser storage item of the preview.</summary>
public sealed record StorageItem(string Key, string Value);

/// <summary>The preview's <c>localStorage</c>, <c>sessionStorage</c> and script-visible cookies.</summary>
public sealed record StorageSnapshot(StorageItem[]? Local, StorageItem[]? Session, string? Cookies);

/// <summary>A change requested in the Storage panel.</summary>
/// <param name="Op"><c>get</c>, <c>set</c>, <c>remove</c> or <c>clear</c>.</param>
/// <param name="Area"><c>local</c> or <c>session</c>.</param>
/// <param name="Key">The item key for <c>set</c> and <c>remove</c>.</param>
/// <param name="Value">The new value for <c>set</c>.</param>
public sealed record StorageCommand(string Op, string? Area = null, string? Key = null, string? Value = null);

/// <summary>One frame of the compare view: an axis (<c>theme</c>, <c>viewport</c>, ...) and its value.</summary>
public sealed record CompareCell(string Axis, string Value);
