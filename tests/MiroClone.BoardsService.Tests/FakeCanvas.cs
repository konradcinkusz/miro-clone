using System.Net;
using System.Net.Http.Headers;

namespace MiroClone.BoardsService.Tests;

/// <summary>
/// Stands in for CanvasService behind the purge client. It records each call and answers with a configured status,
/// or throws, so the tests can see what BoardsService sent and how it reacted.
/// </summary>
internal sealed class FakeCanvas : HttpMessageHandler
{
    public List<CanvasCall> Calls { get; } = [];

    public HttpStatusCode Status { get; set; } = HttpStatusCode.NoContent;

    /// <summary>When set, every call fails with this exception, as an unreachable service does.</summary>
    public Exception? Failure { get; set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls.Add(new CanvasCall(request.Method, request.RequestUri!.AbsolutePath, request.Headers.Authorization));
        if (Failure is not null)
        {
            return Task.FromException<HttpResponseMessage>(Failure);
        }

        return Task.FromResult(new HttpResponseMessage(Status));
    }
}

internal sealed record CanvasCall(HttpMethod Method, string Path, AuthenticationHeaderValue? Authorization);
