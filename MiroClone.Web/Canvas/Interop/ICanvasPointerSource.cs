using MiroClone.Web.Canvas.Interaction;
using Microsoft.AspNetCore.Components;

namespace MiroClone.Web.Canvas.Interop;

/// <summary>
/// Delivers the board surface's pointer and wheel input to the circuit. The browser implementation is <see cref="JsCanvasPointerSource"/>;
/// tests substitute a fake, so the page renders without a browser.
/// </summary>
public interface ICanvasPointerSource : IAsyncDisposable
{
    /// <summary>Starts delivering input from <paramref name="surface"/>. Calling it again is an error.</summary>
    Task AttachAsync(ElementReference surface, Func<CanvasInput, Task> onInput, CancellationToken ct);
}
