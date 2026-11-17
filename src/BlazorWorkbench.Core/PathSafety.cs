namespace BlazorWorkbench;

/// <summary>Path containment checks that respect the case sensitivity of the file system.</summary>
internal static class PathSafety
{
    /// <summary>
    /// Ordinal-ignore-case on Windows, ordinal elsewhere. Linux file systems are case-sensitive, so a case-insensitive
    /// prefix check could accept a sibling folder that differs only in case. Where macOS volumes are case-insensitive,
    /// the ordinal check only rejects more, never less.
    /// </summary>
    public static StringComparison Comparison { get; } = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>The matching comparer, for sets and dictionaries keyed by paths.</summary>
    public static StringComparer Comparer { get; } = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>
    /// True when <paramref name="path"/> resolves to a location strictly inside <paramref name="directory"/> (both are made
    /// absolute and normalized first, so <c>..</c> segments cannot escape).
    /// </summary>
    public static bool IsInside(string path, string directory)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(directory);
        var full = Path.GetFullPath(path);
        var root = Path.GetFullPath(directory);
        if (!Path.EndsInDirectorySeparator(root))
            root += Path.DirectorySeparatorChar;
        return full.Length > root.Length && full.StartsWith(root, Comparison);
    }
}
