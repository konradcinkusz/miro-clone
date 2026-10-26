using MiroClone.Web.Canvas.Core;
using MiroClone.Web.Canvas.Editing;
using MiroClone.Web.Canvas.Interaction;
using Xunit;

namespace MiroClone.Web.Tests.Canvas.Interaction;

/// <summary>
/// The board's gestures: what each one changes, and how many undo steps it makes. The geometry is Core's; these tests pin the
/// editor's choices about when a gesture commits, what it previews, and what a key or wheel does.
/// </summary>
public sealed class BoardEditorTests
{
    private static readonly Guid NewId = Guid.Parse("00000000-0000-4000-8000-00000000be01");

    [Fact]
    public void DraggingWithTheRectangleToolCommitsOneLayerAndReturnsToSelect()
    {
        var session = new RecordingSession([]);
        var editor = new BoardEditor(session, () => NewId);
        editor.SelectTool(CanvasTool.Rectangle);

        editor.Apply(Down(10, 20));
        editor.Apply(Move(60, 70));
        editor.Apply(Up(110, 120));

        Assert.Single(session.Commits);
        Assert.True(session.Document.TryGet(NewId, out var layer));
        Assert.Equal(new Bounds(10, 20, 100, 100), layer.Bounds);
        Assert.Equal(CanvasTool.Select, editor.Tool);
    }

    [Fact]
    public void AClickWithTheTextToolMakesTheDefaultTextBox()
    {
        var session = new RecordingSession([]);
        var editor = new BoardEditor(session, () => NewId);
        editor.SelectTool(CanvasTool.Text);

        editor.Apply(Down(30, 40));
        editor.Apply(Up(30, 40));

        Assert.True(session.Document.TryGet(NewId, out var layer));
        Assert.Equal(new Bounds(30, 40, 160, 40), layer.Bounds);
        Assert.Equal(LayerType.Text, layer.Type);
    }

    [Fact]
    public void ANoteIsMadeWithTheNoteTool()
    {
        var session = new RecordingSession([]);
        var editor = new BoardEditor(session, () => NewId);

        editor.SelectTool(CanvasTool.Note);
        editor.Apply(Down(0, 0));
        editor.Apply(Up(50, 50));

        Assert.True(session.Document.TryGet(NewId, out var note));
        Assert.Equal(LayerType.Note, note.Type);
    }

    [Fact]
    public void AtTheLayerLimitTheToolDrawsNothingAndSaysSo()
    {
        var full = Enumerable.Range(0, CanvasDocument.MaxLayers).Select(_ => Rectangle(0, 0, 10, 10)).ToArray();
        var session = new RecordingSession(full);
        var editor = new BoardEditor(session, () => NewId);
        editor.SelectTool(CanvasTool.Rectangle);

        editor.Apply(Down(0, 0));
        editor.Apply(Up(50, 50));

        Assert.Empty(session.Commits);
        Assert.Equal(CanvasDocument.MaxLayers, session.Document.Count);
        Assert.Equal(BoardEditor.LimitNotice, editor.Frame().Notice);
    }

    [Fact]
    public void DraggingASelectedLayerMovesItByTheDragAsOneStep()
    {
        var layer = Rectangle(0, 0, 50, 50);
        var session = new RecordingSession([layer]);
        var editor = new BoardEditor(session);

        editor.Apply(Down(10, 10));
        editor.Apply(Move(30, 20));
        editor.Apply(Move(40, 25));
        editor.Apply(Up(40, 25));

        Assert.Single(session.Commits);
        Assert.True(session.Document.TryGet(layer.Id, out var moved));
        Assert.Equal(new Bounds(30, 15, 50, 50), moved.Bounds);
    }

    [Fact]
    public void ReleasingWithoutMovingCommitsNothing()
    {
        var layer = Rectangle(0, 0, 50, 50);
        var session = new RecordingSession([layer]);
        var editor = new BoardEditor(session);

        editor.Apply(Down(10, 10));
        editor.Apply(Up(10, 10));

        Assert.Empty(session.Commits);
        Assert.Equal([layer.Id], editor.Frame().Selection);
    }

    [Fact]
    public void ADragPreviewIsShownButNotCommittedUntilRelease()
    {
        var layer = Rectangle(0, 0, 50, 50);
        var session = new RecordingSession([layer]);
        var editor = new BoardEditor(session);

        editor.Apply(Down(10, 10));
        editor.Apply(Move(60, 10));

        Assert.Empty(session.Commits);
        Assert.Equal(50, editor.Frame().Layers.Single().X);
        Assert.Equal(0, layer.X);
    }

    [Fact]
    public void EscapeDuringADragPutsTheLayerBackWithoutACommit()
    {
        var layer = Rectangle(0, 0, 50, 50);
        var session = new RecordingSession([layer]);
        var editor = new BoardEditor(session);

        editor.Apply(Down(10, 10));
        editor.Apply(Move(60, 10));
        editor.Key(Plain("Escape"));

        Assert.Empty(session.Commits);
        Assert.Equal(0, editor.Frame().Layers.Single().X);
    }

    [Fact]
    public void ShiftAddsToTheSelectionInsteadOfReplacingIt()
    {
        var first = Rectangle(0, 0, 10, 10);
        var second = Rectangle(100, 100, 10, 10);
        var session = new RecordingSession([first, second]);
        var editor = new BoardEditor(session);

        editor.Apply(Down(5, 5));
        editor.Apply(Up(5, 5));
        editor.Apply(Down(105, 105, additive: true));
        editor.Apply(Up(105, 105, additive: true));

        Assert.Equal([first.Id, second.Id], editor.Frame().Selection);
    }

    [Fact]
    public void ANetDragSelectsEveryLayerItTouches()
    {
        var inside = Rectangle(0, 0, 10, 10);
        var grazed = Rectangle(60, 60, 10, 10);
        var outside = Rectangle(200, 200, 10, 10);
        var editor = new BoardEditor(new RecordingSession([inside, grazed, outside]));

        editor.Apply(Down(-5, -5));
        editor.Apply(Move(50, 50));

        Assert.NotNull(editor.Frame().Net);
        Assert.Equal([inside.Id], editor.Frame().Selection);

        editor.Apply(Move(70, 70));
        Assert.Equal([inside.Id, grazed.Id], editor.Frame().Selection);

        editor.Apply(Up(70, 70));
        Assert.Null(editor.Frame().Net);
    }

    [Fact]
    public void FrameOffersEightHandlesForASingleSelectedRectangle()
    {
        var layer = Rectangle(0, 0, 100, 100);
        var editor = new BoardEditor(new RecordingSession([layer]));
        editor.Apply(Down(50, 50));
        editor.Apply(Up(50, 50));

        var frame = editor.Frame();

        Assert.Equal(new Bounds(0, 0, 100, 100), frame.SelectionBounds);
        Assert.Equal(8, frame.Handles.Count);
    }

    [Fact]
    public void ResizingFromTheSouthEastHandleMovesOnlyThatCorner()
    {
        var layer = Rectangle(0, 0, 100, 100);
        var session = new RecordingSession([layer]);
        var editor = new BoardEditor(session);
        editor.Apply(Down(50, 50));
        editor.Apply(Up(50, 50));

        editor.Apply(Down(100, 100));
        editor.Apply(Move(150, 140));
        editor.Apply(Up(150, 140));

        Assert.Single(session.Commits);
        Assert.True(session.Document.TryGet(layer.Id, out var resized));
        Assert.Equal(new Bounds(0, 0, 150, 140), resized.Bounds);
    }

    [Fact]
    public void APathHasNoHandles()
    {
        var path = LayerFactory.Path(Guid.NewGuid(), [new Point(0, 0), new Point(40, 40)], new Rgb(1, 1, 1));
        var editor = new BoardEditor(new RecordingSession([path]));
        editor.Apply(Down(20, 20));
        editor.Apply(Up(20, 20));

        Assert.Empty(editor.Frame().Handles);
    }

    [Fact]
    public void DeleteRemovesTheWholeSelectionInOneStep()
    {
        var first = Rectangle(0, 0, 10, 10);
        var second = Rectangle(20, 20, 10, 10);
        var session = new RecordingSession([first, second]);
        var editor = new BoardEditor(session);
        editor.Apply(Down(5, 5));
        editor.Apply(Up(5, 5));
        editor.Apply(Down(25, 25, additive: true));
        editor.Apply(Up(25, 25, additive: true));

        editor.Key(Plain("Delete"));

        Assert.Single(session.Commits);
        Assert.Equal(0, session.Document.Count);
        Assert.Empty(editor.Frame().Selection);
    }

    [Fact]
    public void DeleteWithNothingSelectedCommitsNothing()
    {
        var session = new RecordingSession([Rectangle(0, 0, 10, 10)]);

        new BoardEditor(session).Key(Plain("Backspace"));

        Assert.Empty(session.Commits);
    }

    [Fact]
    public void APencilStrokeCommitsOneSimplifiedPath()
    {
        var session = new RecordingSession([]);
        var editor = new BoardEditor(session, () => NewId);
        editor.SelectTool(CanvasTool.Pencil);

        editor.Apply(Down(0, 0));
        for (var x = 1; x <= 10; x++)
        {
            editor.Apply(Move(x, 0));
        }

        editor.Apply(Up(10, 0));

        Assert.Single(session.Commits);
        Assert.True(session.Document.TryGet(NewId, out var path));
        Assert.Equal(LayerType.Path, path.Type);
        Assert.Equal([new Point(0, 0), new Point(10, 0)], path.Points);
    }

    [Fact]
    public void APencilClickDrawsNothing()
    {
        var session = new RecordingSession([]);
        var editor = new BoardEditor(session);
        editor.SelectTool(CanvasTool.Pencil);

        editor.Apply(Down(4, 4));
        editor.Apply(Up(4, 4));

        Assert.Empty(session.Commits);
    }

    [Fact]
    public void AWheelPansTheViewOppositeToTheScroll()
    {
        var editor = new BoardEditor(new RecordingSession([]));

        editor.Apply(new WheelInput(new Point(0, 0), DeltaX: 5, DeltaY: 10, Zoom: false));

        Assert.Equal(-5, editor.Camera.OffsetX);
        Assert.Equal(-10, editor.Camera.OffsetY);
    }

    [Fact]
    public void ACtrlWheelZoomsAroundThePointerAndKeepsTheWorldPointUnderIt()
    {
        var editor = new BoardEditor(new RecordingSession([]));
        var anchor = new Point(200, 120);
        var before = editor.Camera.ScreenToWorld(anchor);

        editor.Apply(new WheelInput(anchor, DeltaX: 0, DeltaY: -100, Zoom: true));

        Assert.True(editor.Camera.Zoom > 1);
        var after = editor.Camera.ScreenToWorld(anchor);
        Assert.Equal(before.X, after.X, 6);
        Assert.Equal(before.Y, after.Y, 6);
    }

    [Fact]
    public void ANonFiniteWheelChangesNothing()
    {
        var editor = new BoardEditor(new RecordingSession([]));

        editor.Apply(new WheelInput(new Point(0, 0), double.NaN, 10, Zoom: false));

        Assert.Equal(Camera.Default, editor.Camera);
    }

    [Fact]
    public void ANonPrimaryButtonDoesNotSelect()
    {
        var layer = Rectangle(0, 0, 50, 50);
        var editor = new BoardEditor(new RecordingSession([layer]));

        editor.Apply(new PointerInput(PointerPhase.Down, new Point(10, 10), Primary: false, Additive: false));

        Assert.Empty(editor.Frame().Selection);
    }

    [Fact]
    public void TheFrameShowsTheSelectionInWorldUnitsAtTheCurrentZoom()
    {
        var layer = Rectangle(0, 0, 10, 10);
        var editor = new BoardEditor(new RecordingSession([layer]));
        editor.Apply(new WheelInput(new Point(0, 0), 0, -100, Zoom: true));
        editor.Apply(Down(5, 5));
        editor.Apply(Up(5, 5));

        var handle = editor.Frame().Handles[0].Square;

        Assert.Equal(BoardEditor.HandlePixels / editor.Camera.Zoom, handle.Width, 6);
    }

    [Fact]
    public void CtrlZUndoesTheLastGestureAndCtrlShiftZRedoesIt()
    {
        var session = new RecordingSession([]);
        var editor = new BoardEditor(session, () => NewId);
        editor.SelectTool(CanvasTool.Rectangle);
        editor.Apply(Down(0, 0));
        editor.Apply(Up(50, 50));

        editor.Key(new KeyInput("z", Ctrl: true, Shift: false));
        Assert.Equal(0, session.Document.Count);
        Assert.True(editor.Frame().CanRedo);

        editor.Key(new KeyInput("Z", Ctrl: true, Shift: true));
        Assert.Equal(1, session.Document.Count);
    }

    [Fact]
    public void BringToFrontAndSendToBackMoveTheSelectionAsOneStep()
    {
        var a = Rectangle(0, 0, 10, 10);
        var b = Rectangle(20, 0, 10, 10);
        var c = Rectangle(40, 0, 10, 10);
        var session = new RecordingSession([a, b, c]);
        var editor = new BoardEditor(session);
        editor.Apply(Down(5, 5));
        editor.Apply(Up(5, 5));

        editor.BringToFront();
        Assert.Equal(new[] { b.Id, c.Id, a.Id }, session.Document.Order);

        editor.SendToBack();
        Assert.Equal(new[] { a.Id, b.Id, c.Id }, session.Document.Order);
        Assert.Equal(2, session.Commits.Count);
    }

    [Fact]
    public void SetFillRecoloursEverySelectedLayerInOneStep()
    {
        var a = Rectangle(0, 0, 10, 10);
        var b = Rectangle(20, 0, 10, 10);
        var session = new RecordingSession([a, b]);
        var editor = new BoardEditor(session);
        editor.SelectAll();

        var red = Palette.Colours[0].Colour;
        editor.SetFill(red);

        Assert.Single(session.Commits);
        Assert.All(session.Document.Ordered(), layer => Assert.Equal(red, layer.Fill));
    }

    [Fact]
    public void TypingIntoATextLayerCommitsTheTextWhenEditingFinishes()
    {
        var session = new RecordingSession([]);
        var editor = new BoardEditor(session, () => NewId);
        editor.SelectTool(CanvasTool.Text);
        editor.Apply(Down(30, 40));
        editor.Apply(Up(30, 40));

        Assert.NotNull(editor.Frame().Editing);
        editor.EditText("Hello");
        editor.FinishEdit();

        Assert.Null(editor.Frame().Editing);
        Assert.Equal("Hello", Assert.Single(session.Document.Ordered()).Value);
        Assert.Equal(2, session.Commits.Count);
    }

    [Fact]
    public void AnEmptyTextLayerIsRemovedWhenEditingFinishes()
    {
        var session = new RecordingSession([]);
        var editor = new BoardEditor(session, () => NewId);
        editor.SelectTool(CanvasTool.Note);
        editor.Apply(Down(0, 0));
        editor.Apply(Up(0, 0));

        editor.FinishEdit();

        Assert.Equal(0, session.Document.Count);
        Assert.Empty(editor.Frame().Selection);
    }

    [Fact]
    public void TheKeyboardBelongsToTheTextOverlayWhileItIsOpen()
    {
        var session = new RecordingSession([Rectangle(0, 0, 10, 10)]);
        var editor = new BoardEditor(session);
        editor.SelectTool(CanvasTool.Text);
        editor.Apply(Down(30, 40));
        editor.Apply(Up(30, 40));

        editor.Key(Plain("Delete"));
        editor.Key(new KeyInput("z", Ctrl: true, Shift: false));

        Assert.Equal(2, session.Document.Count);
        Assert.NotNull(editor.Frame().Editing);
    }

    [Fact]
    public void EnterEditsTheOneSelectedTextLayer()
    {
        var text = new Layer(Guid.NewGuid(), LayerType.Text, 0, 0, 100, 40, new Rgb(0, 0, 0), "old");
        var editor = new BoardEditor(new RecordingSession([text]));
        editor.Apply(Down(5, 5));
        editor.Apply(Up(5, 5));

        editor.Key(Plain("Enter"));

        Assert.Equal(text.Id, editor.Frame().Editing!.Id);
        Assert.Equal("old", editor.Frame().Editing!.Text);
    }

    [Fact]
    public void SelectAllSelectsEveryLayer()
    {
        var session = new RecordingSession([Rectangle(0, 0, 10, 10), Rectangle(20, 0, 10, 10)]);
        var editor = new BoardEditor(session);

        editor.Key(new KeyInput("a", Ctrl: true, Shift: false));

        Assert.Equal(2, editor.Frame().Selection.Count);
    }

    [Fact]
    public void ReconcileDropsASelectedLayerThatARemoteDeleteRemoved()
    {
        var layer = Rectangle(0, 0, 50, 50);
        var session = new RecordingSession([layer]);
        var editor = new BoardEditor(session);
        editor.Apply(Down(10, 10));
        editor.Apply(Up(10, 10));
        Assert.Equal([layer.Id], editor.Frame().Selection);

        session.Commit([LayerMutation.Delete(layer.Id)]);
        editor.Reconcile();

        Assert.Empty(editor.Frame().Selection);
        Assert.Null(editor.Frame().SelectionBounds);
    }

    private static KeyInput Plain(string key) => new(key, Ctrl: false, Shift: false);

    private static Layer Rectangle(double x, double y, double width, double height) =>
        new(Guid.NewGuid(), LayerType.Rectangle, x, y, width, height, new Rgb(1, 2, 3));

    private static PointerInput Down(double x, double y, bool additive = false) =>
        new(PointerPhase.Down, new Point(x, y), Primary: true, Additive: additive);

    private static PointerInput Move(double x, double y) =>
        new(PointerPhase.Move, new Point(x, y), Primary: true, Additive: false);

    private static PointerInput Up(double x, double y, bool additive = false) =>
        new(PointerPhase.Up, new Point(x, y), Primary: true, Additive: additive);

    /// <summary>A session that records each commit, so a test can count the undo steps a gesture made.</summary>
    private sealed class RecordingSession(IEnumerable<Layer> layers) : ICanvasSession
    {
        private readonly LocalCanvasSession _inner = new(CanvasDocument.Load(layers));

        public List<IReadOnlyList<LayerMutation>> Commits { get; } = [];

        public CanvasDocument Document => _inner.Document;

        public bool CanUndo => _inner.CanUndo;

        public bool CanRedo => _inner.CanRedo;

        public IReadOnlyList<LayerMutation> Undo() => _inner.Undo();

        public IReadOnlyList<LayerMutation> Redo() => _inner.Redo();

        public IReadOnlyList<LayerMutation> Commit(IReadOnlyList<LayerMutation> mutations)
        {
            var applied = _inner.Commit(mutations);
            if (applied.Count > 0)
            {
                Commits.Add(applied);
            }

            return applied;
        }

        public ValueTask DisposeAsync() => _inner.DisposeAsync();
    }
}
