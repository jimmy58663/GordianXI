// src/Gordian.Core/Network/Packets/PlayerCommandPackets.cs
using System;
using System.Buffers.Binary;
using System.Text;

namespace Gordian.Core.Network.Packets
{
    /// <summary>
    /// The <c>Mode</c> of C2S 0x0E8 (<c>/heal</c>), 0x0EA (<c>/sit</c>) and 0x113 (<c>/sitchair</c>).
    /// Values referenced from XiPackets (https://github.com/atom0s/XiPackets), <c>world/client/0x00E8</c>, <c>0x00EA</c>
    /// and <c>0x0113</c>.
    /// </summary>
    public enum RestMode : uint
    {
        Toggle = 0,
        On = 1,
        Off = 2
    }

    /// <summary>
    /// The chat scope (<c>Kind</c>) of a proposal, C2S 0x0A0 and S2C 0x078 / 0x079. Linkshell 3 (4) only appears in the
    /// server packets; the client cannot make a proposal in it.
    /// Values referenced from XiPackets (https://github.com/atom0s/XiPackets), <c>world/client/0x00A0</c> and
    /// <c>world/server/0x0078</c>; LandSandBoat accepts the five client kinds (<c>0x0a0_switch_proposal.h</c>).
    /// </summary>
    public enum ProposalKind : byte
    {
        Party = 1,
        Linkshell1 = 2,
        Linkshell2 = 3,
        Linkshell3 = 4,
        Say = 5,
        Shout = 6
    }

    /// <summary>
    /// The <c>State</c> of S2C 0x0F6: the response to a wide scan request.
    /// Values referenced from XiPackets (https://github.com/atom0s/XiPackets), <c>world/server/0x00F6</c>.
    /// </summary>
    public enum TrackingListState : uint
    {
        None = 0,
        ListStart = 1,
        ListEnd = 2,
        End = 3,
        Error = 0x0A
    }

    /// <summary>
    /// The <c>State</c> of S2C 0x0F5: the tracked entity's position update.
    /// Values referenced from XiPackets (https://github.com/atom0s/XiPackets), <c>world/server/0x00F5</c>.
    /// </summary>
    public enum TrackingPosState : uint
    {
        None = 0,
        Start = 1,
        Lose = 2,
        End = 3
    }

    /// <summary>
    /// S2C 0x078 (GP_SERV_COMMAND_SWITCH_START): a player made a proposal (<c>/nominate</c>, <c>/propose</c>). Payload
    /// offsets (packet offset minus 4): 0 u32 proposer id, 4 u32 <c>AllNum</c> (eligible voters; the client ignores it),
    /// 8 u16 proposer index, 10 <c>sName[15]</c>, 25 <c>Kind</c>, 26 <c>Str</c>. <c>Str</c> is <c>[question]</c>, then
    /// <c>\n</c> and <c>n:option</c> per option; LandSandBoat sizes the packet to the string.
    /// Packet structure referenced from XiPackets (https://github.com/atom0s/XiPackets), <c>world/server/0x0078</c>;
    /// server side referenced from LandSandBoat (https://github.com/LandSandBoat/server), <c>s2c/0x078_switch_start.cpp</c>.
    /// </summary>
    public readonly ref struct S2C_0x078_SwitchStart
    {
        public const ushort PacketId = 0x078;
        public const int MinPayloadLength = 26;

        public bool IsValid { get; }
        public uint ProposerId { get; }
        public uint AllNum { get; }
        public ushort ProposerIndex { get; }
        public string ProposerName { get; }
        public ProposalKind Kind { get; }

        /// <summary>The question and options text, with the brackets and line breaks of the wire format.</summary>
        public string Text { get; }

        public S2C_0x078_SwitchStart(ReadOnlySpan<byte> payload)
        {
            this = default;
            ProposerName = string.Empty;
            Text = string.Empty;
            if (payload.Length < MinPayloadLength) return;

            ProposerId = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0, 4));
            AllNum = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(4, 4));
            ProposerIndex = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(8, 2));
            ProposerName = PlayerCommandText.ReadString(payload.Slice(10, 15));
            Kind = (ProposalKind)payload[25];
            Text = PlayerCommandText.ReadString(payload.Slice(26));
            IsValid = true;
        }
    }

    /// <summary>
    /// S2C 0x079 (GP_SERV_COMMAND_SWITCH_PROC): the tally of a proposal, after each vote (<see cref="Closed"/> false, a
    /// fixed 0x30 byte packet with no text) and once at the end (<see cref="Closed"/> true, with the question and every
    /// option's final tally). Payload offsets: 0 u32 <c>AllNum</c>, 4 u16 <c>VoteNumTbl[9]</c> (entry 0 unused), 22 <c>Kind</c>,
    /// 23 <c>State</c> (0 live, 2 closed), 24 <c>QuestionNum</c> (options + 1), 25 <c>sPropName[15]</c>, 40 <c>Str</c>
    /// (<c>[question]</c>, then <c>n[votes]:option</c> per option, separated by <c>\n</c>).
    /// Packet structure referenced from XiPackets (https://github.com/atom0s/XiPackets), <c>world/server/0x0079</c>;
    /// server side referenced from LandSandBoat (https://github.com/LandSandBoat/server), <c>s2c/0x079_switch_proc.cpp</c>.
    /// </summary>
    public readonly ref struct S2C_0x079_SwitchProc
    {
        public const ushort PacketId = 0x079;
        public const int MinPayloadLength = 40;
        public const int VoteSlots = 9;

        public bool IsValid { get; }
        public uint AllNum { get; }
        public ProposalKind Kind { get; }
        public byte State { get; }
        public byte QuestionNum { get; }
        public string ProposerName { get; }
        public string Text { get; }

        private readonly ReadOnlySpan<byte> _votes;

        /// <summary>True when the proposal ended (<c>State</c> 2).</summary>
        public bool Closed => State == 2;

        /// <summary>The votes for option <paramref name="option"/> (1 to 8).</summary>
        public ushort GetVotes(int option)
            => option is < 1 or >= VoteSlots || _votes.Length < VoteSlots * 2
                ? (ushort)0
                : BinaryPrimitives.ReadUInt16LittleEndian(_votes.Slice(option * 2, 2));

        public S2C_0x079_SwitchProc(ReadOnlySpan<byte> payload)
        {
            this = default;
            ProposerName = string.Empty;
            Text = string.Empty;
            if (payload.Length < MinPayloadLength) return;

            AllNum = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0, 4));
            _votes = payload.Slice(4, VoteSlots * 2);
            Kind = (ProposalKind)payload[22];
            State = payload[23];
            QuestionNum = payload[24];
            ProposerName = PlayerCommandText.ReadString(payload.Slice(25, 15));
            Text = PlayerCommandText.ReadString(payload.Slice(40));
            IsValid = true;
        }
    }

    /// <summary>
    /// S2C 0x0F4 (GP_SERV_COMMAND_TRACKING_LIST): one wide scan entry. Payload offsets: 0 u32 <c>ActIndex:16 | Level:8 |
    /// Type:3 | unused:5</c>, 4 i16 x, 6 i16 z (both the difference from the player), 8 <c>sName[16]</c>. <c>Type</c> colours
    /// the dot: 0 blue (a player; hidden unless <c>sName</c> is set), 1 green (NPC), 2 red (monster). LandSandBoat does
    /// not fill the name or the level of NPCs (<c>0x0f4_tracking_list.cpp</c>).
    /// Packet structure referenced from XiPackets (https://github.com/atom0s/XiPackets), <c>world/server/0x00F4</c>.
    /// </summary>
    public readonly ref struct S2C_0x0F4_TrackingList
    {
        public const ushort PacketId = 0x0F4;
        public const int MinPayloadLength = 24;

        public bool IsValid { get; }
        public ushort ActIndex { get; }
        public byte Level { get; }
        public byte Type { get; }
        public short DeltaX { get; }
        public short DeltaZ { get; }
        public string Name { get; }

        public S2C_0x0F4_TrackingList(ReadOnlySpan<byte> payload)
        {
            this = default;
            Name = string.Empty;
            if (payload.Length < MinPayloadLength) return;

            uint packed = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0, 4));
            ActIndex = (ushort)(packed & 0xFFFF);
            Level = (byte)((packed >> 16) & 0xFF);
            Type = (byte)((packed >> 24) & 0x07);
            DeltaX = BinaryPrimitives.ReadInt16LittleEndian(payload.Slice(4, 2));
            DeltaZ = BinaryPrimitives.ReadInt16LittleEndian(payload.Slice(6, 2));
            Name = PlayerCommandText.ReadString(payload.Slice(8, 16));
            IsValid = true;
        }
    }

    /// <summary>
    /// S2C 0x0F5 (GP_SERV_COMMAND_TRACKING_POS): the position of the entity being tracked (<c>C2S 0x0F5</c>). Payload
    /// offsets: 0 f32 x, 4 f32 y, 8 f32 z, 12 u8 level, 14 u16 <c>ActIndex</c>, 16 u32 state. LandSandBoat sends the
    /// entity's own coordinates (y is height, as everywhere in the client) and level 1.
    /// Packet structure referenced from XiPackets (https://github.com/atom0s/XiPackets), <c>world/server/0x00F5</c>.
    /// </summary>
    public readonly ref struct S2C_0x0F5_TrackingPos
    {
        public const ushort PacketId = 0x0F5;
        public const int MinPayloadLength = 20;

        public bool IsValid { get; }
        public float X { get; }
        public float Y { get; }
        public float Z { get; }
        public byte Level { get; }
        public ushort ActIndex { get; }
        public TrackingPosState State { get; }

        public S2C_0x0F5_TrackingPos(ReadOnlySpan<byte> payload)
        {
            this = default;
            if (payload.Length < MinPayloadLength) return;

            X = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(0, 4));
            Y = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(4, 4));
            Z = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(8, 4));
            Level = payload[12];
            ActIndex = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(14, 2));
            State = (TrackingPosState)BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(16, 4));
            IsValid = true;
        }
    }

    /// <summary>
    /// S2C 0x0F6 (GP_SERV_COMMAND_TRACKING_STATE): the start (1) and end (2) of a wide scan list, or an error (0x0A).
    /// Payload: 0 u32 state.
    /// Packet structure referenced from XiPackets (https://github.com/atom0s/XiPackets), <c>world/server/0x00F6</c>.
    /// </summary>
    public readonly ref struct S2C_0x0F6_TrackingState
    {
        public const ushort PacketId = 0x0F6;
        public const int MinPayloadLength = 4;

        public bool IsValid { get; }
        public TrackingListState State { get; }

        public S2C_0x0F6_TrackingState(ReadOnlySpan<byte> payload)
        {
            this = default;
            if (payload.Length < MinPayloadLength) return;
            State = (TrackingListState)BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0, 4));
            IsValid = true;
        }
    }

    /// <summary>
    /// S2C 0x11A (GP_SERV_COMMAND_EMOTE_LIST): the unlocked job emotes (<c>/jobemote</c>) and chairs (<c>/sitchair</c>),
    /// the answer to C2S 0x119. Payload: 0 u32 job emote bits (bit 0 WAR ... bit 21 RUN), 4 u16 chair bits (bit 0 chair 1 ...
    /// bit 10 chair 11).
    /// Packet structure referenced from XiPackets (https://github.com/atom0s/XiPackets), <c>world/server/0x011A</c>;
    /// server side referenced from LandSandBoat (https://github.com/LandSandBoat/server), <c>s2c/0x11a_emote_list.cpp</c>.
    /// </summary>
    public readonly ref struct S2C_0x11A_EmoteList
    {
        public const ushort PacketId = 0x11A;
        public const int MinPayloadLength = 6;

        public bool IsValid { get; }
        public uint JobEmotes { get; }
        public ushort Chairs { get; }

        public S2C_0x11A_EmoteList(ReadOnlySpan<byte> payload)
        {
            this = default;
            if (payload.Length < MinPayloadLength) return;
            JobEmotes = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0, 4));
            Chairs = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(4, 2));
            IsValid = true;
        }
    }

    /// <summary>
    /// S2C 0x11E (GP_SERV_COMMAND_JUMP): another player used <c>/jump</c>. Payload: 0 u16 <c>ActIndex</c> of the jumper.
    /// Packet structure referenced from XiPackets (https://github.com/atom0s/XiPackets), <c>world/server/0x011E</c>;
    /// server side referenced from LandSandBoat (https://github.com/LandSandBoat/server), <c>s2c/0x11e_jump.cpp</c>.
    /// </summary>
    public readonly ref struct S2C_0x11E_Jump
    {
        public const ushort PacketId = 0x11E;
        public const int MinPayloadLength = 2;

        public bool IsValid { get; }
        public ushort ActIndex { get; }

        public S2C_0x11E_Jump(ReadOnlySpan<byte> payload)
        {
            this = default;
            if (payload.Length < MinPayloadLength) return;
            ActIndex = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(0, 2));
            IsValid = true;
        }
    }

    internal static class PlayerCommandText
    {
        /// <summary>Reads a NUL terminated string from a field or the rest of a payload (UTF-8, which covers the ASCII names the server sends).</summary>
        public static string ReadString(ReadOnlySpan<byte> field)
        {
            int end = field.IndexOf((byte)0);
            if (end < 0) end = field.Length;
            return Encoding.UTF8.GetString(field.Slice(0, end));
        }
    }

    /// <summary>
    /// Builders for the everyday command packets: <c>/heal</c>, <c>/sit</c>, <c>/sitchair</c>, <c>/random</c>, proposals
    /// and votes, wide scan, the emote list request and effect end. Every size is the one LandSandBoat's
    /// <c>ValidatedPacketHandler</c> expects (the struct rounded up to 4 bytes); a different size is silently dropped.
    /// </summary>
    public static class PlayerCommandPacketBuilder
    {
        /// <summary>The longest question and options text LandSandBoat reads (<c>Str[128]</c>, NUL terminated).</summary>
        public const int MaxProposalTextBytes = 127;

        /// <summary>The longest proposer name a vote carries (<c>Name[15]</c> in LandSandBoat).</summary>
        public const int MaxVoteNameBytes = 14;

        private static byte[] BuildWords(ushort opcode, int wordCount, ushort sequenceId)
        {
            var packet = new byte[wordCount * 4];
            PacketHeader.Write(packet, opcode, (ushort)wordCount, sequenceId);
            return packet;
        }

        /// <summary>
        /// Builds C2S 0x0E8 (GP_CLI_COMMAND_CAMP, 8 bytes): <c>/heal</c>. LandSandBoat refuses it in an event, while dead,
        /// crafting, engaged or under an abnormal status, and when the request does not change the state (on while healing).
        /// Packet structure referenced from XiPackets (https://github.com/atom0s/XiPackets), <c>world/client/0x00E8</c>;
        /// server side referenced from LandSandBoat (https://github.com/LandSandBoat/server), <c>c2s/0x0e8_camp.cpp</c>.
        /// </summary>
        public static byte[] BuildCamp(RestMode mode, ushort sequenceId = 0)
        {
            var packet = BuildWords(0x0E8, 2, sequenceId);
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4, 4), (uint)mode);
            return packet;
        }

        /// <summary>
        /// Builds C2S 0x0EA (GP_CLI_COMMAND_SIT, 8 bytes): <c>/sit</c>. It also cancels healing.
        /// Packet structure referenced from XiPackets (https://github.com/atom0s/XiPackets), <c>world/client/0x00EA</c>;
        /// server side referenced from LandSandBoat (https://github.com/LandSandBoat/server), <c>c2s/0x0ea_sit.cpp</c>.
        /// </summary>
        public static byte[] BuildSit(RestMode mode, ushort sequenceId = 0)
        {
            var packet = BuildWords(0x0EA, 2, sequenceId);
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4, 4), (uint)mode);
            return packet;
        }

        /// <summary>
        /// Builds C2S 0x113 (<c>/sitchair</c>, 12 bytes): <paramref name="chairId"/> 0 is the plain chair, 1 and up the
        /// unlocked chairs; LandSandBoat accepts 0 to 20 and falls back to chair 0 for a key item the player lacks.
        /// Packet structure referenced from XiPackets (https://github.com/atom0s/XiPackets), <c>world/client/0x0113</c>;
        /// server side referenced from LandSandBoat (https://github.com/LandSandBoat/server), <c>c2s/0x113_sitchair.cpp</c>.
        /// </summary>
        public static byte[] BuildSitChair(RestMode mode, uint chairId, ushort sequenceId = 0)
        {
            var packet = BuildWords(0x113, 3, sequenceId);
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4, 4), (uint)mode);
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(8, 4), chairId);
            return packet;
        }

        /// <summary>
        /// Builds C2S 0x0A2 (GP_CLI_COMMAND_DICE, 8 bytes): <c>/random</c>. The word is the number typed after the command;
        /// neither the client nor LandSandBoat uses it (the server rolls 0 to 999 itself).
        /// Packet structure referenced from XiPackets (https://github.com/atom0s/XiPackets), <c>world/client/0x00A2</c>;
        /// server side referenced from LandSandBoat (https://github.com/LandSandBoat/server), <c>c2s/0x0a2_dice.cpp</c>.
        /// </summary>
        public static byte[] BuildDice(uint typedNumber = 0, ushort sequenceId = 0)
        {
            var packet = BuildWords(0x0A2, 2, sequenceId);
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4, 4), typedNumber);
            return packet;
        }

        /// <summary>
        /// Builds C2S 0x0A0 (GP_CLI_COMMAND_SWITCH_PROPOSAL, variable): a proposal (<c>/nominate</c>, <c>/propose</c>).
        /// Layout: 4 u8 <c>Kind</c>, 5 <c>Str</c> (the raw command text, NUL terminated, at most 127 bytes), the packet
        /// rounded up to 4 bytes. An empty <paramref name="text"/> is a cancel (LandSandBoat closes the sender's live
        /// proposal when one exists). LandSandBoat splits the text on spaces, honouring double quotes: the first token
        /// is the question, the rest (up to 8) the options.
        /// Packet structure referenced from XiPackets (https://github.com/atom0s/XiPackets), <c>world/client/0x00A0</c>;
        /// server side referenced from LandSandBoat (https://github.com/LandSandBoat/server), <c>c2s/0x0a0_switch_proposal.h</c>
        /// and <c>nominate_manager.cpp</c>.
        /// </summary>
        public static byte[] BuildProposal(ProposalKind kind, string text, ushort sequenceId = 0)
        {
            byte[] str = Encoding.UTF8.GetBytes(text ?? string.Empty);
            int strLength = Math.Min(str.Length, MaxProposalTextBytes);
            int words = (5 + strLength + 1 + 3) / 4;
            var packet = BuildWords(0x0A0, (ushort)words, sequenceId);
            packet[4] = (byte)kind;
            str.AsSpan(0, strLength).CopyTo(packet.AsSpan(5));
            return packet;
        }

        /// <summary>
        /// Builds C2S 0x0A1 (GP_CLI_COMMAND_SWITCH_VOTE, variable): <c>/vote</c>. Layout: 4 u8 option index (1 to 8),
        /// 5 the proposer's name (NUL terminated, at most 14 bytes), rounded up to 4 bytes.
        /// Packet structure referenced from XiPackets (https://github.com/atom0s/XiPackets), <c>world/client/0x00A1</c>;
        /// server side referenced from LandSandBoat (https://github.com/LandSandBoat/server), <c>c2s/0x0a1_switch_vote.h</c>.
        /// </summary>
        public static byte[] BuildVote(byte optionIndex, string proposerName, ushort sequenceId = 0)
        {
            byte[] name = Encoding.UTF8.GetBytes(proposerName ?? string.Empty);
            int nameLength = Math.Min(name.Length, MaxVoteNameBytes);
            int words = (5 + nameLength + 1 + 3) / 4;
            var packet = BuildWords(0x0A1, (ushort)words, sequenceId);
            packet[4] = optionIndex;
            name.AsSpan(0, nameLength).CopyTo(packet.AsSpan(5));
            return packet;
        }

        /// <summary>
        /// Builds C2S 0x0F4 (GP_CLI_COMMAND_TRACKING_LIST, 8 bytes): asks for the wide scan list. <c>SendFlg</c> is always
        /// 1; LandSandBoat drops the packet otherwise. It answers only for Ranger and Beastmaster (or every job when its
        /// <c>ALL_JOBS_WIDESCAN</c> setting is on), with S2C 0x0F6 start, one 0x0F4 per entity, and 0x0F6 end.
        /// Packet structure referenced from XiPackets (https://github.com/atom0s/XiPackets), <c>world/client/0x00F4</c>;
        /// server side referenced from LandSandBoat (https://github.com/LandSandBoat/server), <c>c2s/0x0f4_tracking_list.cpp</c>.
        /// </summary>
        public static byte[] BuildTrackingList(ushort sequenceId = 0)
        {
            var packet = BuildWords(0x0F4, 2, sequenceId);
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4, 4), 1);
            return packet;
        }

        /// <summary>
        /// Builds C2S 0x0F5 (GP_CLI_COMMAND_TRACKING_START, 8 bytes): tracks the monster or NPC at <paramref name="actIndex"/>
        /// (1 to 4096 in LandSandBoat) while it is within the wide scan range.
        /// Packet structure referenced from XiPackets (https://github.com/atom0s/XiPackets), <c>world/client/0x00F5</c>;
        /// server side referenced from LandSandBoat (https://github.com/LandSandBoat/server), <c>c2s/0x0f5_tracking_start.cpp</c>.
        /// </summary>
        public static byte[] BuildTrackingStart(ushort actIndex, ushort sequenceId = 0)
        {
            var packet = BuildWords(0x0F5, 2, sequenceId);
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4, 4), actIndex);
            return packet;
        }

        /// <summary>
        /// Builds C2S 0x0F6 (GP_CLI_COMMAND_TRACKING_END, 8 bytes): stops tracking.
        /// Packet structure referenced from XiPackets (https://github.com/atom0s/XiPackets), <c>world/client/0x00F6</c>;
        /// server side referenced from LandSandBoat (https://github.com/LandSandBoat/server), <c>c2s/0x0f6_tracking_end.cpp</c>.
        /// </summary>
        public static byte[] BuildTrackingEnd(ushort sequenceId = 0)
        {
            var packet = BuildWords(0x0F6, 2, sequenceId);
            return packet;
        }

        /// <summary>
        /// Builds C2S 0x119 (emote list request, 4 bytes): asks for the unlocked job emotes and chairs; the server answers
        /// with S2C 0x11A. Retail sends it when Main Menu, Communication opens.
        /// Packet structure referenced from XiPackets (https://github.com/atom0s/XiPackets), <c>world/client/0x0119</c>;
        /// server side referenced from LandSandBoat (https://github.com/LandSandBoat/server), <c>c2s/0x119_emote_list.cpp</c>.
        /// </summary>
        public static byte[] BuildEmoteListRequest(ushort sequenceId = 0) => BuildWords(0x119, 1, sequenceId);

        /// <summary>
        /// Builds C2S 0x059 (GP_CLI_COMMAND_EFFECTEND, 16 bytes): the client's report that its synthesis animation ended.
        /// <paramref name="effectPara"/> is 0 when the animations played out and 1 when the client left the synthesis
        /// early (XiPackets); LandSandBoat ignores the packet.
        /// Packet structure referenced from XiPackets (https://github.com/atom0s/XiPackets), <c>world/client/0x0059</c>;
        /// server side referenced from LandSandBoat (https://github.com/LandSandBoat/server), <c>c2s/0x059_effectend.cpp</c>.
        /// </summary>
        public static byte[] BuildEffectEnd(uint effectPara = 0, ushort sequenceId = 0)
        {
            var packet = BuildWords(0x059, 4, sequenceId);
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4, 4), effectPara);
            return packet;
        }
    }
}
