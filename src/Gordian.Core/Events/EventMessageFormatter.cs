// src/Gordian.Core/Events/EventMessageFormatter.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Gordian.Core.Resources.Tables;

namespace Gordian.Core.Events
{
    /// <summary>What a dialog message's codes substitute: the numbers, names and entities of the moment.</summary>
    public interface IEventMessageContext
    {
        /// <summary>Number parameter n (a packet's num[n], or an event's work value).</summary>
        int GetNumber(int index);

        /// <summary>The player's name (0x08).</summary>
        string PlayerName { get; }

        /// <summary>The speaking entity's name (0x09), empty when there is none.</summary>
        string NpcName { get; }

        /// <summary>
        /// The name of party / alliance member n of a 0x19 n code (0 the player, 1-5 the other members of the player's
        /// party, 6-11 and 12-17 the alliance's other two parties), or null.
        /// </summary>
        string? GetEntityName(int index);

        /// <summary>
        /// The name of the thing a 0x01 block refers to: <paramref name="kind"/> is its type byte and
        /// <paramref name="id"/> the thing's id. The formatter asks only for '#' (0x23) item name, 0x24 item log name
        /// (singular), 0x25 item log name (plural), '3' (0x33) key item name, '5' (0x35) key item plural, '8' (0x38) zone
        /// name; the other kinds are mapped onto these first. Null when unknown.
        /// </summary>
        string? ResolveName(byte kind, int id);

        /// <summary>Whether the player is female (picks 0x7F 0x85 "[his/her]"), or null when unknown.</summary>
        bool? PlayerIsFemale => null;

        /// <summary>String parameter n of a 0x1C n code (an event's S2C 0x033 strings), or null.</summary>
        string? GetEventString(int index) => null;

        /// <summary>The name of the entity with this server id (a 0x18 n code reads the id from number n), or null.</summary>
        string? GetEntityNameById(uint serverId) => null;

        /// <summary>The time zone the date codes (0x7F 0xA0-0xAA) are shown in: the machine's local zone, as retail does.</summary>
        TimeZoneInfo TimeZone => TimeZoneInfo.Local;
    }

    /// <summary>
    /// Turns a decoded dialog message into log lines, and a query into its comment lines and options, with the
    /// codes substituted from an <see cref="IEventMessageContext"/>.
    /// A 0x01 name block's first value is the index of the number parameter that holds the thing's id (LandSandBoat
    /// sends the item id as the first parameter of ITEM_OBTAINED, whose text names item parameter 0).
    /// </summary>
    public static class EventMessageFormatter
    {
        /// <summary>The item kind byte of a 0x01 name block.</summary>
        public const byte ItemKind = (byte)'#';
        /// <summary>An item's singular log name ("pile of chocobo bedding"), after an article (0x01 kind 0x24).</summary>
        public const byte ItemLogNameKind = 0x24;
        /// <summary>An item's plural log name ("piles of chocobo bedding", 0x01 kind 0x25).</summary>
        public const byte ItemPluralKind = 0x25;
        public const byte KeyItemKind = (byte)'3';
        /// <summary>A key item's plural name ("traverser stones", 0x01 kind 0x35).</summary>
        public const byte KeyItemPluralKind = (byte)'5';
        public const byte ZoneKind = (byte)'8';

        /// <summary>The 0x01 kind with no value (01 01 01) that stands for the article of the item tag after it.</summary>
        public const byte ArticleKind = 0x01;

        /// <summary>The Vana'diel epoch the date codes count from: 2001-12-31 15:00 UTC (2002-01-01 00:00 JST).</summary>
        private static readonly DateTimeOffset VanadielEpochUtc = new(2001, 12, 31, 15, 0, 0, TimeSpan.Zero);

        /// <summary>Every line of the message (choices included, one per line).</summary>
        public static List<string> FormatLines(EventMessage message, IEventMessageContext context)
        {
            var (comments, choices) = FormatQuery(message, context);
            comments.AddRange(choices);
            return comments;
        }

        /// <summary>The lines before the choice list, and the choices (empty without a 0x0B code).</summary>
        public static (List<string> Comments, List<string> Choices) FormatQuery(EventMessage message, IEventMessageContext context)
        {
            var comments = new List<string>();
            var choices = new List<string>();
            var line = new StringBuilder();
            bool inChoices = false;
            int caseMode = 0;

            void Flush()
            {
                string text = line.ToString();
                line.Clear();
                if (inChoices) choices.Add(text);
                else if (text.Length > 0 || comments.Count > 0) comments.Add(text);
            }

            // A substitution, capitalised when a 0x7F 0x80 1 code stands before it.
            void Substitute(string text)
            {
                if (caseMode == 1 && text.Length > 0) text = char.ToUpperInvariant(text[0]) + text.Substring(1);
                caseMode = 0;
                line.Append(text);
            }

            var segments = message.Segments;
            for (int s = 0; s < segments.Count; s++)
            {
                var segment = segments[s];
                switch (segment.Kind)
                {
                    case EventMessageSegmentKind.Text:
                        line.Append(segment.Text);
                        break;
                    case EventMessageSegmentKind.LineBreak:
                        Flush();
                        break;
                    case EventMessageSegmentKind.ChoicesStart:
                        if (line.Length > 0) Flush();
                        inChoices = true;
                        break;
                    case EventMessageSegmentKind.Number:
                        Substitute(FormatNumber(segment.Code, context.GetNumber(segment.Argument)));
                        break;
                    case EventMessageSegmentKind.DateField:
                        Substitute(FormatDateField(segment.Code, context.GetNumber(segment.Argument), context.TimeZone));
                        break;
                    case EventMessageSegmentKind.Name:
                        Substitute(segment.Code == ArticleKind && segment.Values is not { Count: > 0 }
                            ? Article(segments, s, context)
                            : FormatName(segment, context));
                        break;
                    case EventMessageSegmentKind.Selector:
                    {
                        var alternatives = segment.Alternatives;
                        if (alternatives is { Count: > 0 })
                        {
                            int number = context.GetNumber(segment.Argument);
                            // 0x7F 0x92 "[singular/plural]": the first when the number is 1 (xi-tools docs/events/authoring.md).
                            int pick = segment.Code == 0x92 ? (number == 1 ? 0 : 1) : number;
                            Substitute(alternatives[Math.Clamp(pick, 0, alternatives.Count - 1)]);
                        }
                        break;
                    }
                    case EventMessageSegmentKind.GenderSelector when segment.Alternatives is { Count: >= 2 } words:
                        Substitute(context.PlayerIsFemale is bool female ? words[female ? 1 : 0] : $"[{string.Join('/', words)}]");
                        break;
                    case EventMessageSegmentKind.PlayerName:
                        Substitute(context.PlayerName);
                        break;
                    case EventMessageSegmentKind.NpcName:
                        Substitute(context.NpcName);
                        break;
                    case EventMessageSegmentKind.EntityName:
                    {
                        string? name = segment.Code switch
                        {
                            0x19 => context.GetEntityName(segment.Argument),
                            0x18 => context.GetEntityNameById(unchecked((uint)context.GetNumber(segment.Argument))),
                            _ => null, // 0x7F 0x93: whose name is not known
                        };
                        Substitute(name ?? string.Empty);
                        break;
                    }
                    case EventMessageSegmentKind.EventString:
                        Substitute(context.GetEventString(segment.Argument) ?? string.Empty);
                        break;
                    case EventMessageSegmentKind.CaseMode:
                        caseMode = segment.Argument;
                        break;
                }
            }
            if (line.Length > 0 || (inChoices && choices.Count == 0)) Flush();
            // Trailing empty lines (a prompt after a line break) are not rows.
            while (comments.Count > 0 && comments[^1].Length == 0) comments.RemoveAt(comments.Count - 1);
            while (choices.Count > 0 && choices[^1].Length == 0) choices.RemoveAt(choices.Count - 1);
            return (comments, choices);
        }

        /// <summary>
        /// A number as its code writes it: 0x0A plain, 0x94 two digits ("{0A 00}:{7F 94 01}" is a clock time), 0x99 four
        /// digits, 0x95 hexadecimal (upper case), 0x96 binary. The forms follow xi-tools' names for the codes
        /// (TWO_DIGIT_VALUE, FOUR_DIGIT_VALUE, HEX_VALUE, BINARY_VALUE) and the corpus lines that use them.
        /// </summary>
        public static string FormatNumber(byte code, int value) => code switch
        {
            0x94 => value.ToString("D2", CultureInfo.InvariantCulture),
            0x99 => value.ToString("D4", CultureInfo.InvariantCulture),
            0x95 => value.ToString("X", CultureInfo.InvariantCulture),
            0x96 => Convert.ToString(value, 2),
            _ => value.ToString(CultureInfo.InvariantCulture),
        };

        /// <summary>
        /// One field of a date held as seconds since the Vana'diel epoch, 2001-12-31 15:00 UTC (LandSandBoat sends the
        /// Mog Locker lease end that way), shown in the player's local time zone <paramref name="zone"/> (local time when
        /// null). Confirmed against retail on 2026-10-03: the lease line 6702 ("A0/A1/A2 A3:A9:AA") with 781790400 reads
        /// "10/9/2026 20:00:00" on a US Pacific (UTC-7) machine. So in the English client 0xA0 is the month, 0xA1 the day
        /// and 0xA2 the year, unpadded; 0xA3-0xA5 hour, minute, second unpadded; 0xA6-0xAA month, day, hour, minute,
        /// second with two digits (0xA9 / 0xAA confirmed, 0xA6-0xA8 corpus reading only).
        /// </summary>
        public static string FormatDateField(byte code, int seconds, TimeZoneInfo? zone = null)
        {
            var utc = VanadielEpochUtc.AddSeconds(seconds);
            var date = TimeZoneInfo.ConvertTime(utc, zone ?? TimeZoneInfo.Local);
            return code switch
            {
                0xA0 => date.Month.ToString(CultureInfo.InvariantCulture),
                0xA1 => date.Day.ToString(CultureInfo.InvariantCulture),
                0xA2 => date.Year.ToString(CultureInfo.InvariantCulture),
                0xA3 => date.Hour.ToString(CultureInfo.InvariantCulture),
                0xA4 => date.Minute.ToString(CultureInfo.InvariantCulture),
                0xA5 => date.Second.ToString(CultureInfo.InvariantCulture),
                0xA6 => date.Month.ToString("D2", CultureInfo.InvariantCulture),
                0xA7 => date.Day.ToString("D2", CultureInfo.InvariantCulture),
                0xA8 => date.Hour.ToString("D2", CultureInfo.InvariantCulture),
                0xA9 => date.Minute.ToString("D2", CultureInfo.InvariantCulture),
                0xAA => date.Second.ToString("D2", CultureInfo.InvariantCulture),
                _ => string.Empty,
            };
        }

        /// <summary>
        /// The text of a 0x01 tag. Item kinds 0x23-0x2A (xi-tools docs/dialog/format.md): 0x23 the item's name, 0x24 and
        /// 0x26-0x28 its singular log name, 0x25 its plural log name, 0x29 / 0x2A (two values: count parameter, item
        /// parameter) the singular or plural log name by the count; 0x03 / 0x04 a number; 0x33 / 0x36 a key item's name,
        /// 0x35 its plural; 0x37 / 0x38 a zone's name. Others, and names the context cannot resolve, print as
        /// &lt;kind id&gt;.
        /// </summary>
        private static string FormatName(EventMessageSegment segment, IEventMessageContext context)
        {
            var values = segment.Values;
            int first = values is { Count: > 0 } ? context.GetNumber(values[0]) : 0;
            byte kind = segment.Code;
            int id = first;
            byte resolveAs = kind;
            switch (kind)
            {
                case 0x03:
                case 0x04:
                    return first.ToString(CultureInfo.InvariantCulture);
                case 0x26:
                case 0x27:
                case 0x28:
                    resolveAs = ItemLogNameKind;
                    break;
                case 0x29:
                case 0x2A:
                    if (values is { Count: >= 2 })
                    {
                        id = context.GetNumber(values[1]);
                        resolveAs = first == 1 ? ItemLogNameKind : ItemPluralKind;
                    }
                    else
                    {
                        resolveAs = ItemLogNameKind;
                    }
                    break;
                case 0x36:
                    resolveAs = KeyItemKind;
                    break;
                case 0x37:
                    resolveAs = ZoneKind;
                    break;
            }
            // A kind that is not a printable character (0x17, 0x84...) shows as hex, never as a control character.
            return context.ResolveName(resolveAs, id) ?? (kind is >= 0x20 and < 0x7F ? $"<{(char)kind}{id}>" : $"<{kind:X2}:{id}>");
        }

        /// <summary>
        /// The article an 01 01 01 tag stands for: "a" or "an" by the first letter of the item tag that follows it ("You
        /// dig up 01 01 01 &lt;0x24 item&gt;" = "You dig up a chunk of copper ore"), the rule retail's shop lines follow
        /// (<see cref="Ui.StockUiShop.DescribeCount"/>). "a" when no item follows or its name is not known.
        /// </summary>
        private static string Article(IReadOnlyList<EventMessageSegment> segments, int index, IEventMessageContext context)
        {
            for (int s = index + 1; s < segments.Count; s++)
            {
                var next = segments[s];
                if (next.Kind == EventMessageSegmentKind.LineBreak) break;
                if (next.Kind != EventMessageSegmentKind.Name) continue;
                if (next.Code is < 0x23 or > 0x2A) break;
                string name = FormatName(next, context);
                bool vowel = name.Length > 0 && "aeiouAEIOU".IndexOf(name[0]) >= 0;
                return vowel ? "an" : "a";
            }
            return "a";
        }
    }

    /// <summary>A context with fixed numbers and no names, for messages that carry everything they show.</summary>
    public sealed class SimpleMessageContext : IEventMessageContext
    {
        private readonly IReadOnlyList<int> _numbers;
        private readonly Func<int, string?>? _partyMemberName;
        private readonly Func<uint, string?>? _entityNameById;

        public SimpleMessageContext(IReadOnlyList<int>? numbers = null, string playerName = "", string npcName = "",
            Func<byte, int, string?>? resolveName = null, Func<int, string?>? partyMemberName = null,
            Func<uint, string?>? entityNameById = null, TimeZoneInfo? timeZone = null)
        {
            TimeZone = timeZone ?? TimeZoneInfo.Local;
            _numbers = numbers ?? Array.Empty<int>();
            PlayerName = playerName;
            NpcName = npcName;
            ResolveNameFunc = resolveName;
            _partyMemberName = partyMemberName;
            _entityNameById = entityNameById;
        }

        public Func<byte, int, string?>? ResolveNameFunc { get; }
        public TimeZoneInfo TimeZone { get; }
        public string PlayerName { get; }
        public string NpcName { get; }
        public int GetNumber(int index) => index >= 0 && index < _numbers.Count ? _numbers[index] : 0;
        public string? GetEntityName(int index) => _partyMemberName?.Invoke(index);
        public string? GetEntityNameById(uint serverId) => _entityNameById?.Invoke(serverId);
        public string? ResolveName(byte kind, int id) => ResolveNameFunc?.Invoke(kind, id);
    }
}
