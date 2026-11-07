# Publishing a component catalog

`workbench publish` produces a static, self-contained catalog that any static file server can host (GitHub Pages, Azure Static Web Apps, an internal share).

```bash
workbench publish -o artifacts/catalog --version-label 1.4.0
```

The output contains:

| Path | Content |
| --- | --- |
| `index.html` | Components grouped by category, client-side search (names, titles, tags, parameters), coverage summary. |
| `components/{id}.html` | Reference page: summary, Markdown documentation, parameter table (type, default, required, cascading, obsolete, description), dependencies, and every scenario with its screenshot and copyable Razor usage. |
| `screenshots/{scenario}.png` | Screenshots captured in the `laptop` viewport (skip them with `--no-screenshots`). |
| `search.json` | Search index. |
| `coverage.json` | Documentation and scenario coverage: components with scenarios, documented components and parameters, and lists of gaps. |
| `.workbench-catalog` | Marker identifying the folder as a published catalog. |

Publishing into an existing folder replaces only the entries above and keeps anything else (for example a `CNAME` file). The output folder must be new, empty, or a previously published catalog: `publish` refuses any other non-empty folder (such as `.` or `docs`) instead of deleting it.

## What is published, and what is not

The publisher works from the catalog API and screenshots. It never includes:

- host URLs, machine names or environment variables;
- absolute source paths (only paths relative to the `workbench.json` directory are kept);
- diagnostics, logs, traces, fake HTTP exchanges or simulated users;
- components or scenarios tagged `internal`. Add more exclusions with `--exclude-tag secret --exclude-tag wip`.

You publish what the scenarios render and what you write in documentation. Review fixtures and Markdown for sensitive content before you publish externally.

## Keeping docs in sync

Everything comes from the code:

- Summaries and parameter descriptions come from XML documentation comments (`/// <summary>` in `@code` blocks or code-behind). Enable `<GenerateDocumentationFile>true</GenerateDocumentationFile>` in the component library.
- Defaults are read from a freshly constructed component instance.
- Razor usage snippets are generated from the scenario definitions that tests run. The examples are therefore executed, not just written down.
- Deprecation comes from `[Obsolete]` or `set.Deprecated(...)`.

`coverage.json` and the **Overview** page list undocumented components and components without scenarios, so you can gate releases on coverage if you want to.

## Live previews

The static catalog has screenshots, not interactive previews: Interactive Server previews need a running host. To share live previews, run a Workbench host that is protected by authentication; see [remote access](security.md#remote-access).
