# ADR 0007: Platform-specific visual baselines with explicit approval

- **Status**: Accepted
- **Date**: 2026-11-03

## Context
Font rasterization and antialiasing differ between Windows, Linux and macOS, and between browser builds. With a single set of baselines, comparisons either fail across platforms or need tolerances so loose that real regressions pass.

## Decision
Store baselines under `{baselines}/{platform}/{scenarioId}/{variant}.png`, with `windows`, `linux` or `macos` as the platform. A single `manifest.json` records the hash, size, browser and version, OS, viewport, theme and approval time of every baseline. Each run compares against the baselines of its own platform only. `workbench approve` writes to the platform recorded in the report, so a Linux CI run can be approved on a Windows workstation. Test runs never write baselines.

## Consequences
- Comparisons are stable with strict defaults (`maxDiffPixelRatio = 0`).
- Every platform that runs checks needs its own approved set. The first CI run reports missing baselines until they are approved from the CI artifact.
- Baseline changes show up in pull requests as binary diffs plus manifest changes.
