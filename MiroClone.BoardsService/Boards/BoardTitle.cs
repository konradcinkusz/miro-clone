using Microsoft.AspNetCore.Http;

namespace MiroClone.BoardsService.Boards;

/// <summary>
/// The one title rule, shared by create and rename: trimmed, 1 to 60 characters (contract). A rejection is a
/// problem-details response naming the field, so the web app can show it next to the input.
/// </summary>
public static class BoardTitle
{
    public const int MaxLength = 60;

    /// <summary>The trimmed title, or null when it is missing, blank or longer than <see cref="MaxLength"/>.</summary>
    public static string? Normalize(string? raw)
    {
        var title = raw?.Trim();
        return title is { Length: > 0 and <= MaxLength } ? title : null;
    }

    public static IResult Invalid() => Results.ValidationProblem(new Dictionary<string, string[]>
    {
        ["title"] = [$"Title must be between 1 and {MaxLength} characters."],
    });
}
