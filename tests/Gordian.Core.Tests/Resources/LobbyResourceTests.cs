// tests/Gordian.Core.Tests/Resources/LobbyResourceTests.cs
using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Tables;
using Gordian.Core.Resources.Ui;
using Xunit;

namespace Gordian.Core.Tests.Resources
{
    /// <summary>The lobby's DAT resources: the XISTRING parser, the lobby text tables and the lobby menus (retail data when installed).</summary>
    public class LobbyResourceTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";

        private static ResourceManager? Resources()
        {
            if (!Directory.Exists(GameDirectory)) return null;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            return rm;
        }

        [Fact]
        public void XiStringTable_ParsesTheIndexAndStrings()
        {
            var strings = new[] { "First.", "Second line." };
            int dataStart = 0x38 + strings.Length * 12;
            var data = new System.Collections.Generic.List<byte>();
            var offsets = new int[strings.Length];
            for (int i = 0; i < strings.Length; i++)
            {
                offsets[i] = data.Count;
                data.AddRange(Encoding.ASCII.GetBytes(strings[i]));
                data.Add(0);
            }
            var file = new byte[dataStart + data.Count];
            "XISTRING"u8.CopyTo(file);
            BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(0x24), strings.Length);
            for (int i = 0; i < strings.Length; i++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(0x38 + i * 12), offsets[i]);
                BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(0x38 + i * 12 + 4), (ushort)strings[i].Length);
            }
            data.CopyTo(file, dataStart);

            var table = XiStringTable.Parse(file);
            Assert.NotNull(table);
            Assert.Equal(2, table!.Count);
            Assert.Equal("Second line.", table[1]);
            Assert.Equal(string.Empty, table[5]);
            Assert.Null(XiStringTable.Parse("d_msg"u8));
        }

        [Fact]
        public void LobbyTextTables_WithoutData_StillFormatTheCode()
        {
            var text = new LobbyTextTables(null, null);
            Assert.Equal(string.Empty, text.Status(LobbyTextTables.AcquiringCharacterList));
            Assert.Equal(new[] { "Error code: FFXI-3313" }, text.ErrorLines(313));
        }

        [Fact]
        public void LobbyTextTables_RetailRowsSayWhatTheConstantsName()
        {
            var rm = Resources();
            if (rm == null) return;
            var text = LobbyTextTables.Load(rm.LoadDatBytes);
            if (!text.HasStatusText) return;
            Assert.Equal("Acquiring character list.", text.Status(LobbyTextTables.AcquiringCharacterList));
            Assert.Equal("Notifying lobby server of choice.", text.Status(LobbyTextTables.NotifyingLobbyOfChoice));
            Assert.Equal("Select a character to play.", text.Status(LobbyTextTables.HelpSelectCharacterToPlay));
            Assert.StartsWith("Select a character to delete.", text.Status(LobbyTextTables.HelpSelectCharacterToDelete));
            Assert.Equal("The data will be lost forever. Proceed?", text.Status(LobbyTextTables.DeleteConfirm));
            Assert.Equal("Choose your character's race and gender.", text.Status(LobbyTextTables.HelpChooseRace));
            Assert.Equal("Select a country to start in.", text.Status(LobbyTextTables.HelpChooseNation));
            Assert.Equal("The Republic of Bastok", text.Status(LobbyTextTables.BastokTitle));
            Assert.Equal("You cannot create any more characters.", text.Status(LobbyTextTables.CannotCreateMoreCharacters));
            Assert.StartsWith("Register \"%s\"", text.Status(LobbyTextTables.RegisterAndBeginLine1));

            var nameTaken = text.ErrorLines(313);
            Assert.Equal("The character name you entered is unavailable.", nameTaken[0]);
            Assert.Equal("Error code: FFXI-3313", nameTaken[^1]);
            Assert.StartsWith("Same character is already logged in.", text.ErrorLines(201)[0]);
            Assert.StartsWith("Unable to connect to world server.", text.ErrorLines(305)[0]);
            Assert.StartsWith("Could not connect to lobby server.", text.ErrorLines(332)[0]);
            Assert.StartsWith("Character names must be at least three letters", text.ErrorLines(110)[0]);
        }

        [Fact]
        public void LobbyLibrary_HasTheLobbyMenusAndThePs2GroupAlias()
        {
            var rm = Resources();
            if (rm == null) return;
            var library = UiResourceLibrary.LoadLobby(rm);
            Assert.NotNull(library);
            Assert.True(library!.TryGetMenu("loby2win", out var main));
            Assert.Equal(5, main.Buttons.Count);
            Assert.True(library.TryGetMenu("dbnamese", out var list));
            Assert.Equal(16, list.Buttons.Count);
            Assert.Equal((304, 242), (list.FindButton(1)!.X, list.FindButton(1)!.Y));
            Assert.Equal((480, 242), (list.FindButton(9)!.X, list.FindButton(9)!.Y));
            Assert.True(library.TryGetMenu("ptc9dele", out var delete));
            foreach (var shape in delete.Buttons[0].Shapes) Assert.True(library.TryGetImage(shape, out _), $"{shape.GroupName}#{shape.ImageIndex}");
            Assert.True(library.TryGetGroup("lobbyps2", out var ps2));
            Assert.Equal("lobbywin", ps2.Name);
            // The in-game menus still resolve: the creation menus draw their labels from windowps.
            Assert.True(library.TryGetMenu("chmkrace", out var race));
            foreach (var button in race.Buttons) foreach (var shape in button.Shapes) Assert.True(library.TryGetImage(shape, out _));
        }
    }
}
