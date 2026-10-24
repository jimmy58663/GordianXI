// src/Gordian.Engine/Ui/StockUiEquipSets.cs
using System;
using System.Collections.Generic;
using Gordian.Core.Network.Packets;
using Gordian.Core.World;

namespace Gordian.Engine.Ui
{
    /// <summary>What a set slot list's extra rows do (<see cref="StockUiItemRow.SetAction"/>).</summary>
    public enum StockUiSetRowAction : byte
    {
        None = 0,

        /// <summary>"Remove piece." (ROM/165/72 #275): the slot takes off whatever it holds when the set is used (red X).</summary>
        RemovePiece,

        /// <summary>"Keep same piece." (#276): the slot is left empty, so using the set leaves it as it is.</summary>
        KeepSamePiece,
    }

    /// <summary>
    /// The Equipment Sets windows (#92), as the English menu DAT (ROM/119/51) authors them (read with the menu dump and the
    /// title ROM/165/76 and help ROM/165/75 tables) and the maintainer's retail captures 2026-10-13 (image-11 ... image-14,
    /// UI 1:1) lay them out:
    /// <list type="bullet">
    /// <item>Main menu page 2 > Macros opens <c>mcrmenu</c> (384,48 top right, "Macro"): Edit Macro (help 829) and Edit
    /// Equip. Set (help 830, "Edit equipment sets.").</item>
    /// <item><c>mcres20</c> (384,48, 112 x 330, title band "Equipment"): twenty blank capsules (<c>keytops3</c> #86) at a 16 px
    /// pitch with the set names over them, all white (image-11: empty sets are not greyed), page arrows (buttons 21 / 22);
    /// right on the last page wraps to the first and left on the first to the last (round 1 note).</item>
    /// <item><c>mcresmn</c> ("Options", authored at 0,0): Edit (831), Change Name (832), Copy (833), Paste (834), Delete
    /// (835), Use Current (836), Equip (837), Style Lock (921); image-11 shows it right against the list's left edge, its top
    /// level with the list's.</item>
    /// <item>Edit (image-12, -13): the help bar "Equipment Sets" with the slot's help, <c>mcresed</c> (16,48, the check window's
    /// 4 x 4 slot order) with the set's items, "Remove piece" slots under a red X (<c>keytops3</c> #429, the <c>marker</c>
    /// texture's X stretched over the 32 x 32 slot), the storage's equipment right of it at x 198 (Storage Options, then
    /// "Remove piece." and "Keep same piece." from ROM/165/72 #275 / #276, then the items), and the item info window under
    /// the grid (16,240) for the slot's item.</item>
    /// <item>Storage Options (image-14) opens <c>mcrmogc</c> (384,48 top right, title 169 "Storage Options", help 802 "Select
    /// a form of storage."): Inventory, Mog Safe, Mog Safe 2, Storage, Mog Locker, Mog Satchel, Mog Sack, Mog Case, Mog
    /// Wardrobe 1-8.</item>
    /// </list>
    /// </summary>
    public static class StockUiEquipSets
    {
        public const string MacroMenu = "mcrmenu", ListMenu = "mcres20", OptionsMenu = "mcresmn", GridMenu = "mcresed", StorageMenu = "mcrmogc";

        /// <summary>The help bar's title while a set is edited (ROM/165/76 #172).</summary>
        public const string Title = "Equipment Sets";

        /// <summary><c>mcrmenu</c>'s buttons.</summary>
        public const int EditMacroButton = 1, EditEquipSetButton = 2;

        /// <summary><c>mcresmn</c>'s buttons, top to bottom.</summary>
        public const int EditButton = 1, ChangeNameButton = 2, CopyButton = 3, PasteButton = 4, DeleteButton = 5,
            UseCurrentButton = 6, EquipButton = 7, StyleLockButton = 8;

        /// <summary><c>mcres20</c>'s page arrows (left of and right of its title band).</summary>
        public const int PreviousPageButton = 21, NextPageButton = 22;

        /// <summary>The rows <c>mcres20</c> shows at once (its buttons 1-20).</summary>
        public const int VisibleRows = EquipmentSetBook.SetsPerPage;

        /// <summary>The options window's place: right against the list's left edge (384 - 112, anchored top right as the list is), its top level with the chosen row (image-11).</summary>
        public const int OptionsX = 272;

        /// <summary>The storage's equipment right of the grid (16 + 182; image-12).</summary>
        public const int SlotListX = 198;

        /// <summary>
        /// The set names (maintainer retail capture 2026-10-13, image-16): 6 px into a capsule, upright and white in the log font
        /// at 0.83 of its size ("Set002" 43 px wide), cut where the 70 px text area ends ("BehavRese"; round 2 found 0.67
        /// too small). While Change Name types one, the field draws it at the log font's full size (9 px a letter), 76 px wide,
        /// scrolled left so the end of the text and the caret stay in view.
        /// </summary>
        public const float NameInset = 6, NameScale = 0.83f, NameFieldWidth = 70, EditScale = 1f, EditFieldWidth = 76, CaretWidth = 3;

        /// <summary>The menu message table's (ROM/165/72) set texts.</summary>
        public const int RemovePieceText = 275, KeepSamePieceText = 276, PastePromptText = 277, DeletePromptText = 278, UseCurrentPromptText = 279;

        /// <summary>The texts when the table is not read (tests): the same words.</summary>
        public static string FallbackText(int id) => id switch
        {
            RemovePieceText => "Remove piece.",
            KeepSamePieceText => "Keep same piece.",
            PastePromptText => "Performing this action will overwrite the current set with a new one. Proceed?",
            DeletePromptText => "Truly delete this set?",
            UseCurrentPromptText => "Proceed with overwriting this set with your current equipment?",
            _ => string.Empty,
        };

        /// <summary><c>mcrmogc</c>'s buttons by container (the buttons in their authored top-to-bottom order, image-14).</summary>
        private static readonly (int Button, ContainerId Container)[] StorageButtons =
        {
            (1, ContainerId.Inventory), (2, ContainerId.MogSafe), (9, ContainerId.MogSafe2), (3, ContainerId.Storage), (4, ContainerId.MogLocker),
            (5, ContainerId.MogSatchel), (6, ContainerId.MogSack), (7, ContainerId.MogCase), (8, ContainerId.Wardrobe), (10, ContainerId.Wardrobe2),
            (11, ContainerId.Wardrobe3), (12, ContainerId.Wardrobe4), (13, ContainerId.Wardrobe5), (14, ContainerId.Wardrobe6),
            (15, ContainerId.Wardrobe7), (16, ContainerId.Wardrobe8),
        };

        public static bool TryGetStorage(int buttonId, out ContainerId container)
        {
            foreach (var (button, c) in StorageButtons)
            {
                if (button == buttonId)
                {
                    container = c;
                    return true;
                }
            }
            container = default;
            return false;
        }

        public static int StorageButtonOf(ContainerId container)
        {
            foreach (var (button, c) in StorageButtons)
            {
                if (c == container) return button;
            }
            return 0;
        }

        /// <summary>
        /// A set's item list: the Equipment list's rows (Storage Options, then the storage's equipment, for one slot or all), with
        /// "Remove piece." and "Keep same piece." after Storage Options, and nothing greyed or marked worn (image-12: every name
        /// white; a set may hold what the job cannot wear).
        /// </summary>
        public static List<StockUiItemRow> SetRows(InventoryState? inventory, ContainerId container, EquipSlotId? slot, Func<ushort, Gordian.Core.Resources.Models.ItemRecord?>? lookup,
            Func<int, string?>? text = null)
        {
            var rows = StockUiItems.EquipRows(inventory, container, slot, lookup, default);
            for (int i = 0; i < rows.Count; i++)
            {
                if (!rows[i].IsStorageOptions) rows[i] = rows[i] with { Greyed = false, GreyedReason = null, Equipped = false };
            }
            string Text(int id) => text?.Invoke(id) is { Length: > 0 } t ? t : FallbackText(id);
            rows.Insert(1, new StockUiItemRow(0, Text(RemovePieceText), 0, container, 0, SetAction: StockUiSetRowAction.RemovePiece));
            rows.Insert(2, new StockUiItemRow(0, Text(KeepSamePieceText), 0, container, 0, SetAction: StockUiSetRowAction.KeepSamePiece));
            return rows;
        }
        /// <summary>The Use Current set: every slot's worn item (where it is now), empty slots empty.</summary>
        public static EquipmentSet FromWorn(EquipmentSet set, InventoryState? inventory)
        {
            var pieces = new EquipmentSetPiece[EquipmentSet.SlotCount];
            for (int i = 0; i < EquipmentSet.SlotCount; i++)
            {
                var worn = StockUiItems.EquippedItem(inventory, (EquipSlotId)i);
                pieces[i] = worn != null ? EquipmentSetPiece.Item(worn.Container, worn.Slot, worn.ItemId) : EquipmentSetPiece.Empty;
            }
            return set.WithPieces(pieces);
        }

        /// <summary>
        /// The set after the server's check (S2C 0x116): LandSandBoat fills <c>Items[0..15]</c> by equipment slot and leaves
        /// <c>Items[16]</c> empty (<c>0x116_equipset_valid.cpp</c>), while XiPackets (world/server/0x0116) describes
        /// <c>Items[0]</c> as the changed piece and <c>Items[1..16]</c> as the slots. GordianXI follows LandSandBoat.
        /// Each slot: the item when <c>HasItemFlg</c> is set, "Remove piece" when <c>RemoveItemFlg</c> is, else empty.
        /// </summary>
        public static EquipmentSet ApplyValidation(EquipmentSet set, IReadOnlyList<EquipsetSlotEntry> entries)
        {
            var pieces = new EquipmentSetPiece[EquipmentSet.SlotCount];
            for (int i = 0; i < EquipmentSet.SlotCount; i++)
            {
                if (i >= entries.Count) break;
                var e = entries[i];
                pieces[i] = EquipmentSetPiece.FromRequestItem(e.HasItem, e.RemoveItem, e.Container, e.ItemIndex, e.ItemId);
            }
            return set.WithPieces(pieces);
        }

        /// <summary>The C2S 0x052 entries for a set whose <paramref name="slot"/> changes: the other slots as they are, the changed one zero (XiPackets).</summary>
        public static EquipsetRequestItem[] CheckEntries(EquipmentSet set, EquipSlotId slot)
        {
            var entries = new EquipsetRequestItem[EquipmentSet.SlotCount];
            for (int i = 0; i < EquipmentSet.SlotCount; i++)
            {
                entries[i] = i == (int)slot ? default : set.Pieces[i].ToRequestItem();
            }
            return entries;
        }
    }
}
