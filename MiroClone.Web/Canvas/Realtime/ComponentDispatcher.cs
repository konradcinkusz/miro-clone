namespace MiroClone.Web.Canvas.Realtime;

/// <summary>
/// A synchronization context whose work runs through a component's <c>InvokeAsync</c>, so a live session changes the page the
/// way the page's own events do (blazor-web-app §6). The page creates one and hands it to the session it opens.
/// </summary>
public sealed class ComponentDispatcher(Func<Action, Task> invokeAsync) : SynchronizationContext
{
    private readonly Func<Action, Task> _invokeAsync = invokeAsync ?? throw new ArgumentNullException(nameof(invokeAsync));

    public override void Post(SendOrPostCallback d, object? state) => _ = _invokeAsync(() => d(state));
}
