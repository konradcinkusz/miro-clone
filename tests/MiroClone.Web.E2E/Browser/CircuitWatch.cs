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
    private int _ready;
    private bool _startSent;

    /// <summary>Subscribes before the first navigation, so no circuit is missed.</summary>
    public CircuitWatch(IPage page)
    {
        page.WebSocket += OnWebSocket;
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

        return done.Task.WaitAsync(limit ?? TimeSpan.FromSeconds(30));
    }

    private void OnWebSocket(object? sender, IWebSocket socket)
    {
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
