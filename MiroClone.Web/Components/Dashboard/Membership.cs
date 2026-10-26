namespace MiroClone.Web.Components.Dashboard;

/// <summary>Which signed-in members may invite others to the workspace. authservice enforces it too; this only hides the control.</summary>
public static class Membership
{
    public static bool CanInvite(string? role) =>
        string.Equals(role, "Owner", StringComparison.OrdinalIgnoreCase)
        || string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase);
}
