using BlazorWorkbench.Catalog;
using BlazorWorkbench.Contracts;

namespace BlazorWorkbench.Testing;

/// <summary>Where one catalog component is used: a file and line of a <c>.razor</c> file.</summary>
public sealed record UsageLocation(string File, int Line);

/// <summary>How a catalog component is used by the scanned Razor files.</summary>
/// <param name="Id">The component id.</param>
/// <param name="Title">The catalog title.</param>
/// <param name="Name">The tag name (<c>Button</c>).</param>
/// <param name="Status">The lifecycle status, if declared (Deprecated components still in use are worth a look).</param>
/// <param name="Uses">The number of tags.</param>
/// <param name="Files">The number of files with at least one tag.</param>
/// <param name="Locations">The first locations, in file order.</param>
/// <param name="Parameters">How often each parameter is set, by parameter name; parameters never set are listed with 0.</param>
/// <param name="OtherAttributes">Attributes that match no parameter (splatted to the element, or mistakes), with their counts.</param>
/// <param name="Ambiguous">True when another catalog component has the same tag name, so unqualified tags count for both.</param>
public sealed record ComponentUsageEntry(
    string Id,
    string Title,
    string Name,
    ComponentStatus? Status,
    int Uses,
    int Files,
    IReadOnlyList<UsageLocation> Locations,
    IReadOnlyDictionary<string, int> Parameters,
    IReadOnlyDictionary<string, int> OtherAttributes,
    bool Ambiguous);

/// <summary>The result of <see cref="ComponentUsage.Scan"/>.</summary>
public sealed record ComponentUsageReport(IReadOnlyList<string> Roots, int ScannedFiles, IReadOnlyList<ComponentUsageEntry> Components)
{
    public IEnumerable<ComponentUsageEntry> Unused => Components.Where(c => c.Uses == 0);
}

/// <summary>
/// Counts how Razor files (typically the apps that consume a component library) use the catalog's components: tags,
/// files, and which parameters they set. It reads markup only (<c>.razor</c> files): components created in C#
/// (<c>OpenComponent&lt;T&gt;</c>, <c>DynamicComponent</c>) are not counted, and generic type arguments are ignored.
/// </summary>
public static class ComponentUsage
{
    /// <summary>Directories that never hold source to scan.</summary>
    public static IReadOnlyList<string> SkippedDirectories { get; } = ["bin", "obj", "node_modules", ".git", ".vs", "artifacts", "wwwroot"];

    private const int MaxLocations = 20;
    private const long MaxFileBytes = 2 * 1024 * 1024;

    public static ComponentUsageReport Scan(CatalogDto catalog, IEnumerable<string> roots, IEnumerable<string>? exclude = null)
    {
        var rootList = roots.Select(Path.GetFullPath).Distinct(PathSafety.Comparer).ToList();
        var excludes = (exclude ?? []).ToList();
        var byName = catalog.Components.GroupBy(c => TagName(c.Name), StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var byFullName = catalog.Components.GroupBy(c => TagName(c.FullName), StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var tallies = catalog.Components.ToDictionary(c => c.Id, _ => new Tally(), StringComparer.Ordinal);
        var scanned = 0;

        foreach (var (file, display) in EnumerateFiles(rootList, excludes))
        {
            string text;
            try
            {
                if (new FileInfo(file).Length > MaxFileBytes)
                    continue;
                text = File.ReadAllText(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            scanned++;
            foreach (var tag in RazorTags.Read(text))
            {
                List<ComponentDto>? matches = null;
                if (byFullName.TryGetValue(tag.Name, out var qualified))
                    matches = qualified;
                else if (byName.TryGetValue(tag.Name, out var named))
                    matches = named;
                if (matches is null)
                    continue;

                foreach (var component in matches)
                {
                    var tally = tallies[component.Id];
                    tally.Uses++;
                    tally.Files.Add(display);
                    if (tally.Locations.Count < MaxLocations)
                        tally.Locations.Add(new UsageLocation(display, tag.Line));
                    foreach (var attribute in tag.Attributes)
                        Count(component, tally, attribute);
                    if (tag.Content is { } content)
                        CountContent(component, tally, content);
                }
            }
        }

        var entries = catalog.Components.Select(c =>
        {
            var tally = tallies[c.Id];
            var parameters = c.Parameters.Where(p => p.Kind != ParameterKind.CapturedAttributes && p.Kind != ParameterKind.Cascading)
                .ToDictionary(p => p.Name, p => tally.Parameters.GetValueOrDefault(p.Name), StringComparer.Ordinal);
            return new ComponentUsageEntry(c.Id, c.Title, TagName(c.Name), c.Status, tally.Uses, tally.Files.Count, tally.Locations, parameters,
                tally.Other, byName.TryGetValue(TagName(c.Name), out var same) && same.Count > 1);
        })
        .OrderByDescending(e => e.Uses).ThenBy(e => e.Title, StringComparer.OrdinalIgnoreCase)
        .ToList();
        return new ComponentUsageReport(rootList, scanned, entries);
    }

    /// <summary>The tag name of a type name: generic arity and nested-type separators removed.</summary>
    private static string TagName(string fullName)
    {
        var tick = fullName.IndexOf('`', StringComparison.Ordinal);
        var name = tick < 0 ? fullName : fullName[..tick];
        var generic = name.IndexOf('<', StringComparison.Ordinal);
        return (generic < 0 ? name : name[..generic]).Replace('+', '.');
    }

    private static void Count(ComponentDto component, Tally tally, string attribute)
    {
        // @bind-Value, @bind-Value:after and Value:get set Value; other directives (@ref, @key, @onclick, @attributes) are not parameters.
        var name = attribute;
        if (name.StartsWith("@bind-", StringComparison.Ordinal))
            name = name["@bind-".Length..];
        else if (name.StartsWith('@'))
            return;
        var colon = name.IndexOf(':', StringComparison.Ordinal);
        if (colon >= 0)
        {
            if (name[(colon + 1)..] is not ("get" or "set"))
                return;
            name = name[..colon];
        }

        if (name.Length == 0)
            return;
        // TItem="Product": a generic type argument, not a parameter.
        if (component.IsGeneric && name[0] == 'T' && (name.Length == 1 || char.IsAsciiLetterUpper(name[1])) &&
            !component.Parameters.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
            return;
        // Razor matches component parameters case-insensitively.
        var parameter = component.Parameters.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase) && p.Kind is not (ParameterKind.CapturedAttributes or ParameterKind.Cascading));
        if (parameter is not null)
            tally.Parameters[parameter.Name] = tally.Parameters.GetValueOrDefault(parameter.Name) + 1;
        else
            tally.Other[name] = tally.Other.GetValueOrDefault(name) + 1;
    }

    private static void CountContent(ComponentDto component, Tally tally, string content)
    {
        // Named fragments are child elements with the parameter's name (<Header>...</Header>); anything else is ChildContent.
        var named = false;
        foreach (var parameter in component.Parameters.Where(p => p.Kind is ParameterKind.RenderFragment or ParameterKind.Template && p.Name != "ChildContent"))
        {
            if (RazorTags.HasChildElement(content, parameter.Name))
            {
                tally.Parameters[parameter.Name] = tally.Parameters.GetValueOrDefault(parameter.Name) + 1;
                named = true;
            }
        }

        var child = component.Parameters.FirstOrDefault(p => p.Name == "ChildContent");
        if (child is not null && (!named || RazorTags.HasChildElement(content, "ChildContent")) && !string.IsNullOrWhiteSpace(content))
            tally.Parameters["ChildContent"] = tally.Parameters.GetValueOrDefault("ChildContent") + 1;
    }

    private static IEnumerable<(string Path, string Display)> EnumerateFiles(IReadOnlyList<string> roots, IReadOnlyList<string> excludes)
    {
        var seen = new HashSet<string>(PathSafety.Comparer);
        foreach (var root in roots)
        {
            if (File.Exists(root))
            {
                if (seen.Add(root))
                    yield return (root, Path.GetFileName(root));
                continue;
            }

            if (!Directory.Exists(root))
                continue;
            var pending = new Stack<string>([root]);
            while (pending.Count > 0)
            {
                var directory = pending.Pop();
                IEnumerable<string> files, directories;
                try
                {
                    files = Directory.EnumerateFiles(directory, "*.razor").Order(StringComparer.Ordinal).ToList();
                    directories = Directory.EnumerateDirectories(directory).OrderDescending(StringComparer.Ordinal).ToList();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                foreach (var file in files)
                {
                    var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                    if (excludes.Any(p => Glob.IsMatch(relative, p)) || !seen.Add(file))
                        continue;
                    yield return (file, roots.Count == 1 ? relative : $"{Path.GetFileName(root)}/{relative}");
                }

                foreach (var sub in directories)
                {
                    var name = Path.GetFileName(sub);
                    var relative = Path.GetRelativePath(root, sub).Replace('\\', '/');
                    if (SkippedDirectories.Contains(name, StringComparer.OrdinalIgnoreCase) || name.StartsWith('.') ||
                        excludes.Any(p => Glob.IsMatch(relative, p) || Glob.IsMatch(relative + "/", p)))
                        continue;
                    // Symbolic links are not followed: they can loop or leave the root.
                    if (new DirectoryInfo(sub).LinkTarget is not null)
                        continue;
                    pending.Push(sub);
                }
            }
        }
    }

    private sealed class Tally
    {
        public int Uses;
        public HashSet<string> Files { get; } = new(StringComparer.Ordinal);
        public List<UsageLocation> Locations { get; } = [];
        public Dictionary<string, int> Parameters { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, int> Other { get; } = new(StringComparer.Ordinal);
    }
}

/// <summary>A minimal reader of Razor markup tags: names, attribute names, line numbers and element content.</summary>
internal static class RazorTags
{
    internal sealed record Tag(string Name, int Line, IReadOnlyList<string> Attributes, string? Content);

    /// <summary>Tags whose name starts with an upper-case letter (components; HTML elements are lower case), outside comments.</summary>
    public static IEnumerable<Tag> Read(string text)
    {
        var line = 1;
        var lineAt = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c != '<')
                continue;
            if (Starts(text, i, "<!--"))
            {
                i = Skip(text, i, "-->");
                continue;
            }

            if (i + 1 >= text.Length || !char.IsAsciiLetterUpper(text[i + 1]))
                continue;
            var nameEnd = i + 1;
            while (nameEnd < text.Length && (char.IsAsciiLetterOrDigit(text[nameEnd]) || text[nameEnd] is '_' or '.'))
                nameEnd++;
            if (nameEnd < text.Length && !(char.IsWhiteSpace(text[nameEnd]) || text[nameEnd] is '/' or '>'))
                continue;
            var name = text[(i + 1)..nameEnd];

            // Line numbers are counted lazily, only for tags that are read.
            for (; lineAt < i; lineAt++)
                if (text[lineAt] == '\n') line++;

            var (attributes, end, selfClosing) = ReadAttributes(text, nameEnd);
            if (end < 0)
                yield break;
            string? content = null;
            if (!selfClosing)
            {
                var close = FindClose(text, end + 1, name);
                if (close >= 0)
                    content = text[(end + 1)..close];
            }

            yield return new Tag(name, line, attributes, content);
            // Nested component tags are read too: continue right after this tag's start, not after its content.
            i = end;
        }

        static bool Starts(string s, int at, string value) => string.CompareOrdinal(s, at, value, 0, value.Length) == 0;

        static int Skip(string s, int at, string end)
        {
            var found = s.IndexOf(end, at, StringComparison.Ordinal);
            return found < 0 ? s.Length : found + end.Length - 1;
        }
    }

    /// <summary>Reads attribute names up to the unquoted <c>&gt;</c>. Returns the index of that <c>&gt;</c> (or -1).</summary>
    private static (List<string> Attributes, int End, bool SelfClosing) ReadAttributes(string text, int at)
    {
        var attributes = new List<string>();
        var i = at;
        while (i < text.Length)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '>') return (attributes, i, i > 0 && text[i - 1] == '/');
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '>') return (attributes, i + 1, true);

            var start = i;
            while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] is not ('=' or '>') && !(text[i] == '/' && i + 1 < text.Length && text[i + 1] == '>'))
            {
                if (text[i] == '(') { i = SkipBalanced(text, i, '(', ')'); continue; } // @(expression) or @attributes(...)
                i++;
            }

            if (i > start)
                attributes.Add(text[start..i]);
            while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
            if (i < text.Length && text[i] == '=')
            {
                i++;
                while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
                if (i < text.Length && text[i] is '"' or '\'')
                {
                    var quote = text[i];
                    // "@(expression)" can hold the same quote character: skip the expression before looking for the closing quote.
                    var from = i + 2 < text.Length && text[i + 1] == '@' && text[i + 2] == '(' ? SkipBalanced(text, i + 2, '(', ')') : i + 1;
                    var close = from < text.Length ? text.IndexOf(quote, from) : -1;
                    if (close < 0) return (attributes, -1, false);
                    i = close + 1;
                }
                else if (i + 1 < text.Length && text[i] == '@' && text[i + 1] == '(')
                {
                    i = SkipBalanced(text, i + 1, '(', ')');
                }
                else
                {
                    while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] != '>') i++;
                }
            }
            else if (i == start)
            {
                i++; // an unexpected character: move on
            }
        }

        return (attributes, -1, false);
    }

    private static int SkipBalanced(string text, int at, char open, char close)
    {
        var depth = 0;
        for (var i = at; i < text.Length; i++)
        {
            if (text[i] == open) depth++;
            else if (text[i] == close && --depth == 0) return i + 1;
            else if (text[i] is '"' or '\'')
            {
                var end = text.IndexOf(text[i], i + 1);
                if (end < 0) return text.Length;
                i = end;
            }
        }

        return text.Length;
    }

    /// <summary>The index of the <c>&lt;/name&gt;</c> that closes an element opened before <paramref name="at"/>, counting nested elements of the same name.</summary>
    private static int FindClose(string text, int at, string name)
    {
        var depth = 1;
        var i = at;
        while (i < text.Length)
        {
            var next = text.IndexOf('<', i);
            if (next < 0) return -1;
            if (string.CompareOrdinal(text, next + 1, "/" + name, 0, name.Length + 1) == 0 && IsNameEnd(text, next + 2 + name.Length))
            {
                if (--depth == 0) return next;
            }
            else if (string.CompareOrdinal(text, next + 1, name, 0, name.Length) == 0 && IsNameEnd(text, next + 1 + name.Length))
            {
                var (_, end, selfClosing) = ReadAttributes(text, next + 1 + name.Length);
                if (end < 0) return -1;
                if (!selfClosing) depth++;
                i = end + 1;
                continue;
            }

            i = next + 1;
        }

        return -1;
    }

    private static bool IsNameEnd(string text, int at) => at >= text.Length || char.IsWhiteSpace(text[at]) || text[at] is '>' or '/';

    /// <summary>True when <paramref name="content"/> has a <c>&lt;name&gt;</c> element (a named render fragment).</summary>
    public static bool HasChildElement(string content, string name)
    {
        for (var i = content.IndexOf('<', StringComparison.Ordinal); i >= 0; i = content.IndexOf('<', i + 1))
        {
            if (string.CompareOrdinal(content, i + 1, name, 0, name.Length) == 0 && IsNameEnd(content, i + 1 + name.Length))
                return true;
        }

        return false;
    }
}
