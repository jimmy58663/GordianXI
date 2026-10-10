// src/Gordian.App/Audio/SoundFocusTracker.cs
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace Gordian.App.Audio
{
    /// <summary>
    /// Tells <see cref="SoundControls"/> whether a GordianXI window, and a viewport window, has focus (#265, "play only
    /// while the window is active"). Uses Avalonia's own window activation, so it works the same on every platform.
    /// Recomputed after the activation events settle, so moving focus between two GordianXI windows never reads as
    /// "inactive" in between.
    /// </summary>
    public static class SoundFocusTracker
    {
        private static bool _installed;
        private static bool _pending;

        /// <summary>Starts tracking every window of the desktop lifetime (call once, on the UI thread).</summary>
        public static void Install(IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (_installed)
            {
                return;
            }

            _installed = true;
            WindowBase.IsActiveProperty.Changed.AddClassHandler<Window>((_, _) => Schedule(desktop));
            Schedule(desktop);
        }

        private static void Schedule(IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (_pending)
            {
                return;
            }

            _pending = true;
            Dispatcher.UIThread.Post(() =>
            {
                _pending = false;
                bool any = desktop.Windows.Any(w => w.IsActive);
                bool viewport = desktop.Windows.Any(w => w.IsActive && w is ViewportWindow);
                SoundControls.SetFocus(any, viewport);
            }, DispatcherPriority.Background);
        }
    }
}
