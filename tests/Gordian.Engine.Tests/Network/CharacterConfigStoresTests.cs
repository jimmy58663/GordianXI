// tests/Gordian.Engine.Tests/Network/CharacterConfigStoresTests.cs
using System;
using Gordian.Core.Config;
using Gordian.Core.Network;
using Gordian.Core.World;
using Gordian.Engine.Macros;
using Gordian.Engine.Sessions;
using Gordian.Engine.Ui;
using Xunit;

namespace Gordian.Engine.Tests.Network
{
    /// <summary>
    /// The per-character stores key on the server and world as well as the name (#639), so the same character name on two
    /// servers or worlds gets its own macros, sets, layout, settings and history, and two sessions of one character share them.
    /// </summary>
    public sealed class CharacterConfigStoresTests
    {
        /// <summary>A name no other test uses, so the stores' process-wide caches start empty for it.</summary>
        private static string UniqueName() => "Char" + Guid.NewGuid().ToString("N")[..8];

        private static CharacterSession Session(string name, string server, string world = "Asura") =>
            new(name, 1001, "account", new SessionNetworkManager("127.0.0.1", 54230), serverName: server, worldName: world);

        [Fact]
        public void Session_FolderIsServerWorldAndCharacter()
        {
            string name = UniqueName();
            using var session = Session(name, "127.0.0.1", "Gordian");

            Assert.Equal(System.IO.Path.Combine("127.0.0.1", "Gordian", name), session.Folder.RelativePath);
        }

        [Fact]
        public void Session_WithNoWorld_UsesUnknown()
        {
            string name = UniqueName();
            using var session = Session(name, "127.0.0.1", "");

            Assert.Equal(System.IO.Path.Combine("127.0.0.1", "Unknown", name), session.Folder.RelativePath);
        }

        [Fact]
        public void Session_TheSameNameOnTwoWorlds_GetsSeparateLayoutsSettingsAndSets()
        {
            string name = UniqueName();
            using var asura = Session(name, "Retail", "Asura");
            using var shiva = Session(name, "Retail", "Shiva");

            Assert.NotSame(asura.ActionService.UiLayout, shiva.ActionService.UiLayout);
            Assert.NotSame(asura.ActionService.UiSettings, shiva.ActionService.UiSettings);
            Assert.NotSame(asura.ActionService.EquipmentSets, shiva.ActionService.EquipmentSets);
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

            Assert.NotSame(MacroStore.GetForCharacter(new CharacterFolderKey("Retail", "Asura", name)), MacroStore.GetForCharacter(new CharacterFolderKey("127.0.0.1", "Asura", name)));
            Assert.Same(MacroStore.GetForCharacter(new CharacterFolderKey("Retail", "Asura", name)), MacroStore.GetForCharacter(new CharacterFolderKey("Retail", "Asura", name)));
        }

        [Fact]
        public void SynthesisHistory_TheSameNameOnTwoServers_IsSeparate()
        {
            string name = UniqueName();

            Assert.NotSame(SynthesisHistoryStore.GetForCharacter(new CharacterFolderKey("Retail", "Asura", name)), SynthesisHistoryStore.GetForCharacter(new CharacterFolderKey("127.0.0.1", "Asura", name)));
        }

        [Fact]
        public void Stores_IgnoreTheCaseOfTheName()
        {
            string name = UniqueName();

            Assert.Same(StockUiLayoutStore.GetForCharacter(new CharacterFolderKey("Retail", "Asura", name)), StockUiLayoutStore.GetForCharacter(new CharacterFolderKey("Retail", "Asura", name.ToUpperInvariant())));
        }
    }
}
