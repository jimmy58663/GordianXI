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
        /// <summary>The main menu's Items window: the inventory, Use / Drop on Confirm, the sort options beside it.</summary>
        Items,

        /// <summary>The command menu's Items window: the usable items only; Confirm uses the item at once.</summary>
        Usable,

        /// <summary>The Recycling Bin (the options' Recycle Bin): thrown-away items; Confirm restores one to the inventory.</summary>
        RecycleBin,

        /// <summary>The Equipment window's list for one slot: the items of one storage that fit it; Confirm equips.</summary>
        Equipment,
    }

    /// <summary>
    /// One row of an item list (the Items and Equipment windows share the list, #351 / #352): the item, its count and where
    /// it lives (sent in C2S 0x037 / 0x028 / 0x029 / 0x050). <see cref="Greyed"/> rows show why on Confirm
    /// (<see cref="GreyedReason"/>); <see cref="Equipped"/> marks an item worn in some slot; <see cref="IsStorageOptions"/> is
    /// the Equipment list's first row, "Storage Options ▶", which opens the storage choice.
    /// </summary>
    public readonly record struct StockUiItemRow(ushort ItemId, string Name, uint Count, ContainerId Container, byte Slot,
        int StackSize = 1, bool Equipped = false, bool Greyed = false, string? GreyedReason = null, bool IsStorageOptions = false,
        StockUiSetRowAction SetAction = StockUiSetRowAction.None)
    {
        /// <summary>Whether the count is drawn over the icon: stackable items and stacks of more than one.</summary>
        public bool ShowsCount => !IsStorageOptions && SetAction == StockUiSetRowAction.None && (StackSize > 1 || Count > 1);
    }

    /// <summary>What the Equipment list checks an item against: the main job and its level (race is not known to the client state yet).</summary>
    public readonly record struct StockUiEquipCharacter(byte MainJob, byte MainJobLevel);

    /// <summary>
    /// The Equipment window's stats side (the English menu DAT's <c>persona</c> panel, laid out as the maintainer's retail
    /// capture 2026-10-11 shows it: the name, "Lv99 Bard" (the main job, blue), "Lv49 White Mage", "ILv 119/Su 0", then
    /// HP and MP as current / max, TP, and STR ... CHR as the base with the bonus in green after it). Attack, Defense and the
    /// eight elemental resistances (Fire, Ice, Wind, Earth, Thunder, Water, Light, Dark, as
    /// <see cref="LocalPlayerState.ElementalResistances"/>) fill the labels and element marks baked under the grid.
    /// </summary>
    public sealed record StockUiCharacterStats(string Name, string MainJob, string SubJob, string ItemLevelLine,
        int Hp, int MaxHp, int Mp, int MaxMp, int Tp, IReadOnlyList<int> BaseStats, IReadOnlyList<int> StatBonuses,
        int Attack = 0, int Defense = 0, IReadOnlyList<int>? Resistances = null);

    /// <summary>
    /// The help bar an item window shows at the top of the screen (<c>titlewin</c> + <c>helpwind</c>): the title, the count
    /// fraction drawn after it ("29/30", null for none) and the help text id (ROM/165/75; -1 for none).
    /// </summary>
    public readonly record struct StockUiItemHelpBar(string Title, int? Numerator, int? Denominator, int HelpTextId);

    /// <summary>
    /// The stock Items window (#351) and Equipment window (#352) from the English menu DAT (39542, ROM/119/51; read
    /// 2026-10-11 with the menu dump, help and title text from ROM/165/75 and /76), laid out after the maintainer's retail
    /// captures (2026-10-11, 2560 x 1440, the UI drawn 1:1):
    /// <list type="bullet">
    /// <item><c>inventor</c> (16,48 top-left, 182 x 190, title 2 "Items", help 3 "Select an item."): ten 132 x 16 rows at an
    /// 18 px pitch from (3,6), each with a 16 x 16 slot box (keytops3 #50) as its label, cursor group <c>anc_item</c> (the
    /// orange box over the icon and a highlight bar), the end rows linked to themselves (the list scrolls). Every list
    /// (main menu Items, command menu Items, the Recycling Bin, the Equipment list at x <see cref="EquipListX"/>) uses it.</item>
    /// <item><c>iuse</c> (top right 384,48, 112 x 42, title "Items"): Use (windowps #129, greyed #413, help 26 "Use an
    /// item.") and Drop (#123 / #414, help 272).</item>
    /// <item><c>itmsort2</c> (top right 384,48, 112 x 77, "Options" / "+ : Sort"): Auto (help 269), Manual (help 270) and
    /// Recycle Bin (help 1068 "Display thrown-away items. They will disappear for good when you change areas, log out, or
    /// disconnect."). It stands at the top right while the main menu's Items list is open; + (keyboard) / Y (gamepad)
    /// puts the cursor on it. Auto asks <c>sortyn</c> (384,124 under it: "Commands", Yes / No, help 80 "Sort list?", No
    /// under the cursor); Manual returns to the list (help 5 "Select item to move.").</item>
    /// <item><c>equip</c> (130,48 top-left, 182 x 190, title 20 "Equipment"): the 4 x 4 grid of the check window
    /// (<see cref="StockUiCheck"/>), cursor <c>anc_equi</c>; <c>persona</c> (16,48, 112 x 190, "Status") is the stats side
    /// and <c>money</c> ("Current Gil") under it; the item list beside the grid shows the storage's equipment and, after
    /// Confirm on a slot, the items of that slot; its first row, "Storage Options ▶", opens <c>mnstorag</c> (top right:
    /// Inventory, Mog Wardrobe 1-8, help 803 "Display equipment in your inventory.").</item>
    /// </list>
    /// Packets (LandSandBoat, https://github.com/LandSandBoat/server, <c>src/map/packets/c2s/</c>): C2S 0x037 ITEM_USE
    /// (LandSandBoat rejects a non-zero ItemNum, so the count is sent as 0; containers Inventory, Temporary Items and the
    /// eight wardrobes; not in a Mog House; the target within 12 yalms), 0x028 ITEM_DUMP (any container but the recycle
    /// bin, 0-99 items, never gil; an inventory item goes to the recycle bin unless it is NoRecycle), 0x029 ITEM_MOVE (the
    /// bin's items go back to the inventory or the Mog Locker; <c>ItemIndex2</c> 82 = "any free slot", XiPackets), 0x03A
    /// ITEM_STACK (merges the container's partial stacks; the order of the list is the client's), 0x050 EQUIP_SET (item
    /// index 0 unequips; Inventory and Mog Wardrobe 1-8; the Satchel, Sack and Case only with a server setting).
    /// LandSandBoat empties the recycle bin when the character is loaded, i.e. at every zone-in (<c>charutils::LoadChar</c>
    /// -> <c>EmptyRecycleBin</c>), and tells the client its contents with S2C 0x020 (<c>ITEM_ATTR</c>, container 17).
    /// </summary>
    public static class StockUiItems
    {
        public const string ListMenu = "inventor";

        /// <summary>The main menu's Items entry ("menuwind" button 4), where the cursor returns with the Items window.</summary>
        public const int MainMenuItemsButton = 4;
        public const int ListRows = 10;

        public const string ActionMenu = "iuse";
        public const int UseButton = 1, DropButton = 2;

        public const string OptionsMenu = "itmsort2";
        public const int AutoSortButton = 1, ManualSortButton = 2, RecycleBinButton = 3;

        /// <summary>Auto-sort's confirmation ("Commands": Yes / No) under the options.</summary>
        public const string SortConfirmMenu = "sortyn";
        public const int SortYesButton = 1, SortNoButton = 2;

        public const string EquipmentMenu = "equip";
        public const string StatsMenu = "persona";
        public const string GilMenu = "money";
        public const string EquipStorageMenu = "mnstorag";

        /// <summary>Layout x of the Equipment window's list: right of the grid (130 + 182 + 2; retail capture 2026-10-11).</summary>
        public const int EquipListX = 314;

        /// <summary>Layout x of the Recycling Bin's companion inventory list, drawn right beside the bin (16 + 182; retail capture 2026-10-11).</summary>
        public const int RecycleInventoryX = 198;

        /// <summary>The Equipment window's description and the Drop / restore description beside the count box (layout x 130).</summary>
        public const int EquipDescriptionX = 130;

        /// <summary>Help text ids (ROM/165/75): "Select an item.", "Select item to move.", "Sort list?".</summary>
        public const int HelpSelectItem = 3, HelpSelectItemToMove = 5, HelpSortList = 80;

        /// <summary>Title text ids (ROM/165/76): "Items", "Equipment", "No. of Items", "Recycling Bin".</summary>
        public const string TitleItems = "Items", TitleEquipment = "Equipment", TitleQuantity = "No. of Items", TitleRecycleBin = "Recycling Bin";

        /// <summary>
        /// The client message (ROM/165/70 #140) shown in the error colour when a crystal is used with a full inventory
        /// (the maintainer's retail check, 2026-10-11).
        /// </summary>
        public const string InventoryFullMessage = "Unable to execute that command. Your inventory is full.";

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

        /// <summary>
        /// ItemIndex2 of a C2S 0x029 that moves an item into another container's first free slot (XiPackets
        /// <c>world/client/0x0029</c>: "set to 82 when the client is moving items to another container"; LandSandBoat unites
        /// stacks below 82).
        /// </summary>
        public const byte AnyFreeSlot = 82;

        public static bool TryGetEquipStorage(int buttonId, out ContainerId container)
        {
            if (buttonId >= 1 && buttonId <= EquipStorageButtons.Length)
            {
                container = EquipStorageButtons[buttonId - 1];
                return true;
            }
            container = default;
            return false;
        }

        public static int EquipStorageButtonOf(ContainerId container) => Array.IndexOf(EquipStorageButtons, container) + 1;

        /// <summary>A storage's name as the storage labels and the title table write it.</summary>
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
            ContainerId.RecycleBin => "Recycling Bin",
            _ => container.ToString(),
        };

        /// <summary>
        /// Whether a container is available: S2C 0x01C gave it slots and did not disable it. LandSandBoat sends every size
        /// as 1 + the container's slots (slot 0 counted) and a usable size of 0 for a disabled one (<c>0x01c_item_max.cpp</c>);
        /// <see cref="S2C_0x01C_ItemMax"/> already takes the 1 off, so <see cref="ContainerState.MaxSize"/> is the slot count.
        /// </summary>
        public static bool IsAvailable(InventoryState? inventory, ContainerId container)
        {
            if (inventory == null) return false;
            var state = inventory.GetContainer(container);
            return state.MaxSize > 0 && state.UsableSize > 0;
        }

        /// <summary>The used and total slots the help bar shows ("29/30"): occupied slots (gil left out) over the usable size.</summary>
        public static (int Used, int Total) Fill(InventoryState? inventory, ContainerId container)
        {
            if (inventory == null) return (0, 0);
            int used = 0;
            foreach (var item in inventory.SnapshotItems(container))
            {
                if (item.Slot >= 1 && item.ItemId != 0 && item.ItemId != InventoryState.GilItemId) used++;
            }
            var state = inventory.GetContainer(container);
            // The decoder (S2C_0x01C_ItemMax) has already taken off LandSandBoat's slot 0: the sizes are the item slots. Subtracting
            // it again showed 29 for a 30-slot inventory (the maintainer's round 2, 2026-10-12).
            int total = state.UsableSize > 0 ? state.UsableSize : state.MaxSize;
            return (used, Math.Max(0, total));
        }

        /// <summary>Whether the inventory is full (its size known from S2C 0x01C).</summary>
        public static bool IsInventoryFull(InventoryState? inventory)
        {
            var (used, total) = Fill(inventory, ContainerId.Inventory);
            return total > 0 && used >= total;
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

        /// <summary>
        /// Whether an item is a crystal (fire ... dark, 4096-4103, and the HQ crystals, 4238-4245): using one opens the
        /// synthesis window rather than sending C2S 0x037 (the maintainer's retail check, 2026-10-11). Item ids from
        /// LandSandBoat's item tables.
        /// </summary>
        public static bool IsCrystal(ushort itemId) => itemId is >= 4096 and <= 4103 or >= 4238 and <= 4245;

        /// <summary>
        /// Whether an item can be used: a crystal (synthesis), or an item flagged CanUse (LandSandBoat <c>ItemFlag::CanUse</c>,
        /// 0x200) whose DAT valid targets name someone. Equipment carrying valid targets without the flag (an Ark Sword's
        /// 0x01) is not usable: retail greys its Use (the maintainer's retail check, 2026-10-11).
        /// </summary>
        public static bool IsUsable(ItemRecord? record) =>
            record != null && (IsCrystal((ushort)record.ItemId) || ((record.Flags & CanUseFlag) != 0 && record.ValidTargets != 0));

        /// <summary>
        /// Whether an item is equipment: DAT item type 4 (weapons, ammo, instruments) or 5 (armour, shields). Other types can
        /// carry a non-zero equip mask in the same field (a Linkshell's reads 0x01), so the mask alone does not say; a record
        /// without a type (tests, hand-made records) falls back to the mask.
        /// </summary>
        public static bool IsEquipment(ItemRecord? record) =>
            record != null && (record.ItemType is ItemTypeWeapon or ItemTypeArmor || (record.ItemType == 0 && (record.EquipSlotsMask != 0 || record.Skill != 0)));

        /// <summary>DAT item types (the record's type field; read from the English item DATs, 2026-10-12).</summary>
        public const ushort ItemTypeGeneral = 1, ItemTypeWeapon = 4, ItemTypeArmor = 5, ItemTypeLinkshell = 6, ItemTypeUsable = 7, ItemTypeCrystal = 8,
            ItemTypeFurnishing = 10;

        /// <summary>
        /// Type 1 auto-sort ("Sort items by category when organizing them.", Config > Gameplay > Inventory Type). Derived from
        /// the maintainer's retail captures (2026-10-12, image-29 to image-37) against the DAT fields of every item shown:
        /// crystals (type 8); usable items (CanUse); weapons by skill (dagger, sword, great sword, ... katana, great katana;
        /// archery, marksmanship); ammo (the ammo slot, fishing lures included); instruments and other range-slot tools (string,
        /// wind, handbell, fishing); shields; then head, body, hands, legs, feet, neck, waist, back, earrings, rings; then
        /// everything else (materials, linkshells, furnishings, key-like items). Equipment within a group goes by level
        /// descending (the item level when it has one: 119 before 99), then item id; the other groups by item id.
        /// </summary>
        public static int SortCategory(ItemRecord? record)
        {
            if (record == null) return 99;
            if (record.ItemType == ItemTypeCrystal || IsCrystal((ushort)record.ItemId)) return 0;
            if (!IsEquipment(record)) return (record.Flags & CanUseFlag) != 0 && record.ValidTargets != 0 ? 1 : 90;
            bool range = record.CanEquipSlot((int)EquipSlotId.Ranged), main = record.CanEquipSlot((int)EquipSlotId.Main);
            if (record.CanEquipSlot((int)EquipSlotId.Ammo)) return 30;
            if (range && record.Skill is 41 or 42 or 45 or 48) return 40;
            if (main || range) return 20;
            if (record.ShieldSize > 0 || record.CanEquipSlot((int)EquipSlotId.Sub)) return 50;
            ReadOnlySpan<EquipSlotId> order = stackalloc EquipSlotId[]
            {
                EquipSlotId.Head, EquipSlotId.Body, EquipSlotId.Hands, EquipSlotId.Legs, EquipSlotId.Feet, EquipSlotId.Neck,
                EquipSlotId.Waist, EquipSlotId.Back, EquipSlotId.Ear1, EquipSlotId.Ring1,
            };
            for (int i = 0; i < order.Length; i++)
            {
                if (record.CanEquipSlot((int)order[i])) return 60 + i;
            }
            return 89;
        }

        /// <summary>The level an equipment group sorts by: the item level when there is one, else the level (65535 = none).</summary>
        private static int SortLevel(ItemRecord record) => record.ItemLevel > 0 ? record.ItemLevel : record.Level == ushort.MaxValue ? 0 : record.Level;

        /// <summary>Compares two items in Type 1 order (<see cref="SortCategory"/>).</summary>
        public static int CompareByCategory(ItemRecord? a, ItemRecord? b, ushort idA, ushort idB)
        {
            int ca = SortCategory(a), cb = SortCategory(b);
            if (ca != cb) return ca.CompareTo(cb);
            if (a != null && b != null && IsEquipment(a))
            {
                if (ca == 20 && a.Skill != b.Skill) return a.Skill.CompareTo(b.Skill);
                int byLevel = SortLevel(b).CompareTo(SortLevel(a));
                if (byLevel != 0) return byLevel;
            }
            return idA.CompareTo(idB);
        }

        /// <summary>
        /// A container's rows in slot order (gil left out), worn items marked; with <paramref name="usableOnly"/> only the
        /// items that can be used (the command menu's list, the maintainer's capture 2026-10-11). The client's own order
        /// (auto / manual sort) is applied by the caller.
        /// </summary>
        public static List<StockUiItemRow> Rows(InventoryState? inventory, ContainerId container, Func<ushort, ItemRecord?>? lookup, bool usableOnly = false)
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
                if (usableOnly && !IsUsable(lookup?.Invoke(item.ItemId))) continue;
                rows.Add(new StockUiItemRow(item.ItemId, record.Name, item.Count, container, item.Slot, Math.Max(1, (int)record.StackSize),
                    Equipped: equipped.Contains((container, item.Slot))));
            }
            return rows;
        }

        /// <summary>
        /// Orders rows by a client order of slots (auto / manual sort); slots not in the order keep their place after the
        /// ordered ones, in slot order.
        /// </summary>
        public static void ApplyOrder(List<StockUiItemRow> rows, IReadOnlyList<byte>? order)
        {
            if (order == null || order.Count == 0) return;
            var rank = new Dictionary<byte, int>(order.Count);
            for (int i = 0; i < order.Count; i++) rank.TryAdd(order[i], i);
            rows.Sort((a, b) =>
            {
                int ra = rank.TryGetValue(a.Slot, out int x) ? x : int.MaxValue, rb = rank.TryGetValue(b.Slot, out int y) ? y : int.MaxValue;
                return ra != rb ? ra.CompareTo(rb) : a.Slot.CompareTo(b.Slot);
            });
        }

        /// <summary>
        /// Auto-sort's order of slots: Type 1 by category (<see cref="CompareByCategory"/>, from the retail captures), Type 2 by
        /// name ("Sort items by name when organizing them and stacking them when possible."; PROVISIONAL: on retail the
        /// maintainer saw Type 2 leave the order unchanged). Equal items keep slot order.
        /// </summary>
        public static List<byte> AutoOrder(IEnumerable<StockUiItemRow> rows, Func<ushort, ItemRecord?>? lookup, int type = 1)
        {
            var sorted = new List<StockUiItemRow>(rows);
            sorted.Sort((a, b) =>
            {
                int by = type == 2
                    ? string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase)
                    : CompareByCategory(lookup?.Invoke(a.ItemId), lookup?.Invoke(b.ItemId), a.ItemId, b.ItemId);
                return by != 0 ? by : a.Slot.CompareTo(b.Slot);
            });
            return sorted.ConvertAll(r => r.Slot);
        }

        /// <summary>
        /// Manual sort (the maintainer's retail check, 2026-10-11): the first item chosen is put below the second when it was
        /// above it, above it when it was below; i.e. it takes the second's place. Returns the new order of slots.
        /// </summary>
        public static List<byte> MoveInOrder(IReadOnlyList<StockUiItemRow> rows, int from, int to)
        {
            var slots = new List<byte>(rows.Count);
            foreach (var row in rows) slots.Add(row.Slot);
            if (from < 0 || from >= slots.Count || to < 0 || to >= slots.Count || from == to) return slots;
            byte moved = slots[from];
            slots.RemoveAt(from);
            slots.Insert(to, moved);
            return slots;
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
        /// The Equipment list: "Storage Options ▶" first, then the equipment in <paramref name="container"/> in slot order,
        /// only the items that fit <paramref name="slot"/> when one is given (after Confirm on a grid slot), every piece of
        /// equipment otherwise (the list beside the grid while the cursor is on it; the maintainer's captures, 2026-10-11).
        /// Items the job or level cannot wear, and items worn in another slot, are greyed (PROVISIONAL); the item in the slot
        /// itself is marked worn, and Confirm on it removes it.
        /// </summary>
        public static List<StockUiItemRow> EquipRows(InventoryState? inventory, ContainerId container, EquipSlotId? slot, Func<ushort, ItemRecord?>? lookup,
            StockUiEquipCharacter character)
        {
            var rows = new List<StockUiItemRow> { new(0, StorageOptionsText, 0, container, 0, IsStorageOptions: true) };
            if (inventory == null) return rows;
            var worn = slot is { } s ? EquippedItem(inventory, s) : null;
            var equipped = EquippedSlots(inventory);
            var items = inventory.SnapshotItems(container);
            Array.Sort(items, (a, b) => a.Slot.CompareTo(b.Slot));
            foreach (var item in items)
            {
                if (item.Slot == 0 || item.ItemId == 0 || item.ItemId == InventoryState.GilItemId) continue;
                var record = lookup?.Invoke(item.ItemId);
                if (record == null || !IsEquipment(record) || record.EquipSlotsMask == 0) continue;
                if (slot is { } fit && !FitsSlot(record, fit)) continue;
                bool isEquipped = equipped.Contains((container, item.Slot));
                bool wornHere = worn != null && worn.Container == container && worn.Slot == item.Slot;
                string? reason = slot == null ? null
                    : isEquipped && !wornHere ? "That item is equipped in another slot."
                    : wornHere ? null : CannotEquipReason(record, character);
                rows.Add(new StockUiItemRow(item.ItemId, record.Name, item.Count, container, item.Slot, Math.Max(1, (int)record.StackSize),
                    Equipped: isEquipped, Greyed: reason != null, GreyedReason: reason));
            }
            return rows;
        }

        /// <summary>The Equipment list's first row (retail capture 2026-10-11).</summary>
        public const string StorageOptionsText = "Storage Options";

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
