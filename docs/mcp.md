# MCP integration for coding agents

`workbench mcp` serves Workbench to AI coding agents over the [Model Context Protocol](https://modelcontextprotocol.io) on stdio. It uses the official `ModelContextProtocol` SDK. The AI integration is optional: Workbench works fully without it.

The tools call the same APIs as the UI and the CLI: the catalog API, the validator and the Playwright runner.

## Tools

| Tool | Kind | Description |
| --- | --- | --- |
| `list_components(filter?)` | read-only | Components and their scenarios. |
| `get_component(id)` | read-only | Parameters, docs, dependencies and scenarios with Razor usage. |
| `validate_scenarios()` | read-only | Errors and warnings with remedies. |
| `suggest_scenarios(id)` | read-only | Missing boundary cases (enum values, nulls, empty or long text, loading, error, offline, signed-in, RTL) and a C# `ScenarioSet` skeleton to review. |
| `list_changed_scenarios(baseRef?)` | read-only | Scenarios affected by uncommitted git changes (and by commits since `baseRef`), including components that render a changed component, with the files that could not be attributed. |
| `find_component_usage(id?, paths?)` | read-only | Where `.razor` files use a component (file and line) and which parameters they set; without `id`, use counts, unused components and deprecated ones still in use. Scans the `workbench.json` directory or subdirectories of it, never outside. |
| `get_scenario_url(id, panel?, args?)` | read-only | The Workbench URL of a scenario (with a panel and inspector arguments) and its bare preview URL, to hand to a person or a browser tool. |
| `screenshot_scenario(id, viewport?, theme?, args?)` | runs a browser | A PNG of the scenario as an image block, so an agent can see the result of an edit; viewport and theme names come from the catalog. Not available for Blazor Hybrid hosts. |
| `run_tests(filter?, accessibility?, visual?, responsive?)` | runs browsers | A summary with failed steps, blocking findings, visual differences and widths that scroll sideways. Never changes baselines. |
| `get_last_report(filter?)` | read-only | Failures and diagnostics from the latest `report.json`. |
| `approve_baselines(filter, confirm)` | **destructive** | Approves screenshots. Refused unless the server runs with `--allow-approve` **and** the call passes `confirm=true`. |

## Client configuration

Claude Code:

```bash
claude mcp add blazor-workbench -- workbench mcp --project path/to/MyWorkbench.csproj
```

Generic client configuration (`.mcp.json` or similar):

```json
{
  "mcpServers": {
    "blazor-workbench": {
      "command": "workbench",
      "args": ["mcp", "--config", "workbench.json"]
    }
  }
}
```

The server starts the host on its first tool call (or attaches with `--url`) and stops it on exit.

## Safety

- **Read-only by default.** Baselines change only with `--allow-approve` and a confirmation argument. A human should review the differences first.
- **No arbitrary execution.** Tools accept only filters, ids and paths inside the Workbench root. They cannot run commands, write files outside the configured output and baseline directories, read files outside the Workbench root, or evaluate code.
- **Validated input.** Generated scenario code is text that the agent proposes. It runs only after a developer adds it to the code base, and then goes through normal validation (`validate_scenarios`).
- **Redacted output.** Diagnostics and reports pass through the same redaction as the UI.
- **Audit log.** Every tool call is appended to `{output}/mcp-audit.log` with its timestamp, tool name and arguments.

## Agent skill

`workbench skill` writes a `SKILL.md` that teaches agents this workflow and the scenario API; see
[the CLI reference](cli.md#workbench-skill--o-path---force).

## Suggested agent workflow

1. `get_component` to understand the parameters.
2. `suggest_scenarios`, then write scenarios into the host project.
3. `validate_scenarios`.
4. `run_tests(filter: "component-id-*", accessibility: true)`.
5. Before changing or removing a parameter, `find_component_usage(id)` shows who sets it.
6. Fix the component, then `list_changed_scenarios` to see everything the edit can affect, and `run_tests` on those scenarios to verify.
   `screenshot_scenario` shows what a scenario looks like after the edit, without running the whole suite.
7. Ask a human to review visual changes before `approve_baselines`.
