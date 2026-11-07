namespace MiroClone.Web.Account;

/// <summary>
/// The demo logins the development seed creates (MiroClone.Seed, ADR 0050 and 0051), restated here because the web app references no
/// other project. Shown on the sign-in page only when <see cref="DemoLoginHint.Visible"/>. The password is a published development value,
/// never a credential. gitleaks:allow
/// </summary>
public static class DemoLogins
{
    public const string Password = "Dev-Password-123!"; // gitleaks:allow

    public static IReadOnlyList<DemoLogin> All { get; } =
    [
        new("owner@dev.test", "Owner", "Dev Workspace"),
        new("admin@dev.test", "Admin", "Dev Workspace"),
        new("member@dev.test", "Member", "Dev Workspace"),
        new("other@dev.test", "Owner", "Other Workspace"),
    ];
}

public sealed record DemoLogin(string Email, string Role, string Workspace);

/// <summary>
/// Whether the sign-in page lists the demo logins: only in Development and only when seeding is on (<c>Seed:Enabled</c>, the switch the
/// seed itself reads, passed to the web app by the AppHost and by compose). Unset means off, so a missing setting never shows credentials.
/// </summary>
public sealed record DemoLoginHint(bool Visible)
{
    public const string SeedEnabledKey = "Seed:Enabled";

    public static DemoLoginHint From(IConfiguration configuration, IHostEnvironment environment) =>
        new(environment.IsDevelopment() && configuration.GetValue<bool>(SeedEnabledKey));
}
