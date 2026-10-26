namespace MiroClone.Web.Components.Dashboard;

/// <summary>
/// The dashboard's list state: the search text, the favourites view and the page. It lives in the address, so a link reopens
/// the same view (blazor-web-app §3). The rules are plain C# so that the pages and the tests agree on one set of them.
/// </summary>
public static class BoardQuery
{
    /// <summary>Matches the boards service default; the service caps a page at 50 (contract).</summary>
    public const int PageSize = 20;

    /// <summary>The trimmed search text, or null when there is none, so an empty box sends no filter.</summary>
    public static string? Search(string? raw) => string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();

    /// <summary>The requested page, never below the first.</summary>
    public static int Page(int? raw) => raw is >= 1 ? raw.Value : 1;

    /// <summary>The page count for a total: an empty list still has one page, so the pager always has a current page.</summary>
    public static int PageCount(int total) => Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));

    public static bool HasPrevious(int page) => page > 1;

    public static bool HasNext(int page, int total) => page < PageCount(total);

    /// <summary>The address that names this view. Defaults are left out, so the team boards page at the root is just "/".</summary>
    public static string Url(string? search, bool favourites, int page)
    {
        var parts = new List<string>(3);
        if (Search(search) is { } text)
        {
            parts.Add("search=" + Uri.EscapeDataString(text));
        }

        if (favourites)
        {
            parts.Add("favourites=true");
        }

        if (page > 1)
        {
            parts.Add("page=" + page.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        return parts.Count == 0 ? "/" : "/?" + string.Join('&', parts);
    }
}
