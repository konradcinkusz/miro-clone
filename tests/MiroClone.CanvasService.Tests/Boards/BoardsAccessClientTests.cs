using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using MiroClone.CanvasService.Boards;
using Xunit;

namespace MiroClone.CanvasService.Tests.Boards;

/// <summary>
/// The status mapping of the anti-corruption client. Fails closed: only 204 allows, 404 refuses, and anything else is an
/// error the caller must not treat as "allowed" or "empty".
/// </summary>
public sealed class BoardsAccessClientTests
{
    private static readonly Guid Board = Guid.NewGuid();
    private const string Token = "caller-token";

    [Fact]
    public async Task NoContentAllowsAndTheCallerTokenIsForwarded()
    {
        var stub = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

        var access = await CheckAsync(stub);

        Assert.Equal(BoardAccess.Allowed, access);
        var request = Assert.Single(stub.Requests);
        Assert.Equal($"/api/boards/{Board:D}/access", request.RequestUri!.AbsolutePath);
        Assert.Equal(new AuthenticationHeaderValue("Bearer", Token), request.Headers.Authorization);
    }

    [Fact]
    public async Task NotFoundRefusesWithoutAnError()
    {
        var stub = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        Assert.Equal(BoardAccess.NotFound, await CheckAsync(stub));
    }

    [Fact]
    public async Task ARedirectIsNotFollowedAndIsReportedAsBadGateway()
    {
        var stub = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Redirect)
        {
            Headers = { Location = new Uri("https://elsewhere.test/api/boards/x/access") },
        });

        var error = await Assert.ThrowsAsync<BoardsServiceException>(() => CheckAsync(stub));

        Assert.Equal(StatusCodes.Status502BadGateway, error.StatusCode);
        Assert.Single(stub.Requests);
    }

    [Fact]
    public async Task AServerErrorFailsClosedAsServiceUnavailable()
    {
        var stub = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var error = await Assert.ThrowsAsync<BoardsServiceException>(() => CheckAsync(stub));

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, error.StatusCode);
    }

    [Fact]
    public async Task AnUnreachableBoardsServiceFailsClosedAsServiceUnavailable()
    {
        var stub = new StubHandler(_ => throw new HttpRequestException("connection refused"));

        var error = await Assert.ThrowsAsync<BoardsServiceException>(() => CheckAsync(stub));

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, error.StatusCode);
    }

    private static Task<BoardAccess> CheckAsync(HttpMessageHandler handler)
    {
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://boards.test/") };
        var access = new BoardsAccessClient(client, NullLogger<BoardsAccessClient>.Instance);
        return access.CheckAsync(Board, Token, TestContext.Current.CancellationToken);
    }

    /// <summary>Answers each request from a function and records what it was asked, so a test can assert on the outgoing call.</summary>
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }
}
