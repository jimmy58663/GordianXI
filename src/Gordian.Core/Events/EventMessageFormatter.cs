// src/Gordian.Core/Events/EventMessageFormatter.cs
using System;
using System.Collections.Generic;
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

        /// <summary>The name of the entity a 0x19 n / 0x7F 0x93 code refers to (party member n, the related entity), or null.</summary>
        string? GetEntityName(int index);

        /// <summary>
        /// The name of the thing a 0x01 block refers to: <paramref name="kind"/> is its type byte ('#' item, '3' key
        /// item, '8' zone) and <paramref name="id"/> the thing's id. Null when unknown.
        /// </summary>
        string? ResolveName(byte kind, int id);

        /// <summary>Whether the player is female (picks 0x7F 0x85 "[his/her]"), or null when unknown.</summary>
        bool? PlayerIsFemale => null;
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
        public const byte KeyItemKind = (byte)'3';
        public const byte ZoneKind = (byte)'8';

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

            void Flush()
            {
                string text = line.ToString();
                line.Clear();
                if (inChoices) choices.Add(text);
                else if (text.Length > 0 || comments.Count > 0) comments.Add(text);
            }

            foreach (var segment in message.Segments)
            {
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
                        line.Append(context.GetNumber(segment.Argument));
                        break;
                    case EventMessageSegmentKind.Name:
                    {
                        int id = segment.Values is { Count: > 0 } values ? context.GetNumber(values[0]) : 0;
                        line.Append(context.ResolveName(segment.Code, id) ?? $"<{(char)segment.Code}{id}>");
                        break;
                    }
                    case EventMessageSegmentKind.Selector:
                    {
                        var alternatives = segment.Alternatives;
                        if (alternatives is { Count: > 0 })
                        {
                            int pick = Math.Clamp(context.GetNumber(segment.Argument), 0, alternatives.Count - 1);
                            line.Append(alternatives[pick]);
                        }
                        break;
                    }
                    case EventMessageSegmentKind.GenderSelector when segment.Alternatives is { Count: >= 2 } words:
                        line.Append(context.PlayerIsFemale is bool female ? words[female ? 1 : 0] : $"[{string.Join('/', words)}]");
                        break;
                    case EventMessageSegmentKind.PlayerName:
                        line.Append(context.PlayerName);
                        break;
                    case EventMessageSegmentKind.NpcName:
                        line.Append(context.NpcName);
                        break;
                    case EventMessageSegmentKind.EntityName:
                        line.Append(context.GetEntityName(segment.Argument) ?? string.Empty);
                        break;
                }
            }
            if (line.Length > 0 || (inChoices && choices.Count == 0)) Flush();
            // Trailing empty lines (a prompt after a line break) are not rows.
            while (comments.Count > 0 && comments[^1].Length == 0) comments.RemoveAt(comments.Count - 1);
            while (choices.Count > 0 && choices[^1].Length == 0) choices.RemoveAt(choices.Count - 1);
            return (comments, choices);
        }
    }

    /// <summary>A context with fixed numbers and no names, for messages that carry everything they show.</summary>
    public sealed class SimpleMessageContext : IEventMessageContext
    {
        private readonly IReadOnlyList<int> _numbers;

        public SimpleMessageContext(IReadOnlyList<int>? numbers = null, string playerName = "", string npcName = "",
            Func<byte, int, string?>? resolveName = null)
        {
            _numbers = numbers ?? Array.Empty<int>();
            PlayerName = playerName;
            NpcName = npcName;
            ResolveNameFunc = resolveName;
        }

        public Func<byte, int, string?>? ResolveNameFunc { get; }
        public string PlayerName { get; }
        public string NpcName { get; }
        public int GetNumber(int index) => index >= 0 && index < _numbers.Count ? _numbers[index] : 0;
        public string? GetEntityName(int index) => null;
        public string? ResolveName(byte kind, int id) => ResolveNameFunc?.Invoke(kind, id);
    }
}
