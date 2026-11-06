// src/Gordian.Core/World/MogHouseFurniture.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources.Models;
using Gordian.Core.Resources.Tables;

namespace Gordian.Core.World
{
    /// <summary>
    /// One furnishing held in a Mog Safe and where it stands in the room, read from its item's extra data (S2C 0x020
    /// <c>ExtData</c>): byte 1 bit 0 the second floor, bit 6 placed; +6 the grid column (C2S 0x0FA <c>x</c>), +7 the
    /// stacking level (<c>y</c>), +8 the grid row (<c>z</c>), +9 the rotation (<c>v</c>, 0-3).
    /// Extra data layout referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/items/exdata/furniture.h).
    /// </summary>
    public readonly record struct FurniturePlacement(
        ContainerId Container,
        byte Slot,
        ushort ItemId,
        bool Installed,
        bool SecondFloor,
        byte X,
        byte Level,
        byte Z,
        byte Rotation)
    {
        public const int MinExtDataLength = 10;

        /// <summary>Reads a furnishing's placement from its extra data; an item without enough bytes reads as not placed.</summary>
        public static FurniturePlacement Read(InventoryItem item)
        {
            var ext = item.ExtData;
            if (ext == null || ext.Length < MinExtDataLength)
            {
                return new FurniturePlacement(item.Container, item.Slot, item.ItemId, false, false, 0, 0, 0, 0);
            }
            return new FurniturePlacement(item.Container, item.Slot, item.ItemId,
                Installed: (ext[1] & 0x40) != 0, SecondFloor: (ext[1] & 0x01) != 0,
                X: ext[6], Level: ext[7], Z: ext[8], Rotation: (byte)(ext[9] & 3));
        }
    }

    /// <summary>
    /// A mannequin's worn items, read from its extra data: Storage slots (0 = none) for main, sub, ranged, head, body,
    /// hands, legs and feet at +10-+17 (the order of C2S 0x03B's <see cref="SubcontainerSlotIndex"/>), the race at +18
    /// and the pose at +19. The server takes mannequin gear from the Storage only.
    /// Extra data layout referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/items/exdata/mannequin.h).
    /// </summary>
    public readonly record struct MannequinGear(byte Main, byte Sub, byte Ranged, byte Head, byte Body, byte Hands, byte Legs, byte Feet, byte Race, byte Pose)
    {
        public const int SlotCount = 8;

        public static MannequinGear Read(InventoryItem item)
        {
            var ext = item.ExtData;
            if (ext == null || ext.Length < 20) return default;
            return new MannequinGear(ext[10], ext[11], ext[12], ext[13], ext[14], ext[15], ext[16], ext[17], ext[18], ext[19]);
        }

        /// <summary>The Storage slot worn in a mannequin slot (0-7, <see cref="SubcontainerSlotIndex"/> order); 0 for none.</summary>
        public byte this[int index] => index switch
        {
            0 => Main,
            1 => Sub,
            2 => Ranged,
            3 => Head,
            4 => Body,
            5 => Hands,
            6 => Legs,
            _ => index == 7 ? Feet : (byte)0,
        };
    }

    /// <summary>
    /// The Mog House layout grid and the furnishings in the Mog Safes. The grid is cells of 0.6 yalm seen from above, centred on
    /// the room: x 0-19 from west to east, z rows from the door side, (0, 0) the bottom left facing north. The first floor's rows
    /// run 0-23 with an 8 x 18 block in front of the door (x 6-13, z 0-17) that takes nothing, and five wall slots (0-4); the
    /// second floor's rows run 0-25, all usable, with 92 wall slots. Furnishings are item type 10, flowerpots 12 and mannequins 14
    /// in the item DAT (checked against the retail item DAT, 2026-11-02: Simple Bed 2 is 10, Brass Flowerpot 217 is 12, H♂
    /// Mannequin 256 is 14). Footprints and placement rules come from the client's <see cref="FurnitureTable"/>.
    /// **Beyond XiPackets / LandSandBoat:** the cell size and origin, from the San d'Oria room (file 389) against the retail video
    /// <c>20261104-0517-47</c>: the permanent rug is 4.8 x 7.2 yalms centred on the room and fills columns 6-13, rows 6-17; a
    /// Simple Bed's mesh (2.1 x 3.4) fits its 4 x 6 cells only at 0.6.
    /// Grid sizes referenced from XiPackets (https://github.com/atom0s/XiPackets/tree/main/world/client/0x00FA) and
    /// LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/c2s/0x0fa_myroom_layout.cpp).
    /// </summary>
    public static class MogHouseFurniture
    {
        public const ushort FurnishingItemType = 10, FlowerpotItemType = 12, MannequinItemType = 14;

        public const int GridWidth = 20;
        public const int FirstFloorRows = 24, SecondFloorRows = 26;
        public const int FirstFloorWallSlots = 5, SecondFloorWallSlots = 92;

        /// <summary>The first floor's unusable block in front of the door: columns 6-13, rows 0-17.</summary>
        public const int DoorBlockLeft = 6, DoorBlockRight = 13, DoorBlockTop = 17;

        public static int Rows(bool secondFloor) => secondFloor ? SecondFloorRows : FirstFloorRows;

        /// <summary>The Storage's size limit (LandSandBoat <c>ITEM_MAX</c> for the container).</summary>
        public const int MaxStorage = 80;

        /// <summary>A grid cell's side in yalms.</summary>
        public const float CellSize = 0.6f;

        /// <summary>The east and west walls' distance from the room's middle (video: one cell's width past the grid, less a little).</summary>
        public const float WallX = 6.5f;

        /// <summary>How high a wall piece's origin hangs (<b>provisional</b>: the top-down video does not show it).</summary>
        public const float WallHeight = 2.5f;

        /// <summary>The first floor's wall slots' places along the walls: slots 0-1 on the east wall, 2-4 on the west (XiPackets' diagram).</summary>
        private static readonly (bool East, float Z)[] FirstFloorWalls = { (true, 4.8f), (true, -4.8f), (false, -4.8f), (false, 0f), (false, 4.8f) };

        /// <summary>A cell corner's place on the floor, internal space: x east, z north (the room's middle is the origin).</summary>
        public static (float X, float Z) CellCorner(float x, float z, bool secondFloor) =>
            ((x - GridWidth / 2f) * CellSize, (z - Rows(secondFloor) / 2f) * CellSize);

        /// <summary>A footprint's cells across and deep at a rotation: quarter turns swap them.</summary>
        public static (int X, int Z) Footprint(in FurnitureInfo info, int rotation) =>
            (rotation & 1) != 0 ? (info.SizeZ, info.SizeX) : (info.SizeX, info.SizeZ);

        /// <summary>
        /// Where a floor piece's mesh goes in display space (-x, -y, z), its origin at the footprint's middle, raised by its
        /// stacking level (C2S 0x0FA <c>y</c>, tenths of a yalm), turned a quarter clockwise per <paramref name="rotation"/>
        /// (<b>provisional</b>: which way is not checked against retail).
        /// </summary>
        public static Matrix4x4 FloorTransform(in FurnitureInfo info, int x, int z, int level, int rotation, bool secondFloor)
        {
            var (w, d) = Footprint(info, rotation);
            var (cx, cz) = CellCorner(x + w / 2f, z + d / 2f, secondFloor);
            return Matrix4x4.CreateRotationY(-rotation * MathF.PI / 2) * Matrix4x4.CreateTranslation(-cx, level * 0.1f, cz);
        }

        /// <summary>A first-floor wall slot's place, display space, its mesh's back on the wall and facing into the room; null for a slot with no known place.</summary>
        public static Matrix4x4? WallTransform(int slot, bool secondFloor)
        {
            if (secondFloor || slot < 0 || slot >= FirstFloorWalls.Length) return null;
            var (east, z) = FirstFloorWalls[slot];
            return Matrix4x4.CreateRotationY(east ? -MathF.PI / 2 : MathF.PI / 2) * Matrix4x4.CreateTranslation(east ? -WallX : WallX, WallHeight, z);
        }

        /// <summary>Whether a footprint at a cell stays on usable floor.</summary>
        public static bool FootprintFits(int x, int z, int width, int depth, bool secondFloor)
        {
            for (int cx = x; cx < x + width; cx++)
            {
                for (int cz = z; cz < z + depth; cz++)
                {
                    if (!IsFloorCell(cx, cz, secondFloor)) return false;
                }
            }
            return true;
        }

        /// <summary>Whether a grid cell takes furniture on that floor.</summary>
        public static bool IsFloorCell(int x, int z, bool secondFloor)
        {
            if (x < 0 || z < 0 || x >= GridWidth || z >= Rows(secondFloor)) return false;
            if (secondFloor) return true;
            return x < DoorBlockLeft || x > DoorBlockRight || z > DoorBlockTop;
        }

        /// <summary>Whether an item type is one the layout places (a furnishing, flowerpot or mannequin).</summary>
        public static bool IsFurnishingType(ushort itemType) =>
            itemType is FurnishingItemType or FlowerpotItemType or MannequinItemType;

        /// <summary>Whether an item is furniture the layout can use.</summary>
        public static bool IsFurniture(ushort itemId, Func<ushort, ItemRecord?>? lookup) => lookup?.Invoke(itemId) is { } record && IsFurnishingType(record.ItemType);

        /// <summary>Whether an item is a mannequin (item ids 256-263: Hume ♂/♀, Elvaan ♂/♀, Tarutaru ♂/♀, Mithra, Galka).</summary>
        public static bool IsMannequin(ushort itemId) => itemId is >= 256 and <= 263;

        /// <summary>The Mog Safes the layout reads, the second only with the second floor (LandSandBoat's 0x03B handler).</summary>
        public static readonly ContainerId[] Containers = { ContainerId.MogSafe, ContainerId.MogSafe2 };

        /// <summary>
        /// Every furnishing in the Mog Safes, placed or not, by container and slot. <paramref name="lookup"/> tells
        /// furnishings from the other items kept there; without it nothing is listed.
        /// </summary>
        public static List<FurniturePlacement> Snapshot(InventoryState inventory, Func<ushort, ItemRecord?>? lookup)
        {
            var list = new List<FurniturePlacement>();
            if (lookup == null) return list;
            foreach (var container in Containers)
            {
                foreach (var item in inventory.SnapshotItems(container))
                {
                    if (item.ItemId == 0 || item.ItemId == InventoryState.GilItemId) continue;
                    if (lookup(item.ItemId) is not { } record || !IsFurnishingType(record.ItemType)) continue;
                    list.Add(FurniturePlacement.Read(item));
                }
            }
            list.Sort((a, b) => a.Container != b.Container ? a.Container.CompareTo(b.Container) : a.Slot.CompareTo(b.Slot));
            return list;
        }
    }
}
