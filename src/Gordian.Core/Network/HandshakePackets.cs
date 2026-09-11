// src/Gordian.Core/Network/HandshakePackets.cs
using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Gordian.Core.Network
{
    /// <summary>
    /// Factory helpers for constructing binary-accurate client handshake sub-packets
    /// and login datagrams compatible with LandSandBoat (LSB).
    /// </summary>
    public static class HandshakePackets
    {
        public const int LoginSubPacketSize = 92;
        public const int GameOkSubPacketSize = 12;
        public const int NetEndSubPacketSize = 8;
        public const int PosSubPacketSize = 32;
        public const int FfxiHeaderSize = 28;
        public const int FfxiChecksumSize = 16;
        public const int LoginDatagramTotalSize = FfxiHeaderSize + LoginSubPacketSize + FfxiChecksumSize; // 136 bytes

        /// <summary>
        /// Convenience overload that constructs and returns a standalone 92-byte GP_CLI_LOGIN (0x00A) sub-packet byte array.
        /// Intended primarily for unit tests, offline serialization, and isolated diagnostics.
        /// For high-performance networking paths, use the zero-allocation <see cref="BuildLoginSubPacket(Span{byte}, uint, string, string, ReadOnlySpan{byte}, uint, ushort)"/> overload.
        /// </summary>
        public static byte[] BuildLoginSubPacket(
            uint characterId,
            string characterName,
            string accountName,
            ReadOnlySpan<byte> ticket = default,
            uint clientVersion = 1,
            ushort sequenceId = 0)
        {
            byte[] packet = new byte[LoginSubPacketSize];
            BuildLoginSubPacket(packet.AsSpan(), characterId, characterName, accountName, ticket, clientVersion, sequenceId);
            return packet;
        }

        /// <summary>
        /// Writes the 92-byte GP_CLI_LOGIN (0x00A) sub-packet directly into a destination Span without allocations.
        /// </summary>
        public static void BuildLoginSubPacket(
            Span<byte> destination,
            uint characterId,
            string characterName,
            string accountName,
            ReadOnlySpan<byte> ticket = default,
            uint clientVersion = 1,
            ushort sequenceId = 0)
        {
            if (destination.Length < LoginSubPacketSize)
            {
                throw new ArgumentException($"Destination span must be at least {LoginSubPacketSize} bytes.", nameof(destination));
            }

            destination.Slice(0, LoginSubPacketSize).Clear();

            // 1. Header: ID 0x00A, Size 23 (23 * 4 = 92 bytes)
            // (id & 0x1FF) | ((size & 0x7F) << 9)
            ushort headerWord = (ushort)(0x00A | (23 << 9));
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(0, 2), headerWord);
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(2, 2), sequenceId);

            // Offset 4: LoginPacketCheck (1 byte, computed below)
            // Offset 5: padding00 (1 byte, 0)
            // Offset 6: unknown00 (2 bytes, MyPort = 0)
            // Offset 8: unknown01 (4 bytes, MyIP = 0)
            // Offset 12: UniqueNo (4 bytes)
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(12, 4), characterId);

            // Offset 16..34: GrapIDTbl (18 bytes, already 0)

            // Offset 34..49: sName (15 bytes, null-terminated ASCII)
            if (!string.IsNullOrEmpty(characterName))
            {
                byte[] nameBytes = Encoding.ASCII.GetBytes(characterName);
                int nameLen = Math.Min(nameBytes.Length, 14); // Keep room for null terminator
                nameBytes.AsSpan(0, nameLen).CopyTo(destination.Slice(34, nameLen));
            }

            // Offset 49..64: sAccunt (15 bytes, null-terminated ASCII)
            if (!string.IsNullOrEmpty(accountName))
            {
                byte[] accBytes = Encoding.ASCII.GetBytes(accountName);
                int accLen = Math.Min(accBytes.Length, 14);
                accBytes.AsSpan(0, accLen).CopyTo(destination.Slice(49, accLen));
            }

            // Offset 64..80: Ticket (16 bytes)
            if (!ticket.IsEmpty)
            {
                int ticketLen = Math.Min(ticket.Length, 16);
                ticket.Slice(0, ticketLen).CopyTo(destination.Slice(64, ticketLen));
            }

            // Offset 80..84: Ver (4 bytes)
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(80, 4), clientVersion);

            // Offset 84..88: sPlatform (4 bytes: PC = 1)
            destination[84] = 0x01;
            destination[85] = 0x00;
            destination[86] = 0x00;
            destination[87] = 0x00;

            // Offset 88..90: uCliLang (2 bytes: English = 0)
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(88, 2), 0);

            // Offset 90..92: dammyArea (2 bytes: 0)
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(90, 2), 0);

            // Calculate LoginPacketCheck:
            // Sum of all bytes from offset 8 (unknown01) to offset 91 inclusive
            byte checksum = 0;
            for (int i = 8; i < LoginSubPacketSize; i++)
            {
                checksum += destination[i];
            }
            destination[4] = checksum;
        }

        /// <summary>
        /// Builds a full unencrypted 136-byte UDP datagram envelope containing the 28-byte FFXI header,
        /// 92-byte GP_CLI_LOGIN sub-packet, and 16-byte MD5 checksum.
        /// </summary>
        public static byte[] BuildLoginDatagram(
            uint characterId,
            string characterName,
            string accountName,
            ReadOnlySpan<byte> ticket = default,
            uint clientVersion = 1,
            ushort clientPacketSeq = 1)
        {
            byte[] datagram = new byte[LoginDatagramTotalSize];

            // 1. 28-byte FFXI Header
            BinaryPrimitives.WriteUInt16LittleEndian(datagram.AsSpan(0, 2), 0); // ServerPacketId = 0
            BinaryPrimitives.WriteUInt16LittleEndian(datagram.AsSpan(2, 2), clientPacketSeq); // ClientPacketId
            uint timestamp = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            BinaryPrimitives.WriteUInt32LittleEndian(datagram.AsSpan(8, 4), timestamp);

            // 2. 92-byte GP_CLI_LOGIN sub-packet at offset 28
            Span<byte> subPacketSpan = datagram.AsSpan(FfxiHeaderSize, LoginSubPacketSize);
            BuildLoginSubPacket(subPacketSpan, characterId, characterName, accountName, ticket, clientVersion, sequenceId: 0);

            // 3. 16-byte MD5 checksum computed over the 92-byte sub-packet at offset 120
            Span<byte> md5Span = datagram.AsSpan(FfxiHeaderSize + LoginSubPacketSize, FfxiChecksumSize);
            MD5.HashData(subPacketSpan, md5Span);

            return datagram;
        }

        /// <summary>
        /// Builds the 12-byte GP_CLI_GAMEOK (0x00C) sub-packet.
        /// </summary>
        public static byte[] BuildGameOkSubPacket(ushort sequenceId = 0, uint clientState = 0, uint debugClientFlg = 0)
        {
            byte[] packet = new byte[GameOkSubPacketSize];
            // Header: ID 0x00C, Size 3 (3 * 4 = 12 bytes)
            ushort headerWord = (ushort)(0x00C | (3 << 9));
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(0, 2), headerWord);
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2, 2), sequenceId);

            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4, 4), clientState);
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(8, 4), debugClientFlg);

            return packet;
        }

        /// <summary>
        /// Builds the 8-byte GP_CLI_NETEND (0x00D) sub-packet.
        /// </summary>
        public static byte[] BuildNetEndSubPacket(ushort sequenceId = 0, ushort state = 0)
        {
            byte[] packet = new byte[NetEndSubPacketSize];
            // Header: ID 0x00D, Size 2 (2 * 4 = 8 bytes)
            ushort headerWord = (ushort)(0x00D | (2 << 9));
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(0, 2), headerWord);
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2, 2), sequenceId);

            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), state);
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(6, 2), 0); // padding

            return packet;
        }

        /// <summary>
        /// Builds the 32-byte GP_CLI_POS (0x015) keepalive / position sub-packet.
        /// </summary>
        public static byte[] BuildPosPingPongSubPacket(
            ushort sequenceId = 0,
            float x = 0f,
            float y = 0f,
            float z = 0f,
            byte dir = 0)
        {
            byte[] packet = new byte[PosSubPacketSize];
            // Header: ID 0x015, Size 8 (8 * 4 = 32 bytes)
            ushort headerWord = (ushort)(0x015 | (8 << 9));
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(0, 2), headerWord);
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2, 2), sequenceId);

            BinaryPrimitives.WriteSingleLittleEndian(packet.AsSpan(4, 4), x);
            BinaryPrimitives.WriteSingleLittleEndian(packet.AsSpan(8, 4), z);
            BinaryPrimitives.WriteSingleLittleEndian(packet.AsSpan(12, 4), y);

            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(16, 2), 0); // MovTime
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(18, 2), 0); // MoveFlame

            packet[20] = dir;
            packet[21] = 0; // Modes: TargetMode, RunMode, GroundMode

            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(22, 2), 0); // facetarget
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(24, 4), (uint)Environment.TickCount); // TimeNow
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(28, 4), 0); // padding

            return packet;
        }
    }
}
