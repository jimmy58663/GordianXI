using System;
using System.Buffers.Binary;
using System.Text;
using Gordian.Core.Network;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources;
using Xunit;

namespace Gordian.Core.Tests.Network;
public class AutoTranslateTests
{
    public static byte[] Dictionary(string label = "Synthetic greeting")
    {
        var text = Encoding.ASCII.GetBytes(label);
        var data = new byte[76 + 6 + text.Length];
        data[0] = data[1] = 2; data[2] = 1;
        Encoding.ASCII.GetBytes("Test category").CopyTo(data, 4);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(68), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(72), (uint)(6 + text.Length));
        data[76] = data[77] = 2; data[78] = 1; data[79] = 5;
        data[80] = (byte)(text.Length + 1); text.CopyTo(data, 81);
        return data;
    }
    [Fact]
    public void LiteralDictionaryIdentitySurvivesWireAndIncomingDisplay()
    {
        var catalog = AutoTranslateCatalog.Parse(Dictionary());
        var phrase = Assert.Single(catalog.Phrases);
        var message = new ChatText("Synthetic greeting", [new ChatPhraseSpan(0, 18, phrase.Key)]);
        Span<byte> wire = stackalloc byte[128];
        int size = message.Encode(wire);
        Assert.Equal(new byte[] { 0xFD, 2, 2, 1, 5, 0xFD }, wire[..size].ToArray());
        Assert.Equal("{Synthetic greeting}", ChatText.Decode(wire[..size], catalog));
        Assert.Equal("Synthetic greeting", ChatText.Decode(Encoding.ASCII.GetBytes("Synthetic greeting"), catalog));
    }
    [Fact]
    public void BuildersPreservePhraseBytesAndRejectOverflowBeforeWriting()
    {
        var text = new ChatText("x greeting y", [new(2, 8, 0x05010202)]);
        Span<byte> buffer = stackalloc byte[256];
        int n = ChatOutboundPackets.BuildChatStd(buffer, ChatSendKind.Party, text);
        Assert.Equal(new byte[] { 120, 32, 253, 2, 2, 1, 5, 253, 32, 121, 0 }, buffer.Slice(6, 11).ToArray());
        Assert.Equal(20, n);
        ChatOutboundPackets.BuildChatTell(buffer, "Fixture", text);
        Assert.Equal((byte)253, buffer[23]);
        var full = new ChatText(new string('a', 127));
        Assert.Equal(127, full.Encode(buffer));
        var overflow = new ChatText(new string('a', 128));
        var untouched = new byte[256]; Array.Fill(untouched, (byte)77);
        Assert.Throws<ArgumentException>(() => ChatOutboundPackets.BuildChatStd(untouched, ChatSendKind.Say, overflow));
        Assert.All(untouched, b => Assert.Equal((byte)77, b));
    }
    [Theory]
    [InlineData("@C2B", 1, 0)]
    [InlineData("", 0, 1)]
    public void ReferencesAndBlanksAreNotFabricatedCandidates(string value, int unresolved, int blank)
    {
        var catalog = AutoTranslateCatalog.Parse(Dictionary(value));
        Assert.Empty(catalog.Phrases); Assert.Equal(unresolved, catalog.UnresolvedCount); Assert.Equal(blank, catalog.BlankCount);
    }
    [Fact]
    public void MalformedDictionaryIsRejected()
    {
        var bytes = Dictionary();
        for (int i = 1; i < bytes.Length; i++)
            Assert.Throws<System.IO.InvalidDataException>(() => AutoTranslateCatalog.Parse(bytes.AsSpan(0, i)));
        bytes[80] = 0;
        Assert.Throws<System.IO.InvalidDataException>(() => AutoTranslateCatalog.Parse(bytes));
    }

    [Fact]
    public void IndirectReferencesResolveByHexResourceIndexNotPhraseIndex()
    {
        var catalog = AutoTranslateCatalog.Parse(Dictionary("@C2B"), (kind, id) => kind == 'C' && id == 43 ? "Fixture spell" : null);
        Assert.Equal("Fixture spell", Assert.Single(catalog.Phrases).Label);
        Assert.Equal(0, catalog.UnresolvedCount);
        Assert.Empty(AutoTranslateCatalog.Parse(Dictionary("@Cbad!"), (_, _) => "fake").Phrases);
    }

    [Fact]
    public void IncomingLanguageUsesLocalLabelAndMalformedMarkersStayVisible()
    {
        var catalog = AutoTranslateCatalog.Parse(Dictionary());
        Assert.Equal("{Synthetic greeting}", ChatText.Decode(new byte[] {253, 2, 1, 1, 5, 253}, catalog));
        Assert.Equal("{Unknown phrase}", ChatText.Decode(new byte[] {253, 7, 2, 1, 5, 253}, catalog));
        Assert.Equal("[invalid phrase]x", ChatText.Decode(new byte[] {253, 120}, catalog));
        Assert.Throws<ArgumentException>(() => new ChatText("bad", [new(0, 3, 0x05000202)]));
    }

    [Fact]
    public async System.Threading.Tasks.Task PacketModuleSyntheticLoopbackResolvesIncomingPhrase()
    {
        var catalog = AutoTranslateCatalog.Parse(Dictionary());
        var dispatcher = new PacketDispatcher();
        var module = new ChatPacketModule((packet, reliable) =>
        {
            Assert.True(reliable);
            byte[] payload = new byte[32];
            payload[0] = (byte)ChatMessageType.Party;
            packet.Span.Slice(6, 6).CopyTo(payload.AsSpan(19));
            dispatcher.Dispatch(new PacketHeader(0x017, payload.Length + 4, 1), payload);
            return System.Threading.Tasks.Task.CompletedTask;
        }) { AutoTranslate = catalog };
        module.Register(dispatcher);
        ChatMessage? received = null;
        module.ChatMessageReceived += value => received = value;
        await module.SendChatAsync(ChatSendKind.Party, new ChatText("Synthetic greeting", [new(0, 18, 0x05010202)]));
        Assert.NotNull(received); Assert.True(received.HasAutoTranslate);
        Assert.Equal("{Synthetic greeting}", received.Message);
    }
    [Fact]
    public void MultibyteBudgetAndTypedBraceTextNeverMakeTokens()
    {
        Span<byte> buffer = stackalloc byte[256];
        Assert.Equal(127, new ChatText(new string('あ', 63) + "a").Encode(buffer));
        Assert.Throws<ArgumentException>(() => new ChatText(new string('あ', 64)).Encode(new byte[256]));
        int n = new ChatText("{Synthetic greeting}").Encode(buffer);
        Assert.DoesNotContain((byte)253, buffer[..n].ToArray());
    }

    [Fact]
    public void PlainCp932PrivateUseCannotInjectWireMarkers()
    {
        Assert.Throws<ArgumentException>(() => new ChatText("\uF8F1").Encode(new byte[128]));
    }

    [Fact]
    public void ZeroGroupAndDuplicateCategoriesAreRejected()
    {
        var bytes = Dictionary(); bytes[2] = bytes[78] = 0;
        Assert.Throws<System.IO.InvalidDataException>(() => AutoTranslateCatalog.Parse(bytes));
        var valid = Dictionary();
        Assert.Throws<System.IO.InvalidDataException>(() => AutoTranslateCatalog.Parse(valid.Concat(valid).ToArray()));
    }

    [Fact]
    public void WireFieldReservesOneOfIts128BytesForTerminator()
    {
        Assert.Throws<ArgumentException>(() => new ChatText(new string('a', 128)).Encode(new byte[128]));
    }
}
