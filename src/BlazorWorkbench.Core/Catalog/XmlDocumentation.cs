using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace BlazorWorkbench.Catalog;

/// <summary>
/// Reads the XML documentation file produced by <c>GenerateDocumentationFile</c> next to an assembly.
/// Razor <c>@code</c> members documented with <c>///</c> comments are included by the Razor compiler.
/// </summary>
public sealed partial class XmlDocumentation
{
    private static readonly ConcurrentDictionary<Assembly, XmlDocumentation?> s_cache = new();
    private readonly Dictionary<string, XElement> _members;

    private XmlDocumentation(Dictionary<string, XElement> members) => _members = members;

    /// <summary>Returns the documentation of <paramref name="assembly"/>, or null when no XML file is available.</summary>
    public static XmlDocumentation? For(Assembly assembly) => s_cache.GetOrAdd(assembly, Load);

    /// <summary>Supplies documentation for an assembly whose XML file cannot be read from disk (for example in WebAssembly).</summary>
    public static void Register(Assembly assembly, string xml) => s_cache[assembly] = Parse(xml);

    /// <summary>Parses documentation XML (exposed for tests and custom documentation providers).</summary>
    public static XmlDocumentation Parse(string xml)
    {
        var doc = XDocument.Parse(xml);
        var members = new Dictionary<string, XElement>(StringComparer.Ordinal);
        foreach (var member in doc.Descendants("member"))
        {
            if (member.Attribute("name")?.Value is { } name)
                members[name] = member;
        }

        return new XmlDocumentation(members);
    }

    public string? GetSummary(string memberId) => Get(memberId, "summary");
    public string? GetRemarks(string memberId) => Get(memberId, "remarks");

    /// <summary>The documentation id of a type without the <c>T:</c> prefix (<c>Ns.Outer.Inner</c>, <c>Ns.Grid`1</c>).</summary>
    public static string TypeId(Type type)
    {
        var name = type.FullName ?? type.Name;
        return name.Replace('+', '.');
    }

    private string? Get(string memberId, string element) =>
        _members.TryGetValue(memberId, out var member) && member.Element(element) is { } e ? Normalize(e) : null;

    private static XmlDocumentation? Load(Assembly assembly)
    {
        try
        {
            if (assembly.IsDynamic || string.IsNullOrEmpty(assembly.Location))
                return null;
            var path = Path.ChangeExtension(assembly.Location, ".xml");
            return File.Exists(path) ? Parse(File.ReadAllText(path)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or XmlException)
        {
            return null;
        }
    }

    private static string? Normalize(XElement element)
    {
        var sb = new StringBuilder();
        Append(element, sb);
        var text = WhitespaceRegex().Replace(sb.ToString(), " ").Trim();
        return text.Length == 0 ? null : text;
    }

    private static void Append(XNode node, StringBuilder sb)
    {
        switch (node)
        {
            case XText text:
                sb.Append(text.Value);
                break;
            case XElement { Name.LocalName: "see" or "seealso" } e:
                var cref = e.Attribute("cref")?.Value ?? e.Attribute("href")?.Value;
                var langword = e.Attribute("langword")?.Value;
                if (!e.IsEmpty) sb.Append(e.Value);
                else if (langword is not null) sb.Append('`').Append(langword).Append('`');
                else if (cref is not null) sb.Append('`').Append(ShortName(cref)).Append('`');
                break;
            case XElement { Name.LocalName: "paramref" or "typeparamref" } e:
                sb.Append('`').Append(e.Attribute("name")?.Value).Append('`');
                break;
            case XElement { Name.LocalName: "c" or "code" } e:
                sb.Append('`').Append(e.Value).Append('`');
                break;
            case XElement { Name.LocalName: "para" or "br" } e:
                sb.Append(' ');
                foreach (var child in e.Nodes()) Append(child, sb);
                sb.Append(' ');
                break;
            case XElement e:
                foreach (var child in e.Nodes()) Append(child, sb);
                break;
        }
    }

    private static string ShortName(string cref)
    {
        var name = cref.Length > 2 && cref[1] == ':' ? cref[2..] : cref;
        var paren = name.IndexOf('(', StringComparison.Ordinal);
        if (paren >= 0) name = name[..paren];
        var dot = name.LastIndexOf('.');
        name = dot >= 0 ? name[(dot + 1)..] : name;
        var tick = name.IndexOf('`', StringComparison.Ordinal);
        return tick >= 0 ? name[..tick] : name;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}

/// <summary>Resolves the source file of a component from its portable PDB (Razor sources map through <c>#line</c> directives).</summary>
public static class SourceLocator
{
    private static readonly ConcurrentDictionary<Assembly, MetadataReaderProvider?> s_pdbs = new();

    public static SourceLocation? Find(Type type)
    {
        try
        {
            var definition = type.IsGenericType ? type.GetGenericTypeDefinition() : type;
            if (s_pdbs.GetOrAdd(definition.Assembly, OpenPdb) is not { } provider)
                return null;

            var reader = provider.GetMetadataReader();
            SourceLocation? fallback = null;
            foreach (var method in definition.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                         .OrderBy(m => m.Name == "BuildRenderTree" ? 0 : 1))
            {
                var handle = MetadataTokens.MethodDebugInformationHandle(method.MetadataToken);
                if (handle.IsNil) continue;
                MethodDebugInformation info;
                try { info = reader.GetMethodDebugInformation(handle); }
                catch (BadImageFormatException) { continue; }

                foreach (var point in info.GetSequencePoints())
                {
                    if (point.IsHidden || point.Document.IsNil) continue;
                    var path = reader.GetString(reader.GetDocument(point.Document).Name);
                    var location = new SourceLocation(path, point.StartLine);
                    if (path.EndsWith(".razor", StringComparison.OrdinalIgnoreCase))
                        return location with { Line = 1 };
                    var normalized = path.Replace('\\', '/');
                    if (!path.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) && !normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase))
                        fallback ??= location;
                }
            }

            return fallback;
        }
        catch (Exception ex) when (ex is BadImageFormatException or IOException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    private static MetadataReaderProvider? OpenPdb(Assembly assembly)
    {
        try
        {
            if (assembly.IsDynamic || string.IsNullOrEmpty(assembly.Location))
                return null;
            var pdb = Path.ChangeExtension(assembly.Location, ".pdb");
            if (!File.Exists(pdb))
                return null;
            var bytes = File.ReadAllBytes(pdb);
            return MetadataReaderProvider.FromPortablePdbImage(System.Collections.Immutable.ImmutableArray.Create(bytes));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BadImageFormatException)
        {
            return null;
        }
    }
}
