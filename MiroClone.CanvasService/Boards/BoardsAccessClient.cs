using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace MiroClone.CanvasService.Boards;

/// <summary>
/// The anti-corruption client for BoardsService's <c>GET /api/boards/{id}/access</c> (ADR 0003, service-api-patterns §5).
/// The caller's bearer token is forwarded, so BoardsService enforces the workspace, not this service. Redirects are not
/// followed: a 3xx between services is a configuration bug and is reported as one.
/// </summary>
public sealed partial class BoardsAccessClient(HttpClient http, ILogger<BoardsAccessClient> logger) : IBoardAccess
{
    public async Task<BoardAccess> CheckAsync(Guid boardId, string bearerToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/boards/{boardId:D}/access");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            LogUnreachable(logger, ex.Message);
            throw new BoardsServiceException(StatusCodes.Status503ServiceUnavailable, "BoardsService is unreachable.", ex);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NoContent)
            {
                return BoardAccess.Allowed;
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return BoardAccess.NotFound;
            }

            if ((int)response.StatusCode is >= 300 and < 400)
            {
                LogRedirect(logger, http.BaseAddress?.ToString() ?? string.Empty, (int)response.StatusCode);
                throw new BoardsServiceException(StatusCodes.Status502BadGateway, "BoardsService answered with a redirect.");
            }

            LogUnexpected(logger, (int)response.StatusCode);
            throw new BoardsServiceException(StatusCodes.Status503ServiceUnavailable, "BoardsService did not answer the access check.");
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "BoardsService is unreachable for an access check: {Error}")]
    private static partial void LogUnreachable(ILogger logger, string error);

    [LoggerMessage(Level = LogLevel.Error, Message = "BoardsService answered {Url} with a redirect ({Status}); check its base address.")]
    private static partial void LogRedirect(ILogger logger, string url, int status);

    [LoggerMessage(Level = LogLevel.Warning, Message = "BoardsService answered the access check with {Status}.")]
    private static partial void LogUnexpected(ILogger logger, int status);
}
