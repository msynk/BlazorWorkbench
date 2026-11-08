# CI setup

The CLI needs no interactive prompts. It returns documented [exit codes](cli.md#exit-codes) and writes artifacts that are useful without rerunning the job.

## GitHub Actions

```yaml
jobs:
  workbench:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: 10.0.x }
      - run: dotnet tool restore                      # BlazorWorkbench.Cli in .config/dotnet-tools.json
      - run: dotnet build -c Release
      - run: dotnet workbench install-browsers --with-deps
      - run: dotnet workbench validate --no-build -c Release
      - run: dotnet workbench test --no-build -c Release --a11y --visual --retries 1
      - uses: actions/upload-artifact@v4
        if: always()
        with:
          name: workbench-report
          path: artifacts/workbench
      - uses: dorny/test-reporter@v1        # optional: JUnit results in the checks tab
        if: always()
        with: { name: Workbench, path: artifacts/workbench/junit.xml, reporter: java-junit }
```

> `-c Release` must match the configuration of the earlier build when you use `--no-build`. The default is `Debug`.

This repository's own workflow is [.github/workflows/ci.yml](../.github/workflows/ci.yml).

## Azure Pipelines

```yaml
steps:
  - task: UseDotNet@2
    inputs: { version: 10.0.x }
  - script: dotnet tool restore && dotnet workbench install-browsers --with-deps
  - script: dotnet workbench test --a11y --visual
  - task: PublishTestResults@2
    condition: always()
    inputs: { testResultsFormat: JUnit, testResultsFiles: artifacts/workbench/junit.xml }
  - publish: artifacts/workbench
    condition: always()
    artifact: workbench-report
```

## .NET MAUI Blazor Hybrid in CI

Automated hybrid runs need a Windows runner (WebView2 is preinstalled on GitHub's Windows images) and the MAUI workload:

```yaml
- run: dotnet workload install maui-windows
- run: dotnet build -c Debug
- run: workbench test --no-build --a11y -p src/Contoso.Workbench.Maui/Contoso.Workbench.Maui.csproj --framework net10.0-windows10.0.19041.0
```

Linux runners can still build and test everything else. This repository uses a solution filter without the MAUI projects (`BlazorWorkbench.Web.slnf`) on Linux.

## Visual baselines in CI

Baselines are stored per platform (`baselines/linux`, `baselines/windows`, …). The first visual run on a new CI platform reports every screenshot as *missing*. To set it up:

1. Download the `workbench-report` artifact.
2. Review `index.html`.
3. Run `workbench approve -o <downloaded report dir>`. Screenshots go to the platform folder that captured them, even when you approve on another OS.
4. Commit the new baselines.

Pin the browser through the `Microsoft.Playwright` version and the runner image so that baselines stay stable. Blazor Hybrid baselines (`windows-hybrid`) depend on the WebView2 runtime of the runner instead; run them as an informational step or pin the runtime (see [Blazor Hybrid](hybrid.md#visual-baselines)). For Linux, run the visual suite in a pinned container image, so you can reproduce and approve Linux baselines locally with Docker; see [visual regression](visual-regression.md#linux-baselines). The report records the browser version and OS of every run.

## Distinguishing failures

| Situation | Exit code | Where to look |
| --- | --- | --- |
| Component defect, failed assertion, accessibility violation, visual change | 1 | `index.html`, `junit.xml`, traces, failure screenshots |
| Invalid scenario definition | 1 (`validate`, or the render check during `test`) | Issue codes and remedies |
| Host did not build or start, browser missing | 3 | `host.log`, console output |
| Wrong arguments | 2 | Console output |

## Incremental runs

- `--changed-since origin/main` runs only the scenarios that the changed files can affect, and falls back to all scenarios when a change cannot be attributed (for example shared code). Check out with full history (`fetch-depth: 0`).
- `--only-failed` reruns the failures recorded in the previous `report.json`. Use it to retry locally or to confirm a fix.
- Filters (`-f "inputs-*"` or `-f "tag:smoke"`) split large catalogs across jobs. Each job writes its own output directory with `-o`.
- Run the whole suite on the main branch. Use change-based selection for pull requests.
