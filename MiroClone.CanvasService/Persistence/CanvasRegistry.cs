using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MiroClone.CanvasService.Canvas;

namespace MiroClone.CanvasService.Persistence;

/// <summary>A board's canvas could not be read from the store. The caller answers "unavailable", never "empty".</summary>
public sealed class CanvasStoreUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Holds every loaded board in memory, marks them dirty on change, and flushes the dirty ones to the store on a timer
/// (state-snapshot-persistence §1). The hot path only touches memory. A failed write re-queues its board, and a failed
/// delete stays pending, so a dead database degrades the service but never blocks an edit (§4).
/// </summary>
public sealed partial class CanvasRegistry(
    ICanvasSnapshotStore store,
    IOptions<CanvasOptions> options,
    ILogger<CanvasRegistry> logger) : IDisposable
{
    private readonly Lock _lock = new();
    private readonly Dictionary<Guid, BoardCanvas> _canvases = [];
    private readonly HashSet<Guid> _dirty = [];
    private readonly HashSet<Guid> _pendingDeletes = [];

    /// <summary>Serialises every store write: the flush tick, and deletes issued by requests.</summary>
    private readonly SemaphoreSlim _storeGate = new(1, 1);

    private bool _persisting = true;

    /// <summary>False from the first failed store operation until one succeeds. Read by the health check.</summary>
    public bool IsPersisting
    {
        get
        {
            lock (_lock)
            {
                return _persisting;
            }
        }
    }

    public int LoadedBoards
    {
        get
        {
            lock (_lock)
            {
                return _canvases.Count;
            }
        }
    }

    /// <summary>Creates the schema, then loads the most recent boards. A failure is logged and the service runs in memory (§4).</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        try
        {
            await store.EnsureSchemaAsync(cancellationToken);
            var recent = await store.LoadRecentAsync(options.Value.RehydrateLimit, cancellationToken);
            lock (_lock)
            {
                foreach (var snapshot in recent)
                {
                    _canvases.TryAdd(snapshot.BoardId, Restore(snapshot));
                }
            }

            LogRehydrated(logger, recent.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            RecordFailure("startup", ex);
        }
    }

    public void Dispose() => _storeGate.Dispose();

    public BoardCanvas? TryGet(Guid boardId)
    {
        lock (_lock)
        {
            return _canvases.GetValueOrDefault(boardId);
        }
    }

    /// <summary>
    /// The board's canvas, loaded from the store on first use. A read that fails throws rather than returning an empty
    /// board: an empty board would be flushed over the stored one on the next tick.
    /// </summary>
    public async Task<BoardCanvas> GetOrLoadAsync(Guid boardId, CancellationToken cancellationToken)
    {
        if (TryGet(boardId) is { } cached)
        {
            return cached;
        }

        BoardCanvas loaded;
        try
        {
            var stored = await store.FindAsync(boardId, cancellationToken);
            loaded = stored is null ? BoardCanvas.Empty(boardId) : Restore(stored);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            RecordFailure("load", ex);
            throw new CanvasStoreUnavailableException($"Canvas {boardId} could not be loaded.", ex);
        }

        lock (_lock)
        {
            if (_canvases.TryGetValue(boardId, out var raced))
            {
                return raced;
            }

            _canvases.Add(boardId, loaded);
            return loaded;
        }
    }

    /// <summary>Marks a board for the next flush. Called under no lock by the hub, after a change was applied.</summary>
    public void MarkDirty(Guid boardId)
    {
        lock (_lock)
        {
            _dirty.Add(boardId);
        }
    }

    /// <summary>
    /// Drops a board from memory and deletes its snapshot. The delete is queued before the store is touched, so a failed
    /// delete is retried on every tick and cannot come back as a ghost on a later rehydrate (state-snapshot §5).
    /// </summary>
    public async Task RemoveAsync(Guid boardId, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            _canvases.Remove(boardId);
            _dirty.Remove(boardId);
            _pendingDeletes.Add(boardId);
        }

        await _storeGate.WaitAsync(cancellationToken);
        try
        {
            await DeletePendingAsync(cancellationToken);
        }
        finally
        {
            _storeGate.Release();
        }
    }

    /// <summary>
    /// One flush tick: pending deletes first, then every dirty board. A board removed mid-tick is skipped, so it cannot be
    /// written back after its delete. Cancellation re-queues the boards not yet written.
    /// </summary>
    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        await _storeGate.WaitAsync(cancellationToken);
        try
        {
            await DeletePendingAsync(cancellationToken);

            var dirty = TakeDirty();
            for (var index = 0; index < dirty.Count; index++)
            {
                var boardId = dirty[index];
                try
                {
                    await WriteAsync(boardId, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    Requeue(dirty.Skip(index));
                    throw;
                }
                catch (Exception ex)
                {
                    Requeue([boardId]);
                    RecordFailure("write", ex);
                }
            }
        }
        finally
        {
            _storeGate.Release();
        }
    }

    private async Task DeletePendingAsync(CancellationToken cancellationToken)
    {
        List<Guid> pending;
        lock (_lock)
        {
            pending = [.. _pendingDeletes];
        }

        foreach (var boardId in pending)
        {
            try
            {
                await store.DeleteAsync(boardId, cancellationToken);
                lock (_lock)
                {
                    _pendingDeletes.Remove(boardId);
                }

                RecordSuccess();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                RecordFailure("delete", ex);
            }
        }
    }

    private async Task WriteAsync(Guid boardId, CancellationToken cancellationToken)
    {
        var canvas = TryGet(boardId);
        if (canvas is null)
        {
            return;
        }

        var state = canvas.Snapshot();
        var json = JsonSerializer.Serialize(state, CanvasJson.Options);
        await store.UpsertAsync(new StoredSnapshot(boardId, state.Version, DateTimeOffset.UtcNow, json), cancellationToken);
        RecordSuccess();
    }

    private List<Guid> TakeDirty()
    {
        lock (_lock)
        {
            var dirty = _dirty.ToList();
            _dirty.Clear();
            return dirty;
        }
    }

    private void Requeue(IEnumerable<Guid> boardIds)
    {
        lock (_lock)
        {
            foreach (var boardId in boardIds)
            {
                if (_canvases.ContainsKey(boardId))
                {
                    _dirty.Add(boardId);
                }
            }
        }
    }

    private static BoardCanvas Restore(StoredSnapshot snapshot)
    {
        var state = JsonSerializer.Deserialize<CanvasState>(snapshot.Json, CanvasJson.Options)
            ?? throw new InvalidOperationException($"Stored canvas {snapshot.BoardId} is empty.");
        return BoardCanvas.Restore(snapshot.BoardId, state);
    }

    private void RecordSuccess()
    {
        var recovered = false;
        lock (_lock)
        {
            recovered = !_persisting;
            _persisting = true;
        }

        if (recovered)
        {
            LogRecovered(logger);
        }
    }

    private void RecordFailure(string operation, Exception ex)
    {
        lock (_lock)
        {
            _persisting = false;
        }

        LogPersistenceFailed(logger, operation, ex.Message);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Canvas snapshots loaded: {Count} boards in memory.")]
    private static partial void LogRehydrated(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Canvas snapshot persistence recovered.")]
    private static partial void LogRecovered(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Canvas snapshot {Operation} failed; boards stay in memory and are retried: {Error}")]
    private static partial void LogPersistenceFailed(ILogger logger, string operation, string error);
}
