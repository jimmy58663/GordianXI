// src/Gordian.Engine/Ui/StockUiMogHouse.cs
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Gordian.Core.Network.Packets;
using Gordian.Core.World;

namespace Gordian.Engine.Ui
{
    /// <summary>What a Mog House menu entry does (#93); the Argument of <see cref="StockUiMenuCommand.MogHouseAction"/>.</summary>
    public enum StockUiMogHouseAction
    {
        /// <summary><c>myroom</c> Mog Safe: the storage list (View House).</summary>
        Storages,
        Delivery,
        ChangeJobs,
        Gardening,

        /// <summary><c>myroom</c> Layout: the furniture grid.</summary>
        Layout,

        /// <summary><c>myroom</c> Open Mog House: opens it to the party and alliance, or closes it (C2S 0x0CB).</summary>
        OpenHouse,

        /// <summary><c>myroom</c> Remodel: the house style list (C2S 0x0CB Remodel).</summary>
        Remodel,

        /// <summary><c>rmlo1</c>, <c>rmlo2</c>, <c>rmlo4</c>: pick a furnishing from the Mog Safes to place.</summary>
        Place,

        /// <summary><c>rmlo1</c>, <c>rmlo2</c>, <c>rmlo4</c>: asks to clear every piece on this floor (<c>rmlo3</c>).</summary>
        ClearAll,

        /// <summary><c>rmlo1</c>: leaves the layout (C2S 0x0FA with every field 0).</summary>
        QuitLayout,

        /// <summary><c>rmlo2</c>, <c>rmlo4</c>: picks up the piece under the cursor to move it.</summary>
        Move,

        /// <summary><c>rmlo2</c>, <c>rmlo4</c>: clears the piece under the cursor away (C2S 0x0FB).</summary>
        ClearAway,

        /// <summary><c>rmlo3</c>: clears every piece on this floor away.</summary>
        ClearAllConfirm,

        /// <summary><c>rmlo3</c>: back to the grid.</summary>
        Return,

        /// <summary><c>rmlo4</c>: the mannequin's equipment (C2S 0x03B).</summary>
        Mannequin,
    }

    /// <summary>
    /// The Mog House menus (#93), from the English menu DAT (39542, ROM/119/51; read 2026-11-02) and the maintainer's
    /// retail capture <c>Mog_House.mp4</c> (2026-10-16, the help texts only):
    /// <list type="bullet">
    /// <item><c>myroom</c> (112 x 120 at 16,208), the moogle's menu in the character's own Mog House, opened by S2C 0x02E:
    /// Storage (button 1, help 1066), Delivery Box (2, 350), Change Jobs (3, 43), Gardening (4, 356), Layout (5, 357), Open Mog
    /// (6, 666), Remodel (7, 936). <c>mogext</c> (16,272) is the same menu's first three away from it (a Nomad Moogle).</item>
    /// <item><c>rmlo1</c> (16,240), Confirm on empty floor: Layout (help 409 "Place a piece of furniture..."), Remove All (410),
    /// Back (411 "Quit rearranging the furniture."). <c>rmlo2</c> (16,224), on a placed piece: Rearrange (412), Remove (413),
    /// Layout (409), Remove All (414). <c>rmlo4</c> (16,208), on a mannequin: Equip (604), then <c>rmlo2</c>'s four. <c>rmlo3</c>
    /// (16,256, anchored top left unlike the others): Remove All (415), Exit (371 "Return."). The labels are the DAT's sprites
    /// (offscreen render, 2026-11-02); which of these retail opens on which cell is inferred from the help texts.</item>
    /// <item><c>roomlist</c> (366 x 88 at 16,208, help 937 "Select house style.") and <c>furnitur</c> (182 x 190 at 16,48, ten
    /// 132 px rows, help 3 "Select an item.") are list windows; until their retail look is captured both lists open as a
    /// query window (<c>roomlist</c> has the query's own frame), <b>provisional</b>.</item>
    /// </list>
    /// </summary>
    public static class StockUiMogHouse
    {
        public const string MoogleMenu = "myroom", AwayMoogleMenu = "mogext";
        public const string LayoutFloorMenu = "rmlo1", LayoutPieceMenu = "rmlo2", ClearAllMenu = "rmlo3", LayoutMannequinMenu = "rmlo4";

        /// <summary><c>myroom</c>'s Open Mog House button: it reads "Close" while the house is open (<b>provisional</b>).</summary>
        public const int OpenHouseButton = 6;

        /// <summary>The house styles of C2S 0x0CB Remodel's <c>Param2</c> (LandSandBoat <c>GP_CLI_COMMAND_MYROOM_IS_PARAM2</c>).</summary>
        public static readonly IReadOnlyList<(ushort Style, string Name)> HouseStyles = new (ushort, string)[]
        {
            (615, "San d'Orian style"), (616, "Bastokan style"), (617, "Windurstian style"), (618, "Mog Patio"),
        };

        /// <summary>The mannequin's slots in C2S 0x03B's order, with the equipment slot bit (item DAT <c>EquipSlotsMask</c>) each takes.</summary>
        public static readonly IReadOnlyList<(SubcontainerSlotIndex Slot, string Name, int EquipBit)> MannequinSlots = new (SubcontainerSlotIndex, string, int)[]
        {
            (SubcontainerSlotIndex.MainWeapon, "Main", 0), (SubcontainerSlotIndex.SubWeapon, "Sub", 1), (SubcontainerSlotIndex.RangedWeapon, "Ranged", 2),
            (SubcontainerSlotIndex.Head, "Head", 4), (SubcontainerSlotIndex.Body, "Body", 5), (SubcontainerSlotIndex.Hands, "Hands", 6),
            (SubcontainerSlotIndex.Legs, "Legs", 7), (SubcontainerSlotIndex.Feet, "Feet", 8),
        };

        // GordianXI's wording where retail's is not captured yet (provisional).
        public const string OpenHouseQuestion = "Open your Mog House to party and alliance members?";
        public const string CloseHouseQuestion = "Close your Mog House to visitors?";
        public const string HouseOpenedNotice = "Your Mog House is now open to party and alliance members.";
        public const string HouseClosedNotice = "Your Mog House is now closed to visitors.";
        public const string LayoutWhileOpenNotice = "You cannot rearrange furniture while your Mog House is open to visitors.";
        public const string NoFurnitureNotice = "You have no furniture to place.";
        public const string CellNotUsableNotice = "You cannot place furniture there.";
        public const string ClearAllQuestion = "Clear away all furnishings from this floor?";
        public const string StyleQuestion = "Select house style.";
        public const string FurnitureQuestion = "Select furniture to place in your room.";
        public const string MannequinQuestion = "Change the equipment on your mannequin.";
        public const string RemoveAllRow = "Remove all.";
        public const string RemoveRow = "Remove.";
        public const string EmptySlotText = "-";

        public static StockUiMenuEntry Entry(string label, StockUiMogHouseAction action) =>
            new(label, Command: StockUiMenuCommand.MogHouseAction, Argument: (int)action);

        /// <summary>The layout menu for Confirm on a cell: the mannequin's, a placed piece's, or empty floor's.</summary>
        public static string LayoutMenuFor(FurniturePlacement? piece) =>
            piece is not { } p ? LayoutFloorMenu : MogHouseFurniture.IsMannequin(p.ItemId) ? LayoutMannequinMenu : LayoutPieceMenu;
    }

    /// <summary>The network side of the Mog House menus (#93), implemented by the session over Core's packet modules.</summary>
    public interface IStockUiMogHouse
    {
        /// <summary>Whether the room shown is a second floor (the furniture there is the second floor's).</summary>
        bool OnSecondFloor { get; }

        /// <summary>C2S 0x0FA: places (or moves) a furnishing at a grid cell.</summary>
        Task PlaceAsync(FurniturePlacement piece, bool secondFloor, byte x, byte level, byte z, byte rotation);

        /// <summary>C2S 0x0FA with every field 0: the layout is finished (the server then updates the Moghancement).</summary>
        Task FinishLayoutAsync();

        /// <summary>C2S 0x0FB: clears a placed furnishing away.</summary>
        Task ClearAwayAsync(FurniturePlacement piece);

        /// <summary>C2S 0x0CB Open or Close.</summary>
        Task SetHouseOpenAsync(bool open);

        /// <summary>C2S 0x0CB Remodel with the house style.</summary>
        Task RemodelAsync(ushort style);

        /// <summary>C2S 0x03B: equips (or with <paramref name="kind"/> Unequip / UnequipAll removes) a Storage item on a mannequin.</summary>
        Task MannequinAsync(SubcontainerKind kind, FurniturePlacement mannequin, SubcontainerSlotIndex slot, byte storageSlot);
    }

    /// <summary>
    /// The layout grid while the Layout menu is open (#93): the floor, the cursor cell, the piece being placed or moved
    /// and the furnishings of the Mog Safes. Retail switches the camera to a top-down view of the room; GordianXI does
    /// not draw furniture in the room yet, so the App draws the grid seen from above with the pieces on it (each at its
    /// anchor cell: the footprints are not known to the client). Read by the App every frame; written by the menus.
    /// </summary>
    public sealed class MogHouseLayoutState
    {
        private readonly object _sync = new();
        private List<FurniturePlacement> _pieces = new();

        public bool Active { get; internal set; }
        public bool SecondFloor { get; internal set; }
        public int CursorX { get; internal set; }
        public int CursorZ { get; internal set; }

        /// <summary>The piece following the cursor (Place or Move), or null.</summary>
        public FurniturePlacement? Held { get; internal set; }

        /// <summary>The held piece's rotation (0-3, quarter turns).</summary>
        public byte Rotation { get; internal set; }

        /// <summary>Every furnishing in the Mog Safes, placed or not.</summary>
        public IReadOnlyList<FurniturePlacement> Pieces
        {
            get { lock (_sync) return _pieces; }
        }

        internal void SetPieces(List<FurniturePlacement> pieces)
        {
            lock (_sync) _pieces = pieces;
        }

        /// <summary>Whether a piece stands on the floor shown.</summary>
        public bool IsShown(FurniturePlacement piece) => piece.Installed && piece.SecondFloor == SecondFloor;

        /// <summary>The placed piece anchored at a cell of the floor shown (the highest stacked one first), or null.</summary>
        public FurniturePlacement? PieceAt(int x, int z)
        {
            FurniturePlacement? found = null;
            foreach (var piece in Pieces)
            {
                if (!IsShown(piece) || piece.X != x || piece.Z != z) continue;
                if (Held is { } held && held.Container == piece.Container && held.Slot == piece.Slot) continue;
                if (found == null || piece.Level > found.Value.Level) found = piece;
            }
            return found;
        }

        /// <summary>The pieces not placed anywhere (the ones Place offers), flowerpots left out on a second floor (LandSandBoat refuses them there).</summary>
        public List<FurniturePlacement> Unplaced(Func<ushort, bool> isFlowerpot)
        {
            var list = new List<FurniturePlacement>();
            foreach (var piece in Pieces)
            {
                if (piece.Installed) continue;
                if (SecondFloor && isFlowerpot(piece.ItemId)) continue;
                list.Add(piece);
            }
            return list;
        }

        /// <summary>Moves the cursor one cell, staying on the grid.</summary>
        internal void MoveCursor(int dx, int dz)
        {
            CursorX = Math.Clamp(CursorX + dx, 0, MogHouseFurniture.GridWidth - 1);
            CursorZ = Math.Clamp(CursorZ + dz, 0, MogHouseFurniture.Rows(SecondFloor) - 1);
        }
    }
}
