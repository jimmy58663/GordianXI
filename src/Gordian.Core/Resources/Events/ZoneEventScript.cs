// src/Gordian.Core/Resources/Events/ZoneEventScript.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Gordian.Core.Resources.Events
{
    /// <summary>
    /// The compiled event scripts of one actor: the event ids it answers to, where each starts in the block's byte
    /// code, and the block's immediate data (message ids, item ids, other event ids the code refers to by index).
    /// </summary>
    public sealed class EventBlock
    {
        /// <summary>The actor number of player and zone events (no entity of the zone).</summary>
        public const uint ZoneActor = 0x7FFFFFFF;

        /// <summary>The actor number the local player's block carries in the retail scripts.</summary>
        public const uint PlayerActor = 0x7FFFFFF0;

        /// <summary>An event id matching any requested id (the block's fallback event).</summary>
        public const ushort AnyEventId = 0xFFFE;

        public EventBlock(uint actorId, ushort[] offsets, ushort[] eventIds, uint[] references, byte[] code)
        {
            ActorId = actorId;
            Offsets = offsets;
            EventIds = eventIds;
            References = references;
            Code = code;
        }

        /// <summary>The entity server id this block belongs to, or <see cref="ZoneActor"/> / <see cref="PlayerActor"/>.</summary>
        public uint ActorId { get; }

        /// <summary>Byte offset into <see cref="Code"/> where each event starts (same order as <see cref="EventIds"/>).</summary>
        public IReadOnlyList<ushort> Offsets { get; }

        /// <summary>The event ids of the block (the server's EventPara).</summary>
        public IReadOnlyList<ushort> EventIds { get; }

        /// <summary>The immediate data table the code references (operand values with bit 15 set).</summary>
        public IReadOnlyList<uint> References { get; }

        /// <summary>The block's byte code (every event, back to back).</summary>
        public byte[] Code { get; }

        /// <summary>The index of an event id in <see cref="EventIds"/>, or -1.</summary>
        public int IndexOf(ushort eventId)
        {
            for (int i = 0; i < EventIds.Count; i++)
            {
                if (EventIds[i] == eventId) return i;
            }
            return -1;
        }

        /// <summary>
        /// Finds an event's byte range: the requested id, else the block's <see cref="AnyEventId"/> fallback (the
        /// retail client's second lookup). Returns false when the block has neither.
        /// </summary>
        public bool TryGetEvent(ushort eventId, out int start, out int end)
        {
            int index = IndexOf(eventId);
            if (index < 0) index = IndexOf(AnyEventId);
            if (index < 0)
            {
                start = end = 0;
                return false;
            }
            start = Offsets[index];
            end = Code.Length;
            // Events are stored in ascending offset order; the next offset bounds this one.
            for (int i = 0; i < Offsets.Count; i++)
            {
                if (Offsets[i] > start && Offsets[i] < end) end = Offsets[i];
            }
            return true;
        }
    }

    /// <summary>
    /// A zone's compiled event scripts (the "Zone Events" DAT), one <see cref="EventBlock"/> per actor.
    /// <para>
    /// File location and layout referenced from XiEvents (https://github.com/atom0s/XiEvents, "Event DAT Files.md"
    /// and "Event DAT Structures.md"): the file of zone <c>z</c> is file id 5820 + z for zones 0-255 and
    /// 84735 + (z - 256) for zones 256-299 (checked against the retail file table, 2026-09-28). The file starts with
    /// a block count and a dword size per block; each block is: actor number, event count, that many ushort event
    /// offsets, that many ushort event ids, an immediate-data count and dwords, the byte code size and the byte code
    /// (padded to 4 bytes). Checked against the retail Southern San d'Oria file (ROM/21/39.DAT, 502 blocks).
    /// </para>
    /// </summary>
    public sealed class ZoneEventScript
    {
        private readonly Dictionary<uint, EventBlock> _byActor = new();

        private ZoneEventScript(List<EventBlock> blocks)
        {
            Blocks = blocks;
            foreach (var block in blocks) _byActor.TryAdd(block.ActorId, block);
        }

        public IReadOnlyList<EventBlock> Blocks { get; }

        /// <summary>The file id of a zone's event script DAT, or -1 for a zone id out of range.</summary>
        public static int GetFileId(int zoneId)
        {
            if (zoneId < 0 || zoneId > 299) return -1;
            return zoneId < 256 ? 5820 + zoneId : 84735 + (zoneId - 256);
        }

        /// <summary>The block of an actor (an entity server id, or the zone/player actor numbers).</summary>
        public bool TryGetBlock(uint actorId, out EventBlock block) => _byActor.TryGetValue(actorId, out block!);

        /// <summary>The first block that carries an event id, for tools and tests.</summary>
        public EventBlock? FindEvent(ushort eventId)
        {
            foreach (var block in Blocks)
            {
                if (block.IndexOf(eventId) >= 0) return block;
            }
            return null;
        }

        /// <summary>Decodes an event DAT. Returns null when the buffer does not hold one.</summary>
        public static ZoneEventScript? Parse(ReadOnlySpan<byte> file)
        {
            if (file.Length < 4) return null;
            int count = BinaryPrimitives.ReadInt32LittleEndian(file);
            if (count <= 0 || count > 100_000 || 4 + 4L * count > file.Length) return null;

            var blocks = new List<EventBlock>(count);
            int position = 4 + 4 * count;
            for (int i = 0; i < count; i++)
            {
                int size = BinaryPrimitives.ReadInt32LittleEndian(file.Slice(4 + 4 * i));
                if (size < 16 || position + size > file.Length) return null;
                var block = ParseBlock(file.Slice(position, size));
                if (block == null) return null;
                blocks.Add(block);
                position += size;
            }
            return new ZoneEventScript(blocks);
        }

        private static EventBlock? ParseBlock(ReadOnlySpan<byte> data)
        {
            uint actor = BinaryPrimitives.ReadUInt32LittleEndian(data);
            int eventCount = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(4));
            if (eventCount < 0 || eventCount > 10_000) return null;
            int offset = 8;
            if (offset + 4 * eventCount + 4 > data.Length) return null;

            var offsets = new ushort[eventCount];
            for (int i = 0; i < eventCount; i++) offsets[i] = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset + 2 * i));
            offset += 2 * eventCount;
            var ids = new ushort[eventCount];
            for (int i = 0; i < eventCount; i++) ids[i] = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset + 2 * i));
            offset += 2 * eventCount;

            int referenceCount = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset));
            offset += 4;
            if (referenceCount < 0 || offset + 4L * referenceCount + 4 > data.Length) return null;
            var references = new uint[referenceCount];
            for (int i = 0; i < referenceCount; i++) references[i] = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset + 4 * i));
            offset += 4 * referenceCount;

            int codeSize = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset));
            offset += 4;
            if (codeSize < 0 || offset + codeSize > data.Length) return null;
            var code = data.Slice(offset, codeSize).ToArray();
            foreach (ushort start in offsets)
            {
                if (start > codeSize) return null;
            }
            return new EventBlock(actor, offsets, ids, references, code);
        }
    }
}
