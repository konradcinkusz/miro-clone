using MiroClone.Web.Canvas.Realtime;
using Xunit;

namespace MiroClone.Web.Tests.Canvas.Realtime;

public sealed class ComponentDispatcherTests
{
    [Fact]
    public void PostedWorkRunsThroughTheComponentsInvokeAsync()
    {
        var invoked = new List<Action>();
        var dispatcher = new ComponentDispatcher(work =>
        {
            invoked.Add(work);
            return Task.CompletedTask;
        });
        var ran = 0;

        dispatcher.Post(_ => ran++, null);

        // The work is handed to InvokeAsync, and runs there, not on the thread that posted it.
        Assert.Single(invoked);
        Assert.Equal(0, ran);
        invoked[0]();
        Assert.Equal(1, ran);
    }
}
