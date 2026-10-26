using MiroClone.Web.Canvas.Core;

namespace MiroClone.Web.Canvas.Editing;

/// <summary>
/// The circuit's copy of one board's layers. Every local edit goes through <see cref="Commit"/>, so the page never changes the
/// document itself and every change has an inverse for undo. WP09 implements it in memory, loaded over REST. WP10 adds the hub
/// implementation with the same calls: it also sends the returned mutations to the room and applies the remote ones.
/// </summary>
public interface ICanvasSession : IAsyncDisposable
{
    /// <summary>The layers as the session holds them. Read it freely; change it only through <see cref="Commit"/>.</summary>
    CanvasDocument Document { get; }

    /// <summary>
    /// Applies a local action as one undoable step, through <see cref="MutationApplier"/> and <see cref="History"/>.
    /// Returns the mutations that changed the document, in order: the ones WP10 sends to the room.
    /// </summary>
    IReadOnlyList<LayerMutation> Commit(IReadOnlyList<LayerMutation> mutations);
}
