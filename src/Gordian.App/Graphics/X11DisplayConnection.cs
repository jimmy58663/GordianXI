using System;
using System.Runtime.InteropServices;
using Avalonia.Platform;

namespace Gordian.App.Graphics;

/// <summary>
/// An owned connection to Avalonia's default X server. Avalonia 12.1.5 initializes Xlib threads and opens
/// the default DISPLAY before constructing native hosts; it exposes XIDs, not a public Display pointer.
/// DISPLAY must remain unchanged during application lifetime. Never close Avalonia's connection.
/// </summary>
internal sealed class X11DisplayConnection : IDisposable
{
    public IntPtr Handle { get; private set; }

    public X11DisplayConnection(IPlatformHandle parent, IPlatformHandle child)
    {
        // Validate before any Xlib call, including on unsupported native Wayland hosts.
        NativeViewportSource.CreateXlib(parent, new IntPtr(1));
        NativeViewportSource.CreateXlib(child, new IntPtr(1));
        Handle = XOpenDisplay(IntPtr.Zero);
        if (Handle == IntPtr.Zero)
            throw new InvalidOperationException("Cannot open the Avalonia X11 DISPLAY for the Vulkan viewport.");
        try
        {
            // The pinned base host synchronizes child creation. Confirm both XIDs are on this server's
            // same root before passing either to Vulkan. No event selection or window ownership here.
            if (GetRoot(parent.Handle) != GetRoot(child.Handle))
                throw new InvalidOperationException("The X11 viewport and parent are on different roots.");
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private IntPtr GetRoot(IntPtr window)
    {
        int result = XQueryTree(Handle, window, out var root, out _, out var children, out _);
        if (children != IntPtr.Zero) XFree(children);
        if (result == 0 || root == IntPtr.Zero)
            throw new InvalidOperationException("The Avalonia X11 window is not available on DISPLAY.");
        return root;
    }

    // Caller stops the render worker and disposes its Vulkan surface before closing this connection.
    public void Dispose()
    {
        if (Handle == IntPtr.Zero) return;
        XCloseDisplay(Handle);
        Handle = IntPtr.Zero;
    }

    [DllImport("libX11.so.6")] private static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport("libX11.so.6")] private static extern int XCloseDisplay(IntPtr display);
    [DllImport("libX11.so.6")] private static extern int XQueryTree(IntPtr display, IntPtr window,
        out IntPtr root, out IntPtr parent, out IntPtr children, out uint count);
    [DllImport("libX11.so.6")] private static extern int XFree(IntPtr data);
}
