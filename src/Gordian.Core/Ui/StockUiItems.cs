// src/Gordian.Core/Ui/StockUiItems.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources.Models;
using Gordian.Core.World;

namespace Gordian.Core.Ui
{
    /// <summary>Which window an item list (<see cref="StockUiItems.ListMenu"/>) belongs to.</summary>
    public enum StockUiItemListKind
    {
        /// <summary>The Items window: one container's items, Use / Drop on Confirm.</summary>
        Items,

        /// <summary>The Equipment window's list for one slot: the items that fit it, Confirm equips.</summary>
        Equipment,
    }

    /// <summary>
    /// One row of an item list (the Items window and the Equipment window share the list, #351 / #352): the item, its
    /// count and where it lives (sent in C2S 0x037 / 0x028 / 0x050). <see cref="Greyed"/> rows show why on Confirm
    /// (<see cref="GreyedReason"/>); <see cref="Equipped"/> marks an item worn in some slot; <see cref="IsRemove"/> is the
    /// Equipment list's "Remove" row (unequip the slot).
    /// </summary>
    public readonly record struct StockUiItemRow(ushort ItemId, string Name, uint Count, ContainerId Container, byte Slot,
        int StackSize = 1, bool Equipped = false, bool Greyed = false, string? GreyedReason = null, bool IsRemove = false)
    {
        /// <summary>Whether the count is drawn: stackable items and stacks of more than one.</summary>
        public bool ShowsCount => !IsRemove && (StackSize > 1 || Count > 1);
    }

    /// <summary>What the Equipment list checks an item against: the main job and its level (race is not known to the client state yet).</summary>
    public readonly record struct StockUiEquipCharacter(byte MainJob, byte MainJobLevel);

    /// <summary>
    /// The Equipment window's stats side (the English menu DAT's <c>persona</c> panel: HP, MP, TP, then STR ... CHR): the
    /// character's name, jobs line and the values. Base and bonus are S2C 0x061's (or 0x01B's) base stats and modifiers;
    /// Attack, Defense and the eight elemental resistances (Fire, Ice, Wind, Earth, Thunder, Water, Light, Dark, as
    /// <see cref="LocalPlayerState.ElementalResistances"/>) fill the labels and element marks baked under the grid.
    /// </summary>
    public sealed record StockUiCharacterStats(string Name, string Jobs, int MaxHp, int MaxMp, int Tp,
        IReadOnlyList<int> BaseStats, IReadOnlyList<int> StatBonuses, int Attack = 0, int Defense = 0, IReadOnlyList<int>? Resistances = null);

    /// <summary>
    /// The stock Items window (#351) and Equipment window (#352) from the English menu DAT (39542, ROM/119/51; read
    /// 2026-10-11 with the menu dump, help and title text from ROM/165/75 and /76):
    /// <list type="bullet">
    /// <item><c>inventor</c> (16,48 top-left, 182 x 190, title 2 "Items", help 3 "Select an item."): ten 132 x 16 rows at an
    /// 18 px pitch from (3,6), each with a 16 x 16 slot box (keytops3 #50) as its label, cursor group <c>anc_item</c>, the end
    /// rows linked to themselves (the list scrolls, as the shop list does). Both windows' lists use it; the Equipment
    /// window's is drawn beside the grid at x <see cref="EquipListX"/> (PROVISIONAL: retail's place not captured).</item>
    /// <item><c>iuse</c> (top right 384,48, 112 x 42, title "Items"): Use (windowps #129, greyed #413, help 26 "Use an
    /// item.") and Drop (#123 / #414, help 272 "Dispose of item. Most items will be placed in the recycle bin...").</item>
    /// <item><c>itmsortw</c> (top right 384,48, 112 x 77, "Options" / "+ : Sort"): Auto (help 269 "Automatically rearrange
    /// items."), Manual (help 270, greyed #561) and Switch (help 1067 "Display a different storage system.", greyed #826).
    /// It stands beside the list while the list is open; + (keyboard) / Y (gamepad) puts the cursor on it, as the Treasure
    /// Pool's "+ : Spoils Options" does.</item>
    /// <item><c>itmstora</c> (top right, 112 x 266): the sixteen storages, Inventory, Mog Safe, Mog Safe 2, Storage, Mog
    /// Locker, Mog Satchel, Mog Sack, Mog Case, Mog Wardrobe 1-8, each with a greyed alternate.</item>
    /// <item><c>equip</c> (130,48 top-left, 182 x 190, title 20 "Equipment"): the same 4 x 4 grid of slots as the check
    /// window (<see cref="StockUiCheck"/>: buttons 1-16 by their labels), cursor <c>anc_equi</c>; <c>persona</c> (16,48,
    /// 112 x 190, "Status": HP, MP, TP, STR ... CHR baked in) is the stats side; <c>mnstorag</c> (top right, "Storage
    /// Options": Inventory, Mog Wardrobe 1-8, help 803 "Display equipment in your inventory.") picks which storage's
    /// equipment the list shows.</item>
    /// </list>
    /// Packets (LandSandBoat, https://github.com/LandSandBoat/server, <c>src/map/packets/c2s/</c>): C2S 0x037 ITEM_USE
    /// (LandSandBoat rejects a non-zero ItemNum, so the count is sent as 0; containers Inventory, Temporary Items and the
    /// eight wardrobes; not in a Mog House; the target within 12 yalms), 0x028 ITEM_DUMP (any container but the recycle
    /// bin, 0-99 items, never gil), 0x03A ITEM_STACK (merges the container's partial stacks; the order of the list is the
    /// client's), 0x050 EQUIP_SET (item index 0 unequips; Inventory and Mog Wardrobe 1-8; the Satchel, Sack and Case only
    /// with a server setting).
    /// <para>
    /// PROVISIONAL (not checked against retail captures): the row layout (name 22 px in, count right-aligned), the header
    /// (storage name and used / total on the top border), greyed rows (equipped items cannot be dropped; equipment the job
    /// or level cannot wear), the auto-sort order (by item id), the Drop prompt's wording, the "Remove" row at the top of
    /// the Equipment list, where the description window sits (under the list, as the check window's) and the stats side's
    /// content.
    /// </para>
    /// </summary>
    public static class StockUiItems
    {
        public const string ListMenu = "inventor";
        public const int ListRows = 10;

        public const string ActionMenu = "iuse";
        public const int UseButton = 1, DropButton = 2;

        public const string OptionsMenu = "itmsortw";
        public const int AutoSortButton = 1, ManualSortButton = 2, SwitchButton = 3;

        public const string StorageMenu = "itmstora";

        public const string EquipmentMenu = "equip";
        public const string StatsMenu = "persona";
        public const string EquipStorageMenu = "mnstorag";

        /// <summary>Layout x of the Equipment window's list: right of the grid (130 + 182 + 2), in the top-right corner.</summary>
        public const int EquipListX = 314;

        /// <summary>The containers <c>itmstora</c>'s buttons 1-16 show, in their authored order (labels read from windowps).</summary>
        private static readonly ContainerId[] StorageButtons =
        {
            ContainerId.Inventory, ContainerId.MogSafe, ContainerId.MogSafe2, ContainerId.Storage, ContainerId.MogLocker,
            ContainerId.MogSatchel, ContainerId.MogSack, ContainerId.MogCase, ContainerId.Wardrobe, ContainerId.Wardrobe2,
            ContainerId.Wardrobe3, ContainerId.Wardrobe4, ContainerId.Wardrobe5, ContainerId.Wardrobe6, ContainerId.Wardrobe7,
            ContainerId.Wardrobe8,
        };

        /// <summary>The containers <c>mnstorag</c>'s buttons 1-9 show: Inventory, Mog Wardrobe 1-8.</summary>
        private static readonly ContainerId[] EquipStorageButtons =
        {
            ContainerId.Inventory, ContainerId.Wardrobe, ContainerId.Wardrobe2, ContainerId.Wardrobe3, ContainerId.Wardrobe4,
            ContainerId.Wardrobe5, ContainerId.Wardrobe6, ContainerId.Wardrobe7, ContainerId.Wardrobe8,
        };

        /// <summary>Containers an item can be used from (LandSandBoat <c>0x037_item_use.cpp</c>).</summary>
        public static readonly IReadOnlyList<ContainerId> UsableContainers = new[]
        {
            ContainerId.Inventory, ContainerId.TempItems, ContainerId.Wardrobe, ContainerId.Wardrobe2, ContainerId.Wardrobe3,
            ContainerId.Wardrobe4, ContainerId.Wardrobe5, ContainerId.Wardrobe6, ContainerId.Wardrobe7, ContainerId.Wardrobe8,
        };

        /// <summary>Containers equipment is worn from (LandSandBoat <c>0x050_equip_set.cpp</c>; the Satchel, Sack and Case need a server setting).</summary>
        public static readonly IReadOnlyList<ContainerId> EquippableContainers = EquipStorageButtons;

        /// <summary>The item flag of a usable item (LandSandBoat <c>ItemFlag::CanUse</c>); the DAT's valid targets say on whom.</summary>
        public const uint CanUseFlag = 0x0200;

        /// <summary>LandSandBoat <c>TARGETTYPE</c> bits of an item's valid targets (<c>battle_entity.h</c>).</summary>
        public const ushort TargetSelf = 0x0001, TargetParty = 0x0002, TargetEnemy = 0x0004, TargetAlliance = 0x0008,
            TargetPlayer = 0x0010, TargetPlayerDead = 0x0020, TargetNpc = 0x0040, TargetPet = 0x0100;

        public static bool TryGetStorage(int buttonId, out ContainerId container) => TryGet(StorageButtons, buttonId, out container);

        public static bool TryGetEquipStorage(int buttonId, out ContainerId container) => TryGet(EquipStorageButtons, buttonId, out container);

        public static int StorageButtonOf(ContainerId container) => Array.IndexOf(StorageButtons, container) + 1;

        public static int EquipStorageButtonOf(ContainerId container) => Array.IndexOf(EquipStorageButtons, container) + 1;

        private static bool TryGet(ContainerId[] buttons, int buttonId, out ContainerId container)
        {
            if (buttonId >= 1 && buttonId <= buttons.Length)
            {
                container = buttons[buttonId - 1];
                return true;
            }
            container = default;
            return false;
        }

        /// <summary>A storage's name as the <c>itmstora</c> labels and the title table write it.</summary>
        public static string ContainerName(ContainerId container) => container switch
        {
            ContainerId.Inventory => "Inventory",
            ContainerId.MogSafe => "Mog Safe",
            ContainerId.MogSafe2 => "Mog Safe 2",
            ContainerId.Storage => "Storage",
            ContainerId.TempItems => "Temporary Items",
            ContainerId.MogLocker => "Mog Locker",
            ContainerId.MogSatchel => "Mog Satchel",
            ContainerId.MogSack => "Mog Sack",
            ContainerId.MogCase => "Mog Case",
            ContainerId.Wardrobe => "Mog Wardrobe",
            >= ContainerId.Wardrobe2 and <= ContainerId.Wardrobe8 => $"Mog Wardrobe {(int)container - (int)ContainerId.Wardrobe2 + 2}",
            ContainerId.RecycleBin => "Recycle Bin",
            _ => container.ToString(),
        };

        /// <summary>
        /// Whether a container is available: S2C 0x01C gave it slots and did not disable it. LandSandBoat sends every size
        /// as 1 + the container's slots (slot 0 counted) and a usable size of 0 for a disabled one (<c>0x01c_item_max.cpp</c>).
        /// </summary>
        public static bool IsAvailable(InventoryState? inventory, ContainerId container)
        {
            if (inventory == null) return false;
            var state = inventory.GetContainer(container);
            return state.MaxSize > 1 && state.UsableSize > 1;
        }

        /// <summary>The used and total slots the list's header shows ("45/80"): occupied slots (gil left out) over the usable size.</summary>
        public static (int Used, int Total) Fill(InventoryState? inventory, ContainerId container)
        {
            if (inventory == null) return (0, 0);
            int used = 0;
            foreach (var item in inventory.SnapshotItems(container))
            {
                if (item.Slot >= 1 && item.ItemId != 0 && item.ItemId != InventoryState.GilItemId) used++;
            }
            var state = inventory.GetContainer(container);
            // S2C 0x01C counts slot 0 in every size: 81 means 80 item slots.
            int total = (state.UsableSize > 0 ? state.UsableSize : state.MaxSize) - 1;
            return (used, Math.Max(0, total));
        }

        /// <summary>The (container, slot) pairs worn in any equipment slot (S2C 0x050; index 0 or 0xFF is empty).</summary>
        public static HashSet<(ContainerId, byte)> EquippedSlots(InventoryState? inventory)
        {
            var set = new HashSet<(ContainerId, byte)>();
            if (inventory == null) return set;
            for (var slot = EquipSlotId.Main; slot <= EquipSlotId.Back; slot++)
            {
                var (container, index) = inventory.GetEquipped(slot);
                if (index != 0 && index != 0xFF) set.Add((container, index));
            }
            return set;
        }

        /// <summary>The item worn in an equipment slot, if any.</summary>
        public static InventoryItem? EquippedItem(InventoryState? inventory, EquipSlotId slot)
        {
            if (inventory == null) return null;
            var (container, index) = inventory.GetEquipped(slot);
            if (index == 0 || index == 0xFF) return null;
            return inventory.GetContainer(container).TryGetItem(index, out var item) && item.ItemId != 0 ? item : null;
        }

        /// <summary>Whether an item can be used at all (its DAT valid targets name someone).</summary>
        public static bool IsUsable(ItemRecord? record) => record != null && record.ValidTargets != 0;

        /// <summary>
        /// The Items window's rows for a container: its items in slot order (gil left out), or by item id when the
        /// container has been auto-sorted (<paramref name="sorted"/>); worn items are marked.
        /// </summary>
        public static List<StockUiItemRow> Rows(InventoryState? inventory, ContainerId container, Func<ushort, ItemRecord?>? lookup, bool sorted = false)
        {
            var rows = new List<StockUiItemRow>();
            if (inventory == null) return rows;
            var equipped = EquippedSlots(inventory);
            var items = inventory.SnapshotItems(container);
            Array.Sort(items, (a, b) => a.Slot.CompareTo(b.Slot));
            foreach (var item in items)
            {
                if (item.Slot == 0 || item.ItemId == 0 || item.ItemId == InventoryState.GilItemId || item.Count == 0) continue;
                var record = StockUiShop.Lookup(lookup, item.ItemId);
                rows.Add(new StockUiItemRow(item.ItemId, record.Name, item.Count, container, item.Slot, Math.Max(1, (int)record.StackSize),
                    Equipped: equipped.Contains((container, item.Slot))));
            }
            if (sorted) rows.Sort(CompareSorted);
            return rows;
        }

        /// <summary>Auto-sort's order: by item id, then slot (PROVISIONAL: retail's category order is not captured).</summary>
        private static int CompareSorted(StockUiItemRow a, StockUiItemRow b)
        {
            int byId = a.ItemId.CompareTo(b.ItemId);
            return byId != 0 ? byId : a.Slot.CompareTo(b.Slot);
        }

        /// <summary>The DAT equip-slot mask bit of a slot (<c>EquipSlotsMask</c> bit n = slot n; the ears and rings share a type).</summary>
        public static bool FitsSlot(ItemRecord record, EquipSlotId slot) => record.CanEquipSlot((int)slot);

        /// <summary>
        /// Why the character cannot wear an item that fits the slot, or null when it can: the main job must be in the item's
        /// jobs and its level at least the item's.
        /// </summary>
        public static string? CannotEquipReason(ItemRecord record, StockUiEquipCharacter character)
        {
            if (character.MainJob != 0 && record.JobsMask != 0 && !record.CanEquipJob(character.MainJob)) return "Your current job cannot equip that item.";
            if (character.MainJobLevel != 0 && record.Level != ushort.MaxValue && record.Level > character.MainJobLevel) return "Your level is too low to equip that item.";
            return null;
        }

        /// <summary>
        /// The Equipment list for a slot: "Remove" first when the slot holds an item, then every item in the equippable
        /// containers (or <paramref name="only"/>) whose equip mask fits the slot, in container then slot order. Items the
        /// job or level cannot wear, and items worn in another slot, are greyed; the item in the slot itself is marked.
        /// </summary>
        public static List<StockUiItemRow> EquipRows(InventoryState? inventory, EquipSlotId slot, Func<ushort, ItemRecord?>? lookup,
            StockUiEquipCharacter character, ContainerId? only = null)
        {
            var rows = new List<StockUiItemRow>();
            if (inventory == null) return rows;
            var worn = EquippedItem(inventory, slot);
            if (worn != null) rows.Add(new StockUiItemRow(0, RemoveText, 0, worn.Container, 0, IsRemove: true));
            var equipped = EquippedSlots(inventory);
            foreach (var container in EquippableContainers)
            {
                if (only is { } filter && filter != container) continue;
                var items = inventory.SnapshotItems(container);
                Array.Sort(items, (a, b) => a.Slot.CompareTo(b.Slot));
                foreach (var item in items)
                {
                    if (item.Slot == 0 || item.ItemId == 0 || item.ItemId == InventoryState.GilItemId) continue;
                    var record = lookup?.Invoke(item.ItemId);
                    if (record == null || !FitsSlot(record, slot)) continue;
                    bool wornHere = worn != null && worn.Container == container && worn.Slot == item.Slot;
                    bool wornElsewhere = !wornHere && equipped.Contains((container, item.Slot));
                    string? reason = wornElsewhere ? "That item is equipped in another slot." : CannotEquipReason(record, character);
                    rows.Add(new StockUiItemRow(item.ItemId, record.Name, item.Count, container, item.Slot, Math.Max(1, (int)record.StackSize),
                        Equipped: wornHere || wornElsewhere, Greyed: reason != null, GreyedReason: reason));
                }
            }
            return rows;
        }

        /// <summary>The Equipment list's first row while the slot holds an item (PROVISIONAL wording).</summary>
        public const string RemoveText = "Remove";

        /// <summary>
        /// The Drop prompt (PROVISIONAL wording: retail's is not captured): "Drop a Ronfaure chestnut?", "Drop 12 Ronfaure chestnuts?".
        /// </summary>
        public static string DropPrompt(ItemRecord record, uint count) => $"Drop {StockUiShop.DescribeCount(record, count)}?";

        private static readonly Dictionary<string, EquipSlotId> SlotWords = new(StringComparer.OrdinalIgnoreCase)
        {
            ["main"] = EquipSlotId.Main, ["sub"] = EquipSlotId.Sub, ["range"] = EquipSlotId.Ranged, ["ranged"] = EquipSlotId.Ranged,
            ["ammo"] = EquipSlotId.Ammo, ["head"] = EquipSlotId.Head, ["body"] = EquipSlotId.Body, ["hands"] = EquipSlotId.Hands,
            ["legs"] = EquipSlotId.Legs, ["feet"] = EquipSlotId.Feet, ["neck"] = EquipSlotId.Neck, ["waist"] = EquipSlotId.Waist,
            ["ear1"] = EquipSlotId.Ear1, ["lear"] = EquipSlotId.Ear1, ["ear2"] = EquipSlotId.Ear2, ["rear"] = EquipSlotId.Ear2,
            ["ring1"] = EquipSlotId.Ring1, ["lring"] = EquipSlotId.Ring1, ["ring2"] = EquipSlotId.Ring2, ["rring"] = EquipSlotId.Ring2,
            ["back"] = EquipSlotId.Back,
        };

        /// <summary>The slot an <c>/equip</c> slot word names (main, sub, range, ammo, head, neck, ear1/lear, ear2/rear, body, hands, ring1/lring, ring2/rring, back, waist, legs, feet).</summary>
        public static bool TryParseSlot(string word, out EquipSlotId slot) => SlotWords.TryGetValue(word.Trim(), out slot);

        /// <summary>
        /// Splits a command's arguments into a name (quoted, or the first word when unquoted) and the rest:
        /// <c>"Echo Drops" &lt;me&gt;</c> gives ("Echo Drops", "&lt;me&gt;"). With <paramref name="nameIsRest"/> an unquoted
        /// name runs to the end (<c>/equip head Bronze Cap</c>).
        /// </summary>
        public static (string Name, string Target) SplitName(string args, bool nameIsRest = false)
        {
            string trimmed = args.Trim();
            if (trimmed.StartsWith('"'))
            {
                int close = trimmed.IndexOf('"', 1);
                return close > 0 ? (trimmed[1..close].Trim(), trimmed[(close + 1)..].Trim()) : (trimmed.Trim('"').Trim(), string.Empty);
            }
            if (nameIsRest) return (trimmed, string.Empty);
            // Unquoted: the name may hold spaces, so the target is the last word when it is a <...> token.
            int lastSpace = trimmed.LastIndexOf(' ');
            if (lastSpace > 0 && trimmed[(lastSpace + 1)..].StartsWith('<'))
            {
                return (trimmed[..lastSpace].Trim(), trimmed[(lastSpace + 1)..]);
            }
            return (trimmed, string.Empty);
        }

        /// <summary>Whether an item's names match a typed name (the short name or the log name, case-insensitive).</summary>
        public static bool NameMatches(ItemRecord record, string name) =>
            record.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
            || (record.LogName.Length > 0 && record.LogName.Equals(name, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// The first item named <paramref name="name"/> in the containers, in their order and then slot order (an item
        /// id in digits also matches). Null when none is held.
        /// </summary>
        public static InventoryItem? FindItem(InventoryState? inventory, Func<ushort, ItemRecord?>? lookup, string name,
            IReadOnlyList<ContainerId> containers, Func<InventoryItem, ItemRecord, bool>? accept = null)
        {
            if (inventory == null || string.IsNullOrWhiteSpace(name)) return null;
            bool byId = ushort.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out ushort id);
            foreach (var container in containers)
            {
                var items = inventory.SnapshotItems(container);
                Array.Sort(items, (a, b) => a.Slot.CompareTo(b.Slot));
                foreach (var item in items)
                {
                    if (item.Slot == 0 || item.ItemId == 0 || item.ItemId == InventoryState.GilItemId) continue;
                    var record = StockUiShop.Lookup(lookup, item.ItemId);
                    bool match = byId ? item.ItemId == id : NameMatches(record, name);
                    if (match && (accept == null || accept(item, record))) return item;
                }
            }
            return null;
        }
    }
}
