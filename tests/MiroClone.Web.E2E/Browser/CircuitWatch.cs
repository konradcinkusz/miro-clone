using System.Text;
using Microsoft.Playwright;

namespace MiroClone.Web.E2E.Browser;

/// <summary>
/// Tells the test when a page's Blazor circuit is ready to take input (blazor-web-app §13). An interactive page is HTML first; its
/// input is discarded until the circuit answers the <c>StartCircuit</c> it sends. The watch counts a circuit as ready at the first
/// frame received after <c>StartCircuit</c> is sent, and a test waits for that count before it interacts. It never sleeps.
/// </summary>
public sealed class CircuitWatch : IDisposable
{
    private readonly object _gate = new();
    private readonly List<(int Target, TaskCompletionSource Done)> _waiters = [];
    private readonly IPage _page;
    private readonly List<string> _events = [];
    private int _ready;
    private bool _startSent;

    /// <summary>Subscribes before the first navigation, so no circuit is missed.</summary>
    public CircuitWatch(IPage page)
    {
        _page = page;
        page.WebSocket += OnWebSocket;
        // What the browser saw, kept so that a circuit that never becomes ready says why (console, failed requests, HTTP errors).
        page.Console += (_, message) => Note($"console.{message.Type}: {message.Text}");
        page.PageError += (_, error) => Note($"pageerror: {error}");
        page.RequestFailed += (_, request) => Note($"request failed: {request.Method} {request.Url}: {request.Failure}");
        page.Response += (_, response) =>
        {
            if (response.Status >= 400 || (response.Status >= 300 && response.Request.Method != "GET"))
            {
                Note($"response {response.Status}: {response.Request.Method} {response.Url} location={response.Headers.GetValueOrDefault("location")}");
            }
        };
    }

    private void Note(string line)
    {
        lock (_gate)
        {
            _events.Add($"{DateTime.UtcNow:HH:mm:ss.fff} {line}");
        }
    }

    private string Describe()
    {
        lock (_gate)
        {
            return $"page {_page.Url}; ready={_ready}, startSent={_startSent}\n  " + string.Join("\n  ", _events.TakeLast(40));
        }
    }

    /// <summary>How many circuits have become ready on this page so far.</summary>
    public int ReadyCount
    {
        get
        {
            lock (_gate)
            {
                return _ready;
            }
        }
    }

    /// <summary>Completes when at least <paramref name="target"/> circuits have become ready.</summary>
    public Task WaitForReadyAsync(int target, TimeSpan? limit = null)
    {
        TaskCompletionSource done;
        lock (_gate)
        {
            if (_ready >= target)
            {
                return Task.CompletedTask;
            }

            done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiters.Add((target, done));
        }

        return WaitWithReportAsync(done.Task, limit ?? TimeSpan.FromSeconds(30));
    }

    private async Task WaitWithReportAsync(Task done, TimeSpan limit)
    {
        try
        {
            await done.WaitAsync(limit);
        }
        catch (TimeoutException)
        {
            throw new TimeoutException($"The Blazor circuit did not become ready within {limit.TotalSeconds:0}s. {Describe()}");
        }
    }

    private void OnWebSocket(object? sender, IWebSocket socket)
    {
        Note($"websocket opened: {socket.Url}");
        socket.Close += (_, _) => Note($"websocket closed: {socket.Url}");
        socket.SocketError += (_, error) => Note($"websocket error: {socket.Url}: {error}");
        if (!socket.Url.Contains("/_blazor", StringComparison.Ordinal))
        {
            return;
        }

        socket.FrameSent += (_, frame) =>
        {
            if (Text(frame).Contains("StartCircuit", StringComparison.Ordinal))
            {
                lock (_gate)
                {
                    _startSent = true;
                }
            }
        };

        socket.FrameReceived += (_, _) =>
        {
            lock (_gate)
            {
                if (!_startSent)
                {
                    return;
                }

                _startSent = false;
                _ready++;
                foreach (var waiter in _waiters.Where(waiter => waiter.Target <= _ready).ToArray())
                {
                    _waiters.Remove(waiter);
                    waiter.Done.TrySetResult();
                }
            }
        };
    }

    private static string Text(IWebSocketFrame frame) =>
        frame.Text ?? (frame.Binary is { } bytes ? Encoding.UTF8.GetString(bytes) : string.Empty);

    public void Dispose()
    {
    }
}
