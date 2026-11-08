using BlazorWorkbench;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using BlazorWorkbench.Demo.Components;
using BlazorWorkbench.Demo.Components.Forms;
using BlazorWorkbench.Demo.Components.Layout;
using BlazorWorkbench.Demo.Fakes;

namespace BlazorWorkbench.Demo.Scenarios;

public sealed class AppHeaderScenarios : ScenarioSet<AppHeader>
{
    protected override void Configure(ScenarioSetBuilder<AppHeader> set)
    {
        set.Title("Layout/App header")
           .Tags("responsive", "feature-flags", "navigation", "showcase")
           .Documentation("""
               The header collapses its navigation behind a **Menu** button below 720 px. Viewport presets make every breakpoint
               reviewable, and automated runs emulate a real phone for the `mobile` preset (touch input, mobile viewport).

               It also reads `IFeatureFlags`. Each scenario states its flags explicitly, three different ways:

               ```csharp
               s.Service<IFeatureFlags>(_ => StaticFeatureFlags.None);                  // an instance per activation
               s.Service<IFeatureFlags, AllFeaturesEnabled>();                         // a type, created with DI
               s.Services(services => services.Replace(ServiceDescriptor.Scoped<IFeatureFlags>(...)));  // full control
               ```

               **Try it**

               - Pick the `mobile` viewport in the toolbar and open the menu, or run the *Phone* interactions.
               - Click a navigation link: the preview stays put, and **Diagnostics** shows the intercepted navigation.
               - Open **Environment**: the *Service sources* table lists `IFeatureFlags` as a scenario override.
               """);

        set.Setup(s => s
            .Service<IFeatureFlags>(_ => StaticFeatureFlags.None)
            .User("Ada Lovelace")
            .Parameter(h => h.Active, "Shop")
            .Parameter(h => h.CartCount, 3));

        set.Add("Desktop", s => s
            .Visual(v => v.Viewports("laptop", "tablet"))
            .Interaction("navigation stays inside the preview", i => i
                .Click(By.Role("link", "Orders"))
                .ExpectVisible(By.Role("link", "Orders"))
                .ExpectNoErrors()));

        set.Add("Phone", s => s
            .Viewport("mobile")
            .Interaction("menu reveals the navigation", i => i
                .ExpectHidden(By.Role("link", "Orders"))
                .Click(By.Role("button", "Menu"))
                .ExpectAttribute(By.Role("button", "Menu"), "aria-expanded", "true")
                .ExpectVisible(By.Role("link", "Orders"))
                .Checkpoint("menu-open")));

        set.Add("Signed out", s => s
            .Anonymous()
            .Parameter(h => h.CartCount, 0)
            .Interaction("offers sign in", i => i
                .Click(By.Role("button", "Sign in"))
                .ExpectCallback("OnSignIn", times: 1)));

        set.Add("Rewards enabled", s => s
            .Service<IFeatureFlags>(_ => new StaticFeatureFlags(DemoFeatures.Rewards))
            .Interaction("shows the flagged link", i => i.ExpectVisible(By.Role("link", "Rewards"))));

        set.Add("Every feature on", s => s
            .Service<IFeatureFlags, AllFeaturesEnabled>()
            .Description("Uses .Service<IFeatureFlags, AllFeaturesEnabled>(): the implementation is created by dependency injection."));

        set.Add("Busy cart (full control)", s => s
            .Services(services => services.Replace(ServiceDescriptor.Scoped<IFeatureFlags>(_ => new StaticFeatureFlags(DemoFeatures.Rewards))))
            .Parameter(h => h.CartCount, 128)
            .Description("Registers the flags with .Services(...), which gives full control over the scenario's service collection."));

        set.Add("Right to left", s => s
            .Culture("ar-SA")
            .User("ليلى أحمد")
            .Parameter(h => h.Brand, "ترِيل")
            .Parameter(h => h.Links, (IReadOnlyList<NavLinkItem>)[new("المتجر", "/shop"), new("المسارات", "/trails"), new("الطلبات", "/orders")])
            .Parameter(h => h.Active, "المتجر"));
    }
}

public sealed class OnboardingWizardScenarios : ScenarioSet<OnboardingWizard>
{
    protected override void Configure(ScenarioSetBuilder<OnboardingWizard> set)
    {
        set.Title("Forms/Onboarding wizard")
           .Tags("forms", "keyboard", "focus", "showcase")
           .Documentation("""
               Multi-step flows are where interaction tests earn their keep. The *Start* scenario's tests double as executable
               documentation of the flow:

               - fill, select, check and uncheck form controls, then go back and forth between steps;
               - assert that focus moves to each new step's heading (`ExpectFocused`), which screen reader users rely on;
               - drive the wizard with the keyboard only (`Press("Enter")`);
               - record visual checkpoints of every step (`Checkpoint("preferences")`) in `--visual` runs.

               *Review step* jumps straight to the last step with `StartStep`, so design review needs no clicking.
               """);

        set.Add("Start", s => s
            .Interaction("next is enabled once a name is entered", i => i
                .ExpectDisabled(By.Role("button", "Next"))
                .Fill(By.Label("Full name"), "Ada Lovelace")
                .ExpectEnabled(By.Role("button", "Next")))
            .Interaction("completes every step", i => i
                .Fill(By.Label("Full name"), "Ada Lovelace")
                .Select(By.Label("Favorite activity"), "Trail running")
                .Click(By.Role("button", "Next"))
                .ExpectFocused(By.Role("heading", "Preferences"))
                .Checkpoint("preferences")
                .Check(By.Label("Imperial"))
                .Uncheck(By.Label("Email me trail news"))
                .ExpectUnchecked(By.Label("Email me trail news"))
                .Click(By.Role("button", "Next"))
                .ExpectText(By.TestId("review"), "Imperial")
                .ExpectText(By.TestId("review"), "Trail running")
                .Checkpoint("review")
                .Click(By.Role("button", "Finish"))
                .ExpectVisible(By.Text("You're all set, Ada!"))
                .ExpectCallback("OnCompleted", times: 1))
            .Interaction("enter continues and back keeps answers", i => i
                .Fill(By.Label("Full name"), "Grace Hopper")
                .Press("Enter", By.Label("Full name"))
                .ExpectFocused(By.Role("heading", "Preferences"))
                .Click(By.Role("button", "Back"))
                .ExpectFocused(By.Role("heading", "Your profile"))
                .ExpectValue(By.Label("Full name"), "Grace Hopper")));

        set.Add("Prefilled", s => s
            .Parameter(w => w.Initial, new OnboardingProfile("Grace Hopper", "Climbing", MeasurementUnits.Imperial, Newsletter: false)));

        set.Add("Review step", s => s
            .Parameter(w => w.Initial, new OnboardingProfile("Grace Hopper", "Climbing", MeasurementUnits.Imperial, Newsletter: false))
            .Parameter(w => w.StartStep, 3)
            .Interaction("finishes from the review", i => i
                .Click(By.Role("button", "Finish"))
                .ExpectCallback("OnCompleted", times: 1)));
    }
}
