# ADR 0002: Iframe previews and a host-centric automation contract

- **Status**: Accepted
- **Date**: 2026-11-02

## Context
Previews need CSS and layout isolation, real viewport media queries, state isolation for each preview, and a contract that automation can use. The CLI must not load user assemblies: version conflicts, native dependencies and DI graphs make in-process loading fragile.

## Decision
- The shell renders each scenario in an iframe (`/_workbench/preview/{id}`). Each iframe is its own Blazor circuit with its own DI scope and `ScenarioSession`. Shell and preview communicate through same-origin `postMessage`.
- The host exposes `/_workbench/api/{health,catalog,validate,scenarios/{id}}`. The CLI starts or attaches to a host and drives preview URLs with Playwright.
- Readiness and diagnostics are published on `window.__workbench` and `data-workbench-state`.

## Consequences
- Viewport presets are real iframe sizes. Theme and direction changes need no reload, and Reset creates a fresh circuit.
- Each preview costs one circuit, which is acceptable for a development tool.
- Media-query dark mode cannot be emulated per iframe. It is emulated in automated runs only.
- The bridge message format is internal. `window.__workbench` is public.
