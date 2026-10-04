// src/Gordian.Core/Ui/StockUiChatLog.cs
using System;
using System.Collections.Generic;

namespace Gordian.Core.Ui
{
    /// <summary>
    /// What a log line is, which picks its colour and the log window it goes to.
    /// </summary>
    public enum ChatLogChannel : byte
    {
        Say,
        Shout,
        Yell,
        Tell,
        Party,
        Linkshell,
        Linkshell2,
        Unity,
        AssistJ,
        AssistE,
        Emote,
        /// <summary>Standard system messages (message ids resolved by <c>StandardMessages</c>).</summary>
        System,

        /// <summary>System text the server sends as chat (0x017 system types: welcome and server notices).</summary>
        ServerMessage,

        Combat,

        /// <summary>Client messages: command results, menu notices.</summary>
        Notice,

        /// <summary>Client errors and warnings (unknown command, no tell recipient...).</summary>
        Error,

        /// <summary>Event and NPC dialog lines (retail chat modes 150/151: the zone dialog table's text).</summary>
        Dialog,

        /// <summary>Zone messages about the player (0x036 / 0x02A with the no-name flag: "Home point set!", "Obtained: ...").</summary>
        Message,
    }

    /// <summary>
    /// The message types of the config menu's Log page (retail routes each to Window 1 or Window 2 when the log is
    /// split), named after their rows in the config row table (ROM/165/74): the chat rows 36-47 and 196, For Self
    /// 48-53, For Others 54-59 and System 60-62. Values are bit positions of <see cref="StockUiSettingKey.LogWindow2Types"/>.
    /// </summary>
    public enum ChatLogType : byte
    {
        Say,
        Shout,
        Yell,
        Tell,
        Party,
        Linkshell,
        Linkshell2,
        AssistJ,
        AssistE,
        Unity,
        Emote,
        /// <summary>Messages ("Message").</summary>
        Message,
        NpcConversation,

        /// <summary>HP/MP you recover.</summary>
        SelfRecover,
        /// <summary>HP/MP you lose.</summary>
        SelfLose,
        SelfBeneficial,
        SelfDetrimental,
        /// <summary>Effects you resist.</summary>
        SelfResist,
        /// <summary>Actions you evade.</summary>
        SelfEvade,

        OthersRecover,
        OthersLose,
        OthersBeneficial,
        OthersDetrimental,
        OthersResist,
        OthersEvade,

        StandardBattle,
        CallsForHelp,
        BasicSystem,
    }

    /// <summary>
    /// One logical log line. The renderer wraps it to the window width and caches the result on the line
    /// (render thread only).
    /// </summary>
    public sealed class ChatLogLine
    {
        public ChatLogLine(ChatLogChannel channel, string text, DateTime timestamp, StockUiFontColorId? fontColor = null, ChatLogType? type = null)
        {
            Channel = channel;
            Text = text ?? string.Empty;
            Timestamp = timestamp;
            FontColor = fontColor ?? StockUiFontColors.ForChannel(channel);
            Type = type ?? TypeOf(channel);
        }

        public ChatLogChannel Channel { get; }
        public string Text { get; }

        /// <summary>The Font Colors row the line is drawn with; null for lines with a fixed colour (system text, notices).</summary>
        public StockUiFontColorId? FontColor { get; }

        /// <summary>The Log page's message type, which picks the window the line goes to when the log is split.</summary>
        public ChatLogType Type { get; }

        /// <summary>The Log page type of a channel's lines (combat lines are classified per line, see <see cref="StockUiCombatLog"/>).</summary>
        public static ChatLogType TypeOf(ChatLogChannel channel) => channel switch
        {
            ChatLogChannel.Say => ChatLogType.Say,
            ChatLogChannel.Shout => ChatLogType.Shout,
            ChatLogChannel.Yell => ChatLogType.Yell,
            ChatLogChannel.Tell => ChatLogType.Tell,
            ChatLogChannel.Party => ChatLogType.Party,
            ChatLogChannel.Linkshell => ChatLogType.Linkshell,
            ChatLogChannel.Linkshell2 => ChatLogType.Linkshell2,
            ChatLogChannel.Unity => ChatLogType.Unity,
            ChatLogChannel.AssistJ => ChatLogType.AssistJ,
            ChatLogChannel.AssistE => ChatLogType.AssistE,
            ChatLogChannel.Emote => ChatLogType.Emote,
            ChatLogChannel.Dialog => ChatLogType.NpcConversation,
            ChatLogChannel.Message => ChatLogType.Message,
            ChatLogChannel.Combat => ChatLogType.StandardBattle,
            _ => ChatLogType.BasicSystem,
        };

        /// <summary>Local time the line was logged (shown by the config menu's Timestamp option).</summary>
        public DateTime Timestamp { get; }

        /// <summary>Wrap cache: the key (width and timestamp mode) the rows were wrapped for.</summary>
        internal (float Width, int TimestampMode) WrapKey;
        internal IReadOnlyList<string>? WrappedRows;
    }

    /// <summary>
    /// The stock log windows' contents: a bounded scrollback of <see cref="ChatLogLine"/>s for each of the two log
    /// windows, and each window's scroll position. Lines arrive from the network thread and are read by the
    /// render thread, so every access takes the log's lock.
    /// <para>
    /// With the config menu's "Log Window Multi-window" OFF Window 1 shows everything; otherwise each line goes to
    /// the window its <see cref="ChatLogLine.Type"/> is routed to by the Log page (<see cref="Window2Types"/>:
    /// battle messages to Window 2 by default).
    /// </para>
    /// </summary>
    public sealed class StockUiChatLog
    {
        /// <summary>Lines kept per window.</summary>
        public const int Capacity = 1000;

        /// <summary>
        /// The types a fresh character's log shows in Window 2: every For Self and For Others type, standard battle
        /// messages and calls for help (cnf.dat 0x298 = ffffffff, read as the battle word; provisional bit mapping).
        /// </summary>
        public const uint DefaultWindow2Types =
            ((1u << ((int)ChatLogType.CallsForHelp + 1)) - 1) & ~((1u << (int)ChatLogType.SelfRecover) - 1);

        /// <summary>The bit of a type in <see cref="Window2Types"/>.</summary>
        public static uint Bit(ChatLogType type) => 1u << (int)type;

        private readonly object _sync = new();
        private readonly List<ChatLogLine>[] _windows = { new(), new() };
        private readonly int[] _scroll = new int[2];

        /// <summary>The Log page's routing: the types that go to Window 2 (the settings' <see cref="StockUiSettingKey.LogWindow2Types"/>).</summary>
        public Func<uint> Window2Types { get; set; } = () => DefaultWindow2Types;

        /// <summary>Whether a line goes to Window 2 while the log is split.</summary>
        public bool IsWindow2(ChatLogLine line) => (Window2Types() & Bit(line.Type)) != 0;

        /// <summary>True when the log is split into two windows (config "Log Window Multi-window" not OFF).</summary>
        public bool MultiWindow { get; set; }

        /// <summary>Raised after a line was added (on the thread that added it).</summary>
        public event Action<ChatLogLine>? LineAdded;

        public void Add(ChatLogChannel channel, string text) => Add(new ChatLogLine(channel, text, DateTime.Now));

        public void Add(ChatLogLine line)
        {
            ArgumentNullException.ThrowIfNull(line);
            int window = IsWindow2(line) ? 1 : 0;
            lock (_sync)
            {
                var lines = _windows[window];
                lines.Add(line);
                _added[window]++;
                if (lines.Count > Capacity) lines.RemoveRange(0, lines.Count - Capacity);
                // A window scrolled back keeps showing the same lines while new ones arrive below.
                if (_scroll[window] > 0) _scroll[window] = Math.Min(_scroll[window] + 1, lines.Count - 1);
            }
            LineAdded?.Invoke(line);
        }

        public void Clear()
        {
            lock (_sync)
            {
                for (int i = 0; i < _windows.Length; i++)
                {
                    _windows[i].Clear();
                    _scroll[i] = 0;
                }
            }
        }

        private readonly long[] _added = new long[2];

        /// <summary>
        /// How many lines have arrived for a window (1 or 2) since the log was created, never decreasing (Window 1
        /// counts both windows' lines without multi-window): the reactive sizing grows a window by the difference.
        /// </summary>
        public long AddedCount(int window)
        {
            lock (_sync)
            {
                int i = WindowIndex(window);
                return !MultiWindow && i == 0 ? _added[0] + _added[1] : MultiWindow ? _added[i] : 0;
            }
        }

        /// <summary>Lines stored for a window (1 or 2). Without multi-window, Window 1 shows both.</summary>
        public int Count(int window)
        {
            lock (_sync) return _windows[WindowIndex(window)].Count;
        }

        /// <summary>How many lines a window is scrolled back from the newest (0 = following new lines).</summary>
        public int ScrollOffset(int window)
        {
            lock (_sync) return _scroll[WindowIndex(window)];
        }

        /// <summary>Scrolls a window back (positive) or forward (negative) by whole lines.</summary>
        public void Scroll(int window, int lines)
        {
            int i = WindowIndex(window);
            lock (_sync)
            {
                int max = Math.Max(0, TotalCount(i) - 1);
                _scroll[i] = Math.Clamp(_scroll[i] + lines, 0, max);
            }
        }

        public void ScrollToNewest()
        {
            lock (_sync)
            {
                _scroll[0] = 0;
                _scroll[1] = 0;
            }
        }

        /// <summary>
        /// Copies the lines a window shows, oldest first, into <paramref name="destination"/>: up to
        /// <paramref name="maxLines"/> lines ending <see cref="ScrollOffset"/> lines before the newest. Without
        /// multi-window, Window 1 shows both windows' lines in arrival order.
        /// </summary>
        public void CopyVisible(int window, int maxLines, List<ChatLogLine> destination)
        {
            destination.Clear();
            int i = WindowIndex(window);
            lock (_sync)
            {
                if (!MultiWindow && i == 1) return;
                int skip = _scroll[i];
                if (MultiWindow || _windows[1].Count == 0)
                {
                    var lines = _windows[i];
                    int end = lines.Count - skip;
                    int start = Math.Max(0, end - maxLines);
                    for (int k = start; k < end; k++) destination.Add(lines[k]);
                    return;
                }

                // Merged view: walk both windows back from the newest by timestamp, then restore order.
                var a = _windows[0];
                var b = _windows[1];
                int ia = a.Count - 1, ib = b.Count - 1;
                int skipped = 0;
                while ((ia >= 0 || ib >= 0) && destination.Count < maxLines)
                {
                    ChatLogLine next;
                    if (ib < 0 || (ia >= 0 && a[ia].Timestamp >= b[ib].Timestamp)) next = a[ia--];
                    else next = b[ib--];
                    if (skipped < skip)
                    {
                        skipped++;
                        continue;
                    }
                    destination.Add(next);
                }
                destination.Reverse();
            }
        }

        private int TotalCount(int index) =>
            !MultiWindow && index == 0 ? _windows[0].Count + _windows[1].Count : _windows[index].Count;

        private static int WindowIndex(int window) => window == 2 ? 1 : 0;

        /// <summary>
        /// Wraps a line to <paramref name="maxWidth"/> layout pixels, measuring each character with
        /// <paramref name="advance"/> (the log font's pen advance): breaks at the last space that fits (the break's spaces are dropped), or mid-word
        /// for a word wider than the line.
        /// </summary>
        public static List<string> Wrap(Func<char, float> advance, string text, float maxWidth)
        {
            var rows = new List<string>();
            if (string.IsNullOrEmpty(text))
            {
                rows.Add(string.Empty);
                return rows;
            }

            int start = 0;
            while (start < text.Length)
            {
                float width = 0;
                int lastSpace = -1;
                int i = start;
                for (; i < text.Length; i++)
                {
                    float step = advance(text[i]);
                    if (width + step > maxWidth && i > start) break;
                    if (text[i] == ' ') lastSpace = i;
                    width += step;
                }
                if (i >= text.Length)
                {
                    rows.Add(text.Substring(start));
                    break;
                }

                int end = lastSpace > start ? lastSpace : i;
                rows.Add(text.Substring(start, end - start).TrimEnd());
                start = end;
                while (start < text.Length && text[start] == ' ') start++;
            }
            return rows;
        }

        /// <summary>Characters of the timestamp prefix for a mode (retail draws it white whatever the line's colour).</summary>
        public static int TimestampLength(int timestampMode) => timestampMode switch { 1 => 8, 2 => 11, _ => 0 };

        /// <summary>The line's text with the config menu's timestamp prefix (1 = "[HH:mm] ", 2 = "[HH:mm:ss] ").</summary>
        public static string WithTimestamp(ChatLogLine line, int timestampMode) => timestampMode switch
        {
            1 => $"[{line.Timestamp:HH:mm}] {line.Text}",
            2 => $"[{line.Timestamp:HH:mm:ss}] {line.Text}",
            _ => line.Text,
        };

        /// <summary>
        /// The line's wrapped rows for a width and timestamp mode, cached on the line (call from one thread only:
        /// the renderer's).
        /// </summary>
        public static IReadOnlyList<string> GetWrappedRows(ChatLogLine line, Func<char, float> advance, float maxWidth, int timestampMode)
        {
            if (line.WrappedRows != null && line.WrapKey == (maxWidth, timestampMode)) return line.WrappedRows;
            var rows = Wrap(advance, WithTimestamp(line, timestampMode), maxWidth);
            line.WrappedRows = rows;
            line.WrapKey = (maxWidth, timestampMode);
            return rows;
        }
    }
}
