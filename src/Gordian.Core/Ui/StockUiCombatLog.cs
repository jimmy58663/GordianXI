// src/Gordian.Core/Ui/StockUiCombatLog.cs
using System.Collections.Generic;
using Gordian.Core.Network.Packets;

namespace Gordian.Core.Ui
{
    /// <summary>What a combat line reports, as the Font Colors and Log pages group battle messages.</summary>
    public enum CombatLogEffect : byte
    {
        /// <summary>Anything else ("Standard battle messages").</summary>
        Standard,
        Recover,
        Lose,
        Beneficial,
        Detrimental,
        Resist,
        Evade,
        Casting,
        CallForHelp,
        /// <summary>Skill-ups (the Basic system messages row: its sample is a skill-up).</summary>
        SkillUp,
        /// <summary>Experience, limit points and levels: white in retail's log (capture 2026-10-04).</summary>
        Experience,
    }

    /// <summary>
    /// Sorts combat lines into the Font Colors rows (For Self / For Others / System) and the Log page's types: the
    /// effect from the battle message id (and an action's resolution), "self" when the line's subject (the target, or
    /// the actor hit by a reaction) is your character.
    /// <para>
    /// The message ids are the 0x028 / 0x029 battle message numbers (the client's message table, as LandSandBoat sends
    /// them; texts as <see cref="CombatLogFormatter"/> prints them). The grouping is GordianXI's reading of the
    /// message texts, since the retail table that maps messages to colours is not decoded; ids not listed are
    /// standard battle messages. Provisional until compared with a retail log in the Font Colors defaults (#53).
    /// </para>
    /// </summary>
    public static class StockUiCombatLog
    {
        private static readonly Dictionary<ushort, CombatLogEffect> Effects = Build();

        private static Dictionary<ushort, CombatLogEffect> Build()
        {
            var d = new Dictionary<ushort, CombatLogEffect>();
            void Add(CombatLogEffect effect, params ushort[] ids)
            {
                foreach (ushort id in ids) d[id] = effect;
            }

            // "recovers N HP / MP" (spells, abilities, items, drains on the drainer are not split out).
            Add(CombatLogEffect.Recover, 7, 24, 25, 102, 103, 224, 263, 276, 318, 367);
            // "takes / hits for N points of damage", drains on the drained, spikes and counters.
            Add(CombatLogEffect.Lose, 1, 2, 33, 44, 67, 110, 157, 161, 162, 163, 185, 187, 227, 229, 252, 264, 317, 352, 353, 576, 577);
            // "gains the effect of" / "receives the effect of".
            Add(CombatLogEffect.Beneficial, 186, 194, 230, 266, 319);
            Add(CombatLogEffect.Detrimental, 236, 237, 242, 270, 277, 278, 279);
            // "resists the spell", "no effect", "fails to take effect".
            Add(CombatLogEffect.Resist, 75, 85, 114, 189, 284, 655, 656);
            // misses, dodges, parries, evades, shadows.
            Add(CombatLogEffect.Evade, 14, 15, 30, 31, 32, 70, 158, 188, 282, 324, 354);
            Add(CombatLogEffect.Casting, 3, 327);
            Add(CombatLogEffect.CallForHelp, 19);
            Add(CombatLogEffect.SkillUp, 38, 53);
            Add(CombatLogEffect.Experience, 8, 9, 11, 50, 253, 371, 372);
            return d;
        }

        /// <summary>The effect a battle message reports.</summary>
        public static CombatLogEffect EffectOf(ushort messageId) =>
            Effects.TryGetValue(messageId, out var effect) ? effect : CombatLogEffect.Standard;

        /// <summary>The effect an action line reports: a miss, parry or a starting cast whatever its message, else by message id.</summary>
        public static CombatLogEffect EffectOf(in CombatLogLine line)
        {
            if (line.Part == CombatLogLinePart.Primary)
            {
                if (line.Resolution is ActionResolution.Miss or ActionResolution.Parry) return CombatLogEffect.Evade;
                if (line.Category == ActionCategory.MagicStart) return CombatLogEffect.Casting;
            }
            if (line.Part != CombatLogLinePart.Primary) return CombatLogEffect.Lose;
            var effect = EffectOf(line.MessageId);
            if (effect == CombatLogEffect.Standard && line.Category is ActionCategory.BasicAttack or ActionCategory.RangedFinish) return CombatLogEffect.Lose;
            return effect;
        }

        /// <summary>The Font Colors row for an effect on yourself or on someone else; null for experience lines (white).</summary>
        public static StockUiFontColorId? FontColorOf(CombatLogEffect effect, bool self) => effect switch
        {
            CombatLogEffect.Recover => self ? StockUiFontColorId.SelfRecover : StockUiFontColorId.OthersRecover,
            CombatLogEffect.Lose => self ? StockUiFontColorId.SelfDamage : StockUiFontColorId.OthersDamage,
            CombatLogEffect.Beneficial => self ? StockUiFontColorId.SelfBeneficial : StockUiFontColorId.OthersBeneficial,
            CombatLogEffect.Detrimental => self ? StockUiFontColorId.SelfDetrimental : StockUiFontColorId.OthersDetrimental,
            CombatLogEffect.Resist => self ? StockUiFontColorId.SelfNoEffect : StockUiFontColorId.OthersNoEffect,
            CombatLogEffect.Evade => self ? StockUiFontColorId.SelfMiss : StockUiFontColorId.OthersMiss,
            CombatLogEffect.CallForHelp => StockUiFontColorId.CallForHelp,
            CombatLogEffect.SkillUp => StockUiFontColorId.BasicSystem,
            CombatLogEffect.Experience => null,
            // Casting starts and every other battle message: the Standard battle messages row (its sample is a cast).
            _ => StockUiFontColorId.StandardBattle,
        };

        /// <summary>The Log page type for an effect on yourself or on someone else.</summary>
        public static ChatLogType TypeOf(CombatLogEffect effect, bool self) => effect switch
        {
            CombatLogEffect.Recover => self ? ChatLogType.SelfRecover : ChatLogType.OthersRecover,
            CombatLogEffect.Lose => self ? ChatLogType.SelfLose : ChatLogType.OthersLose,
            CombatLogEffect.Beneficial => self ? ChatLogType.SelfBeneficial : ChatLogType.OthersBeneficial,
            CombatLogEffect.Detrimental => self ? ChatLogType.SelfDetrimental : ChatLogType.OthersDetrimental,
            CombatLogEffect.Resist => self ? ChatLogType.SelfResist : ChatLogType.OthersResist,
            CombatLogEffect.Evade => self ? ChatLogType.SelfEvade : ChatLogType.OthersEvade,
            CombatLogEffect.CallForHelp => ChatLogType.CallsForHelp,
            CombatLogEffect.SkillUp or CombatLogEffect.Experience => ChatLogType.BasicSystem,
            _ => ChatLogType.StandardBattle,
        };

        /// <summary>A combat log line for a formatted action result.</summary>
        public static ChatLogLine LineFor(in CombatLogLine line, uint localPlayerId, System.DateTime timestamp)
        {
            var effect = EffectOf(line);
            bool self = localPlayerId != 0 && line.TargetId == localPlayerId;
            return new ChatLogLine(ChatLogChannel.Combat, line.Text, timestamp, FontColorOf(effect, self), TypeOf(effect, self));
        }

        /// <summary>A combat log line for a battle message (0x029): its subject is the message's target.</summary>
        public static ChatLogLine LineFor(string text, ushort messageId, uint targetId, uint localPlayerId, System.DateTime timestamp)
        {
            var effect = EffectOf(messageId);
            bool self = localPlayerId != 0 && targetId == localPlayerId;
            return new ChatLogLine(ChatLogChannel.Combat, text, timestamp, FontColorOf(effect, self), TypeOf(effect, self));
        }
    }
}
