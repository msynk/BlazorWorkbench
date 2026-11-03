# ADR 0003: Scenario service overrides through container rewriting

- **Status**: Accepted
- **Date**: 2026-11-02

## Context
Blazor injects services from the service provider of the renderer, which is the circuit. .NET 10 offers neither injection per component subtree nor a hook that activates properties. Scenarios still need different implementations of the same service type, isolated per preview.

## Options
1. A custom `IComponentActivator`. Rejected: property injection runs after activation and overwrites the values.
2. Replace the circuit's scope factory. Rejected as invasive and fragile.
3. Rewrite the registrations of overridable types to scoped factories that ask the `ScenarioSession` of the scope, and keep the original registration as a keyed fallback.

## Decision
Option 3. During `AddBlazorWorkbench`, discovery computes every service type that a scenario registers. `IHostApplicationBuilder.ConfigureContainer` applies the rewrite after all host registrations. The simulations that stay stable for a session (`HttpClient`, `IHttpClientFactory`, `AuthenticationStateProvider`) are always rewritten. `AuthenticationStateProvider` also implements `IHostEnvironmentAuthenticationStateProvider` so circuit start-up keeps working.

Framework singletons such as Kestrel consume `TimeProvider`, so it stays a singleton. It delegates to the session of the current circuit activity, which is tracked through an `AsyncLocal`: the circuit handler sets it for every inbound activity, and `ScenarioHost` sets it while rendering.

## Consequences
- Overrides are isolated per session and created for each activation.
- Overridden services become scoped. Scope validation rejects host singletons that depend on them at startup; this is documented.
- A service resolved in the same scope before activation cannot be swapped. Workbench detects and reports this.
- An override added through hot reload for a type that was not overridable at startup needs a restart, and the preview reports it.
