// src/Gordian.Core/Ui/StockUiChat.cs
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Gordian.Core.Actions;
using Gordian.Core.Diagnostics;
using Gordian.Core.Input;
using Gordian.Core.Network;
using Gordian.Core.Network.Packets;
using Gordian.Core.World;

namespace Gordian.Core.Ui
{
    /// <summary>
    /// The stock chat for one session: fills the log windows (<see cref="Log"/>) from incoming chat, system, combat
    /// and client messages in the retail line formats, and sends what is typed on the input line
    /// (<see cref="Input"/>) through the session's command dispatcher
    /// (<see cref="PlayerActionService.ExecuteCommandAsync"/>), echoing your own chat to the log as the server does
    /// not send it back.
    /// </summary>
    public sealed class StockUiChat
    {
        public StockUiChat()
        {
            Input.Submitted += (line, mode) => _ = SubmitAsync(line, mode);
            Input.ScrollRequested += (window, amount, pages) => Log.Scroll(window, pages ? amount * Math.Max(1, PageSize(window) - 1) : amount);
            Input.OpenChanged += open =>
            {
                if (!open) Log.ScrollToNewest();
            };
        }

        public StockUiChatLog Log { get; } = new();
        public StockUiChatInput Input { get; } = new();

        /// <summary>Your character's name, for the echo of your own lines.</summary>
        public Func<string> CharacterName { get; set; } = () => string.Empty;

        /// <summary>Your character's server id: combat lines about it take the Font Colors "For Self" rows.</summary>
        public Func<uint> LocalPlayerId { get; set; } = () => 0;

        /// <summary>The config menu's client-only chat filter mask (Tell, Party, Linkshell, Linkshell 2, Unity).</summary>
        public Func<uint> ClientChatFilters { get; set; } = () => 0;

        /// <summary>Lines a log window shows (1 or 2), which is how far a page scroll moves.</summary>
        public Func<int, int> PageSize { get; set; } = _ => 8;

        /// <summary>Runs a typed line: the session's command dispatcher, with the chat mode's channel as the default.</summary>
        public Func<string, ChatSendKind, Task<PlayerActionResult>>? Execute { get; set; }

        /// <summary>
        /// Opens the input line for a key pressed in gameplay, returning true when it did (the key is then the
        /// line's, not the character's): Enter outside a menu (retail), or a key the profile binds to
        /// <see cref="InputAction.OpenChat"/> (the slash key, and Space in the Full layout). The character the chat
        /// key types (<paramref name="keySymbol"/>, the slash) starts the new line; Space's is not typed.
        /// With <paramref name="confirmTaken"/> (a target is selected, or an event dialog runs) Enter is the Confirm
        /// action instead, as in retail, where Enter talks to the targeted NPC and only opens the line otherwise.
        /// </summary>
        public bool TryOpen(GordianKey key, InputModifiers modifiers, InputProfile? profile, bool menuOpen, string? keySymbol = null,
            bool confirmTaken = false)
        {
            if (Input.IsOpen) return false;
            bool open = !menuOpen && !confirmTaken && modifiers == InputModifiers.None && key is GordianKey.Enter or GordianKey.NumPadEnter;
            if (!open && profile != null)
            {
                var chord = new InputChord(key, modifiers);
                foreach (var bound in profile.GetChords(InputAction.OpenChat))
                {
                    if (bound.Equals(chord)) open = true;
                }
            }
            if (!open) return false;
            if (key == GordianKey.Space)
            {
                Input.Open(" ");
                return true;
            }
            Input.Open();
            if (key is not (GordianKey.Enter or GordianKey.NumPadEnter) && StockUiChatInput.IsTypedSymbol(keySymbol))
            {
                Input.InsertKeySymbol(keySymbol!);
            }
            return true;
        }

        /// <summary>
        /// Splits the log into two windows (config "Log Window Multi-window" not OFF), which also offers Window 2 in
        /// the chat-mode list.
        /// </summary>
        public void SetMultiWindow(bool multiWindow)
        {
            Log.MultiWindow = multiWindow;
            if (!multiWindow && SelectedLogWindow == 2) SelectedLogWindow = 0;
        }

        /// <summary>
        /// The log window (1 or 2) selected to scroll through, 0 for none. Retail cycles it with the numeric keypad +
        /// (gamepad Y): Window 1, Window 2 when split, then none (the status icons follow in retail, to cancel a
        /// buff, once they can be selected). A selected window is drawn opaque; Up/Down scroll it a line.
        /// </summary>
        public int SelectedLogWindow { get; set; }

        /// <summary>Selects the next log window, or none after the last.</summary>
        public void CycleLogWindow()
        {
            SelectedLogWindow = SelectedLogWindow switch
            {
                0 => 1,
                1 when Log.MultiWindow => 2,
                _ => 0,
            };
            if (SelectedLogWindow == 0) Log.ScrollToNewest();
        }

        /// <summary>Releases the selected log window and returns it to the newest lines.</summary>
        public void ReleaseLogWindow()
        {
            SelectedLogWindow = 0;
            Log.ScrollToNewest();
        }

        /// <summary>Subscribes the log to a session's message sources.</summary>
        public void Attach(ChatPacketModule chat, PartyState party, CombatState combat, StockUiMenuController menus,
            Func<uint, string?> resolveEntityName)
        {
            chat.ChatMessageReceived += OnChatMessage;
            chat.SystemMessageReceived += msg => Log.Add(ChatLogChannel.System, StandardMessages.FormatMessage(msg));
            chat.LinkshellMessageReceived += msg =>
            {
                if (string.IsNullOrEmpty(msg.Message)) return;
                Log.Add(msg.Slot == LinkshellSlot.LS1 ? ChatLogChannel.Linkshell : ChatLogChannel.Linkshell2,
                    $"[{msg.LinkshellName}] {msg.Message}");
            };
            party.InviteReceived += invite =>
                Log.Add(ChatLogChannel.System, $"{invite.InviterName} invites you to join a party. Type /join to accept or /decline to decline.");
            combat.ActionExecuted += record =>
            {
                // Each line takes its Font Colors row and Log page type from what it reports and whom (StockUiCombatLog).
                uint me = LocalPlayerId();
                foreach (var line in CombatLogFormatter.FormatActionLines(record, resolveEntityName))
                {
                    Log.Add(StockUiCombatLog.LineFor(line, me, DateTime.Now));
                }
            };
            combat.BattleMessageReceived += record =>
            {
                // A message may span lines (a monster check prints its level, then its defense and evasion).
                uint me = LocalPlayerId();
                foreach (string line in CombatLogFormatter.FormatBattleMessage(record, resolveEntityName).Split('\n'))
                {
                    if (line.Length > 0) Log.Add(StockUiCombatLog.LineFor(line, record.MessageId, record.TargetId, me, DateTime.Now));
                }
            };
            menus.NoticePosted += message => Log.Add(ChatLogChannel.Notice, message);
        }

        /// <summary>
        /// The line retail prints above every system message of kinds 6 and 7 (one banner per packet, same channel).
        /// Seen in the maintainer's retail Windower chat logs: LSB GM command replies (kind 6, e.g. "Key item 3 was
        /// given to ...", Gemini 2026-10-03 17:09:09) and retail maintenance notices; kind 29 lines ("... now has
        /// learned 25 of 835 spells.") have none.
        /// </summary>
        public const string SystemMessageBanner = "----== SystemMessage ==----";

        /// <summary>
        /// Formats an <c>Attr</c> 0x08 message (a DAT message reference) into its log lines, or null when its table
        /// cannot be read. Set by the event dialog controller, which owns the message tables and formatter.
        /// </summary>
        public Func<ChatMessage, IReadOnlyList<string>?>? FormattedMessageResolver { get; set; }

        /// <summary>True for the kinds retail heads with <see cref="SystemMessageBanner"/>.</summary>
        public static bool HasSystemBanner(ChatMessageType type) => type is ChatMessageType.System1 or ChatMessageType.System2;

        /// <summary>
        /// Logs an incoming chat line unless the client-only chat filters drop its channel. System kinds 6 and 7 get
        /// the retail banner line first; a DAT message reference (<c>Attr</c> 0x08) is formatted from its table, and
        /// dropped (never shown as its raw value list) when the table is not available.
        /// </summary>
        public void OnChatMessage(ChatMessage msg)
        {
            var channel = ChannelOf(msg.Type);
            if (IsClientFiltered(channel, ClientChatFilters())) return;
            if (channel == ChatLogChannel.Tell && !string.IsNullOrEmpty(msg.Sender)) Input.TellTarget = msg.Sender;

            IReadOnlyList<string>? lines = null;
            if (msg.Formatted is { } reference)
            {
                lines = FormattedMessageResolver?.Invoke(msg);
                if (lines == null || lines.Count == 0)
                {
                    GordianLog.Debug("CHAT_UI", $"[{msg.Type}] DAT message {reference.Table} #{reference.MessageId} not shown: its table is not read.");
                    return;
                }
            }
            else if ((msg.Attr & S2C_0x017_ChatStd.AttrFormatted) != 0)
            {
                return; // an unreadable reference: retail prints no value list either
            }

            if (HasSystemBanner(msg.Type)) Log.Add(channel, SystemMessageBanner);
            if (lines == null)
            {
                Log.Add(channel, FormatIncoming(msg.Type, msg.Sender, msg.Message));
                return;
            }
            foreach (string line in lines) Log.Add(channel, line);
        }

        /// <summary>
        /// Sends a typed line. Plain text goes out on the chat mode's channel (Tell mode to the last tell partner);
        /// slash commands go to the dispatcher as typed. Your own chat is echoed in the retail format; any other
        /// result is logged as a client notice (warnings and errors in the error colour).
        /// </summary>
        public async Task SubmitAsync(string line, ChatInputMode mode)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            string raw = line.Trim();
            if (TryChatModeCommand(raw)) return;
            bool command = raw.StartsWith('/') || raw.StartsWith('!');
            if (!command && mode == ChatInputMode.Tell)
            {
                if (string.IsNullOrEmpty(Input.TellTarget))
                {
                    Log.Add(ChatLogChannel.Error, "No one to send a tell to. Use /tell <name> <message>.");
                    return;
                }
                raw = $"/tell {Input.TellTarget} {raw}";
            }

            var kind = SendKindOf(mode);
            var parsed = ChatCommandRouter.Parse(raw, kind);
            if (parsed.Kind == ChatCommandResultKind.SendChat && string.IsNullOrWhiteSpace(parsed.Message)) return;

            var execute = Execute;
            if (execute == null) return;
            PlayerActionResult result;
            try
            {
                result = await execute(raw, kind).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                GordianLog.Error("CHAT_UI", $"Chat line failed: {ex.Message}", ex);
                Log.Add(ChatLogChannel.Error, $"Could not send: {ex.Message}");
                return;
            }

            string me = CharacterName();
            // Shout as the default mode lasts one line (retail /chatmode: "[Shout] will be reset after using once").
            if (!command && mode == ChatInputMode.Shout) Input.SetMode(ChatInputMode.Say);
            if (result.Success && parsed.Kind == ChatCommandResultKind.SendChat)
            {
                Log.Add(ChannelOf(parsed.SpeechKind), FormatOutgoing(parsed.SpeechKind, me, parsed.Message));
                return;
            }
            if (result.Success && parsed.Kind == ChatCommandResultKind.SendTell)
            {
                Input.TellTarget = parsed.Recipient;
                Log.Add(ChatLogChannel.Tell, $">>{parsed.Recipient} : {parsed.Message}");
                return;
            }
            if (result.Success && parsed.Kind == ChatCommandResultKind.ServerCommand)
            {
                Log.Add(ChatLogChannel.Say, FormatOutgoing(ChatSendKind.Say, me, parsed.Message));
                return;
            }

            if (string.IsNullOrEmpty(result.Message)) return;
            var channel = result.Kind is PlayerActionResultKind.Warning or PlayerActionResultKind.Error
                ? ChatLogChannel.Error
                : ChatLogChannel.Notice;
            foreach (var row in result.Message.Split('\n'))
            {
                string text = row.TrimEnd('\r');
                if (text.Length > 0) Log.Add(channel, text);
            }
        }

        /// <summary>
        /// A chat mode picked in the command menu's chat-mode list (retail, in-game check 2026-09-28): the default
        /// mode changes and the input line opens at once in it (Tell to the partner just chosen). Shout is the
        /// exception: the default mode is left alone and the line opens with <c>/sh </c> typed.
        /// </summary>
        public void OpenInputInMode(ChatInputMode mode)
        {
            if (mode == ChatInputMode.Shout)
            {
                Input.Open();
                Input.InsertText("/sh ");
                return;
            }
            Input.SetMode(mode);
            Input.Open();
        }

        /// <summary>
        /// Retail's <c>/chatmode [mode]</c> (alias <c>/cm</c>): sets the default chat mode (<c>tell</c> takes a name;
        /// shout lasts one line), or with no mode shows the current one. Returns false for any other line.
        /// </summary>
        public bool TryChatModeCommand(string raw)
        {
            if (!raw.StartsWith('/')) return false;
            string[] words = raw[1..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) return false;
            string verb = words[0].ToLowerInvariant();
            if (verb is not ("chatmode" or "cm")) return false;

            if (words.Length == 1)
            {
                string current = Input.Mode == ChatInputMode.Tell && Input.TellTarget.Length > 0
                    ? $"Tell ({Input.TellTarget})"
                    : StockUiChatInput.Label(Input.Mode);
                Log.Add(ChatLogChannel.Notice, $"Current chat mode: {current}.");
                return true;
            }

            ChatInputMode? mode = words[1].ToLowerInvariant() switch
            {
                "say" or "s" => ChatInputMode.Say,
                "shout" or "sh" => ChatInputMode.Shout,
                "tell" or "t" => ChatInputMode.Tell,
                "party" or "p" => ChatInputMode.Party,
                "linkshell" or "linkshell1" or "l" or "l1" => ChatInputMode.Linkshell,
                "linkshell2" or "l2" => ChatInputMode.Linkshell2,
                "unity" or "u" => ChatInputMode.Unity,
                "assistj" => ChatInputMode.AssistJ,
                "assiste" => ChatInputMode.AssistE,
                _ => null,
            };
            if (mode == null)
            {
                Log.Add(ChatLogChannel.Error, $"Unknown chat mode \"{words[1]}\".");
                return true;
            }
            if (mode == ChatInputMode.Tell)
            {
                if (words.Length < 3)
                {
                    Log.Add(ChatLogChannel.Error, "Specify who to send tells to: /chatmode tell <name>.");
                    return true;
                }
                Input.TellTarget = words[2];
            }
            Input.SetMode(mode.Value);
            return true;
        }

        public static ChatSendKind SendKindOf(ChatInputMode mode) => mode switch
        {
            ChatInputMode.Shout => ChatSendKind.Shout,
            ChatInputMode.Party => ChatSendKind.Party,
            ChatInputMode.Linkshell => ChatSendKind.Linkshell1,
            ChatInputMode.Linkshell2 => ChatSendKind.Linkshell2,
            ChatInputMode.Unity => ChatSendKind.Unity,
            ChatInputMode.AssistJ => ChatSendKind.AssistJ,
            ChatInputMode.AssistE => ChatSendKind.AssistE,
            _ => ChatSendKind.Say,
        };

        public static ChatLogChannel ChannelOf(ChatMessageType type) => type switch
        {
            ChatMessageType.Say or ChatMessageType.NoSpeakerSay or ChatMessageType.SayCopy24
                or ChatMessageType.SayCopy25 => ChatLogChannel.Say,
            ChatMessageType.Shout or ChatMessageType.NoSpeakerShout => ChatLogChannel.Shout,
            ChatMessageType.Yell => ChatLogChannel.Yell,
            ChatMessageType.Tell => ChatLogChannel.Tell,
            ChatMessageType.Party or ChatMessageType.NoSpeakerParty => ChatLogChannel.Party,
            ChatMessageType.Linkshell or ChatMessageType.NoSpeakerLinkshell or ChatMessageType.Linkshell3
                or ChatMessageType.NoSpeakerLinkshell3 => ChatLogChannel.Linkshell,
            ChatMessageType.Linkshell2 or ChatMessageType.NoSpeakerLinkshell2 => ChatLogChannel.Linkshell2,
            ChatMessageType.Unity => ChatLogChannel.Unity,
            ChatMessageType.JpAssist => ChatLogChannel.AssistJ,
            ChatMessageType.NaAssist => ChatLogChannel.AssistE,
            ChatMessageType.Emotion => ChatLogChannel.Emote,
            _ => ChatLogChannel.ServerMessage,
        };

        public static ChatLogChannel ChannelOf(ChatSendKind kind) => kind switch
        {
            ChatSendKind.Shout => ChatLogChannel.Shout,
            ChatSendKind.Yell => ChatLogChannel.Yell,
            ChatSendKind.Party => ChatLogChannel.Party,
            ChatSendKind.Linkshell1 or ChatSendKind.LinkshellPvp => ChatLogChannel.Linkshell,
            ChatSendKind.Linkshell2 => ChatLogChannel.Linkshell2,
            ChatSendKind.Unity => ChatLogChannel.Unity,
            ChatSendKind.AssistJ => ChatLogChannel.AssistJ,
            ChatSendKind.AssistE => ChatLogChannel.AssistE,
            ChatSendKind.Emote => ChatLogChannel.Emote,
            _ => ChatLogChannel.Say,
        };

        /// <summary>
        /// An incoming line in the retail log format: <c>Name : text</c> (say, shout, yell, assist),
        /// <c>Name&gt;&gt; text</c> (tell), <c>(Name) text</c> (party), <c>&lt;Name&gt; text</c> (linkshell;
        /// <c>[2]&lt;Name&gt;</c> for the second), <c>{Name} text</c> (Unity); emotes and system text as sent,
        /// as are the speakerless variants.
        /// </summary>
        public static string FormatIncoming(ChatMessageType type, string sender, string message)
        {
            if (string.IsNullOrEmpty(sender)) return message;
            return type switch
            {
                ChatMessageType.Tell => $"{sender}>> {message}",
                ChatMessageType.Party => $"({sender}) {message}",
                ChatMessageType.Linkshell or ChatMessageType.Linkshell3 => $"<{sender}> {message}",
                ChatMessageType.Linkshell2 => $"[2]<{sender}> {message}",
                ChatMessageType.Unity => $"{{{sender}}} {message}",
                ChatMessageType.Say or ChatMessageType.SayCopy24 or ChatMessageType.SayCopy25 or ChatMessageType.Shout
                    or ChatMessageType.Yell or ChatMessageType.JpAssist or ChatMessageType.NaAssist => $"{sender} : {message}",
                _ => message,
            };
        }

        /// <summary>Your own line as the log shows it (the incoming format with your name).</summary>
        public static string FormatOutgoing(ChatSendKind kind, string name, string message) => kind switch
        {
            ChatSendKind.Party => $"({name}) {message}",
            ChatSendKind.Linkshell1 or ChatSendKind.LinkshellPvp => $"<{name}> {message}",
            ChatSendKind.Linkshell2 => $"[2]<{name}> {message}",
            ChatSendKind.Unity => $"{{{name}}} {message}",
            ChatSendKind.Emote => message,
            _ => $"{name} : {message}",
        };

        /// <summary>True when the client-only chat filter mask drops a channel.</summary>
        public static bool IsClientFiltered(ChatLogChannel channel, uint mask) => channel switch
        {
            ChatLogChannel.Tell => (mask & StockUiConfigPages.ClientChatFilterTell) != 0,
            ChatLogChannel.Party => (mask & StockUiConfigPages.ClientChatFilterParty) != 0,
            ChatLogChannel.Linkshell => (mask & StockUiConfigPages.ClientChatFilterLinkshell) != 0,
            ChatLogChannel.Linkshell2 => (mask & StockUiConfigPages.ClientChatFilterLinkshell2) != 0,
            ChatLogChannel.Unity => (mask & StockUiConfigPages.ClientChatFilterUnity) != 0,
            _ => false,
        };
    }
}
