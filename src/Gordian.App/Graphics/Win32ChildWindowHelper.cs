// src/Gordian.App/Graphics/Win32ChildWindowHelper.cs
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// Identifies which mouse button a raw Win32 mouse message refers to.
    /// </summary>
    internal enum RawMouseButton
    {
        Left,
        Right,
        Middle
    }

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

        private const int GWLP_WNDPROC = -4;

        private const uint WM_MOUSEMOVE = 0x0200;
        private const uint WM_LBUTTONDOWN = 0x0201;
        private const uint WM_LBUTTONUP = 0x0202;
        private const uint WM_RBUTTONDOWN = 0x0204;
        private const uint WM_RBUTTONUP = 0x0205;
        private const uint WM_MBUTTONDOWN = 0x0207;
        private const uint WM_MBUTTONUP = 0x0208;

        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        /// <summary>
        /// Raised for raw mouse move/button messages received by a subclassed child window,
        /// bypassing Avalonia's routed-event tree entirely (see remarks on <see cref="CreateChildWindow"/>).
        /// </summary>
        public readonly struct RawMouseEvent
        {
            public double X { get; }
            public double Y { get; }
            public RawMouseButton? ButtonDown { get; }
            public RawMouseButton? ButtonUp { get; }

            public RawMouseEvent(double x, double y, RawMouseButton? buttonDown, RawMouseButton? buttonUp)
            {
                X = x;
                Y = y;
                ButtonDown = buttonDown;
                ButtonUp = buttonUp;
            }
        }

        private sealed class SubclassState
        {
            public required WndProcDelegate Proc { get; init; }
            public required IntPtr OriginalWndProc { get; init; }
            public Action<RawMouseEvent>? Callback { get; set; }
        }

        // Keeps each child window's replacement WndProc delegate (so the GC never collects it out
        // from under the unmanaged callback pointer we handed to Windows), its original WndProc
        // (so non-mouse messages keep working normally), and the managed callback to invoke.
        private static readonly Dictionary<IntPtr, SubclassState> _subclasses = new();

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

        [DllImport("user32.dll", SetLastError = true, EntryPoint = "SetWindowLongPtrW")]
        private static extern IntPtr SetWindowLongPtrW(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", SetLastError = true, EntryPoint = "CallWindowProcW")]
        private static extern IntPtr CallWindowProcW(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        /// <summary>
        /// Creates the native child window used purely as a Direct3D/Vulkan presentation surface,
        /// and subclasses it so its raw mouse messages are exposed via <see cref="SetRawMouseHandler"/>.
        /// </summary>
        /// <remarks>
        /// This window has no input handling of its own - it exists only to give the graphics API
        /// a real HWND to render into. Windows routes mouse button/move messages by hit-testing
        /// (whichever window is physically under the cursor), so without this subclass those
        /// messages would go directly to this child window and never reach Avalonia's routed-event
        /// tree at all, silently bypassing every InputElement pointer handler on the control or its
        /// ancestors. (Wheel messages are unaffected: Windows routes WM_MOUSEWHEEL by keyboard
        /// focus, not hit-test, so it already reaches the Avalonia-managed top-level window.)
        /// </remarks>
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

            SubclassForRawMouseInput(hwnd);

            return hwnd;
        }

        /// <summary>
        /// Registers the callback that receives this child window's raw mouse move/button events.
        /// Pass null to unregister (done automatically by <see cref="DestroyChildWindow"/>).
        /// </summary>
        public static void SetRawMouseHandler(IntPtr hwnd, Action<RawMouseEvent>? callback)
        {
            if (hwnd != IntPtr.Zero && _subclasses.TryGetValue(hwnd, out var state))
            {
                state.Callback = callback;
            }
        }

        private static void SubclassForRawMouseInput(IntPtr hwnd)
        {
            WndProcDelegate newProc = (h, msg, wParam, lParam) =>
            {
                if (_subclasses.TryGetValue(h, out var state))
                {
                    switch (msg)
                    {
                        case WM_MOUSEMOVE:
                            state.Callback?.Invoke(new RawMouseEvent(GetXLParam(lParam), GetYLParam(lParam), null, null));
                            break;
                        case WM_LBUTTONDOWN:
                            state.Callback?.Invoke(new RawMouseEvent(GetXLParam(lParam), GetYLParam(lParam), RawMouseButton.Left, null));
                            break;
                        case WM_LBUTTONUP:
                            state.Callback?.Invoke(new RawMouseEvent(GetXLParam(lParam), GetYLParam(lParam), null, RawMouseButton.Left));
                            break;
                        case WM_RBUTTONDOWN:
                            state.Callback?.Invoke(new RawMouseEvent(GetXLParam(lParam), GetYLParam(lParam), RawMouseButton.Right, null));
                            break;
                        case WM_RBUTTONUP:
                            state.Callback?.Invoke(new RawMouseEvent(GetXLParam(lParam), GetYLParam(lParam), null, RawMouseButton.Right));
                            break;
                        case WM_MBUTTONDOWN:
                            state.Callback?.Invoke(new RawMouseEvent(GetXLParam(lParam), GetYLParam(lParam), RawMouseButton.Middle, null));
                            break;
                        case WM_MBUTTONUP:
                            state.Callback?.Invoke(new RawMouseEvent(GetXLParam(lParam), GetYLParam(lParam), null, RawMouseButton.Middle));
                            break;
                    }

                    return CallWindowProcW(state.OriginalWndProc, h, msg, wParam, lParam);
                }

                return IntPtr.Zero;
            };

            IntPtr newProcPtr = Marshal.GetFunctionPointerForDelegate(newProc);
            IntPtr originalProc = SetWindowLongPtrW(hwnd, GWLP_WNDPROC, newProcPtr);
            _subclasses[hwnd] = new SubclassState { Proc = newProc, OriginalWndProc = originalProc };
        }

        private static int GetXLParam(IntPtr lParam) => unchecked((short)(lParam.ToInt64() & 0xFFFF));
        private static int GetYLParam(IntPtr lParam) => unchecked((short)((lParam.ToInt64() >> 16) & 0xFFFF));

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
                _subclasses.Remove(hwnd);
                DestroyWindow(hwnd);
            }
        }
    }
}
