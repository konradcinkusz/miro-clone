using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MiroClone.Web.Canvas.Core;
using MiroClone.Web.Canvas.Editing;
using MiroClone.Web.Canvas.Interaction;
using MiroClone.Web.Canvas.Interop;
using MiroClone.Web.Components.Board;
using MiroClone.Web.Tests.TestSupport;
using Xunit;

namespace MiroClone.Web.Tests.Components.Board;

/// <summary>The surface draws a frame and hands input up. Pointer capture and coordinates are the browser's job (ADR 0027).</summary>
public sealed class CanvasSurfaceTests : BunitContext
{
    private readonly FakeCanvasPointer _pointer = new();

    public CanvasSurfaceTests() => Services.AddSingleton<ICanvasPointerSource>(_pointer);

    [Fact]
    public void TheSurfaceAttachesOnceAndDrawsTheWorldAtTheCameraTransform()
    {
        var frame = new BoardEditor(new LocalCanvasSession(CanvasDocument.Load([]))).Frame() with
        {
            Camera = new Camera(5, 7, 2),
        };

        var page = Render<CanvasSurface>(parameters => parameters.Add(component => component.Frame, frame));

        Assert.Equal(1, _pointer.AttachCount);
        Assert.Equal("matrix(2 0 0 2 5 7)", page.Find("[data-testid='canvas-world']").GetAttribute("transform"));
    }

    [Fact]
    public void ASelectedRectangleShowsItsBoxAndEightHandles()
    {
        var layer = new Layer(Guid.NewGuid(), LayerType.Rectangle, 0, 0, 100, 100, new Rgb(1, 2, 3));
        var frame = SelectOnly(layer);

        var page = Render<CanvasSurface>(parameters => parameters.Add(component => component.Frame, frame));

        Assert.NotNull(page.Find("[data-testid='selection-box']"));
        Assert.Equal(8, page.FindAll(".board-handle").Count);
        Assert.NotNull(page.Find("[data-testid='handle-southeast']"));
    }

    [Fact]
    public async Task InputFromThePointerSourceReachesThePage()
    {
        var received = new List<CanvasInput>();
        Render<CanvasSurface>(parameters => parameters
            .Add(component => component.Frame, new BoardEditor(new LocalCanvasSession(CanvasDocument.Load([]))).Frame())
            .Add(component => component.OnInput, (CanvasInput input) => received.Add(input)));

        await _pointer.Emit(new WheelInput(new Point(1, 2), 0, 3, Zoom: false));

        var wheel = Assert.IsType<WheelInput>(Assert.Single(received));
        Assert.Equal(3, wheel.DeltaY);
    }

    [Fact]
    public async Task DisposingTheSurfaceDisposesTheBrowserSource()
    {
        Render<CanvasSurface>(parameters => parameters.Add(component => component.Frame, SelectOnly(null)));

        await DisposeAsync();

        Assert.True(_pointer.Disposed);
    }

    private static CanvasFrame SelectOnly(Layer? layer)
    {
        var session = new LocalCanvasSession(CanvasDocument.Load(layer is null ? [] : [layer]));
        var editor = new BoardEditor(session);
        if (layer is not null)
        {
            editor.Apply(new PointerInput(PointerPhase.Down, new Point(5, 5), Primary: true, Additive: false));
            editor.Apply(new PointerInput(PointerPhase.Up, new Point(5, 5), Primary: true, Additive: false));
        }

        return editor.Frame();
    }
}
