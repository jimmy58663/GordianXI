// src/Gordian.Core/Network/Packets/LifecyclePackets.cs
using System;
using System.Buffers.Binary;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Gordian.Core.Config;
using Gordian.Core.Diagnostics;

namespace Gordian.Core.Network.Packets
{
    #region Enums

    /// <summary>
    /// Logout and Zone Transition states transmitted by the server in S2C 0x00B.
    /// Matches LandSandBoat GP_GAME_LOGOUT_STATE (https://github.com/LandSandBoat/server).
    /// </summary>
    public enum LogoutState : byte
    {
        None = 0,
        Logout = 1,
        ZoneChange = 2,
        MyRoom = 3,
        Cancel = 4,
        PolExit = 5,
        JobExit = 6,
        PolExitMyRoom = 7,
        Timeout = 8,
        GmLogout = 9,
        End = 10
    }

    /// <summary>
    /// World position update mode flags sent by server in S2C 0x05B / 0x065 (POSMODE).
    /// </summary>
    public enum PosMode : byte
    {
        Normal = 0x00,
        Event = 0x01,
        Clear = 0x02,
        Pop = 0x03,
        Reset = 0x05,
        Materialize = 0x06,
        Lock = 0x08,
        Unlock = 0x09,
        Rotate = 0x0A
    }

    /// <summary>
    /// Logout and Shutdown request mode in C2S 0x0E7 (GP_CLI_COMMAND_REQLOGOUT).
    /// </summary>
    public enum ReqLogoutMode : ushort
    {
        Toggle = 0x00,
        LogoutOn = 0x01,
        Off = 0x02,
        ShutdownOn = 0x03
    }

    /// <summary>
    /// Logout and Shutdown request kind in C2S 0x0E7.
    /// </summary>
    public enum ReqLogoutKind : ushort
    {
        Logout = 0x01,
        Shutdown = 0x03
    }

    /// <summary>
    /// Mog House city exit target bit in C2S 0x05E (GP_CLI_COMMAND_MAPRECT_MYROOMEXITBIT).
    /// </summary>
    public enum MogHouseExitBit : byte
    {
        Default = 0,
        SandOria = 1,
        Bastok = 2,
        Windurst = 3,
        Jeuno = 4,
        Whitegate = 5,
        RonfaureFront = 6,
        GustabergFront = 7,
        SarutaFront = 8,
        Adoulin = 9
    }

    /// <summary>
    /// Mog House exit mode selection in C2S 0x05E (GP_CLI_COMMAND_MAPRECT_MYROOMEXITMODE).
    /// </summary>
    public enum MogHouseExitMode : byte
    {
        AreaEnteredFrom = 0,
        Option1 = 1,
        Option2 = 2,
        Option3 = 3,
        Option4 = 4,
        Mog2F = 125,
        Mog1F = 126,
        MogGarden = 127
    }

    #endregion

    #region Inbound Decoders (readonly ref struct)

    /// <summary>
    /// S2C 0x00A (GP_SERV_LOGIN): Server Login Acknowledgment.
    /// Confirms character login and provides initial world coordinates, heading,
    /// character appearance table (GrapIDTbl[9]), and character name.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server).
    /// </summary>
    public readonly ref struct S2C_0x00A_LoginAck
    {
        public const ushort PacketId = 0x00A;

        private readonly ReadOnlySpan<byte> _payload;

        public uint UniqueNo { get; }
        public ushort ActorIndex { get; }
        public byte Direction { get; }
        public float X { get; }
        public float Y { get; }
        public float Z { get; }
        public ushort ZoneId { get; }
        public ushort WeatherNumber { get; }
        public bool IsValid { get; }

        public S2C_0x00A_LoginAck(ReadOnlySpan<byte> payload)
        {
            _payload = payload;
            if (payload.Length < 20)
            {
                UniqueNo = 0;
                ActorIndex = 0;
                Direction = 0;
                X = 0f;
                Y = 0f;
                Z = 0f;
                ZoneId = 0;
                WeatherNumber = 0;
                IsValid = false;
                return;
            }

            UniqueNo = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0, 4));
            ActorIndex = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(4, 2));
            Direction = payload[7];
            X = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(8, 4));
            // FFXI native wire convention: X at +8 (East/West), Elevation at +12, North/South at +16.
            // GordianXI 3D canonical coordinates (Y-up):
            // X = East(+)/West(-), Y = Elevation (Up(+)/Down(-)), Z = North(-)/South(+).
            Y = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(12, 4)); // Wire offset 12: Elevation -> 3D Y
            Z = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(16, 4)); // Wire offset 16: North/South -> 3D Z
            ZoneId = payload.Length >= 46
                ? BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(44, 2))
                : (ushort)0;
            WeatherNumber = payload.Length >= 102
                ? BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(100, 2))
                : (ushort)0;
            IsValid = true;
        }

        /// <summary>
        /// Reads the 9-element equipment/model visual appearance table (GrapIDTbl) if present.
        /// Slot indices: 0:Race/Face, 1:Head, 2:Body, 3:Hands, 4:Legs, 5:Feet, 6:Main, 7:Sub, 8:Ranged.
        /// In 0x00A payload, GrapIDTbl is located at offset 0x40 (64).
        /// </summary>
        public bool TryGetGrapIdTable(Span<ushort> destination)
        {
            if (destination.Length < 9) return false;
            const int grapOffset = 0x40;
            if (_payload.Length < grapOffset + 18) return false;

            for (int i = 0; i < 9; i++)
            {
                destination[i] = BinaryPrimitives.ReadUInt16LittleEndian(_payload.Slice(grapOffset + (i * 2), 2));
            }
            return true;
        }

        /// <summary>
        /// Reads character name ASCII string from the login packet if present.
        /// In 0x00A payload, name is located at offset 0x80 (128).
        /// </summary>
        public string GetName()
        {
            const int nameOffset = 0x80;
            if (_payload.Length < nameOffset + 1) return string.Empty;

            ReadOnlySpan<byte> nameSpan = _payload.Slice(nameOffset, Math.Min(16, _payload.Length - nameOffset));
            int len = 0;
            while (len < nameSpan.Length && nameSpan[len] != 0)
            {
                len++;
            }
            return len > 0 ? Encoding.ASCII.GetString(nameSpan.Slice(0, len)) : string.Empty;
        }
    }

    /// <summary>
    /// S2C 0x008 (GP_SERV_ENTERZONE): Server Zone Entrance confirmation.
    /// Contains the 48-byte table of zones previously entered by this character.
    /// </summary>
    public readonly ref struct S2C_0x008_EnterZone
    {
        public const ushort PacketId = 0x008;
        public const int EnterZoneTableSize = 48;

        public ReadOnlySpan<byte> EnterZoneTable { get; }
        public bool IsValid { get; }

        public S2C_0x008_EnterZone(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < EnterZoneTableSize)
            {
                EnterZoneTable = ReadOnlySpan<byte>.Empty;
                IsValid = false;
                return;
            }

            EnterZoneTable = payload.Slice(0, EnterZoneTableSize);
            IsValid = true;
        }

        public bool HasVisitedZone(ushort zoneId)
        {
            if (!IsValid || zoneId >= EnterZoneTableSize * 8) return false;
            int byteIndex = zoneId / 8;
            int bitIndex = zoneId % 8;
            return (EnterZoneTable[byteIndex] & (1 << bitIndex)) != 0;
        }
    }

    /// <summary>
    /// S2C 0x00B (GP_SERV_COMMAND_LOGOUT): Zone Transition & Logout Response.
    /// Transmits zone change directives, mog house entry, and target map server IP/Port.
    /// </summary>
    public readonly ref struct S2C_0x00B_Logout
    {
        public const ushort PacketId = 0x00B;

        public LogoutState State { get; }
        public uint TargetIpRaw { get; }
        public ushort TargetPort { get; }
        public uint ErrorCode { get; }
        public bool IsValid { get; }

        public S2C_0x00B_Logout(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < 12)
            {
                State = LogoutState.None;
                TargetIpRaw = 0;
                TargetPort = 0;
                ErrorCode = 0;
                IsValid = false;
                return;
            }

            State = (LogoutState)payload[0];

            int iwasakiOffset = payload.Length >= 20 ? 4 : 1;
            TargetIpRaw = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(iwasakiOffset, 4));
            uint portRaw = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(iwasakiOffset + 4, 4));
            TargetPort = (ushort)(portRaw & 0xFFFF);

            int errOffset = iwasakiOffset + 16;
            ErrorCode = payload.Length >= errOffset + 4
                ? BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(errOffset, 4))
                : 0;

            IsValid = true;
        }

        public IPAddress GetTargetIpAddress()
        {
            byte b1 = (byte)(TargetIpRaw & 0xFF);
            byte b2 = (byte)((TargetIpRaw >> 8) & 0xFF);
            byte b3 = (byte)((TargetIpRaw >> 16) & 0xFF);
            byte b4 = (byte)((TargetIpRaw >> 24) & 0xFF);
            return new IPAddress(new byte[] { b1, b2, b3, b4 });
        }
    }

    /// <summary>
    /// S2C 0x015 (GP_SERV_POS): Server Keepalive / Position Ping.
    /// </summary>
    public readonly ref struct S2C_0x015_PosPing
    {
        public const ushort PacketId = 0x015;
        public bool IsValid { get; }

        public S2C_0x015_PosPing(ReadOnlySpan<byte> payload)
        {
            IsValid = true;
        }
    }

    /// <summary>
    /// S2C 0x0EE: Server Feature Restrictions Packet. Carries a full 64-bit
    /// <see cref="FeatureRestrictions"/> bitmask so the server can restrict any combination
    /// of client capabilities without being constrained to a fixed set of named tiers.
    /// </summary>
    public readonly ref struct S2C_0x0EE_FeatureRestrictions
    {
        public const ushort PacketId = 0x0EE;
        public FeatureRestrictions Value { get; }
        public bool IsValid { get; }

        public S2C_0x0EE_FeatureRestrictions(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < 8)
            {
                Value = FeatureRestrictions.None;
                IsValid = false;
                return;
            }

            Value = (FeatureRestrictions)BinaryPrimitives.ReadUInt64LittleEndian(payload);
            IsValid = true;
        }
    }

    /// <summary>
    /// S2C 0x057 (GP_SERV_COMMAND_WEATHER): Server Weather Update.
    /// Informs the client of current zone weather condition and scheduled change time.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server).
    /// </summary>
    public readonly ref struct S2C_0x057_Weather
    {
        public const ushort PacketId = 0x057;

        public uint StartTime { get; }
        public ushort WeatherNumber { get; }
        public ushort WeatherOffsetTime { get; }
        public bool IsValid { get; }

        public S2C_0x057_Weather(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < 6)
            {
                StartTime = 0;
                WeatherNumber = 0;
                WeatherOffsetTime = 0;
                IsValid = false;
                return;
            }

            StartTime = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0, 4));
            WeatherNumber = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(4, 2));
            WeatherOffsetTime = payload.Length >= 8
                ? BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(6, 2))
                : (ushort)0;
            IsValid = true;
        }
    }

    /// <summary>
    /// S2C 0x05B (GP_SERV_COMMAND_WPOS): Entity World Position / Warp Update.
    /// Sent by the server to update an entity's position or trigger warp transitions.
    /// </summary>
    public readonly ref struct S2C_0x05B_WPos
    {
        public const ushort PacketId = 0x05B;

        public float X { get; }
        public float Y { get; }
        public float Z { get; }
        public uint UniqueNo { get; }
        public ushort ActorIndex { get; }
        public PosMode Mode { get; }
        public byte Direction { get; }
        public bool IsValid { get; }

        public S2C_0x05B_WPos(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < 20)
            {
                X = 0f;
                Y = 0f;
                Z = 0f;
                UniqueNo = 0;
                ActorIndex = 0;
                Mode = PosMode.Normal;
                Direction = 0;
                IsValid = false;
                return;
            }

            X = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(0, 4));
            Y = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(4, 4)); // Wire offset 4: Elevation -> 3D Y
            Z = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(8, 4)); // Wire offset 8: North/South -> 3D Z
            UniqueNo = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(12, 4));
            ActorIndex = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(16, 2));
            Mode = (PosMode)payload[18];
            Direction = payload[19];
            IsValid = true;
        }
    }

    /// <summary>
    /// S2C 0x065 (GP_SERV_COMMAND_WPOS2): Entity World Position / Reset Update (2).
    /// Sent by the server to reset or update position (e.g. after denied zoneline or zone entrance).
    /// </summary>
    public readonly ref struct S2C_0x065_WPos2
    {
        public const ushort PacketId = 0x065;

        public float X { get; }
        public float Y { get; }
        public float Z { get; }
        public uint UniqueNo { get; }
        public ushort ActorIndex { get; }
        public PosMode Mode { get; }
        public byte Direction { get; }
        public bool IsValid { get; }

        public S2C_0x065_WPos2(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < 20)
            {
                X = 0f;
                Y = 0f;
                Z = 0f;
                UniqueNo = 0;
                ActorIndex = 0;
                Mode = PosMode.Normal;
                Direction = 0;
                IsValid = false;
                return;
            }

            X = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(0, 4));
            Y = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(4, 4)); // Wire offset 4: Elevation -> 3D Y
            Z = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(8, 4)); // Wire offset 8: North/South -> 3D Z
            UniqueNo = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(12, 4));
            ActorIndex = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(16, 2));
            Mode = (PosMode)payload[18];
            Direction = payload[19];
            IsValid = true;
        }
    }

    #endregion

    #region Outbound Builders

    /// <summary>
    /// Static builder methods for lifecycle and connection client sub-packets.
    /// </summary>
    public static class LifecycleOutboundPackets
    {
        public const int GameOkSubPacketSize = 12;
        public const int NetEndSubPacketSize = 8;
        public const int PosSubPacketSize = 32;
        public const int MapRectSubPacketSize = 24;
        public const int EventEndSubPacketSize = 20;
        public const int ZoneTransitionSubPacketSize = 8;
        public const int EventEndXzySubPacketSize = 32;
        public const int ReqLogoutSubPacketSize = 8;

        public static void BuildGameOk(Span<byte> destination, ushort sequenceId = 0, uint clientState = 0, uint debugClientFlg = 0)
        {
            if (destination.Length < GameOkSubPacketSize)
                throw new ArgumentException($"Destination must be at least {GameOkSubPacketSize} bytes.", nameof(destination));

            destination.Slice(0, GameOkSubPacketSize).Clear();
            ushort headerWord = (ushort)(0x00C | (3 << 9));
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(0, 2), headerWord);
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(2, 2), sequenceId);
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(4, 4), clientState);
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(8, 4), debugClientFlg);
        }

        public static byte[] BuildGameOk(ushort sequenceId = 0, uint clientState = 0, uint debugClientFlg = 0)
        {
            byte[] packet = new byte[GameOkSubPacketSize];
            BuildGameOk(packet.AsSpan(), sequenceId, clientState, debugClientFlg);
            return packet;
        }

        public static void BuildNetEnd(Span<byte> destination, ushort sequenceId = 0, ushort state = 0)
        {
            if (destination.Length < NetEndSubPacketSize)
                throw new ArgumentException($"Destination must be at least {NetEndSubPacketSize} bytes.", nameof(destination));

            destination.Slice(0, NetEndSubPacketSize).Clear();
            ushort headerWord = (ushort)(0x00D | (2 << 9));
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(0, 2), headerWord);
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(2, 2), sequenceId);
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(4, 2), state);
        }

        public static byte[] BuildNetEnd(ushort sequenceId = 0, ushort state = 0)
        {
            byte[] packet = new byte[NetEndSubPacketSize];
            BuildNetEnd(packet.AsSpan(), sequenceId, state);
            return packet;
        }

        /// <summary>
        /// Builds the 32-byte GP_CLI_POS (0x015) position sub-packet into a destination span.
        /// </summary>
        /// <param name="moveFrame">Client locomotion animation frame counter (Run Count, accumulating frame counter when moving, 1 when stationary).</param>
        /// <param name="isWalking">True if character is walking rather than running (sets RunMode bit).</param>
        public static void BuildPos(
            Span<byte> destination,
            ushort sequenceId = 0,
            float x = 0f,
            float y = 0f,
            float z = 0f,
            byte dir = 0,
            ushort targetIndex = 0,
            ushort moveFrame = 0,
            bool isWalking = false)
        {
            if (destination.Length < PosSubPacketSize)
                throw new ArgumentException($"Destination must be at least {PosSubPacketSize} bytes.", nameof(destination));

            destination.Slice(0, PosSubPacketSize).Clear();
            ushort headerWord = (ushort)(0x015 | (8 << 9));
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(0, 2), headerWord);
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(2, 2), sequenceId);
            BinaryPrimitives.WriteSingleLittleEndian(destination.Slice(4, 4), x);
            BinaryPrimitives.WriteSingleLittleEndian(destination.Slice(8, 4), y); // Wire offset 8 is Elevation (3D Y)
            BinaryPrimitives.WriteSingleLittleEndian(destination.Slice(12, 4), z); // Wire offset 12 is North/South (3D Z)
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(16, 2), 0); // MovTime: Always 0 on retail FFXI protocol
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(18, 2), moveFrame); // MoveFlame / Run Count: accumulating frame counter when moving, 1 when stationary
            destination[20] = dir;
            byte modes = (byte)((targetIndex != 0 ? 0x01 : 0x00) | (isWalking ? 0x02 : 0x00));
            destination[21] = modes; // Bit 0 = TargetMode, Bit 1 = RunMode (0 = run, 1 = walk)
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(22, 2), targetIndex);
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(24, 4), (uint)Environment.TickCount);
        }

        public static byte[] BuildPos(
            ushort sequenceId = 0,
            float x = 0f,
            float y = 0f,
            float z = 0f,
            byte dir = 0,
            ushort targetIndex = 0,
            ushort moveFrame = 0,
            bool isWalking = false)
        {
            byte[] packet = new byte[PosSubPacketSize];
            BuildPos(packet.AsSpan(), sequenceId, x, y, z, dir, targetIndex, moveFrame, isWalking);
            return packet;
        }

        public static void BuildMapRect(
            Span<byte> destination,
            uint rectId,
            float x,
            float y,
            float z,
            ushort actorIndex = 0,
            byte myRoomExitBit = 0,
            byte myRoomExitMode = 0,
            ushort sequenceId = 0)
        {
            if (destination.Length < MapRectSubPacketSize)
                throw new ArgumentException($"Destination must be at least {MapRectSubPacketSize} bytes.", nameof(destination));

            destination.Slice(0, MapRectSubPacketSize).Clear();
            ushort headerWord = (ushort)(0x05E | (6 << 9));
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(0, 2), headerWord);
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(2, 2), sequenceId);
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(4, 4), rectId);
            BinaryPrimitives.WriteSingleLittleEndian(destination.Slice(8, 4), x);
            BinaryPrimitives.WriteSingleLittleEndian(destination.Slice(12, 4), y);
            BinaryPrimitives.WriteSingleLittleEndian(destination.Slice(16, 4), z);
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(20, 2), actorIndex);
            destination[22] = myRoomExitBit;
            destination[23] = myRoomExitMode;
        }

        public static byte[] BuildMapRect(
            uint rectId,
            float x,
            float y,
            float z,
            ushort actorIndex = 0,
            byte myRoomExitBit = 0,
            byte myRoomExitMode = 0,
            ushort sequenceId = 0)
        {
            byte[] packet = new byte[MapRectSubPacketSize];
            BuildMapRect(packet.AsSpan(), rectId, x, y, z, actorIndex, myRoomExitBit, myRoomExitMode, sequenceId);
            return packet;
        }

        public static void BuildEventEnd(
            Span<byte> destination,
            uint uniqueNo,
            uint endPara,
            ushort actIndex,
            ushort mode = 0,
            ushort eventNum = 0,
            ushort eventPara = 0,
            ushort sequenceId = 0)
        {
            if (destination.Length < EventEndSubPacketSize)
                throw new ArgumentException($"Destination must be at least {EventEndSubPacketSize} bytes.", nameof(destination));

            destination.Slice(0, EventEndSubPacketSize).Clear();
            ushort headerWord = (ushort)(0x05B | (5 << 9));
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(0, 2), headerWord);
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(2, 2), sequenceId);
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(4, 4), uniqueNo);
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(8, 4), endPara);
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(12, 2), actIndex);
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(14, 2), mode);
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(16, 2), eventNum);
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(18, 2), eventPara);
        }

        public static byte[] BuildEventEnd(
            uint uniqueNo,
            uint endPara,
            ushort actIndex,
            ushort mode = 0,
            ushort eventNum = 0,
            ushort eventPara = 0,
            ushort sequenceId = 0)
        {
            byte[] packet = new byte[EventEndSubPacketSize];
            BuildEventEnd(packet.AsSpan(), uniqueNo, endPara, actIndex, mode, eventNum, eventPara, sequenceId);
            return packet;
        }

        public static uint MakeFourCc(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return 0;
            Span<byte> bytes = stackalloc byte[4];
            int written = Encoding.ASCII.GetBytes(tag.AsSpan(0, Math.Min(4, tag.Length)), bytes);
            if (written < 4)
            {
                bytes.Slice(written).Clear();
            }
            return BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        }

        public static void BuildMapRect(
            Span<byte> destination,
            string rectTag,
            float x,
            float y,
            float z,
            ushort actorIndex = 0,
            MogHouseExitBit myRoomExitBit = MogHouseExitBit.Default,
            MogHouseExitMode myRoomExitMode = MogHouseExitMode.AreaEnteredFrom,
            ushort sequenceId = 0)
        {
            BuildMapRect(destination, MakeFourCc(rectTag), x, y, z, actorIndex, (byte)myRoomExitBit, (byte)myRoomExitMode, sequenceId);
        }

        public static byte[] BuildMapRect(
            string rectTag,
            float x,
            float y,
            float z,
            ushort actorIndex = 0,
            MogHouseExitBit myRoomExitBit = MogHouseExitBit.Default,
            MogHouseExitMode myRoomExitMode = MogHouseExitMode.AreaEnteredFrom,
            ushort sequenceId = 0)
        {
            byte[] packet = new byte[MapRectSubPacketSize];
            BuildMapRect(packet.AsSpan(), rectTag, x, y, z, actorIndex, myRoomExitBit, myRoomExitMode, sequenceId);
            return packet;
        }

        public static void BuildZoneTransition(
            Span<byte> destination,
            byte unknown00 = 2,
            byte unknown01 = 0,
            ushort sequenceId = 0)
        {
            if (destination.Length < ZoneTransitionSubPacketSize)
                throw new ArgumentException($"Destination must be at least {ZoneTransitionSubPacketSize} bytes.", nameof(destination));

            destination.Slice(0, ZoneTransitionSubPacketSize).Clear();
            ushort headerWord = (ushort)(0x011 | (2 << 9));
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(0, 2), headerWord);
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(2, 2), sequenceId);
            destination[4] = unknown00;
            destination[5] = unknown01;
            // destination[6..7] remains 0
        }

        public static byte[] BuildZoneTransition(
            byte unknown00 = 2,
            byte unknown01 = 0,
            ushort sequenceId = 0)
        {
            byte[] packet = new byte[ZoneTransitionSubPacketSize];
            BuildZoneTransition(packet.AsSpan(), unknown00, unknown01, sequenceId);
            return packet;
        }

        public static void BuildEventEndXzy(
            Span<byte> destination,
            float x,
            float y,
            float z,
            uint uniqueNo,
            uint endPara,
            ushort actIndex,
            byte mode = 0,
            sbyte dir = 0,
            ushort eventNum = 0,
            ushort eventPara = 0,
            ushort sequenceId = 0)
        {
            if (destination.Length < EventEndXzySubPacketSize)
                throw new ArgumentException($"Destination must be at least {EventEndXzySubPacketSize} bytes.", nameof(destination));

            destination.Slice(0, EventEndXzySubPacketSize).Clear();
            ushort headerWord = (ushort)(0x05C | (8 << 9));
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(0, 2), headerWord);
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(2, 2), sequenceId);
            BinaryPrimitives.WriteSingleLittleEndian(destination.Slice(4, 4), x);
            BinaryPrimitives.WriteSingleLittleEndian(destination.Slice(8, 4), y);
            BinaryPrimitives.WriteSingleLittleEndian(destination.Slice(12, 4), z);
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(16, 4), uniqueNo);
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(20, 4), endPara);
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(24, 2), eventNum);
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(26, 2), eventPara);
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(28, 2), actIndex);
            destination[30] = mode;
            destination[31] = (byte)dir;
        }

        public static byte[] BuildEventEndXzy(
            float x,
            float y,
            float z,
            uint uniqueNo,
            uint endPara,
            ushort actIndex,
            byte mode = 0,
            sbyte dir = 0,
            ushort eventNum = 0,
            ushort eventPara = 0,
            ushort sequenceId = 0)
        {
            byte[] packet = new byte[EventEndXzySubPacketSize];
            BuildEventEndXzy(packet.AsSpan(), x, y, z, uniqueNo, endPara, actIndex, mode, dir, eventNum, eventPara, sequenceId);
            return packet;
        }

        public static void BuildReqLogout(
            Span<byte> destination,
            ReqLogoutMode mode = ReqLogoutMode.LogoutOn,
            ReqLogoutKind kind = ReqLogoutKind.Logout,
            ushort sequenceId = 0)
        {
            if (destination.Length < ReqLogoutSubPacketSize)
                throw new ArgumentException($"Destination must be at least {ReqLogoutSubPacketSize} bytes.", nameof(destination));

            destination.Slice(0, ReqLogoutSubPacketSize).Clear();
            ushort headerWord = (ushort)(0x0E7 | (2 << 9));
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(0, 2), headerWord);
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(2, 2), sequenceId);
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(4, 2), (ushort)mode);
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(6, 2), (ushort)kind);
        }

        public static byte[] BuildReqLogout(
            ReqLogoutMode mode = ReqLogoutMode.LogoutOn,
            ReqLogoutKind kind = ReqLogoutKind.Logout,
            ushort sequenceId = 0)
        {
            byte[] packet = new byte[ReqLogoutSubPacketSize];
            BuildReqLogout(packet.AsSpan(), mode, kind, sequenceId);
            return packet;
        }
    }

    #endregion

    #region Lifecycle Domain Module

    /// <summary>
    /// Encapsulates handling of handshake, connection lifecycle, and zone transition packets
    /// (0x00A, 0x008, 0x00B, 0x015, 0x0EE).
    /// </summary>
    public sealed class LifecyclePacketModule
    {
        private readonly SessionProfile _profile;
        private readonly Func<ReadOnlyMemory<byte>, bool, Task> _sendChunkCallback;
        private readonly Action<PacketDirection, ushort, ushort, ReadOnlySpan<byte>>? _logPacketCallback;

        public event Action? HandshakeCompleted;
        public event Action<float, float, float, byte, ushort>? PlayerPositionUpdated;
        public event Action<ushort>? ZoneReceived;
        public event Action<ushort>? WeatherReceived;
        public event Action<LogoutState, IPAddress, ushort, uint>? ZoneTransitionReceived;
        public event Action<uint, ushort[], string>? LoginAppearanceReceived;

        /// <summary>
        /// Optional delegate to retrieve the player's current position, heading, and locomotion state when answering server 0x015 PosPing.
        /// Returns (X, Y [Elevation], Z [North/South], Dir, TargetIndex, MoveFrame, IsWalking).
        /// </summary>
        public Func<(float X, float Y, float Z, byte Dir, ushort TargetIndex, ushort MoveFrame, bool IsWalking)>? PositionProvider { get; set; }

        public bool LogOutboundOnRoute { get; set; } = true;

        public LifecyclePacketModule(
            SessionProfile profile,
            Func<ReadOnlyMemory<byte>, bool, Task> sendChunkCallback,
            Action<PacketDirection, ushort, ushort, ReadOnlySpan<byte>>? logPacketCallback = null)
        {
            _profile = profile ?? throw new ArgumentNullException(nameof(profile));
            _sendChunkCallback = sendChunkCallback ?? throw new ArgumentNullException(nameof(sendChunkCallback));
            _logPacketCallback = logPacketCallback;
        }

        public void Register(IPacketDispatcher dispatcher)
        {
            ArgumentNullException.ThrowIfNull(dispatcher);
            dispatcher.Register(S2C_0x00A_LoginAck.PacketId, HandleLoginAck);
            dispatcher.Register(S2C_0x008_EnterZone.PacketId, HandleEnterZone);
            dispatcher.Register(S2C_0x00B_Logout.PacketId, HandleLogout);
            dispatcher.Register(S2C_0x015_PosPing.PacketId, HandlePosPing);
            dispatcher.Register(S2C_0x057_Weather.PacketId, HandleWeather);
            dispatcher.Register(S2C_0x05B_WPos.PacketId, HandleWPos);
            dispatcher.Register(S2C_0x065_WPos2.PacketId, HandleWPos2);
            dispatcher.Register(S2C_0x0EE_FeatureRestrictions.PacketId, HandleFeatureRestrictions);
        }

        private void HandleLoginAck(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var ack = new S2C_0x00A_LoginAck(payload);
            if (ack.IsValid)
            {
                Span<ushort> grap = stackalloc ushort[9];
                if (ack.TryGetGrapIdTable(grap))
                {
                    string name = ack.GetName();
                    LoginAppearanceReceived?.Invoke(ack.UniqueNo, grap.ToArray(), name);
                }

                GordianLog.Debug("LIFECYCLE", $"Extracted player initial position: X={ack.X:F2}, Y={ack.Y:F2}, Z={ack.Z:F2}, Dir={ack.Direction}, ActIndex={ack.ActorIndex}, ZoneId={ack.ZoneId}, Weather={ack.WeatherNumber}");
                PlayerPositionUpdated?.Invoke(ack.X, ack.Y, ack.Z, ack.Direction, ack.ActorIndex);
                if (ack.ZoneId != 0)
                {
                    ZoneReceived?.Invoke(ack.ZoneId);
                }
                if (ack.WeatherNumber != 0)
                {
                    WeatherReceived?.Invoke(ack.WeatherNumber);
                }
            }

            byte[] gameOk = LifecycleOutboundPackets.BuildGameOk(sequenceId: 0);
            if (LogOutboundOnRoute)
            {
                _logPacketCallback?.Invoke(PacketDirection.Outbound, 0x00C, 0, gameOk);
            }
            _ = _sendChunkCallback(gameOk, true);
        }

        private void HandleEnterZone(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var enterZone = new S2C_0x008_EnterZone(payload);
            GordianLog.Debug("LIFECYCLE", $"Received GP_SERV_ENTERZONE (0x008). Table valid={enterZone.IsValid}");

            // 1. Send 0x00D (NetEnd)
            byte[] netEnd = LifecycleOutboundPackets.BuildNetEnd(sequenceId: 0);
            if (LogOutboundOnRoute)
            {
                _logPacketCallback?.Invoke(PacketDirection.Outbound, 0x00D, 0, netEnd);
            }
            _ = _sendChunkCallback(netEnd, true);

            // 2. Send 0x011 (ZoneTransition confirmation matching retail/LSB protocol)
            byte[] zoneTransition = LifecycleOutboundPackets.BuildZoneTransition(sequenceId: 0);
            if (LogOutboundOnRoute)
            {
                _logPacketCallback?.Invoke(PacketDirection.Outbound, 0x011, 0, zoneTransition);
            }
            _ = _sendChunkCallback(zoneTransition, true);

            // 3. Send 0x061 (CliStatus request to receive GroupAttr, CliStatus, and CliStatus2 from server)
            byte[] cliStatus = EntityOutboundPackets.BuildCliStatus();
            if (LogOutboundOnRoute)
            {
                _logPacketCallback?.Invoke(PacketDirection.Outbound, 0x061, 0, cliStatus);
            }
            _ = _sendChunkCallback(cliStatus, true);

            HandshakeCompleted?.Invoke();
        }

        private void HandleLogout(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var logout = new S2C_0x00B_Logout(payload);
            if (!logout.IsValid)
            {
                GordianLog.Warning("LIFECYCLE", "Received invalid 0x00B logout packet payload.");
                return;
            }

            IPAddress targetIp = logout.GetTargetIpAddress();
            GordianLog.Info("LIFECYCLE", $"Received GP_SERV_COMMAND_LOGOUT (0x00B): State={logout.State}, Target={targetIp}:{logout.TargetPort}, Err={logout.ErrorCode}");
            ZoneTransitionReceived?.Invoke(logout.State, targetIp, logout.TargetPort, logout.ErrorCode);
        }

        private void HandleWeather(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var weather = new S2C_0x057_Weather(payload);
            if (weather.IsValid)
            {
                GordianLog.Debug("LIFECYCLE", $"Received GP_SERV_COMMAND_WEATHER (0x057): Weather={weather.WeatherNumber}, Offset={weather.WeatherOffsetTime}, StartTime={weather.StartTime}");
                WeatherReceived?.Invoke(weather.WeatherNumber);
            }
        }

        private void HandleWPos(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var wpos = new S2C_0x05B_WPos(payload);
            if (!wpos.IsValid) return;

            GordianLog.Debug("LIFECYCLE", $"Received GP_SERV_COMMAND_WPOS (0x05B): X={wpos.X:F2}, Y={wpos.Y:F2}, Z={wpos.Z:F2}, Dir={wpos.Direction}, ActIndex={wpos.ActorIndex}, Mode={wpos.Mode}");
            PlayerPositionUpdated?.Invoke(wpos.X, wpos.Y, wpos.Z, wpos.Direction, wpos.ActorIndex);
        }

        private void HandleWPos2(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var wpos = new S2C_0x065_WPos2(payload);
            if (!wpos.IsValid) return;

            GordianLog.Debug("LIFECYCLE", $"Received GP_SERV_COMMAND_WPOS2 (0x065): X={wpos.X:F2}, Y={wpos.Y:F2}, Z={wpos.Z:F2}, Dir={wpos.Direction}, ActIndex={wpos.ActorIndex}, Mode={wpos.Mode}");
            PlayerPositionUpdated?.Invoke(wpos.X, wpos.Y, wpos.Z, wpos.Direction, wpos.ActorIndex);
        }

        private void HandlePosPing(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var (x, y, z, dir, targetIdx, moveFrame, isWalking) = PositionProvider?.Invoke() ?? (0f, 0f, 0f, (byte)0, (ushort)0, (ushort)1, false);
            byte[] posPong = LifecycleOutboundPackets.BuildPos(
                sequenceId: header.SequenceId,
                x: x,
                y: y,
                z: z,
                dir: dir,
                targetIndex: targetIdx,
                moveFrame: moveFrame,
                isWalking: isWalking);

            if (LogOutboundOnRoute)
            {
                _logPacketCallback?.Invoke(PacketDirection.Outbound, 0x015, header.SequenceId, posPong);
            }
            _ = _sendChunkCallback(posPong, true);
        }

        private void HandleFeatureRestrictions(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var restrictions = new S2C_0x0EE_FeatureRestrictions(payload);
            if (restrictions.IsValid)
            {
                _profile.FeatureRestrictions = restrictions.Value;
                GordianLog.Info("LIFECYCLE", $"Server feature restrictions updated to: {restrictions.Value}");
            }
        }

        public async Task RequestZoneChangeAsync(uint rectId, float x, float y, float z, ushort actorIndex = 0)
        {
            byte[] packet = LifecycleOutboundPackets.BuildMapRect(rectId, x, y, z, actorIndex);
            if (LogOutboundOnRoute)
            {
                _logPacketCallback?.Invoke(PacketDirection.Outbound, 0x05E, 0, packet);
            }
            await _sendChunkCallback(packet, true).ConfigureAwait(false);
        }

        public Task RequestZoneChangeAsync(string rectTag, float x, float y, float z, ushort actorIndex = 0)
        {
            return RequestZoneChangeAsync(LifecycleOutboundPackets.MakeFourCc(rectTag), x, y, z, actorIndex);
        }

        public async Task RequestMogHouseExitAsync(MogHouseExitBit exitBit, MogHouseExitMode exitMode, float x = 0f, float y = 0f, float z = 0f, ushort actorIndex = 0)
        {
            byte[] packet = LifecycleOutboundPackets.BuildMapRect("zmrq", x, y, z, actorIndex, exitBit, exitMode);
            if (LogOutboundOnRoute)
            {
                _logPacketCallback?.Invoke(PacketDirection.Outbound, 0x05E, 0, packet);
            }
            await _sendChunkCallback(packet, true).ConfigureAwait(false);
        }

        public async Task RequestLogoutAsync(ReqLogoutMode mode = ReqLogoutMode.LogoutOn, ReqLogoutKind kind = ReqLogoutKind.Logout)
        {
            byte[] packet = LifecycleOutboundPackets.BuildReqLogout(mode, kind);
            if (LogOutboundOnRoute)
            {
                _logPacketCallback?.Invoke(PacketDirection.Outbound, 0x0E7, 0, packet);
            }
            await _sendChunkCallback(packet, true).ConfigureAwait(false);
        }
    }

    #endregion
}
