// tests/Gordian.Core.Tests/Ui/ZoneLoadingScreenTests.cs
using Gordian.Core.Network;
using Gordian.Core.Ui;
using Xunit;

namespace Gordian.Core.Tests.Ui
{
    public class ZoneLoadingScreenTests
    {
        [Fact]
        public void ZoneLinePending_GoesBlackWithTheIndicatorOnceBlack()
        {
            var screen = new ZoneLoadingScreen();
            // Settled in zone 230, then the zone-line request goes out: black, and "Downloading data ..." as soon as it is.
            Assert.False(screen.ComputeLoading(SessionState.ActiveInWorld, 230, 0, 230, 0));
            for (double t = 1; t <= 1.05 + ZoneLoadingScreen.FadeOutSeconds; t += 0.1)
            {
                screen.Update(SessionState.ActiveInWorld, 230, 0, 230, t, zoneLinePending: true);
            }
            Assert.True(screen.IsLoading);
            Assert.Equal(1f, screen.Opacity);
            Assert.True(screen.IndicatorVisible);
            // The zone change arrives: the session connects to the next map server, the sprite stays.
            screen.Update(SessionState.ConnectingToGameServer, 230, 0, 230, 3);
            Assert.True(screen.IndicatorVisible);
            // In the new zone, loaded: the sprite goes at once and the scene fades in.
            screen.Update(SessionState.ActiveInWorld, 101, 0, 101, 4);
            Assert.False(screen.IndicatorVisible);
            Assert.False(screen.IsLoading);
        }

        [Fact]
        public void ZoneLineRefused_FadesBackIn()
        {
            var screen = new ZoneLoadingScreen();
            screen.Update(SessionState.ActiveInWorld, 230, 0, 230, 0);
            screen.Update(SessionState.ActiveInWorld, 230, 0, 230, 0.5, zoneLinePending: true);
            Assert.True(screen.Opacity > 0f);
            // Refused: the fade-out still reaches full black (round 3: a fade, once started, always covers the change),
            // holds it briefly and then fades back in.
            double t = 0.6;
            for (; t < 3.0 && screen.Opacity < 1f; t += 0.05) screen.Update(SessionState.ActiveInWorld, 230, 0, 230, t, zoneLinePending: false);
            Assert.Equal(1f, screen.Opacity);
            for (; t < 3.5 && screen.IsLoading; t += 0.05) screen.Update(SessionState.ActiveInWorld, 230, 0, 230, t, zoneLinePending: false);
            Assert.False(screen.IsLoading);
            Assert.False(screen.IndicatorVisible);
        }

        [Fact]
        public void FadeOut_AlwaysReachesBlack_AndHoldsTheMinimumBeforeFadingIn()
        {
            var screen = new ZoneLoadingScreen();
            screen.Update(SessionState.ActiveInWorld, 230, 0, 230, 0);
            // A swap to a cached zone: ready almost at once, but the fade-out runs to full black first.
            screen.Update(SessionState.ActiveInWorld, 230, 0, 230, 0.05, zoneSwapPending: true);
            Assert.False(screen.IsFullyBlack);
            double t = 0.1;
            for (; t < 0.9; t += 0.05) screen.Update(SessionState.ActiveInWorld, 230, 0, 230, t);
            Assert.True(screen.IsLoading);
            Assert.True(screen.Opacity < 1f);
            for (; screen.Opacity < 1f && t < 3; t += 0.05) screen.Update(SessionState.ActiveInWorld, 230, 0, 230, t);
            double blackAt = t;
            Assert.True(screen.IndicatorVisible);
            for (; screen.IsLoading && t < 3; t += 0.01) screen.Update(SessionState.ActiveInWorld, 230, 0, 230, t);
            Assert.InRange(t - blackAt, ZoneLoadingScreen.MinimumBlackSeconds - 0.06, ZoneLoadingScreen.MinimumBlackSeconds + 0.06);
        }

        [Fact]
        public void EventSceneZone_HoldsBlackUntilTheEventPlacesItsCamera()
        {
            var screen = new ZoneLoadingScreen();
            screen.Reset(black: true); // the swap to the scene zone went under the black
            screen.Update(SessionState.ActiveInWorld, 230, 239, 230, 0, zoneSwapPending: true);
            // Scene zone on screen, no camera yet: still black.
            Assert.True(screen.ComputeLoading(SessionState.ActiveInWorld, 230, 239, 239, 1, eventCameraPending: true));
            Assert.True(screen.ComputeLoading(SessionState.ActiveInWorld, 230, 239, 239, 2, eventCameraPending: true));
            // The first camera shot: fade in, and the event zone stays settled when its camera is later released.
            Assert.False(screen.ComputeLoading(SessionState.ActiveInWorld, 230, 239, 239, 2.5, eventCameraPending: false));
            Assert.False(screen.ComputeLoading(SessionState.ActiveInWorld, 230, 239, 239, 3, eventCameraPending: true));
        }

        [Fact]
        public void EventSceneZone_CameraWaitIsCapped()
        {
            var screen = new ZoneLoadingScreen();
            screen.Reset(black: true);
            screen.Update(SessionState.ActiveInWorld, 230, 239, 230, 0, zoneSwapPending: true);
            Assert.True(screen.ComputeLoading(SessionState.ActiveInWorld, 230, 239, 239, 1, eventCameraPending: true));
            Assert.False(screen.ComputeLoading(SessionState.ActiveInWorld, 230, 239, 239, 1 + ZoneLoadingScreen.EventCameraWaitSeconds + 0.1, eventCameraPending: true));
        }

        [Fact]
        public void EventSceneZoneSwap_CountsAsLoading()
        {
            var screen = new ZoneLoadingScreen();
            Assert.False(screen.ComputeLoading(SessionState.ActiveInWorld, 230, 0, 230, 0));
            // An event opens Windurst Walls (239) for its scene: the swap loads under the black, then the event's zone is its own.
            Assert.True(screen.ComputeLoading(SessionState.ActiveInWorld, 230, 239, 230, 1, zoneSwapPending: true));
            Assert.False(screen.ComputeLoading(SessionState.ActiveInWorld, 230, 239, 239, 2));
        }

        [Fact]
        public void IndicatorWaitsForFullBlack()
        {
            var screen = new ZoneLoadingScreen();
            screen.Reset(black: false);
            screen.Update(SessionState.ConnectingToGameServer, 0, 0, 0, 0);
            screen.Update(SessionState.ConnectingToGameServer, 0, 0, 0, 0.1);
            Assert.False(screen.IndicatorVisible);
            screen.Reset(black: true);
            screen.Update(SessionState.ConnectingToGameServer, 0, 0, 0, 1);
            Assert.True(screen.IndicatorVisible);
        }

        [Theory]
        [InlineData(SessionState.ConnectingToGameServer)]
        [InlineData(SessionState.ExchangingCryptoKeys)]
        [InlineData(SessionState.LoadingWorldData)]
        public void Connecting_OrZoning_IsLoading(SessionState state)
        {
            var screen = new ZoneLoadingScreen();
            Assert.True(screen.ComputeLoading(state, 230, 0, 230, 0));
        }

        [Fact]
        public void InTheWorld_WaitsForTheZoneGeometryButNotForever()
        {
            var screen = new ZoneLoadingScreen();
            Assert.True(screen.ComputeLoading(SessionState.ActiveInWorld, 231, 0, 230, 0));
            Assert.True(screen.ComputeLoading(SessionState.ActiveInWorld, 231, 0, 230, 10));
            Assert.False(screen.ComputeLoading(SessionState.ActiveInWorld, 231, 0, 231, 11));
            // A zone that never loads: shown anyway after the wait.
            Assert.True(screen.ComputeLoading(SessionState.ActiveInWorld, 232, 0, 231, 20));
            Assert.False(screen.ComputeLoading(SessionState.ActiveInWorld, 232, 0, 231, 20 + ZoneLoadingScreen.GeometryWaitSeconds + 1));
        }

        [Fact]
        public void InTheWorld_WaitsForThePlayersModel_ThenStaysSettled()
        {
            var screen = new ZoneLoadingScreen();
            // Geometry on screen but the look not here yet: still black (no placeholder model).
            Assert.True(screen.ComputeLoading(SessionState.ActiveInWorld, 231, 0, 231, 0, playerReady: false));
            Assert.False(screen.ComputeLoading(SessionState.ActiveInWorld, 231, 0, 231, 1, playerReady: true));
            // Settled: a look change or geometry reload in the same zone does not black the screen again.
            Assert.False(screen.ComputeLoading(SessionState.ActiveInWorld, 231, 0, 231, 2, playerReady: false));
            Assert.False(screen.ComputeLoading(SessionState.ActiveInWorld, 231, 0, 0, 3));
            // The next zone change waits again, and a model that never loads is shown anyway after the wait.
            Assert.True(screen.ComputeLoading(SessionState.LoadingWorldData, 0, 0, 231, 4));
            Assert.True(screen.ComputeLoading(SessionState.ActiveInWorld, 232, 0, 232, 5, playerReady: false));
            Assert.False(screen.ComputeLoading(SessionState.ActiveInWorld, 232, 0, 232, 5 + ZoneLoadingScreen.GeometryWaitSeconds + 1, playerReady: false));
            Assert.False(screen.ComputeLoading(SessionState.ActiveInWorld, 232, 0, 232, 30, playerReady: false));
        }

        [Fact]
        public void AnEventsSceneZone_IsNotLoading()
        {
            var screen = new ZoneLoadingScreen();
            Assert.False(screen.ComputeLoading(SessionState.ActiveInWorld, 231, 244, 244, 0));
            Assert.False(screen.ComputeLoading(SessionState.Disconnected, 0, 0, 0, 0));
        }

        [Fact]
        public void Fades_ToBlackAndBack()
        {
            var screen = new ZoneLoadingScreen();
            double t = 0;
            float Run(bool loading, double seconds)
            {
                float opacity = screen.Update(loading, t);
                for (double end = t + seconds; t < end - 1e-9;) opacity = screen.Update(loading, t = System.Math.Min(end, t + 1 / 60.0));
                return opacity;
            }
            Assert.Equal(0f, Run(true, 0));
            Assert.InRange(Run(true, ZoneLoadingScreen.FadeOutSeconds / 2), 0.45f, 0.55f);
            Assert.Equal(1f, Run(true, ZoneLoadingScreen.FadeOutSeconds));
            Assert.Equal(1f, Run(true, 3));
            Assert.InRange(Run(false, ZoneLoadingScreen.FadeInSeconds / 2), 0.45f, 0.55f);
            Assert.Equal(0f, Run(false, ZoneLoadingScreen.FadeInSeconds));
            // A stalled frame does not jump the fade: one update moves at most a quarter second's worth.
            Assert.InRange(screen.Update(true, t + 10), 0.0f, 0.25f / (float)ZoneLoadingScreen.FadeOutSeconds + 0.01f);
        }

        [Fact]
        public void Reset_StartsBlackForASessionStillConnecting()
        {
            var screen = new ZoneLoadingScreen();
            screen.Reset(black: true);
            Assert.Equal(1f, screen.Update(true, 100));
            screen.Reset(black: false);
            Assert.Equal(0f, screen.Opacity);
        }
    }
}
