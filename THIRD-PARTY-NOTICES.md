# Third-party notices

Blazor Workbench is licensed under the MIT License (see [LICENSE](LICENSE)). It depends on the following third-party components. They are distributed as NuGet package dependencies and are not vendored into this repository.

| Component | License | Notes |
| --- | --- | --- |
| [axe-core](https://github.com/dequelabs/axe-core) via `Deque.AxeCore.Commons` 4.14.0 | MPL-2.0 | The unmodified `axe.min.js` embedded in Deque's package is served by `BlazorWorkbench.Hosting` at `/_workbench/axe.min.js`. Source: https://github.com/dequelabs/axe-core. Copyright (c) Deque Systems, Inc. |
| `Newtonsoft.Json` (transitive through `Deque.AxeCore.Commons`) | MIT | Copyright (c) James Newton-King. |
| [Microsoft.Playwright](https://github.com/microsoft/playwright-dotnet) 1.64.1 | MIT | Includes the Playwright driver (Node.js and Playwright, Apache-2.0). Browsers are downloaded at runtime by `workbench install-browsers` under their own licenses. |
| [bUnit](https://github.com/bUnit-dev/bUnit) 2.x | MIT | Used only by `BlazorWorkbench.Testing.Bunit`. |
| [System.CommandLine](https://github.com/dotnet/command-line-api) | MIT | CLI parsing. |
| [ModelContextProtocol](https://github.com/modelcontextprotocol/csharp-sdk) 2.3.0 | Apache-2.0 | MCP server in the CLI. |
| Microsoft.AspNetCore.*, Microsoft.Extensions.* (including `Microsoft.Extensions.TimeProvider.Testing` and `Microsoft.AspNetCore.App.Internal.Assets`) | MIT | .NET runtime libraries. |

The image comparison metric follows the YIQ color-difference approach published by Kotsarenko and Ramos ("Measuring perceived color difference using YIQ NTSC transmission color space in mobile applications", 2010), as used by pixelmatch. Workbench contains no pixelmatch code.
