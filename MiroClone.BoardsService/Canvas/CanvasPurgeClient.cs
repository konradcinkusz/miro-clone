using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MiroClone.BoardsService.Canvas;

/// <summary>
/// The anti-corruption client for CanvasService's snapshot purge (ADR 0003). Best effort: a failed purge is logged and
/// returns false, and the board stays deleted. The orphan snapshot it leaves is harmless (ADR 0011).
/// </summary>
internal sealed partial class CanvasPurgeClient(HttpClient http, ILogger<CanvasPurgeClient> logger)
{
    /// <summary>
    /// Deletes the board's canvas snapshot. The caller's bearer is forwarded because CanvasService enforces authorization
    /// itself (service-api-patterns §5). DELETE is idempotent, so the kernel's resilience handler may retry it.
    /// </summary>
    /// <returns>True when the snapshot is gone (204) or was never written (404); false otherwise.</returns>
    public async Task<bool> PurgeAsync(Guid boardId, string? authorization, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"api/canvas/{boardId:D}");
        if (authorization is not null)
        {
            request.Headers.TryAddWithoutValidation("Authorization", authorization);
        }

        try
        {
            using var response = await http.SendAsync(request, cancellationToken);
            if (response.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.NotFound)
            {
                return true;
            }

            LogPurgeRefused(logger, boardId, (int)response.StatusCode);
            return false;
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Covers the unreachable service, HttpClient.Timeout, and the resilience pipeline's own rejections (an open circuit
            // throws its own type). None may fail the request: the delete has committed, and the caller's cancellation is the
            // one exception that is not swallowed.
            LogPurgeUnreachable(logger, boardId, exception);
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Canvas purge for board {BoardId} returned {StatusCode}; the orphan snapshot is left for the sweep.")]
    private static partial void LogPurgeRefused(ILogger logger, Guid boardId, int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Canvas purge for board {BoardId} did not complete; the orphan snapshot is left for the sweep.")]
    private static partial void LogPurgeUnreachable(ILogger logger, Guid boardId, Exception exception);
}

internal static class CanvasPurgeRegistration
{
    /// <summary>
    /// Base address from Canvas:BaseUrl, defaulting to the Aspire service name. Redirects are not followed: a redirect between
    /// services is a configuration bug, and following one would turn a DELETE into a GET. HttpClient.Timeout caps the purge
    /// at 8 seconds, which caps the kernel's resilience budget too (service-api-patterns §5): the purge is advisory, because
    /// the delete has already committed when it runs.
    /// </summary>
    public static IServiceCollection AddCanvasPurgeClient(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpClient<CanvasPurgeClient>(http =>
            {
                http.BaseAddress = new Uri(configuration["Canvas:BaseUrl"] ?? "http://canvas-service/", UriKind.Absolute);
                http.Timeout = TimeSpan.FromSeconds(8);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });

        return services;
    }
}
