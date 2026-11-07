# Security and privacy

Workbench is a development tool that runs next to application code. It is secure by default; changing that requires an explicit setting.

## Defaults

| Default | Effect |
| --- | --- |
| Development only | `MapBlazorWorkbench` throws outside the `Development` environment unless `AllowNonDevelopmentEnvironments = true`. |
| Loopback only | Requests from non-loopback addresses get `403` unless `AllowRemoteAccess = true`. |
| Defensive headers | `X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer`, `Content-Security-Policy: frame-ancestors 'self'`. |
| No real network for components | `HttpClient` points at `https://workbench.invalid/`, a reserved domain that never resolves. Unmatched requests return 404 and are reported; they are never passed through. |
| Isolated simulations | Simulated users, clocks and service overrides exist only in the preview session that activated the scenario. Host authorization is never weakened. |
| `noindex` | Workbench pages ask search engines not to index them. |
| Blazor Hybrid automation | The MAUI app opens its read-only automation API (loopback, `Host` header checked, no CORS) and the WebView2 DevTools port (loopback) only when the CLI starts it with the corresponding environment variables. See [Blazor Hybrid](hybrid.md#security). |
| Simulated device data | `.Device(...)` fakes never call platform APIs. Secure storage values are never written to diagnostics. |

## Remote access

Shared or hosted previews require authentication:

```csharp
builder.Services.AddAuthentication(...).AddOpenIdConnect(...);
builder.Services.AddAuthorization(o => o.AddPolicy("workbench", p => p.RequireRole("Developers")));
builder.AddBlazorWorkbench(o =>
{
    o.AllowRemoteAccess = true;
    o.RemoteAccessPolicy = "workbench";        // applied to the UI, previews and /_workbench endpoints
    o.AllowNonDevelopmentEnvironments = true;  // if the server is not in Development
});
app.UseAuthentication();
app.UseAuthorization();
app.MapBlazorWorkbench();
```

Start-up fails when remote access is enabled without a policy, unless `AllowAnonymousRemoteAccess = true` is set. Use that only on isolated networks.

## Redaction

Diagnostics, HTTP exchange records, callback arguments, the report and the MCP output pass through the `Redactor`:

- `Authorization`, `Cookie`, `Set-Cookie`, `X-Api-Key` and similar headers are always redacted.
- Query parameters such as `token`, `access_token`, `code`, `key`, `secret`, `password` or `sig`, and credentials embedded in URLs (`https://user:pw@…`).
- Bearer, Basic and Digest credentials, JWTs, and `password=`/`secret:`/`apiKey` pairs in text, JSON and connection strings.
- Custom patterns through `options.Redaction.Patterns`, and custom names through `options.Redaction.SensitiveNames` and `SensitiveHeaders`.

Redaction prefers to remove too much rather than too little. Callback arguments are summarized with a depth and length limit; arbitrary component state is never captured. Diagnostic buffers are bounded (`MaxDiagnostics`, 500 by default).

## Safe rendering

- Markdown documentation is rendered by `SafeMarkdown`. Raw HTML is always escaped. Links are limited to `http`, `https`, `mailto` and relative URLs, and code-block languages are validated.
- Published catalogs and HTML reports HTML-encode all dynamic text.
- The visual file endpoint serves only `.png` files inside the configured baseline and result directories, with path traversal checks.
- Preview messages are accepted only from the same origin and only from the preview iframe.

## Artifacts

| Artifact | Contains | Guidance |
| --- | --- | --- |
| Baselines (committed) | Screenshots of scenarios | Use synthetic fixtures. Review baselines like code. |
| `report.json`, `junit.xml`, `index.html` | Redacted diagnostics, findings, step results | Keep CI retention short (for example 14 days). |
| Playwright traces | DOM snapshots and screenshots of failing interactions | Contain what the scenario renders. Disable them with `--no-trace` for sensitive suites. |
| `mcp-audit.log` | Tool names and arguments | Local file in the output directory. |
| Published catalog | Docs, screenshots, Razor usage | Tag internal components with `internal` (excluded by default). Absolute paths and host details are never included. |

Delete old run artifacts by removing the output directory. Each test run clears `screenshots/`, `traces/` and `failures/` before it starts.

## Custom code

Scenario factories, fixtures, fake services and providers are your own code and run in the host process with its permissions, like unit tests. Workbench does not sandbox them. Do not load scenarios from untrusted sources. The MCP server never executes generated code: it returns code for a developer to review.

## Dependencies

| Package | License | Use |
| --- | --- | --- |
| Microsoft.Playwright | MIT | Browser automation (CLI, testing). |
| Deque.AxeCore.Commons (axe-core 4.14) | MPL-2.0 | Unmodified axe-core script served to previews. |
| bunit | MIT | bUnit integration package only. |
| System.CommandLine | MIT | CLI. |
| ModelContextProtocol | Apache-2.0 | MCP server. |
| Microsoft.Maui.* (Controls, Essentials, WebView.Maui) | MIT | `BlazorWorkbench.Maui` and `BlazorWorkbench.Maui.Essentials` only. |
| Microsoft.Extensions.* / ASP.NET Core | MIT | Runtime. `Microsoft.AspNetCore.Components.WebAssembly.Server` is pinned to 10.0.14 because earlier 10.0.x servicing releases have high-severity advisories (GHSA-g27p-qf6j-c96c, GHSA-qjpx-jqm5-h2c4). |

Restore fails on known vulnerabilities, because NuGet audit warnings are treated as errors. CI and the release workflow also run `dotnet list package --vulnerable --include-transitive`. Releases attach an SPDX SBOM and symbol packages; packages are built deterministically with Source Link.
