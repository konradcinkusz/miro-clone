using Microsoft.AspNetCore.Components;
using MiroClone.Web.Canvas.Interaction;
using MiroClone.Web.Canvas.Interop;

namespace MiroClone.Web.Tests.TestSupport;

/// <summary>Stands in for the browser's pointer module: a test raises the input the surface would have received.</summary>
internal sealed class FakeCanvasPointer : ICanvasPointerSource
{
    private Func<CanvasInput, Task>? _onInput;

    public int AttachCount { get; private set; }

    public bool Disposed { get; private set; }

    public Task AttachAsync(ElementReference surface, Func<CanvasInput, Task> onInput, CancellationToken ct)
    {
        AttachCount++;
        _onInput = onInput;
        return Task.CompletedTask;
    }

    public Task Emit(CanvasInput input) =>
        _onInput is { } onInput ? onInput(input) : throw new InvalidOperationException("The pointer source is not attached.");

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}
