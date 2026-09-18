// src/Gordian.App/Graphics/Win32ChildWindowHelper.cs
using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// Helper for creating and managing isolated Win32 child windows for native viewport embedding in Avalonia.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal static class Win32ChildWindowHelper
    {
        private const int WS_CHILD = 0x40000000;
        private const int WS_VISIBLE = 0x10000000;
        private const int WS_CLIPSIBLINGS = 0x04000000;
        private const int WS_CLIPCHILDREN = 0x02000000;

        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateWindowExW(
            int dwExStyle,
            string lpClassName,
            string lpWindowName,
            int dwStyle,
            int x,
            int y,
            int nWidth,
            int nHeight,
            IntPtr hWndParent,
            IntPtr hMenu,
            IntPtr hInstance,
            IntPtr lpParam);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(
            IntPtr hWnd,
            IntPtr hWndInsertAfter,
            int X,
            int Y,
            int cx,
            int cy,
            uint uFlags);

        public static IntPtr CreateChildWindow(IntPtr parentHwnd, int width, int height)
        {
            if (parentHwnd == IntPtr.Zero)
            {
                throw new ArgumentException("Parent HWND must not be zero.", nameof(parentHwnd));
            }

            int style = WS_CHILD | WS_VISIBLE | WS_CLIPSIBLINGS | WS_CLIPCHILDREN;
            IntPtr hwnd = CreateWindowExW(
                0,
                "static",
                "GordianXI_Viewport",
                style,
                0,
                0,
                Math.Max(1, width),
                Math.Max(1, height),
                parentHwnd,
                IntPtr.Zero,
                IntPtr.Zero,
                IntPtr.Zero);

            if (hwnd == IntPtr.Zero)
            {
                int error = Marshal.GetLastWin32Error();
                throw new InvalidOperationException($"Failed to create Win32 child viewport window. Win32 Error: {error}");
            }

            return hwnd;
        }

        public static void ResizeChildWindow(IntPtr hwnd, int width, int height)
        {
            if (hwnd != IntPtr.Zero)
            {
                SetWindowPos(hwnd, IntPtr.Zero, 0, 0, Math.Max(1, width), Math.Max(1, height), SWP_NOZORDER | SWP_NOACTIVATE);
            }
        }

        public static void DestroyChildWindow(IntPtr hwnd)
        {
            if (hwnd != IntPtr.Zero)
            {
                DestroyWindow(hwnd);
            }
        }
    }
}
