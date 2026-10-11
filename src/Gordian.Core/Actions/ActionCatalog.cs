// src/Gordian.Core/Actions/ActionCatalog.cs
using System;
using System.Collections.Generic;
using System.Text;
using Gordian.Core.Resources.Tables;

namespace Gordian.Core.Actions
{
    /// <summary>
    /// The client's target bits on a spell (<see cref="SpellRecord.TargetFlags"/>) or command
    /// (<see cref="CommandRecord.TargetFlags"/>): who the target cursor may land on. Read from the retail table
    /// (2026-10-11): Cure 0x3F (anyone, enemies included for undead), Curaga 0x05 (yourself and your party), Raise 0x1D
    /// (yourself, party, alliance, NPCs), Fire / Provoke / Steal / every weapon skill 0x20 (enemies), Escape and every
    /// Trust 0x01 (yourself). Windower's resources name the same bits. The meaning of 0x40 and up is not known.
    /// <b>Differs from LandSandBoat:</b> its <c>TARGETTYPE</c> numbers the kinds differently (enemy 0x04, player 0x10).
    /// </summary>
    [Flags]
    public enum ActionTargetFlags : ushort
    {
        None = 0,
        Self = 0x01,
        Player = 0x02,
        Party = 0x04,
        Alliance = 0x08,
        Npc = 0x10,
        Enemy = 0x20,
    }

    /// <summary>Maps target bits between the item DAT's layout and the spell / command tables' (<see cref="ActionTargetFlags"/>).</summary>
    public static class ItemTargetFlags
    {
        /// <summary>
        /// An item record's ValidTargets (the item DAT; the layout of LandSandBoat's <c>TARGETTYPE</c>, src/map/entities/battle_entity.h: 0x01 self,
        /// 0x02 party, 0x04 enemy, 0x08 alliance, 0x10 player, 0x40 NPC) as <see cref="ActionTargetFlags"/>; other bits are dropped.
        /// </summary>
        public static ActionTargetFlags ToActionTargets(int validTargets)
        {
            var flags = ActionTargetFlags.None;
            if ((validTargets & 0x01) != 0) flags |= ActionTargetFlags.Self;
            if ((validTargets & 0x02) != 0) flags |= ActionTargetFlags.Party;
            if ((validTargets & 0x04) != 0) flags |= ActionTargetFlags.Enemy;
            if ((validTargets & 0x08) != 0) flags |= ActionTargetFlags.Alliance;
            if ((validTargets & 0x10) != 0) flags |= ActionTargetFlags.Player;
            if ((validTargets & 0x40) != 0) flags |= ActionTargetFlags.Npc;
            return flags;
        }
    }

    /// <summary>What a typed or menu action is: a spell (<c>/ma</c>), a job ability (<c>/ja</c>) or a weapon skill (<c>/ws</c>).</summary>
    public enum ActionKind
    {
        Spell,
        JobAbility,
        WeaponSkill,
        /// <summary>An item used on a target (C2S 0x037), through the sub-target cursor.</summary>
        Item,
    }

    /// <summary>
    /// The client's spells, job abilities and weapon skills by id and by name: the records of
    /// <see cref="SpellCommandTables"/> (file id 81, ROM/118/114) and their names from the d_msg tables ROM/181/73
    /// (spells, row = spell id) and ROM/181/72 (commands, row = command id: weapon skills at their id, job abilities at
    /// 512 + id), as xi-tools documents them (https://github.com/vekien/xi-tools, <c>docs/dat_index.md</c>). Names are
    /// matched as retail's <c>/ma</c>, <c>/ja</c> and <c>/ws</c> take them: case-insensitive, extra spaces ignored.
    /// </summary>
    public sealed class ActionCatalog
    {
        private readonly Func<int, string?> _spellName;
        private readonly Func<int, string?> _commandName;
        private readonly object _sync = new();
        private Dictionary<string, int>? _spellsByName, _abilitiesByName, _weaponSkillsByName;

        public ActionCatalog(SpellCommandTables tables, Func<int, string?> spellName, Func<int, string?> commandName)
        {
            Tables = tables ?? throw new ArgumentNullException(nameof(tables));
            _spellName = spellName ?? throw new ArgumentNullException(nameof(spellName));
            _commandName = commandName ?? throw new ArgumentNullException(nameof(commandName));
        }

        /// <summary>Reads the tables and names from the game's resources; null when the table is missing.</summary>
        public static ActionCatalog? Load(Resources.ResourceManager resources)
        {
            ArgumentNullException.ThrowIfNull(resources);
            var bytes = resources.LoadDatBytesByFileId(SpellCommandTables.FileId);
            var tables = bytes != null ? SpellCommandTables.Parse(bytes) : null;
            if (tables == null) return null;
            return new ActionCatalog(tables,
                id => resources.TryGetString(Resources.Models.DMsgCategory.Spells, id, out var name) ? name : null,
                id => resources.TryGetString(Resources.Models.DMsgCategory.Abilities, id, out var name) ? name : null);
        }

        /// <summary>
        /// The session-independent catalog the app provides once the game's resources are known (built on first use);
        /// null in tests and without an install, when only numeric ids are understood.
        /// </summary>
        public static Func<ActionCatalog?>? Provider { get; set; }

        public SpellCommandTables Tables { get; }

        /// <summary>
        /// Command ids from here up are mounts, not job abilities: retail's <c>/ja</c> resolves only ids below 0x700
        /// (xi-tools, <c>docs/dats/ROM_118_114.md</c>).
        /// </summary>
        public const int JobAbilityLimit = 0x700;

        public SpellRecord? GetSpell(int id) => Tables.GetSpell(id);

        /// <summary>A job ability's record (command 512 + <paramref name="abilityId"/>).</summary>
        public CommandRecord? GetJobAbility(int abilityId) => abilityId >= 0 ? Tables.GetCommand(CommandRecord.JobAbilityBase + abilityId) : null;

        /// <summary>A weapon skill's record (command <paramref name="weaponSkillId"/>), when it is one.</summary>
        public CommandRecord? GetWeaponSkill(int weaponSkillId) =>
            weaponSkillId is >= 0 and < CommandRecord.JobAbilityBase && Tables.GetCommand(weaponSkillId) is { Type: CommandRecord.WeaponSkillType } ws ? ws : null;

        public string? SpellName(int id) => Clean(_spellName(id));

        public string? JobAbilityName(int abilityId) => Clean(_commandName(CommandRecord.JobAbilityBase + abilityId));

        public string? WeaponSkillName(int weaponSkillId) => Clean(_commandName(weaponSkillId));

        /// <summary>The name of an action of a kind by its id, or null.</summary>
        public string? Name(ActionKind kind, int id) => kind switch
        {
            ActionKind.Spell => SpellName(id),
            ActionKind.JobAbility => JobAbilityName(id),
            _ => WeaponSkillName(id),
        };

        /// <summary>Finds a spell by name (case-insensitive).</summary>
        public bool TryFindSpell(string name, out SpellRecord spell)
        {
            spell = null!;
            EnsureIndexes();
            if (!_spellsByName!.TryGetValue(Normalize(name), out int id) || Tables.GetSpell(id) is not { } found) return false;
            spell = found;
            return true;
        }

        /// <summary>Finds a job ability by name; <paramref name="abilityId"/> is the id C2S 0x01A and S2C 0x0AC use (command id - 512).</summary>
        public bool TryFindJobAbility(string name, out ushort abilityId, out CommandRecord record)
        {
            abilityId = 0;
            record = null!;
            EnsureIndexes();
            if (!_abilitiesByName!.TryGetValue(Normalize(name), out int command) || Tables.GetCommand(command) is not { } found) return false;
            abilityId = (ushort)(command - CommandRecord.JobAbilityBase);
            record = found;
            return true;
        }

        /// <summary>Finds a weapon skill by name.</summary>
        public bool TryFindWeaponSkill(string name, out ushort weaponSkillId, out CommandRecord record)
        {
            weaponSkillId = 0;
            record = null!;
            EnsureIndexes();
            if (!_weaponSkillsByName!.TryGetValue(Normalize(name), out int command) || Tables.GetCommand(command) is not { } found) return false;
            weaponSkillId = (ushort)command;
            record = found;
            return true;
        }

        /// <summary>Finds an action of a kind by name, or by its number ("/ma 1" stays numeric); the id, or null.</summary>
        public int? Find(ActionKind kind, string name)
        {
            switch (kind)
            {
                case ActionKind.Spell:
                    return TryFindSpell(name, out var spell) ? spell.Id : null;
                case ActionKind.JobAbility:
                    return TryFindJobAbility(name, out ushort ability, out _) ? ability : null;
                default:
                    return TryFindWeaponSkill(name, out ushort ws, out _) ? ws : null;
            }
        }

        /// <summary>A name as the lookups compare it: trimmed, inner runs of spaces collapsed, quotes dropped.</summary>
        public static string Normalize(string name)
        {
            var text = new StringBuilder(name.Length);
            bool space = false;
            foreach (char c in name.Trim().Trim('"'))
            {
                if (char.IsWhiteSpace(c))
                {
                    space = true;
                    continue;
                }
                if (space && text.Length > 0) text.Append(' ');
                space = false;
                text.Append(c);
            }
            return text.ToString();
        }

        private static string? Clean(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            name = name.Trim();
            // Unused rows are named "." in retail's tables (xi-tools, docs/menu/records.md).
            return name == "." ? null : name;
        }

        private void EnsureIndexes()
        {
            if (_spellsByName != null) return;
            lock (_sync)
            {
                if (_spellsByName != null) return;
                var spells = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int id = 0; id < Tables.Spells.Count; id++)
                {
                    if (Tables.Spells[id] == null || SpellName(id) is not { } name) continue;
                    spells.TryAdd(Normalize(name), id); // the first record of a name wins
                }

                var abilities = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var weaponSkills = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int id = 0; id < Tables.Commands.Count; id++)
                {
                    var command = Tables.Commands[id];
                    if (command == null || Clean(_commandName(id)) is not { } name) continue;
                    if (id < CommandRecord.JobAbilityBase)
                    {
                        if (command.Type == CommandRecord.WeaponSkillType) weaponSkills.TryAdd(Normalize(name), id);
                    }
                    else if (id < JobAbilityLimit && command.Type is not (CommandRecord.WeaponSkillType or CommandRecord.TraitType))
                    {
                        abilities.TryAdd(Normalize(name), id);
                    }
                }
                _abilitiesByName = abilities;
                _weaponSkillsByName = weaponSkills;
                _spellsByName = spells;
            }
        }
    }
}
