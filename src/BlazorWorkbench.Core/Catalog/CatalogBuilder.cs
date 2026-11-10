using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorWorkbench.Catalog;

/// <summary>
/// Discovers components and scenarios and produces a <see cref="WorkbenchCatalog"/>. Discovery is deterministic:
/// the same assemblies always yield the same ids in the same order. Failures are recorded as issues and never
/// remove unrelated components.
/// </summary>
public static class CatalogBuilder
{
    /// <param name="options">Discovery options.</param>
    /// <param name="isServiceAvailable">
    /// Optional check for services registered by the host (for example <c>IServiceProviderIsService</c>). When supplied,
    /// missing component dependencies are reported as errors.
    /// </param>
    public static WorkbenchCatalog Build(WorkbenchOptions options, Func<Type, bool>? isServiceAvailable = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        var issues = new List<CatalogIssue>();
        var reader = new ComponentMetadataReader(t => options.ParameterEditors.ContainsKey(t), options.ReadParameterDefaults, options.ResolveSourceLocations);

        var componentAssemblies = options.Assemblies.Distinct().OrderBy(a => a.FullName, StringComparer.Ordinal).ToArray();
        var allAssemblies = componentAssemblies.Concat(options.ScenarioAssemblies).Distinct().OrderBy(a => a.FullName, StringComparer.Ordinal).ToArray();
        var typesByAssembly = allAssemblies.ToDictionary(a => a, a => GetLoadableTypes(a, issues));

        // 1. Scenario sets (discovered + explicit) and providers. A type registered explicitly is not also discovered:
        // the registered instance wins, so scenarios are never declared twice (which would yield '-2' keys and WB0006).
        var explicitSets = options.ScenarioSets.Distinct().ToList();
        var explicitSetTypes = explicitSets.Select(s => s.GetType()).ToHashSet();
        var sets = new List<ScenarioSetDefinition>();
        foreach (var type in typesByAssembly.Values.SelectMany(t => t).Where(t => IsScenarioSetType(t) && !explicitSetTypes.Contains(t)).OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            if (TryCreate<IScenarioSet>(type, issues) is { } set)
                TryBuildSet(set, type, sets, issues);
        }

        foreach (var set in explicitSets)
            TryBuildSet(set, set.GetType(), sets, issues);

        var explicitProviders = options.ScenarioProviders.Distinct().ToList();
        var explicitProviderTypes = explicitProviders.Select(p => p.GetType()).ToHashSet();
        var providers = typesByAssembly.Values.SelectMany(t => t)
            .Where(t => t.IsClass && !t.IsAbstract && !t.ContainsGenericParameters && (t.IsPublic || t.IsNestedPublic) &&
                        typeof(IScenarioProvider).IsAssignableFrom(t) && !t.IsDefined(typeof(WorkbenchIgnoreAttribute)) &&
                        !explicitProviderTypes.Contains(t))
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
            .Select(t => TryCreate<IScenarioProvider>(t, issues))
            .OfType<IScenarioProvider>()
            .Concat(explicitProviders);
        foreach (var provider in providers)
        {
            try
            {
                sets.AddRange(provider.GetScenarioSets());
            }
            catch (Exception ex)
            {
                issues.Add(new(IssueSeverity.Error, "WB0008", $"Scenario provider {provider.GetType().Name} failed: {ex.Message}", TypeName: provider.GetType().FullName,
                    Remedy: "Fix the provider; other scenarios remain available."));
            }
        }

        // 2. Component types: scenario targets, explicit registrations and discovered components.
        var componentTypes = new List<Type>();
        void AddComponent(Type t)
        {
            if (!componentTypes.Contains(t))
                componentTypes.Add(t);
        }

        foreach (var set in sets) AddComponent(set.ComponentType);
        foreach (var type in options.Components) AddComponent(type);

        if (options.IncludeComponentsWithoutScenarios)
        {
            foreach (var assembly in componentAssemblies)
            {
                foreach (var type in typesByAssembly[assembly].OrderBy(t => t.FullName, StringComparer.Ordinal))
                {
                    if (IsDiscoverableComponent(type, options, issues))
                        AddComponent(type);
                }
            }
        }

        // 3. Build entries.
        var entries = new List<ComponentEntry>();
        var usedComponentIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var type in componentTypes)
        {
            if (type.ContainsGenericParameters)
            {
                issues.Add(new(IssueSeverity.Error, "WB0004", $"Open generic component {Naming.FormatTypeName(type)} cannot be rendered.", TypeName: type.FullName,
                    Remedy: $"Register a closed type, for example ScenarioSet<{Naming.FormatTypeName(type).Split('<')[0]}<YourType>>."));
                continue;
            }

            ComponentMetadata metadata;
            try
            {
                metadata = reader.Read(type);
            }
            catch (Exception ex)
            {
                issues.Add(new(IssueSeverity.Error, "WB0009", $"Metadata for {type.FullName} could not be read: {ex.Message}", TypeName: type.FullName));
                continue;
            }

            var componentSets = sets.Where(s => s.ComponentType == type).ToArray();
            var title = componentSets.Select(s => s.Title).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t)) ?? DefaultTitle(type);
            var id = Naming.ToKebabCase(title.Replace('/', '-'));
            if (id.Length == 0) id = "component";
            if (!usedComponentIds.Add(id))
            {
                var original = id;
                for (var n = 2; !usedComponentIds.Add(id = $"{original}-{n}"); n++) { }
                issues.Add(new(IssueSeverity.Warning, "WB0007", $"Component id '{original}' is used by more than one component; {type.FullName} uses '{id}'.",
                    id, TypeName: type.FullName, Remedy: "Give the scenario set a unique Title so ids stay stable."));
            }

            var scenarios = componentSets.SelectMany(s => s.Scenarios).ToList();
            if (scenarios.Count == 0)
            {
                scenarios.Add(new ScenarioDefinition
                {
                    Key = "default",
                    Name = "Default",
                    ComponentType = type,
                    IsGenerated = true,
                    Description = "Generated scenario using the component's default parameter values.",
                });
                issues.Add(new(IssueSeverity.Info, "WB1015", $"{metadata.Name} has no scenarios; a default scenario was generated.", id,
                    Remedy: $"Add a ScenarioSet<{metadata.Name}> to document meaningful states."));
            }

            var componentDesigns = componentSets.SelectMany(s => s.Designs).Distinct().ToArray();
            var usedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var scenario in scenarios)
            {
                var key = scenario.Key.Length > 0 ? scenario.Key : "scenario";
                if (!usedKeys.Add(key))
                {
                    var original = key;
                    for (var n = 2; !usedKeys.Add(key = $"{original}-{n}"); n++) { }
                    issues.Add(new(IssueSeverity.Error, "WB0006", $"Scenario key '{original}' is declared more than once for {metadata.Name}.", id, $"{id}--{key}",
                        Remedy: "Scenario names (or explicit keys) must be unique per component; ids are used in URLs and baselines."));
                }

                scenario.ComponentId = id;
                scenario.Id = $"{id}--{key}";
                if (componentDesigns.Length > 0)
                    scenario.Designs = [.. componentDesigns, .. scenario.Designs.Where(d => !componentDesigns.Contains(d))];
            }

            var deprecation = componentSets.Select(s => s.DeprecationMessage).FirstOrDefault(d => d is not null) ?? metadata.ObsoleteMessage
                              ?? (metadata.IsObsolete ? "This component is obsolete." : null);

            entries.Add(new ComponentEntry
            {
                Id = id,
                Title = title,
                Metadata = metadata,
                Description = componentSets.Select(s => s.Description).FirstOrDefault(d => d is not null) ?? metadata.Summary,
                Category = componentSets.Select(s => s.Category).FirstOrDefault(c => c is not null) ?? (title.Contains('/') ? title.Split('/')[0] : null),
                Tags = componentSets.SelectMany(s => s.Tags).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                Documentation = string.Join("\n\n", componentSets.Select(s => s.Documentation).OfType<string>()) is { Length: > 0 } md ? md : null,
                DeprecationMessage = deprecation,
                Status = componentSets.Select(s => s.Status).FirstOrDefault(s => s is not null) ?? (deprecation is not null ? ComponentStatus.Deprecated : null),
                Designs = componentDesigns,
                Fixtures = componentSets.SelectMany(s => s.Fixtures).ToArray(),
                Scenarios = scenarios,
            });
        }

        entries.Sort((a, b) => string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase) is var c && c != 0 ? c : string.CompareOrdinal(a.Id, b.Id));

        // 4. Validation.
        foreach (var entry in entries)
        {
            foreach (var scenario in entry.Scenarios)
                issues.AddRange(ScenarioValidator.Validate(scenario, entry, options, isServiceAvailable));
        }

        // 5. Relationships: which catalog components render which (read from the compiled render code).
        try
        {
            var byType = entries.GroupBy(e => e.ComponentType).ToDictionary(g => g.Key, g => g.First());
            var uses = ComponentRelations.FindRenderedComponents(byType.Keys.ToArray());
            var usedBy = ComponentRelations.Invert(uses);
            foreach (var entry in entries)
            {
                entry.Uses = uses.TryGetValue(entry.ComponentType, out var u) ? u.Select(t => byType[t].Id).ToArray() : [];
                entry.UsedBy = usedBy.TryGetValue(entry.ComponentType, out var b) ? b.Select(t => byType[t].Id).ToArray() : [];
            }
        }
        catch (Exception ex)
        {
            issues.Add(new(IssueSeverity.Info, "WB0010", $"Component relationships could not be read: {ex.Message}"));
        }

        // 6. Documentation directives ({{scenario:id}} and friends) in pages and component documentation.
        var catalog = new WorkbenchCatalog(entries, issues, options);
        foreach (var page in options.Pages)
            issues.AddRange(ValidateDirectives(page.Markdown, catalog, $"Page '{page.Title}'", null));
        foreach (var entry in entries.Where(e => e.Documentation is not null))
            issues.AddRange(ValidateDirectives(entry.Documentation, catalog, $"The documentation of {entry.Title}", entry.Id));
        foreach (var duplicate in options.Pages.GroupBy(p => p.Id, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            issues.Add(new(IssueSeverity.Warning, "WB1025", $"{duplicate.Count()} documentation pages share the id '{duplicate.Key}'; only the first is reachable.",
                Remedy: "Give every page a unique title."));

        return new WorkbenchCatalog(entries, issues, options);
    }

    private static IEnumerable<CatalogIssue> ValidateDirectives(string? markdown, WorkbenchCatalog catalog, string where, string? componentId)
    {
        foreach (var segment in Documentation.MarkdownDirectives.Split(markdown).Where(s => s.IsDirective))
        {
            var argument = segment.Argument;
            var missing = segment.Directive switch
            {
                "scenario" => argument is null || catalog.FindScenario(argument) is null,
                "scenarios" or "parameters" => argument is null || catalog.FindComponent(argument) is null,
                _ => false,
            };
            if (missing)
                yield return new CatalogIssue(IssueSeverity.Warning, "WB1025",
                    $"{where} embeds {{{{{segment.Directive}:{argument}}}}}, but no {(segment.Directive == "scenario" ? "scenario" : "component")} has the id '{argument}'.",
                    componentId, Remedy: "Use an id from the explorer's URL: scenario/{id} or docs/{id}.");
        }
    }

    /// <summary>Adds dependency validation (WB1005) using the host's service registrations.</summary>
    public static WorkbenchCatalog ValidateDependencies(WorkbenchCatalog catalog, Func<Type, bool> isServiceAvailable) =>
        catalog.WithIssues(catalog.Components.SelectMany(c => c.Scenarios.SelectMany(s => ScenarioValidator.ValidateDependencies(s, c, isServiceAvailable))).ToArray());

    /// <summary>Default catalog path: the namespace relative to the assembly name, then the component name.</summary>
    public static string DefaultTitle(Type type)
    {
        var ns = type.Namespace ?? "";
        var assembly = type.Assembly.GetName().Name ?? "";
        string relative;
        if (ns == assembly) relative = "";
        else if (ns.StartsWith(assembly + ".", StringComparison.Ordinal)) relative = ns[(assembly.Length + 1)..];
        else relative = ns.Contains('.') ? ns[(ns.LastIndexOf('.') + 1)..] : ns;

        var name = Naming.FormatTypeName(type);
        return relative.Length == 0 ? name : $"{relative.Replace('.', '/')}/{name}";
    }

    private static void TryBuildSet(IScenarioSet set, Type type, List<ScenarioSetDefinition> sets, List<CatalogIssue> issues)
    {
        try
        {
            sets.Add(set.Build());
        }
        catch (Exception ex)
        {
            issues.Add(new(IssueSeverity.Error, "WB0003", $"Scenario set {type.Name} threw while configuring: {ex.GetType().Name}: {ex.Message}",
                TypeName: type.FullName, Remedy: "Fix the Configure method; the component is still listed with its other scenarios."));
        }
    }

    // Only public types are discovered; private or internal sets (for example test helpers) must be registered explicitly.
    private static bool IsScenarioSetType(Type t) =>
        t.IsClass && !t.IsAbstract && !t.ContainsGenericParameters && (t.IsPublic || t.IsNestedPublic) &&
        typeof(IScenarioSet).IsAssignableFrom(t) && !t.IsDefined(typeof(WorkbenchIgnoreAttribute));

    private static T? TryCreate<T>(Type type, List<CatalogIssue> issues) where T : class
    {
        try
        {
            if (type.GetConstructor(Type.EmptyTypes) is null)
            {
                issues.Add(new(IssueSeverity.Error, "WB0002", $"{type.Name} needs a public parameterless constructor to be discovered.", TypeName: type.FullName,
                    Remedy: "Add a parameterless constructor or register an instance explicitly in WorkbenchOptions."));
                return null;
            }

            return (T)Activator.CreateInstance(type)!;
        }
        catch (Exception ex)
        {
            var inner = ex is TargetInvocationException { InnerException: { } i } ? i : ex;
            issues.Add(new(IssueSeverity.Error, "WB0002", $"{type.Name} could not be created: {inner.Message}", TypeName: type.FullName));
            return null;
        }
    }

    // Custom parameter editors are Workbench tooling, not components to preview.
    private static bool IsParameterEditor(Type t)
    {
        for (var b = t.BaseType; b is not null; b = b.BaseType)
        {
            if (b.IsGenericType && b.GetGenericTypeDefinition().FullName == "BlazorWorkbench.Components.Shell.ParameterEditorBase`1")
                return true;
        }

        return false;
    }

    private static readonly HashSet<string> s_workbenchAssemblies = new(StringComparer.Ordinal)
    {
        "BlazorWorkbench.Components", "BlazorWorkbench.Hosting", "BlazorWorkbench.Testing.Bunit",
    };

    private static bool IsDiscoverableComponent(Type t, WorkbenchOptions options, List<CatalogIssue> issues)
    {
        if (!t.IsClass || t.IsAbstract || !typeof(IComponent).IsAssignableFrom(t))
            return false;
        if (!(t.IsPublic || t.IsNestedPublic) || t.IsDefined(typeof(CompilerGeneratedAttribute)) || t.Name.StartsWith('<') || t.Name == "_Imports")
            return false;
        if (t.IsDefined(typeof(WorkbenchIgnoreAttribute), false) || typeof(LayoutComponentBase).IsAssignableFrom(t) || IsParameterEditor(t))
            return false;
        var ns = t.Namespace ?? "";
        if (ns.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal) || s_workbenchAssemblies.Contains(t.Assembly.GetName().Name ?? ""))
            return false;
        if (options.ExcludedNamespaces.Any(x => ns == x || ns.StartsWith(x + ".", StringComparison.Ordinal)))
            return false;
        if (!options.IncludeRoutableComponents && t.IsDefined(typeof(RouteAttribute), false))
            return false;
        if (options.ComponentFilter is { } filter && !filter(t))
            return false;
        if (t.ContainsGenericParameters)
        {
            issues.Add(new(IssueSeverity.Info, "WB0004", $"Open generic component {Naming.FormatTypeName(t)} was skipped.", TypeName: t.FullName,
                Remedy: $"Create a ScenarioSet<{Naming.FormatTypeName(t).Split('<')[0]}<YourType>> or call options.AddComponent<...>() with a closed type."));
            return false;
        }

        return true;
    }

    private static Type[] GetLoadableTypes(Assembly assembly, List<CatalogIssue> issues)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            foreach (var loader in ex.LoaderExceptions.OfType<Exception>().Select(e => e.Message).Distinct().Take(5))
            {
                issues.Add(new(IssueSeverity.Warning, "WB0001", $"Some types in {assembly.GetName().Name} could not be loaded: {loader}",
                    Remedy: "Check that all dependencies of the assembly are referenced by the Workbench host."));
            }

            return ex.Types.OfType<Type>().ToArray();
        }
    }

    /// <summary>Runs a scenario's service configurations against an empty collection (used for validation and override planning).</summary>
    internal static (IServiceCollection Services, Exception? Error) DryRunServices(ScenarioDefinition scenario)
    {
        var services = new ServiceCollection();
        try
        {
            var context = new ScenarioContext(scenario, TimeProvider.System, new ScenarioGates(), new ScenarioActivityTracker());
            foreach (var configure in scenario.ServiceConfigurations)
                configure(services, context);
            return (services, null);
        }
        catch (Exception ex)
        {
            return (services, ex);
        }
    }
}
