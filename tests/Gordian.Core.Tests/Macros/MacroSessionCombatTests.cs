// tests/Gordian.Core.Tests/Macros/MacroSessionCombatTests.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Gordian.Engine.Input;
using Gordian.Engine.Macros;
using Gordian.Core.Network;
using Gordian.Core.Tests.Ui;
using Gordian.Engine.Sessions;
using Gordian.Engine.Ui;
using Gordian.Core.World;
using Xunit;

namespace Gordian.Core.Tests.Macros
{
    /// <summary>Macros through a whole character session while engaged (#339 round 2, A22): key, tick, runner, chat and log.</summary>
    public class MacroSessionCombatTests
    {
        [Fact]
        public async Task EngagedSession_CtrlDigitRunsAndEchoes()
        {
            var net = new SessionNetworkManager("127.0.0.1", 54231) { CurrentState = SessionState.ActiveInWorld };
            var session = new CharacterSession("MacroCombatTester" + Guid.NewGuid().ToString("N")[..6], 77, "acct", net);
            // A library of its own, so the test never saves into the macros folder.
            var library = new MacroLibrary();
            library.ReplaceAll(new[] { new[] { new Macro("Echo", new[] { "/echo engaged ok" }) } }, null, 0, 0);
            session.Macros.Library = library;

            session.LocalPlayer.ServerId = 0x01020304;
            session.World.UpsertEntity(new WorldEntity(0x01020304, 10, EntityType.Player) { Name = "Tester", Position = Vector3.Zero });
            session.World.UpsertEntity(new WorldEntity(0x01000022, 34, EntityType.Monster) { Name = "Wild Rabbit", Position = new Vector3(3, 0, 0), Hpp = 100 });
            session.ActionService.Menus.Library = StockUiMenuControllerTests.SyntheticLibrary();
            Assert.True(session.ActionService.SetTargetByServerId(0x01000022));
            session.Combat.Engage(0x01000022, 34);
            Assert.True(session.ActionService.OpenTargetCommandMenu());

            var input = session.InputState;
            void Tick() => session.Locomotion.Update(TimeSpan.FromMilliseconds(16));
            Tick();
            input.SetModifiers(InputModifiers.Control);
            input.SetKeyDown(GordianKey.LeftCtrl);
            Tick();
            Assert.Null(MacroController.BlockReason(session.ActionService.Menus, session.Chat));
            Assert.Equal(MacroPalette.Ctrl, session.Macros.ShownPalette);
            input.SetKeyDown(GordianKey.D1);
            Tick();
            await session.Macros.Runner.Current;
            var lines = new List<ChatLogLine>();
            session.Chat.Log.CopyVisible(1, 20, lines);
            Assert.Contains(lines, l => l.Text == "engaged ok" && l.Channel == ChatLogChannel.Echo);
        }
    }
}
