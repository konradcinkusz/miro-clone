using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MiroClone.CanvasService.Persistence;

/// <summary>
/// The flush loop. It wakes on a fixed interval, so a burst of edits to one board becomes one write. On shutdown it flushes
/// once more, bounded, so a clean stop keeps the last interval of edits.
/// </summary>
/// <remarks>
/// This is a plain <see cref="IHostedService"/> rather than a <c>BackgroundService</c> on purpose. The loop is owned here and
/// the final flush runs inside <see cref="StopAsync"/>, which the host always awaits. A <c>BackgroundService</c>'s final flush
/// depends on its <c>ExecuteAsync</c> having started; measured under parallel test hosts, that did not always happen, and a
/// clean stop then lost the last interval of edits (ADR 0011).
/// </remarks>
internal sealed partial class CanvasSnapshotWriter(
    CanvasRegistry registry,
    IOptions<CanvasOptions> options,
    ILogger<CanvasSnapshotWriter> logger) : IHostedService, IDisposable
{
    private static readonly TimeSpan ShutdownFlushBudget = TimeSpan.FromSeconds(10);

    private CancellationTokenSource? _stopping;
    private Task? _loop;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _stopping = new CancellationTokenSource();
        _loop = RunAsync(_stopping.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_stopping is null || _loop is null)
        {
            return;
        }

        await _stopping.CancelAsync();
        await _loop.WaitAsync(cancellationToken);
    }

    public void Dispose() => _stopping?.Dispose();

    private async Task RunAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.FlushInterval);
        while (await NextTickAsync(timer, stoppingToken))
        {
            try
            {
                await registry.FlushAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                LogFlushFailed(logger, ex.Message);
            }
        }

        await FlushOnShutdownAsync();
    }

    private static async Task<bool> NextTickAsync(PeriodicTimer timer, CancellationToken stoppingToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private async Task FlushOnShutdownAsync()
    {
        using var budget = new CancellationTokenSource(ShutdownFlushBudget);
        try
        {
            await registry.FlushAsync(budget.Token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFlushFailed(logger, ex.Message);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Canvas flush failed: {Error}")]
    private static partial void LogFlushFailed(ILogger logger, string error);
}
