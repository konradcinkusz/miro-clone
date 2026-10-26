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

/// <summary>A selection handle: the square it covers, in world units.</summary>
public readonly record struct HandleMark(ResizeHandle Handle, Bounds Square);

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
    string? Notice);

/// <summary>
/// The board's interaction rules: which tool is active, what is selected, and what each pointer gesture does. It holds no
/// geometry of its own; hit-testing, net selection, resizing, translation, path simplification and undo all come from
/// <see cref="MiroClone.Web.Canvas.Core"/>. Changes are committed through <see cref="ICanvasSession"/>, one undo step per gesture.
/// </summary>
public sealed class BoardEditor
{
    /// <summary>The size of a handle, and the reach of a stroke hit, in screen pixels. Converted to world units at the current zoom.</summary>
    public const double HandlePixels = 8;
    public const double HitPixels = 4;
    public const double PencilStepPixels = 1;

    /// <summary>The zoom change per wheel pixel. A wheel notch of about 100 pixels zooms by about 16 percent.</summary>
    public const double ZoomPerPixel = 0.0015;

    public const string LimitNotice = "A board holds at most 100 layers.";

    /// <summary>The fill of a new layer. The colour picker and palette arrive with WP11.</summary>
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

    /// <summary>Delete and Backspace remove the selection; Escape cancels the gesture in progress and leaves nothing changed.</summary>
    public void Key(string key)
    {
        switch (key)
        {
            case "Delete" or "Backspace":
                DeleteSelection();
                break;
            case "Escape":
                Cancel();
                break;
        }
    }

    public void SelectTool(CanvasTool tool)
    {
        Cancel();
        Tool = tool;
    }

    /// <summary>Removes the selected layers as one undo step.</summary>
    public void DeleteSelection()
    {
        Cancel();
        var ids = _selection.Where(_session.Document.Contains).ToArray();
        if (ids.Length == 0)
        {
            return;
        }

        _session.Commit(ids.Select(LayerMutation.Delete).ToArray());
        _selection.Clear();
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
        new(Camera, Layers().ToArray(), _selection.ToArray(), SelectionBounds(), Handles(), Net(), _notice);

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
        _draft = LayerFactory.Create(_draftId, _drawType, _origin, _origin, DefaultFill);
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
                _draft = LayerFactory.Create(_draftId, _drawType, _origin, _pointer, DefaultFill);
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
                Insert(LayerFactory.Path(_draftId, PathSimplifier.Simplify(_points, PencilStepPixels / Camera.Zoom), DefaultFill));
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
        _draft = _points.Count >= 2 ? LayerFactory.Path(_draftId, _points, DefaultFill) : null;
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
    }

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

    /// <summary>Handles show for one selected layer that is not a path (ADR 0028: the same rule as the web canvas today).</summary>
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

    private double HandleSize => HandlePixels / Camera.Zoom;

    private Layer Original(Guid id) =>
        _session.Document.TryGet(id, out var layer) ? layer : throw new InvalidOperationException($"Layer {id} is not on the board.");

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
