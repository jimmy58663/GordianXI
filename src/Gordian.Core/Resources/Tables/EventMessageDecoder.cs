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
        /// <summary>
        /// A numeric message parameter: the packet's or event's number <see cref="EventMessageSegment.Argument"/>.
        /// <see cref="EventMessageSegment.Code"/> says how it is written: 0x0A (0x0A n) plain, and the 0x7F n codes 0x94
        /// two digits, 0x95 hexadecimal, 0x96 binary, 0x99 four digits.
        /// </summary>
        Number,
        /// <summary>A named thing (0x01 block): <see cref="EventMessageSegment.Code"/> says what kind, <see cref="EventMessageSegment.Values"/> which one.</summary>
        Name,
        /// <summary>
        /// One of several alternatives picked by number parameter <see cref="EventMessageSegment.Argument"/>:
        /// <see cref="EventMessageSegment.Code"/> 0x0C (0x0C n "[a/b/c]") picks alternative n, 0x92 and 0x86 (0x7F 0x92 n
        /// "[a/b]") the first when the number is 1, else the second.
        /// </summary>
        Selector,
        /// <summary>The message waits for the player to confirm before the event continues (0x7F 0x31, 0x32, 0x33, 0x37).</summary>
        Prompt,
        /// <summary>The player's name (0x08).</summary>
        PlayerName,
        /// <summary>The speaking NPC's name (0x09).</summary>
        NpcName,
        /// <summary>
        /// The name of another entity: <see cref="EventMessageSegment.Code"/> 0x19 (0x19 n) party / alliance member n,
        /// 0x18 (0x18 n) the entity whose server id is number parameter n, 0x93 (0x7F 0x93) not known.
        /// </summary>
        EntityName,
        /// <summary>String parameter <see cref="EventMessageSegment.Argument"/> (0x1C n): an event's string (S2C 0x033).</summary>
        EventString,
        /// <summary>
        /// The text waits <see cref="EventMessageSegment.Argument"/> seconds (0x7F 0x35 n, 0x7F 0x36 n): a pause inside
        /// the line, or, at the end of a line without a prompt, how long it stays before it closes.
        /// </summary>
        Pause,
        /// <summary>
        /// The next substitution starts with a capital letter (0x7F 0x80 1); <see cref="EventMessageSegment.Argument"/>
        /// holds the mode.
        /// </summary>
        CaseMode,
        /// <summary>
        /// A field of a date and time held in number parameter <see cref="EventMessageSegment.Argument"/> as seconds
        /// since 2001-12-31 15:00 UTC, shown in local time (0x7F 0xA0-0xAA n): <see cref="EventMessageSegment.Code"/> 0xA0
        /// year, 0xA1 month, 0xA2 day (checked against retail), 0xA3 / 0xA8 hour, 0xA4 / 0xA9 minute,
        /// 0xA5 / 0xAA second, 0xA6 / 0xA7 month / day (0xA6-0xAA two digits).
        /// </summary>
        DateField,
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
        /// <summary>
        /// One of two words picked by the sex of message entity <see cref="EventMessageSegment.Argument"/> (0x7F 0x90
        /// "[his/her]" the first entity, the emote's caster; 0x7F 0x91 the second, its target):
        /// <see cref="EventMessageSegment.Alternatives"/>.
        /// </summary>
        EntityGenderSelector,
        /// <summary>
        /// The article of message entity <see cref="EventMessageSegment.Argument"/> (0x7F 0x88 n "[the /]"): the first
        /// alternative for an entity whose name takes an article (a monster), the second for one that does not (a
        /// player). <see cref="EventMessageSegment.Alternatives"/> holds the list.
        /// </summary>
        ArticleSelector,
        /// <summary>The compass direction the first message entity faces (0x1D: the untargeted <c>/point</c> line).</summary>
        Heading,
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
            int? paused = null;
            foreach (var segment in segments)
            {
                if (segment.Kind == EventMessageSegmentKind.Prompt) HasPrompt = true;
                if (segment.Kind == EventMessageSegmentKind.ChoicesStart) HasChoices = true;
                if (segment.Kind == EventMessageSegmentKind.AutoClose) AutoCloseSeconds = segment.Argument;
                if (segment.Kind == EventMessageSegmentKind.Pause) paused = (paused ?? 0) + segment.Argument;
                if (segment.Kind == EventMessageSegmentKind.Position && segment.Values is { Count: 2 } at) Position = (at[0], at[1]);
            }
            // A line without a prompt that pauses (0x7F 0x36 n at its end: "Shhh! Be quiet!" 0x7F 0x36 0x01) stays up for
            // its pauses before it closes; with a prompt the pauses are only beats inside the line.
            if (AutoCloseSeconds == null && !HasPrompt && paused is int seconds) AutoCloseSeconds = seconds;
        }

        public IReadOnlyList<EventMessageSegment> Segments { get; }

        /// <summary>Whether the message ends with a wait for the player's confirm.</summary>
        public bool HasPrompt { get; }

        /// <summary>Where the event message mode shows the message (0x02 code), or null.</summary>
        public (int X, int Y)? Position { get; }

        /// <summary>
        /// Seconds after which the message closes by itself (0x7F 0x34 n; or the pauses 0x7F 0x35 / 0x36 n of a message
        /// without a prompt), or null when it waits (or does not).
        /// </summary>
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
    /// Codes were worked out from the retail English tables (2026-09-28, 2026-10-03) with the XiEvents 0x0024 query
    /// handler (https://github.com/atom0s/XiEvents) and xi-tools' dialog decode table (https://github.com/vekien/xi-tools,
    /// docs/dialog/format.md and the parameter counts of its Shift-JIS event table) for the code lengths, and
    /// xi-tinkerer's decoder (https://github.com/InoUno/xi-tinkerer, read for the format only) for the 0x01 block layout:
    /// </para>
    /// <list type="bullet">
    /// <item>0x00 ends the message; 0x07 breaks the line; 0x0B starts the choice list of a query.</item>
    /// <item>0x01 len type ... names something: the type byte says what ('#' item, '3' key item, '8' zone and the
    /// rest listed in docs/events/message-codes.md), then sub-blocks of (length ^ 0x80), that many value bytes each ^ 0x80
    /// (little-endian), and a closing byte.</item>
    /// <item>0x0A n prints number parameter n; 0x0C n picks one alternative of the next "[a/b/c]" by number parameter n;
    /// 0x08 / 0x09 the player's / NPC's name; 0x18 n the name of the entity whose server id is parameter n; 0x19 n party
    /// member n's name; 0x1C n string parameter n; 0x1F c sets a colour; 0x02 x:u16 / 0x03 y:u16 set the line's screen
    /// position; the other codes below 0x20 take one argument byte.</item>
    /// <item>0x7F 0x31 (also 0x32, 0x33, 0x37) is the prompt: the event waits for the player's confirm, and the 0x00 after it
    /// ends the string. 0x7F 0x34 n closes the message after n seconds (the Southern San d'Oria intro's narration carries 9
    /// and 5; the maintainer's retail recording, 2026-09-30, shows those lines for 9.2-9.3 s and 5.1 s); 0x7F 0x35 / 0x36 n
    /// pause n seconds. 0x7F 0x85 "[a/b]" picks by the player's sex, 0x7F 0x92 n "[a/b]" by whether number parameter n
    /// is 1; 0x7F 0x80 1 capitalises the next substitution; 0x7F 0x94 / 0x95 / 0x96 / 0x99 n print number n with two
    /// digits, in hexadecimal, in binary, with four digits; 0x7F 0xA0-0xAA n print a field of the date in parameter n.
    /// 0x7F codes are two bytes long except those taking an argument: 0x34-0x36, 0x80, 0x81, 0x84, 0x86-0x88, 0x8C,
    /// 0x8F, 0x92, 0x94-0x97, 0x99, 0xA0-0xAC, 0xB0, 0xB1, 0xB4, 0xB5 (three) and 0x38 (four).</item>
    /// <item>The client's own tables (emotes ROM/27/70, system messages ROM/27/76) add codes about the message's entities:
    /// 0x01 kinds 0x10 / 0x11 with no value name entity 0 / 1 (an emote's caster and target), 0x7F 0x88 n "[the /]" picks
    /// entity n's article, 0x7F 0x90 / 0x91 "[his/her]" entity 0's / 1's sex, 0x7F 0x86 n "[a/b]" singular or plural by
    /// number n, 0x12 n prints number n and 0x1D the heading (names from xi-tools' Shift-JIS code table; the meanings
    /// were read from those tables, 2026-10-03).</item>
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

        /// <summary>
        /// A selector code waiting for its "[a/b/...]" list. The list need not follow the code at once: retail lines put
        /// text or a space between them ("{0A 02} {7F 92 02}credit[/s]", "{7F 85} [Lord/Lady]"), always on the same line.
        /// </summary>
        private readonly record struct PendingSelector(EventMessageSegmentKind Kind, byte Code, int Argument);

        public static EventMessage Decode(ReadOnlySpan<byte> raw)
        {
            var segments = new List<EventMessageSegment>();
            var text = new List<byte>();
            PendingSelector? pending = null;
            int i = 0;
            while (i < raw.Length)
            {
                byte b = raw[i];
                if (b == 0x00) break;

                if (b >= 0x20 && b != 0x7F && b != 0xEF && b != 0xFD)
                {
                    if (b == (byte)'[' && pending is { } selector && TryReadAlternatives(raw, i, out var alternatives, out int next))
                    {
                        FlushText(segments, text);
                        segments.Add(new EventMessageSegment(selector.Kind, Argument: selector.Argument, Code: selector.Code, Alternatives: alternatives));
                        pending = null;
                        i = next;
                        continue;
                    }
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
                            // A lone set_x (or set_y below) is three bytes (xi-tools docs/dialog/format.md).
                            segments.Add(new EventMessageSegment(EventMessageSegmentKind.Unknown, Argument: ReadU16(raw, i + 1), Code: 0x02));
                            i += 3;
                        }
                        break;
                    case 0x03:
                        segments.Add(new EventMessageSegment(EventMessageSegmentKind.Unknown, Argument: ReadU16(raw, i + 1), Code: 0x03));
                        i += 3;
                        break;
                    case 0x07:
                        segments.Add(new EventMessageSegment(EventMessageSegmentKind.LineBreak));
                        pending = null;
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
                        pending = null;
                        i++;
                        break;
                    case 0x0A:
                        segments.Add(new EventMessageSegment(EventMessageSegmentKind.Number, Argument: ArgumentAt(raw, i + 1), Code: 0x0A));
                        i += 2;
                        break;
                    case 0x0C:
                        pending = new PendingSelector(EventMessageSegmentKind.Selector, 0x0C, ArgumentAt(raw, i + 1));
                        i += 2;
                        break;
                    case 0x12:
                        // Number parameter n (xi-tools NUMBER): the system and emote tables (ROM/27/76: "Executing logout
                        // in {12 00} seconds.", "The compass reads: X:{12 00} Y:{12 01}...") use it where zone tables use 0x0A.
                        segments.Add(new EventMessageSegment(EventMessageSegmentKind.Number, Argument: ArgumentAt(raw, i + 1), Code: 0x12));
                        i += 2;
                        break;
                    case 0x1D:
                        // The heading (xi-tools HEADING, which gives it one argument byte). Its one use in the English
                        // tables is the untargeted /point line of the emote table (ROM/27/70 message 1: "{caster} points
                        // {1D}."), where the byte after it is the line's full stop, so it takes none here.
                        segments.Add(new EventMessageSegment(EventMessageSegmentKind.Heading, Code: 0x1D));
                        i++;
                        break;
                    case 0x18:
                    case 0x19:
                        segments.Add(new EventMessageSegment(EventMessageSegmentKind.EntityName, Argument: ArgumentAt(raw, i + 1), Code: b));
                        i += 2;
                        break;
                    case 0x1C:
                        segments.Add(new EventMessageSegment(EventMessageSegmentKind.EventString, Argument: ArgumentAt(raw, i + 1), Code: 0x1C));
                        i += 2;
                        break;
                    case 0x1F:
                        segments.Add(new EventMessageSegment(EventMessageSegmentKind.Colour, Argument: ArgumentAt(raw, i + 1)));
                        i += 2;
                        break;
                    case 0x7F:
                        i = DecodeExtended(raw, i, segments, ref pending);
                        break;
                    case 0xEF:
                        segments.Add(new EventMessageSegment(EventMessageSegmentKind.Icon, Argument: ArgumentAt(raw, i + 1)));
                        i += 2;
                        break;
                    case 0xFD:
                        segments.Add(new EventMessageSegment(EventMessageSegmentKind.Unknown, Code: 0xFD));
                        i += i + 5 < raw.Length && raw[i + 5] == 0xFD ? 6 : 1;
                        break;
                    default:
                        // The remaining codes below 0x20 carry one argument byte.
                        segments.Add(new EventMessageSegment(EventMessageSegmentKind.Unknown, Argument: ArgumentAt(raw, i + 1), Code: b));
                        i += 2;
                        break;
                }
            }
            FlushText(segments, text);
            return new EventMessage(segments);
        }

        private static bool IsSjisLead(byte b) => (b >= 0x81 && b <= 0x9F) || (b >= 0xE0 && b <= 0xFC);

        private static int ArgumentAt(ReadOnlySpan<byte> raw, int index) => index < raw.Length ? raw[index] : 0;

        private static int ReadU16(ReadOnlySpan<byte> raw, int index) => ArgumentAt(raw, index) | (ArgumentAt(raw, index + 1) << 8);

        private static void FlushText(List<EventMessageSegment> segments, List<byte> text)
        {
            if (text.Count == 0) return;
            segments.Add(new EventMessageSegment(EventMessageSegmentKind.Text, Cp932.GetString(text.ToArray())));
            text.Clear();
        }

        /// <summary>Reads the "[a/b/...]" list at <paramref name="open"/> (a '['); false when it is not closed.</summary>
        private static bool TryReadAlternatives(ReadOnlySpan<byte> raw, int open, out string[] alternatives, out int next)
        {
            int close = raw.Slice(open).IndexOf((byte)']');
            int end = raw.Slice(open).IndexOf((byte)0x00);
            if (close <= 0 || (end >= 0 && end < close))
            {
                alternatives = Array.Empty<string>();
                next = open;
                return false;
            }
            alternatives = Cp932.GetString(raw.Slice(open + 1, close - 1)).Split('/');
            next = open + close + 1;
            return true;
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

        private static int DecodeExtended(ReadOnlySpan<byte> raw, int i, List<EventMessageSegment> segments, ref PendingSelector? pending)
        {
            if (i + 1 >= raw.Length) return raw.Length;
            byte code = raw[i + 1];
            int argument = ArgumentAt(raw, i + 2);
            switch (code)
            {
                case 0x31:
                case 0x32:
                case 0x33:
                case 0x37:
                    // The manual prompts (xi-tools docs/dialog/format.md; retail uses 0x31). The 0x00 that follows ends the
                    // string, as it does in retail: what comes after it is padding or another sub-string.
                    segments.Add(new EventMessageSegment(EventMessageSegmentKind.Prompt, Code: code));
                    return i + 2;
                case 0x34:
                    segments.Add(new EventMessageSegment(EventMessageSegmentKind.AutoClose, Argument: argument, Code: code));
                    return i + 3;
                case 0x35:
                case 0x36:
                    segments.Add(new EventMessageSegment(EventMessageSegmentKind.Pause, Argument: argument, Code: code));
                    return i + 3;
                case 0x38:
                    // Two argument bytes (7F 38 B4 00 = 180, C8 00, F0 00 at the end of prompt-less cutscene lines); the unit
                    // is not known.
                    segments.Add(new EventMessageSegment(EventMessageSegmentKind.Unknown, Argument: ReadU16(raw, i + 2), Code: code));
                    return i + 4;
                case 0x80:
                    // 7F 80 01 stands before the substitutions that open a sentence or a menu row ("Obtained key item:
                    // {7F 80 01}<key item>", "{7F 80 01}{01 01 01} {item} will be used to..."): a capital first letter.
                    segments.Add(new EventMessageSegment(EventMessageSegmentKind.CaseMode, Argument: argument, Code: code));
                    return i + 3;
                case 0x85:
                    // "[his/her]": the player's sex picks (the retail recording shows a Mithra's lines with "her").
                    pending = new PendingSelector(EventMessageSegmentKind.GenderSelector, code, 0);
                    return i + 2;
                case 0x86:
                case 0x92:
                    // 0x92 and 0x86 (xi-tools ABILITY_PLURAL_SELECT; the system table's "{12 00} {7F 86 00}[second/seconds]
                    // until position reset.") both pick the singular when number n is 1.
                    pending = new PendingSelector(EventMessageSegmentKind.Selector, code, argument);
                    return i + 3;
                case 0x88:
                    // "[the /]" before an entity's name (xi-tools NPC_PROPER_SELECT): the emote lines' "waves to
                    // {7F 88 01}[the /]{01 01 11}" and the system table's "You find ... on {7F 88 01}[the /]{01 01 11}.".
                    pending = new PendingSelector(EventMessageSegmentKind.ArticleSelector, code, argument);
                    return i + 3;
                case 0x90:
                case 0x91:
                    // "[his/her]" by an entity's sex (xi-tools NPC0_GENDER / NPC1_GENDER): "claps {7F 90}[his/her] hands".
                    pending = new PendingSelector(EventMessageSegmentKind.EntityGenderSelector, code, code - 0x90);
                    return i + 2;
                case 0x93:
                    segments.Add(new EventMessageSegment(EventMessageSegmentKind.EntityName, Code: code));
                    return i + 2;
                case 0x94:
                case 0x95:
                case 0x96:
                case 0x99:
                    segments.Add(new EventMessageSegment(EventMessageSegmentKind.Number, Argument: argument, Code: code));
                    return i + 3;
                case >= 0xA0 and <= 0xAA:
                    segments.Add(new EventMessageSegment(EventMessageSegmentKind.DateField, Argument: argument, Code: code));
                    return i + 3;
                case 0x81:
                case 0x84:
                case 0x87:
                case 0x8C:
                case 0x8F:
                case 0x97:
                case 0xAB:
                case 0xAC:
                case 0xB0:
                case 0xB1:
                case 0xB4:
                case 0xB5:
                    segments.Add(new EventMessageSegment(EventMessageSegmentKind.Unknown, Argument: argument, Code: code));
                    return i + 3;
                case 0xFB:
                case 0xFC:
                    return i + 2; // entity name wrap markers
                default:
                    segments.Add(new EventMessageSegment(EventMessageSegmentKind.Unknown, Code: code));
                    return i + 2;
            }
        }
    }
}
