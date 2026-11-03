# ADR 0004: Ship Blazor framework scripts through the internal assets package

- **Status**: Accepted
- **Date**: 2026-11-03

## Context
Since .NET 10, the SDK adds `_framework/blazor.web.js` as a static web asset only when a project contains `.razor` files (`RequiresAspNetWebAssets`). A typical Workbench host contains only `Program.cs` and scenario classes, so its previews never started: `blazor.web.js` returned 404. A `buildTransitive` props file cannot fix this, because NuGet restore evaluates projects without the imports that packages provide, so restore never sees the property.

## Decision
`BlazorWorkbench.Hosting` references `Microsoft.AspNetCore.App.Internal.Assets` 10.0.0 with `VersionOverride`, because central package management forbids versions for implicitly defined packages. The `buildTransitive` targets of that package add the framework scripts to consuming web projects.

## Consequences
- Hosts work without extra configuration. A clean consumer project built from the produced packages confirmed this.
- Workbench depends on a package marked *internal*. If the SDK changes the mechanism, Workbench must follow. Hosts can still set `<RequiresAspNetWebAssets>true</RequiresAspNetWebAssets>` themselves.
- When the SDK also adds the package implicitly (hosts with `.razor` files), NuGet uses the higher version.
