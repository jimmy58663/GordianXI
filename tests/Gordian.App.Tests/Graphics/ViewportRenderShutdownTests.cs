using System.Reflection;
using Avalonia.Platform;
using Gordian.App.Graphics;

namespace Gordian.App.Tests.Graphics;

public class ViewportRenderShutdownTests
{
    [Fact]
    public async Task DestroyReturnsWhileWorkerRetainsResources()
    {
        var control = new VeldridViewportControl();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource();
        var type = typeof(VeldridViewportControl);
        var child = new PlatformHandle(IntPtr.Zero, "test");
        type.GetField("_activeNativeControl", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(control, child);
        type.GetField("_renderLoopCts", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(control, cts);
        type.GetField("_renderTask", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(control, release.Task);
        var destroy = Task.Run(() => type.GetMethod("DestroyNativeControlCore", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(control, [child]), TestContext.Current.CancellationToken);
        try
        {
            Assert.Same(destroy, await Task.WhenAny(destroy, Task.Delay(1000, TestContext.Current.CancellationToken)));
            await destroy;
            Assert.True(cts.IsCancellationRequested);
            Assert.Same(release.Task, type.GetField("_renderTask", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(control));
        }
        finally
        {
            release.TrySetResult();
            try { await destroy.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken); }
            catch (TargetInvocationException) { /* Old synchronous teardown also attempts UI writes on this test thread. */ }
        }
    }
}
