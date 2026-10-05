// tests/Gordian.Core.Tests/Ui/ZoneLoadingScreenTests.cs
using Gordian.Core.Network;
using Gordian.Core.Ui;
using Xunit;

namespace Gordian.Core.Tests.Ui
{
    public class ZoneLoadingScreenTests
    {
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
