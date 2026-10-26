using MiroClone.Web.Api;

namespace MiroClone.Web.Canvas.Realtime;

/// <summary>
/// Receives what the hub sends to this client. The session implements it; the transport calls it. Calls arrive on the
/// transport's threads, so the session marshals them itself.
/// </summary>
public interface ICanvasHubListener
{
    void OnInitialState(CanvasStateDto state, IReadOnlyList<PresenceDto> participants);

    void OnLayerMutation(string connectionId, LayerMutationDto mutation);

    void OnPresenceUpdated(string connectionId, PresenceDto presence);

    void OnPresenceLeft(string connectionId);
}

/// <summary>
/// One connection to the canvas hub. The seam that lets the session be tested without a socket: production uses SignalR
/// (<see cref="SignalRCanvasHubConnection"/>), tests use a fake. Every method throws <see cref="Microsoft.AspNetCore.SignalR.HubException"/>
/// when the hub refuses the call, with the service's error code in its message.
/// </summary>
public interface ICanvasHubConnection : IAsyncDisposable
{
    /// <summary>Completes when the connection closes: normally, or faulted with the cause.</summary>
    Task Closed { get; }

    Task StartAsync(CancellationToken cancellationToken);

    /// <summary>Joins the board. The hub answers with <see cref="ICanvasHubListener.OnInitialState"/> before this completes.</summary>
    Task JoinBoardAsync(Guid boardId, CancellationToken cancellationToken);

    Task SendMutationAsync(Guid boardId, LayerMutationDto mutation, CancellationToken cancellationToken);

    Task UpdatePresenceAsync(Guid boardId, PresenceDto presence, CancellationToken cancellationToken);
}

/// <summary>Makes connections. The access token is asked for at each request, so a refreshed token is used on the next one.</summary>
public interface ICanvasHubConnector
{
    ICanvasHubConnection Create(Func<CancellationToken, Task<string?>> accessToken, ICanvasHubListener listener);
}
