// src/Gordian.Core/Ui/StockUiEquipSets.cs
using System;
using System.Collections.Generic;
using Gordian.Core.Network.Packets;
using Gordian.Core.World;

namespace Gordian.Core.Ui
{
    /// <summary>
    /// The Equipment Sets windows (#92), as the English menu DAT (ROM/119/51) authors them; read 2026-10-12 with the menu
    /// dump (<c>GORDIAN_UI_DUMP_MENUS=mcr*</c>) and the title (ROM/165/76) and help (ROM/165/75) tables:
    /// <list type="bullet">
    /// <item>Main menu page 2 > Macros opens <c>mcrmenu</c> (384,48 top right, "Macro"): Edit Macro (help 829) and Edit
    /// Equip. Set (help 830, "Edit equipment sets.").</item>
    /// <item><c>mcres20</c> (384,48, 112 x 330, title band "Equipment"): twenty blank capsules (<c>keytops3</c> #86) at a 16 px
    /// pitch, the set names drawn by the client, page arrows (buttons 21 / 22) for the next twenty.</item>
    /// <item><c>mcresmn</c> ("Equipment Sets", title 172; authored at 0,0, so the client places it): Edit (831), Change Name
    /// (832), Copy (833), Paste (834, with a greyed look), Delete (835), Use Current (836), Equip (837), Style Lock (921, with
    /// a greyed look).</item>
    /// <item><c>mcresed</c> (16,48, 182 x 190, title 172, cursor <c>anc_equi</c>): the 4 x 4 slot grid in the check window's
    /// order (help 243-258).</item>
    /// </list>
    /// PROVISIONAL (no retail capture): where <c>mcresmn</c> goes (left of the list, level with the chosen set), that the set
    /// grid shows beside it, and the item list's place (right of the grid).
    /// </summary>
    public static class StockUiEquipSets
    {
        public const string MacroMenu = "mcrmenu", ListMenu = "mcres20", OptionsMenu = "mcresmn", GridMenu = "mcresed";

        /// <summary><c>mcrmenu</c>'s buttons.</summary>
        public const int EditMacroButton = 1, EditEquipSetButton = 2;

        /// <summary><c>mcresmn</c>'s buttons, top to bottom.</summary>
        public const int EditButton = 1, ChangeNameButton = 2, CopyButton = 3, PasteButton = 4, DeleteButton = 5,
            UseCurrentButton = 6, EquipButton = 7, StyleLockButton = 8;

        /// <summary>The rows <c>mcres20</c> shows at once (its buttons 1-20).</summary>
        public const int VisibleRows = EquipmentSetBook.SetsPerPage;

        /// <summary>The options window's place: left of the list (384 - 112), its top level with the chosen row (PROVISIONAL).</summary>
        public const int OptionsX = 272;

        /// <summary>The slot's item list beside the grid (16 + 182 + 2; PROVISIONAL).</summary>
        public const int SlotListX = 200;

        /// <summary>The set names start this far into a capsule (PROVISIONAL).</summary>
        public const float NameInset = 8;

        /// <summary>The Delete prompt.</summary>
        public static string DeletePrompt(int number, EquipmentSet set) => $"Delete equipment set {number} ({set.Name})?";

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
