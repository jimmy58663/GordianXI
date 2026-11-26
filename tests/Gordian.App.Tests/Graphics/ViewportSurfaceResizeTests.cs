using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Gordian.App.Graphics;
using NeoVeldrid;

namespace Gordian.App.Tests.Graphics;

public class ViewportSurfaceResizeTests
{
    [Fact]
    public void DelayedNativeExtentRecoversWithoutAnotherSizeNotification()
    {
        var (control, swapchain, tick) = Fixture();
        Request(control, 1779, 988);
        tick(0);
        Assert.Equal(1u, swapchain.Framebuffer.Width);
        Assert.Equal(1, swapchain.Resizes);
        // The backend returns success while native geometry remains 1x1. Do not
        // synthesize another managed SizeChanged or rely on Present/out-of-date.
        for (int i = 1; i <= 1000; i++) tick(i * Stopwatch.Frequency / 1000);
        Assert.InRange(swapchain.Resizes, 1, 11);
        swapchain.NativeReady = true;
        tick(2 * Stopwatch.Frequency);
        Assert.Equal(1779u, swapchain.Framebuffer.Width);
        Assert.Equal(988u, swapchain.Framebuffer.Height);
        int settled = swapchain.Resizes;
        for (int i = 3; i < 103; i++) tick(i * Stopwatch.Frequency);
        Assert.Equal(settled, swapchain.Resizes);
    }

    [Fact]
    public void NewRequestBypassesRetryIntervalAndCoalesces()
    {
        var (control, swapchain, tick) = Fixture();
        Request(control, 800, 600); tick(0);
        swapchain.NativeReady = true;
        Request(control, 900, 650); Request(control, 950, 700);
        tick(1);
        Assert.Equal(950u, swapchain.Framebuffer.Width);
        Assert.Equal(700u, swapchain.Framebuffer.Height);
        Assert.Equal(2, swapchain.Resizes);
    }

    [Fact]
    public void SettledSurfaceChecksAllocateNothing()
    {
        var (control, swapchain, tick) = Fixture();
        swapchain.NativeReady = true;
        Request(control, 800, 600); tick(0);
        for (int i = 1; i < 10000; i++) tick(i * Stopwatch.Frequency);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 10000; i < 20000; i++) tick(i * Stopwatch.Frequency);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal(1, swapchain.Resizes);
    }

    private static (VeldridViewportControl, ClampedSwapchain, Action<long>) Fixture()
    {
        var control = new VeldridViewportControl();
        var manager = new VeldridDeviceManager();
        var swapchain = new ClampedSwapchain();
        // CPU-only identity sentinel: no device/native function is ever invoked.
        // Framebuffer constructors are internal, so use an uninitialized ordinary
        // framebuffer's scalar base properties. These objects own no native handles.
        var deviceType = typeof(GraphicsDevice).Assembly.GetType("NeoVeldrid.Vk.VkGraphicsDevice")!;
        typeof(VeldridDeviceManager).GetProperty("Device")!.SetValue(manager, RuntimeHelpers.GetUninitializedObject(deviceType));
        Set(manager, "_ownSwapchain", swapchain);
        Set(control, "_deviceManager", manager);
        var method = typeof(VeldridViewportControl).GetMethod("ApplyPendingSurfaceSize", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (control, swapchain, method.CreateDelegate<Action<long>>(control));
    }

    private static void Request(VeldridViewportControl control, uint width, uint height) =>
        Set(control, "_pendingSurfaceSize", (long)(((ulong)width << 32) | height));
    private static void Set(object target, string name, object value) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);

    private sealed class ClampedSwapchain : Swapchain
    {
        public bool NativeReady;
        public int Resizes;
        public override Framebuffer Framebuffer { get; } = (Framebuffer)RuntimeHelpers.GetUninitializedObject(
            typeof(Framebuffer).Assembly.GetType("NeoVeldrid.Vk.VkFramebuffer")!);
        public override void Resize(uint width, uint height)
        {
            Resizes++;
            typeof(Framebuffer).GetField("<Width>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(Framebuffer, NativeReady ? width : 1u);
            typeof(Framebuffer).GetField("<Height>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(Framebuffer, NativeReady ? height : 1u);
        }
        public override bool SyncToVerticalBlank { get; set; }
        public override string Name { get; set; } = "delayed-native-fixture";
        public override bool IsDisposed => false;
        public override void Dispose() => throw new InvalidOperationException("CPU sentinel owns no native resource");
    }
}
