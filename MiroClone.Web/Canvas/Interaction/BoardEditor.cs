using MiroClone.Web.Canvas.Core;
using MiroClone.Web.Canvas.Editing;

namespace MiroClone.Web.Canvas.Interaction;

public enum CanvasTool
{
    Select,
    Rectangle,
    Ellipse,
    Text,
    Note,
    Pencil,
}

public enum PointerPhase
{
    Down,
    Move,
    Up,
    Cancel,
}

/// <summary>What the surface reports. Coordinates are screen pixels relative to the surface.</summary>
public abstract record CanvasInput;

/// <param name="Primary">True for the main button. Only the main button starts a gesture.</param>
/// <param name="Additive">Shift is held: add to the selection instead of replacing it.</param>
public sealed record PointerInput(PointerPhase Phase, Point Screen, bool Primary, bool Additive) : CanvasInput;

/// <param name="Zoom">Ctrl or Meta is held: zoom at the pointer. Otherwise the wheel pans.</param>
public sealed record WheelInput(Point Screen, double DeltaX, double DeltaY, bool Zoom) : CanvasInput;

/// <summary>A key press from the board page. Ctrl is true for Ctrl or Meta.</summary>
/// <param name="Key">The browser's key name, such as <c>Delete</c>, <c>Enter</c> or <c>z</c>.</param>
public sealed record KeyInput(string Key, bool Ctrl, bool Shift);

/// <summary>A selection handle: the square it covers, in world units.</summary>
public readonly record struct HandleMark(ResizeHandle Handle, Bounds Square);

/// <summary>
/// The text or note being edited in place. <see cref="Box"/> is in screen pixels, so the overlay sits over the layer at the
/// current zoom. <see cref="Ink"/> is the text colour and <see cref="Fill"/> the note's background.
/// </summary>
public sealed record TextEdit(Guid Id, bool Multiline, Bounds Box, string Text, Rgb Fill, Rgb Ink, double FontPixels);

/// <summary>
/// Everything the surface draws in one render. <see cref="BoardEditor.Frame"/> builds it, so the markup reads values and
/// decides nothing. Layers run bottom to top, with a drawing in progress last.
/// </summary>
public sealed record CanvasFrame(
    Camera Camera,
    IReadOnlyList<Layer> Layers,
    IReadOnlyList<Guid> Selection,
    Bounds? SelectionBounds,
    IReadOnlyList<HandleMark> Handles,
    Bounds? Net,
    string? Notice,
    TextEdit? Editing = null,
    bool CanUndo = false,
    bool CanRedo = false);

/// <summary>
/// The board's interaction rules: which tool is active, what is selected, and what each gesture, key and command does. It holds no
/// geometry of its own; hit-testing, net selection, resizing, translation, path simplification and undo all come from
/// <see cref="MiroClone.Web.Canvas.Core"/>. Changes are committed through <see cref="ICanvasSession"/>, one undo step per gesture or command.
/// </summary>
public sealed class BoardEditor
{
    /// <summary>The size of a handle, and the reach of a stroke hit, in screen pixels. Converted to world units at the current zoom.</summary>
    public const double HandlePixels = 8;
    public const double HitPixels = 4;
    public const double PencilStepPixels = 1;

    /// <summary>The zoom change per wheel pixel. A wheel notch of about 100 pixels zooms by about 16 percent.</summary>
    public const double ZoomPerPixel = 0.0015;

    /// <summary>The text size of a text layer, in world units: the SVG default, which the inline editor matches.</summary>
    public const double FontPixels = 16;

    public const string LimitNotice = "A board holds at most 100 layers.";

    /// <summary>The fill of a new rectangle, ellipse or note.</summary>
    public static readonly Rgb DefaultFill = new(0xd9, 0xe2, 0xff);

    private readonly ICanvasSession _session;
    private readonly Func<Guid> _newId;
    private readonly List<Guid> _selection = [];
    private readonly List<Guid> _baseSelection = [];
    private readonly Dictionary<Guid, Layer> _originals = [];
    private readonly Dictionary<Guid, Layer> _preview = [];
    private readonly List<Point> _points = [];
    private Gesture _gesture = Gesture.Idle;
    private Point _origin;
    private Point _pointer;
    private ResizeHandle _handle;
    private LayerType _drawType;
    private Guid _draftId;
    private Layer? _draft;
    private string? _notice;
    private Guid? _editing;
    private string _editText = string.Empty;

    public BoardEditor(ICanvasSession session, Func<Guid>? newId = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _newId = newId ?? Guid.NewGuid;
    }

    public CanvasTool Tool { get; private set; } = CanvasTool.Select;

    public Camera Camera { get; private set; } = Camera.Default;

    public void Apply(CanvasInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        switch (input)
        {
            case PointerInput pointer:
                ApplyPointer(pointer);
                break;
            case WheelInput wheel:
                ApplyWheel(wheel);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(input), input, "The surface sent an input this editor does not know.");
        }
    }

    /// <summary>
    /// The keyboard (ADR 0033). While a text layer is being edited the overlay owns the keyboard, so this does nothing.
    /// Delete and Backspace remove the selection; Escape cancels the gesture in progress; Enter edits the one selected text or note;
    /// Ctrl+Z undoes, Ctrl+Shift+Z redoes, Ctrl+A selects all; V, T, N, R, E and P choose a tool.
    /// </summary>
    public void Key(KeyInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (_editing is not null)
        {
            return;
        }

        if (input.Ctrl)
        {
            switch (input.Key)
            {
                case "z" or "Z" when input.Shift:
                    Redo();
                    break;
                case "z" or "Z":
                    Undo();
                    break;
                case "a" or "A":
                    SelectAll();
                    break;
            }

            return;
        }

        switch (input.Key)
        {
            case "Delete" or "Backspace":
                DeleteSelection();
                break;
            case "Escape":
                Cancel();
                break;
            case "Enter":
                EditSelected();
                break;
            case "v" or "V":
                SelectTool(CanvasTool.Select);
                break;
            case "t" or "T":
                SelectTool(CanvasTool.Text);
                break;
            case "n" or "N":
                SelectTool(CanvasTool.Note);
                break;
            case "r" or "R":
                SelectTool(CanvasTool.Rectangle);
                break;
            case "e" or "E":
                SelectTool(CanvasTool.Ellipse);
                break;
            case "p" or "P":
                SelectTool(CanvasTool.Pencil);
                break;
        }
    }

    public void SelectTool(CanvasTool tool)
    {
        FinishEdit();
        Cancel();
        Tool = tool;
    }

    /// <summary>Removes the selected layers as one undo step.</summary>
    public void DeleteSelection()
    {
        FinishEdit();
        Cancel();
        var ids = _selection.Where(_session.Document.Contains).ToArray();
        if (ids.Length == 0)
        {
            return;
        }

        _session.Commit(ids.Select(LayerMutation.Delete).ToArray());
        _selection.Clear();
    }

    /// <summary>Drops selected layers that a remote change removed, so the selection never names a layer that is not on the board (WP10).</summary>
    public void Reconcile() => PruneSelection();

    /// <summary>Selects every layer on the board.</summary>
    public void SelectAll()
    {
        FinishEdit();
        Cancel();
        _selection.Clear();
        _selection.AddRange(_session.Document.Order);
    }

    /// <summary>Moves the selected layers above every other layer, keeping their order among themselves. One undo step.</summary>
    public void BringToFront() => Reorder(LayerOrdering.BringToFront);

    /// <summary>Moves the selected layers below every other layer, keeping their order among themselves. One undo step.</summary>
    public void SendToBack() => Reorder(LayerOrdering.SendToBack);

    /// <summary>Gives every selected layer the colour, as one undo step. A layer already that colour is left out.</summary>
    public void SetFill(Rgb fill)
    {
        FinishEdit();
        Cancel();
        var changed = new List<LayerMutation>();
        foreach (var id in _selection)
        {
            if (_session.Document.TryGet(id, out var layer) && layer.Fill != fill)
            {
                changed.Add(LayerMutation.Upsert(layer with { Fill = fill }));
            }
        }

        if (changed.Count > 0)
        {
            _session.Commit(changed);
        }
    }

    /// <summary>Reverses the last step. The selection keeps only the layers that still exist.</summary>
    public void Undo()
    {
        FinishEdit();
        Cancel();
        _session.Undo();
        PruneSelection();
    }

    /// <summary>Replays the last undone step.</summary>
    public void Redo()
    {
        FinishEdit();
        Cancel();
        _session.Redo();
        PruneSelection();
    }

    /// <summary>Opens a text or note layer for editing in place. Other layer types cannot be edited in place.</summary>
    public void BeginEdit(Guid id)
    {
        FinishEdit();
        Cancel();
        if (!_session.Document.TryGet(id, out var layer) || !IsText(layer))
        {
            return;
        }

        _selection.Clear();
        _selection.Add(id);
        _editing = id;
        _editText = layer.Value ?? string.Empty;
    }

    /// <summary>Replaces the text being edited. Each keystroke calls this; nothing is committed until <see cref="FinishEdit"/>.</summary>
    public void EditText(string text)
    {
        if (_editing is not null)
        {
            _editText = text;
        }
    }

    /// <summary>
    /// Ends the edit and commits the text as one undo step. Empty text removes the layer, so a click with the text tool that types
    /// nothing leaves nothing behind. Does nothing when no edit is open.
    /// </summary>
    public void FinishEdit()
    {
        if (_editing is not { } id)
        {
            return;
        }

        var text = _editText;
        _editing = null;
        _editText = string.Empty;

        if (!_session.Document.TryGet(id, out var layer))
        {
            return;
        }

        if (text.Length == 0)
        {
            _session.Commit([LayerMutation.Delete(id)]);
            _selection.Remove(id);
            return;
        }

        if ((layer.Value ?? string.Empty) != text)
        {
            _session.Commit([LayerMutation.Upsert(layer with { Value = text })]);
        }
    }

    /// <summary>Drops the gesture in progress without committing it. A drag returns to where it started.</summary>
    public void Cancel()
    {
        _gesture = Gesture.Idle;
        _preview.Clear();
        _originals.Clear();
        _points.Clear();
        _baseSelection.Clear();
        _draft = null;
    }

    public CanvasFrame Frame() =>
        new(
            Camera,
            Layers().ToArray(),
            _selection.ToArray(),
            SelectionBounds(),
            Handles(),
            Net(),
            _notice,
            EditView(),
            _session.CanUndo,
            _session.CanRedo);

    private void ApplyPointer(PointerInput input)
    {
        switch (input.Phase)
        {
            case PointerPhase.Down when input.Primary:
                Press(input.Screen, input.Additive);
                break;
            case PointerPhase.Move:
                Drag(input.Screen);
                break;
            case PointerPhase.Up:
                Release(input.Screen);
                break;
            case PointerPhase.Cancel:
                Cancel();
                break;
        }
    }

    private void ApplyWheel(WheelInput input)
    {
        if (!double.IsFinite(input.DeltaX) || !double.IsFinite(input.DeltaY))
        {
            return;
        }

        Camera = input.Zoom
            ? Camera.ZoomAt(input.Screen, Math.Exp(Math.Clamp(-input.DeltaY, -500, 500) * ZoomPerPixel))
            : Camera.Pan(-input.DeltaX, -input.DeltaY);
    }

    private void Press(Point screen, bool additive)
    {
        FinishEdit();
        Cancel();
        _notice = null;
        _pointer = _origin = Camera.ScreenToWorld(screen);

        if (Tool == CanvasTool.Select)
        {
            PressSelect(_origin, additive);
            return;
        }

        if (_session.Document.Count >= CanvasDocument.MaxLayers)
        {
            _notice = LimitNotice;
            return;
        }

        _draftId = _newId();
        if (Tool == CanvasTool.Pencil)
        {
            _gesture = Gesture.Pencil;
            _points.Add(_origin);
            return;
        }

        _gesture = Gesture.Draw;
        _drawType = ToLayerType(Tool);
        _draft = LayerFactory.Create(_draftId, _drawType, _origin, _origin, FillFor(Tool));
    }

    private void PressSelect(Point world, bool additive)
    {
        if (CanResize() && SelectionBounds() is { } box &&
            Resizer.HandleAt(box, world, HandleSize / 2) is { } handle)
        {
            _gesture = Gesture.Resize;
            _handle = handle;
            _originals[_selection[0]] = Original(_selection[0]);
            return;
        }

        var hit = HitTester.Topmost(_session.Document, world, HitPixels / Camera.Zoom);
        if (hit is not null)
        {
            if (!_selection.Contains(hit.Id))
            {
                if (!additive)
                {
                    _selection.Clear();
                }

                _selection.Add(hit.Id);
            }

            foreach (var id in _selection)
            {
                _originals[id] = Original(id);
            }

            _gesture = Gesture.Translate;
            return;
        }

        if (!additive)
        {
            _selection.Clear();
        }

        _baseSelection.AddRange(_selection);
        _gesture = Gesture.Net;
    }

    private void Drag(Point screen)
    {
        _pointer = Camera.ScreenToWorld(screen);

        switch (_gesture)
        {
            case Gesture.Translate:
                MoveSelection();
                break;
            case Gesture.Resize:
                ResizeSelection();
                break;
            case Gesture.Net:
                SelectNet();
                break;
            case Gesture.Draw:
                _draft = LayerFactory.Create(_draftId, _drawType, _origin, _pointer, FillFor(Tool));
                break;
            case Gesture.Pencil:
                ExtendStroke();
                break;
        }
    }

    private void Release(Point screen)
    {
        Drag(screen);

        switch (_gesture)
        {
            case Gesture.Translate or Gesture.Resize:
                CommitPreview();
                break;
            case Gesture.Draw when _draft is { } draft:
                Insert(draft);
                break;
            case Gesture.Pencil when _points.Count >= 2:
                Insert(LayerFactory.Path(_draftId, PathSimplifier.Simplify(_points, PencilStepPixels / Camera.Zoom), FillFor(CanvasTool.Pencil)));
                break;
        }

        Cancel();
    }

    /// <summary>Every selected layer moves from where it was at press, so the drag never accumulates rounding.</summary>
    private void MoveSelection()
    {
        var dx = _pointer.X - _origin.X;
        var dy = _pointer.Y - _origin.Y;

        _preview.Clear();
        foreach (var (id, layer) in _originals)
        {
            _preview[id] = Translator.Move(layer, dx, dy);
        }
    }

    private void ResizeSelection()
    {
        var layer = _originals.Values.Single();
        _preview.Clear();
        _preview[layer.Id] = Resizer.Resize(layer, _handle, _pointer);
    }

    private void SelectNet()
    {
        var net = Marquee.Net(_origin, _pointer);
        _selection.Clear();
        _selection.AddRange(_baseSelection);
        foreach (var id in Marquee.Select(_session.Document, net))
        {
            if (!_selection.Contains(id))
            {
                _selection.Add(id);
            }
        }
    }

    private void ExtendStroke()
    {
        if (PathGeometry.Distance(_points[^1], _pointer) < PencilStepPixels / Camera.Zoom)
        {
            return;
        }

        _points.Add(_pointer);
        _draft = _points.Count >= 2 ? LayerFactory.Path(_draftId, _points, FillFor(CanvasTool.Pencil)) : null;
    }

    private void CommitPreview()
    {
        var changed = _preview.Values
            .Where(moved => !moved.HasSameContent(_originals[moved.Id]))
            .Select(LayerMutation.Upsert)
            .ToArray();

        if (changed.Length > 0)
        {
            _session.Commit(changed);
        }
    }

    private void Insert(Layer layer)
    {
        if (_session.Document.Count >= CanvasDocument.MaxLayers)
        {
            _notice = LimitNotice;
            return;
        }

        _session.Commit([LayerMutation.Upsert(layer)]);
        Tool = CanvasTool.Select;
        if (IsText(layer))
        {
            BeginEdit(layer.Id);
        }
    }

    private void Reorder(Func<IReadOnlyList<Guid>, IEnumerable<Guid>, IReadOnlyList<Guid>> arrange)
    {
        FinishEdit();
        Cancel();
        var selected = _selection.Where(_session.Document.Contains).ToArray();
        if (selected.Length == 0)
        {
            return;
        }

        _session.Commit([LayerMutation.Reorder(arrange(_session.Document.Order, selected))]);
    }

    /// <summary>Enter opens the one selected text or note. Anything else does nothing.</summary>
    private void EditSelected()
    {
        if (_selection is [var only] && _session.Document.TryGet(only, out var layer) && IsText(layer))
        {
            BeginEdit(only);
        }
    }

    private void PruneSelection() => _selection.RemoveAll(id => !_session.Document.Contains(id));

    private IEnumerable<Layer> Layers()
    {
        foreach (var layer in _session.Document.Ordered())
        {
            yield return _preview.GetValueOrDefault(layer.Id, layer);
        }

        if (_draft is { } draft)
        {
            yield return draft;
        }
    }

    private Bounds? SelectionBounds()
    {
        var boxes = Layers().Where(layer => _selection.Contains(layer.Id)).Select(layer => layer.Bounds).ToArray();
        return Bounds.Union(boxes);
    }

    /// <summary>Handles show for one selected layer that is not a path (ADR 0031: the same rule as the web canvas today).</summary>
    private bool CanResize() =>
        _selection is [var only] && _session.Document.TryGet(only, out var layer) && layer.Type != LayerType.Path;

    private HandleMark[] Handles()
    {
        if (!CanResize() || SelectionBounds() is not { } box)
        {
            return [];
        }

        var size = HandleSize;
        return Enum.GetValues<ResizeHandle>()
            .Select(handle =>
            {
                var at = Resizer.HandlePosition(box, handle);
                return new HandleMark(handle, new Bounds(at.X - (size / 2), at.Y - (size / 2), size, size));
            })
            .ToArray();
    }

    private Bounds? Net() => _gesture == Gesture.Net ? Marquee.Net(_origin, _pointer) : null;

    /// <summary>The overlay for the text being edited, placed over the layer at the current zoom.</summary>
    private TextEdit? EditView()
    {
        if (_editing is not { } id || !_session.Document.TryGet(id, out var layer))
        {
            return null;
        }

        var at = Camera.WorldToScreen(new Point(layer.X, layer.Y));
        var box = new Bounds(at.X, at.Y, layer.Width * Camera.Zoom, layer.Height * Camera.Zoom);
        var note = layer.Type == LayerType.Note;
        var ink = note ? Palette.ReadableOn(layer.Fill) : layer.Fill;
        return new TextEdit(id, note, box, _editText, layer.Fill, ink, FontPixels * Camera.Zoom);
    }

    private double HandleSize => HandlePixels / Camera.Zoom;

    private Layer Original(Guid id) =>
        _session.Document.TryGet(id, out var layer) ? layer : throw new InvalidOperationException($"Layer {id} is not on the board.");

    private static bool IsText(Layer layer) => layer.Type is LayerType.Text or LayerType.Note;

    /// <summary>Text and pencil strokes default to ink, so they read on the white surface; shapes and notes take the shape default.</summary>
    private static Rgb FillFor(CanvasTool tool) => tool is CanvasTool.Text or CanvasTool.Pencil ? Palette.Ink : DefaultFill;

    private static LayerType ToLayerType(CanvasTool tool) => tool switch
    {
        CanvasTool.Rectangle => LayerType.Rectangle,
        CanvasTool.Ellipse => LayerType.Ellipse,
        CanvasTool.Text => LayerType.Text,
        CanvasTool.Note => LayerType.Note,
        _ => throw new ArgumentOutOfRangeException(nameof(tool), tool, "Only a shape tool makes a layer from a drag."),
    };

    private enum Gesture
    {
        Idle,
        Translate,
        Resize,
        Net,
        Draw,
        Pencil,
    }
}
