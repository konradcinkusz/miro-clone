using MiroClone.Web.Canvas.Core;

namespace MiroClone.Web.Canvas.Editing;

/// <summary>
/// The circuit's copy of one board's layers. Every local edit goes through <see cref="Commit"/>, so the page never changes the
/// document itself and every change has an inverse for undo. The page uses the hub implementation (<c>HubCanvasSession</c>), which
/// also sends the returned mutations to the room and applies the remote ones. Tests use an in-memory double (WP15 removed the
/// production one: the page never ran it).
/// </summary>
public interface ICanvasSession : IAsyncDisposable
{
    /// <summary>The layers as the session holds them. Read it freely; change it only through <see cref="Commit"/>, <see cref="Undo"/> or <see cref="Redo"/>.</summary>
    CanvasDocument Document { get; }

    /// <summary>Whether <see cref="Undo"/> has a step to take.</summary>
    bool CanUndo { get; }

    /// <summary>Whether <see cref="Redo"/> has a step to take.</summary>
    bool CanRedo { get; }

    /// <summary>
    /// Applies a local action as one undoable step, through <see cref="MutationApplier"/> and <see cref="History"/>.
    /// Returns the mutations that changed the document, in order: the ones WP10 sends to the room.
    /// </summary>
    IReadOnlyList<LayerMutation> Commit(IReadOnlyList<LayerMutation> mutations);

    /// <summary>Reverses the last step. Returns the mutations that changed the document, which WP10 also sends to the room.</summary>
    IReadOnlyList<LayerMutation> Undo();

    /// <summary>Replays the last undone step. Returns the mutations that changed the document.</summary>
    IReadOnlyList<LayerMutation> Redo();
}
