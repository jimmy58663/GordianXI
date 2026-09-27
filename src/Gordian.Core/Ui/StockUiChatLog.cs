// src/Gordian.Core/Ui/StockUiChatLog.cs
using System;
using System.Collections.Generic;
using Gordian.Core.Resources.Ui;

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
        System,
        Combat,

        /// <summary>Client messages: command results, menu notices.</summary>
        Notice,

        /// <summary>Client errors and warnings (unknown command, no tell recipient...).</summary>
        Error,
    }

    /// <summary>
    /// One logical log line. The renderer wraps it to the window width and caches the result on the line
    /// (render thread only).
    /// </summary>
    public sealed class ChatLogLine
    {
        public ChatLogLine(ChatLogChannel channel, string text, DateTime timestamp)
        {
            Channel = channel;
            Text = text ?? string.Empty;
            Timestamp = timestamp;
        }

        public ChatLogChannel Channel { get; }
        public string Text { get; }

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
    /// With the config menu's "Log Window Multi-window" OFF everything goes to Window 1; otherwise combat lines go
    /// to Window 2 (retail's Log page routes message types per window; its row text needs the menu string table,
    /// so the split is fixed until then).
    /// </para>
    /// </summary>
    public sealed class StockUiChatLog
    {
        /// <summary>Lines kept per window.</summary>
        public const int Capacity = 1000;

        private readonly object _sync = new();
        private readonly List<ChatLogLine>[] _windows = { new(), new() };
        private readonly int[] _scroll = new int[2];

        /// <summary>Returns true when <paramref name="channel"/> goes to Window 2 while the log is split.</summary>
        public static bool IsWindow2Channel(ChatLogChannel channel) => channel == ChatLogChannel.Combat;

        /// <summary>True when the log is split into two windows (config "Log Window Multi-window" not OFF).</summary>
        public bool MultiWindow { get; set; }

        /// <summary>Raised after a line was added (on the thread that added it).</summary>
        public event Action<ChatLogLine>? LineAdded;

        public void Add(ChatLogChannel channel, string text) => Add(new ChatLogLine(channel, text, DateTime.Now));

        public void Add(ChatLogLine line)
        {
            ArgumentNullException.ThrowIfNull(line);
            int window = IsWindow2Channel(line.Channel) ? 1 : 0;
            lock (_sync)
            {
                var lines = _windows[window];
                lines.Add(line);
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
        /// Wraps a line to <paramref name="maxWidth"/> layout pixels of <paramref name="font"/> at
        /// <paramref name="scale"/>: breaks at the last space that fits (the break's spaces are dropped), or mid-word
        /// for a word wider than the line.
        /// </summary>
        public static List<string> Wrap(UiFont font, string text, float maxWidth, float scale = 1f)
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
                    float advance = font.GetAdvance(text[i]) * scale;
                    if (width + advance > maxWidth && i > start) break;
                    if (text[i] == ' ') lastSpace = i;
                    width += advance;
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
        public static IReadOnlyList<string> GetWrappedRows(ChatLogLine line, UiFont font, float maxWidth, int timestampMode)
        {
            if (line.WrappedRows != null && line.WrapKey == (maxWidth, timestampMode)) return line.WrappedRows;
            var rows = Wrap(font, WithTimestamp(line, timestampMode), maxWidth);
            line.WrappedRows = rows;
            line.WrapKey = (maxWidth, timestampMode);
            return rows;
        }
    }
}
