using System.Net;

namespace MiroClone.Web.Components.Dashboard;

/// <summary>
/// The words a failed board call shows. The boards service answers with a status code; the page turns it into one sentence that
/// says what happened, and never shows the response body (it may name internals). The fallback is the sentence for any status
/// not named here, so each action keeps its own wording.
/// </summary>
public static class BoardErrors
{
    public static string For(HttpRequestException error, string fallback) => error.StatusCode switch
    {
        HttpStatusCode.NotFound => "This board no longer exists. It may have been deleted.",
        HttpStatusCode.BadRequest => $"Enter a title of 1 to {BoardTitle.MaxLength} characters.",
        HttpStatusCode.Unauthorized => "Your session has ended. Sign in again to continue.",
        HttpStatusCode.Forbidden => "This session has no active workspace, so boards cannot be changed.",
        HttpStatusCode.TooManyRequests => "Too many requests. Wait a moment and try again.",
        _ => fallback,
    };
}
