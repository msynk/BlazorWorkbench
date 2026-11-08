using BlazorWorkbench;
using BlazorWorkbench.Demo.Components.Account;

namespace BlazorWorkbench.Demo.Scenarios;

public sealed class WorkspaceSettingsScenarios : ScenarioSet<WorkspaceSettings>
{
    protected override void Configure(ScenarioSetBuilder<WorkspaceSettings> set)
    {
        set.Title("Account/Workspace settings")
           .Tags("authorization", "policies", "showcase")
           .Documentation("""
               One component, six people, no test accounts. Each scenario signs in a simulated user with `.User(...)`, and the
               component's `AuthorizeView Policy="..."` blocks evaluate the **real** policies from `WorkspacePolicies`, which
               the host registers with `services.AddDemoComponents()`.

               | Scenario | Roles and claims | Sees |
               | --- | --- | --- |
               | Viewer | `Viewer` | Read-only settings |
               | Editor | `Editor` | Can rename |
               | Admin | `Admin` | Can rename and invite |
               | Owner on Pro | `Owner`, `plan=pro` | Everything, including billing and the danger zone |
               | Billing contact | `Viewer`, `permission=billing` | Read-only settings, but billing |
               | Signed out | anonymous | A sign-in prompt |

               The simulated principal exists only inside the preview; it never authenticates a real request.
               """);

        set.Setup(s => s.Parameter(w => w.WorkspaceName, "Trail Crew").Parameter(w => w.MemberCount, 12));

        set.Add("Viewer", s => s
            .User("Ada Lovelace", "Viewer")
            .Interaction("settings are read only", i => i
                .ExpectVisible(By.Text("Read only"))
                .ExpectCount(By.Role("button", "Invite people"), 0)
                .ExpectCount(By.Role("button", "Delete workspace"), 0)));

        set.Add("Editor", s => s
            .User("Linus Torvalds", "Editor")
            .Interaction("renames the workspace", i => i
                .ExpectDisabled(By.Role("button", "Save"))
                .Fill(By.Label("Workspace name"), "Summit Crew")
                .Click(By.Role("button", "Save"))
                .ExpectCallback("OnRename", times: 1)));

        set.Add("Admin", s => s
            .User("Margaret Hamilton", "Admin")
            .Interaction("invites people", i => i
                .Click(By.Role("button", "Invite people"))
                .ExpectCallback("OnInvite", times: 1)
                .ExpectCount(By.Role("button", "Manage billing"), 0)));

        set.Add("Owner on Pro", s => s
            .User(u => u.Name("Grace Hopper").Role("Owner").Claim("plan", "pro"))
            .Interaction("can delete the workspace", i => i
                .ExpectVisible(By.Text("Unlimited projects"))
                .Click(By.Role("button", "Delete workspace"))
                .ExpectCallback("OnDelete", times: 1)));

        set.Add("Billing contact", s => s
            .User(u => u.Name("Alan Turing").Role("Viewer").Claim("permission", "billing"))
            .Description("Not an owner, but the permission=billing claim satisfies the ManageBilling policy.")
            .Interaction("manages billing without edit rights", i => i
                .ExpectVisible(By.Text("Read only"))
                .Click(By.Role("button", "Manage billing"))
                .ExpectCallback("OnManageBilling", times: 1)));

        set.Add("Signed out", s => s
            .Anonymous()
            .Interaction("asks to sign in", i => i
                .Click(By.Role("button", "Sign in"))
                .ExpectCallback("OnSignIn", times: 1)));
    }
}

public sealed class SessionTimeoutScenarios : ScenarioSet<SessionTimeout>
{
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 10, 0, 0, TimeSpan.Zero);

    protected override void Configure(ScenarioSetBuilder<SessionTimeout> set)
    {
        set.Title("Account/Session timeout")
           .Tags("clock", "timers", "focus", "showcase")
           .Documentation("""
               Testing "sign out after 15 idle minutes" by hand means waiting 15 minutes. The component takes its time and its
               timers from `TimeProvider`, and the scenarios freeze the clock with `.Clock(...)`, so you can jump straight to any
               moment.

               **Try it**

               - Open *Active* and press **Environment → Clock → +1 min** until the warning appears (after 8 minutes), then
                 **+1 s** to watch the countdown. **+1 h** jumps straight to *signed out*.
               - In *Expiry warning*, focus moves into the dialog (an accessibility requirement), and **Stay signed in** restarts
                 the timer: check **Actions** for `OnExtended`.
               - The bUnit tests in `demo/BlazorWorkbench.Demo.Components.Tests` advance the same fake clock in code.
               """);

        set.Setup(s => s.Clock(Now).Layout(ScenarioLayout.Fullscreen));

        set.Add("Active", s => s
            .Parameter(t => t.LastActivity, Now.AddMinutes(-5))
            .Description("Ten minutes until sign-out; the warning appears two minutes before."));

        set.Add("Expiry warning", s => s
            .Parameter(t => t.LastActivity, Now.AddMinutes(-14))
            .Interaction("moves focus into the warning", i => i
                .ExpectFocused(By.Role("alertdialog"))
                .ExpectText(By.TestId("countdown"), "1:00"))
            .Interaction("stay signed in restarts the session", i => i
                .Click(By.Role("button", "Stay signed in"))
                .ExpectHidden(By.Role("alertdialog"))
                .ExpectText(By.Role("status"), "Session active")
                .ExpectCallback("OnExtended", times: 1))
            .Interaction("sign out now ends the session", i => i
                .Click(By.Role("button", "Sign out now"))
                .ExpectVisible(By.Role("heading", "You have been signed out"))
                .ExpectCallback("OnSignedOut", times: 1)));

        set.Add("Signed out", s => s
            .Parameter(t => t.LastActivity, Now.AddMinutes(-20))
            .Interaction("reports the sign-out once", i => i
                .ExpectVisible(By.Role("heading", "You have been signed out"))
                .ExpectCallback("OnSignedOut", times: 1)
                .Click(By.Role("button", "Sign in again"))
                .ExpectCallback("OnSignIn", times: 1)));

        set.Add("Short timeout", s => s
            .Parameter(t => t.IdleTimeout, TimeSpan.FromMinutes(2))
            .Parameter(t => t.WarningBefore, TimeSpan.FromMinutes(1))
            .Parameter(t => t.LastActivity, Now)
            .Description("Two minutes with a one-minute warning: press +1 min in Environment twice to see both transitions."));
    }
}
