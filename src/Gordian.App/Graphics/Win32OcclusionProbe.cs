// src/Gordian.App/Graphics/Win32OcclusionProbe.cs
using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// Whether a viewport's native surface is hidden behind other windows (#301), so a background viewport window that
    /// cannot be seen stops drawing. With desktop composition every window draws whether covered or not, so this walks the
    /// top-level windows above the viewport's in z-order and subtracts their frames from the surface's rectangle. It is
    /// conservative: layered (possibly see-through) and cloaked windows, and windows on other virtual desktops, never count
    /// as covering, so an overlay never pauses a window that can be seen.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal static class Win32OcclusionProbe
    {
        private const uint GA_ROOT = 2;
        private const uint GW_HWNDPREV = 3;
        private const int GWL_EXSTYLE = -20;
        private const long WS_EX_LAYERED = 0x00080000;
        private const long WS_EX_TRANSPARENT = 0x00000020;
        private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
        private const int DWMWA_CLOAKED = 14;
        private const int RGN_DIFF = 4;
        private const int NULLREGION = 1;
        private const int MaxWindowsAbove = 512;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hwnd, uint command);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr hwnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsIconic(IntPtr hwnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out RECT value, int size);

        [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
        private static extern int DwmGetWindowAttributeInt(IntPtr hwnd, int attribute, out int value, int size);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);

        [DllImport("gdi32.dll")]
        private static extern int CombineRgn(IntPtr destination, IntPtr source1, IntPtr source2, int mode);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteObject(IntPtr handle);

        /// <summary>
        /// True when the surface cannot be seen: its window is minimised or hidden, or the windows above it cover all of it.
        /// Safe from any thread; false whenever unsure.
        /// </summary>
        public static bool IsFullyCovered(IntPtr surface)
        {
            if (surface == IntPtr.Zero) return false;
            try
            {
                IntPtr root = GetAncestor(surface, GA_ROOT);
                if (root == IntPtr.Zero) return false;
                if (IsIconic(root) || !IsWindowVisible(root)) return true;
                if (!GetWindowRect(surface, out var own) || own.Right <= own.Left || own.Bottom <= own.Top) return false;

                IntPtr visible = CreateRectRgn(own.Left, own.Top, own.Right, own.Bottom);
                if (visible == IntPtr.Zero) return false;
                try
                {
                    int checkedWindows = 0;
                    for (IntPtr above = GetWindow(root, GW_HWNDPREV); above != IntPtr.Zero && checkedWindows < MaxWindowsAbove;
                         above = GetWindow(above, GW_HWNDPREV), checkedWindows++)
                    {
                        if (!Covers(above, out var bounds)) continue;
                        IntPtr cover = CreateRectRgn(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
                        if (cover == IntPtr.Zero) continue;
                        int result = CombineRgn(visible, visible, cover, RGN_DIFF);
                        DeleteObject(cover);
                        if (result == NULLREGION) return true;
                    }
                    return false;
                }
                finally
                {
                    DeleteObject(visible);
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Whether a window above the viewport hides what is behind it, and the frame it hides.</summary>
        private static bool Covers(IntPtr hwnd, out RECT bounds)
        {
            bounds = default;
            if (!IsWindowVisible(hwnd) || IsIconic(hwnd)) return false;
            long exStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
            if ((exStyle & (WS_EX_LAYERED | WS_EX_TRANSPARENT)) != 0) return false;
            if (DwmGetWindowAttributeInt(hwnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return false;
            // The visible frame, without the drop shadow GetWindowRect includes.
            if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out bounds, Marshal.SizeOf<RECT>()) != 0 &&
                !GetWindowRect(hwnd, out bounds))
            {
                return false;
            }
            return bounds.Right > bounds.Left && bounds.Bottom > bounds.Top;
        }
    }
}
