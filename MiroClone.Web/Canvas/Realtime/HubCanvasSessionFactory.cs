using Microsoft.Extensions.Logging;
using MiroClone.Web.Auth;
using MiroClone.Web.Canvas.Core;

namespace MiroClone.Web.Canvas.Realtime;

/// <summary>
/// Opens the production session: a SignalR connection from the circuit to the canvas service. The access token comes from the
/// session service on the server, so it never reaches the browser (ADR 0022). Changes run on the dispatcher the page passes.
/// </summary>
public sealed class HubCanvasSessionFactory(
    SessionService sessions,
    IHttpMessageHandlerFactory handlers,
    TimeProvider clock,
    ILogger<HubCanvasSession> logger) : ICanvasSessionFactory
{
    /// <summary>The canvas service's hub. The host name is the logical service name that service discovery resolves.</summary>
    public static readonly Uri HubUrl = new("http://canvas-service/hubs/canvas");

    /// <summary>The handler chain with service discovery applied (ServiceDefaults adds it to every client).</summary>
    public const string HandlerName = "canvas-hub";

    public IRealtimeCanvasSession Open(
        string sessionId,
        Guid selfUserId,
        string selfName,
        Guid boardId,
        CanvasDocument initial,
        SynchronizationContext? dispatcher)
    {
        ArgumentException.ThrowIfNullOrEmpty(sessionId);

        var connector = new SignalRCanvasHubConnector(HubUrl, () => handlers.CreateHandler(HandlerName));
        return new HubCanvasSession(
            boardId,
            selfUserId,
            selfName,
            initial,
            connector,
            cancellation => sessions.GetAccessTokenAsync(sessionId, cancellation),
            clock,
            dispatcher: dispatcher,
            logger: logger);
    }
}
