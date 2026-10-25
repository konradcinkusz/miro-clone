namespace MiroClone.BoardsService.Boards;

/// <summary>
/// Clamps list inputs instead of rejecting them (service-api-patterns §4): an unclamped limit is a one-line outage,
/// so a page size above the contract's 50 is served as 50, not as an error (ADR 0012).
/// </summary>
public static class ListPaging
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 50;

    /// <summary>The highest page whose offset still fits an int, so Skip cannot overflow.</summary>
    public static readonly int MaxPage = int.MaxValue / MaxPageSize;

    public static int PageSize(int? requested) => Math.Clamp(requested ?? DefaultPageSize, 1, MaxPageSize);

    public static int Page(int? requested) => Math.Clamp(requested ?? 1, 1, MaxPage);
}
