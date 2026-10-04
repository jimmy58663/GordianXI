// tests/Gordian.Core.Tests/Events/ChatDatReferenceTests.cs
using System.IO;
using System.Linq;
using System.Text;
using Gordian.Core.Events;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Models;
using Gordian.Core.Resources.Tables;
using Xunit;

namespace Gordian.Core.Tests.Events
{
    /// <summary>S2C 0x017 messages with Attr 0x08: a DAT message reference formatted with the packet's parameters.</summary>
    public class ChatDatReferenceTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";

        private static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text);

        private static byte[] Tag(byte kind, int parameter) =>
            new byte[] { 0x01, 0x05, kind, 0x82, (byte)(0x80 | parameter), 0x80, 0x80 };

        private static string? Names(byte kind, int id) => (kind, id) switch
        {
            (EventMessageFormatter.UnityLeaderKind, 10) => "Yoran-Oran",
            (EventMessageFormatter.ZoneKind, 291) => "Reisenjima",
            _ => null,
        };

        [Fact]
        public void FormatChatReference_MakesTheLeadingUnityLeaderTheSpeaker()
        {
            // 0x89 leader tag (parameter 0), text, zone tag (parameter 2), text: the shape of Unity message 495.
            var raw = Tag(EventMessageFormatter.UnityLeaderKind, 0).Concat(Ascii("Monsters were sighted in "))
                .Concat(Tag(EventMessageFormatter.ZoneKind, 2)).Concat(Ascii(".")).Concat(new byte[] { 0x7F, 0x31, 0x00 }).ToArray();
            var message = EventMessageDecoder.Decode(raw);
            var reference = new ChatFormattedMessage(ChatFormattedTable.UnityMess, 495, 10, 45, 291, 2, 2);

            var lines = EventDialogController.FormatChatReference(message, reference, ChatMessageType.Unity, string.Empty, "Me", Names);

            Assert.Equal(new[] { "{Yoran-Oran} Monsters were sighted in Reisenjima." }, lines);
        }

        [Fact]
        public void FormatChatReference_UsesThePacketSenderOtherwise()
        {
            var raw = Ascii("Hello from ").Concat(Tag(EventMessageFormatter.ZoneKind, 0)).Concat(Ascii(".")).ToArray();
            var message = EventMessageDecoder.Decode(raw);
            var reference = new ChatFormattedMessage(ChatFormattedTable.EventMess, 7, 291, 0, 0, 0, 0);

            Assert.Equal(new[] { "Cybin : Hello from Reisenjima." },
                EventDialogController.FormatChatReference(message, reference, ChatMessageType.Say, "Cybin", "Me", Names));
            Assert.Equal(new[] { "Hello from Reisenjima." },
                EventDialogController.FormatChatReference(message, reference, ChatMessageType.System3, string.Empty, "Me", Names));
        }

        /// <summary>
        /// The retail capture's reference (2025-02-10: Unity, table 10, message 0x1EF, parameters 10, 0x2D, 291, 2, 2)
        /// against the game's own UnityMess table and Unity leader names; retail printed
        /// "{Yoran-Oran} Our field researchers have sent a report-ethy from ?-? in Reisenjima. Apparently, monsters
        /// affiliated with Quetzalcoatl have arrived there. Those guys are always getting themselves into trouble."
        /// Skipped without a game install.
        /// </summary>
        [Fact]
        public void RetailUnityReference_FormatsLikeRetail()
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var table = ZoneDialogTable.Parse(rm.LoadDatBytesByFileId(EventDialogController.UnityMessageFileId)!);
            Assert.NotNull(table);
            var message = table!.GetMessage(0x1EF);
            Assert.NotNull(message);
            var reference = new ChatFormattedMessage(ChatFormattedTable.UnityMess, 0x1EF, 10, 0x2D, 291, 2, 2);

            var lines = EventDialogController.FormatChatReference(message!, reference, ChatMessageType.Unity, string.Empty, "Me",
                (kind, id) => EventMessageNames.Resolve(rm, kind, id));

            Assert.Equal("Pieuje", EventMessageNames.Resolve(rm, EventMessageFormatter.UnityLeaderKind, 1));
            Assert.Equal("Sylvie", EventMessageNames.Resolve(rm, EventMessageFormatter.UnityLeaderKind, 11));
            Assert.True(rm.TryGetString(DMsgCategory.MiscStrings, EventMessageNames.UnityLeaderRow, out var none));
            Assert.Equal("No Unity", none);
            Assert.Equal(new[]
            {
                "{Yoran-Oran} Our field researchers have sent a report-ethy from ?-? in Reisenjima. Apparently, monsters " +
                "affiliated with Quetzalcoatl have arrived there. Those guys are always getting themselves into trouble.",
            }, lines);
        }
    }
}
