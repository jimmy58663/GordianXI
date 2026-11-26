using System;
using System.Buffers.Binary;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Gordian.Core.Network;
using Gordian.Core.Resources;
using Gordian.Engine.Actions;
using Gordian.Engine.Ui;
using Xunit;

namespace Gordian.Engine.Tests.Ui;

public class PhraseCommandTests
{
    internal static AutoTranslateCatalog Catalog(params string[] labels)
    {
        var data = new byte[76 + labels.Sum(s => s.Length + 6)];
        data[0] = data[1] = 2; data[2] = 13;
        Encoding.ASCII.GetBytes("Fixture commands").CopyTo(data, 4);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(68), (uint)labels.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(72), (uint)(data.Length - 76));
        int at = 76;
        for (int i = 0; i < labels.Length; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at), Key(i));
            data[at + 4] = (byte)(labels[i].Length + 1);
            Encoding.ASCII.GetBytes(labels[i]).CopyTo(data, at + 5);
            at += labels[i].Length + 6;
        }
        return AutoTranslateCatalog.Parse(data);
    }
    internal static uint Key(int i = 0) => (uint)((i + 1) << 24) | 0x000d0202;

    [Theory]
    [InlineData("/party")]
    [InlineData("/p")]
    [InlineData("/tell")]
    [InlineData("/echo")]
    public async Task CommandPhrasePreservesBinaryBodyAndLiteralRecipient(string verb)
    {
        var chat = new StockUiChat(); chat.Input.Catalog = Catalog(verb, "Synthetic body");
        ChatText? sent = null; ChatCommandResult? routed = null;
        chat.ExecuteRich = (parsed, body) => { routed = parsed; sent = body; return Task.FromResult(PlayerActionResult.Ok("")); };
        string prefix = verb + (verb == "/tell" ? " Fixture " : " ");
        var line = new ChatText(prefix + "Synthetic body", [new(0, verb.Length, Key()), new(prefix.Length, 14, Key(1))]);
        await chat.SubmitAsync(line, ChatInputMode.Say);
        if (verb == "/echo") { Assert.Null(sent); var rows = new System.Collections.Generic.List<ChatLogLine>(); chat.Log.CopyTab(StockUiLogTab.Window1, 0, 10, rows); Assert.Equal("{Synthetic body}", rows.Last().Text); return; }
        Assert.NotNull(sent); Assert.Equal("Synthetic body", sent.Text);
        var bytes = new byte[128]; int count = sent.Encode(bytes);
        Assert.Equal(new byte[] {253, 2, 2, 13, 2, 253}, bytes.AsSpan(0, count).ToArray());
        if (verb == "/tell") Assert.Equal("Fixture", routed!.Recipient);
    }

    [Theory]
    [InlineData("/party /shutdown", 7, 9, true)]
    [InlineData("hello /shutdown", 6, 9, true)]
    [InlineData("/shutdownExtra", 0, 9, true)]
    [InlineData("/shutdown", 0, 5, true)]
    [InlineData("/tell /shutdown body", 6, 9, false)]
    [InlineData("/ma /shutdown <me>", 4, 9, false)]
    public async Task NonCommandPositionCannotExecute(string text, int start, int length, bool chatExpected)
    {
        var chat = new StockUiChat(); chat.Input.Catalog = Catalog("/shutdown");
        int actions = 0; ChatText? sent = null;
        chat.Execute = (_, _) => { actions++; return Task.FromResult(PlayerActionResult.Ok("")); };
        chat.ExecuteRich = (_, body) => { sent = body; return Task.FromResult(PlayerActionResult.Ok("")); };
        await chat.SubmitAsync(new ChatText(text, [new(start, length, Key())]), ChatInputMode.Party);
        Assert.Equal(0, actions); Assert.Equal(chatExpected, sent != null);
        if (sent != null) Assert.Single(sent.Phrases);
    }

    [Theory]
    [InlineData("/logout")]
    [InlineData("!fixture")]
    public async Task MismatchedCanonicalIdentityNeverExecutes(string text)
    {
        var chat = new StockUiChat(); chat.Input.Catalog = Catalog("/shutdown");
        int actions = 0; ChatText? sent = null;
        chat.Execute = (_, _) => { actions++; return Task.FromResult(PlayerActionResult.Ok("")); };
        chat.ExecuteRich = (_, body) => { sent = body; return Task.FromResult(PlayerActionResult.Ok("")); };
        await chat.SubmitAsync(new ChatText(text, [new(0, text.Length, Key())]), ChatInputMode.Party);
        Assert.Equal(0, actions); Assert.NotNull(sent); Assert.Single(sent.Phrases);
    }

    [Fact]
    public void MultipleMatchesStillRequireSelectionAndNeverSubmit()
    {
        var input = new StockUiChatInput { Catalog = Catalog("/shutdown", "/shout") };
        int sends = 0; input.RichSubmitted += (_, _) => sends++;
        input.Open(); input.InsertText("/sh"); input.HandleKey(Gordian.Engine.Input.GordianKey.Tab, 0);
        Assert.Equal(2, input.Completion.Rows.Count); Assert.Equal("/sh", input.Text);
        input.HandleKey(Gordian.Engine.Input.GordianKey.Enter, 0);
        Assert.Equal("/shutdown", input.Text); Assert.Single(input.RichText.Phrases);
        Assert.True(input.IsOpen); Assert.Equal(0, sends);
    }

    [Theory]
    [InlineData("/shutdown", "")]
    [InlineData("/shutdown", " on")]
    [InlineData("/logout", " off")]
    public async Task ExplicitCanonicalLeadingCommandUsesOrdinaryDispatcher(string verb, string suffix)
    {
        var chat = new StockUiChat(); chat.Input.Catalog = Catalog(verb);
        string? executed = null; int sends = 0;
        chat.Execute = (line, _) => { executed = line; return Task.FromResult(PlayerActionResult.Ok("fixture")); };
        chat.ExecuteRich = (_, _) => { sends++; return Task.FromResult(PlayerActionResult.Ok("")); };
        await chat.SubmitAsync(new ChatText(verb + suffix, [new(0, verb.Length, Key())]), ChatInputMode.Tell);
        Assert.Equal(verb + suffix, executed); Assert.Equal(0, sends);
    }
}
