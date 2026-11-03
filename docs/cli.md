# CLI reference

```bash
dotnet tool install -g BlazorWorkbench.Cli    # or a local tool: dotnet new tool-manifest && dotnet tool install BlazorWorkbench.Cli
workbench --help
```

## Global options

| Option | Description |
| --- | --- |
| `--config <path>` | `workbench.json` to use. By default it is searched upward from the current directory. |
| `-p, --project <csproj>` | The Workbench host project. Default: `project` in `workbench.json`, or the only `.csproj` in the current directory. |
| `--url <url>` | Use a running host instead of starting one. |
| `--no-build` | Start the host without building it. |
| `-c, --configuration <name>` | Build configuration of the host (default `Debug`). Must match an earlier build when combined with `--no-build`. |
| `-v, --verbose` | Stream host output to stderr. |

Progress goes to **stderr**. Machine-readable output (`--json`) goes to **stdout**. Colors are disabled when output is redirected or `NO_COLOR` is set.

## Commands

### `workbench init [--force]`
Creates `workbench.json` and detects the host project (a project that references `BlazorWorkbench.Hosting`).

### `workbench list [-f <filter>] [--json]`
Lists components and scenarios. Filters are comma-separated globs on scenario ids, component ids or `Title/Scenario`, or `tag:name`.

### `workbench validate [--json] [--warnings-as-errors]`
Validates every scenario without rendering it ([codes](diagnostics-codes.md)). Exit code 1 on errors, or also on warnings with `--warnings-as-errors`.

### `workbench test [options]`

| Option | Description |
| --- | --- |
| `-f, --filter` | Scenario filter. |
| `--a11y` | Run axe-core scans. |
| `--visual` | Compare screenshots with baselines. |
| `--no-interactions` | Skip interaction tests. |
| `--viewport <names…>`, `--theme <names…>` | Variant matrix. |
| `-j, --workers <n>` | Parallel browser contexts (default from config: 2). |
| `--retries <n>` | Retry failed scenarios; passes on retry are reported as flaky. |
| `--only-failed` | Run only scenarios that failed in the previous `report.json`. |
| `--fail-on <impact>` | Lowest accessibility impact that fails: `minor`, `moderate`, `serious`, `critical`. |
| `--allow-missing-baselines` | Report missing baselines without failing. |
| `--browser <name>` | `chromium` (default), `firefox` or `webkit`. |
| `--headed` | Show the browser window. |
| `--no-trace` | Do not record Playwright traces for failures. |
| `--timeout <seconds>` | Readiness timeout per page (default 30). |
| `-o, --output`, `--baselines` | Directories (default from config). |

### `workbench a11y`
Same as `test --a11y --no-interactions`, without visual checks.

### `workbench approve [-f <filter>] [--all] [--prune] [-o <report dir>] [--baselines <dir>]`
Copies the screenshots of failed or missing visual checks from the last report into the baselines and updates `manifest.json`. See [visual regression](visual-regression.md).

### `workbench publish [-o <dir>] [--no-screenshots] [--exclude-tag <tags…>] [--version-label <v>]`
Publishes a static catalog; see [publishing](publishing.md).

### `workbench dev`
Runs `dotnet watch` on the host project.

### `workbench install-browsers [--browser chromium] [--with-deps]`
Installs the Playwright browser. Use `--with-deps` on Linux CI agents.

### `workbench mcp [--allow-approve] [-o <dir>]`
Starts the MCP server on stdio; see [MCP](mcp.md).

## Exit codes

| Code | Meaning |
| --- | --- |
| 0 | Success. |
| 1 | Test or validation failures. |
| 2 | Usage error. |
| 3 | Infrastructure error (host start-up, browser, cancellation). |

## `workbench.json`

```jsonc
{
  "project": "samples/SampleWorkbench/SampleWorkbench.csproj",   // host project
  "baselines": "samples/SampleWorkbench/baselines",              // commit this directory
  "output": "artifacts/workbench",                               // reports; do not commit
  "browser": "chromium",
  "viewports": ["laptop"],
  "themes": ["light"],
  "workers": 4,
  "timeoutSeconds": 30,
  "retries": 0,
  "filter": null,
  "visual": { "threshold": 0.1, "maxDiffPixelRatio": 0 },
  "accessibility": { "failOn": "serious", "tags": ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa", "best-practice"] }
}
```

Paths are relative to the file. The preview host reads the same file, so the **Visual** panel can find baselines and results. Comments and trailing commas are allowed.
