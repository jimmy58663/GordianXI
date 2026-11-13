// tests/Gordian.App.Tests/Graphics/StockUiNamePlateMagnifyTests.cs
using System.Numerics;
using Gordian.App.Graphics;
using Gordian.Core.Network;
using Gordian.Core.World;
using Gordian.Engine.Actions;
using Gordian.Engine.Sessions;
using Xunit;

namespace Gordian.App.Tests.Graphics
{
    /// <summary>Which name plates the sub-target cursor draws at twice the size (#356 round 2, TESTING-2026-11-12 D2).</summary>
    public class StockUiNamePlateMagnifyTests
    {
        private const uint SelfId = 500, FriendId = 0x0A;

        private static CharacterSession Session()
        {
            var net = new SessionNetworkManager("127.0.0.1", 54231) { CurrentState = SessionState.ActiveInWorld };
            var session = new CharacterSession("Gemini", SelfId, "user", net);
            session.LocalPlayer.ServerId = SelfId;
            session.World.UpsertEntity(new WorldEntity(SelfId, 10, EntityType.Player) { Name = "Gemini", Position = Vector3.Zero });
            session.World.UpsertEntity(new WorldEntity(FriendId, 11, EntityType.Player) { Name = "Claude", Position = new Vector3(2, 0, 0) });
            return session;
        }

        [Fact]
        public void SingleTargetCursor_MagnifiesThePlateUnderIt()
        {
            var session = Session();
            Assert.Empty(StockUiNamePlates.MagnifiedPlates(session));
            session.ActionService.BeginSubTarget("Cure", ActionTargetFlags.Self | ActionTargetFlags.Player, _ => System.Threading.Tasks.Task.FromResult(PlayerActionResult.Ok("")),
                session.World.TryGetByServerId(FriendId, out var friend) ? friend : null);
            Assert.Equal(FriendId, session.ActionService.SubTargeting!.Current.ServerId);
            Assert.Equal(new[] { FriendId }, StockUiNamePlates.MagnifiedPlates(session));
        }

        [Fact]
        public void OwnPlate_IsNeverMagnified()
        {
            var session = Session();
            session.ActionService.BeginSubTarget("Cure", ActionTargetFlags.Self, _ => System.Threading.Tasks.Task.FromResult(PlayerActionResult.Ok("")));
            Assert.Equal(SelfId, session.ActionService.SubTargeting!.Current.ServerId);
            Assert.Empty(StockUiNamePlates.MagnifiedPlates(session));
        }
    }
}
