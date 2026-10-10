// src/Gordian.App/Services/WindowFocusHook.cs
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Gordian.App.Audio;

namespace Gordian.App.Services
{
    /// <summary>
    /// Feeds Avalonia's window activation into the app's <see cref="WindowFocusTracker"/> and publishes the tracker's
    /// flags to the sound controls (#265 "play only while active", #304). Uses Avalonia's own activation, so it works the
    /// same on every platform. The flags are recomputed after the activation events settle, so moving the focus between
    /// two GordianXI windows never reads as "inactive" in between.
    /// </summary>
    public static class WindowFocusHook
    {
        private static bool _installed;
        private static bool _pending;

        /// <summary>Starts tracking every window of the desktop lifetime (call once, on the UI thread).</summary>
        public static void Install(IClassicDesktopStyleApplicationLifetime desktop, WindowFocusTracker tracker)
        {
            if (_installed)
            {
                return;
            }

            _installed = true;
            WindowBase.IsActiveProperty.Changed.AddClassHandler<Window>((_, _) => Schedule(desktop, tracker));
            tracker.Changed += (_, _) => Schedule(desktop, tracker);
            Schedule(desktop, tracker);
        }

        private static void Schedule(IClassicDesktopStyleApplicationLifetime desktop, WindowFocusTracker tracker)
        {
            if (_pending)
            {
                return;
            }

            _pending = true;
            Dispatcher.UIThread.Post(() =>
            {
                _pending = false;
                tracker.SetAnyWindowActive(desktop.Windows.Any(w => w.IsActive));
                SoundControls.SetFocus(tracker.IsAppFocused, tracker.IsViewportFocused);
            }, DispatcherPriority.Background);
        }
    }
}
