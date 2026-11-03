# ADR 0006: Serializable interaction steps with Playwright as the authoritative runner

- **Status**: Accepted
- **Date**: 2026-11-02

## Context
Interaction tests must run from the CLI, from the UI and through MCP, and they must be readable in reports. C# delegates that use Playwright would force a Playwright dependency on every host and could not run in the UI.

## Decision
Interaction steps are a small serializable DSL in Core: `By.*` locators that mirror the user-facing locators of Playwright, plus actions and assertions. The CLI executes the steps with Playwright, which is the authoritative runner. The UI runs a best-effort JavaScript runner that applies the same strictness rules.

## Consequences
- One definition serves the UI, the CLI, MCP and reports, and it is validated statically (`WB1011`, `WB1012`).
- The DSL is less expressive than raw Playwright. For complex flows, use `WorkbenchHost.PreviewUrl` with your own Playwright tests.
- The two runners can disagree in edge cases such as accessible name computation and the default actions of keys. This is documented.
