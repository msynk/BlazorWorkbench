# ADR 0001: Modular monolith with a UI-free core

- **Status**: Accepted
- **Date**: 2026-11-02

## Context
The same scenario definitions must serve the interactive UI, unit tests, browser automation, documentation and AI agents. Discovery and validation must work without a browser.

## Decision
Split the product into packages with one-way dependencies:

- `Core`: authoring API, discovery, validation, contracts and redaction. It depends only on `Microsoft.AspNetCore.Components` and the DI abstractions.
- `Components`: the UI and the preview runtime.
- `Hosting`: the ASP.NET Core integration.
- `Testing`: the Playwright runner and reports, without an ASP.NET dependency.
- `Testing.Bunit` and `Cli`.

The product has no separate services and no cloud components.

## Consequences
- Validation, snippets and the skeleton generator run anywhere, including in MCP tools.
- The contracts (`CatalogDto`, `TestRunReport`, `workbench.json`) live in Core and are shared by the host, the CLI and MCP.
- There are more packages to version. All packages are released together with the same version.
