// src/Gordian.Core/Resources/Tables/FurnitureTable.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using Gordian.Core.Resources.Containers;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Models;

namespace Gordian.Core.Resources.Tables
{
    /// <summary>How a furnishing stands (the <c>furn</c> table's first word).</summary>
    [Flags]
    public enum FurnitureFlags : ushort
    {
        /// <summary>Stands on the floor.</summary>
        Floor = 0,

        /// <summary>Hangs in a wall slot.</summary>
        Wall = 1,

        /// <summary>Stands on the floor and takes other pieces on top (tables, desks).</summary>
        Surface = 2,

        /// <summary>May stand on a <see cref="Surface"/> piece (vases, statues).</summary>
        OnTable = 4,

        /// <summary>A flowerpot (always with <see cref="OnTable"/> in the table, but placed on the floor by LandSandBoat).</summary>
        Flowerpot = 8,
    }

    /// <summary>A furnishing's layout data: footprint in grid cells and height in hundredths of a yalm.</summary>
    public readonly record struct FurnitureInfo(FurnitureFlags Flags, byte SizeX, byte SizeZ, ushort Height)
    {
        public bool IsWall => (Flags & FurnitureFlags.Wall) != 0;
        public bool IsSurface => (Flags & FurnitureFlags.Surface) != 0;
        public bool GoesOnTable => (Flags & FurnitureFlags.OnTable) != 0 && (Flags & FurnitureFlags.Flowerpot) == 0;

        /// <summary>The stacking level a piece standing on this one sends (C2S 0x0FA <c>y</c>): the height / 10.</summary>
        public byte StackLevel => (byte)(Height / 10);
    }

    /// <summary>A furnishing's mesh and textures (<see cref="FurnitureTable.ModelFileId"/>), the mesh in the room's display space around its origin.</summary>
    public sealed record FurnitureModel(ushort ItemId, IReadOnlyList<MeshGroup> Meshes, IReadOnlyDictionary<string, DecodedTexture> Textures);

    /// <summary>
    /// The client's furniture table: file 32922 (ROM directory <c>myro</c>), section type 0x59 <c>furn</c>, 1024 records of 8
    /// bytes: u16 flags (<see cref="FurnitureFlags"/>), u16 item id, u8 cells across, u8 cells deep, u16 height; 0 cells for an
    /// item that is not furniture. Found 2026-11-04 (#93): all 495 rows of LandSandBoat's <c>item_furnishing</c> are in it, 472 with
    /// the same size and height (flowerpots and mannequins differ: LandSandBoat keeps 1 x 1, height 0), and its placement column is
    /// this table's flags (0 floor, 1 surface = 2 here, 2 wall = 1 here, 3 on table = 4 here).
    /// **Beyond LandSandBoat / XiPackets**, neither of which names this table.
    /// A piece's model is one file per item in three runs of ids (<see cref="ModelFileId"/>; found by matching mesh sizes against
    /// this table): a Texture and a ZoneMesh section, the mesh's origin at the middle of its footprint on the floor (a wall piece: at the wall, facing -Z).
    /// Mannequins have no such file, and the statues from 264 are actors with routines, not meshes.
    /// </summary>
    public static class FurnitureTable
    {
        public const int FileId = 32922;
        private const int SectionType = 0x59, RecordSize = 8;

        /// <summary>The file of an item's furniture model; 0 for an id outside the three runs.</summary>
        public static int ModelFileId(ushort itemId) => itemId switch
        {
            < 256 => 32927 + itemId,
            >= 264 and < 512 => 51183 + itemId,
            >= 3584 and < 4096 => 59227 + itemId,
            _ => 0,
        };

        /// <summary>Reads the <c>furn</c> section of the table's file.</summary>
        public static Dictionary<ushort, FurnitureInfo> Parse(ReadOnlySpan<byte> dat)
        {
            var table = new Dictionary<ushort, FurnitureInfo>();
            foreach (var header in DatSectionWalker.ReadHeaders(dat))
            {
                if ((int)header.TypeCode != SectionType || header.DataOffset + header.DataSizeBytes > dat.Length) continue;
                var records = dat.Slice(header.DataOffset, header.DataSizeBytes);
                for (int at = 0; at + RecordSize <= records.Length; at += RecordSize)
                {
                    var record = records.Slice(at, RecordSize);
                    if (record[4] == 0) continue;
                    ushort id = BinaryPrimitives.ReadUInt16LittleEndian(record.Slice(2, 2));
                    table[id] = new FurnitureInfo((FurnitureFlags)BinaryPrimitives.ReadUInt16LittleEndian(record), record[4], record[5],
                        BinaryPrimitives.ReadUInt16LittleEndian(record.Slice(6, 2)));
                }
            }
            return table;
        }
    }
}
