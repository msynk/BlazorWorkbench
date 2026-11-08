using Microsoft.AspNetCore.Authorization;

namespace BlazorWorkbench.Demo.Components.Account;

/// <summary>
/// Named authorization policies used by <see cref="WorkspaceSettings"/>. Apps register them with
/// <see cref="DemoComponentsServiceCollectionExtensions.AddDemoComponents"/>; so does every Workbench host, which is
/// how simulated users (<c>.User(...)</c> in scenarios) are evaluated against the same rules as production.
/// </summary>
public static class WorkspacePolicies
{
    /// <summary>Editors, admins and owners can change workspace settings.</summary>
    public const string Edit = "workspace:edit";

    /// <summary>Admins and owners can invite and remove members.</summary>
    public const string ManageMembers = "workspace:members";

    /// <summary>Owners, and anyone with the <c>permission=billing</c> claim, can manage billing.</summary>
    public const string ManageBilling = "workspace:billing";

    /// <summary>Workspaces on the Pro or Enterprise plan (<c>plan</c> claim) get Pro features.</summary>
    public const string ProFeatures = "workspace:pro";

    public static void Configure(AuthorizationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.AddPolicy(Edit, p => p.RequireRole("Editor", "Admin", "Owner"));
        options.AddPolicy(ManageMembers, p => p.RequireRole("Admin", "Owner"));
        options.AddPolicy(ManageBilling, p => p.RequireAssertion(c => c.User.IsInRole("Owner") || c.User.HasClaim("permission", "billing")));
        options.AddPolicy(ProFeatures, p => p.RequireClaim("plan", "pro", "enterprise"));
    }
}
