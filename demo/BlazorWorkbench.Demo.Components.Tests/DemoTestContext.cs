using System.Globalization;
using Bunit;
using BlazorWorkbench.Demo.Components;

namespace BlazorWorkbench.Demo.Components.Tests;

/// <summary>
/// Base class of the demo tests. Each test gets its own context, which hosts one scenario session: the scenario's fake
/// HTTP backend, simulated user, fake clock, services and fixtures are installed exactly as in the Workbench preview.
/// </summary>
public abstract class DemoTestContext : BunitContext
{
    protected DemoTestContext()
    {
        // Scenarios without .Culture(...) render in the current culture; pin it so currency assertions hold on every machine.
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");

        // Dialogs and the wizard move focus with ElementReference.FocusAsync().
        JSInterop.Mode = JSRuntimeMode.Loose;

        // The same registrations as the app and the Workbench hosts (authorization policies, default feature flags).
        Services.AddDemoComponents();
    }
}
