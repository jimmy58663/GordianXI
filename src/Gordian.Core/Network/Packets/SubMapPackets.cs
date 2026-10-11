// src/Gordian.Core/Network/Packets/SubMapPackets.cs
using System;
using System.Buffers.Binary;

namespace Gordian.Core.Network.Packets
{
    /// <summary>
    /// The <c>State</c> of C2S 0x0F2 (GP_CLI_COMMAND_SUBMAPCHANGE).
    /// Values referenced from XiPackets (https://github.com/atom0s/XiPackets/tree/main/world/client/0x00F2) and
    /// LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/c2s/0x0f2_submapchange.h), which
    /// drops any other value and drops <see cref="Event"/> unless the character is in an event.
    /// </summary>
    public enum SubMapChangeState : ushort
    {
        /// <summary>The player walked into a different sub-region of the map.</summary>
        General = 0x01,

        /// <summary>An event moved the player between sub-regions (event opcode 0x75 sub 2).</summary>
        Event = 0x02,
    }

    /// <summary>
    /// S2C 0x10E (GP_SERV_COMMAND_REQSUBMAPNUM): the sub-map number the client asked for with C2S 0x0EB (event opcode
    /// 0xA6). The client stores it as the player's sub-map number and in the event's work values, and the event goes on.
    /// Payload: 0 u32 <c>MapNum</c> (8-byte packet).
    /// Packet structure referenced from XiPackets (https://github.com/atom0s/XiPackets/tree/main/world/server/0x010E) and
    /// LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x10e_reqsubmapnum.h), which
    /// always answers 0 and only while the character is held by an event.
    /// </summary>
    public readonly ref struct S2C_0x10E_ReqSubMapNum
    {
        public const ushort PacketId = 0x10E;

        public bool IsValid { get; }

        /// <summary>The sub-map number.</summary>
        public uint MapNum { get; }

        public S2C_0x10E_ReqSubMapNum(ReadOnlySpan<byte> payload)
        {
            IsValid = payload.Length >= 4;
            MapNum = IsValid ? BinaryPrimitives.ReadUInt32LittleEndian(payload) : 0;
        }
    }

    /// <summary>
    /// S2C 0x10F (GP_SERV_COMMAND_REQLOGOUTINFO): a logout mode answering C2S 0x0EC. Payload: 0 u32 <c>Mode</c>
    /// (8-byte packet). XiPackets calls it deprecated (the client never sends 0x0EC), and LandSandBoat neither defines
    /// nor sends it; decoded so it is not logged as unhandled.
    /// Packet structure referenced from XiPackets (https://github.com/atom0s/XiPackets/tree/main/world/server/0x010F).
    /// </summary>
    public readonly ref struct S2C_0x10F_ReqLogoutInfo
    {
        public const ushort PacketId = 0x10F;

        public bool IsValid { get; }

        /// <summary>The logout mode.</summary>
        public uint Mode { get; }

        public S2C_0x10F_ReqLogoutInfo(ReadOnlySpan<byte> payload)
        {
            IsValid = payload.Length >= 4;
            Mode = IsValid ? BinaryPrimitives.ReadUInt32LittleEndian(payload) : 0;
        }
    }

    /// <summary>
    /// S2C 0x005 (GP_SERV_COMMAND_PACKETCONTROL): sets the client's packet send rate value <c>PacketCnt</c> (the retail
    /// client starts every zone at 400 and times its send queue from it). Payload: 0 u32 <c>PacketCnt</c>, then 20
    /// bytes of padding (28-byte packet).
    /// Packet structure referenced from XiPackets (https://github.com/atom0s/XiPackets/tree/main/world/server/0x0005).
    /// LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x005_packetcontrol.h) declares
    /// it as unimplemented and never sends it.
    /// </summary>
    public readonly ref struct S2C_0x005_PacketControl
    {
        public const ushort PacketId = 0x005;

        /// <summary>The value the retail client resets <c>PacketCnt</c> to on every zone-in.</summary>
        public const uint DefaultPacketCount = 400;

        public bool IsValid { get; }

        /// <summary>The new packet count value.</summary>
        public uint PacketCount { get; }

        public S2C_0x005_PacketControl(ReadOnlySpan<byte> payload)
        {
            IsValid = payload.Length >= 4;
            PacketCount = IsValid ? BinaryPrimitives.ReadUInt32LittleEndian(payload) : 0;
        }
    }

    /// <summary>
    /// S2C 0x006 (GP_SERV_COMMAND_NARAKU): a GM request for the client's cached collision positions, which the retail
    /// client answers with GM command packets (C2S 0x01F). XiPackets knows no layout past the header and no capture of
    /// it; GordianXI only notes its arrival and does not answer.
    /// Packet structure referenced from XiPackets (https://github.com/atom0s/XiPackets/tree/main/world/server/0x0006).
    /// LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x006_naraku.h) declares it as an
    /// empty, unimplemented packet and never sends it.
    /// </summary>
    public readonly ref struct S2C_0x006_Naraku
    {
        public const ushort PacketId = 0x006;

        /// <summary>Always true: the packet has no fields.</summary>
        public bool IsValid => true;

        public S2C_0x006_Naraku(ReadOnlySpan<byte> payload)
        {
        }
    }

    /// <summary>
    /// Builders for the sub-map packets: C2S 0x0EB (GP_CLI_COMMAND_REQSUBMAPNUM, 4 bytes, header only) and C2S 0x0F2
    /// (GP_CLI_COMMAND_SUBMAPCHANGE, 8 bytes: payload 0 u16 <c>State</c>, 2 u16 <c>SubMapNumber</c>).
    /// Packet structures referenced from XiPackets (https://github.com/atom0s/XiPackets/tree/main/world/client/0x00EB,
    /// <c>world/client/0x00F2</c>); sizes checked against LandSandBoat's structs
    /// (https://github.com/LandSandBoat/server/blob/base/src/map/packets/c2s/0x0eb_reqsubmapnum.h,
    /// <c>0x0f2_submapchange.h</c>), whose size check drops any other length.
    /// </summary>
    public static class SubMapOutboundPackets
    {
        public const int ReqSubMapNumSize = 4;
        public const int SubMapChangeSize = 8;

        /// <summary>Writes C2S 0x0EB into <paramref name="destination"/> and returns its length.</summary>
        public static int BuildReqSubMapNum(Span<byte> destination, ushort sequenceId = 0)
        {
            if (destination.Length < ReqSubMapNumSize)
                throw new ArgumentException($"Destination must be at least {ReqSubMapNumSize} bytes.", nameof(destination));

            BinaryPrimitives.WriteUInt16LittleEndian(destination, (ushort)(0x0EB | ((ReqSubMapNumSize / 4) << 9)));
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(2), sequenceId);
            return ReqSubMapNumSize;
        }

        /// <summary>C2S 0x0EB as a new array.</summary>
        public static byte[] BuildReqSubMapNum(ushort sequenceId = 0)
        {
            byte[] packet = new byte[ReqSubMapNumSize];
            BuildReqSubMapNum(packet, sequenceId);
            return packet;
        }

        /// <summary>Writes C2S 0x0F2 into <paramref name="destination"/> and returns its length.</summary>
        public static int BuildSubMapChange(Span<byte> destination, SubMapChangeState state, ushort subMapNumber, ushort sequenceId = 0)
        {
            if (destination.Length < SubMapChangeSize)
                throw new ArgumentException($"Destination must be at least {SubMapChangeSize} bytes.", nameof(destination));

            BinaryPrimitives.WriteUInt16LittleEndian(destination, (ushort)(0x0F2 | ((SubMapChangeSize / 4) << 9)));
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(2), sequenceId);
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(4), (ushort)state);
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(6), subMapNumber);
            return SubMapChangeSize;
        }

        /// <summary>C2S 0x0F2 as a new array.</summary>
        public static byte[] BuildSubMapChange(SubMapChangeState state, ushort subMapNumber, ushort sequenceId = 0)
        {
            byte[] packet = new byte[SubMapChangeSize];
            BuildSubMapChange(packet, state, subMapNumber, sequenceId);
            return packet;
        }
    }
}
