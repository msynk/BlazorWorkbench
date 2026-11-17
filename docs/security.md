# Security and privacy

Workbench is a development tool that runs next to application code. It is secure by default; changing that requires an explicit setting.

## Defaults

| Default | Effect |
| --- | --- |
| Development only | `MapBlazorWorkbench` throws outside the `Development` environment unless `AllowNonDevelopmentEnvironments = true`. |
| Loopback only | Requests from non-loopback addresses get `403` unless `AllowRemoteAccess = true`. |
| Local host names only | Without `AllowRemoteAccess`, requests whose `Host` header is not `localhost`, `*.localhost` or a loopback address (`127.0.0.1`, `[::1]`; any port) get `403`. This blocks DNS rebinding, where a foreign site points its own host name at `127.0.0.1` to read the Workbench from a visitor's browser. A local reverse proxy that forwards another host name needs `AllowRemoteAccess` with a policy, or must forward `Host: localhost`. |
| Defensive headers | `X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer`, `Content-Security-Policy: frame-ancestors 'self'`, added to every response of the host that does not already carry the header. A `Content-Security-Policy` (or the other two headers) set by the host is never overwritten. |
| No real network for components | `HttpClient` points at `https://workbench.invalid/`, a reserved domain that never resolves. Unmatched requests return 404 and are reported; they are never passed through. |
| Isolated simulations | Simulated users, clocks and service overrides exist only in the preview session that activated the scenario. Host authorization is never weakened. |
| `noindex` | Workbench pages ask search engines not to index them. |
| Baseline approval from the UI | On for local hosts, off for hosts with `AllowRemoteAccess` unless `AllowApprovalFromUi = true`. The HTTP endpoint (WebAssembly hosts) requires the `X-Workbench-Request` header and rejects cross-site requests (`Sec-Fetch-Site`), and only copies screenshots from inside the results directory. |
| Version control | The explorer's change marks run `git status` (and `git diff` with `ChangesBase`) in the source root, read-only. `ShowChanges = false` turns it off. Base refs (`ChangesBase`, `--changed-since`, the MCP `baseRef` argument) that start with `-` or contain whitespace, control characters or `..` are rejected, and are passed after `--end-of-options`, so they can never be read as git options. A canceled git call is killed. |
| JavaScript interop | Planned answers replace calls only in the preview session of the scenario. Arguments of recorded calls are redacted before they reach diagnostics or the Environment panel. |
| Blazor Hybrid automation | The MAUI app opens its read-only automation API (loopback, `Host` header checked, no CORS) and the WebView2 DevTools port (loopback) only when the CLI starts it with the corresponding environment variables. See [Blazor Hybrid](hybrid.md#security). |
| Simulated device data | `.Device(...)` fakes never call platform APIs. Secure storage values are never written to diagnostics. |
| Editor links | **Edit scenario** and **Edit component** contain absolute paths of the machine running the Workbench. ASP.NET Core hosts offer them only in Development with remote access off; setting `EditorUrlTemplate` yourself shows them to everyone who can open the Workbench. Published catalogs never contain them. |
| Preview commands | A preview obeys messages only from the window that embeds it (the shell), so script in a sibling frame (another preview, the Gallery, the test runner) cannot change its arguments, storage or gates. |
| Referenced catalogs | Only `search.json` of a referenced catalog is read (size and count limits, ids restricted to URL-safe characters), and its pages are framed with `sandbox` without `allow-scripts` and with `referrerpolicy="no-referrer"`. References come from your code, never from requests. |
| Browser storage | Each preview gets its own in-memory `localStorage` and `sessionStorage` (`IsolateBrowserStorage`); what components store never reaches the browser's real storage. |

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
