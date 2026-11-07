# Diagnostic codes

Codes are stable. Errors fail `workbench validate` and make the scenario's render check fail during `workbench test`. Warnings and infos are reported only.

## Discovery (WB0xxx)

| Code | Severity | Meaning | Remedy |
| --- | --- | --- | --- |
| WB0001 | Warning | Some types of an assembly could not be loaded. | Reference all dependencies of the assembly from the host. |
| WB0002 | Error | A scenario set or provider cannot be created (no public parameterless constructor, or the constructor threw). | Add a constructor, or register an instance with `options.AddScenarios(...)`. |
| WB0003 | Error | A scenario set threw in `Configure`. | Fix `Configure`. The component's other scenarios stay available. |
| WB0004 | Info/Error | An open generic component cannot be rendered. | Use a closed type: `ScenarioSet<Grid<Person>>`. |
| WB0006 | Error | A scenario key is used twice for a component (disambiguated with `-2`). | Rename the scenario or set `.Key(...)`. |
| WB0007 | Warning | Two components produce the same id (disambiguated). | Set a unique `Title`. |
| WB0008 | Error | A scenario provider threw. | Fix the provider. |
| WB0009 | Error | Component metadata could not be read. | Check the component type. |

## Scenario validation (WB1xxx)

| Code | Severity | Meaning |
| --- | --- | --- |
| WB1001 | Error | The value is assigned to something that is not a parameter (and the component does not capture unmatched values). |
| WB1002 | Error | The value type does not match the parameter type, `null` is used for a non-nullable value type, text is used for a non-`RenderFragment` parameter, or a fixture produces the wrong type. |
| WB1003 | Warning | An `[EditorRequired]` parameter is not provided. |
| WB1004 | Warning | A cascading parameter has no value in the scenario (`Task<AuthenticationState>` and `EditContext` are supplied automatically). |
| WB1005 | Error | The component injects a service that neither the host nor the scenario provides. |
| WB1006 | Error | The scenario's service configuration threw. |
| WB1007 | Error | Unknown culture name. |
| WB1008 | Error | Unknown viewport preset. |
| WB1009 | Error | Unknown theme. |
| WB1010 | Error | Invalid HTTP rule (empty pattern, unsupported method, non-positive `Times`). |
| WB1011 | Error | `ExpectCallback` names something that is not an `EventCallback` parameter. |
| WB1012 | Error/Warning | Invalid interaction step (missing target, value or expected value), duplicate interaction name, or an empty interaction. |
| WB1013 | Error | Open generic service types cannot be overridden. |
| WB1014 | Error | Unknown fixture. |
| WB1015 | Info | The component has no scenarios; a default scenario was generated. |
| WB1019 | Warning | The scenario uses an `[Obsolete]` parameter. |
| WB1020 | Error | A visual threshold is outside 0–1. |
| WB1021 | Warning | Accessibility rules are disabled without a justification. |
| WB1022 | Error | `TimeProvider` overridden through `Services`; use `.Clock(...)`. |

## Runtime diagnostics

Runtime diagnostics appear in the **Diagnostics** panel and in reports. Each one carries a timestamp, a scenario id, a source and a severity:

| Source | Examples |
| --- | --- |
| `Scenario` | Activation, setup failures, overrides added after startup, early resolution warnings, clock changes. |
| `Render` | Unhandled exceptions from rendering or event handlers (caught by the error boundary). |
| `Console` | `console.error`/`warn`, uncaught errors, unhandled promise rejections. |
| `Network` | Failed browser `fetch`/XHR requests. |
| `Http` | Fake backend exchanges, unmatched requests, faults. |
| `Resource` | Failed stylesheets, scripts, images and fonts (4xx/5xx or network errors). |
| `JsInterop` | Bridge failures. |
| `Navigation` | Intercepted navigation. |
| `Callback` | Calls of instrumented `EventCallback` parameters. |
| `Focus` | Focus changes (Trace severity). |
| `Platform` | Calls to simulated device services from `.Device(...)`: share requests, opened URIs, location requests, clipboard and preference writes. |
