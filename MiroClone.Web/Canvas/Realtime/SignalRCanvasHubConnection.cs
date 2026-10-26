using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using MiroClone.Web.Api;

namespace MiroClone.Web.Canvas.Realtime;

/// <summary>
/// The SignalR client for the canvas hub, run by the circuit (ADR 0035). The bearer token is read from the session service
/// for each request and never reaches the browser. The transports are server-sent events and long polling, because the
/// WebSocket transport opens its own socket and would bypass the service-discovery handler that resolves the service name.
/// </summary>
public sealed class SignalRCanvasHubConnection : ICanvasHubConnection
{
    private readonly HubConnection _hub;
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public SignalRCanvasHubConnection(
        Uri hubUrl,
        Func<HttpMessageHandler> handlers,
        Func<CancellationToken, Task<string?>> accessToken,
        ICanvasHubListener listener)
    {
        _hub = new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                options.AccessTokenProvider = () => accessToken(CancellationToken.None);
                options.Transports = HttpTransportType.ServerSentEvents | HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => handlers();
            })
            .Build();

        _hub.On<CanvasStateDto, IReadOnlyList<PresenceDto>>(HubMethods.InitialState, listener.OnInitialState);
        _hub.On<string, LayerMutationDto>(HubMethods.LayerMutation, listener.OnLayerMutation);
        _hub.On<string, PresenceDto>(HubMethods.PresenceUpdated, listener.OnPresenceUpdated);
        _hub.On<string>(HubMethods.PresenceLeft, listener.OnPresenceLeft);
        _hub.Closed += error =>
        {
            if (error is null)
            {
                _closed.TrySetResult();
            }
            else
            {
                _closed.TrySetException(error);
            }

            return Task.CompletedTask;
        };
    }

    public Task Closed => _closed.Task;

    public Task StartAsync(CancellationToken cancellationToken) => _hub.StartAsync(cancellationToken);

    public Task JoinBoardAsync(Guid boardId, CancellationToken cancellationToken) =>
        _hub.InvokeAsync(HubMethods.JoinBoard, boardId, cancellationToken);

    public Task SendMutationAsync(Guid boardId, LayerMutationDto mutation, CancellationToken cancellationToken) =>
        _hub.InvokeAsync(HubMethods.SendMutation, boardId, mutation, cancellationToken);

    public Task UpdatePresenceAsync(Guid boardId, PresenceDto presence, CancellationToken cancellationToken) =>
        _hub.InvokeAsync(HubMethods.UpdatePresence, boardId, presence, cancellationToken);

    public ValueTask DisposeAsync() => _hub.DisposeAsync();
}

/// <summary>Makes <see cref="SignalRCanvasHubConnection"/>s for one hub address and one way of reaching it.</summary>
public sealed class SignalRCanvasHubConnector(Uri hubUrl, Func<HttpMessageHandler> handlers) : ICanvasHubConnector
{
    public ICanvasHubConnection Create(Func<CancellationToken, Task<string?>> accessToken, ICanvasHubListener listener) =>
        new SignalRCanvasHubConnection(hubUrl, handlers, accessToken, listener);
}
