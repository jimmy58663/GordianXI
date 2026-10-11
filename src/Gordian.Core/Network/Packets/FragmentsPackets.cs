// src/Gordian.Core/Network/Packets/FragmentsPackets.cs
using System;
using System.Buffers.Binary;
using System.Text;

namespace Gordian.Core.Network.Packets
{
    /// <summary>
    /// What a fragments exchange (C2S 0x04B / S2C 0x04D) carries: the <c>value1</c> byte, which the client sets on its
    /// request task and the server echoes. Values referenced from XiPackets
    /// (https://github.com/atom0s/XiPackets/tree/main/world/server/0x004D) and LandSandBoat
    /// (https://github.com/LandSandBoat/server/blob/base/src/map/packets/c2s/0x04b_fragments.cpp).
    /// </summary>
    public enum FragmentsKind : byte
    {
        None = 0,

        /// <summary>The server message (<c>/servmes</c>, and the message shown at login): a string.</summary>
        ServerMessage = 1,

        /// <summary>Event VM opcode 0xB3 ranking boards (the Selbina fishing ranking): 36-byte ranking entries.</summary>
        Ranking = 2,
    }

    /// <summary>
    /// S2C 0x04D (GP_SERV_COMMAND_FRAGMENTS): one fragment of content the client asked for with C2S 0x04B. Payload:
    /// 0 u8 <c>Command</c> (1 the first fragment, 2 a later one; 0x0A-0x0D ranking requests), 1 s8 <c>Result</c>,
    /// 2 u8 <c>value1</c> (<see cref="FragmentsKind"/>), 3 u8 <c>value2</c> (the language for a server message:
    /// 1 Japanese, 2 English, 3 French, 4 German; the board for a ranking), 4 s32 <c>timestamp</c>, 8 s32
    /// <c>size_total</c>, 12 s32 <c>offset</c>, 16 s32 <c>data_size</c>, 20 <c>data</c> (at most 236 bytes). The packet
    /// is sized to its data. A server message longer than 236 bytes comes in several fragments; only the last is
    /// null-terminated, and the client asks for each next one at <c>offset + data_size</c>.
    /// Packet structure referenced from XiPackets (https://github.com/atom0s/XiPackets/tree/main/world/server/0x004D) and
    /// LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x04d_fragments_servmes.h,
    /// <c>0x04d_fragments_fishranking.h</c>).
    /// </summary>
    public readonly ref struct S2C_0x04D_Fragments
    {
        public const ushort PacketId = 0x04D;

        /// <summary>The most data one fragment holds.</summary>
        public const int MaxDataSize = 236;

        private const int DataOffset = 20;

        private readonly ReadOnlySpan<byte> _payload;

        public bool IsValid { get; }
        public byte Command { get; }
        public sbyte Result { get; }
        public FragmentsKind Kind { get; }
        public byte Value2 { get; }
        public int Timestamp { get; }
        public int SizeTotal { get; }
        public int Offset { get; }
        public int DataSize { get; }

        public S2C_0x04D_Fragments(ReadOnlySpan<byte> payload)
        {
            _payload = payload;
            IsValid = payload.Length >= DataOffset;
            if (!IsValid)
            {
                Command = 0;
                Result = 0;
                Kind = FragmentsKind.None;
                Value2 = 0;
                Timestamp = SizeTotal = Offset = DataSize = 0;
                return;
            }
            Command = payload[0];
            Result = unchecked((sbyte)payload[1]);
            Kind = (FragmentsKind)payload[2];
            Value2 = payload[3];
            Timestamp = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(4));
            SizeTotal = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(8));
            Offset = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(12));
            DataSize = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(16));
        }

        /// <summary>
        /// The fragment's data: <see cref="DataSize"/> bytes, clamped to what the packet holds and to
        /// <see cref="MaxDataSize"/>. For a ranking's first fragment <c>data_size</c> is 0 although the block is filled
        /// (XiPackets); use <see cref="RawData"/> for that.
        /// </summary>
        public ReadOnlySpan<byte> Data
        {
            get
            {
                if (!IsValid || DataSize <= 0) return ReadOnlySpan<byte>.Empty;
                int available = _payload.Length - DataOffset;
                return _payload.Slice(DataOffset, Math.Min(Math.Min(DataSize, MaxDataSize), available));
            }
        }

        /// <summary>Every byte after the fixed fields, whatever <see cref="DataSize"/> says.</summary>
        public ReadOnlySpan<byte> RawData => IsValid ? _payload.Slice(DataOffset) : ReadOnlySpan<byte>.Empty;

        /// <summary>Whether this is the last fragment of its content (<c>offset + data_size &gt;= size_total</c>).</summary>
        public bool IsLast => IsValid && (long)Offset + Math.Max(DataSize, 0) >= SizeTotal;
    }

    /// <summary>
    /// Builds C2S 0x04B (GP_CLI_COMMAND_FRAGMENTS, 24 bytes): the request for content the server sends in fragments.
    /// Payload: 0 u8 <c>Command</c>, 1 s8 <c>Result</c>, 2 u8 <c>value1</c>, 3 u8 <c>value2</c>, 4 s32 <c>timestamp</c>,
    /// 8 s32 <c>size_total</c>, 12 s32 <c>offset</c>, 16 s32 <c>data_size</c>.
    /// Packet structure referenced from XiPackets (https://github.com/atom0s/XiPackets/tree/main/world/client/0x004B); the
    /// size matches LandSandBoat's struct (https://github.com/LandSandBoat/server/blob/base/src/map/packets/c2s/0x04b_fragments.h),
    /// which answers kind 1 with the server message from <c>offset</c> and ignores a repeat of the same offset.
    /// </summary>
    public static class FragmentsOutboundPackets
    {
        public const int FragmentsSize = 24;

        /// <summary>The English language id of a server message request.</summary>
        public const byte LanguageEnglish = 2;

        /// <summary>Writes C2S 0x04B into <paramref name="destination"/> and returns its length.</summary>
        public static int BuildFragments(Span<byte> destination, byte command, FragmentsKind kind, byte value2,
            int timestamp, int sizeTotal, int offset, int dataSize, sbyte result = 0, ushort sequenceId = 0)
        {
            if (destination.Length < FragmentsSize)
                throw new ArgumentException($"Destination must be at least {FragmentsSize} bytes.", nameof(destination));

            destination.Slice(0, FragmentsSize).Clear();
            BinaryPrimitives.WriteUInt16LittleEndian(destination, (ushort)(0x04B | ((FragmentsSize / 4) << 9)));
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(2), sequenceId);
            destination[4] = command;
            destination[5] = unchecked((byte)result);
            destination[6] = (byte)kind;
            destination[7] = value2;
            BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(8), timestamp);
            BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(12), sizeTotal);
            BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(16), offset);
            BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(20), dataSize);
            return FragmentsSize;
        }

        /// <summary>
        /// C2S 0x04B asking for the server message from <paramref name="offset"/>: command 1 for the first fragment,
        /// 2 for a later one (with the first fragment's <paramref name="timestamp"/> and <paramref name="sizeTotal"/>).
        /// </summary>
        public static byte[] BuildServerMessageRequest(int offset = 0, int timestamp = 0, int sizeTotal = 0,
            byte language = LanguageEnglish, ushort sequenceId = 0)
        {
            byte[] packet = new byte[FragmentsSize];
            BuildFragments(packet, offset == 0 ? (byte)1 : (byte)2, FragmentsKind.ServerMessage, language,
                timestamp, sizeTotal, offset, 0, sequenceId: sequenceId);
            return packet;
        }
    }

    /// <summary>What <see cref="ServerMessageAssembler.Add"/> made of a fragment.</summary>
    public enum ServerMessageFragmentResult
    {
        /// <summary>Not part of the message being rebuilt (stale, out of order, or not a server message).</summary>
        Ignored,

        /// <summary>Added; the client asks for the next fragment.</summary>
        NeedMore,

        /// <summary>The message is whole.</summary>
        Complete,
    }

    /// <summary>
    /// Rebuilds the server message from its S2C 0x04D fragments, as the retail client does (XiPackets 0x004D): each
    /// fragment is copied in at its <c>offset</c>, a fragment with another timestamp restarts the message, and the text
    /// ends at the first null once <c>offset + data_size</c> reaches <c>size_total</c>. Not thread-safe (the network
    /// thread uses it).
    /// </summary>
    public sealed class ServerMessageAssembler
    {
        /// <summary>The largest message accepted; LandSandBoat's comes from one setting string.</summary>
        public const int MaxMessageSize = 16 * 1024;

        private byte[] _buffer = new byte[S2C_0x04D_Fragments.MaxDataSize];
        private int _timestamp;
        private int _sizeTotal;
        private int _received;
        private bool _active;

        /// <summary>The timestamp of the message being rebuilt (for the next request).</summary>
        public int Timestamp => _timestamp;

        /// <summary>The message's total size (for the next request).</summary>
        public int SizeTotal => _sizeTotal;

        /// <summary>How far the message is rebuilt: the offset to ask for next.</summary>
        public int NextOffset => _received;

        /// <summary>
        /// Adds a server message fragment: <see cref="ServerMessageFragmentResult.Complete"/> with the whole message
        /// (empty when the server has none) after the last fragment, <see cref="ServerMessageFragmentResult.NeedMore"/>
        /// while more are needed (ask for <see cref="NextOffset"/>), <see cref="ServerMessageFragmentResult.Ignored"/>
        /// for a fragment that does not belong to the message.
        /// </summary>
        public ServerMessageFragmentResult Add(in S2C_0x04D_Fragments fragment, out string? message)
        {
            message = null;
            if (!fragment.IsValid || fragment.Kind != FragmentsKind.ServerMessage) return ServerMessageFragmentResult.Ignored;
            if (fragment.SizeTotal <= 0 || fragment.SizeTotal > MaxMessageSize || fragment.Offset < 0)
            {
                // An empty server message (LandSandBoat leaves the sizes 0 when there is none).
                Reset();
                message = string.Empty;
                return ServerMessageFragmentResult.Complete;
            }

            if (fragment.Offset == 0)
            {
                _timestamp = fragment.Timestamp;
                _sizeTotal = fragment.SizeTotal;
                _received = 0;
                _active = true;
                if (_buffer.Length < _sizeTotal) _buffer = new byte[_sizeTotal];
            }
            else if (!_active || fragment.Timestamp != _timestamp || fragment.Offset != _received)
            {
                return ServerMessageFragmentResult.Ignored; // a stale or out-of-order fragment
            }

            var data = fragment.Data;
            int length = Math.Min(data.Length, _sizeTotal - fragment.Offset);
            if (length > 0) data.Slice(0, length).CopyTo(_buffer.AsSpan(fragment.Offset));
            _received = fragment.Offset + Math.Max(length, 0);
            // A fragment without data would make the next request repeat this offset (which LandSandBoat ignores), so
            // the message ends with what has arrived.
            if (length > 0 && _received < _sizeTotal) return ServerMessageFragmentResult.NeedMore;
            return Complete(out message);
        }

        private ServerMessageFragmentResult Complete(out string? message)
        {
            var text = _buffer.AsSpan(0, Math.Min(_received, _sizeTotal));
            int end = text.IndexOf((byte)0);
            if (end >= 0) text = text.Slice(0, end);
            message = Decode(text);
            Reset();
            return ServerMessageFragmentResult.Complete;
        }

        /// <summary>Forgets a partly rebuilt message.</summary>
        public void Reset()
        {
            _active = false;
            _received = 0;
            _sizeTotal = 0;
            _timestamp = 0;
        }

        private static string Decode(ReadOnlySpan<byte> text)
        {
            if (text.IsEmpty) return string.Empty;
            try
            {
                return Encoding.GetEncoding("shift_jis").GetString(text);
            }
            catch
            {
                return Encoding.UTF8.GetString(text);
            }
        }
    }
}
