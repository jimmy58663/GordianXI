// tests/Gordian.Engine.Tests/Ui/Lobby/LobbyStageTests.cs
using System;
using System.Numerics;
using Gordian.Engine.Ui.Lobby;
using Xunit;
using static Gordian.Engine.Tests.Ui.LobbyControllerTests;

namespace Gordian.Engine.Tests.Ui.Lobby
{
    /// <summary>The stage behind character select, deletion and creation (#552 round 2).</summary>
    public class LobbyStageTests
    {
        [Fact]
        public void TheTitleMenuShowsTheFlythrough_TheListDeletionAndCreationTheStage_EachAtItsHour()
        {
            var lobby = new LobbyController(Backend(Character(1, "Knot"), Free(2)), null, null, showLicence: false);
            Assert.False(LobbyStage.Shows(lobby)); // title menu

            lobby.HandleInput(LobbyInput.Confirm); // Select Character
            Assert.True(LobbyStage.Shows(lobby));
            Assert.Equal(LobbyStage.ListHour, LobbyStage.HourOf(lobby));
            lobby.HandleInput(LobbyInput.Cancel);

            lobby.HandleInput(LobbyInput.Down);
            lobby.HandleInput(LobbyInput.Down);
            lobby.HandleInput(LobbyInput.Confirm); // Delete Character
            Assert.True(lobby.IsDeleteList);
            Assert.Equal(LobbyStage.DeleteHour, LobbyStage.HourOf(lobby));
            lobby.HandleInput(LobbyInput.Cancel);

            lobby.HandleInput(LobbyInput.Up);
            lobby.HandleInput(LobbyInput.Confirm); // Create Character
            Assert.Equal(LobbyScreen.Creation, lobby.Screen);
            Assert.True(LobbyStage.Shows(lobby));
            Assert.Equal(LobbyStage.CreationHour, LobbyStage.HourOf(lobby));
        }

        [Fact]
        public void TheLicencePageShowsNoStage() =>
            Assert.False(LobbyStage.Shows(new LobbyController(Backend(Character(1, "Knot")), null, null)));

        [Fact]
        public void PlacesThePreviewCameraOnTheSpot_LookingAlongTheHeading()
        {
            // A preview camera 5 yalms in front of the model (along +Z), looking back at it along -Z.
            var (eye, target) = LobbyStage.Place(new Vector3(0, 1, 5), new Vector3(0, 1, 0));
            Assert.Equal(LobbyStage.Spot + new Vector3(0, 1, 0), target, new Vector3Comparer());
            var look = Vector3.Normalize(target - eye);
            float heading = LobbyStage.HeadingDegrees * MathF.PI / 180f;
            Assert.Equal(MathF.Cos(heading), look.X, 3);
            Assert.Equal(0f, look.Y, 3);
            Assert.Equal(MathF.Sin(heading), look.Z, 3);
            Assert.Equal(5f, Vector3.Distance(eye, target), 3);
        }

        private sealed class Vector3Comparer : System.Collections.Generic.IEqualityComparer<Vector3>
        {
            public bool Equals(Vector3 a, Vector3 b) => Vector3.Distance(a, b) < 1e-3f;
            public int GetHashCode(Vector3 v) => 0;
        }
    }
}
