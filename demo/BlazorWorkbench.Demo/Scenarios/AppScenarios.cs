using BlazorWorkbench;
using BlazorWorkbench.Demo.Components;
using BlazorWorkbench.Demo.Components.Account;
using BlazorWorkbench.Demo.Components.Forms;
using BlazorWorkbench.Demo.Components.Overlays;

namespace BlazorWorkbench.Demo.Scenarios;

public sealed class UserGreetingScenarios : ScenarioSet<UserGreeting>
{
    protected override void Configure(ScenarioSetBuilder<UserGreeting> set)
    {
        set.Title("Account/User greeting").Tags("authorization");
        set.Documentation("Authentication is simulated per scenario. The simulated principal exists only inside the preview.");

        set.Add("Anonymous", s => s
            .Anonymous()
            .Interaction("offers sign in", i => i.Click(By.Role("button", "Sign in")).ExpectCallback("OnSignIn", times: 1)));
        set.Add("Signed in", s => s.User("Ada Lovelace"));
        set.Add("Administrator", s => s
            .User(u => u.Name("Grace Hopper").Role("Admin").Claim("department", "Engineering"))
            .Interaction("shows admin tools", i => i
                .ExpectVisible(By.Text("Administrator"))
                .Click(By.Role("button", "Manage users"))
                .ExpectCallback("OnManage", times: 1)));
    }
}

public sealed class SignupFormScenarios : ScenarioSet<SignupForm>
{
    protected override void Configure(ScenarioSetBuilder<SignupForm> set)
    {
        set.Title("Forms/Signup form").Description("A sign-up form with data annotation validation.").Tags("forms", "validation");
        set.Fixture<SignupModel>("Prefilled", _ => new SignupModel { Name = "Ada", Email = "ada@example.com", AcceptTerms = true });

        set.Add("Empty", s => s
            .Interaction("shows validation errors", i => i
                .Click(By.Role("button", "Create account"))
                .ExpectVisible(By.Text("Name is required."))
                .ExpectCount(By.Css(".validation-message"), 3)
                .ExpectCallback("OnSubmit", times: 0))
            .Interaction("submits valid data", i => i
                .Fill(By.Label("Name"), "Ada")
                .Fill(By.Label("Email"), "ada@example.com")
                .Check(By.Label("I accept the terms"))
                .Click(By.Role("button", "Create account"))
                .ExpectVisible(By.Text("Thanks, Ada!"))
                .ExpectCallback("OnSubmit", times: 1)));

        set.Add("Prefilled", s => s.Fixture(f => f.Initial, "Prefilled").Parameter(f => f.SubmitText, "Continue"));
    }
}

public sealed class DialogScenarios : ScenarioSet<Dialog>
{
    protected override void Configure(ScenarioSetBuilder<Dialog> set)
    {
        set.Title("Overlays/Dialog").Description("A modal dialog that moves focus into itself when it opens and closes on Escape.").Tags("focus", "keyboard");
        set.Setup(s => s
            .Parameter(d => d.Title, "Discard draft?")
            .ChildContent("Your unsaved changes will be lost.")
            .Layout(ScenarioLayout.Fullscreen));

        set.Add("Open", s => s
            .Parameter(d => d.Open, true)
            .Parameter(d => d.ConfirmText, "Discard")
            .Interaction("receives focus and closes on Escape", i => i
                .ExpectFocused(By.Role("dialog"))
                .Press("Escape")
                .ExpectHidden(By.Role("dialog"))
                .ExpectCallback("OpenChanged", times: 1))
            .Interaction("confirm raises OnConfirm", i => i
                .Click(By.Role("button", "Discard"))
                .ExpectCallback("OnConfirm", times: 1)
                .ExpectHidden(By.Role("dialog"))));

        set.Add("Closed", s => s.Parameter(d => d.Open, false).Visual(v => v.Skip("Renders nothing.")));
    }
}

public sealed class AvatarUploadScenarios : ScenarioSet<AvatarUpload>
{
    private static readonly byte[] Pixel = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    protected override void Configure(ScenarioSetBuilder<AvatarUpload> set)
    {
        set.Title("Forms/Avatar upload").Description("Picks an image with InputFile, shows its name and size, and rejects files above a size limit.").Tags("forms", "files");
        set.Add("Default", s => s
            .Interaction("selecting a file shows its name", i => i
                .Upload(By.Label("Profile picture"), "me.png", Pixel, "image/png")
                .ExpectText(By.TestId("selected-file"), "me.png")
                .ExpectCallback("OnSelected", times: 1)));
        set.Add("Too large", s => s
            .Parameter(a => a.MaxBytes, 10)
            .Interaction("rejects large files", i => i
                .Upload(By.Label("Profile picture"), "big.png", Pixel, "image/png")
                .ExpectVisible(By.Role("alert"))
                .ExpectCallback("OnSelected", times: 0)));
    }
}
