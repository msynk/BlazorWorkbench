using System.CommandLine;
using BlazorWorkbench.Cli;

return await CliApp.RunAsync(args);

namespace BlazorWorkbench.Cli
{
    using BlazorWorkbench.Testing;

    /// <summary>Command line definition. Kept separate from <c>Main</c> so parsing can be unit tested.</summary>
    public static class CliApp
    {
        public static readonly Option<string?> Config = new("--config") { Description = "Path to workbench.json (default: searched upward from the current directory).", Recursive = true };
        public static readonly Option<string?> Project = new("--project", "-p") { Description = "Workbench host project (.csproj). Default: 'project' in workbench.json.", Recursive = true };
        public static readonly Option<string?> Url = new("--url") { Description = "Use an already running Workbench host instead of starting one.", Recursive = true };
        public static readonly Option<bool> NoBuild = new("--no-build") { Description = "Do not build the host project before starting it.", Recursive = true };
        public static readonly Option<bool> Verbose = new("--verbose", "-v") { Description = "Show host output.", Recursive = true };
        public static readonly Option<string> Configuration = new("--configuration", "-c") { Description = "Build configuration of the host project.", DefaultValueFactory = _ => "Debug", Recursive = true };
        public static readonly Option<string?> Framework = new("--framework") { Description = "Target framework of a multi-targeted host project, such as a .NET MAUI app (default: 'framework' in workbench.json).", Recursive = true };

        public static async Task<int> RunAsync(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            var root = Build();
            var parse = root.Parse(args);
            if (parse.Errors.Count > 0)
            {
                foreach (var error in parse.Errors)
                    Console.Error.WriteLine(Out.Red("error: ") + error.Message);
                Console.Error.WriteLine("Run 'workbench --help' for usage.");
                return ExitCodes.Usage;
            }

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            try
            {
                // Exceptions are mapped to documented exit codes below instead of System.CommandLine's generic handler.
                return await parse.InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false }, cts.Token);
            }
            catch (WorkbenchInfrastructureException ex)
            {
                Console.Error.WriteLine(Out.Red("infrastructure error: ") + ex.Message);
                return ExitCodes.Infrastructure;
            }
            catch (OperationCanceledException)
            {
                Console.Error.WriteLine("Canceled.");
                return ExitCodes.Infrastructure;
            }
        }

        private static CliContext Context(ParseResult r) =>
            new(r.GetValue(Config), r.GetValue(Project), r.GetValue(Url), r.GetValue(NoBuild), r.GetValue(Verbose)) { Configuration = r.GetValue(Configuration)!, FrameworkOverride = r.GetValue(Framework) };

        public static RootCommand Build()
        {
            var root = new RootCommand("Blazor Workbench: discover, preview, test and document Razor components in isolation.")
            {
                Config, Project, Url, NoBuild, Verbose, Configuration, Framework,
            };

            // list
            var listFilter = new Option<string?>("--filter", "-f") { Description = "Glob filter on scenario ids, component ids or titles (comma separated)." };
            var listJson = new Option<bool>("--json") { Description = "Write JSON to stdout." };
            var list = new Command("list", "List discovered components and scenarios.") { listFilter, listJson };
            list.SetAction((r, ct) => Commands.ListAsync(Context(r), r.GetValue(listFilter), r.GetValue(listJson), ct));
            root.Subcommands.Add(list);

            // validate
            var validateJson = new Option<bool>("--json") { Description = "Write the validation report as JSON to stdout." };
            var warningsAsErrors = new Option<bool>("--warnings-as-errors") { Description = "Fail (exit code 1) on warnings too." };
            var validate = new Command("validate", "Validate scenario definitions without rendering them.") { validateJson, warningsAsErrors };
            validate.SetAction((r, ct) => Commands.ValidateAsync(Context(r), r.GetValue(validateJson), r.GetValue(warningsAsErrors), ct));
            root.Subcommands.Add(validate);

            // test / a11y
            root.Subcommands.Add(TestCommand("test", "Run scenarios in a browser: render health, interaction tests and, optionally, accessibility and visual checks.", a11yOnly: false));
            root.Subcommands.Add(TestCommand("a11y", "Run automated accessibility scans (axe-core) for scenarios.", a11yOnly: true));

            // approve
            var approveFilter = new Option<string?>("--filter", "-f") { Description = "Only approve scenarios matching this glob." };
            var approveOutput = new Option<string?>("--output", "-o") { Description = "Report directory of the run to approve (default: 'output' in workbench.json)." };
            var approveBaselines = new Option<string?>("--baselines") { Description = "Baseline directory (default: 'baselines' in workbench.json)." };
            var approveAll = new Option<bool>("--all") { Description = "Also re-approve screenshots that passed." };
            var prune = new Option<bool>("--prune") { Description = "Delete baselines of scenarios that no longer exist (requires an unfiltered run)." };
            var approve = new Command("approve", "Approve screenshots from the last visual run as baselines (explicit and reviewable).") { approveFilter, approveOutput, approveBaselines, approveAll, prune };
            approve.SetAction(r => Commands.Approve(Context(r), r.GetValue(approveOutput), r.GetValue(approveBaselines), r.GetValue(approveFilter), r.GetValue(approveAll), r.GetValue(prune)));
            root.Subcommands.Add(approve);

            // dev
            var dev = new Command("dev", "Start the Workbench host with 'dotnet watch' for interactive development.");
            dev.SetAction((r, ct) => Commands.DevAsync(Context(r), ct));
            root.Subcommands.Add(dev);

            // publish
            var publishOutput = new Option<string>("--output", "-o") { Description = "Output directory of the static catalog.", DefaultValueFactory = _ => Path.Combine("artifacts", "catalog") };
            var noScreenshots = new Option<bool>("--no-screenshots") { Description = "Skip scenario screenshots (no browser needed)." };
            var exclude = new Option<string[]>("--exclude-tag") { Description = "Leave out components and scenarios with this tag (default: internal).", AllowMultipleArgumentsPerToken = true };
            var version = new Option<string?>("--version-label") { Description = "Version label shown in the catalog." };
            var publish = new Command("publish", "Publish a static, searchable component catalog with documentation and screenshots.") { publishOutput, noScreenshots, exclude, version };
            publish.SetAction((r, ct) => Commands.PublishAsync(Context(r), r.GetValue(publishOutput)!, !r.GetValue(noScreenshots), r.GetValue(exclude) ?? [], r.GetValue(version), ct));
            root.Subcommands.Add(publish);

            // install-browsers
            var browserArg = new Option<string>("--browser") { Description = "chromium, firefox or webkit.", DefaultValueFactory = _ => "chromium" };
            var withDeps = new Option<bool>("--with-deps") { Description = "Also install operating system dependencies (Linux CI)." };
            var install = new Command("install-browsers", "Install the Playwright browser used for tests.") { browserArg, withDeps };
            install.SetAction(r => Commands.InstallBrowsers(r.GetValue(browserArg)!, r.GetValue(withDeps)));
            root.Subcommands.Add(install);

            // init
            var force = new Option<bool>("--force") { Description = "Overwrite an existing workbench.json." };
            var init = new Command("init", "Create workbench.json in the current directory.") { force };
            init.SetAction(r => Commands.Init(r.GetValue(force)));
            root.Subcommands.Add(init);

            // mcp
            var allowApprove = new Option<bool>("--allow-approve") { Description = "Allow the approve_baselines tool (still requires confirm=true per call)." };
            var mcpOutput = new Option<string?>("--output", "-o") { Description = "Report directory used by tools." };
            var mcp = new Command("mcp", "Serve Workbench tools to AI coding agents over the Model Context Protocol (stdio).") { allowApprove, mcpOutput };
            mcp.SetAction((r, ct) =>
            {
                var context = Context(r);
                return McpServerHost.RunAsync(new McpSettings(context, context.OutputDirectory(r.GetValue(mcpOutput)), context.BaselineDirectory(null), r.GetValue(allowApprove)), ct);
            });
            root.Subcommands.Add(mcp);

            return root;
        }

        private static Command TestCommand(string name, string description, bool a11yOnly)
        {
            var filter = new Option<string?>("--filter", "-f") { Description = "Glob filter on scenario ids/titles, 'tag:name', comma separated." };
            var accessibility = new Option<bool>("--a11y") { Description = "Run axe-core accessibility scans." };
            var visual = new Option<bool>("--visual") { Description = "Compare screenshots against approved baselines." };
            var noInteractions = new Option<bool>("--no-interactions") { Description = "Skip interaction tests." };
            var viewport = new Option<string[]>("--viewport") { Description = "Viewport preset(s) (default: workbench.json).", AllowMultipleArgumentsPerToken = true };
            var theme = new Option<string[]>("--theme") { Description = "Theme(s) (default: workbench.json).", AllowMultipleArgumentsPerToken = true };
            var workers = new Option<int?>("--workers", "-j") { Description = "Parallel browser contexts." };
            var retries = new Option<int?>("--retries") { Description = "Retry failed scenarios N times; passes on retry are reported as flaky." };
            var output = new Option<string?>("--output", "-o") { Description = "Report directory (default: 'output' in workbench.json)." };
            var baselines = new Option<string?>("--baselines") { Description = "Baseline directory (default: 'baselines' in workbench.json)." };
            var browser = new Option<string?>("--browser") { Description = "chromium (default), firefox or webkit." };
            var headed = new Option<bool>("--headed") { Description = "Show the browser window." };
            var onlyFailed = new Option<bool>("--only-failed") { Description = "Run only scenarios that failed in the previous report." };
            var failOn = new Option<string?>("--fail-on") { Description = "Lowest axe impact that fails: minor, moderate, serious (default), critical." };
            failOn.AcceptOnlyFromAmong("minor", "moderate", "serious", "critical");
            var noTrace = new Option<bool>("--no-trace") { Description = "Do not record Playwright traces for failing interactions." };
            var allowMissing = new Option<bool>("--allow-missing-baselines") { Description = "Report missing baselines without failing." };
            var timeout = new Option<int?>("--timeout") { Description = "Seconds to wait for each scenario to become ready." };
            var includeAntiAliasing = new Option<bool>("--include-anti-aliasing") { Description = "Count anti-aliasing-only pixel differences as changes." };
            var changedSince = new Option<string?>("--changed-since") { Description = "Run only scenarios affected by files changed since this git ref (falls back to all when unsure)." };

            var command = new Command(name, description)
            {
                filter, accessibility, visual, noInteractions, viewport, theme, workers, retries, output, baselines, browser, headed,
                onlyFailed, failOn, noTrace, allowMissing, timeout, includeAntiAliasing, changedSince,
            };

            command.SetAction((r, ct) =>
            {
                var context = Context(r);
                var config = context.Config;
                var options = new TestRunOptions
                {
                    Filter = r.GetValue(filter) ?? config.Filter,
                    Accessibility = a11yOnly || r.GetValue(accessibility),
                    Visual = !a11yOnly && r.GetValue(visual),
                    Interactions = !a11yOnly && !r.GetValue(noInteractions),
                    Viewports = r.GetValue(viewport) is { Length: > 0 } v ? v : config.Viewports,
                    Themes = r.GetValue(theme) is { Length: > 0 } t ? t : config.Themes,
                    Workers = r.GetValue(workers) ?? config.Workers,
                    Retries = r.GetValue(retries) ?? config.Retries,
                    OutputDirectory = context.OutputDirectory(r.GetValue(output)),
                    BaselineDirectory = context.BaselineDirectory(r.GetValue(baselines)),
                    Browser = r.GetValue(browser) ?? config.Browser,
                    Headed = r.GetValue(headed),
                    AccessibilityFailOn = r.GetValue(failOn) ?? config.Accessibility.FailOn,
                    AccessibilityTags = config.Accessibility.Tags,
                    TraceOnFailure = !r.GetValue(noTrace),
                    FailOnMissingBaseline = !r.GetValue(allowMissing),
                    VisualThreshold = config.Visual.Threshold,
                    MaxDiffPixelRatio = config.Visual.MaxDiffPixelRatio,
                    IgnoreAntiAliasing = config.Visual.IgnoreAntiAliasing && !r.GetValue(includeAntiAliasing),
                    ReadyTimeout = TimeSpan.FromSeconds(r.GetValue(timeout) ?? config.TimeoutSeconds),
                };
                return Commands.TestAsync(context, options, r.GetValue(onlyFailed), r.GetValue(changedSince), ct);
            });
            return command;
        }
    }
}
