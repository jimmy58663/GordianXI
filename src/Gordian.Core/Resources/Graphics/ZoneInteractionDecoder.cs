// src/Gordian.Core/Resources/Graphics/ZoneInteractionDecoder.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Numerics;
using Gordian.Core.Resources.Containers;

namespace Gordian.Core.Resources.Graphics
{
    /// <summary>
    /// One Section 0x36 ("RID") zone interaction volume: a box turned about Y, with the 4-char id whose first character
    /// gives its kind (<c>_</c> door, <c>@</c> lift, <c>z</c> zone line, <c>m</c> sub-area, ...).
    /// </summary>
    /// <param name="Id">Record +0x24 FourCC.</param>
    /// <param name="Center">Record +0x00: the box centre in zone coordinates (internal space, +Y down).</param>
    /// <param name="RotationY">Record +0x10: the box's turn about Y in radians (its only rotation).</param>
    /// <param name="Size">Record +0x18: the box's full extents along its own X (width), Y (height) and Z (depth).</param>
    /// <param name="LiftFloor0">Record +0x34 (s16): a lift's floor 0, in 1/256 yalm from <see cref="Center"/>.Y.</param>
    /// <param name="LiftFloor1">Record +0x36 (s16): a lift's floor 1, in the same units.</param>
    /// <param name="Param">Record +0x2C (u32, the PS2 client's <c>zone_no</c>): a sub-area's interior id (<c>'m'</c>), a zone
    /// line's destination zone (xi-tools docs/zone/subareas.md).</param>
    public readonly record struct ZoneInteraction(string Id, Vector3 Center, float RotationY, Vector3 Size, short LiftFloor0 = 0, short LiftFloor1 = 0, int Param = 0)
    {
        /// <summary>A building interior's volume (<c>'m'</c>): standing in it shows the interior <see cref="Param"/> (#68).</summary>
        public bool IsSubArea => Id.Length > 0 && Id[0] == 'm' && Param > 0;

        /// <summary>A door's volume: a thin box filling the doorway (Metalworks' guild doors: 2.01 x 2.83 x 0.07).</summary>
        public bool IsDoor => Id.Length > 0 && Id[0] == '_';

        /// <summary>A lift's volume (<c>@</c>): a detection box larger than the car's travel, not its range.</summary>
        public bool IsLift => Id.Length > 0 && Id[0] == '@';

        /// <summary>
        /// A zone line's volume (<c>z</c>): walking into it asks the server for the zone change (C2S 0x05E with this id).
        /// LandSandBoat keys its zone lines by the same four characters (data/zones/*/zone.yaml <c>zonelines</c>, the id's
        /// little-endian bytes), including the Mog House entrances (<c>zmr?</c> / <c>zms?</c>) and exit (<c>zmrq</c>).
        /// </summary>
        public bool IsZoneLine => Id.Length == 4 && Id[0] == 'z';

        /// <summary>The box's width axis (its local X) in the XZ plane, the convention of the door volumes.</summary>
        public Vector2 WidthAxis => new(MathF.Cos(RotationY), -MathF.Sin(RotationY));

        /// <summary>The box's depth axis (its local Z) in the XZ plane.</summary>
        public Vector2 DepthAxis => new(MathF.Sin(RotationY), MathF.Cos(RotationY));

        /// <summary>
        /// Whether <paramref name="point"/> (zone coordinates, internal space) lies inside the turned box, grown by
        /// <paramref name="margin"/> yalms on every side.
        /// </summary>
        public bool Contains(Vector3 point, float margin = 0.0f)
        {
            if (MathF.Abs(point.Y - Center.Y) > (Size.Y * 0.5f) + margin) return false;
            var offset = new Vector2(point.X - Center.X, point.Z - Center.Z);
            return MathF.Abs(Vector2.Dot(offset, WidthAxis)) <= (Size.X * 0.5f) + margin
                && MathF.Abs(Vector2.Dot(offset, DepthAxis)) <= (Size.Z * 0.5f) + margin;
        }

        /// <summary>
        /// A lift's two stops in zone coordinates (internal space, +Y down): each s16 / 256 plus the record's Y, as the
        /// PS2 client's <c>XiLiftActor::SetLift</c> computes them (referenced from xi-tools, docs/zone/elevators.md,
        /// https://github.com/vekien/xi-tools). Metalworks' <c>@6l0</c>: 3856 / 798 on Y -13.1 gives +1.9625 / -9.983,
        /// the heights a Windower capture shows riders at. False for a record that is not a lift or whose floors are not
        /// finite numbers.
        /// </summary>
        public bool TryGetLiftFloors(out float floor0, out float floor1)
        {
            floor0 = (LiftFloor0 / 256.0f) + Center.Y;
            floor1 = (LiftFloor1 / 256.0f) + Center.Y;
            return IsLift && float.IsFinite(floor0) && float.IsFinite(floor1);
        }
    }

    /// <summary>
    /// Clean-room decoder for the FFXI zone DAT Section 0x36 ZoneInteraction ("RID") table. The section is not
    /// encrypted; it starts with the magic <c>RID</c>, payload +0x10 holds the offset of the first table, which is a u32
    /// count, 12 bytes of padding and then 0x40-byte entries. Format referenced from xi-tools
    /// (https://github.com/vekien/xi-tools, docs/zone/subareas.md §1, docs/zone/doors.md and docs/zone/elevators.md,
    /// after the PS2 client's <c>KO_RectData</c>); checked against Metalworks (zone 237), whose door records match its
    /// door leaves and whose lift records match a Windower capture of its lift (#66).
    /// </summary>
    public static class ZoneInteractionDecoder
    {
        private const int EntrySize = 0x40;

        /// <summary>Decodes a Section 0x36 payload; an empty list when it is short or malformed.</summary>
        public static List<ZoneInteraction> Decode(ReadOnlySpan<byte> payload)
        {
            var result = new List<ZoneInteraction>();
            if (payload.Length < 0x14 || payload[0] != (byte)'R' || payload[1] != (byte)'I' || payload[2] != (byte)'D') return result;

            int dataOffset = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(0x10));
            if (dataOffset < 0x14 || dataOffset + 16 > payload.Length) return result;
            int count = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(dataOffset));
            int first = dataOffset + 16;
            if (count <= 0 || count > (payload.Length - first) / EntrySize) return result;

            for (int i = 0; i < count; i++)
            {
                var entry = payload.Slice(first + (i * EntrySize), EntrySize);
                string id = ReadId(entry.Slice(0x24, 4));
                if (id.Length == 0) continue;
                result.Add(new ZoneInteraction(
                    id,
                    new Vector3(Single(entry, 0x00), Single(entry, 0x04), Single(entry, 0x08)),
                    Single(entry, 0x10),
                    new Vector3(Single(entry, 0x18), Single(entry, 0x1C), Single(entry, 0x20)),
                    BinaryPrimitives.ReadInt16LittleEndian(entry.Slice(0x34)),
                    BinaryPrimitives.ReadInt16LittleEndian(entry.Slice(0x36)),
                    BinaryPrimitives.ReadInt32LittleEndian(entry.Slice(0x2C))));
            }
            return result;
        }

        /// <summary>Decodes the first Section 0x36 of a zone DAT; empty when the zone has none.</summary>
        public static List<ZoneInteraction> DecodeFromDat(ReadOnlySpan<byte> datBytes)
        {
            foreach (var header in DatSectionWalker.ReadHeaders(datBytes))
            {
                if (header.TypeCode != DatSectionType.ZoneInteractions || header.DataOffset + header.DataSizeBytes > datBytes.Length) continue;
                return Decode(datBytes.Slice(header.DataOffset, header.DataSizeBytes));
            }
            return new List<ZoneInteraction>();
        }

        /// <summary>
        /// Decodes every Section 0x36 of a zone DAT, in file order. A zone splits its records over several tables: the
        /// lift records sit in their own (Metalworks' <c>e237</c> under <c>even</c>, Palborough Mines' <c>l143</c> under
        /// <c>lift</c>, Pso'Xja's <c>l009</c>), not in the first one the doors come from. Empty when the zone has none.
        /// </summary>
        public static List<ZoneInteraction> DecodeAllFromDat(ReadOnlySpan<byte> datBytes)
        {
            var result = new List<ZoneInteraction>();
            foreach (var header in DatSectionWalker.ReadHeaders(datBytes))
            {
                if (header.TypeCode != DatSectionType.ZoneInteractions || header.DataOffset + header.DataSizeBytes > datBytes.Length) continue;
                result.AddRange(Decode(datBytes.Slice(header.DataOffset, header.DataSizeBytes)));
            }
            return result;
        }

        /// <summary>
        /// The records of every Section 0x36 table that holds no lift record (<c>@</c>), in file order: where the zone's
        /// doors come from. The lift tables also hold the shaft doors (Metalworks' <c>even/e237</c> beside <c>_6ls</c>-<c>_6lv</c>),
        /// which <c>EntersEmptyShaft</c> covers. Round 4 E2 (2026-10-20): the first table alone left 101 zones without door
        /// blockers (1,448 doors, Alzadaal Undersea Ruins' 33 among them), since their first table has no door record.
        /// </summary>
        public static List<ZoneInteraction> DecodeDoorTablesFromDat(ReadOnlySpan<byte> datBytes)
        {
            var result = new List<ZoneInteraction>();
            foreach (var header in DatSectionWalker.ReadHeaders(datBytes))
            {
                if (header.TypeCode != DatSectionType.ZoneInteractions || header.DataOffset + header.DataSizeBytes > datBytes.Length) continue;
                AddDoorTable(result, Decode(datBytes.Slice(header.DataOffset, header.DataSizeBytes)));
            }
            return result;
        }

        /// <summary>Adds one Section 0x36 table's records to the door records unless it is a lift table (see <see cref="DecodeDoorTablesFromDat"/>).</summary>
        public static void AddDoorTable(List<ZoneInteraction> doorRecords, List<ZoneInteraction> table)
        {
            ArgumentNullException.ThrowIfNull(doorRecords);
            ArgumentNullException.ThrowIfNull(table);
            foreach (var record in table)
            {
                if (record.IsLift) return;
            }
            doorRecords.AddRange(table);
        }

        private static float Single(ReadOnlySpan<byte> entry, int offset) => BinaryPrimitives.ReadSingleLittleEndian(entry.Slice(offset));

        private static string ReadId(ReadOnlySpan<byte> span)
        {
            int end = span.IndexOf((byte)0);
            if (end < 0) end = span.Length;
            Span<char> chars = stackalloc char[4];
            for (int i = 0; i < end; i++)
            {
                byte b = span[i];
                if (b < 0x20 || b > 0x7E) return string.Empty;
                chars[i] = (char)b;
            }
            return new string(chars.Slice(0, end));
        }
    }
}
