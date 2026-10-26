namespace MiroClone.Web.Components.Dashboard;

/// <summary>
/// The board title rule as the dashboard applies it before it sends a request: trimmed, 1 to 60 characters. The boards service
/// applies the same rule and answers 400 otherwise; BoardTitleTests pins the two together.
/// </summary>
public static class BoardTitle
{
    public const int MaxLength = 60;

    /// <summary>The trimmed title, or null when it is blank or too long.</summary>
    public static string? Normalize(string? raw)
    {
        var title = raw?.Trim();
        return title is { Length: > 0 and <= MaxLength } ? title : null;
    }

    /// <summary>The message for a title that will not be sent, or null when the title is acceptable.</summary>
    public static string? Error(string? raw) =>
        Normalize(raw) is null ? $"Enter a title of 1 to {MaxLength} characters." : null;
}
