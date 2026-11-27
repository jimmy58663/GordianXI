// tests/Gordian.Engine.Tests/Network/CharacterConfigStoresTests.cs
using System;
using Gordian.Core.Network;
using Gordian.Core.World;
using Gordian.Engine.Macros;
using Gordian.Engine.Sessions;
using Gordian.Engine.Ui;
using Xunit;

namespace Gordian.Engine.Tests.Network
{
    /// <summary>
    /// The per-character stores key on the server as well as the name (#639), so the same character name on two servers
    /// gets its own macros, sets, layout, settings and history, and two sessions of one character share them.
    /// </summary>
    public sealed class CharacterConfigStoresTests
    {
        /// <summary>A name no other test uses, so the stores' process-wide caches start empty for it.</summary>
        private static string UniqueName() => "Char" + Guid.NewGuid().ToString("N")[..8];

        private static CharacterSession Session(string name, string server) =>
            new(name, 1001, "account", new SessionNetworkManager("127.0.0.1", 54230), serverName: server);

        [Fact]
        public void Session_KeepsItsServerName()
        {
            using var session = Session(UniqueName(), "127.0.0.1");

            Assert.Equal("127.0.0.1", session.ServerName);
        }

        [Fact]
        public void Session_TheSameNameOnTwoServers_GetsSeparateLayoutsSettingsAndSets()
        {
            string name = UniqueName();
            using var retail = Session(name, "Retail");
            using var lsb = Session(name, "127.0.0.1");

            Assert.NotSame(retail.ActionService.UiLayout, lsb.ActionService.UiLayout);
            Assert.NotSame(retail.ActionService.UiSettings, lsb.ActionService.UiSettings);
            Assert.NotSame(retail.ActionService.EquipmentSets, lsb.ActionService.EquipmentSets);
        }

        [Fact]
        public void Session_TheSameCharacterTwice_SharesItsLayoutSettingsAndSets()
        {
            string name = UniqueName();
            using var first = Session(name, "127.0.0.1");
            using var second = Session(name, "127.0.0.1");

            Assert.Same(first.ActionService.UiLayout, second.ActionService.UiLayout);
            Assert.Same(first.ActionService.UiSettings, second.ActionService.UiSettings);
            Assert.Same(first.ActionService.EquipmentSets, second.ActionService.EquipmentSets);
        }

        [Fact]
        public void Macros_TheSameNameOnTwoServers_AreSeparate()
        {
            string name = UniqueName();

            Assert.NotSame(MacroStore.GetForCharacter("Retail", name), MacroStore.GetForCharacter("127.0.0.1", name));
            Assert.Same(MacroStore.GetForCharacter("Retail", name), MacroStore.GetForCharacter("Retail", name));
        }

        [Fact]
        public void SynthesisHistory_TheSameNameOnTwoServers_IsSeparate()
        {
            string name = UniqueName();

            Assert.NotSame(SynthesisHistoryStore.GetForCharacter("Retail", name), SynthesisHistoryStore.GetForCharacter("127.0.0.1", name));
        }

        [Fact]
        public void Stores_IgnoreTheCaseOfTheName()
        {
            string name = UniqueName();

            Assert.Same(StockUiLayoutStore.GetForCharacter("Retail", name), StockUiLayoutStore.GetForCharacter("Retail", name.ToUpperInvariant()));
        }
    }
}
