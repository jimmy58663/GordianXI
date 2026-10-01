// src/Gordian.Core/Resources/Tables/EventMessageDecoder.cs
using System;
using System.Collections.Generic;
using System.Text;

namespace Gordian.Core.Resources.Tables
{
    /// <summary>What one piece of a dialog message is.</summary>
    public enum EventMessageSegmentKind
    {
        /// <summary>Literal text.</summary>
        Text,
        /// <summary>A line break (0x07): the log shows the next piece on a new line.</summary>
        LineBreak,
        /// <summary>The choice list starts here (0x0B): every line after it is one option of a query menu.</summary>
        ChoicesStart,
        /// <summary>A numeric message parameter (0x0A n): the packet's or event's number <see cref="EventMessageSegment.Argument"/>.</summary>
        Number,
        /// <summary>A named thing (0x01 block): <see cref="EventMessageSegment.Code"/> says what kind, <see cref="EventMessageSegment.Values"/> which one.</summary>
        Name,
        /// <summary>One of several alternatives (0x0C n "[a/b/c]"), picked by number parameter <see cref="EventMessageSegment.Argument"/>.</summary>
        Selector,
        /// <summary>The message waits for the player to confirm before the event continues (0x7F 0x31).</summary>
        Prompt,
        /// <summary>The player's name (0x08).</summary>
        PlayerName,
        /// <summary>The speaking NPC's name (0x09).</summary>
        NpcName,
        /// <summary>The name of a party member or another entity the event names (0x19 n, 0x7F 0x93).</summary>
        EntityName,
        /// <summary>A text colour change (0x1F c).</summary>
        Colour,
        /// <summary>An inline icon (0xEF n): elements, auto-translate brackets and the like.</summary>
        Icon,
        /// <summary>
        /// The message closes by itself after <see cref="EventMessageSegment.Argument"/> seconds (0x7F 0x34 n), as the
        /// intro narration does.
        /// </summary>
        AutoClose,
        /// <summary>
        /// Where the event message mode shows the message (0x02 x:u16 0x03 y:u16): <see cref="EventMessageSegment.Values"/>
        /// holds x and y.
        /// </summary>
        Position,
        /// <summary>
        /// One of two words picked by the player's sex (0x7F 0x85 "[male/female]", e.g. "[his/her]"):
        /// <see cref="EventMessageSegment.Alternatives"/>.
        /// </summary>
        GenderSelector,
        /// <summary>A code this decoder knows the length of but not the meaning.</summary>
        Unknown,
    }

    /// <summary>One piece of a decoded dialog message.</summary>
    public readonly record struct EventMessageSegment(
        EventMessageSegmentKind Kind,
        string Text = "",
        int Argument = 0,
        byte Code = 0,
        IReadOnlyList<int>? Values = null,
        IReadOnlyList<string>? Alternatives = null);

    /// <summary>A decoded dialog message: its pieces in order.</summary>
    public sealed class EventMessage
    {
        public EventMessage(IReadOnlyList<EventMessageSegment> segments)
        {
            Segments = segments;
            foreach (var segment in segments)
            {
                if (segment.Kind == EventMessageSegmentKind.Prompt) HasPrompt = true;
                if (segment.Kind == EventMessageSegmentKind.ChoicesStart) HasChoices = true;
                if (segment.Kind == EventMessageSegmentKind.AutoClose) AutoCloseSeconds = segment.Argument;
                if (segment.Kind == EventMessageSegmentKind.Position && segment.Values is { Count: 2 } at) Position = (at[0], at[1]);
            }
        }

        public IReadOnlyList<EventMessageSegment> Segments { get; }

        /// <summary>Whether the message ends with a wait for the player's confirm.</summary>
        public bool HasPrompt { get; }

        /// <summary>Where the event message mode shows the message (0x02 code), or null.</summary>
        public (int X, int Y)? Position { get; }

        /// <summary>Seconds after which the message closes by itself (0x7F 0x34 n), or null when it waits (or does not).</summary>
        public int? AutoCloseSeconds { get; }

        /// <summary>Whether the message carries a choice list (a query menu's options).</summary>
        public bool HasChoices { get; }

        /// <summary>The text with every code dropped and line breaks turned into spaces (for logs and tests).</summary>
        public string ToPlainText()
        {
            var sb = new StringBuilder();
            foreach (var segment in Segments)
            {
                switch (segment.Kind)
                {
                    case EventMessageSegmentKind.Text: sb.Append(segment.Text); break;
                    case EventMessageSegmentKind.LineBreak: sb.Append(' '); break;
                    case EventMessageSegmentKind.ChoicesStart: sb.Append(" / "); break;
                    case EventMessageSegmentKind.Selector when segment.Alternatives is { Count: > 0 } alts: sb.Append(alts[0]); break;
                }
            }
            while (sb.ToString().Contains("  ")) sb.Replace("  ", " ");
            return sb.ToString().Trim();
        }
    }

    /// <summary>
    /// Decodes the control-coded strings of a zone dialog table (<see cref="ZoneDialogTable"/>).
    /// <para>
    /// Codes were worked out from the retail English tables (2026-09-28) with the XiEvents 0x0024 query handler
    /// (https://github.com/atom0s/XiEvents) for the 0x7F code lengths and xi-tinkerer's decoder
    /// (https://github.com/InoUno/xi-tinkerer, read for the format only) for the 0x01 block layout:
    /// </para>
    /// <list type="bullet">
    /// <item>0x00 ends the message; 0x07 breaks the line; 0x0B starts the choice list of a query.</item>
    /// <item>0x01 len type ... names something: the type byte says what ('#' item, '3' key item, '8' zone in
    /// the tables read so far), then sub-blocks of (length ^ 0x80), that many value bytes each ^ 0x80 (little-endian),
    /// and a closing byte.</item>
    /// <item>0x0A n prints number parameter n; 0x0C n "[a/b/c]" prints the alternative number parameter n picks;
    /// 0x08 / 0x09 the player's / NPC's name; 0x19 n another entity's name; 0x1F c sets a colour; the other codes
    /// below 0x20 take one argument byte.</item>
    /// <item>0x7F 0x31 (then 0x00) is the prompt: the event waits for the player's confirm. 0x7F 0x34 n closes the message
    /// after n seconds (the Southern San d'Oria intro's narration carries 9 and 5; the maintainer's retail recording,
    /// 2026-09-30, shows those lines for 9.2-9.3 s and 5.1 s). 0x7F 0x85 "[a/b]" picks by the player's sex; 0x02 x 0x03 y
    /// (six bytes) places a line on the screen. Other 0x7F codes are two
    /// bytes long except 0x34-0x36, 0x80, 0x84, 0x86, 0x8C and 0x92 (three) and 0x38 (four).</item>
    /// <item>0xEF n is an icon; 0xFD ... 0xFD (six bytes) an auto-translate resource; everything else is Shift-JIS text.</item>
    /// </list>
    /// </summary>
    public static class EventMessageDecoder
    {
        private static readonly Encoding Cp932 = CreateCp932();

        private static Encoding CreateCp932()
        {
            try
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                return Encoding.GetEncoding(932);
            }
            catch
            {
                return Encoding.Latin1;
            }
        }

        public static EventMessage Decode(ReadOnlySpan<byte> raw)
        {
            var segments = new List<EventMessageSegment>();
            var text = new List<byte>();
            int i = 0;
            while (i < raw.Length)
            {
                byte b = raw[i];
                if (b == 0x00) break;

                if (b >= 0x20 && b != 0x7F && b != 0xEF && b != 0xFD)
                {
                    text.Add(b);
                    // Shift-JIS lead byte: the trail byte is part of the character, whatever its value.
                    if (IsSjisLead(b) && i + 1 < raw.Length)
                    {
                        text.Add(raw[i + 1]);
                        i += 2;
                    }
                    else
                    {
                        i++;
                    }
                    continue;
                }

                FlushText(segments, text);
                switch (b)
                {
                    case 0x01:
                        i = DecodeNameBlock(raw, i, segments);
                        break;
                    case 0x02:
                        // 0x02 x:u16 0x03 y:u16: the narration's screen position (the Southern San d'Oria intro's lines
                        // start 02 50 00 03 54 01, x 80, y 340, where the maintainer's retail recording shows them).
                        if (i + 5 < raw.Length && raw[i + 3] == 0x03)
                        {
                            int x = raw[i + 1] | (raw[i + 2] << 8), y = raw[i + 4] | (raw[i + 5] << 8);
                            segments.Add(new EventMessageSegment(EventMessageSegmentKind.Position, Code: 0x02, Values: new[] { x, y }));
                            i += 6;
                        }
                        else
                        {
                            segments.Add(new EventMessageSegment(EventMessageSegmentKind.Unknown, Code: 0x02));
                            i += 5;
                        }
                        break;
                    case 0x07:
                        segments.Add(new EventMessageSegment(EventMessageSegmentKind.LineBreak));
                        i++;
                        break;
                    case 0x08:
                        segments.Add(new EventMessageSegment(EventMessageSegmentKind.PlayerName));
                        i++;
                        break;
                    case 0x09:
                        segments.Add(new EventMessageSegment(EventMessageSegmentKind.NpcName));
                        i++;
                        break;
                    case 0x0B:
                        segments.Add(new EventMessageSegment(EventMessageSegmentKind.ChoicesStart));
                        i++;
                        break;
                    case 0x0A:
                        segments.Add(new EventMessageSegment(EventMessageSegmentKind.Number, Argument: i + 1 < raw.Length ? raw[i + 1] : 0));
                        i += 2;
                        break;
                    case 0x0C:
                        i = DecodeSelector(raw, i, segments);
                        break;
                    case 0x19:
                        segments.Add(new EventMessageSegment(EventMessageSegmentKind.EntityName, Argument: i + 1 < raw.Length ? raw[i + 1] : 0, Code: 0x19));
                        i += 2;
                        break;
                    case 0x1F:
                        segments.Add(new EventMessageSegment(EventMessageSegmentKind.Colour, Argument: i + 1 < raw.Length ? raw[i + 1] : 0));
                        i += 2;
                        break;
                    case 0x7F:
                        i = DecodeExtended(raw, i, segments);
                        break;
                    case 0xEF:
                        segments.Add(new EventMessageSegment(EventMessageSegmentKind.Icon, Argument: i + 1 < raw.Length ? raw[i + 1] : 0));
                        i += 2;
                        break;
                    case 0xFD:
                        segments.Add(new EventMessageSegment(EventMessageSegmentKind.Unknown, Code: 0xFD));
                        i += i + 5 < raw.Length && raw[i + 5] == 0xFD ? 6 : 1;
                        break;
                    default:
                        // The remaining codes below 0x20 carry one argument byte.
                        segments.Add(new EventMessageSegment(EventMessageSegmentKind.Unknown, Argument: i + 1 < raw.Length ? raw[i + 1] : 0, Code: b));
                        i += 2;
                        break;
                }
            }
            FlushText(segments, text);
            return new EventMessage(segments);
        }

        private static bool IsSjisLead(byte b) => (b >= 0x81 && b <= 0x9F) || (b >= 0xE0 && b <= 0xFC);

        private static void FlushText(List<EventMessageSegment> segments, List<byte> text)
        {
            if (text.Count == 0) return;
            segments.Add(new EventMessageSegment(EventMessageSegmentKind.Text, Cp932.GetString(text.ToArray())));
            text.Clear();
        }

        private static int DecodeNameBlock(ReadOnlySpan<byte> raw, int i, List<EventMessageSegment> segments)
        {
            if (i + 2 >= raw.Length)
            {
                segments.Add(new EventMessageSegment(EventMessageSegmentKind.Unknown, Code: 0x01));
                return raw.Length;
            }
            int length = raw[i + 1];
            byte type = raw[i + 2];
            int end = Math.Min(raw.Length, i + 2 + Math.Max(1, length));
            var values = new List<int>();
            int p = i + 3;
            while (p < end)
            {
                int subLength = raw[p] ^ 0x80;
                p++;
                if (subLength <= 0 || subLength > 4 || p + subLength > end) break;
                int value = 0;
                for (int k = 0; k < subLength; k++) value |= (raw[p + k] ^ 0x80) << (8 * k);
                values.Add(value);
                p += subLength + 1; // the value bytes and the closing byte
            }
            segments.Add(new EventMessageSegment(EventMessageSegmentKind.Name, Argument: values.Count > 0 ? values[0] : 0, Code: type, Values: values));
            return end;
        }

        private static int DecodeSelector(ReadOnlySpan<byte> raw, int i, List<EventMessageSegment> segments)
        {
            int argument = i + 1 < raw.Length ? raw[i + 1] : 0;
            int p = i + 2;
            if (p < raw.Length && raw[p] == (byte)'[')
            {
                int close = raw.Slice(p).IndexOf((byte)']');
                if (close > 0)
                {
                    string inner = Cp932.GetString(raw.Slice(p + 1, close - 1));
                    segments.Add(new EventMessageSegment(EventMessageSegmentKind.Selector, Argument: argument, Alternatives: inner.Split('/')));
                    return p + close + 1;
                }
            }
            segments.Add(new EventMessageSegment(EventMessageSegmentKind.Selector, Argument: argument, Alternatives: Array.Empty<string>()));
            return p;
        }

        private static int DecodeExtended(ReadOnlySpan<byte> raw, int i, List<EventMessageSegment> segments)
        {
            if (i + 1 >= raw.Length) return raw.Length;
            byte code = raw[i + 1];
            switch (code)
            {
                case 0x31:
                    segments.Add(new EventMessageSegment(EventMessageSegmentKind.Prompt));
                    return i + 2 < raw.Length && raw[i + 2] == 0x00 ? i + 3 : i + 2;
                case 0x93:
                    segments.Add(new EventMessageSegment(EventMessageSegmentKind.EntityName, Code: 0x93));
                    return i + 2;
                case 0x85:
                {
                    // "[his/her]": the player's sex picks (the retail recording shows a Mithra's lines with "her").
                    int p = i + 2;
                    if (p < raw.Length && raw[p] == (byte)'[')
                    {
                        int close = raw.Slice(p).IndexOf((byte)']');
                        if (close > 0)
                        {
                            string inner = Cp932.GetString(raw.Slice(p + 1, close - 1));
                            segments.Add(new EventMessageSegment(EventMessageSegmentKind.GenderSelector, Code: code, Alternatives: inner.Split('/')));
                            return p + close + 1;
                        }
                    }
                    segments.Add(new EventMessageSegment(EventMessageSegmentKind.Unknown, Code: code));
                    return i + 2;
                }
                case 0xFB:
                case 0xFC:
                    return i + 2; // entity name wrap markers
                case 0x34:
                    segments.Add(new EventMessageSegment(EventMessageSegmentKind.AutoClose, Argument: i + 2 < raw.Length ? raw[i + 2] : 0, Code: code));
                    return i + 3;
                case 0x35:
                case 0x36:
                case 0x80:
                case 0x84:
                case 0x86:
                case 0x8C:
                case 0x92:
                    segments.Add(new EventMessageSegment(EventMessageSegmentKind.Unknown, Argument: i + 2 < raw.Length ? raw[i + 2] : 0, Code: code));
                    return i + 3;
                case 0x38:
                    segments.Add(new EventMessageSegment(EventMessageSegmentKind.Unknown, Code: code));
                    return i + 4;
                default:
                    segments.Add(new EventMessageSegment(EventMessageSegmentKind.Unknown, Code: code));
                    return i + 2;
            }
        }
    }
}
