using System.Threading.Channels;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MiroClone.Web.Api;
using MiroClone.Web.Canvas.Core;
using MiroClone.Web.Canvas.Editing;

namespace MiroClone.Web.Canvas.Realtime;

/// <summary>
/// The circuit's live copy of one board, joined to the canvas hub (ADR 0035, ADR 0036). Local edits apply at once and are
/// sent in order from an outbox. The room's edits are applied through <see cref="MutationApplier"/>. When the connection
/// drops, the session retries with backoff and re-reads the board; edits made while it was down are replayed on top of
/// the re-read state. A mutation the service refuses is dropped and the board is re-read, so the service's state wins.
/// </summary>
/// <remarks>
/// All document changes run on <paramref name="dispatcher"/> when one is given, which for the page is the circuit's own
/// context. Without one, a change runs on the thread that delivered it, which only the tests use.
/// </remarks>
public sealed partial class HubCanvasSession : IRealtimeCanvasSession, ICanvasHubListener
{
    /// <summary>The least gap between two presence sends: at most 20 a second (ADR 0036).</summary>
    public static readonly TimeSpan PresencePace = TimeSpan.FromMilliseconds(50);

    /// <summary>How long the outbox waits before it tries a rate-limited edit again.</summary>
    public static readonly TimeSpan RateLimitPause = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// The most edits the outbox holds while the connection is down (ADR 0035). At the cap, new local edits are refused and the
    /// page says so, so a long offline session cannot grow without bound.
    /// </summary>
    public const int DefaultMaximumOutbox = 1000;

    private static readonly IReadOnlyList<LayerMutation> NoChange = [];

    private static readonly TimeSpan MaximumRetryDelay = TimeSpan.FromSeconds(15);

    private readonly Guid _boardId;
    private readonly Guid _selfUserId;
    private readonly string _selfName;
    private readonly ICanvasHubConnector _connector;
    private readonly Func<CancellationToken, Task<string?>> _accessToken;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _retryDelay;
    private readonly SynchronizationContext? _dispatcher;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Channel<bool> _outboxSignal = Channel.CreateBounded<bool>(SignalOptions());
    private readonly Channel<bool> _presenceSignal = Channel.CreateBounded<bool>(SignalOptions());
    private readonly Lock _gate = new();
    private readonly History _history = new();
    private readonly Queue<LayerMutation> _outbox = new();
    private readonly int _maximumOutbox;
    private readonly Dictionary<string, PresenceEntry> _participants = [];
    private long _received;

    private CanvasDocument _document;
    private CanvasConnectionState _state = CanvasConnectionState.Connecting;
    private ICanvasHubConnection? _connection;
    private bool _resync;
    private Point? _cursor;
    private IReadOnlyList<Guid> _selection = [];
    private Task[] _loops = [];
    private bool _started;
    private bool _disposed;

    /// <param name="initial">The board as the REST load returned it. The hub's first state replaces it.</param>
    /// <param name="accessToken">The signed-in session's access token, or null when the session has ended.</param>
    /// <param name="dispatcher">Where document changes run; the circuit's context on the page, null in tests.</param>
    /// <param name="retryDelay">The first wait before a reconnect. It doubles up to 15 seconds and resets after a live connection.</param>
    /// <param name="maximumOutbox">The most edits held unsent before new edits are refused (ADR 0035).</param>
    public HubCanvasSession(
        Guid boardId,
        Guid selfUserId,
        string selfName,
        CanvasDocument initial,
        ICanvasHubConnector connector,
        Func<CancellationToken, Task<string?>> accessToken,
        TimeProvider clock,
        SynchronizationContext? dispatcher = null,
        ILogger? logger = null,
        TimeSpan? retryDelay = null,
        int maximumOutbox = DefaultMaximumOutbox)
    {
        _maximumOutbox = maximumOutbox > 0 ? maximumOutbox : throw new ArgumentOutOfRangeException(nameof(maximumOutbox));
        _boardId = boardId;
        _selfUserId = selfUserId;
        _selfName = selfName ?? throw new ArgumentNullException(nameof(selfName));
        _document = initial ?? throw new ArgumentNullException(nameof(initial));
        _connector = connector ?? throw new ArgumentNullException(nameof(connector));
        _accessToken = accessToken ?? throw new ArgumentNullException(nameof(accessToken));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _dispatcher = dispatcher;
        _logger = logger ?? NullLogger.Instance;
        _retryDelay = retryDelay ?? TimeSpan.FromSeconds(1);
    }

    public CanvasDocument Document
    {
        get
        {
            lock (_gate)
            {
                return _document;
            }
        }
    }

    public CanvasConnectionState Connection
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    public IReadOnlyList<ParticipantView> Participants
    {
        get
        {
            lock (_gate)
            {
                var self = _participants.Values.FirstOrDefault(entry => entry.Presence.UserId == _selfUserId).Presence;
                var views = new List<ParticipantView> { new(_selfUserId, _selfName, HubWire.Colour(self?.Color), IsSelf: true) };
                views.AddRange(OthersOnePerUser().Select(presence => new ParticipantView(presence.UserId, NameOf(presence), HubWire.Colour(presence.Color), IsSelf: false)));
                return views;
            }
        }
    }

    public IReadOnlyList<RemoteParticipant> RemoteParticipants
    {
        get
        {
            lock (_gate)
            {
                return OthersOnePerUser()
                    .Select(presence => new RemoteParticipant(
                        presence.UserId,
                        NameOf(presence),
                        HubWire.Colour(presence.Color),
                        presence.Cursor is { } cursor ? new Point(cursor.X, cursor.Y) : null,
                        BoundsOf(presence.Selection)))
                    .ToArray();
            }
        }
    }

    public event Action? Changed;

    public bool CanUndo
    {
        get
        {
            lock (_gate)
            {
                return _history.CanUndo;
            }
        }
    }

    public bool CanRedo
    {
        get
        {
            lock (_gate)
            {
                return _history.CanRedo;
            }
        }
    }

    public IReadOnlyList<LayerMutation> Commit(IReadOnlyList<LayerMutation> mutations)
    {
        lock (_gate)
        {
            return _outbox.Count >= _maximumOutbox ? NoChange : Send(_history.Execute(_document, mutations));
        }
    }

    /// <summary>True while the outbox is full. New local edits are refused until the connection takes some of the queue.</summary>
    public bool EditsPaused
    {
        get
        {
            lock (_gate)
            {
                return _outbox.Count >= _maximumOutbox;
            }
        }
    }

    /// <summary>An undo is a change to the room too: the mutations it made are sent, so the other participants see it.</summary>
    public IReadOnlyList<LayerMutation> Undo()
    {
        lock (_gate)
        {
            return _outbox.Count >= _maximumOutbox ? NoChange : Send(_history.Undo(_document));
        }
    }

    public IReadOnlyList<LayerMutation> Redo()
    {
        lock (_gate)
        {
            return _outbox.Count >= _maximumOutbox ? NoChange : Send(_history.Redo(_document));
        }
    }

    public void SetPresence(Point? cursor, IReadOnlyList<Guid> selection)
    {
        ArgumentNullException.ThrowIfNull(selection);

        lock (_gate)
        {
            if (_cursor == cursor && _selection.SequenceEqual(selection))
            {
                return;
            }

            _cursor = cursor;
            _selection = selection.ToArray();
            _presenceSignal.Writer.TryWrite(true);
        }
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_started || _disposed)
            {
                return;
            }

            _started = true;
            var token = _lifetime.Token;
            _loops =
            [
                Task.Run(() => Supervise(RunConnectionsAsync, token), CancellationToken.None),
                Task.Run(() => Supervise(PumpOutboxAsync, token), CancellationToken.None),
                Task.Run(() => Supervise(PumpPresenceAsync, token), CancellationToken.None),
            ];
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task[] loops;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            loops = _loops;
        }

        // The loops own the connection: each one disposes it when it ends, so nothing is left open after this returns.
        await _lifetime.CancelAsync();
        await Task.WhenAll(loops);
        _lifetime.Dispose();
    }

    // ICanvasHubListener. These arrive on the transport's threads and run on the dispatcher.

    public void OnInitialState(CanvasStateDto state, IReadOnlyList<PresenceDto> participants) => Dispatch(() =>
    {
        CanvasDocument next;
        try
        {
            next = CanvasStateMapper.ToDocument(state);
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            LogMalformed(_logger, "board state", exception.Message);
            return;
        }

        lock (_gate)
        {
            _document = next;

            // Edits not yet acknowledged by the service are replayed, so they stay visible. MutationApplier is idempotent,
            // so a replay of an edit the service already has changes nothing.
            foreach (var mutation in _outbox)
            {
                MutationApplier.Apply(_document, mutation);
            }

            _participants.Clear();
            foreach (var presence in participants)
            {
                _participants[ParticipantKey(presence.UserId)] = new PresenceEntry(++_received, presence);
            }
        }

        Notify();
    });

    public void OnLayerMutation(string connectionId, LayerMutationDto mutation) => Dispatch(() =>
    {
        LayerMutation change;
        try
        {
            change = HubWire.ToCore(mutation);
        }
        catch (FormatException exception)
        {
            LogMalformed(_logger, "layer mutation", exception.Message);
            return;
        }

        bool applied;
        lock (_gate)
        {
            applied = MutationApplier.Apply(_document, change).Result == MutationResult.Applied;
        }

        if (applied)
        {
            Notify();
        }
    });

    public void OnPresenceUpdated(string connectionId, PresenceDto presence) => Dispatch(() =>
    {
        lock (_gate)
        {
            _participants[connectionId] = new PresenceEntry(++_received, presence);
        }

        Notify();
    });

    public void OnPresenceLeft(string connectionId) => Dispatch(() =>
    {
        lock (_gate)
        {
            // A connection this session never heard of was on the board before it joined, so its entry came from the join.
            // The room is re-read rather than guessing which entry it was.
            if (!_participants.Remove(connectionId))
            {
                RequestResync();
            }
        }

        Notify();
    });

    /// <summary>Queues the mutations that changed the document, for the room, in order. Called with the gate held.</summary>
    private IReadOnlyList<LayerMutation> Send(IReadOnlyList<LayerMutation> applied)
    {
        foreach (var mutation in applied)
        {
            _outbox.Enqueue(mutation);
        }

        if (applied.Count > 0)
        {
            _outboxSignal.Writer.TryWrite(true);
        }

        return applied;
    }

    private async Task RunConnectionsAsync(CancellationToken ct)
    {
        var delay = _retryDelay;
        while (true)
        {
            var outcome = await ConnectOnceAsync(ct);
            if (outcome == Outcome.Stopped)
            {
                return;
            }

            SetState(CanvasConnectionState.Reconnecting);
            await Task.Delay(delay, _clock, ct);
            delay = outcome == Outcome.Ended ? _retryDelay : TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, MaximumRetryDelay.Ticks));
        }
    }

    private async Task<Outcome> ConnectOnceAsync(CancellationToken ct)
    {
        ICanvasHubConnection? connection = null;
        var wasLive = false;
        try
        {
            if (await _accessToken(ct) is null)
            {
                // The sign-in has ended. Retrying cannot help, so the session says so and stops.
                SetState(CanvasConnectionState.Unavailable);
                return Outcome.Stopped;
            }

            connection = _connector.Create(_accessToken, this);
            await connection.StartAsync(ct);
            await connection.JoinBoardAsync(_boardId, ct);

            lock (_gate)
            {
                _connection = connection;
                _state = CanvasConnectionState.Live;
            }

            wasLive = true;
            Notify();
            _outboxSignal.Writer.TryWrite(true);
            _presenceSignal.Writer.TryWrite(true);

            await connection.Closed.WaitAsync(ct);
            return Outcome.Ended;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return Outcome.Stopped;
        }
        catch (HubException exception) when (IsTerminal(exception))
        {
            LogRefusedJoin(_logger, _boardId, exception.Message);
            SetState(CanvasConnectionState.Unavailable);
            return Outcome.Stopped;
        }
        catch (Exception exception)
        {
            LogConnectionFailed(_logger, _boardId, exception.Message);
            return wasLive ? Outcome.Ended : Outcome.Failed;
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_connection, connection))
                {
                    _connection = null;
                }

                // The room is unknown while the connection is down; the next join says who is there.
                _participants.Clear();
            }

            if (connection is not null)
            {
                await connection.DisposeAsync();
            }

            Notify();
        }
    }

    private async Task PumpOutboxAsync(CancellationToken ct)
    {
        while (true)
        {
            await _outboxSignal.Reader.ReadAsync(ct);
            await DrainOutboxAsync(ct);
        }
    }

    /// <summary>Sends queued edits in order, one at a time. A transport failure leaves the queue as it is for the next connection.</summary>
    /// <summary>Takes the head of the outbox after the service has it, and tells the page when that lifts a pause.</summary>
    private void DequeueSent()
    {
        bool resumed;
        lock (_gate)
        {
            resumed = _outbox.Count >= _maximumOutbox;
            _outbox.Dequeue();
        }

        if (resumed)
        {
            Notify();
        }
    }

    private async Task DrainOutboxAsync(CancellationToken ct)
    {
        while (true)
        {
            ICanvasHubConnection? connection;
            LayerMutation? next;
            bool resync;
            lock (_gate)
            {
                connection = _state == CanvasConnectionState.Live ? _connection : null;
                next = _outbox.TryPeek(out var head) ? head : null;
                resync = _resync && connection is not null;
                if (resync)
                {
                    _resync = false;
                }
            }

            if (connection is null)
            {
                return;
            }

            if (resync)
            {
                try
                {
                    await connection.JoinBoardAsync(_boardId, ct);
                }
                catch (Exception exception) when (!ct.IsCancellationRequested)
                {
                    LogConnectionFailed(_logger, _boardId, exception.Message);
                    return;
                }

                continue;
            }

            if (next is null)
            {
                return;
            }

            try
            {
                await connection.SendMutationAsync(_boardId, HubWire.ToWire(next), ct);
                DequeueSent();
            }
            catch (HubException exception) when (exception.Message.Contains(HubMethods.RateLimited, StringComparison.Ordinal))
            {
                await Task.Delay(RateLimitPause, _clock, ct);
            }
            catch (HubException exception)
            {
                // The service refused the edit (the layer limit, or a shape it does not accept). It is dropped here, and the
                // board is re-read so the local copy matches the service.
                LogRefusedEdit(_logger, _boardId, exception.Message);
                DequeueSent();
                lock (_gate)
                {
                    RequestResync();
                }
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private async Task PumpPresenceAsync(CancellationToken ct)
    {
        while (true)
        {
            await _presenceSignal.Reader.ReadAsync(ct);

            // The pace is the throttle: after a send, the next one waits at least PresencePace.
            await Task.Delay(PresencePace, _clock, ct);
            await SendPresenceAsync(ct);
        }
    }

    private async Task SendPresenceAsync(CancellationToken ct)
    {
        ICanvasHubConnection? connection;
        PresenceDto presence;
        lock (_gate)
        {
            connection = _state == CanvasConnectionState.Live ? _connection : null;

            // Identity is stamped by the service from the token, so the name and colour stay empty here.
            presence = new PresenceDto(
                _selfUserId,
                string.Empty,
                string.Empty,
                _cursor is { } cursor ? new CursorDto(cursor.X, cursor.Y) : null,
                _selection);
        }

        if (connection is null)
        {
            return;
        }

        try
        {
            await connection.UpdatePresenceAsync(_boardId, presence, ct);
        }
        catch (Exception exception) when (!ct.IsCancellationRequested)
        {
            // A refused or failed presence update is not an edit. The next change sends the latest state, and a reconnect re-sends it.
            LogPresenceNotSent(_logger, exception.Message);
        }
    }

    /// <summary>
    /// Runs one background loop. Ending by cancellation is the normal end, and an error during disposal is the shutdown's to
    /// absorb, so the loop never faults and disposal never throws. Any other failure is logged.
    /// </summary>
    private async Task Supervise(Func<CancellationToken, Task> loop, CancellationToken ct)
    {
        try
        {
            await loop(ct);
        }
        catch (Exception exception)
        {
            if (!ct.IsCancellationRequested)
            {
                LogLoopFailed(_logger, exception);
            }
        }
    }

    private void RequestResync()
    {
        _resync = true;
        _outboxSignal.Writer.TryWrite(true);
    }

    private void SetState(CanvasConnectionState state)
    {
        lock (_gate)
        {
            if (_state == state)
            {
                return;
            }

            _state = state;
        }

        Notify();
    }

    /// <summary>Raises <see cref="Changed"/> on the dispatcher. Never called while the gate is held.</summary>
    private void Notify() => Dispatch(() => Changed?.Invoke());

    private void Dispatch(Action action)
    {
        if (_dispatcher is null)
        {
            Run(action);
            return;
        }

        _dispatcher.Post(_ => Run(action), null);
    }

    private void Run(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            LogDispatchFailed(_logger, exception);
        }
    }

    /// <summary>Everyone else, with one entry per user, so two tabs of one person are one avatar and one cursor.</summary>
    private IEnumerable<PresenceDto> OthersOnePerUser() =>
        _participants.Values
            .Where(entry => entry.Presence.UserId != _selfUserId)
            .GroupBy(entry => entry.Presence.UserId)
            .Select(group => group.MaxBy(entry => entry.Received).Presence)
            .OrderBy(NameOf, StringComparer.Ordinal);

    private List<Bounds> BoundsOf(IReadOnlyList<Guid> ids)
    {
        var boxes = new List<Bounds>();
        foreach (var id in ids)
        {
            if (_document.TryGet(id, out var layer))
            {
                boxes.Add(layer.Bounds);
            }
        }

        return boxes;
    }

    private static string NameOf(PresenceDto presence) =>
        string.IsNullOrWhiteSpace(presence.Name) ? "Member" : presence.Name;

    private static string ParticipantKey(Guid userId) => "user:" + userId.ToString("N");

    private static bool IsTerminal(HubException exception) =>
        exception.Message.Contains(HubMethods.BoardNotFound, StringComparison.Ordinal) ||
        exception.Message.Contains(HubMethods.Unauthenticated, StringComparison.Ordinal);

    private static BoundedChannelOptions SignalOptions() => new(1)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true,
        SingleWriter = false,
    };

    /// <summary>A participant's latest presence, with the order it was received in: the newest entry for a user is the one shown.</summary>
    private readonly record struct PresenceEntry(long Received, PresenceDto Presence);

    private enum Outcome
    {
        /// <summary>The connection could not be made. Retry with backoff.</summary>
        Failed,

        /// <summary>The connection was live and then closed. Retry soon.</summary>
        Ended,

        /// <summary>The session is over: disposed, or the service or the sign-in refused it for good.</summary>
        Stopped,
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The canvas hub connection for board {BoardId} failed; retrying: {Error}")]
    private static partial void LogConnectionFailed(ILogger logger, Guid boardId, string error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The canvas hub refused board {BoardId}: {Error}")]
    private static partial void LogRefusedJoin(ILogger logger, Guid boardId, string error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The canvas hub refused a local edit; the board is re-read: {Error}")]
    private static partial void LogRefusedEdit(ILogger logger, Guid boardId, string error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ignored a malformed {What} from the canvas hub: {Error}")]
    private static partial void LogMalformed(ILogger logger, string what, string error);

    [LoggerMessage(Level = LogLevel.Debug, Message = "A presence update was not sent; the next change sends the latest state: {Error}")]
    private static partial void LogPresenceNotSent(ILogger logger, string error);

    [LoggerMessage(Level = LogLevel.Error, Message = "A canvas session background loop failed.")]
    private static partial void LogLoopFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "A canvas session update failed.")]
    private static partial void LogDispatchFailed(ILogger logger, Exception exception);
}
