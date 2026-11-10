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
- **No arbitrary execution.** Tools accept only filters and ids. They cannot run commands, read or write files outside the configured output and baseline directories, or evaluate code.
- **Validated input.** Generated scenario code is text that the agent proposes. It runs only after a developer adds it to the code base, and then goes through normal validation (`validate_scenarios`).
- **Redacted output.** Diagnostics and reports pass through the same redaction as the UI.
- **Audit log.** Every tool call is appended to `{output}/mcp-audit.log` with its timestamp, tool name and arguments.

## Suggested agent workflow

1. `get_component` to understand the parameters.
2. `suggest_scenarios`, then write scenarios into the host project.
3. `validate_scenarios`.
4. `run_tests(filter: "component-id-*", accessibility: true)`.
5. Fix the component, then `run_tests` again to verify.
6. Ask a human to review visual changes before `approve_baselines`.
