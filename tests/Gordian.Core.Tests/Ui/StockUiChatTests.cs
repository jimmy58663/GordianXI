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

        private static ChatMessage Incoming(ChatMessageType type, string sender, string text, byte attr = 0,
            ChatFormattedMessage? formatted = null) =>
            new(type, sender, text, 0, false, 0, 0, DateTime.UtcNow, false, attr, formatted);

        /// <summary>
        /// Retail heads every kind 6 / 7 system message with a banner line (retail Windower log, Gemini 2026-10-03
        /// 17:09:09: "----== SystemMessage ==----" then "Key item 3 was given to Gemini."), the sender not shown.
        /// </summary>
        [Theory]
        [InlineData(ChatMessageType.System1)]
        [InlineData(ChatMessageType.System2)]
        public void Chat_HeadsSystemMessagesWithTheRetailBanner(ChatMessageType type)
        {
            var chat = new StockUiChat();
            chat.OnChatMessage(Incoming(type, "Gemini", "Key item 3 was given to Gemini."));
            chat.OnChatMessage(Incoming(type, "Gemini", "God Mode enabled."));
            Assert.Equal(new[]
            {
                "----== SystemMessage ==----", "Key item 3 was given to Gemini.",
                "----== SystemMessage ==----", "God Mode enabled.",
            }, Texts(chat.Log, 1));
        }

        /// <summary>Kind 29 ("Basic system messages": "Cybin now has learned 25 of 835 spells.") has no banner in retail.</summary>
        [Theory]
        [InlineData(ChatMessageType.System3)]
        [InlineData(ChatMessageType.StandardMessage17)]
        [InlineData(ChatMessageType.StandardMessage32)]
        [InlineData(ChatMessageType.Say)]
        public void Chat_PrintsOtherKindsWithoutTheBanner(ChatMessageType type)
        {
            var chat = new StockUiChat();
            chat.OnChatMessage(Incoming(type, string.Empty, "Cybin now has learned 25 of 835 spells."));
            Assert.Equal(new[] { "Cybin now has learned 25 of 835 spells." }, Texts(chat.Log, 1));
        }

        [Theory]
        [InlineData(ChatMessageType.SayCopy24)]
        [InlineData(ChatMessageType.SayCopy25)]
        public void Chat_PrintsTheSayCopiesAsSay(ChatMessageType type)
        {
            Assert.Equal(ChatLogChannel.Say, StockUiChat.ChannelOf(type));
            Assert.Equal("Cybin : hello", StockUiChat.FormatIncoming(type, "Cybin", "hello"));
        }

        [Fact]
        public void Chat_PrintsADatReferenceThroughTheResolver()
        {
            var reference = new ChatFormattedMessage(ChatFormattedTable.UnityMess, 0x1EF, 10, 0, 0, 0, 0);
            ChatMessage? asked = null;
            var chat = new StockUiChat
            {
                FormattedMessageResolver = msg =>
                {
                    asked = msg;
                    return new[] { "{Yoran-Oran} Our field researchers..." };
                },
            };
            chat.OnChatMessage(Incoming(ChatMessageType.Unity, string.Empty, "0a,01ef,0000000a,", S2C_0x017_ChatStd.AttrFormatted, reference));
            Assert.Equal(reference, asked?.Formatted);
            Assert.Equal(new[] { "{Yoran-Oran} Our field researchers..." }, Texts(chat.Log, 1));
        }

        /// <summary>A reference whose table is not read, or that cannot be parsed, is dropped: retail never prints the value list.</summary>
        [Fact]
        public void Chat_DropsADatReferenceItCannotFormat()
        {
            var chat = new StockUiChat { FormattedMessageResolver = _ => null };
            var reference = new ChatFormattedMessage(ChatFormattedTable.TrustMess, 5, 0, 0, 0, 0, 0);
            chat.OnChatMessage(Incoming(ChatMessageType.System1, string.Empty, "09,0005,", S2C_0x017_ChatStd.AttrFormatted, reference));
            chat.OnChatMessage(Incoming(ChatMessageType.Say, "Cybin", "garbage", S2C_0x017_ChatStd.AttrFormatted));
            Assert.Empty(Texts(chat.Log, 1));
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
        public void Chat_KeypadPlusCyclesTheLogWindows()
        {
            var chat = new StockUiChat();
            for (int i = 0; i < 20; i++) chat.Log.Add(ChatLogChannel.Say, i.ToString());

            chat.CycleLogWindow();
            Assert.Equal(1, chat.SelectedLogWindow);
            chat.CycleLogWindow(); // one window: back to none
            Assert.Equal(0, chat.SelectedLogWindow);

            chat.SetMultiWindow(true);
            chat.CycleLogWindow();
            chat.CycleLogWindow();
            Assert.Equal(2, chat.SelectedLogWindow);
            chat.CycleLogWindow();
            Assert.Equal(0, chat.SelectedLogWindow);

            // Releasing a window returns it to the newest lines.
            chat.CycleLogWindow();
            chat.Log.Scroll(1, 5);
            chat.ReleaseLogWindow();
            Assert.Equal(0, chat.SelectedLogWindow);
            Assert.Equal(0, chat.Log.ScrollOffset(1));

            // Window 2 goes away with the split.
            chat.CycleLogWindow();
            chat.CycleLogWindow();
            chat.SetMultiWindow(false);
            Assert.Equal(0, chat.SelectedLogWindow);
        }

        [Fact]
        public void Chat_KeypadPlusThenSelectsTheStatusIcons_AndConfirmCancelsOne()
        {
            var ids = new List<ushort> { 40, 41, 42, 43, 44, 45, 46, 47, 48, 49, 50 };
            var cancelled = new List<ushort>();
            var chat = new StockUiChat { StatusIds = () => ids, CancelStatus = cancelled.Add };

            chat.CycleLogWindow();                     // Window 1
            Assert.Equal(1, chat.SelectedLogWindow);
            Assert.Equal(-1, chat.SelectedStatusIcon);
            chat.CycleLogWindow();                     // one window: the status icons follow
            Assert.Equal(0, chat.SelectedLogWindow);
            Assert.Equal(0, chat.SelectedStatusIcon);
            Assert.True(chat.IsSelecting);

            chat.MoveStatusSelection(-1, 0);           // left from the first wraps to the last
            Assert.Equal(10, chat.SelectedStatusIcon);
            chat.MoveStatusSelection(1, 0);
            Assert.Equal(0, chat.SelectedStatusIcon);
            chat.MoveStatusSelection(0, 1);            // down a row of nine
            Assert.Equal(9, chat.SelectedStatusIcon);
            chat.MoveStatusSelection(0, 1);            // no third row: stays
            Assert.Equal(9, chat.SelectedStatusIcon);

            Assert.Equal((ushort)49, chat.ConfirmStatusSelection());
            Assert.Equal(new ushort[] { 49 }, cancelled);
            ids.RemoveAt(9);
            ids.RemoveAt(9);                           // the cursor stays on the icons that are left
            Assert.Equal(8, chat.SelectedStatusIcon);

            chat.CycleLogWindow();                     // after the icons: none
            Assert.False(chat.IsSelecting);

            // Without status effects the cycle skips the icons.
            ids.Clear();
            chat.CycleLogWindow();
            chat.CycleLogWindow();
            Assert.False(chat.IsSelecting);

            // Cancel ends the selection.
            ids.Add(7);
            chat.CycleLogWindow();
            chat.CycleLogWindow();
            Assert.Equal(0, chat.SelectedStatusIcon);
            chat.ReleaseLogWindow();
            Assert.False(chat.IsSelecting);
        }

        [Fact]
        public void SavedProfilesMoveTheDPadToTheTargetCursor()
        {
            var old = InputProfile.CreateCompact();
            old.Bindings.Remove(InputAction.TargetCursorLeft);
            old.Bindings.Remove(InputAction.TargetCursorRight);
            old.Bind(InputAction.TargetPrevious, new InputChord(GamepadButton.DPadLeft));
            old.Bind(InputAction.TargetNearest, new InputChord(GamepadButton.DPadRight));

            var loaded = InputProfile.FromJson(old.SaveToJson());
            Assert.True(loaded.TryGetAction(new InputChord(GamepadButton.DPadLeft), out var left));
            Assert.Equal(InputAction.TargetCursorLeft, left);
            Assert.True(loaded.TryGetAction(new InputChord(GamepadButton.DPadRight), out var right));
            Assert.Equal(InputAction.TargetCursorRight, right);
            Assert.True(loaded.TryGetAction(new InputChord(GordianKey.Tab), out var tab));
            Assert.Equal(InputAction.TargetNearest, tab);
        }

        [Fact]
        public void SavedProfilesGetTheLogWindowKeys()
        {
            var old = InputProfile.CreateFullNumpad();
            old.Bindings.Remove(InputAction.CycleLogWindow);
            old.Bind(InputAction.TargetNearest, new InputChord(GordianKey.NumPadAdd));
            old.Bind(InputAction.ToggleAutorun, new InputChord(GamepadButton.Y));

            var loaded = InputProfile.FromJson(old.SaveToJson());
            Assert.True(loaded.TryGetAction(new InputChord(GordianKey.NumPadAdd), out var key));
            Assert.Equal(InputAction.CycleLogWindow, key);
            Assert.True(loaded.TryGetAction(new InputChord(GamepadButton.Y), out var pad));
            Assert.Equal(InputAction.CycleLogWindow, pad);
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
        [Fact]
        public void OpenInputInMode_OpensTheLineInThatMode_ShoutOnlyTypesTheCommand()
        {
            var chat = new StockUiChat();
            chat.Input.TellTarget = "Cybin";
            chat.OpenInputInMode(ChatInputMode.Tell);
            Assert.True(chat.Input.IsOpen);
            Assert.Equal(ChatInputMode.Tell, chat.Input.Mode);
            Assert.Equal(string.Empty, chat.Input.Text);
            chat.Input.Cancel();

            chat.OpenInputInMode(ChatInputMode.Say);
            Assert.Equal(ChatInputMode.Say, chat.Input.Mode);
            chat.Input.Cancel();

            chat.OpenInputInMode(ChatInputMode.Shout);
            Assert.True(chat.Input.IsOpen);
            Assert.Equal(ChatInputMode.Say, chat.Input.Mode); // the default mode is left alone
            Assert.Equal("/sh ", chat.Input.Text);
        }
    }
}
