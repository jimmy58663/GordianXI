using System;
using Avalonia.Platform;
using NeoVeldrid;

namespace Gordian.App.Graphics;

internal static class NativeViewportSource
{
    internal static SwapchainSource CreateXlib(IPlatformHandle child, IntPtr display)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (child.HandleDescriptor != "XID")
            throw new PlatformNotSupportedException(
                $"Linux Vulkan viewport requires an X11 XID; received '{child.HandleDescriptor}'. Native Wayland is not implemented.");
        if (child.Handle == IntPtr.Zero)
            throw new ArgumentException("The X11 child window is null.", nameof(child));
        if (display == IntPtr.Zero)
            throw new ArgumentException("The X11 display connection is null.", nameof(display));
        return SwapchainSource.CreateXlib(display, child.Handle);
    }
}
