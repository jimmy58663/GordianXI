// src/Gordian.Engine/Ui/StockUiMogHouse.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources.Tables;
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

        /// <summary><c>rmlo1</c>, <c>rmlo2</c>, <c>rmlo4</c>: which Mog Safe to take a furnishing from (<c>bankmenu</c>).</summary>
        Place,

        /// <summary><c>bankmenu</c> Mog Safe.</summary>
        PlaceFromSafe,

        /// <summary><c>bankmenu</c> Mog Safe 2.</summary>
        PlaceFromSafe2,

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
    /// <item><c>bankmenu</c> (384,48): Mog Safe (938), Mog Safe 2 (939), then the item list ("Mog Safe 21/80", "Select an item.",
    /// the retail video at 0:31). <c>roomlist</c> (366 x 88 at 16,208, help 937 "Select house style.") has the query's own frame and
    /// opens as a query window (Remodel passed round 1).</item>
    /// <item>In the Mog House the main menu's Map (button 12) reads Mog House (windowps #44, help 41) and opens <c>myroom</c>
    /// (the maintainer's image-29). While the house is open to visitors Open Mog reads Close Mog (windowps #608, its kind-1 shape)
    /// and Layout and Remodel are greyed (their kind-4 shapes).</item>
    /// </list>
    /// </summary>
    public static class StockUiMogHouse
    {
        public const string MoogleMenu = "myroom", AwayMoogleMenu = "mogext";
        public const string SafeMenu = "bankmenu";
        public const string LayoutFloorMenu = "rmlo1", LayoutPieceMenu = "rmlo2", ClearAllMenu = "rmlo3", LayoutMannequinMenu = "rmlo4";

        /// <summary><c>myroom</c>'s buttons that change while the house is open to visitors.</summary>
        public const int LayoutButton = 5, OpenHouseButton = 6, RemodelButton = 7;

        /// <summary>The main menu button that reads Mog House in the Mog House, its label image and help; Close Mog's label.</summary>
        public const int MainMenuButton = 12, MogHouseLabelImage = 44, MogHouseHelp = 41, CloseMogLabelImage = 608;

        public const string SafeTitle = "Mog Safe", Safe2Title = "Safe 2";

        /// <summary><c>bankmenu</c>'s Mog Safe 2 button; <c>mogcont</c>'s is 8.</summary>
        public const int SafeMenuSafe2Button = 2, ViewHouseSafe2Button = 8;

        /// <summary>
        /// The Quests that open Mog Safe 2 and the second floor: Growing Flowers (San d'Oria 58), A Lady's Heart (Bastok 50) and
        /// Flower Child (Windurst 65); returning home then plays a cutscene (LandSandBoat <c>moghouse.lua</c>; the maintainer, round 2).
        /// </summary>
        public static readonly IReadOnlyList<(QuestLogArea Area, int Quest)> Safe2Quests = new[]
        {
            (QuestLogArea.SandOria, 58), (QuestLogArea.Bastok, 50), (QuestLogArea.Windurst, 65),
        };

        /// <summary>The menus that stand on the log window, as the command menu does (round 2).</summary>
        public static bool StandsOnLog(string menuName) =>
            menuName is MoogleMenu or AwayMoogleMenu or LayoutFloorMenu or LayoutPieceMenu or ClearAllMenu or LayoutMannequinMenu;

        /// <summary>The Mannequin window (<see cref="StockUiOpenMenu.Mannequin"/>): the Check window's frame and grid, the DAT's <c>inspect</c>.</summary>
        public const string MannequinMenu = StockUiCheck.MenuName;

        /// <summary><c>inspect</c>'s button under the grid (View Wares there) reads Unequip All here (windowps #545).</summary>
        public const int UnequipAllButton = StockUiCheck.ViewWaresButton, UnequipAllImage = 545;

        /// <summary>The Storage list's place beside the grid (image-49: the frame's 16 + its 182 width).</summary>
        public const int MannequinListX = 198;

        /// <summary>"Change the equipment on your mannequin." (ROM/165/75 604), under "Layout" (image-49).</summary>
        public const int MannequinHelp = 604;
        public const string LayoutTitle = "Layout";

        public const string StorageFullNotice = "You must take items out of your Storage before removing that.";
        public const string LockedGearNotice = "Another mannequin is wearing that.";
        public const string OtherFloorNotice = "That is placed on the other floor.";
        public const string PlacedNotice = "That is placed in your Mog House.";

        /// <summary>The mannequin slot index of an equipment slot, or -1 for one a mannequin does not wear.</summary>
        public static int MannequinIndex(EquipSlotId slot)
        {
            for (int i = 0; i < MannequinSlots.Count; i++)
            {
                if (MannequinSlots[i].EquipBit == (int)slot) return i;
            }
            return -1;
        }

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
        public const string CellNotUsableNotice = "You cannot place furniture there.";
        public const string StyleQuestion = "Select house style.";

        public static StockUiMenuEntry Entry(string label, StockUiMogHouseAction action) =>
            new(label, Command: StockUiMenuCommand.MogHouseAction, Argument: (int)action);

        /// <summary>The menu Confirm opens: the mannequin's, a placed piece's, or (nothing placed) the empty room's.</summary>
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
    /// The Layout (#93), as the retail video <c>20261104-0517-47</c> shows it: the camera looks down on the real room, the cursor
    /// sits on one placed piece (<see cref="Selected"/>) and the directions jump to the next piece that way; Confirm opens that
    /// piece's menu. Rearrange or a piece from a Mog Safe puts it in hand (<see cref="Held"/>): the directions move it a cell (a wall
    /// piece from slot to slot), the menu button turns it, Confirm places it, and the grid and its footprint show until then.
    /// Read by the App every frame; written by the menus.
    /// </summary>
    public sealed class MogHouseLayoutState
    {
        private readonly object _sync = new();
        private List<FurniturePlacement> _pieces = new();

        public bool Active { get; internal set; }
        public bool SecondFloor { get; internal set; }

        /// <summary>A piece's footprint and rules from the client's table; set by the App.</summary>
        public Func<ushort, FurnitureInfo?>? InfoOf { get; set; }

        /// <summary>The Mog Safe and slot of the piece under the cursor, or null with nothing placed on this floor.</summary>
        public (ContainerId Container, byte Slot)? SelectedKey { get; internal set; }

        /// <summary>The piece in hand (Rearrange or a piece from a Mog Safe), or null.</summary>
        public FurniturePlacement? Held { get; internal set; }

        /// <summary>The held piece's cell (its footprint's corner nearest (0, 0)), or its wall slot in <see cref="HeldX"/>.</summary>
        public int HeldX { get; internal set; }
        public int HeldZ { get; internal set; }

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

        /// <summary>Records a placement the server was just asked for, so the piece does not jump back until S2C 0x020 confirms it.</summary>
        internal void UpdatePiece(FurniturePlacement piece)
        {
            lock (_sync)
            {
                var pieces = new List<FurniturePlacement>(_pieces);
                int index = pieces.FindIndex(p => p.Container == piece.Container && p.Slot == piece.Slot);
                if (index >= 0) pieces[index] = piece;
                _pieces = pieces;
            }
        }

        public FurnitureInfo InfoFor(ushort itemId) => InfoOf?.Invoke(itemId) ?? new FurnitureInfo(FurnitureFlags.Floor, 1, 1, 0);

        /// <summary>The held piece's rules.</summary>
        public FurnitureInfo? HeldInfo => Held is { } held ? InfoFor(held.ItemId) : null;

        /// <summary>
        /// Whether a piece can leave the room: LandSandBoat's 0x0FB refuses (and tells the client nothing) when the Storage, which
        /// is as big as the first floor's furniture makes it (at most 80), would be smaller than what it holds.
        /// </summary>
        public bool StorageAllowsRemoving(FurniturePlacement piece, int storageUsed, Func<ushort, int> storageOf)
        {
            int total = 0;
            foreach (var placed in Pieces)
            {
                if (placed.Installed && !placed.SecondFloor) total += storageOf(placed.ItemId);
            }
            return storageUsed <= Math.Min(MogHouseFurniture.MaxStorage, Math.Max(0, total - storageOf(piece.ItemId)));
        }

        /// <summary>Whether a piece stands on the floor shown.</summary>
        public bool IsShown(FurniturePlacement piece) => piece.Installed && piece.SecondFloor == SecondFloor;

        public bool IsHeld(FurniturePlacement piece) => Held is { } held && held.Container == piece.Container && held.Slot == piece.Slot;

        /// <summary>The piece under the cursor, or null.</summary>
        public FurniturePlacement? Selected
        {
            get
            {
                if (SelectedKey is not { } key) return null;
                foreach (var piece in Pieces)
                {
                    if (piece.Container == key.Container && piece.Slot == key.Slot && IsShown(piece)) return piece;
                }
                return null;
            }
        }

        /// <summary>The pieces not placed anywhere in one Mog Safe (what Layout offers), flowerpots left out on a second floor (LandSandBoat refuses them there).</summary>
        public List<FurniturePlacement> Unplaced(ContainerId container)
        {
            var list = new List<FurniturePlacement>();
            foreach (var piece in Pieces)
            {
                if (piece.Installed || piece.Container != container) continue;
                if (SecondFloor && (InfoFor(piece.ItemId).Flags & FurnitureFlags.Flowerpot) != 0) continue;
                list.Add(piece);
            }
            return list;
        }

        /// <summary>A placed piece's middle on the floor plan, display space (x west, z north), or null for a wall slot with no known place.</summary>
        public Vector3? CenterOf(FurniturePlacement piece) => TransformOf(piece)?.Translation;

        /// <summary>Where a placed piece's mesh goes, display space; null for a wall slot with no known place.</summary>
        public Matrix4x4? TransformOf(FurniturePlacement piece)
        {
            var info = InfoFor(piece.ItemId);
            return info.IsWall ? MogHouseFurniture.WallTransform(piece.X, piece.SecondFloor)
                : MogHouseFurniture.FloorTransform(info, piece.X, piece.Z, piece.Level, piece.Rotation, piece.SecondFloor);
        }

        /// <summary>Where the held piece's mesh goes, display space.</summary>
        public Matrix4x4? HeldTransform()
        {
            if (HeldInfo is not { } info) return null;
            return info.IsWall ? MogHouseFurniture.WallTransform(HeldX, SecondFloor)
                : MogHouseFurniture.FloorTransform(info, HeldX, HeldZ, HeldLevel(), Rotation, SecondFloor);
        }

        /// <summary>Moves the cursor to the next placed piece in a direction (x east, z north), nearest first; stays when none lies that way.</summary>
        internal bool MoveSelection(int dx, int dz)
        {
            if (Selected is not { } current || CenterOf(current) is not { } from)
            {
                SelectFirst();
                return SelectedKey != null;
            }
            FurniturePlacement? best = null;
            float bestScore = float.MaxValue;
            foreach (var piece in Pieces)
            {
                if (!IsShown(piece) || piece == current || CenterOf(piece) is not { } to) continue;
                // Display x runs west: east is -x.
                float east = from.X - to.X, north = to.Z - from.Z;
                float along = east * dx + north * dz, across = MathF.Abs(east * dz) + MathF.Abs(north * dx);
                if (along <= 0.01f || across > along * 3) continue;
                float score = along + across * 2;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = piece;
                }
            }
            if (best is not { } next) return false;
            SelectedKey = (next.Container, next.Slot);
            return true;
        }

        /// <summary>The first placed piece on this floor under the cursor; none when the floor is empty.</summary>
        internal void SelectFirst()
        {
            SelectedKey = null;
            foreach (var piece in Pieces)
            {
                if (!IsShown(piece)) continue;
                SelectedKey = (piece.Container, piece.Slot);
                return;
            }
        }

        /// <summary>Takes a piece in hand at a cell (or wall slot).</summary>
        internal void Hold(FurniturePlacement piece, int x, int z, byte rotation)
        {
            Held = piece;
            HeldX = x;
            HeldZ = z;
            Rotation = rotation;
            ClampHeld();
        }

        /// <summary>The held piece a cell over, its footprint staying on the grid; a wall piece to the next slot.</summary>
        internal void MoveHeld(int dx, int dz)
        {
            if (HeldInfo is not { } info) return;
            if (info.IsWall)
            {
                int slots = SecondFloor ? MogHouseFurniture.SecondFloorWallSlots : MogHouseFurniture.FirstFloorWallSlots;
                HeldX = (HeldX + (dx + dz > 0 ? 1 : slots - 1)) % slots;
                return;
            }
            HeldX += dx;
            HeldZ += dz;
            ClampHeld();
        }

        internal void TurnHeld()
        {
            if (HeldInfo is not { IsWall: false }) return;
            Rotation = (byte)((Rotation + 1) & 3);
            ClampHeld();
        }

        private void ClampHeld()
        {
            if (HeldInfo is not { } info || info.IsWall) return;
            var (w, d) = MogHouseFurniture.Footprint(info, Rotation);
            HeldX = Math.Clamp(HeldX, 0, Math.Max(0, MogHouseFurniture.GridWidth - w));
            HeldZ = Math.Clamp(HeldZ, 0, Math.Max(0, MogHouseFurniture.Rows(SecondFloor) - d));
        }

        /// <summary>The Surface piece under a cell, or null (LandSandBoat: the anchor cell must lie in the parent's footprint).</summary>
        private FurniturePlacement? SurfaceAt(int x, int z)
        {
            foreach (var piece in Pieces)
            {
                if (!IsShown(piece) || IsHeld(piece)) continue;
                var info = InfoFor(piece.ItemId);
                if (!info.IsSurface) continue;
                var (w, d) = MogHouseFurniture.Footprint(info, piece.Rotation);
                if (x >= piece.X && x < piece.X + w && z >= piece.Z && z < piece.Z + d) return piece;
            }
            return null;
        }

        /// <summary>The stacking level the held piece would stand at: a table's height under a piece that goes on tables, else the floor.</summary>
        public int HeldLevel() =>
            HeldInfo is { GoesOnTable: true } && SurfaceAt(HeldX, HeldZ) is { } surface ? InfoFor(surface.ItemId).StackLevel : 0;

        /// <summary>
        /// Whether the held piece can go where it is: on usable floor and clear of other floor pieces (a piece on a table only needs
        /// the table), or in a free wall slot. These are LandSandBoat's checks, so a piece that passes is not refused.
        /// </summary>
        public bool CanPlaceHeld()
        {
            if (HeldInfo is not { } info) return false;
            if (info.IsWall)
            {
                foreach (var piece in Pieces)
                {
                    if (IsShown(piece) && !IsHeld(piece) && InfoFor(piece.ItemId).IsWall && piece.X == HeldX) return false;
                }
                return true;
            }
            var (w, d) = MogHouseFurniture.Footprint(info, Rotation);
            if (!MogHouseFurniture.FootprintFits(HeldX, HeldZ, w, d, SecondFloor)) return false;
            if (HeldLevel() != 0) return true;
            foreach (var piece in Pieces)
            {
                if (!IsShown(piece) || IsHeld(piece) || piece.Level != 0) continue;
                var other = InfoFor(piece.ItemId);
                if (other.IsWall) continue;
                var (ow, od) = MogHouseFurniture.Footprint(other, piece.Rotation);
                if (HeldX < piece.X + ow && piece.X < HeldX + w && HeldZ < piece.Z + od && piece.Z < HeldZ + d) return false;
            }
            return true;
        }

        /// <summary>The pieces standing on a Surface piece, which move with it.</summary>
        internal List<FurniturePlacement> StackedOn(FurniturePlacement parent)
        {
            var list = new List<FurniturePlacement>();
            var info = InfoFor(parent.ItemId);
            if (!info.IsSurface) return list;
            var (w, d) = MogHouseFurniture.Footprint(info, parent.Rotation);
            foreach (var piece in Pieces)
            {
                if (IsShown(piece) && piece.Level > 0 && piece.X >= parent.X && piece.X < parent.X + w && piece.Z >= parent.Z && piece.Z < parent.Z + d) list.Add(piece);
            }
            return list;
        }
    }

    /// <summary>
    /// The Layout's camera (#93): looks down on the room from above a focus point on the floor, north at the top. It eases in from
    /// the player's camera and back out, and only pans when the piece under the cursor leaves a box around the focus, as the video
    /// shows (a piece 3.8 yalms off the middle does not move it). 85 px per yalm at 1600 x 900 is 9.2 yalms up at the client's 60
    /// degree field of view; the grid's lines lean 3.3 degrees, so the eye sits a little north of the point it looks at.
    /// All values fitted to that video, <b>provisional</b>. Positions are display space.
    /// </summary>
    public sealed class MogHouseLayoutCamera
    {
        public const float Height = 9.16f, EyeNorth = 0.53f, BoxX = 4.0f, BoxZ = 2.0f, BlendSeconds = 0.6f, PanRate = 6.0f;

        /// <summary>The Mannequin window's view (image-49, <b>provisional</b>): from the room side, this far away and this high, at the mannequin's chest.</summary>
        public const float MannequinDistance = 3.0f, MannequinEyeHeight = 1.3f, MannequinLookHeight = 1.1f;

        private Vector3? _mannequin;
        private float _mannequinBlend;

        private Vector3 _focus, _goal;
        private float _blend;
        private bool _hasFocus;

        /// <summary>Whether the view is any part layout (the player's model is hidden, the camera is overridden).</summary>
        public bool InUse => _blend > 0f;

        /// <summary>Advances the view toward the piece under the cursor or in hand.</summary>
        public void Update(MogHouseLayoutState layout, float seconds, Vector3? mannequin = null)
        {
            float step = seconds / BlendSeconds;
            _blend = Math.Clamp(_blend + (layout.Active ? step : -step), 0f, 1f);
            if (mannequin != null) _mannequin = mannequin;
            _mannequinBlend = Math.Clamp(_mannequinBlend + (mannequin != null ? step : -step), 0f, 1f);
            if (!layout.Active)
            {
                if (_blend == 0f) _hasFocus = false;
                return;
            }
            Vector3? piece = layout.HeldTransform()?.Translation ?? (layout.Selected is { } selected ? layout.CenterOf(selected) : null);
            if (!_hasFocus)
            {
                _hasFocus = true;
                _goal = new Vector3(piece?.X ?? 0f, 0f, piece?.Z ?? 0f);
                _focus = _goal;
            }
            if (piece is { } p)
            {
                _goal.X = Math.Clamp(_goal.X, p.X - BoxX, p.X + BoxX);
                _goal.Z = Math.Clamp(_goal.Z, p.Z - BoxZ, p.Z + BoxZ);
            }
            _focus += (_goal - _focus) * (1f - MathF.Exp(-PanRate * seconds));
        }

        /// <summary>The view this frame, blended from the player's camera (<paramref name="eye"/>, <paramref name="target"/>).</summary>
        public (Vector3 Eye, Vector3 Target, Vector3 Up) View(Vector3 eye, Vector3 target)
        {
            float t = _blend * _blend * (3f - 2f * _blend);
            var topEye = _focus + new Vector3(0f, Height, EyeNorth);
            var view = (Eye: Vector3.Lerp(eye, topEye, t), Target: Vector3.Lerp(target, _focus, t), Up: Vector3.Normalize(Vector3.Lerp(Vector3.UnitY, Vector3.UnitZ, MathF.Max(t, 0.05f))));
            if (_mannequinBlend <= 0f || _mannequin is not { } at) return view;
            float m = _mannequinBlend * _mannequinBlend * (3f - 2f * _mannequinBlend);
            // From the room's middle side, so the eye stays inside the walls.
            var away = new Vector3(-at.X, 0f, -at.Z);
            away = away.LengthSquared() > 0.01f ? Vector3.Normalize(away) : Vector3.UnitZ;
            var close = at + away * MannequinDistance + new Vector3(0f, MannequinEyeHeight, 0f);
            var look = at + new Vector3(0f, MannequinLookHeight, 0f);
            return (Vector3.Lerp(view.Eye, close, m), Vector3.Lerp(view.Target, look, m), Vector3.Normalize(Vector3.Lerp(view.Up, Vector3.UnitY, m)));
        }
    }
}
