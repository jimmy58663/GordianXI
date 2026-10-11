// src/Gordian.Core/Network/Packets/LockstylePackets.cs
using System;
using System.Buffers.Binary;

namespace Gordian.Core.Network.Packets
{
    /// <summary>
    /// S2C 0x11C (LSB <c>GP_SERV_COMMAND_LOCKSTYLE_ERROR</c>): the items a style lock could not use. Payload: 0 u8
    /// <c>Count</c>, 1 three bytes of padding, 4 u16 <c>ItemNo[]</c>. The client prints an error line for each of the
    /// first <c>Count</c> items.
    /// Packet structure referenced from XiPackets (https://github.com/atom0s/XiPackets/tree/main/world/server/0x011C), which
    /// calls it deprecated and leaves the array open, and LandSandBoat
    /// (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x11c_lockstyle_error.h), which sends it after
    /// a C2S 0x053 Set (<c>c2s/0x053_lockstyle.cpp</c>) for items that are not held, that no levelled job can equip, or
    /// whose weapon type does not match the equipped weapon, with a fixed 16-entry array (40-byte packet).
    /// </summary>
    public readonly ref struct S2C_0x11C_LockstyleError
    {
        public const ushort PacketId = 0x11C;

        /// <summary>The size of LandSandBoat's item array.</summary>
        public const int MaxItems = 16;

        private const int ItemsOffset = 4;

        private readonly ReadOnlySpan<byte> _payload;

        public bool IsValid { get; }

        /// <summary><c>Count</c> as sent.</summary>
        public byte Count { get; }

        /// <summary>The number of item ids the packet actually holds: <see cref="Count"/>, clamped to its length.</summary>
        public int ItemCount { get; }

        public S2C_0x11C_LockstyleError(ReadOnlySpan<byte> payload)
        {
            _payload = payload;
            IsValid = payload.Length >= ItemsOffset;
            Count = IsValid ? payload[0] : (byte)0;
            ItemCount = IsValid ? Math.Min(Count, (payload.Length - ItemsOffset) / 2) : 0;
        }

        /// <summary>The item id at <paramref name="index"/> (0 for an index past <see cref="ItemCount"/>).</summary>
        public ushort GetItemId(int index)
        {
            if ((uint)index >= (uint)ItemCount) return 0;
            return BinaryPrimitives.ReadUInt16LittleEndian(_payload.Slice(ItemsOffset + index * 2));
        }
    }
}
