using System;
using System.Buffers.Binary;
using System.Text;
using Gordian.Core.Resources;
using Gordian.Engine.Ui;
using Gordian.Engine.Input;
using Xunit;
namespace Gordian.Engine.Tests.Ui;
public class AutoTranslateInputTests
{
    internal static AutoTranslateCatalog Catalog()
    {
        var bytes = new byte[101]; bytes[0] = bytes[1] = bytes[76] = bytes[77] = 2;
        bytes[2] = bytes[78] = 1; bytes[79] = 5; bytes[80] = 20;
        Encoding.ASCII.GetBytes("Fixture").CopyTo(bytes, 4);
        Encoding.ASCII.GetBytes("Synthetic greeting!").CopyTo(bytes, 81);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(68), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(72), 25);
        return AutoTranslateCatalog.Parse(bytes);
    }
    [Fact]
    public void TabSelectsWithoutSendAndEditingRemovesIdentity()
    {
        var input = new StockUiChatInput { Catalog = Catalog() };
        int sends = 0; input.Submitted += (_, _) => sends++;
        input.Open(); input.InsertText("/p Syn"); input.HandleKey(GordianKey.Tab, InputModifiers.None);
        Assert.False(input.Completion.IsOpen); // one Tab must accept, not require Enter
        Assert.Equal("/p Synthetic greeting!", input.Text);
        Assert.Single(input.RichText.Phrases); Assert.True(input.IsOpen); Assert.Equal(0, sends);
        input.HandleKey(GordianKey.Backspace, InputModifiers.None);
        Assert.Equal("/p Synthetic greeting", input.Text); Assert.Empty(input.RichText.Phrases);
    }

    [Theory]
    [InlineData("/p Syn", 6)]
    [InlineData("/tell Fixture Syn", 21)]
    public async System.Threading.Tasks.Task SelectedPhraseTravelsThroughRouterAndRealPacketBuilder(string draft, int offset)
    {
        byte[]? sent = null;
        var module = new Gordian.Core.Network.Packets.ChatPacketModule((packet, _) =>
        { sent = packet.ToArray(); return System.Threading.Tasks.Task.CompletedTask; });
        var chat = new StockUiChat(); chat.Input.Catalog = Catalog();
        chat.ExecuteRich = async (parsed, text) =>
        {
            if (parsed.Kind == Gordian.Core.Network.ChatCommandResultKind.SendTell)
                await module.SendTellAsync(parsed.Recipient, text);
            else await module.SendChatAsync(parsed.SpeechKind, text);
            return Gordian.Engine.Actions.PlayerActionResult.Ok("", parsed.Kind);
        };
        chat.Input.Open(); chat.Input.InsertText(draft);
        chat.Input.HandleKey(GordianKey.Tab, InputModifiers.None);
        await chat.SubmitAsync(chat.Input.RichText, ChatInputMode.Say);
        Assert.NotNull(sent);
        Assert.Equal(new byte[] {253, 2, 2, 1, 5, 253}, sent.AsSpan(offset, 6).ToArray());
        Assert.Equal("{Synthetic greeting!}", Gordian.Core.Network.ChatText.Decode(sent.AsSpan(offset), Catalog()));
    }

    [Fact]
    public void HistoryRetainsIdentityButTypingIdenticalTextDoesNot()
    {
        var input = new StockUiChatInput { Catalog = Catalog() };
        input.Open(); input.InsertText("Synthetic gr"); input.HandleKey(GordianKey.Tab, 0);
        input.HandleKey(GordianKey.Enter, 0); input.Open(); input.HandleKey(GordianKey.Up, 0);
        Assert.Single(input.RichText.Phrases);
        input.HandleKey(GordianKey.Home, 0); input.InsertText("x ");
        Assert.Equal(2, Assert.Single(input.RichText.Phrases).Start);
        input.HandleKey(GordianKey.Right, 0); input.HandleKey(GordianKey.Right, 0); input.HandleKey(GordianKey.Right, 0);
        input.InsertText("!"); Assert.Empty(input.RichText.Phrases);
        input.Close(); input.Open(); input.InsertText("Synthetic greeting!"); Assert.Empty(input.RichText.Phrases);
    }
    [Fact]
    public void CategoryCancellationAndNumberEntryPreserveDraft()
    {
        var input = new StockUiChatInput { Catalog = Catalog() };
        input.Open(); input.HandleKey(GordianKey.Tab, 0);
        Assert.Equal(0u, Assert.Single(input.Completion.Rows).Key);
        input.HandleKey(GordianKey.Enter, 0); Assert.NotEqual(0u, Assert.Single(input.Completion.Rows).Key);
        input.HandleKey(GordianKey.Escape, 0); Assert.True(input.IsOpen); Assert.Equal("", input.Text);
        input.InsertText("no match"); input.HandleKey(GordianKey.Tab, 0); Assert.Empty(input.Completion.Rows);
        input.HandleKey(GordianKey.Enter, 0); Assert.Equal("no match", input.Text); Assert.True(input.IsOpen);
        input.OpenNumber(2, _ => { }); input.InsertText("12"); input.HandleKey(GordianKey.Tab, 0);
        Assert.False(input.Completion.IsOpen); Assert.Equal("12", input.Text);
    }
    [Fact]
    public async System.Threading.Tasks.Task ExpansionPreservesPhrasesAndRejectsOversizeOrCommandSyntax()
    {
        var chat = new StockUiChat();
        Gordian.Core.Network.ChatText? sent = null;
        chat.ExecuteRich = (_, text) => { sent = text; return System.Threading.Tasks.Task.FromResult(Gordian.Engine.Actions.PlayerActionResult.Ok("")); };
        chat.ExpandTokens = s => s.Replace("<t>", "Fixture target");
        var rich = new Gordian.Core.Network.ChatText("/p <t> Synthetic greeting!", [new(7, 19, 0x05010202)]);
        await chat.SubmitAsync(rich, ChatInputMode.Say);
        Assert.NotNull(sent); Assert.Equal("Fixture target Synthetic greeting!", sent.Text); Assert.Single(sent.Phrases);
        sent = null; chat.ExpandTokens = _ => new string('x', 129);
        await chat.SubmitAsync(rich, ChatInputMode.Say); Assert.Null(sent);
        await chat.SubmitAsync(new Gordian.Core.Network.ChatText("/tell Fixture body", [new(6, 7, 0x05010202)]), ChatInputMode.Say);
        Assert.Null(sent);
    }

    [Fact]
    public void MissingCatalogExplainsUnavailableInsteadOfInventingCandidates()
    {
        var input = new StockUiChatInput(); input.Open(); input.HandleKey(GordianKey.Tab, 0);
        Assert.Empty(input.Completion.Rows); Assert.Contains("unavailable", input.Completion.Notice);
    }

    [Fact]
    public void QuotedChatPrefixRetainsQuotesAndSuffix()
    {
        var input = new StockUiChatInput { Catalog = Catalog() };
        input.Open(); input.InsertText("/p \"Syn\""); input.HandleKey(GordianKey.Left, 0);
        input.HandleKey(GordianKey.Tab, 0); Assert.False(input.Completion.IsOpen);
        Assert.Equal("/p \"Synthetic greeting!\"", input.Text);
        Assert.Equal(4, Assert.Single(input.RichText.Phrases).Start);
    }

    [Theory]
    [InlineData("/logout")]
    [InlineData("!fixture")]
    public async System.Threading.Tasks.Task LeadingPhraseLabelIsNotCommandSyntax(string label)
    {
        var chat = new StockUiChat();
        Gordian.Core.Network.ChatText? sent = null;
        chat.ExecuteRich = (_, text) => { sent = text; return System.Threading.Tasks.Task.FromResult(Gordian.Engine.Actions.PlayerActionResult.Ok("")); };
        await chat.SubmitAsync(new Gordian.Core.Network.ChatText(label, [new(0, label.Length, 0x05010202)]), ChatInputMode.Party);
        Assert.NotNull(sent); Assert.Single(sent.Phrases);
    }
}
