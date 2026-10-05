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
            if (SelectedLogWindow > 1) SelectedLogWindow = 1;
        }

        /// <summary>
        /// 1 while the log is selected, 0 otherwise. Retail's selection cycle (keypad +, gamepad Y; the maintainer's
        /// comparison 2026-10-04): the first press selects the log, both windows at once (they open to their maximum
        /// lines and are drawn opaque), the second the status icons (to cancel a status), the third ends it. While
        /// the log is selected Up/Down scroll Window 1 a line and Confirm opens the full-screen log.
        /// </summary>
        public int SelectedLogWindow { get; set; }

        /// <summary>Whether the log (both windows) is selected.</summary>
        public bool IsLogSelected => SelectedLogWindow != 0;

        /// <summary>Icons per row of the status icon grid ("buff": nine per row, 26 px apart).</summary>
        public const int StatusIconsPerRow = 9;

        /// <summary>Your character's status ids in icon order (the "buff" grid's), for the status-icon step of the cycle.</summary>
        public Func<IReadOnlyList<ushort>> StatusIds { get; set; } = () => Array.Empty<ushort>();

        /// <summary>Asks the server to cancel a status (C2S 0x0F1, <see cref="PlayerActionService.CancelBuffAsync"/>).</summary>
        public Action<ushort>? CancelStatus { get; set; }

        private int _selectedStatus = -1;

        /// <summary>The index of the status icon under the selection cursor, -1 when none is selected.</summary>
        public int SelectedStatusIcon
        {
            get
            {
                if (_selectedStatus < 0) return -1;
                int count = StatusIds().Count;
                return count == 0 ? -1 : Math.Min(_selectedStatus, count - 1);
            }
        }

        /// <summary>Whether the log, a status icon or the full-screen log is selected (the menu keys are then the selection's).</summary>
        public bool IsSelecting => SelectedLogWindow != 0 || SelectedStatusIcon >= 0 || FullLogOpen;

        /// <summary>
        /// The full-screen log (the <c>fulllog</c> window with <c>fep</c> tabs; retail capture 2026-10-04): opened
        /// with Confirm while the log is selected, its tabs cycled with keypad + / gamepad Y, closed with Cancel.
        /// </summary>
        public bool FullLogOpen { get; private set; }

        /// <summary>The full-screen log's tab (Window 1, Window 2, then the chat types in retail's order).</summary>
        public StockUiLogTab FullLogTab { get; private set; }

        /// <summary>How many lines the full-screen log is scrolled back from the tab's newest.</summary>
        public int FullLogScroll { get; private set; }

        /// <summary>Lines the full-screen log shows at once (set by the HUD), which is how far a page moves.</summary>
        public int FullLogPageLines { get; set; } = 20;

        /// <summary>
        /// The next step of the selection cycle: the log, the status icons when there are any, then none; with the
        /// full-screen log open, its next tab (wrapping).
        /// </summary>
        public void CycleLogWindow()
        {
            if (FullLogOpen)
            {
                int count = Enum.GetValues<StockUiLogTab>().Length;
                FullLogTab = (StockUiLogTab)(((int)FullLogTab + 1) % count);
                FullLogScroll = 0;
                return;
            }
            if (_selectedStatus >= 0)
            {
                _selectedStatus = -1;
                return;
            }
            if (SelectedLogWindow == 0)
            {
                SelectedLogWindow = 1;
                return;
            }
            SelectedLogWindow = 0;
            Log.ScrollToNewest();
            if (StatusIds().Count > 0) _selectedStatus = 0;
        }

        /// <summary>Confirm while the log is selected: opens the full-screen log on its first tab (Window 1).</summary>
        public void OpenFullLog()
        {
            if (!IsLogSelected) return;
            FullLogOpen = true;
            FullLogTab = StockUiLogTab.Window1;
            FullLogScroll = 0;
        }

        /// <summary>Cancel in the full-screen log: closes it, back to the selected log.</summary>
        public void CloseFullLog()
        {
            FullLogOpen = false;
            FullLogScroll = 0;
        }

        /// <summary>Scrolls the full-screen log back (positive) or forward (negative) by lines, within the tab's history.</summary>
        public void ScrollFullLog(int lines)
        {
            if (!FullLogOpen) return;
            int total = Log.CopyTab(FullLogTab, 0, 0, _scratch);
            FullLogScroll = Math.Clamp(FullLogScroll + lines, 0, Math.Max(0, total - 1));
        }

        private readonly List<ChatLogLine> _scratch = new();

        /// <summary>Releases the selection (log window or status icon); a log window returns to the newest lines.</summary>
        public void ReleaseLogWindow()
        {
            SelectedLogWindow = 0;
            _selectedStatus = -1;
            FullLogOpen = false;
            FullLogScroll = 0;
            Log.ScrollToNewest();
        }

        /// <summary>Moves the status cursor: left/right an icon, up/down a row of the grid, staying on the icons shown.</summary>
        public void MoveStatusSelection(int dx, int dy)
        {
            int index = SelectedStatusIcon;
            if (index < 0) return;
            int count = StatusIds().Count;
            int next = index + dx + dy * StatusIconsPerRow;
            if (dx != 0) next = (next % count + count) % count;
            else if (next < 0 || next >= count) next = index;
            _selectedStatus = next;
        }

        /// <summary>Confirm on a selected status icon: asks the server to cancel that status. Returns the status id, or null.</summary>
        public ushort? ConfirmStatusSelection()
        {
            int index = SelectedStatusIcon;
            var ids = StatusIds();
            if (index < 0 || index >= ids.Count) return null;
            ushort id = ids[index];
            CancelStatus?.Invoke(id);
            return id;
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
            if (TryImportRetailCommand(raw)) return;
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
            // Search, item search and blacklist replies are server answers retail prints as plain system text (white,
            // Basic system messages); other client results keep GordianXI's notice colour.
            var channel = result.Kind is PlayerActionResultKind.Warning or PlayerActionResultKind.Error
                ? ChatLogChannel.Error
                : IsSystemReply(parsed.Kind) ? ChatLogChannel.System : ChatLogChannel.Notice;
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

        /// <summary>The retail install folder (for <c>/importretail</c>); null when unknown.</summary>
        public Func<string?> GameDirectory { get; set; } = () => null;

        /// <summary>The character's id, whose hex form is the likely name of its retail USER folder.</summary>
        public Func<uint> CharacterId { get; set; } = () => 0;

        /// <summary>The character's stock UI settings, which <c>/importretail</c> overwrites.</summary>
        public Func<StockUiSettings?> Settings { get; set; } = () => null;

        /// <summary>
        /// <c>/importretail [folder]</c> (#51): with no folder, lists the retail USER folders (newest first, the one
        /// named after this character marked); with one, imports its cnf.dat (the Font Colors and the Log page's
        /// routing, which it overwrites). The game folder is only read. Returns false for any other line.
        /// </summary>
        public bool TryImportRetailCommand(string raw)
        {
            if (!raw.StartsWith('/')) return false;
            string[] words = raw[1..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0 || !words[0].Equals("importretail", StringComparison.OrdinalIgnoreCase)) return false;

            string? game = GameDirectory();
            if (string.IsNullOrEmpty(game))
            {
                Log.Add(ChatLogChannel.Error, "Import retail settings: the FINAL FANTASY XI folder was not found.");
                return true;
            }
            var folders = RetailUserSettings.ListFolders(game);
            string guess = RetailUserSettings.FolderNameFor(CharacterId());
            if (words.Length == 1)
            {
                if (folders.Count == 0)
                {
                    Log.Add(ChatLogChannel.Error, "Import retail settings: there are no character folders in the retail USER folder.");
                    return true;
                }
                Log.Add(ChatLogChannel.Notice, "Retail character settings (USER folders, newest first). Import one with /importretail <folder>:");
                foreach (var folder in folders)
                {
                    string mark = folder.Name.Equals(guess, StringComparison.Ordinal) ? "  <- this character?" : string.Empty;
                    string config = folder.HasConfig ? string.Empty : " (no cnf.dat)";
                    Log.Add(ChatLogChannel.Notice, $"  {folder.Name}  {folder.LastModifiedUtc.ToLocalTime():yyyy-MM-dd HH:mm}{config}{mark}");
                }
                return true;
            }

            string name = words[1];
            RetailUserFolder? chosen = null;
            foreach (var folder in folders)
            {
                if (folder.Name.Equals(name, StringComparison.Ordinal)) chosen = folder;
            }
            if (chosen == null)
            {
                Log.Add(ChatLogChannel.Error, $"Import retail settings: there is no USER folder \"{name}\". Type /importretail to list them.");
                return true;
            }
            var settings = Settings();
            var cnf = RetailUserSettings.ReadConfig(chosen.Path);
            if (settings == null || cnf == null)
            {
                Log.Add(ChatLogChannel.Error, $"Import retail settings: USER/{chosen.Name} has no readable cnf.dat.");
                return true;
            }
            var applied = RetailUserSettings.Apply(cnf, settings);
            Log.Add(ChatLogChannel.Notice, applied.Count == 0
                ? $"Imported nothing from USER/{chosen.Name}."
                : $"Imported {string.Join(" and ", applied)} from USER/{chosen.Name}.");
            return true;
        }

        /// <summary>The commands whose replies are the server's answers (<c>/sea</c>, <c>/itemsearch</c>, <c>/blacklist</c>), logged as system text.</summary>
        public static bool IsSystemReply(ChatCommandResultKind kind) =>
            kind is ChatCommandResultKind.PlayerSearch or ChatCommandResultKind.ItemSearch or ChatCommandResultKind.Blacklist;

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
