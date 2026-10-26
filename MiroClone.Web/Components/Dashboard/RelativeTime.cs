using System.Globalization;

namespace MiroClone.Web.Components.Dashboard;

/// <summary>The card's "created" text: a short distance in words, and a date once the board is a month old.</summary>
public static class RelativeTime
{
    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Hour = TimeSpan.FromHours(1);
    private static readonly TimeSpan Day = TimeSpan.FromDays(1);
    private static readonly TimeSpan Month = TimeSpan.FromDays(30);

    public static string Describe(DateTimeOffset created, DateTimeOffset now)
    {
        // A board stamped a moment after this clock read is "just now", not a negative age.
        var age = now > created ? now - created : TimeSpan.Zero;

        if (age < Minute)
        {
            return "just now";
        }

        if (age < Hour)
        {
            return Ago((int)age.TotalMinutes, "minute");
        }

        if (age < Day)
        {
            return Ago((int)age.TotalHours, "hour");
        }

        if (age < Month)
        {
            return Ago((int)age.TotalDays, "day");
        }

        return created.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
    }

    private static string Ago(int amount, string unit) =>
        $"{amount.ToString(CultureInfo.InvariantCulture)} {unit}{(amount == 1 ? string.Empty : "s")} ago";
}
