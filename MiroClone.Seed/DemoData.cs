using System.Security.Cryptography;
using System.Text;

namespace MiroClone.Seed;

/// <summary>
/// The demo estate, and the namespace that keeps it apart from real data: addresses under <c>dev.test</c>, the organization name,
/// the board title prefix and layer ids derived from fixed names. A reset removes only boards inside this namespace. The password is
/// published in the README: it is a development value, never a credential. gitleaks:allow
/// </summary>
internal static class DemoData
{
    public const string OwnerEmail = "owner@dev.test";
    public const string AdminEmail = "admin@dev.test";
    public const string MemberEmail = "member@dev.test";
    public const string OtherEmail = "other@dev.test";
    public const string Password = "Dev-Password-123!"; // gitleaks:allow
    public const string OrganizationName = "Dev Workspace";
    public const string OtherOrganizationName = "Other Workspace";
    public const string BoardPrefix = "Demo: ";
    public const string OtherBoardTitle = $"{BoardPrefix}Other workspace board";

    /// <summary>The boards of the first workspace.</summary>
    public static IReadOnlyList<DemoBoard> Boards { get; } =
    [
        new($"{BoardPrefix}Welcome", Welcome()),
        new($"{BoardPrefix}Sprint planning", Planning()),
        new($"{BoardPrefix}Empty board", []),
    ];

    /// <summary>
    /// The two workspaces. A user belongs to the organization it created or was invited to, and never to both: the web app uses the
    /// single organization in the token (ADR 0024), so the second workspace lets a developer try isolation by hand.
    /// </summary>
    public static IReadOnlyList<DemoWorkspace> Workspaces { get; } =
    [
        new(OrganizationName, [new(OwnerEmail, "Owner"), new(AdminEmail, "Admin"), new(MemberEmail, "Member")], Boards),
        new(OtherOrganizationName, [new(OtherEmail, "Owner")], [new(OtherBoardTitle, OtherBoard())]),
    ];

    public static bool IsDemoBoard(string title) => title.StartsWith(BoardPrefix, StringComparison.Ordinal);

    /// <summary>A stable id for a named layer: the same name gives the same id on every run and on every machine.</summary>
    public static Guid LayerId(string name)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"miroclone-seed/{name}"));
        return new Guid(hash.AsSpan(0, 16));
    }

    private static DemoLayer[] OtherBoard() =>
    [
        Shape("other/frame", "Rectangle", 80, 80, 420, 200, 225, 190, 231),
        Text("other/note", "Note", 110, 110, 360, 100, 255, 249, 196, "This board belongs to Other Workspace. The Dev Workspace users cannot open it."),
    ];

    private static DemoLayer[] Welcome() =>
    [
        Shape("welcome/frame", "Rectangle", 80, 80, 420, 260, 255, 236, 179),
        Shape("welcome/dot", "Ellipse", 560, 120, 160, 160, 187, 222, 251),
        Text("welcome/title", "Note", 110, 110, 360, 120, 255, 249, 196, "Welcome to the demo workspace"),
        Text("welcome/hint", "Text", 110, 270, 360, 40, 255, 236, 179, "Open this board as the other demo user to see it live"),
    ];

    private static DemoLayer[] Planning() =>
    [
        Shape("planning/todo", "Rectangle", 60, 60, 220, 340, 255, 205, 210),
        Shape("planning/doing", "Rectangle", 320, 60, 220, 340, 255, 224, 178),
        Shape("planning/done", "Rectangle", 580, 60, 220, 340, 200, 230, 201),
        Text("planning/note-1", "Note", 80, 120, 180, 90, 255, 249, 196, "Write the first story"),
        Text("planning/note-2", "Note", 340, 120, 180, 90, 255, 249, 196, "Review the board"),
    ];

    private static DemoLayer Shape(string name, string type, double x, double y, double width, double height, byte r, byte g, byte b) =>
        new(LayerId(name), type, x, y, width, height, new DemoColour(r, g, b), null);

    private static DemoLayer Text(string name, string type, double x, double y, double width, double height, byte r, byte g, byte b, string value) =>
        new(LayerId(name), type, x, y, width, height, new DemoColour(r, g, b), value);
}

internal sealed record DemoUser(string Email, string Role);

/// <summary>An organization: the first user founds it (Owner) and invites the others with their roles.</summary>
internal sealed record DemoWorkspace(string Name, IReadOnlyList<DemoUser> Users, IReadOnlyList<DemoBoard> Boards)
{
    public DemoUser Founder => Users[0];
}

internal sealed record DemoBoard(string Title, IReadOnlyList<DemoLayer> Layers);

internal sealed record DemoColour(byte R, byte G, byte B);

/// <summary>A layer as the hub's frozen contract spells it: camelCase keys, the type by name.</summary>
internal sealed record DemoLayer(Guid Id, string Type, double X, double Y, double Width, double Height, DemoColour Fill, string? Value);
