// src/Gordian.Core/Network/Retail/RetailMapPackets.cs
using System;
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Gordian.Core.Network.Retail
{
    /// <summary>
    /// The retail map server's first datagram: 136 unencrypted bytes, a 28-byte transport header, the 92-byte C2S 0x00A
    /// and the MD5 of that packet.
    /// <para>
    /// Layout referenced from the GordianXI retail login spec, docs/network/retail-login-spec.md v2.1.19, section 4.6.1
    /// and test vector 8.
    /// </para>
    /// </summary>
    public static class RetailMapPackets
    {
        public const int TransportHeaderSize = 28;
        public const int LoginPacketSize = 92;
        public const int FirstDatagramSize = TransportHeaderSize + LoginPacketSize + 16;

        /// <summary>
        /// The map server's 20-byte raw key: the lobby session key and LE32(the 0x07 modifier + 4). Its MD5 is the map key
        /// (spec section 5.6, test vector 5). The map session cipher (<c>LegacyBlowfishCryptoSuite.InitializeKey</c>) MD5s
        /// a 20-byte key, so this goes straight in, the same way LandSandBoat's 20-byte key does; that cipher is the FFXI
        /// Blowfish variant (signed key bytes, key cut at its first zero byte) the spec describes.
        /// </summary>
        public static byte[] MapRawKey(ReadOnlySpan<byte> lobbySessionKey, uint selectModifier) =>
            PolHashes.KeyWithCounter(lobbySessionKey, unchecked(selectModifier + 4));

        /// <summary>The 16-byte map key: MD5 of <see cref="MapRawKey"/>.</summary>
        public static byte[] MapKey(ReadOnlySpan<byte> lobbySessionKey, uint selectModifier) => MD5.HashData(MapRawKey(lobbySessionKey, selectModifier));

        /// <summary>
        /// The 0x00A <c>Ticket</c>: MD5(the 15-byte account field as sent, all zeros + the 16-byte map key after the zero-byte
        /// preparation: from its first 0x00 on, every byte is 0x00, length still 16) (spec v2.1.24 sections 4.6.1 and 5.6
        /// item 2, verified on capture 231055's key with a zero at index 10). The map cipher cuts the key the same way.
        /// </summary>
        public static byte[] Ticket(ReadOnlySpan<byte> mapKey)
        {
            Span<byte> buffer = stackalloc byte[15 + 16];
            buffer[..15].Clear();
            mapKey[..16].CopyTo(buffer[15..]);
            TruncateAtZero(buffer[15..]);
            return MD5.HashData(buffer);
        }

        /// <summary>
        /// Fills a retail map datagram header's fields after the sequence and ack words (spec v2.1.25 section 4.6.3):
        /// <c>+0x04</c> the send time's milliseconds (0-999) and <c>+0x08</c> its Unix seconds; <c>+0x0C..+0x1B</c> the
        /// server's last 16 <c>State</c> bytes with word 0's low byte set to 0x01 and word 2 (<c>+0x14</c>) set to our
        /// IPv4 address in network order, when known.
        /// </summary>
        public static void WriteHeader(Span<byte> header, long sentAtUnixMs, ReadOnlySpan<byte> serverState, ReadOnlySpan<byte> clientAddress)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(4, 4), (uint)(sentAtUnixMs % 1000));
            BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(8, 4), (uint)(sentAtUnixMs / 1000));
            serverState[..16].CopyTo(header.Slice(0x0C, 16));
            header[0x0C] = 0x01;
            if (clientAddress.Length == 4) clientAddress.CopyTo(header.Slice(0x14, 4));
        }

        /// <summary>
        /// The map header's State word 2 (<c>+0x14</c>): our public IPv4 address in network byte order. It is the routing
        /// token's address (bytes 2..5, little-endian) reversed (spec v2.1.25 section 4.6.3: the same in every client
        /// datagram after the first across all eight captured map logins).
        /// </summary>
        public static byte[] ClientAddressFromRoutingToken(ReadOnlySpan<byte> routingToken)
        {
            if (routingToken.Length < 6) throw new ArgumentException("The routing token is 6 bytes.", nameof(routingToken));
            return [routingToken[5], routingToken[4], routingToken[3], routingToken[2]];
        }

        /// <summary>Sets the first 0x00 byte of <paramref name="key"/> and every byte after it to 0x00 (spec v2.1.24 section 5.6 item 2).</summary>
        public static void TruncateAtZero(Span<byte> key)
        {
            int zero = key.IndexOf((byte)0);
            if (zero >= 0) key[zero..].Clear();
        }

        /// <summary>
        /// The 136-byte first datagram. The transport header is the observed one (client sequence 1; the other bytes are
        /// not explained, spec section 9 item 7). The 0x00A sends a single space as the name, an empty account field, the
        /// <c>UniqueNo</c> from 0x0B, the ticket, "WIN", language 1 and 0xFFFF at +0x5A.
        /// </summary>
        public static byte[] BuildFirstDatagram(uint uniqueNo, ReadOnlySpan<byte> mapKey)
        {
            var datagram = new byte[FirstDatagramSize];
            ReadOnlySpan<byte> header = [0x01, 0x00, 0x00, 0x00, 0xC0, 0x02, 0x00, 0x00, 0x77, 0x00, 0x00, 0x00];
            header.CopyTo(datagram);
            ReadOnlySpan<byte> headerTail = [0x24, 0xE8, 0xFE, 0x76];
            headerTail.CopyTo(datagram.AsSpan(24));

            Span<byte> packet = datagram.AsSpan(TransportHeaderSize, LoginPacketSize);
            BinaryPrimitives.WriteUInt16LittleEndian(packet, (ushort)(0x00A | ((LoginPacketSize / 4) << 9)));
            BinaryPrimitives.WriteUInt16LittleEndian(packet[2..], 1);
            BinaryPrimitives.WriteUInt32LittleEndian(packet[0x0C..], uniqueNo);
            packet[0x22] = (byte)' ';
            Ticket(mapKey).CopyTo(packet[0x40..]);
            "WIN"u8.CopyTo(packet[0x54..]);
            BinaryPrimitives.WriteUInt16LittleEndian(packet[0x58..], 1);
            BinaryPrimitives.WriteUInt16LittleEndian(packet[0x5A..], 0xFFFF);
            byte sum = 0;
            foreach (byte b in packet[0x08..]) sum += b;
            packet[0x04] = sum;

            MD5.HashData(packet, datagram.AsSpan(TransportHeaderSize + LoginPacketSize));
            return datagram;
        }
    }
}
