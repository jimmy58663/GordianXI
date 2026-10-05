// src/Gordian.Core/Ui/ZoneLoadingScreen.cs
using System;
using Gordian.Core.Network;

namespace Gordian.Core.Ui
{
    /// <summary>
    /// The loading state of a session's view (#36): the screen goes black while the character is zoning in (after
    /// character select) or crossing to another zone, and comes back once the map server has the character in the world
    /// and the zone's geometry is on screen. Retail shows no loading art: no menu, texture or string in the client DATs is
    /// a zone-loading screen (searched 2026-10-04: the in-game and lobby menu DATs, the ROM/165, ROM/97, ROM/176 and
    /// ROM/27 string tables), and the legacy client fades the scene to black while it zones.
    /// <para>
    /// <see cref="Update"/> returns the black overlay's opacity (0 = clear, 1 = black), easing toward it over
    /// <see cref="FadeOutSeconds"/> / <see cref="FadeInSeconds"/> (tuned values, not measured against retail).
    /// </para>
    /// </summary>
    public sealed class ZoneLoadingScreen
    {
        /// <summary>Seconds to fade to black when loading starts, and back when it ends (not measured against retail). The one place these timings live: the lobby's fade after a
        /// character is chosen uses <see cref="FadeOutSeconds"/> too. Lengthened from 0.35 / 0.6 s after the first in-game test
        /// (the transition felt too quick).</summary>
        public const double FadeOutSeconds = 0.6, FadeInSeconds = 1.0;

        /// <summary>
        /// How long the screen stays black waiting for a zone's geometry once the session is in the world; after that the
        /// scene shows anyway, so a zone that cannot be loaded (missing DATs) does not leave the screen black.
        /// </summary>
        public const double GeometryWaitSeconds = 20;

        private float _opacity;
        private double _lastSeconds = double.NaN;
        private ushort _waitingZone;
        private double _waitingSince;

        /// <summary>The current overlay opacity.</summary>
        public float Opacity => _opacity;

        /// <summary>Whether the last update counted the view as loading.</summary>
        public bool IsLoading { get; private set; }

        /// <summary>Restarts the fade: black at once (a session that is still connecting) or clear.</summary>
        public void Reset(bool black)
        {
            _opacity = black ? 1f : 0f;
            _lastSeconds = double.NaN;
            _waitingZone = 0;
            IsLoading = black;
        }

        /// <summary>
        /// Whether a session's view is loading: the session is connecting to or zoning into a map server (not yet in
        /// the world), or it is in the world but its zone's geometry is not on screen yet (for at most
        /// <see cref="GeometryWaitSeconds"/>). An event's scene zone (<paramref name="eventZone"/>) is the event's own
        /// business and does not count.
        /// </summary>
        public bool ComputeLoading(SessionState state, ushort currentZone, ushort eventZone, ushort loadedZone, double nowSeconds)
        {
            if (state is SessionState.ConnectingToGameServer or SessionState.ExchangingCryptoKeys or SessionState.LoadingWorldData)
            {
                _waitingZone = 0;
                return true;
            }
            if (state != SessionState.ActiveInWorld || eventZone != 0 || currentZone == 0 || loadedZone == currentZone)
            {
                _waitingZone = 0;
                return false;
            }
            if (_waitingZone != currentZone)
            {
                _waitingZone = currentZone;
                _waitingSince = nowSeconds;
            }
            return nowSeconds - _waitingSince < GeometryWaitSeconds;
        }

        /// <summary>Advances the fade toward black while <paramref name="loading"/>, toward clear otherwise; returns the opacity.</summary>
        public float Update(bool loading, double nowSeconds)
        {
            IsLoading = loading;
            double dt = double.IsNaN(_lastSeconds) ? 0 : Math.Clamp(nowSeconds - _lastSeconds, 0, 0.25);
            _lastSeconds = nowSeconds;
            float step = (float)(dt / (loading ? FadeOutSeconds : FadeInSeconds));
            _opacity = loading ? Math.Min(1f, _opacity + step) : Math.Max(0f, _opacity - step);
            return _opacity;
        }

        /// <summary><see cref="ComputeLoading"/> and <see cref="Update"/> in one call.</summary>
        public float Update(SessionState state, ushort currentZone, ushort eventZone, ushort loadedZone, double nowSeconds) =>
            Update(ComputeLoading(state, currentZone, eventZone, loadedZone, nowSeconds), nowSeconds);
    }
}
