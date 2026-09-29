// src/Gordian.Core/Network/Packets/TreasurePackets.cs
using System;
using System.Buffers.Binary;
using System.Text;

namespace Gordian.Core.Network.Packets
{
    /// <summary>
    /// A player's entry on a treasure pool item (the <c>Entry</c> byte of S2C 0x0D2).
    /// Packet structure referenced from XiPackets (https://github.com/atom0s/XiPackets), <c>world/server/0x00D2</c>.
    /// </summary>
    public enum TreasureEntryKind : byte
    {
        None = 0,
        Pass = 1,
        Lot = 2
    }

    /// <summary>
    /// The <c>JudgeFlg</c> of S2C 0x0D3: 0 is an ordinary lot or pass, 1 the item was won, 2 the winner cannot take
    /// it and it is lost, 3 and above clear the slot silently.
    /// Packet structure referenced from XiPackets (https://github.com/atom0s/XiPackets), <c>world/server/0x00D3</c>;
    /// values referenced from LandSandBoat (https://github.com/LandSandBoat/server), <c>s2c/0x0d3_trophy_solution.h</c>.
    /// </summary>
    public enum TreasureJudge : byte
    {
        Progress = 0,
        Win = 1,
        WinError = 2,
        Lost = 3
    }

    /// <summary>
    /// S2C 0x0D2 (GP_SERV_COMMAND_TROPHY_LIST): an item, or gil, entering the treasure pool. Payload offsets are the
    /// XiPackets packet offsets minus the 4-byte header. A gold-only packet has <see cref="ItemId"/> 0 and no slot.
    /// Packet structure referenced from XiPackets (https://github.com/atom0s/XiPackets), <c>world/server/0x00D2</c>;
    /// server side referenced from LandSandBoat (https://github.com/LandSandBoat/server), <c>s2c/0x0d2_trophy_list.cpp</c>.
    /// </summary>
    public readonly ref struct S2C_0x0D2_TrophyList
    {
        public const ushort PacketId = 0x0D2;
        public const int MinPayloadLength = 53;

        public bool IsValid { get; }
        public uint ItemCount { get; }
        public uint DropperId { get; }
        public ushort Gold { get; }
        public ushort ItemId { get; }
        public ushort DropperIndex { get; }
        public byte Slot { get; }
        public TreasureEntryKind Entry { get; }
        public bool IsContainer { get; }
        public uint StartTime { get; }
        public bool IsLocallyLotted { get; }
        public ushort LocalLot { get; }
        public uint LeaderId { get; }
        public ushort LeaderIndex { get; }
        public ushort LeaderLot { get; }
        public string LeaderName { get; }
        public bool Named { get; }
        public bool Plural { get; }

        public S2C_0x0D2_TrophyList(ReadOnlySpan<byte> payload)
        {
            this = default;
            LeaderName = string.Empty;
            if (payload.Length < MinPayloadLength) return;

            ItemCount = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0, 4));
            DropperId = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(4, 4));
            Gold = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(8, 2));
            ItemId = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(12, 2));
            DropperIndex = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(14, 2));
            Slot = payload[16];
            Entry = (TreasureEntryKind)payload[17];
            IsContainer = payload[18] != 0;
            StartTime = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(20, 4));
            IsLocallyLotted = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(24, 2)) != 0;
            LocalLot = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(26, 2));
            LeaderId = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(28, 4));
            LeaderIndex = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(32, 2));
            LeaderLot = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(34, 2));
            LeaderName = TreasurePacketText.ReadName(payload.Slice(36, 16));
            Named = (payload[52] & 0x01) != 0;
            Plural = (payload[52] & 0x02) != 0;
            IsValid = true;
        }
    }

    /// <summary>
    /// S2C 0x0D3 (GP_SERV_COMMAND_TROPHY_SOLUTION): a lot, a pass, or the final judgement of a pool item. Payload
    /// offsets are the XiPackets packet offsets minus the 4-byte header. The client reads 24 bytes of each name (an
    /// overrun into the next field); the names are 16 bytes and NUL terminated, so 16 are read here.
    /// Packet structure referenced from XiPackets (https://github.com/atom0s/XiPackets), <c>world/server/0x00D3</c>;
    /// server side referenced from LandSandBoat (https://github.com/LandSandBoat/server), <c>s2c/0x0d3_trophy_solution.cpp</c>.
    /// </summary>
    public readonly ref struct S2C_0x0D3_TrophySolution
    {
        public const ushort PacketId = 0x0D3;
        public const int MinPayloadLength = 50;

        public bool IsValid { get; }
        /// <summary>The current winning lotter (in judgement modes 1 and 2, the winner).</summary>
        public uint LeaderId { get; }
        /// <summary>The player who lotted or passed.</summary>
        public uint EntryId { get; }
        public ushort LeaderIndex { get; }
        public short LeaderLot { get; }
        public ushort EntryIndex { get; }
        /// <summary>True when the entry is a lot, false when it is a pass.</summary>
        public bool EntryIsLot { get; }
        public short EntryLot { get; }
        public byte Slot { get; }
        public TreasureJudge Judge { get; }
        public string LeaderName { get; }
        public string EntryName { get; }

        public S2C_0x0D3_TrophySolution(ReadOnlySpan<byte> payload)
        {
            this = default;
            LeaderName = string.Empty;
            EntryName = string.Empty;
            if (payload.Length < MinPayloadLength) return;

            LeaderId = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0, 4));
            EntryId = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(4, 4));
            LeaderIndex = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(8, 2));
            LeaderLot = BinaryPrimitives.ReadInt16LittleEndian(payload.Slice(10, 2));
            ushort entryWord = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(12, 2));
            EntryIndex = (ushort)(entryWord & 0x7FFF);
            EntryIsLot = (entryWord & 0x8000) != 0;
            EntryLot = BinaryPrimitives.ReadInt16LittleEndian(payload.Slice(14, 2));
            Slot = payload[16];
            Judge = (TreasureJudge)payload[17];
            LeaderName = TreasurePacketText.ReadName(payload.Slice(18, 16));
            EntryName = TreasurePacketText.ReadName(payload.Slice(34, 16));
            IsValid = true;
        }
    }

    internal static class TreasurePacketText
    {
        /// <summary>Reads a NUL terminated name from a fixed field.</summary>
        public static string ReadName(ReadOnlySpan<byte> field)
        {
            int end = field.IndexOf((byte)0);
            if (end < 0) end = field.Length;
            return Encoding.UTF8.GetString(field.Slice(0, end));
        }
    }

    /// <summary>Builders for the treasure pool client packets.</summary>
    public static class TreasurePacketBuilder
    {
        /// <summary>The pool holds 10 items, slots 0 to 9.</summary>
        public const int SlotCount = 10;

        /// <summary>
        /// Builds C2S 0x041 (GP_CLI_COMMAND_TROPHY_ENTRY, 8 bytes): lot on a pool slot. <paramref name="inventoryIndex"/>
        /// is the first empty bag slot the client assumes the item lands in (from 1; slot 0 is gil).
        /// Packet structure referenced from XiPackets (https://github.com/atom0s/XiPackets), <c>world/client/0x0041</c>.
        /// </summary>
        public static byte[] BuildLot(byte slot, byte inventoryIndex, ushort sequenceId = 0)
        {
            var packet = new byte[8];
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(0, 2), (ushort)(0x041 | (2 << 9)));
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2, 2), sequenceId);
            packet[4] = slot;
            packet[5] = inventoryIndex;
            return packet;
        }

        /// <summary>
        /// Builds C2S 0x042 (GP_CLI_COMMAND_TROPHY_ABSENCE): pass on a pool slot. The 6 byte struct is sent as 8
        /// bytes, the 4-byte unit the header's size field counts in.
        /// Packet structure referenced from XiPackets (https://github.com/atom0s/XiPackets), <c>world/client/0x0042</c>.
        /// </summary>
        public static byte[] BuildPass(byte slot, ushort sequenceId = 0)
        {
            var packet = new byte[8];
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(0, 2), (ushort)(0x042 | (2 << 9)));
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2, 2), sequenceId);
            packet[4] = slot;
            return packet;
        }
    }
}
