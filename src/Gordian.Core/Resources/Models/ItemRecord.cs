// src/Gordian.Core/Resources/Models/ItemRecord.cs
using System;
using System.Collections.Generic;

namespace Gordian.Core.Resources.Models
{
    /// <summary>
    /// Strongly-typed FFXI item definition decoded from ROM item record DATs.
    /// Derived from community research in xi-model-viewer (https://github.com/vekien/xi-model-viewer)
    /// and LandSandBoat (https://github.com/LandSandBoat/server).
    /// </summary>
    public sealed class ItemRecord
    {
        #region Common Item Fields
        public uint ItemId { get; set; }
        public uint Flags { get; set; }
        public ushort StackSize { get; set; } = 1;
        public ushort ItemType { get; set; }
        public ushort ResourceId { get; set; }
        public ushort ValidTargets { get; set; }
        #endregion

        #region Equipment Fields
        public ushort Level { get; set; }
        public ushort EquipSlotsMask { get; set; }
        public ushort RacesMask { get; set; }
        public uint JobsMask { get; set; }
        public byte SuperiorLevel { get; set; }
        public byte ItemLevel { get; set; }
        #endregion

        #region Armor & Usable Fields
        public byte ShieldSize { get; set; }
        public byte MaxCharges { get; set; }
        public ushort CastTime { get; set; }
        public ushort UseDelay { get; set; }
        public uint ReuseDelay { get; set; }
        #endregion

        #region Weapon Fields
        public ushort Damage { get; set; }
        public short Delay { get; set; }
        public ushort Dps { get; set; }
        public byte Skill { get; set; }
        public byte JugSize { get; set; }
        #endregion

        #region Strings & Description
        public string Name { get; set; } = string.Empty;
        public string LogName { get; set; } = string.Empty;
        public string LogPlural { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public Dictionary<string, int> ExtractedStats { get; } = new(StringComparer.OrdinalIgnoreCase);
        #endregion

        #region Embedded 32x32 Icon
        /// <summary>
        /// Decoded 32x32 RGBA32 raw pixel data (4096 bytes) if available.
        /// </summary>
        public byte[]? IconRgbaPixels { get; set; }
        #endregion

        public bool CanEquipSlot(int slotIndex) => (EquipSlotsMask & (1 << slotIndex)) != 0;
        public bool CanEquipJob(int jobIndex) => (JobsMask & (1 << jobIndex)) != 0;
        public bool CanEquipRace(int raceIndex) => (RacesMask & (1 << raceIndex)) != 0;

        public override string ToString() => $"[Item {ItemId}] {Name} (Lv.{Level})";
    }
}
