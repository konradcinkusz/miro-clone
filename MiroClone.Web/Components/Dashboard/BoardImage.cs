namespace MiroClone.Web.Components.Dashboard;

/// <summary>
/// The cover picture for a board. The seed (1..10, contract BoardSummary.imageSeed) names one static SVG under wwwroot/boards.
/// The pictures use only the colours of the token contract (PlaceholderContractTests).
/// </summary>
public static class BoardImage
{
    public const int Count = 10;

    public static string PathFor(int seed) => $"boards/seed-{Math.Clamp(seed, 1, Count).ToString(System.Globalization.CultureInfo.InvariantCulture)}.svg";
}
