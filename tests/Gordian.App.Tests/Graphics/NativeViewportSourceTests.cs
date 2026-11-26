using Avalonia.Platform;
using Gordian.App.Graphics;

namespace Gordian.App.Tests.Graphics;

public class NativeViewportSourceTests
{
    [Theory]
    [InlineData("HWND")]
    [InlineData("Wayland")]
    [InlineData("NSView")]
    [InlineData("unknown")]
    public void RejectsForeignHandle(string descriptor) =>
        Assert.Throws<PlatformNotSupportedException>(() => NativeViewportSource.CreateXlib(new PlatformHandle(1, descriptor), 2));

    [Fact]
    public void RejectsMissingWindow() =>
        Assert.Throws<ArgumentException>(() => NativeViewportSource.CreateXlib(new PlatformHandle(0, "XID"), 2));

    [Fact]
    public void RejectsMissingDisplay() =>
        Assert.Throws<ArgumentException>(() => NativeViewportSource.CreateXlib(new PlatformHandle(1, "XID"), 0));

    [Fact]
    public void RejectsNullHandle() => Assert.Throws<ArgumentNullException>(() => NativeViewportSource.CreateXlib(null!, 2));

    [Fact]
    public void CreatesDescriptionWithoutNativeCalls() =>
        Assert.NotNull(NativeViewportSource.CreateXlib(new PlatformHandle(1, "XID"), 2));
}
