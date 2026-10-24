// src/Gordian.Core/Ui/ZoneLoadingScreen.cs
using System;
using Gordian.Core.Network;

namespace Gordian.Engine.Ui
{
    /// <summary>
    /// The loading state of a session's view (#36): the screen goes black while the character is zoning in (after
    /// character select) or crossing to another zone, and comes back once the map server has the character in the world
    /// and the zone's geometry is on screen. Retail has no loading screen art: the legacy client fades the scene to black
    /// while it zones and shows only a small "Downloading data ..." sprite in the bottom-right corner
    /// (<see cref="ZoneLoadingIndicator"/>, #350).
    /// <para>
    /// <see cref="Update"/> returns the black overlay's opacity (0 = clear, 1 = black), easing toward it over
    /// <see cref="FadeOutSeconds"/> / <see cref="FadeInSeconds"/> (measured on a retail recording).
    /// </para>
    /// </summary>
    public sealed class ZoneLoadingScreen
    {
        /// <summary>
        /// Seconds to fade to black when loading starts, and back when it ends: both linear and about one second on the
        /// maintainer's retail recording of two zone lines (2026-10-11, 30 fps, Southern San d'Oria to East Ronfaure and
        /// back: the scene darkened over 28 and 30 frames and came back over 29). The one place these timings live: the
        /// lobby's fade after a character is chosen uses <see cref="FadeOutSeconds"/> too (not measured there).
        /// </summary>
        public const double FadeOutSeconds = 1.0, FadeInSeconds = 1.0;

        /// <summary>
        /// How long the screen stays black waiting for a zone's geometry once the session is in the world; after that the
        /// scene shows anyway, so a zone that cannot be loaded (missing DATs) does not leave the screen black.
        /// </summary>
        public const double GeometryWaitSeconds = 20;

        private float _opacity;
        private double _lastSeconds = double.NaN;
        private ushort _waitingZone;
        private double _waitingSince;

        /// <summary>The zone whose load finished (geometry and the player's model on screen, or the wait ran out): no
        /// later change in it (a geometry reload, a look change) blacks the screen again until the zone changes.</summary>
        private ushort _settledZone;

        /// <summary>The current overlay opacity.</summary>
        public float Opacity => _opacity;

        /// <summary>Whether the last update counted the view as loading.</summary>
        public bool IsLoading { get; private set; }

        /// <summary>
        /// Whether the "Downloading data ..." sprite shows (<see cref="ZoneLoadingIndicator"/>): as soon as the screen is fully
        /// black while loading. Retail waited longer (its black stayed empty until the old zone was torn down: 2.2 s and 5.3 s
        /// on the 2026-10-11 recording); the maintainer asked not to imitate retail's slower loading (round 2, 2026-10-12).
        /// </summary>
        public bool IndicatorVisible => IsLoading && _opacity >= 1f;

        /// <summary>Restarts the fade: black at once (a session that is still connecting) or clear.</summary>
        public void Reset(bool black)
        {
            _opacity = black ? 1f : 0f;
            _lastSeconds = double.NaN;
            _waitingZone = 0;
            _settledZone = 0;
            _fadingOut = false;
            _blackSince = double.NaN;
            _eventWaitZone = 0;
            _eventSettledZone = 0;
            IsLoading = black;
        }

        /// <summary>
        /// Shortest time the screen stays fully black before it fades in (a zone already in the GPU cache can be ready
        /// before the fade-out ends): long enough to draw a frame or two of the new zone, the settled player and the
        /// snapped camera under the black (round 3 of in-game testing saw them change while the fade was still running).
        /// </summary>
        public const double MinimumBlackSeconds = 0.2;

        /// <summary>
        /// Longest the screen stays black after an event's scene zone is on screen, waiting for the event to place its camera
        /// (<c>eventCameraPending</c>): a scene that sets no camera still shows (a safety limit).
        /// </summary>
        public const double EventCameraWaitSeconds = 3.0;

        private ushort _eventWaitZone, _eventSettledZone;
        private double _eventWaitSince;

        private bool _fadingOut;
        private double _blackSince = double.NaN;

        /// <summary>
        /// Whether the view is fully black (a new zone may be swapped in now, unseen). A zone swap waits for this (#350
        /// round 3): the viewport keeps drawing the old zone while the screen fades out and shows the new one only under
        /// full black.
        /// </summary>
        public bool IsFullyBlack => _opacity >= 1f;

        /// <summary>
        /// Whether a session's view is loading: the session is connecting to or zoning into a map server (not yet in
        /// the world), or it is in the world but its zone's geometry or the player's own model
        /// (<paramref name="playerReady"/>) is not on screen yet (for at most <see cref="GeometryWaitSeconds"/>), so the
        /// player never appears as the placeholder model while the look is still on its way (round-2 in-game test). Once
        /// a zone has finished loading it stays loaded until the zone changes. A zone-line request waiting for the server's
        /// answer (<paramref name="zoneLinePending"/>) counts too: retail goes black as the player crosses. And any zone
        /// geometry swap the view has to make (<paramref name="zoneSwapPending"/>: the zone to draw, the session's or an
        /// event's scene zone, is not the one on screen) counts, so an event's or a cutscene's zone, the first zone-in after
        /// character creation and any other swap load under the black too (round 3). Otherwise an event's scene zone
        /// (<paramref name="eventZone"/>) is the event's own business, except that once its zone is on screen the black
        /// holds until the event has placed its camera (<paramref name="eventCameraPending"/>, at most
        /// <see cref="EventCameraWaitSeconds"/>), so the fade-in opens on the cutscene's first shot.
        /// </summary>
        public bool ComputeLoading(SessionState state, ushort currentZone, ushort eventZone, ushort loadedZone, double nowSeconds,
            bool playerReady = true, bool zoneLinePending = false, bool zoneSwapPending = false, bool eventCameraPending = false)
        {
            if (state is SessionState.ConnectingToGameServer or SessionState.ExchangingCryptoKeys or SessionState.LoadingWorldData)
            {
                _waitingZone = 0;
                _settledZone = 0;
                return true;
            }
            if (state != SessionState.ActiveInWorld)
            {
                _waitingZone = 0;
                return false;
            }
            if (eventZone == 0) _eventSettledZone = 0;
            if (!zoneSwapPending && eventZone != 0 && eventZone != _eventSettledZone && eventZone == loadedZone)
            {
                // An event's scene zone is on screen: stay black until the event has placed its camera (round 4: the first
                // view after the fade-in was not the cutscene's), at most EventCameraWaitSeconds.
                if (_eventWaitZone != eventZone)
                {
                    _eventWaitZone = eventZone;
                    _eventWaitSince = nowSeconds;
                }
                // Only when the swap to it went under the black (an event showing the zone already on screen fades nothing).
                if (eventCameraPending && IsLoading && nowSeconds - _eventWaitSince < EventCameraWaitSeconds) return true;
                _eventSettledZone = eventZone;
                _eventWaitZone = 0;
                return false;
            }
            if (!zoneSwapPending && (eventZone != 0 || currentZone == 0 || currentZone == _settledZone))
            {
                _waitingZone = 0;
                // The zone-line request (#350): retail fades to black as soon as the player crosses, before the server answers.
                return eventZone == 0 && zoneLinePending;
            }
            if (!zoneSwapPending && loadedZone == currentZone && playerReady)
            {
                _waitingZone = 0;
                _settledZone = currentZone;
                return false;
            }
            ushort waitKey = currentZone != 0 ? currentZone : eventZone;
            if (_waitingZone != waitKey)
            {
                _waitingZone = waitKey;
                _waitingSince = nowSeconds;
            }
            if (nowSeconds - _waitingSince < GeometryWaitSeconds) return true;
            if (!zoneSwapPending) _settledZone = currentZone;
            return false;
        }

        /// <summary>
        /// Advances the fade toward black while <paramref name="loading"/>, toward clear otherwise; returns the opacity.
        /// A fade-out, once started, always reaches full black, and the black then holds for at least
        /// <see cref="MinimumBlackSeconds"/>, even when the loading ended meanwhile: the black covers the whole swap.
        /// <see cref="IsLoading"/> stays true until the fade-in may start.
        /// </summary>
        public float Update(bool loading, double nowSeconds)
        {
            double dt = double.IsNaN(_lastSeconds) ? 0 : Math.Clamp(nowSeconds - _lastSeconds, 0, 0.25);
            _lastSeconds = nowSeconds;
            if (loading && _opacity < 1f) _fadingOut = true;
            if (_opacity >= 1f)
            {
                _fadingOut = false;
                if (double.IsNaN(_blackSince)) _blackSince = nowSeconds;
            }
            else
            {
                _blackSince = double.NaN;
            }
            bool holdBlack = _opacity >= 1f && !double.IsNaN(_blackSince) && nowSeconds - _blackSince < MinimumBlackSeconds;
            bool darken = loading || _fadingOut || holdBlack;
            IsLoading = darken;
            float step = (float)(dt / (darken ? FadeOutSeconds : FadeInSeconds));
            _opacity = darken ? Math.Min(1f, _opacity + step) : Math.Max(0f, _opacity - step);
            return _opacity;
        }

        /// <summary><see cref="ComputeLoading"/> and <see cref="Update"/> in one call.</summary>
        public float Update(SessionState state, ushort currentZone, ushort eventZone, ushort loadedZone, double nowSeconds,
            bool playerReady = true, bool zoneLinePending = false, bool zoneSwapPending = false, bool eventCameraPending = false) =>
            Update(ComputeLoading(state, currentZone, eventZone, loadedZone, nowSeconds, playerReady, zoneLinePending, zoneSwapPending, eventCameraPending), nowSeconds);
    }
}
