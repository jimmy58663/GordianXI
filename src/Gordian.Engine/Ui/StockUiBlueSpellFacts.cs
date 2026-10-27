// src/Gordian.Engine/Ui/StockUiBlueSpellFacts.cs
// Generated from LandSandBoat (https://github.com/LandSandBoat/server) data: sql/blue_spell_list.sql (trait category and
// weight), sql/blue_traits.sql (trait names), sql/blue_spell_mods.sql (stat bonuses) and the header comments of
// scripts/actions/spells/blue/*.lua (monster type, spell type). Facts only; regenerate rather than hand-edit.
using System.Collections.Generic;

namespace Gordian.Engine.Ui
{
    /// <summary>
    /// What the Set Spells details window (image-20 of the 2026-10-24 round 1 notes) and the "Refine by Type" filter show of a
    /// blue spell beyond the client's spell table: its monster family (1-18 in <c>bluesibo</c>'s button order, 0 unknown), its
    /// type ("Physical" / "Magical"), its stat bonus ("MND+2"), the job trait it counts toward and its trait points ("Clear
    /// Mind (4)": LandSandBoat's trait weight times four, the 8 / 16 / 24 of help 637). None of this is in the client's
    /// <c>mgc_</c> records (checked 2026-10-25); it is LandSandBoat's (see the file header), which leaves 33 spells without a
    /// family: those show no family and only the unfiltered list carries them.
    /// </summary>
    public readonly record struct StockUiBlueSpellFact(byte Family, string? Type, string? Bonus, string? Trait, int TraitPoints);

    public static class StockUiBlueSpellFacts
    {
        /// <summary>The families in <c>bluesibo</c>'s button order (button n = family n).</summary>
        public static readonly IReadOnlyList<string> Families = new[]
        {
            "Aquans", "Amorphs", "Arcana", "Undead", "Vermin", "Demons", "Dragons", "Birds", "Beasts", "Plantoids", "Lizards",
            "Luminians", "Luminions", "Beastmen", "Empty", "Vorageans", "Archaics", "Elementals",
        };

        /// <summary>The facts of a blue spell by id, or null when LandSandBoat lists none.</summary>
        public static StockUiBlueSpellFact? Of(int spellId) => Facts.TryGetValue(spellId, out var fact) ? fact : null;

        /// <summary>A family's name (1-18), or null.</summary>
        public static string? FamilyName(int family) => family >= 1 && family <= Families.Count ? Families[family - 1] : null;

        private static readonly Dictionary<int, StockUiBlueSpellFact> Facts = new()
        {
            [513] = new(1, "Magical", "MND+2", "Clear Mind", 4), // Venom Shell
            [515] = new(1, "Magical", "MND+2", "Clear Mind", 4), // Maelstrom
            [517] = new(1, "Magical", null, "Max MP Boost", 4), // Metallic Body
            [519] = new(1, "Physical", "VIT+1 CHR+1 HP+10", "Evasion Bonus", 4), // Screwdriver
            [521] = new(2, "Magical", "MP+5", null, 0), // MP Drainkiss
            [522] = new(2, "Magical", "HP-5 MP+5", null, 0), // Death Ray
            [524] = new(2, "Magical", "VIT+1", null, 0), // Sandspin
            [527] = new(3, "Physical", "AGI+3", "Undead Killer", 4), // Smite of Rage
            [529] = new(3, "Physical", "STR+1", "Undead Killer", 4), // Bludgeon
            [530] = new(3, "Magical", "AGI+2", null, 0), // Refueling
            [531] = new(3, "Magical", "INT+1", "Magic Defense Bonus", 4), // Ice Break
            [532] = new(3, "Magical", "DEX+3", null, 0), // Blitzstrahl
            [533] = new(3, "Magical", "STR+2", "Auto Refresh", 8), // Self-Destruct
            [534] = new(3, "Magical", "AGI+3", "Max MP Boost", 4), // Mysterious Light
            [535] = new(3, "Magical", "INT-1", "Auto Refresh", 4), // Cold Wave
            [536] = new(4, "Magical", "MND+1", "Clear Mind", 4), // Poison Breath
            [537] = new(4, "Magical", "AGI+1", "Auto Refresh", 4), // Stinking Gas
            [538] = new(4, "Magical", "INT+1", "Magic Attack Bonus", 4), // Memento Mori
            [539] = new(4, "Physical", "HP-5 MP+10", "Defense Bonus", 4), // Terror Touch
            [540] = new(4, "Physical", "VIT+2 INT+1 MND+1", "Attack Bonus", 4), // Spinal Cleave
            [541] = new(4, "Magical", "HP-5 MP+5", null, 0), // Blood Saber
            [542] = new(2, "Magical", "HP-5 MP+5", null, 0), // Digest
            [543] = new(5, "Physical", "INT+1", "Plantoid Killer", 4), // Mandibular Bite
            [544] = new(5, "Magical", "MND+1", "Magic Attack Bonus", 4), // Cursed Sphere
            [545] = new(5, "Physical", "HP-5 MP+15", "Store TP", 4), // Sickle Slash
            [547] = new(5, "Magical", "VIT+3", null, 0), // Cocoon
            [548] = new(5, "Magical", "VIT+1", "Clear Mind", 4), // Filamented Hold
            [549] = new(5, "Magical", "HP+5 CHR+1", "Resist Sleep", 4), // Pollen
            [551] = new(5, "Physical", "MND+1", "Plantoid Killer", 4), // Power Attack
            [554] = new(5, "Physical", "MND+2 CHR+2 HP+5", "Attack Bonus", 4), // Death Scissors
            [555] = new(14, "Magical", "VIT+2", "Magic Defense Bonus", 4), // Magnetite Cloud
            [557] = new(6, "Magical", "HP-5 MP+15", "Magic Attack Bonus", 4), // Eyes On Me
            [560] = new(6, "Physical", "INT+1", "Accuracy Bonus", 4), // Frenetic Rip
            [561] = new(6, "Magical", "AGI+2", "Auto Refresh", 8), // Frightful Roar
            [563] = new(6, "Magical", "AGI+1", "Max MP Boost", 4), // Hecatomb Wave
            [564] = new(7, "Physical", "VIT+1 MP+5", "Max HP Boost", 4), // Body Slam
            [565] = new(7, "Magical", "HP+5 CHR+1", null, 0), // Radiant Breath
            [567] = new(8, "Physical", "CHR+1 HP+5", null, 0), // Helldive
            [569] = new(8, "Physical", "DEX+2", "Rapid Shot", 4), // Jet Stream
            [570] = new(8, "Magical", "HP-5 MP+5", null, 0), // Blood Drain
            [572] = new(8, "Magical", null, "Magic Attack Bonus", 4), // Sound Blast
            [573] = new(8, "Magical", "AGI+1", "Clear Mind", 4), // Feather Tickle
            [574] = new(8, "Magical", null, "Resist Gravity", 4), // Feather Barrier
            [575] = new(8, "Magical", "MP+15", null, 0), // Jettatura
            [576] = new(8, "Magical", "HP+5 CHR+1", "Resist Sleep", 4), // Yawn
            [577] = new(9, "Physical", "AGI+1", "Lizard Killer", 4), // Foot Kick
            [578] = new(9, "Magical", "HP+5", "Resist Sleep", 4), // Wild Carrot
            [579] = new(9, "Magical", "AGI+2", "Auto Refresh", 12), // Voracious Trunk
            [581] = new(9, "Magical", "HP+10 CHR+2", "Auto Regen", 4), // Healing Breeze
            [582] = new(9, "Magical", "AGI+1", "Conserve MP", 4), // Chaotic Eye
            [584] = new(9, "Magical", "HP+5 CHR+1", "Auto Regen", 4), // Sheep Song
            [585] = new(9, "Physical", "HP+5", "Lizard Killer", 4), // Ram Charge
            [587] = new(9, "Physical", "VIT+1", "Lizard Killer", 4), // Claw Cyclone
            [588] = new(9, "Magical", "HP-5", "Clear Mind", 4), // Lowing
            [589] = new(4, "Physical", "DEX+1 CHR+1 HP+5", "Accuracy Bonus", 4), // Dimensional Death
            [591] = new(9, "Magical", "STR+3", "Magic Attack Bonus", 4), // Heat Breath
            [592] = new(9, "Magical", null, null, 0), // Blank Gaze
            [593] = new(9, "Magical", "CHR+1 HP+5", "Resist Sleep", 4), // Magic Fruit
            [594] = new(10, "Physical", "STR+2 DEX+1", "Attack Bonus", 4), // Uppercut
            [595] = new(10, "Magical", "VIT+3 AGI+3", "Beast Killer", 4), // 1000 Needles
            [596] = new(10, "Physical", "STR+1", null, 0), // Pinecone Bomb
            [597] = new(10, "Physical", "MND+1", "Beast Killer", 4), // Sprout Smack
            [598] = new(10, "Magical", "HP-5 MP+5", "Clear Mind", 4), // Soporific
            [599] = new(10, "Physical", "HP-5 MP+5", null, 0), // Queasyshroom
            [603] = new(10, "Physical", "CHR+1 HP+10", "Beast Killer", 4), // Wild Oats
            [604] = new(10, "Magical", "INT+2 MND+2", "Fast Cast", 4), // Bad Breath
            [605] = new(11, "Magical", "HP-5 MP+10", null, 0), // Geist Wall
            [606] = new(11, "Magical", "MND+1", "Clear Mind", 4), // Awful Eye
            [608] = new(11, "Magical", "INT-2", "Conserve MP", 4), // Frost Breath
            [610] = new(11, "Magical", "INT+1", null, 0), // Infrasonics
            [611] = new(12, "Physical", "INT+1 MND-1", "Accuracy Bonus", 4), // Disseverment
            [612] = new(13, "Magical", "HP+20 CHR+2", "Auto Refresh", 16), // Actinic Burst
            [613] = new(13, "Magical", "INT+3 MND+3", "Magic Attack Bonus", 4), // Reactor Cool
            [614] = new(12, "Magical", "MP+10 VIT+2 AGI+2", "Defense Bonus", 4), // Saline Coat
            [615] = new(12, "Magical", "STR+3 DEX+3", "Auto Refresh", 16), // Plasma Charge
            [616] = new(12, "Magical", "HP+20 MP+15", "Attack Bonus", 4), // Temporal Shift
            [617] = new(12, "Physical", "VIT+1 HP-5 MP+5", "Defense Bonus", 4), // Vertical Cleave
            [618] = new(14, "Magical", "STR+1", null, 0), // Blastbomb
            [620] = new(14, "Physical", "DEX+2", "Attack Bonus", 4), // Battle Dance
            [621] = new(14, "Magical", "VIT+1", "Clear Mind", 4), // Sandspray
            [622] = new(14, "Physical", "INT+1", "Defense Bonus", 4), // Grand Slam
            [623] = new(14, "Physical", "DEX+2", null, 0), // Head Butt
            [626] = new(14, "Magical", "STR+2", null, 0), // Bomb Toss
            [628] = new(14, "Physical", "DEX+2", "Max HP Boost", 4), // Frypan
            [629] = new(14, "Magical", "AGI+1", "Max HP Boost", 4), // Flying Hip Press
            [631] = new(14, "Physical", "MND+2", "Rapid Shot", 4), // Hydro Shot
            [632] = new(14, "Magical", "VIT+1", null, 0), // Diamondhide
            [633] = new(14, "Magical", "HP-5 MP+5", "Counter", 4), // Enervation
            [634] = new(14, "Magical", "HP+15 CHR+1", "Auto Refresh", 8), // Light of Penance
            [636] = new(14, "Magical", "VIT+1", "Clear Mind", 4), // Warm-Up
            [637] = new(14, "Magical", "STR+3", "Conserve MP", 4), // Firespit
            [638] = new(14, "Physical", "CHR+2 HP+5", "Rapid Shot", 4), // Feather Storm
            [640] = new(14, "Physical", "MND+2", "Store TP", 4), // Tail Slap
            [641] = new(14, "Physical", "DEX+2 AGI+1", "Evasion Bonus", 4), // Hysteric Barrage
            [642] = new(2, "Magical", "HP-5 MP+5", null, 0), // Amplification
            [643] = new(5, "Physical", "STR+1 DEX+1", null, 0), // Cannonball
            [644] = new(6, "Magical", "MP+5 MND+1", "Clear Mind", 4), // Mind Blast
            [645] = new(5, "Magical", "HP+5 MP+5 CHR+1", "Resist Sleep", 4), // Exuviation
            [646] = new(14, "Magical", "MP-5 MND+2", "Magic Attack Bonus", 4), // Magic Hammer
            [647] = new(7, "Magical", "AGI+2", "Conserve MP", 4), // Zephyr Mantle
            [648] = new(11, "Magical", "MP+3 INT+1 MND+1", "Resist Gravity", 4), // Regurgitation
            [650] = new(10, "Physical", "VIT+1", "Beast Killer", 4), // Seedspray
            [651] = new(2, "Magical", "HP-10 MP+10", "Clear Mind", 4), // Corrosive Ooze
            [652] = new(5, "Physical", "HP+5 STR+1", "Plantoid Killer", 4), // Spiral Spin
            [653] = new(9, "Physical", "AGI+3", "Counter", 4), // Asuran Claws
            [654] = new(1, "Physical", "HP+10 VIT+3", "Fast Cast", 4), // Sub-Zero Smash
            [655] = new(6, "Magical", "STR+3", null, 0), // Triumphant Roar
            [656] = new(0, null, "DEX+2 MND+2", "Tier Zero because this is weaker than WAR double attack", 4), // Acrid Stream
            [657] = new(0, null, "AGI+1 VIT+2", "Dual Wield", 4), // Blazing Bound
            [658] = new(0, null, "MP+5 STR+2", null, 0), // Plenilune Embrace
            [659] = new(0, null, "STR-2 VIT+3", "Tier Zero because this is weaker than WAR double attack", 4), // Demoralizing Roar
            [660] = new(5, "Magical", "DEX+1 AGI+2", "Magic Burst Bonus", 4), // Cimicine Discharge
            [661] = new(4, "Magical", "HP+20", "Dual Wield", 4), // Animating Wail
            [662] = new(3, "Magical", "MP+10 MND+1", null, 0), // Battery Charge
            [663] = new(0, null, "MP+3 MND+1 CHR+1", "Magic Burst Bonus", 4), // Leafstorm
            [664] = new(1, "Magical", "MND+2", null, 0), // Regeneration
            [665] = new(0, null, "HP-20 AGI+5", "Zanshin", 4), // Final Sting
            [666] = new(14, "Physical", "HP+10 DEX+2 MND-3", "Skillchain Bonus", 4), // Goblin Rush
            [667] = new(0, null, "AGI+3 CHR-2", "Accuracy Bonus", 4), // Vanity Dive
            [668] = new(0, null, "MP+7 INT+2", "Max MP Boost", 4), // Magic Barrier
            [669] = new(3, "Physical", "STR+2 DEX+2", "Zanshin", 4), // Whirl of Rage
            [670] = new(0, null, "STR+2 VIT+2 DEX-1 AGI-1", "Skillchain Bonus", 4), // Benthic Typhoon
            [671] = new(15, "Magical", "INT+3 CHR-2", "Fast Cast", 4), // Auroral Drape
            [672] = new(0, null, "MND+3 CHR-2", "Magic Defense Bonus", 4), // Osmosis
            [673] = new(15, "Physical", "DEX+3 CHR-2", "Dual Wield", 4), // Quadratic Continuum
            [674] = new(0, null, "HP-10 DEX+2 AGI+2", "Store TP", 4), // Fantod
            [675] = new(5, "Magical", "VIT+2", "Attack Bonus", 4), // Thermal Pulse
            [677] = new(15, "Physical", "STR+3 CHR-2", "Tier Zero because this is weaker than WAR double attack", 4), // Empty Thrash
            [678] = new(10, "Magical", "HP+5 MP+5 CHR+2", "Magic Attack Bonus", 4), // Dream Flower
            [679] = new(15, "Magical", "VIT+3 CHR-2", "Evasion Bonus", 4), // Occultation
            [680] = new(9, "Magical", "HP-10 DEX+2 INT+2", "Gilfinder", 4), // Charged Whisker
            [681] = new(0, null, "MND+3 CHR-2", "Auto Refresh", 16), // Winds of Promyvion
            [682] = new(11, "Physical", "HP+15 MP-5 INT-1", "Dual Wield", 4), // Delta Thrust
            [683] = new(0, null, "INT+2 MND+2 STR-1 VIT-1", "Gilfinder", 4), // Everyones Grudge
            [684] = new(0, null, "STR+2 AGI+2", "Magic Burst Bonus", 4), // Reaving Wind
            [685] = new(0, null, "HP+15 MP-15 VIT+3", "Max HP Boost", 4), // Barrier Tusk
            [686] = new(0, null, "STR+2 MND+2", "Dual Wield", 4), // Mortal Ray
            [687] = new(0, null, "MP+20 INT+4 MND+2 STR-3 VIT-3", "Conserve MP", 4), // Water Bomb
            [688] = new(3, "Physical", "STR+2", "Tier Zero because this is weaker than WAR double attack", 4), // Heavy Strike
            [689] = new(0, null, "AGI+2 MND+2", "Counter", 4), // Dark Orb
            [690] = new(7, "Magical", "HP+5 AGI+1", "Auto Regen", 4), // White Wind
            [692] = new(5, "Physical", "HP-5 MP-5 AGI+1 DEX+1", "Store TP", 4), // Sudden Lunge
            [693] = new(6, "Physical", "STR+3 CHR+3", "Skillchain Bonus", 4), // Quadrastrike
            [694] = new(0, null, "HP+15 VIT+4", "Max MP Boost", 4), // Vapor Spray
            [695] = new(0, null, "STR+2 DEX+2", "Max HP Boost", 4), // Thunder Breath
            [696] = new(0, null, "HP+10 STR+3 VIT+3 DEX-2 AGI-2", "Counter", 4), // Orcish Counterstance
            [697] = new(2, "Physical", "INT+5 MND+2", "Gilfinder", 4), // Amorphic Spikes
            [698] = new(7, "Magical", "STR+2 AGI+2", "Fast Cast", 4), // Wind breath
            [699] = new(0, null, "STR-3 DEX+4", "Dual Wield", 4), // Barbed Crescent
            [736] = new(0, null, null, null, 0), // Thunderbolt
            [737] = new(0, null, null, null, 0), // Harden Shell
            [738] = new(0, null, null, null, 0), // Absolute Terror
            [739] = new(0, null, null, null, 0), // Gates of Hades
            [740] = new(0, null, null, null, 0), // Tourbillion
            [741] = new(0, null, null, null, 0), // Pyric Bulwark
            [742] = new(0, null, null, null, 0), // Bilgestorm
            [743] = new(0, null, null, null, 0), // Bloodrake
            [744] = new(0, null, null, null, 0), // Droning Whirlwind
            [745] = new(0, null, null, null, 0), // Carcharian Verve
            [746] = new(0, null, null, null, 0), // Blistering Roar
        };
    }
}
