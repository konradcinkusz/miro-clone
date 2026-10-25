namespace MiroClone.Web.Canvas.Core;

/// <summary>
/// Undo and redo for one user's edits on one board. Each entry is the inverse of one local action, so undo replays
/// inverses through <see cref="MutationApplier"/>. Every mutation it returns is one to broadcast. Both stacks are
/// bounded: the oldest entry is dropped first.
/// </summary>
public sealed class History
{
    public const int DefaultCapacity = 100;

    private readonly int _capacity;
    private readonly List<IReadOnlyList<LayerMutation>> _undo = [];
    private readonly List<IReadOnlyList<LayerMutation>> _redo = [];

    public History(int capacity = DefaultCapacity)
    {
        if (capacity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "The history must hold at least one entry.");
        }

        _capacity = capacity;
    }

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    /// <summary>
    /// Applies a local action as one undoable step. A mutation that changes nothing adds no entry. Clears the redo stack.
    /// Returns the mutations that changed the document, in order.
    /// </summary>
    public IReadOnlyList<LayerMutation> Execute(CanvasDocument document, IReadOnlyList<LayerMutation> mutations)
    {
        ArgumentNullException.ThrowIfNull(mutations);

        var applied = new List<LayerMutation>();
        var inverses = new List<IReadOnlyList<LayerMutation>>();
        foreach (var mutation in mutations)
        {
            var outcome = MutationApplier.Apply(document, mutation);
            if (outcome.Result == MutationResult.Applied)
            {
                applied.Add(mutation);
                inverses.Add(outcome.Inverse);
            }
        }

        if (applied.Count == 0)
        {
            return applied;
        }

        Push(_undo, Flatten(inverses));
        _redo.Clear();
        return applied;
    }

    public IReadOnlyList<LayerMutation> Undo(CanvasDocument document) => Replay(document, _undo, _redo);

    public IReadOnlyList<LayerMutation> Redo(CanvasDocument document) => Replay(document, _redo, _undo);

    private List<LayerMutation> Replay(
        CanvasDocument document,
        List<IReadOnlyList<LayerMutation>> from,
        List<IReadOnlyList<LayerMutation>> to)
    {
        if (from.Count == 0)
        {
            return [];
        }

        var entry = from[^1];
        from.RemoveAt(from.Count - 1);

        var applied = new List<LayerMutation>();
        var inverses = new List<IReadOnlyList<LayerMutation>>();
        foreach (var mutation in entry)
        {
            var outcome = MutationApplier.Apply(document, mutation);
            if (outcome.Result == MutationResult.Applied)
            {
                applied.Add(mutation);
                inverses.Add(outcome.Inverse);
            }
        }

        if (applied.Count > 0)
        {
            Push(to, Flatten(inverses));
        }

        return applied;
    }

    private void Push(List<IReadOnlyList<LayerMutation>> stack, IReadOnlyList<LayerMutation> entry)
    {
        stack.Add(entry);
        if (stack.Count > _capacity)
        {
            stack.RemoveAt(0);
        }
    }

    /// <summary>The inverses of a sequence, undone last-first, so one undo reverses the whole step.</summary>
    private static List<LayerMutation> Flatten(List<IReadOnlyList<LayerMutation>> inverses)
    {
        var flat = new List<LayerMutation>();
        for (var i = inverses.Count - 1; i >= 0; i--)
        {
            flat.AddRange(inverses[i]);
        }

        return flat;
    }
}
