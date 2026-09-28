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
        private const uint WM_NCHITTEST = 0x0084;
        private const uint WM_SETCURSOR = 0x0020;
        private const uint WM_MOUSELEAVE = 0x02A3;
        private const uint TME_LEAVE = 0x00000002;
        private const int HTCLIENT = 1;

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

            /// <summary>True when the pointer left the window (WM_MOUSELEAVE); the position means nothing then.</summary>
            public bool Left { get; }

            public RawMouseEvent(double x, double y, RawMouseButton? buttonDown, RawMouseButton? buttonUp, bool left = false)
            {
                X = x;
                Y = y;
                ButtonDown = buttonDown;
                ButtonUp = buttonUp;
                Left = left;
            }
        }

        private sealed class SubclassState
        {
            public required WndProcDelegate Proc { get; init; }
            public required IntPtr OriginalWndProc { get; init; }
            public Action<RawMouseEvent>? Callback { get; set; }
            public Func<IntPtr?>? CursorQuery { get; set; }
            public bool TrackingLeave { get; set; }
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

        // Mouse capture while a button is held, so a drag (camera look, stock window move) keeps reporting moves
        // after the pointer leaves the child window.
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetCapture(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ReleaseCapture();

        // The surface's cursor is the stock arrow, or none while the stock UI draws its hover pointer.
        [DllImport("user32.dll")]
        private static extern IntPtr SetCursor(IntPtr hCursor);

        [StructLayout(LayoutKind.Sequential)]
        private struct TRACKMOUSEEVENT
        {
            public uint cbSize;
            public uint dwFlags;
            public IntPtr hwndTrack;
            public uint dwHoverTime;
        }

        // Asks for WM_MOUSELEAVE, so the stock pointer stops drawing once the pointer leaves the surface.
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool TrackMouseEvent(ref TRACKMOUSEEVENT lpEventTrack);

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
        /// (whichever window is physically under the cursor). A "static" class window reports itself
        /// transparent to that hit test, so the messages went to Avalonia's native-host holder window
        /// beneath it, which swallows them: nothing over the viewport reached Avalonia's routed-event
        /// tree or this subclass (in-game, 2026-09-27: no click or hover arrived by either route). The
        /// subclass therefore claims the client area (WM_NCHITTEST = HTCLIENT), receives the mouse
        /// messages itself and exposes them through <see cref="SetRawMouseHandler"/>, capturing the
        /// mouse while a button is held. (Wheel messages are unaffected: Windows routes WM_MOUSEWHEEL
        /// by keyboard focus, not hit-test, so it already reaches the Avalonia-managed top-level window.)
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

        /// <summary>
        /// Registers the query that picks the system cursor over this child window's client area: a cursor handle,
        /// <see cref="IntPtr.Zero"/> to hide it (the stock UI draws its own pointer), or null for the class default.
        /// </summary>
        public static void SetCursorQuery(IntPtr hwnd, Func<IntPtr?>? cursorQuery)
        {
            if (hwnd != IntPtr.Zero && _subclasses.TryGetValue(hwnd, out var state))
            {
                state.CursorQuery = cursorQuery;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ICONINFO
        {
            [MarshalAs(UnmanagedType.Bool)] public bool fIcon;
            public int xHotspot;
            public int yHotspot;
            public IntPtr hbmMask;
            public IntPtr hbmColor;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPV5HEADER
        {
            public uint bV5Size;
            public int bV5Width;
            public int bV5Height;
            public ushort bV5Planes;
            public ushort bV5BitCount;
            public uint bV5Compression;
            public uint bV5SizeImage;
            public int bV5XPelsPerMeter;
            public int bV5YPelsPerMeter;
            public uint bV5ClrUsed;
            public uint bV5ClrImportant;
            public uint bV5RedMask;
            public uint bV5GreenMask;
            public uint bV5BlueMask;
            public uint bV5AlphaMask;
            public uint bV5CSType;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 36)] public byte[] bV5Endpoints;
            public uint bV5GammaRed;
            public uint bV5GammaGreen;
            public uint bV5GammaBlue;
            public uint bV5Intent;
            public uint bV5ProfileData;
            public uint bV5ProfileSize;
            public uint bV5Reserved;
        }

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPV5HEADER pbmi, uint usage, out IntPtr bits, IntPtr section, uint offset);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateBitmap(int width, int height, uint planes, uint bitsPerPixel, IntPtr bits);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteObject(IntPtr obj);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CreateIconIndirect(ref ICONINFO info);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DestroyCursor(IntPtr cursor);

        /// <summary>
        /// Creates a colour cursor with per-pixel alpha from straight-alpha RGBA pixels (top row first); destroy it
        /// with <see cref="DestroyCursor"/>. Returns <see cref="IntPtr.Zero"/> on failure.
        /// </summary>
        public static IntPtr CreateCursorFromRgba(byte[] rgba, int width, int height, int hotspotX, int hotspotY)
        {
            var header = new BITMAPV5HEADER
            {
                bV5Size = (uint)Marshal.SizeOf<BITMAPV5HEADER>(),
                bV5Width = width,
                bV5Height = -height, // top-down
                bV5Planes = 1,
                bV5BitCount = 32,
                bV5Compression = 3, // BI_BITFIELDS
                bV5RedMask = 0x00FF0000,
                bV5GreenMask = 0x0000FF00,
                bV5BlueMask = 0x000000FF,
                bV5AlphaMask = 0xFF000000,
                bV5Endpoints = new byte[36],
            };
            IntPtr color = CreateDIBSection(IntPtr.Zero, ref header, 0, out IntPtr bits, IntPtr.Zero, 0);
            if (color == IntPtr.Zero) return IntPtr.Zero;
            var bgra = new byte[width * height * 4];
            for (int i = 0; i < bgra.Length; i += 4)
            {
                bgra[i] = rgba[i + 2];
                bgra[i + 1] = rgba[i + 1];
                bgra[i + 2] = rgba[i];
                bgra[i + 3] = rgba[i + 3];
            }
            Marshal.Copy(bgra, 0, bits, bgra.Length);
            IntPtr mask = CreateBitmap(width, height, 1, 1, IntPtr.Zero);
            var info = new ICONINFO { fIcon = false, xHotspot = hotspotX, yHotspot = hotspotY, hbmMask = mask, hbmColor = color };
            IntPtr cursor = CreateIconIndirect(ref info);
            DeleteObject(color);
            DeleteObject(mask);
            return cursor;
        }

        private static void SubclassForRawMouseInput(IntPtr hwnd)
        {
            WndProcDelegate newProc = (h, msg, wParam, lParam) =>
            {
                if (_subclasses.TryGetValue(h, out var state))
                {
                    switch (msg)
                    {
                        case WM_NCHITTEST:
                            // A "static" class window answers HTTRANSPARENT, so Windows would route every mouse
                            // message past it to the window beneath (Avalonia's native-host holder, which swallows
                            // them): neither this subclass nor Avalonia's pointer events ever saw a click over the
                            // viewport. Claiming the client area makes the messages below arrive here.
                            return (IntPtr)HTCLIENT;
                        case WM_SETCURSOR:
                            if ((lParam.ToInt64() & 0xFFFF) == HTCLIENT && state.CursorQuery?.Invoke() is { } cursor)
                            {
                                SetCursor(cursor);
                                return (IntPtr)1;
                            }
                            break;
                        case WM_MOUSELEAVE:
                            state.TrackingLeave = false;
                            state.Callback?.Invoke(new RawMouseEvent(0, 0, null, null, left: true));
                            break;
                        case WM_MOUSEMOVE:
                            if (!state.TrackingLeave)
                            {
                                var track = new TRACKMOUSEEVENT { cbSize = (uint)Marshal.SizeOf<TRACKMOUSEEVENT>(), dwFlags = TME_LEAVE, hwndTrack = h };
                                state.TrackingLeave = TrackMouseEvent(ref track);
                            }
                            state.Callback?.Invoke(new RawMouseEvent(GetXLParam(lParam), GetYLParam(lParam), null, null));
                            break;
                        case WM_LBUTTONDOWN:
                            SetCapture(h);
                            state.Callback?.Invoke(new RawMouseEvent(GetXLParam(lParam), GetYLParam(lParam), RawMouseButton.Left, null));
                            break;
                        case WM_LBUTTONUP:
                            ReleaseCapture();
                            state.Callback?.Invoke(new RawMouseEvent(GetXLParam(lParam), GetYLParam(lParam), null, RawMouseButton.Left));
                            break;
                        case WM_RBUTTONDOWN:
                            SetCapture(h);
                            state.Callback?.Invoke(new RawMouseEvent(GetXLParam(lParam), GetYLParam(lParam), RawMouseButton.Right, null));
                            break;
                        case WM_RBUTTONUP:
                            ReleaseCapture();
                            state.Callback?.Invoke(new RawMouseEvent(GetXLParam(lParam), GetYLParam(lParam), null, RawMouseButton.Right));
                            break;
                        case WM_MBUTTONDOWN:
                            SetCapture(h);
                            state.Callback?.Invoke(new RawMouseEvent(GetXLParam(lParam), GetYLParam(lParam), RawMouseButton.Middle, null));
                            break;
                        case WM_MBUTTONUP:
                            ReleaseCapture();
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
