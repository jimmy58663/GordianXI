// src/Gordian.Core/Network/Packets/PacketHeader.cs
using System;
using System.Buffers.Binary;

namespace Gordian.Core.Network.Packets
{
    /// <summary>
    /// Represents the standard 4-byte header common to all FFXI sub-packets.
    /// Bitfield layout:
    ///   Byte 0..1 (16 bits): bits 0..8 = Packet ID (0..511), bits 9..15 = Size in 4-byte words
    ///   Byte 2..3 (16 bits): Sequence ID
    /// </summary>
    public readonly struct PacketHeader : IEquatable<PacketHeader>
    {
        public ushort PacketId { get; }
        public int TotalSize { get; }
        public ushort SequenceId { get; }

        public PacketHeader(ushort packetId, int totalSize, ushort sequenceId)
        {
            PacketId = (ushort)(packetId & 0x1FF);
            TotalSize = totalSize;
            SequenceId = sequenceId;
        }

        public static bool TryParse(ReadOnlySpan<byte> span, out PacketHeader header)
        {
            if (span.Length < 4)
            {
                header = default;
                return false;
            }

            ushort rawTypeAndSize = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(0, 2));
            ushort packetId = (ushort)(rawTypeAndSize & 0x1FF);
            // In LandSandBoat: SmallPD_Size = (ref<uint8>(ptr, 1) & 0xFE) * 2
            int packetSize = (span[1] & 0xFE) * 2;
            ushort sequenceId = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(2, 2));

            header = new PacketHeader(packetId, packetSize, sequenceId);
            return true;
        }

        public bool Equals(PacketHeader other) =>
            PacketId == other.PacketId && TotalSize == other.TotalSize && SequenceId == other.SequenceId;

        public override bool Equals(object? obj) =>
            obj is PacketHeader other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(PacketId, TotalSize, SequenceId);

        public static bool operator ==(PacketHeader left, PacketHeader right) => left.Equals(right);
        public static bool operator !=(PacketHeader left, PacketHeader right) => !left.Equals(right);

        public override string ToString() =>
            $"0x{PacketId:X3} (Size: {TotalSize}, Seq: {SequenceId})";
    }
}
