using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace MiroClone.Web.Tests.TestSupport;

/// <summary>
/// A stand-in for a service behind an HttpClient: each route answers from a script and every call is logged, so tests check
/// what the web app sent (method, path, bearer, body) without a network.
/// </summary>
internal sealed class ScriptedHandler : HttpMessageHandler
{
    private readonly Dictionary<string, Func<HttpRequestMessage, Task<HttpResponseMessage>>> _routes = new(StringComparer.Ordinal);

    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web);

    public List<Call> Calls { get; } = [];

    public sealed record Call(string Method, string Path, string? Authorization, string? Body);

    public ScriptedHandler On(HttpMethod method, string path, HttpStatusCode status, object? body = null)
    {
        _routes[Key(method.Method, path)] = _ => Task.FromResult(Reply(status, body));
        return this;
    }

    public ScriptedHandler On(HttpMethod method, string path, Func<HttpRequestMessage, Task<HttpResponseMessage>> respond)
    {
        _routes[Key(method.Method, path)] = respond;
        return this;
    }

    public int CountCalls(string method, string path) => Calls.Count(call => call.Method == method && call.Path == path);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath.Trim('/');
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Calls.Add(new Call(request.Method.Method, path, request.Headers.Authorization?.ToString(), body));

        if (_routes.TryGetValue(Key(request.Method.Method, path), out var respond))
        {
            return await respond(request);
        }

        return Reply(HttpStatusCode.NotFound, new { error = $"No script for {request.Method} {path}." });
    }

    public static HttpResponseMessage Reply(HttpStatusCode status, object? body = null)
    {
        var response = new HttpResponseMessage(status);
        if (body is not null)
        {
            var json = body is string text ? text : JsonSerializer.Serialize(body, Wire);
            response.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        return response;
    }

    private static string Key(string method, string path) => $"{method} {path.Trim('/')}";
}
