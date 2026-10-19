// src/Gordian.Core/Ui/StockUiSynthesis.cs
using System;
using System.Collections.Generic;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources.Models;
using Gordian.Core.World;

namespace Gordian.Core.Ui
{
    /// <summary>
    /// One filled slot of the synthesis grid: the item, how many of it, and the inventory slot it is taken from first
    /// (more of the same item come from its other stacks when that one runs short).
    /// </summary>
    public readonly record struct StockUiSynthesisSlot(ushort ItemId, uint Count, byte InventorySlot);

    /// <summary>The crystal of an open synthesis grid: its item id and inventory slot (0 when none of it is held).</summary>
    public readonly record struct StockUiSynthesisCrystal(ushort ItemId, byte InventorySlot);

    /// <summary>
    /// The stock Synthesis windows (#330) from the English menu DAT (39542, ROM/119/51; read 2026-10-18 with the menu dump,
    /// help text from ROM/165/75, titles from ROM/165/76), laid out after the maintainer's retail captures
    /// (<c>Synthesis.png</c>, <c>Crystal_Synthesis.png</c>, <c>Synthesis_History.png</c>, 2026-10-16) and the video
    /// <c>Synth_Wind_Fire_Lightning_Breaks.mp4</c> (2026-10-16/17, the Items window's Use on a crystal, 33-42 s):
    /// <list type="bullet">
    /// <item><c>cmbmenu</c> (top right 384,48, 112 x 42, title 170 "Synthesis"): Crystal Synthesis (windowps #685, help 819
    /// "Expend crystals and synthesize items.") and History (#686, help 820 "View the list of previously synthesized items.").
    /// The main menu's Synthesis (help 816) opens it.</item>
    /// <item>Crystal Synthesis lists the inventory's crystals in <c>inventor</c> ("Items 3/30", "Select an item.").</item>
    /// <item><c>tskill1</c> (16,48, 182 x 94, title 114 "Synthesis", help 492 "Select the items you wish to synthesize.",
    /// cursor <c>anc_equi</c>): the 4 x 2 grid (buttons 1-8, 32 x 32 at (7,22) + 34 n) with OK (9, help 493 "Synthesize this
    /// combination of items.") and Cancel (10, help 379). The client draws the crystal's icon and its element word ("Heat",
    /// "Carve", "Split") in the frame's top band, each slot's item icon with the count over its bottom right, and the
    /// inventory list beside it at x 198 (<c>inventor</c>, help 7 "Select materials for synthesis." while it has the cursor).
    /// Confirm on a slot gives the list the cursor; a stack asks "No. of Items" (<c>itemctrl</c>, help 268); the item goes in
    /// and the cursor moves to the next slot. The crystal in use and worn items are greyed in the list, items placed in the
    /// grid red. OK sends C2S 0x096 and the windows close back to the menu they came from.</item>
    /// <item>History: the grid with <c>cmbhlst</c> (the same list frame, title 170) beside it listing the recipes done
    /// (<see cref="SynthesisHistory"/>), the selected recipe shown in the grid and its result described under the grid;
    /// "+ : Synthesis Options" (<c>cmbhwin</c>, top right, title 171 "Favorites": Favorite, help 817, and Unfavorite, help
    /// 818) marks a recipe.</item>
    /// </list>
    /// Packet: C2S 0x096 COMBINE_ASK (<see cref="CraftingPacketBuilders.BuildCombineAsk"/>, XiPackets world/client/0x0096,
    /// LandSandBoat c2s/0x096_combine_ask): the crystal, then up to eight ingredient items, one entry per item.
    /// PROVISIONAL (not in the captures): the element words of Ice, Earth, Water, Light and Dark (<see cref="ElementWord"/>),
    /// what Confirm on a filled slot does (here it empties the slot), the History list's order and what Confirm on a recipe
    /// does (here the cursor goes to the grid's OK), where the cursor starts in the material list (here its first row).
    /// </summary>
    public static class StockUiSynthesis
    {
        public const string Menu = "cmbmenu";
        public const int CrystalSynthesisButton = 1, HistoryButton = 2;

        public const string GridMenu = "tskill1";
        public const int SlotCount = 8, OkButton = 9, CancelButton = 10;

        public const string HistoryListMenu = "cmbhlst";

        /// <summary>The History list's options ("+ : Synthesis Options", PC keyboard art; <c>cmbh</c> is the gamepad one).</summary>
        public const string OptionsMenu = "cmbhwin";
        public const int FavoriteButton = 1, UnfavoriteButton = 2;

        /// <summary>Layout x of the material and History lists, right beside the grid (16 + 182; video and capture).</summary>
        public const int ListX = 198;

        /// <summary>Layout x of the grid's item description, under the grid (16, layout y 240; video 2026-10-16).</summary>
        public const int DescriptionX = 16;

        /// <summary>The most ingredient items one synthesis takes (C2S 0x096's eight entries).</summary>
        public const int MaxIngredients = CraftingPacketBuilders.MaxIngredients;

        /// <summary>Help text ids (ROM/165/75): "Select materials for synthesis.", "Synthesize this combination of items.".</summary>
        public const int HelpSelectMaterials = 7, HelpSynthesize = 493;

        /// <summary>Titles (ROM/165/76 #114 / #170): the grid's and the History list's help bar.</summary>
        public const string Title = "Synthesis";

        /// <summary>The lock message retail prints when a synthesis is already running (pink; capture 2026-10-07).</summary>
        public const string LockedMessage = "You cannot use that command during synthesis.";

        /// <summary>
        /// The crystal's element (0 Fire, 1 Ice, 2 Wind, 3 Earth, 4 Lightning, 5 Water, 6 Light, 7 Dark), or -1 for an item
        /// that is not a crystal. Crystals 4096-4103, their HQ forms 4238-4245 and 6506-6513 run in that order (item ids from
        /// LandSandBoat's <c>items.h</c>, <c>synthutils::crystalProps</c>).
        /// </summary>
        public static int ElementOf(ushort itemId) => itemId switch
        {
            >= 4096 and <= 4103 => itemId - 4096,
            >= 4238 and <= 4245 => itemId - 4238,
            >= 6506 and <= 6513 => itemId - 6506,
            _ => -1,
        };

        /// <summary>The plain crystal (4096-4103) of an element.</summary>
        public static ushort BaseCrystal(int element) => (ushort)(4096 + element);

        /// <summary>
        /// The word the grid shows after the crystal's icon: Fire "Heat" (the History capture and the video), Wind "Carve" and
        /// Lightning "Split" (the video, 2026-10-16). PROVISIONAL: the other five are not captured and show the element's name.
        /// </summary>
        public static string ElementWord(int element) => element switch
        {
            0 => "Heat",
            1 => "Ice",
            2 => "Carve",
            3 => "Earth",
            4 => "Split",
            5 => "Water",
            6 => "Light",
            7 => "Dark",
            _ => string.Empty,
        };

        /// <summary>Whether a crystal's element word is from a capture (the rest are <see cref="ElementWord"/>'s stand-ins).</summary>
        public static bool IsElementWordCaptured(int element) => element is 0 or 2 or 4;

        /// <summary>The inventory's crystals for the Crystal Synthesis list, in the client's order.</summary>
        public static List<StockUiItemRow> CrystalRows(List<StockUiItemRow> inventoryRows)
        {
            var rows = new List<StockUiItemRow>();
            foreach (var row in inventoryRows)
            {
                if (ElementOf(row.ItemId) >= 0) rows.Add(row);
            }
            return rows;
        }

        /// <summary>How many of an item the grid's slots hold, the slot <paramref name="except"/> left out (-1 for none).</summary>
        public static uint PlacedCount(IReadOnlyList<StockUiSynthesisSlot?> slots, ushort itemId, int except = -1)
        {
            uint n = 0;
            for (int i = 0; i < slots.Count; i++)
            {
                if (i != except && slots[i] is { } s && s.ItemId == itemId) n += s.Count;
            }
            return n;
        }

        /// <summary>How many items the grid holds in all, the slot <paramref name="except"/> left out.</summary>
        public static uint TotalPlaced(IReadOnlyList<StockUiSynthesisSlot?> slots, int except = -1)
        {
            uint n = 0;
            for (int i = 0; i < slots.Count; i++)
            {
                if (i != except && slots[i] is { } s) n += s.Count;
            }
            return n;
        }

        /// <summary>How many of an item the inventory holds, over all its stacks (the crystal in use left out of its own stack once).</summary>
        public static uint HeldCount(InventoryState? inventory, ushort itemId)
        {
            if (inventory == null) return 0;
            uint n = 0;
            foreach (var item in inventory.SnapshotItems(ContainerId.Inventory))
            {
                if (item.Slot != 0 && item.ItemId == itemId) n += item.Count;
            }
            return n;
        }

        /// <summary>
        /// The material list beside the grid: the inventory in the client's order, the crystal in use and worn items greyed
        /// (the video: the Wind Crystal and the linkshells lavender), items in the grid marked (red in the video).
        /// </summary>
        public static List<StockUiItemRow> MaterialRows(List<StockUiItemRow> inventoryRows, StockUiSynthesisCrystal crystal, IReadOnlyList<StockUiSynthesisSlot?> slots)
        {
            var rows = new List<StockUiItemRow>(inventoryRows.Count);
            foreach (var row in inventoryRows)
            {
                bool isCrystal = row.Container == ContainerId.Inventory && row.Slot == crystal.InventorySlot && row.ItemId == crystal.ItemId;
                bool placed = PlacedCount(slots, row.ItemId) > 0;
                var r = row with { Picked = placed };
                if (isCrystal) r = r with { Greyed = true, GreyedReason = $"{row.Name} is the crystal being used." };
                else if (row.Equipped) r = r with { Greyed = true, GreyedReason = $"{row.Name} is equipped." };
                rows.Add(r);
            }
            return rows;
        }

        /// <summary>
        /// Expands the grid into C2S 0x096's ingredient list (one entry per item, each with the inventory slot it comes from):
        /// each slot's items from its own stack first, then the item's other stacks in slot order; the crystal's own stack
        /// gives one item less. Returns null (with the item short) when the inventory does not hold enough, or more than
        /// eight items are asked for.
        /// </summary>
        public static (ushort ItemId, byte Slot)[]? Expand(InventoryState? inventory, StockUiSynthesisCrystal crystal, IReadOnlyList<StockUiSynthesisSlot?> slots, out ushort shortItem)
        {
            shortItem = 0;
            if (inventory == null) return null;
            var left = new Dictionary<byte, (ushort ItemId, uint Count)>();
            foreach (var item in inventory.SnapshotItems(ContainerId.Inventory))
            {
                if (item.Slot == 0 || item.ItemId == 0 || item.Count == 0) continue;
                uint count = item.Count;
                if (item.Slot == crystal.InventorySlot && item.ItemId == crystal.ItemId) count--;
                left[item.Slot] = (item.ItemId, count);
            }
            var result = new List<(ushort, byte)>();
            foreach (var entry in slots)
            {
                if (entry is not { } s) continue;
                uint need = s.Count;
                if (left.TryGetValue(s.InventorySlot, out var own) && own.ItemId == s.ItemId) need = Take(s.InventorySlot, need);
                var ordered = new List<byte>(left.Keys);
                ordered.Sort();
                foreach (byte slot in ordered)
                {
                    if (need == 0) break;
                    if (left[slot].ItemId == s.ItemId) need = Take(slot, need);
                }
                if (need > 0)
                {
                    shortItem = s.ItemId;
                    return null;
                }
            }
            if (result.Count is 0 or > MaxIngredients) return null;
            return result.ToArray();

            uint Take(byte slot, uint need)
            {
                var (id, count) = left[slot];
                uint take = Math.Min(count, need);
                for (uint i = 0; i < take; i++) result.Add((id, slot));
                left[slot] = (id, count - take);
                return need - take;
            }
        }

        /// <summary>
        /// A History recipe resolved against the inventory: its crystal (the recipe's own, else the element's plain crystal,
        /// else any crystal of the element held) and one grid slot per ingredient, from the item's largest stack.
        /// </summary>
        public static (StockUiSynthesisCrystal Crystal, StockUiSynthesisSlot?[] Slots) Resolve(InventoryState? inventory, SynthesisHistoryEntry recipe)
        {
            ArgumentNullException.ThrowIfNull(recipe);
            var items = inventory?.SnapshotItems(ContainerId.Inventory) ?? Array.Empty<InventoryItem>();
            byte FindSlot(Func<ushort, bool> match)
            {
                byte best = 0;
                uint bestCount = 0;
                foreach (var item in items)
                {
                    if (item.Slot == 0 || !match(item.ItemId) || item.Count <= bestCount) continue;
                    best = item.Slot;
                    bestCount = item.Count;
                }
                return best;
            }
            int element = ElementOf(recipe.CrystalItemId);
            ushort crystalId = recipe.CrystalItemId;
            byte crystalSlot = FindSlot(id => id == recipe.CrystalItemId);
            if (crystalSlot == 0 && element >= 0)
            {
                crystalSlot = FindSlot(id => ElementOf(id) == element);
                foreach (var item in items) if (item.Slot == crystalSlot && crystalSlot != 0) crystalId = item.ItemId;
            }
            var slots = new StockUiSynthesisSlot?[SlotCount];
            for (int i = 0; i < recipe.Ingredients.Count && i < SlotCount; i++)
            {
                var ingredient = recipe.Ingredients[i];
                slots[i] = new StockUiSynthesisSlot(ingredient.ItemId, ingredient.Count, FindSlot(id => id == ingredient.ItemId));
            }
            return (new StockUiSynthesisCrystal(crystalId, crystalSlot), slots);
        }

        /// <summary>
        /// The History list's rows: each recipe's result, its name and icon (no count); a recipe that has only broken (its result
        /// not known to the client) shows its first ingredient (PROVISIONAL: what retail shows for it is not captured).
        /// </summary>
        public static List<StockUiItemRow> HistoryRows(IReadOnlyList<SynthesisHistoryEntry> entries, Func<ushort, ItemRecord?>? lookup)
        {
            var rows = new List<StockUiItemRow>(entries.Count);
            foreach (var e in entries)
            {
                ushort shown = e.ResultItemId != 0 ? e.ResultItemId : e.Ingredients.Count > 0 ? e.Ingredients[0].ItemId : e.CrystalItemId;
                var record = StockUiShop.Lookup(lookup, shown);
                rows.Add(new StockUiItemRow(shown, record.Name, 1, ContainerId.Inventory, 0, 1));
            }
            return rows;
        }
    }
}
