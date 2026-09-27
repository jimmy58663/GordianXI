// tests/Gordian.Core.Tests/Ui/StockUiChatTests.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Gordian.Core.Actions;
using Gordian.Core.Input;
using Gordian.Core.Network;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources.Ui;
using Gordian.Core.Ui;
using Xunit;

namespace Gordian.Core.Tests.Ui
{
    public class StockUiChatTests
    {
        /// <summary>A font whose printable glyphs are all <paramref name="advance"/> pixels wide.</summary>
        private static UiFont FixedFont(short advance = 6)
        {
            var images = new List<UiImage>();
            for (int c = 0x20; c < 0x7F; c++)
            {
                images.Add(new UiImage
                {
                    Parts = new[]
                    {
                        new UiSpritePart
                        {
                            TopLeft = new UiPoint(0, 0), TopRight = new UiPoint(advance, 0),
                            BottomLeft = new UiPoint(0, 12), BottomRight = new UiPoint(advance, 12),
                        },
                    },
                });
            }
            return new UiFont(new UiElementGroup { Name = "fontshp", Images = images });
        }

        private static List<string> Texts(StockUiChatLog log, int window, int max = 100)
        {
            var lines = new List<ChatLogLine>();
            log.CopyVisible(window, max, lines);
            return lines.Select(l => l.Text).ToList();
        }

        [Fact]
        public void Log_ShowsNewestLinesAndScrollsBack()
        {
            var log = new StockUiChatLog();
            for (int i = 1; i <= 10; i++) log.Add(ChatLogChannel.Say, $"line {i}");

            Assert.Equal(new[] { "line 8", "line 9", "line 10" }, Texts(log, 1, 3));

            log.Scroll(1, 2);
            Assert.Equal(new[] { "line 6", "line 7", "line 8" }, Texts(log, 1, 3));

            // A scrolled-back window keeps its lines in view while new ones arrive.
            log.Add(ChatLogChannel.Say, "line 11");
            Assert.Equal(new[] { "line 6", "line 7", "line 8" }, Texts(log, 1, 3));

            log.Scroll(1, -100);
            Assert.Equal(0, log.ScrollOffset(1));
            Assert.Equal(new[] { "line 9", "line 10", "line 11" }, Texts(log, 1, 3));

            log.Scroll(1, 1000);
            Assert.Equal(new[] { "line 1" }, Texts(log, 1, 3));
        }

        [Fact]
        public void Log_SplitsCombatIntoWindow2OnlyWithMultiWindow()
        {
            var log = new StockUiChatLog();
            log.Add(new ChatLogLine(ChatLogChannel.Say, "hello", new DateTime(2026, 9, 26, 12, 0, 0)));
            log.Add(new ChatLogLine(ChatLogChannel.Combat, "hit", new DateTime(2026, 9, 26, 12, 0, 1)));
            log.Add(new ChatLogLine(ChatLogChannel.Party, "party", new DateTime(2026, 9, 26, 12, 0, 2)));

            // One window: everything in arrival order, Window 2 empty.
            Assert.Equal(new[] { "hello", "hit", "party" }, Texts(log, 1));
            Assert.Empty(Texts(log, 2));

            log.MultiWindow = true;
            Assert.Equal(new[] { "hello", "party" }, Texts(log, 1));
            Assert.Equal(new[] { "hit" }, Texts(log, 2));
        }

        [Fact]
        public void Log_KeepsItsCapacity()
        {
            var log = new StockUiChatLog();
            for (int i = 0; i < StockUiChatLog.Capacity + 50; i++) log.Add(ChatLogChannel.Say, i.ToString());
            Assert.Equal(StockUiChatLog.Capacity, log.Count(1));
            Assert.Equal("50", Texts(log, 1, StockUiChatLog.Capacity)[0]);
        }

        [Fact]
        public void Wrap_BreaksAtSpacesAndSplitsLongWords()
        {
            var font = FixedFont(6);
            // 10 characters fit in 60 px.
            Assert.Equal(new[] { "hello", "world" }, StockUiChatLog.Wrap(c => font.GetAdvance(c), "hello world", 60));
            Assert.Equal(new[] { "abcdefghij", "klm" }, StockUiChatLog.Wrap(c => font.GetAdvance(c), "abcdefghijklm", 60));
            Assert.Equal(new[] { "short" }, StockUiChatLog.Wrap(c => font.GetAdvance(c), "short", 60));
            Assert.Equal(new[] { "a b c d e", "f" }, StockUiChatLog.Wrap(c => font.GetAdvance(c), "a b c d e f", 60));
        }

        [Fact]
        public void Timestamps_FollowTheConfigOption()
        {
            var line = new ChatLogLine(ChatLogChannel.Say, "hi", new DateTime(2026, 9, 26, 7, 5, 9));
            Assert.Equal("hi", StockUiChatLog.WithTimestamp(line, 0));
            Assert.Equal("[07:05] hi", StockUiChatLog.WithTimestamp(line, 1));
            Assert.Equal("[07:05:09] hi", StockUiChatLog.WithTimestamp(line, 2));
        }

        [Fact]
        public void Input_TypesEditsAndSubmits()
        {
            var input = new StockUiChatInput();
            var sent = new List<(string, ChatInputMode)>();
            input.Submitted += (line, mode) => sent.Add((line, mode));

            input.InsertText("ignored while closed");
            Assert.False(input.HandleKey(GordianKey.A, InputModifiers.None));

            input.Open();
            input.InsertText("helo");
            input.HandleKey(GordianKey.Left, InputModifiers.None);
            input.InsertText("l");
            Assert.Equal("hello", input.Text);
            input.HandleKey(GordianKey.End, InputModifiers.None);
            input.HandleKey(GordianKey.Backspace, InputModifiers.None);
            input.HandleKey(GordianKey.Home, InputModifiers.None);
            input.HandleKey(GordianKey.Delete, InputModifiers.None);
            Assert.Equal("ell", input.Text);

            // Characters the stock font has no glyph for are dropped.
            input.InsertText("あ!");
            Assert.Equal("!ell", input.Text);

            Assert.True(input.HandleKey(GordianKey.Enter, InputModifiers.None));
            Assert.False(input.IsOpen);
            Assert.Equal(new[] { ("!ell", ChatInputMode.Say) }, sent);
        }

        [Fact]
        public void Input_SwallowsTheOpeningSpaceAndLimitsLength()
        {
            var input = new StockUiChatInput();
            input.Open(" ");
            input.InsertText(" ");
            Assert.Equal(string.Empty, input.Text);
            input.InsertText(new string('x', StockUiChatInput.MaxLength + 10));
            Assert.Equal(StockUiChatInput.MaxLength, input.Text.Length);

            // Escape drops the line without sending it.
            bool sent = false;
            input.Submitted += (_, _) => sent = true;
            input.HandleKey(GordianKey.Escape, InputModifiers.None);
            Assert.False(input.IsOpen);
            Assert.False(sent);
        }

        [Fact]
        public void Input_RecallsHistory()
        {
            var input = new StockUiChatInput();
            foreach (var line in new[] { "one", "two" })
            {
                input.Open();
                input.InsertText(line);
                input.HandleKey(GordianKey.Enter, InputModifiers.None);
            }

            input.Open();
            input.HandleKey(GordianKey.Up, InputModifiers.None);
            Assert.Equal("two", input.Text);
            input.HandleKey(GordianKey.Up, InputModifiers.None);
            Assert.Equal("one", input.Text);
            input.HandleKey(GordianKey.Up, InputModifiers.None);
            Assert.Equal("one", input.Text);
            input.HandleKey(GordianKey.Down, InputModifiers.None);
            input.HandleKey(GordianKey.Down, InputModifiers.None);
            Assert.Equal(string.Empty, input.Text);
        }

        [Fact]
        public void Input_ModeListPicksTheChatMode()
        {
            var input = new StockUiChatInput();
            input.Open();
            input.HandleKey(GordianKey.Tab, InputModifiers.None);
            Assert.True(input.IsModeListOpen);
            Assert.Equal(0, input.ModeListIndex);

            // Up wraps to the last mode, which scrolls the eight-row list.
            input.HandleKey(GordianKey.Up, InputModifiers.None);
            Assert.Equal(input.ListEntries.Count - 1, input.ModeListIndex);
            Assert.Equal(ChatInputMode.Window1, input.ListEntries[input.ModeListIndex]);
            Assert.Equal(input.ListEntries.Count - StockUiChatInput.ModeListRows, input.ModeListFirstRow);
            for (int i = 0; i < 4; i++) input.HandleKey(GordianKey.Down, InputModifiers.None); // wraps to Say, then down to Party
            Assert.Equal(0, input.ModeListFirstRow);
            Assert.Equal(ChatInputMode.Party, input.ListEntries[input.ModeListIndex]);

            // While the list is open typing does nothing; Enter picks and keeps the line open.
            input.InsertText("x");
            input.HandleKey(GordianKey.Enter, InputModifiers.None);
            Assert.False(input.IsModeListOpen);
            Assert.True(input.IsOpen);
            Assert.Equal(ChatInputMode.Party, input.Mode);
            Assert.Equal(string.Empty, input.Text);

            // Tab with text typed does not open the list.
            input.InsertText("a");
            input.HandleKey(GordianKey.Tab, InputModifiers.None);
            Assert.False(input.IsModeListOpen);
        }

        [Fact]
        public void Input_TypesKeySymbolsOnceWhateverThePlatformSends()
        {
            var input = new StockUiChatInput();
            input.Open();
            // Key symbol only (the handled key press suppressed the text input) ...
            input.InsertKeySymbol("h");
            input.InsertKeySymbol("i");
            // ... or key symbol followed by the platform's text input for the same key.
            input.InsertKeySymbol("!");
            input.InsertText("!");
            input.InsertKeySymbol("!");
            Assert.Equal("hi!!", input.Text);
        }

        [Fact]
        public void Input_SelectsALogWindowToScroll()
        {
            var input = new StockUiChatInput();
            var scrolls = new List<(int Window, int Amount, bool Pages)>();
            input.ScrollRequested += (w, a, p) => scrolls.Add((w, a, p));
            input.Open();
            Assert.DoesNotContain(ChatInputMode.Window2, input.ListEntries);
            input.HasWindow2 = true;
            input.HandleKey(GordianKey.Tab, InputModifiers.None);
            input.HandleKey(GordianKey.Up, InputModifiers.None); // wraps to the last entry, Window 2
            input.HandleKey(GordianKey.Enter, InputModifiers.None);
            Assert.Equal(2, input.SelectedLogWindow);
            Assert.Equal(ChatInputMode.Say, input.Mode);

            input.HandleKey(GordianKey.Up, InputModifiers.None);
            input.HandleKey(GordianKey.PageDown, InputModifiers.None);
            input.InsertText("x"); // no typing while a window is selected
            Assert.Equal(new[] { (2, 1, false), (2, -1, true) }, scrolls);
            Assert.Equal(string.Empty, input.Text);

            // Cancel (Escape, or B on a gamepad) backs out one step at a time.
            input.Cancel();
            Assert.Equal(0, input.SelectedLogWindow);
            Assert.True(input.IsOpen);
            input.Cancel();
            Assert.False(input.IsOpen);
        }

        [Fact]
        public void Chat_TheSlashKeyStartsTheLineWithASlash()
        {
            var chat = new StockUiChat();
            Assert.True(chat.TryOpen(GordianKey.OemSlash, InputModifiers.None, InputProfile.CreateCompact(), menuOpen: false, keySymbol: "/"));
            chat.Input.InsertText("/"); // the platform's text input, if it follows, is not typed twice
            chat.Input.InsertKeySymbol("p");
            Assert.Equal("/p", chat.Input.Text);
        }

        [Fact]
        public void Chat_OpensOnEnterAndTheChatKey()
        {
            var chat = new StockUiChat();
            var profile = InputProfile.CreateCompact();

            Assert.False(chat.TryOpen(GordianKey.Enter, InputModifiers.None, profile, menuOpen: true));
            Assert.False(chat.TryOpen(GordianKey.W, InputModifiers.None, profile, menuOpen: false));
            Assert.True(chat.TryOpen(GordianKey.OemSlash, InputModifiers.None, profile, menuOpen: false));
            Assert.True(chat.Input.IsOpen);
            chat.Input.Close();

            Assert.True(chat.TryOpen(GordianKey.Enter, InputModifiers.None, profile, menuOpen: false));
            chat.Input.Close();

            // Full layout: Space opens the line and is not typed into it.
            Assert.True(chat.TryOpen(GordianKey.Space, InputModifiers.None, InputProfile.CreateFullNumpad(), menuOpen: false));
            chat.Input.InsertText(" ");
            Assert.Equal(string.Empty, chat.Input.Text);
        }

        [Theory]
        [InlineData(ChatMessageType.Say, "Cybin : hello")]
        [InlineData(ChatMessageType.Shout, "Cybin : hello")]
        [InlineData(ChatMessageType.Tell, "Cybin>> hello")]
        [InlineData(ChatMessageType.Party, "(Cybin) hello")]
        [InlineData(ChatMessageType.Linkshell, "<Cybin> hello")]
        [InlineData(ChatMessageType.Linkshell2, "[2]<Cybin> hello")]
        [InlineData(ChatMessageType.Unity, "{Cybin} hello")]
        [InlineData(ChatMessageType.Emotion, "hello")]
        public void FormatsIncomingLinesLikeRetail(ChatMessageType type, string expected)
        {
            Assert.Equal(expected, StockUiChat.FormatIncoming(type, "Cybin", "hello"));
        }

        [Fact]
        public void Chat_AppliesTheClientChatFilters()
        {
            var chat = new StockUiChat { ClientChatFilters = () => StockUiConfigPages.ClientChatFilterParty };
            chat.OnChatMessage(new ChatMessage(ChatMessageType.Party, "Cybin", "hidden", 0, false, 0, 0, DateTime.UtcNow, false));
            chat.OnChatMessage(new ChatMessage(ChatMessageType.Tell, "Cybin", "shown", 0, false, 0, 0, DateTime.UtcNow, false));
            Assert.Equal(new[] { "Cybin>> shown" }, Texts(chat.Log, 1));
            // A tell makes its sender the Tell mode's recipient.
            Assert.Equal("Cybin", chat.Input.TellTarget);
        }

        [Fact]
        public async Task Chat_SendsThroughTheDispatcherAndEchoes()
        {
            var calls = new List<(string Line, ChatSendKind Kind)>();
            var chat = new StockUiChat
            {
                CharacterName = () => "Gordian",
                Execute = (line, kind) =>
                {
                    calls.Add((line, kind));
                    return Task.FromResult(line.StartsWith("/pos")
                        ? PlayerActionResult.Info("Position: 1, 2, 3")
                        : line.StartsWith("/bogus") ? PlayerActionResult.Warn("Unknown command.") : PlayerActionResult.Ok("sent"));
                },
            };

            await chat.SubmitAsync("hello", ChatInputMode.Party);
            await chat.SubmitAsync("/l hi all", ChatInputMode.Say);
            await chat.SubmitAsync("/t Cybin psst", ChatInputMode.Say);
            await chat.SubmitAsync("/pos", ChatInputMode.Say);
            await chat.SubmitAsync("/bogus", ChatInputMode.Say);
            await chat.SubmitAsync("again", ChatInputMode.Tell);
            await chat.SubmitAsync("/p", ChatInputMode.Say); // a channel command with no text sends nothing

            Assert.Equal(("hello", ChatSendKind.Party), calls[0]);
            Assert.Equal(("/tell Cybin again", ChatSendKind.Say), calls[5]);
            Assert.Equal(6, calls.Count);

            var lines = new List<ChatLogLine>();
            chat.Log.CopyVisible(1, 100, lines);
            Assert.Equal(new[]
            {
                "(Gordian) hello", "<Gordian> hi all", ">>Cybin : psst", "Position: 1, 2, 3", "Unknown command.", ">>Cybin : again",
            }, lines.Select(l => l.Text));
            Assert.Equal(ChatLogChannel.Party, lines[0].Channel);
            Assert.Equal(ChatLogChannel.Notice, lines[3].Channel);
            Assert.Equal(ChatLogChannel.Error, lines[4].Channel);
        }

        [Fact]
        public async Task Chat_TellModeNeedsARecipient()
        {
            int calls = 0;
            var chat = new StockUiChat { Execute = (_, _) => { calls++; return Task.FromResult(PlayerActionResult.Ok("sent")); } };
            await chat.SubmitAsync("hello?", ChatInputMode.Tell);
            Assert.Equal(0, calls);
            Assert.Equal(ChatLogChannel.Error, Assert.Single(GetLines(chat.Log)).Channel);
        }

        [Fact]
        public void Chat_PageKeysScrollTheLogWhileTyping()
        {
            var chat = new StockUiChat { PageSize = _ => 4 };
            for (int i = 0; i < 20; i++) chat.Log.Add(ChatLogChannel.Say, i.ToString());
            chat.Input.Open();
            chat.Input.HandleKey(GordianKey.PageUp, InputModifiers.None);
            Assert.Equal(3, chat.Log.ScrollOffset(1));
            chat.Input.HandleKey(GordianKey.PageDown, InputModifiers.None);
            Assert.Equal(0, chat.Log.ScrollOffset(1));

            // Closing the line returns the log to the newest lines.
            chat.Input.HandleKey(GordianKey.PageUp, InputModifiers.None);
            chat.Input.HandleKey(GordianKey.Escape, InputModifiers.None);
            Assert.Equal(0, chat.Log.ScrollOffset(1));
        }

        [Fact]
        public async Task Chat_ChatModeCommandSetsTheDefaultMode()
        {
            var calls = new List<(string Line, ChatSendKind Kind)>();
            var chat = new StockUiChat
            {
                CharacterName = () => "Gordian",
                Execute = (line, kind) => { calls.Add((line, kind)); return Task.FromResult(PlayerActionResult.Ok("sent")); },
            };

            await chat.SubmitAsync("/chatmode party", ChatInputMode.Say);
            Assert.Equal(ChatInputMode.Party, chat.Input.Mode);
            await chat.SubmitAsync("/cm tell Cybin", ChatInputMode.Party);
            Assert.Equal(ChatInputMode.Tell, chat.Input.Mode);
            Assert.Equal("Cybin", chat.Input.TellTarget);
            await chat.SubmitAsync("/cm", ChatInputMode.Tell);
            Assert.Equal("Current chat mode: Tell (Cybin).", GetLines(chat.Log)[^1].Text);
            Assert.Empty(calls); // handled by the chat itself

            // Shout lasts one line.
            await chat.SubmitAsync("/cm shout", chat.Input.Mode);
            await chat.SubmitAsync("anyone?", chat.Input.Mode);
            Assert.Equal(("anyone?", ChatSendKind.Shout), calls[0]);
            Assert.Equal(ChatInputMode.Say, chat.Input.Mode);
        }

        [Fact]
        public void Router_ParsesUnityAndAssistChannels()
        {
            Assert.Equal(ChatSendKind.Unity, ChatCommandRouter.Parse("/u hi").SpeechKind);
            Assert.Equal(ChatSendKind.AssistJ, ChatCommandRouter.Parse("/assistj hi").SpeechKind);
            Assert.Equal(ChatSendKind.AssistE, ChatCommandRouter.Parse("/assiste hi").SpeechKind);
        }

        private static List<ChatLogLine> GetLines(StockUiChatLog log)
        {
            var lines = new List<ChatLogLine>();
            log.CopyVisible(1, 100, lines);
            return lines;
        }
    }
}
