using MiroClone.Web.Canvas.Core;

namespace MiroClone.Web.Canvas.Editing;

/// <summary>
/// The in-memory session the editor tests use: the layers are edited in memory and nothing is sent anywhere.
/// </summary>
public sealed class LocalCanvasSession(CanvasDocument document) : ICanvasSession
{
    private readonly History _history = new();

    public CanvasDocument Document { get; } = document ?? throw new ArgumentNullException(nameof(document));

    public bool CanUndo => _history.CanUndo;

    public bool CanRedo => _history.CanRedo;

    public IReadOnlyList<LayerMutation> Commit(IReadOnlyList<LayerMutation> mutations) => _history.Execute(Document, mutations);

    public IReadOnlyList<LayerMutation> Undo() => _history.Undo(Document);

    public IReadOnlyList<LayerMutation> Redo() => _history.Redo(Document);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
