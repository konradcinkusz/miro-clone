using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MiroClone.Web.Canvas.Core;
using MiroClone.Web.Canvas.Interaction;

namespace MiroClone.Web.Canvas.Interop;

/// <summary>
/// The browser side of <see cref="ICanvasPointerSource"/>. It loads <c>wwwroot/js/canvas-pointer.js</c>, the only script in the app
/// (ADR 0027), and receives its messages on the circuit. The JavaScript keeps no state: it reports phases, coordinates and buttons.
/// </summary>
public sealed class JsCanvasPointerSource(IJSRuntime js) : ICanvasPointerSource
{
    private const string ModulePath = "./js/canvas-pointer.js";

    private IJSObjectReference? _module;
    private IJSObjectReference? _attachment;
    private DotNetObjectReference<JsCanvasPointerSource>? _self;
    private Func<CanvasInput, Task>? _onInput;

    public async Task AttachAsync(ElementReference surface, Func<CanvasInput, Task> onInput, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(onInput);
        if (_attachment is not null)
        {
            throw new InvalidOperationException("The pointer source is already attached.");
        }

        _onInput = onInput;
        _module = await js.InvokeAsync<IJSObjectReference>("import", ct, ModulePath);
        _self = DotNetObjectReference.Create(this);
        _attachment = await _module.InvokeAsync<IJSObjectReference>("attach", ct, surface, _self);
    }

    [JSInvokable]
    public Task OnPointer(string phase, double x, double y, int button, bool shift) =>
        Deliver(new PointerInput(ParsePhase(phase), new Point(x, y), Primary: button == 0, Additive: shift));

    [JSInvokable]
    public Task OnWheel(double x, double y, double deltaX, double deltaY, bool zoom) =>
        Deliver(new WheelInput(new Point(x, y), deltaX, deltaY, zoom));

    public async ValueTask DisposeAsync()
    {
        // Cleared first, so a second call (the page and the surface both dispose it) does nothing.
        var attachment = _attachment;
        var module = _module;
        var self = _self;
        _attachment = null;
        _module = null;
        _self = null;
        _onInput = null;

        if (attachment is not null)
        {
            await attachment.InvokeVoidAsync("dispose");
            await attachment.DisposeAsync();
        }

        if (module is not null)
        {
            await module.DisposeAsync();
        }

        self?.Dispose();
    }

    private Task Deliver(CanvasInput input) => _onInput is { } onInput ? onInput(input) : Task.CompletedTask;

    private static PointerPhase ParsePhase(string phase) => phase switch
    {
        "down" => PointerPhase.Down,
        "move" => PointerPhase.Move,
        "up" => PointerPhase.Up,
        "cancel" => PointerPhase.Cancel,
        _ => throw new ArgumentOutOfRangeException(nameof(phase), phase, "The surface sent an unknown pointer phase."),
    };
}
