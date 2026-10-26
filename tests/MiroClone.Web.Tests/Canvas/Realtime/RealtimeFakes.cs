using Microsoft.AspNetCore.SignalR;
using MiroClone.Web.Api;
using MiroClone.Web.Canvas.Core;
using MiroClone.Web.Canvas.Realtime;
using Xunit;

namespace MiroClone.Web.Tests.Canvas.Realtime;

/// <summary>
/// A stand-in for the canvas hub with one shared "server" state: every connection joins that state, and what the test puts into
/// it is what the next join returns. Failures are scripted, so the reconnect and refusal paths run without a socket.
/// </summary>
internal sealed class FakeHubConnector : ICanvasHubConnector
{
    private readonly List<FakeHubConnection> _connections = [];
    private readonly Lock _lock = new();

    /// <summary>The state the next join is answered with, and the presence list that comes with it.</summary>
    public CanvasStateDto? ServerState { get; set; }

    public IReadOnlyList<PresenceDto> ServerPresence { get; set; } = [];

    /// <summary>When set, every join is refused with this exception (for example a board the caller cannot see).</summary>
    public Exception? JoinError { get; set; }

    /// <summary>When set, every connection fails to start.</summary>
    public Exception? StartError { get; set; }

    public IReadOnlyList<FakeHubConnection> Connections
    {
        get
        {
            lock (_lock)
            {
                return _connections.ToArray();
            }
        }
    }

    public FakeHubConnection Latest => Connections[^1];

    public ICanvasHubConnection Create(Func<CancellationToken, Task<string?>> accessToken, ICanvasHubListener listener)
    {
        var connection = new FakeHubConnection(this, listener);
        lock (_lock)
        {
            _connections.Add(connection);
        }

        return connection;
    }

    internal (CanvasStateDto? State, IReadOnlyList<PresenceDto> Presence) Answer() => (ServerState, ServerPresence);
}

internal sealed class FakeHubConnection(FakeHubConnector server, ICanvasHubListener listener) : ICanvasHubConnection
{
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Lock _lock = new();
    private readonly List<LayerMutationDto> _sent = [];
    private readonly List<PresenceDto> _presence = [];
    private int _joins;

    /// <summary>Failures for the next sends, in order. A failure with a HubException is a refusal; any other is a transport failure.</summary>
    public Queue<Exception> SendFailures { get; } = new();

    public ICanvasHubListener Listener => listener;

    public int Joins => _joins;

    public bool Disposed { get; private set; }

    public IReadOnlyList<LayerMutationDto> Sent
    {
        get
        {
            lock (_lock)
            {
                return _sent.ToArray();
            }
        }
    }

    public IReadOnlyList<PresenceDto> Presence
    {
        get
        {
            lock (_lock)
            {
                return _presence.ToArray();
            }
        }
    }

    public Task Closed => _closed.Task;

    public Task StartAsync(CancellationToken cancellationToken) =>
        server.StartError is { } error ? Task.FromException(error) : Task.CompletedTask;

    public Task JoinBoardAsync(Guid boardId, CancellationToken cancellationToken)
    {
        if (server.JoinError is { } refusal)
        {
            return Task.FromException(refusal);
        }

        Interlocked.Increment(ref _joins);
        var (state, presence) = server.Answer();
        if (state is not null)
        {
            // The hub sends the state before the join completes, as the service does.
            listener.OnInitialState(state, presence);
        }

        return Task.CompletedTask;
    }

    public Task SendMutationAsync(Guid boardId, LayerMutationDto mutation, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (SendFailures.TryDequeue(out var failure))
            {
                return Task.FromException(failure);
            }

            _sent.Add(mutation);
        }

        return Task.CompletedTask;
    }

    public Task UpdatePresenceAsync(Guid boardId, PresenceDto presence, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            _presence.Add(presence);
        }

        return Task.CompletedTask;
    }

    /// <summary>Closes the connection as the network would: normally when <paramref name="error"/> is null, faulted otherwise.</summary>
    public void Drop(Exception? error = null)
    {
        if (error is null)
        {
            _closed.TrySetResult();
        }
        else
        {
            _closed.TrySetException(error);
        }
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        _closed.TrySetResult();
        return ValueTask.CompletedTask;
    }
}

/// <summary>A synchronization context that queues work until the test pumps it, so a test can see where a change would run.</summary>
internal sealed class QueueingContext : SynchronizationContext
{
    private readonly Queue<Action> _queue = new();
    private readonly Lock _lock = new();

    public int Queued
    {
        get
        {
            lock (_lock)
            {
                return _queue.Count;
            }
        }
    }

    public override void Post(SendOrPostCallback d, object? state)
    {
        lock (_lock)
        {
            _queue.Enqueue(() => d(state));
        }
    }

    /// <summary>Runs everything queued so far, on the calling thread.</summary>
    public void Pump()
    {
        while (true)
        {
            Action next;
            lock (_lock)
            {
                if (_queue.Count == 0)
                {
                    return;
                }

                next = _queue.Dequeue();
            }

            next();
        }
    }
}

/// <summary>Wire-shaped fixtures: layers, states and presence as the service sends them.</summary>
internal static class WireFixtures
{
    public static LayerDto Rectangle(Guid id, double x = 10, double y = 20, double width = 100, double height = 50) =>
        new(id, "Rectangle", x, y, width, height, new FillDto(1, 2, 3), null, null);

    public static CanvasStateDto State(long version, params LayerDto[] layers) =>
        new(version, layers.Select(layer => layer.Id).ToArray(), layers.ToDictionary(layer => layer.Id));

    public static PresenceDto Presence(Guid userId, string name = "Member 1a2b", string colour = "#2563eb", CursorDto? cursor = null, params Guid[] selection) =>
        new(userId, name, colour, cursor, selection);

    public static LayerMutationDto Upsert(LayerDto layer) => new("Upsert", layer, null, null);

    public static LayerMutationDto Delete(Guid id) => new("Delete", null, id, null);
}

internal static class Eventually
{
    /// <summary>Waits until the condition holds, polling in small steps, and fails the test if it does not hold in time.</summary>
    public static async Task AssertAsync(Func<bool> condition, string because = "the condition", int timeoutMilliseconds = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail($"Timed out waiting for {because}.");
            }

            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
    }
}

/// <summary>Opens the live session over a fake hub, with a long retry delay so a test sees the reconnecting state and nothing after it.</summary>
internal sealed class TestSessionFactory(ICanvasHubConnector connector) : ICanvasSessionFactory
{
    public IRealtimeCanvasSession Open(string sessionId, Guid selfUserId, string selfName, Guid boardId, CanvasDocument initial) =>
        new HubCanvasSession(
            boardId,
            selfUserId,
            selfName,
            initial,
            connector,
            _ => Task.FromResult<string?>("token"),
            TimeProvider.System,
            dispatcher: null,
            logger: null,
            retryDelay: TimeSpan.FromMinutes(1));
}
