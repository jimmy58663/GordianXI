using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Gordian.Core.Diagnostics;

namespace Gordian.App.ViewModels
{
    /// <summary>One coloured run of a log line in the Logs tab viewer; <see cref="Match"/> marks search hits.</summary>
    public readonly record struct LogSegment(string Text, string Foreground, bool Match);

    /// <summary>
    /// Splits a log line into coloured runs for the Logs tab viewer (#566): timestamp, level, category, character tag and
    /// message each get a colour, and the parts matching the search are marked for highlighting. A system or network line
    /// is <c>[time] [LEVEL] [CATEGORY] [Char] message</c>; a status or chat line is <c>[time] [Char or Channel] message</c>.
    /// A line that does not follow the pattern (a stack trace line) is all message, in its level's colour.
    /// </summary>
    public static class LogLineSegments
    {
        public const string TimestampColor = "#808080";
        public const string CategoryColor = "#C586C0";
        public const string TagColor = "#4EC9B0";
        public const string MessageColor = "#D4D4D4";

        private static readonly Regex LevelLine = new(
            @"^(\[[^\]]*\])( \[(?:TRACE|DEBUG|INFO|WARN|ERROR|STATUS)\])( \[[^\]]+\])( \[[^\]]+\])?(.*)$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex TimedLine = new(
            @"^(\[[0-9:.]+\])( \[[^\]]+\])?(.*)$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>The colour of a level's tag.</summary>
        public static string LevelColor(LogLevel? level) => level switch
        {
            LogLevel.Error => "#F48771",
            LogLevel.Warn => "#E5C07B",
            LogLevel.Info => "#4FC1FF",
            LogLevel.Debug or LogLevel.Trace => "#8A8A8A",
            LogLevel.Status => "#E5C07B",
            _ => MessageColor,
        };

        /// <summary>The colour of a line's message text: errors and warnings keep their level's colour, debug is dimmed.</summary>
        public static string MessageColorFor(LogLevel? level) => level switch
        {
            LogLevel.Error => "#F48771",
            LogLevel.Warn => "#E5C07B",
            LogLevel.Debug or LogLevel.Trace => "#8A8A8A",
            _ => MessageColor,
        };

        /// <summary>
        /// The runs of <paramref name="text"/>. <paramref name="hasLevels"/> says the file carries a level column;
        /// <paramref name="search"/> (case-insensitive) splits out the matching parts.
        /// </summary>
        public static List<LogSegment> Split(string text, LogLevel? level, bool hasLevels, string? search)
        {
            var colored = new List<(string Text, string Color)>(5);
            if (string.IsNullOrEmpty(text))
            {
                colored.Add((string.Empty, MessageColor));
            }
            else if (hasLevels && LevelLine.Match(text) is { Success: true } m)
            {
                colored.Add((m.Groups[1].Value, TimestampColor));
                colored.Add((m.Groups[2].Value, LevelColor(level)));
                colored.Add((m.Groups[3].Value, CategoryColor));
                if (m.Groups[4].Success) colored.Add((m.Groups[4].Value, TagColor));
                colored.Add((m.Groups[5].Value, MessageColorFor(level)));
            }
            else if (!hasLevels && TimedLine.Match(text) is { Success: true } t)
            {
                colored.Add((t.Groups[1].Value, TimestampColor));
                if (t.Groups[2].Success) colored.Add((t.Groups[2].Value, TagColor));
                colored.Add((t.Groups[3].Value, MessageColor));
            }
            else
            {
                colored.Add((text, MessageColorFor(level)));
            }

            // Matches are found on the whole line, so one that spans two runs ("] [WARN") is marked in both.
            var matched = new bool[text?.Length ?? 0];
            if (!string.IsNullOrEmpty(search) && !string.IsNullOrEmpty(text))
            {
                for (int hit = text.IndexOf(search, StringComparison.OrdinalIgnoreCase); hit >= 0;
                     hit = text.IndexOf(search, hit + search.Length, StringComparison.OrdinalIgnoreCase))
                {
                    for (int i = hit; i < hit + search.Length; i++) matched[i] = true;
                }
            }

            var segments = new List<LogSegment>(colored.Count + 2);
            int offset = 0;
            foreach (var (part, color) in colored)
            {
                int start = 0;
                while (start < part.Length)
                {
                    bool isMatch = matched.Length > 0 && matched[offset + start];
                    int end = start + 1;
                    while (end < part.Length && (matched.Length > 0 && matched[offset + end]) == isMatch) end++;
                    segments.Add(new LogSegment(part[start..end], color, isMatch));
                    start = end;
                }
                offset += part.Length;
            }
            if (segments.Count == 0) segments.Add(new LogSegment(string.Empty, MessageColor, false));
            return segments;
        }
    }
}
