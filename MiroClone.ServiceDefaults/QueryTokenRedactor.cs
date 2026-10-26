using System.Diagnostics;
using System.Text.RegularExpressions;
using OpenTelemetry;

namespace MiroClone.ServiceDefaults;

/// <summary>
/// Removes a bearer token from the URL tags of a span before it is exported (ADR 0047). The canvas hub takes its token in the
/// query string, because a browser WebSocket cannot send a header, so the ASP.NET Core instrumentation records the full URL with
/// the token in it. The token is a credential: it must not reach the telemetry backend.
/// </summary>
public sealed class QueryTokenRedactor : BaseProcessor<Activity>
{
    private static readonly string[] UrlTags = ["url.full", "url.query", "http.url", "http.target"];

    private static readonly Regex Token = new("(access_token=)[^&#\\s]*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    public override void OnEnd(Activity data)
    {
        foreach (var name in UrlTags)
        {
            if (data.GetTagItem(name) is string value && Token.IsMatch(value))
            {
                data.SetTag(name, Redact(value));
            }
        }
    }

    /// <summary>The value with its token replaced. Any other query value is kept, so the route is still readable in a trace.</summary>
    public static string Redact(string value) => Token.Replace(value, "$1REDACTED");
}
