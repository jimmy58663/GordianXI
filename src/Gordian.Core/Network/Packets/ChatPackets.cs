// src/Gordian.Core/Network/Packets/ChatPackets.cs
// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server)
// and Atom0s XiPackets research (https://github.com/atom0s/XiPackets).

using System;
using System.Buffers.Binary;
using System.Text;

namespace Gordian.Core.Network.Packets
{
    #region Enums

    /// <summary>
    /// Message type / channel identifiers transmitted in S2C 0x017 (GP_SERV_COMMAND_CHAT_STD).
    /// Wire format referenced from LandSandBoat CHAT_MESSAGE_TYPE (src/map/enums/chat_message_type.h).
    /// </summary>
    public enum ChatMessageType : byte
    {
        Say = 0,
        Shout = 1,
        Unknown = 2,
        Tell = 3,
        Party = 4,
        Linkshell = 5,
        System1 = 6,
        System2 = 7,
        Emotion = 8,
        GmPrompt = 12,
        NoSpeakerSay = 13,
        NoSpeakerShout = 14,
        NoSpeakerParty = 15,
        NoSpeakerLinkshell = 16,
        Yell = 26,
        Linkshell2 = 27,
        NoSpeakerLinkshell2 = 28,
        System3 = 29,
        Linkshell3 = 30,
        NoSpeakerLinkshell3 = 31,
        Unity = 33,
        JpAssist = 34,
        NaAssist = 35
    }

    /// <summary>
    /// Chat send channel types used in C2S 0x0B5 (GP_CLI_COMMAND_CHAT_STD).
    /// Protocol specification referenced from LandSandBoat (src/map/packets/c2s/0x0b5_chat_std.h).
    /// </summary>
    public enum ChatSendKind : byte
    {
        Say = 0x00,
        Shout = 0x01,
        Party = 0x04,
        Linkshell1 = 0x05,
        Emote = 0x08,
        LinkshellPvp = 0x18,
        Yell = 0x1A,
        Linkshell2 = 0x1B,
        Unity = 0x21,
        AssistJ = 0x22,
        AssistE = 0x23
    }

    /// <summary>
    /// Interaction types for assist channel mentor features in C2S 0x0B7 (GP_CLI_COMMAND_ASSIST_CHANNEL).
    /// Protocol specification referenced from LandSandBoat (src/map/packets/c2s/0x0b7_assist_channel.h).
    /// </summary>
    public enum AssistActionKind : byte
    {
        GiveThumbsUp = 0x24,
        IssueWarning = 0x25,
        AddToMuteList = 0x26,
        RemoveFromMuteList = 0x27
    }

    /// <summary>
    /// Language identifiers for auto-translation queries and responses (C2S 0x02B / S2C 0x047).
    /// </summary>
    public enum TranslateLanguage : byte
    {
        Japanese = 0,
        English = 1
    }

    /// <summary>
    /// Linkshell equipped slot index.
    /// </summary>
    public enum LinkshellSlot : byte
    {
        LS1 = 0,
        LS2 = 1
    }

    /// <summary>
    /// Permission levels for modifying linkshell messages (C2S 0x0E2).
    /// </summary>
    public enum LinkshellWriteLevel : byte
    {
        Linkshell = 0,
        Pearlsack = 1,
        Linkpearl = 2
    }

    /// <summary>
    /// Player search comment categories used in C2S 0x0E0 (GP_CLI_COMMAND_SET_USERMSG).
    /// </summary>
    public enum SearchMessageType : uint
    {
        Default = 0x00,
        EXPPartySeekParty = 0x11,
        EXPPartyFindMember = 0x12,
        EXPPartyOther = 0x13,
        BattleContentSeekParty = 0x21,
        BattleContentFindMember = 0x22,
        BattleContentOther = 0x23,
        MissionsQuestSeekParty = 0x31,
        MissionsQuestFindMember = 0x32,
        MissionsQuestOther = 0x33,
        ItemWantToSell = 0x41,
        ItemWantToBuy = 0x42,
        ItemOther = 0x43,
        LinkshellLookingForLS = 0x51,
        LinkshellRecruiting = 0x52,
        LinkshellOther = 0x53,
        LookingForFriends = 0x61,
        Others = 0x73
    }

    #endregion

    #region Inbound Decoders (readonly ref struct)

    /// <summary>
    /// S2C 0x017 (GP_SERV_COMMAND_CHAT_STD): Standard chat and communication message packet.
    /// Protocol specification referenced from LandSandBoat (src/map/packets/s2c/0x017_chat_std.cpp)
    /// and Atom0s XiPackets (world/server/0x0017).
    /// </summary>
    public readonly ref struct S2C_0x017_ChatStd
    {
        public const ushort PacketId = 0x017;
        private readonly ReadOnlySpan<byte> _payload;

        public bool IsValid { get; }
        public ChatMessageType Kind { get; }
        public byte Attr { get; }
        public ushort Data { get; }

        public bool IsGm => (Attr & 0x01) != 0;
        public ushort ZoneId => Data;
        public byte MasteryRank => (byte)(Data & 0xFF);
        public byte MentorRank => (byte)((Data >> 8) & 0xFF);

        public S2C_0x017_ChatStd(ReadOnlySpan<byte> payload)
        {
            _payload = payload;
            if (payload.Length < 19)
            {
                IsValid = false;
                Kind = ChatMessageType.Say;
                Attr = 0;
                Data = 0;
                return;
            }

            Kind = (ChatMessageType)payload[0];
            Attr = payload[1];
            Data = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(2, 2));
            IsValid = true;
        }

        public string GetSenderName()
        {
            if (!IsValid || _payload.Length < 19) return string.Empty;
            ReadOnlySpan<byte> nameSpan = _payload.Slice(4, 15);
            int len = 0;
            while (len < nameSpan.Length && nameSpan[len] != 0)
            {
                len++;
            }
            return len > 0 ? Encoding.ASCII.GetString(nameSpan.Slice(0, len)) : string.Empty;
        }

        public string GetMessage()
        {
            if (!IsValid || _payload.Length <= 19) return string.Empty;
            ReadOnlySpan<byte> msgSpan = _payload.Slice(19);
            int len = 0;
            while (len < msgSpan.Length && msgSpan[len] != 0)
            {
                len++;
            }
            if (len == 0) return string.Empty;

            try
            {
                return Encoding.GetEncoding("shift_jis").GetString(msgSpan.Slice(0, len));
            }
            catch
            {
                return Encoding.UTF8.GetString(msgSpan.Slice(0, len));
            }
        }

        public ReadOnlySpan<byte> RawMessageSpan =>
            IsValid && _payload.Length > 19 ? _payload.Slice(19) : ReadOnlySpan<byte>.Empty;

        public bool HasAutoTranslate()
        {
            ReadOnlySpan<byte> msg = RawMessageSpan;
            return msg.Contains((byte)0xFD);
        }
    }

    /// <summary>
    /// S2C 0x009 (GP_SERV_COMMAND_MESSAGE): General purpose system messages.
    /// Protocol specification referenced from LandSandBoat (src/map/packets/s2c/0x009_message.cpp)
    /// and Atom0s XiPackets (world/server/0x0009).
    /// </summary>
    public readonly ref struct S2C_0x009_SysMessage
    {
        public const ushort PacketId = 0x009;
        private readonly ReadOnlySpan<byte> _payload;

        public bool IsValid { get; }
        public uint UniqueNo { get; }
        public ushort ActorIndex { get; }
        public ushort MessageId { get; }
        public byte Attr { get; }

        public S2C_0x009_SysMessage(ReadOnlySpan<byte> payload)
        {
            _payload = payload;
            if (payload.Length < 9)
            {
                IsValid = false;
                UniqueNo = 0;
                ActorIndex = 0;
                MessageId = 0;
                Attr = 0;
                return;
            }

            UniqueNo = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0, 4));
            ActorIndex = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(4, 2));
            MessageId = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(6, 2));
            Attr = payload[8];
            IsValid = true;
        }

        public string GetData()
        {
            if (!IsValid || _payload.Length <= 9) return string.Empty;
            ReadOnlySpan<byte> dataSpan = _payload.Slice(9);
            int len = 0;
            while (len < dataSpan.Length && dataSpan[len] != 0)
            {
                len++;
            }
            if (len == 0) return string.Empty;

            try
            {
                return Encoding.GetEncoding("shift_jis").GetString(dataSpan.Slice(0, len));
            }
            catch
            {
                return Encoding.UTF8.GetString(dataSpan.Slice(0, len));
            }
        }

        public ReadOnlySpan<byte> RawDataSpan =>
            IsValid && _payload.Length > 9 ? _payload.Slice(9) : ReadOnlySpan<byte>.Empty;
    }

    /// <summary>
    /// S2C 0x047 (GP_SERV_COMMAND_TRANSLATE): Auto-translate term response.
    /// Protocol specification referenced from LandSandBoat (src/map/packets/s2c/0x047_translate.h)
    /// and Atom0s XiPackets (world/server/0x0047).
    /// </summary>
    public readonly ref struct S2C_0x047_Translate
    {
        public const ushort PacketId = 0x047;
        private readonly ReadOnlySpan<byte> _payload;

        public bool IsValid { get; }
        public ushort ItemNo { get; }
        public TranslateLanguage FromIndex { get; }
        public TranslateLanguage ToIndex { get; }

        public S2C_0x047_Translate(ReadOnlySpan<byte> payload)
        {
            _payload = payload;
            if (payload.Length < 4)
            {
                IsValid = false;
                ItemNo = 0;
                FromIndex = TranslateLanguage.Japanese;
                ToIndex = TranslateLanguage.English;
                return;
            }

            ItemNo = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(0, 2));
            FromIndex = (TranslateLanguage)payload[2];
            ToIndex = (TranslateLanguage)payload[3];
            IsValid = true;
        }

        public string GetFromString()
        {
            if (!IsValid || _payload.Length < 68) return string.Empty;
            ReadOnlySpan<byte> strSpan = _payload.Slice(4, Math.Min(64, _payload.Length - 4));
            int len = 0;
            while (len < strSpan.Length && strSpan[len] != 0) len++;
            if (len == 0) return string.Empty;
            try { return Encoding.GetEncoding("shift_jis").GetString(strSpan.Slice(0, len)); }
            catch { return Encoding.UTF8.GetString(strSpan.Slice(0, len)); }
        }

        public string GetToString()
        {
            if (!IsValid || _payload.Length < 132) return string.Empty;
            ReadOnlySpan<byte> strSpan = _payload.Slice(68, Math.Min(64, _payload.Length - 68));
            int len = 0;
            while (len < strSpan.Length && strSpan[len] != 0) len++;
            if (len == 0) return string.Empty;
            try { return Encoding.GetEncoding("shift_jis").GetString(strSpan.Slice(0, len)); }
            catch { return Encoding.UTF8.GetString(strSpan.Slice(0, len)); }
        }
    }

    /// <summary>
    /// S2C 0x0CC (GP_SERV_COMMAND_LINKSHELL_MESSAGE): Linkshell MOTD and permissions response.
    /// Protocol specification referenced from LandSandBoat (src/map/packets/s2c/0x0cc_linkshell_message.h)
    /// and Atom0s XiPackets (world/server/0x00CC).
    /// </summary>
    public readonly ref struct S2C_0x0CC_LinkshellMessage
    {
        public const ushort PacketId = 0x0CC;
        private readonly ReadOnlySpan<byte> _payload;

        public bool IsValid { get; }
        public byte Stat { get; }
        public byte Attr { get; }
        public byte ReadLevel { get; }
        public byte WriteLevel { get; }
        public byte PubEditLevel { get; }
        public LinkshellSlot Slot { get; }
        public ushort SequenceId { get; }
        public uint UpdateTime { get; }
        public ushort OpType { get; }

        public DateTimeOffset UpdateDateTime => DateTimeOffset.FromUnixTimeSeconds(UpdateTime);

        public S2C_0x0CC_LinkshellMessage(ReadOnlySpan<byte> payload)
        {
            _payload = payload;
            if (payload.Length < 136)
            {
                IsValid = false;
                Stat = 0;
                Attr = 0;
                ReadLevel = 0;
                WriteLevel = 0;
                PubEditLevel = 0;
                Slot = LinkshellSlot.LS1;
                SequenceId = 0;
                UpdateTime = 0;
                OpType = 0;
                return;
            }

            Stat = (byte)(payload[0] & 0x0F);
            Attr = (byte)((payload[0] >> 4) & 0x0F);

            byte byte1 = payload[1];
            ReadLevel = (byte)(byte1 & 0x03);
            WriteLevel = (byte)((byte1 >> 2) & 0x03);
            PubEditLevel = (byte)((byte1 >> 4) & 0x03);
            Slot = (LinkshellSlot)((byte1 >> 6) & 0x03);

            SequenceId = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(2, 2));
            UpdateTime = payload.Length >= 136 ? BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(132, 4)) : 0;
            OpType = payload.Length >= 154 ? BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(152, 2)) : (ushort)0;
            IsValid = true;
        }

        public string GetMessage()
        {
            if (!IsValid || _payload.Length < 132) return string.Empty;
            ReadOnlySpan<byte> msgSpan = _payload.Slice(4, 128);
            int len = 0;
            while (len < msgSpan.Length && msgSpan[len] != 0) len++;
            if (len == 0) return string.Empty;
            try { return Encoding.GetEncoding("shift_jis").GetString(msgSpan.Slice(0, len)); }
            catch { return Encoding.UTF8.GetString(msgSpan.Slice(0, len)); }
        }

        public string GetModifier()
        {
            if (!IsValid || _payload.Length < 152) return string.Empty;
            ReadOnlySpan<byte> modSpan = _payload.Slice(136, 16);
            int len = 0;
            while (len < modSpan.Length && modSpan[len] != 0) len++;
            return len > 0 ? Encoding.ASCII.GetString(modSpan.Slice(0, len)) : string.Empty;
        }

        public string GetLinkshellName()
        {
            if (!IsValid || _payload.Length < 172) return string.Empty;
            ReadOnlySpan<byte> lsSpan = _payload.Slice(156, 16);
            int len = 0;
            while (len < lsSpan.Length && lsSpan[len] != 0) len++;
            return len > 0 ? Encoding.ASCII.GetString(lsSpan.Slice(0, len)) : string.Empty;
        }
    }

    #endregion

    #region Outbound Builders

    /// <summary>
    /// High-performance, zero-allocation builder methods for client communication and chat sub-packets.
    /// Wire formats referenced from LandSandBoat and Atom0s XiPackets research.
    /// </summary>
    public static class ChatOutboundPackets
    {
        public const int UserMsgSubPacketSize = 152;
        public const int LinkshellSubPacketSize = 148;
        public const int AssistChannelSubPacketSize = 24;

        /// <summary>
        /// Builds C2S 0x0B5 (GP_CLI_COMMAND_CHAT_STD): General chat message (Say, Shout, Party, LS, Yell, Unity, etc.).
        /// Protocol specification referenced from LandSandBoat (src/map/packets/c2s/0x0b5_chat_std.h).
        /// </summary>
        public static int BuildChatStd(Span<byte> destination, ChatSendKind kind, ReadOnlySpan<char> message, ushort sequenceId = 0)
        {
            Span<byte> msgBytes = stackalloc byte[128];
            int written = 0;
            if (!message.IsEmpty)
            {
                try
                {
                    written = Encoding.GetEncoding("shift_jis").GetBytes(message, msgBytes);
                }
                catch
                {
                    written = Encoding.UTF8.GetBytes(message, msgBytes);
                }
            }

            int unalignedSize = 4 + 2 + written + 1; // Header (4) + Kind (1) + Dammy (1) + message + null
            int totalSize = Math.Max(8, (unalignedSize + 3) & ~3);

            if (destination.Length < totalSize)
                throw new ArgumentException($"Destination must be at least {totalSize} bytes.", nameof(destination));

            destination.Slice(0, totalSize).Clear();
            ushort words = (ushort)(totalSize / 4);
            ushort headerWord = (ushort)(0x0B5 | (words << 9));
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(0, 2), headerWord);
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(2, 2), sequenceId);

            destination[4] = (byte)kind;
            destination[5] = 0x00;

            if (written > 0)
            {
                msgBytes.Slice(0, written).CopyTo(destination.Slice(6));
            }
            destination[6 + written] = 0x00;

            return totalSize;
        }

        public static byte[] BuildChatStd(ChatSendKind kind, string message, ushort sequenceId = 0)
        {
            Span<byte> buffer = stackalloc byte[256];
            int size = BuildChatStd(buffer, kind, message.AsSpan(), sequenceId);
            byte[] result = new byte[size];
            buffer.Slice(0, size).CopyTo(result);
            return result;
        }

        /// <summary>
        /// Builds C2S 0x0B6 (GP_CLI_COMMAND_CHAT_NAME): Direct whisper / tell to specific character.
        /// Protocol specification referenced from LandSandBoat (src/map/packets/c2s/0x0b6_chat_name.h).
        /// </summary>
        public static int BuildChatTell(Span<byte> destination, ReadOnlySpan<char> recipient, ReadOnlySpan<char> message, ushort sequenceId = 0)
        {
            Span<byte> msgBytes = stackalloc byte[128];
            int msgLen = 0;
            if (!message.IsEmpty)
            {
                try
                {
                    msgLen = Encoding.GetEncoding("shift_jis").GetBytes(message, msgBytes);
                }
                catch
                {
                    msgLen = Encoding.UTF8.GetBytes(message, msgBytes);
                }
            }

            int unalignedSize = 4 + 2 + 15 + msgLen + 1; // Header (4) + 2 dummy + 15 name + message + null
            int totalSize = Math.Max(24, (unalignedSize + 3) & ~3);

            if (destination.Length < totalSize)
                throw new ArgumentException($"Destination must be at least {totalSize} bytes.", nameof(destination));

            destination.Slice(0, totalSize).Clear();
            ushort words = (ushort)(totalSize / 4);
            ushort headerWord = (ushort)(0x0B6 | (words << 9));
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(0, 2), headerWord);
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(2, 2), sequenceId);

            destination[4] = 0x03; // MESSAGE_TELL opcode validation marker
            destination[5] = 0x00;

            if (!recipient.IsEmpty)
            {
                int nameLen = Math.Min(15, Encoding.ASCII.GetByteCount(recipient));
                Encoding.ASCII.GetBytes(recipient.Slice(0, Math.Min(recipient.Length, 15)), destination.Slice(6, nameLen));
            }

            if (msgLen > 0)
            {
                msgBytes.Slice(0, msgLen).CopyTo(destination.Slice(21));
            }
            destination[21 + msgLen] = 0x00;

            return totalSize;
        }

        public static byte[] BuildChatTell(string recipient, string message, ushort sequenceId = 0)
        {
            Span<byte> buffer = stackalloc byte[256];
            int size = BuildChatTell(buffer, recipient.AsSpan(), message.AsSpan(), sequenceId);
            byte[] result = new byte[size];
            buffer.Slice(0, size).CopyTo(result);
            return result;
        }

        /// <summary>
        /// Builds C2S 0x0B7 (GP_CLI_COMMAND_ASSIST_CHANNEL): Assist channel mentor actions.
        /// Protocol specification referenced from LandSandBoat (src/map/packets/c2s/0x0b7_assist_channel.h).
        /// </summary>
        public static int BuildAssistChannel(Span<byte> destination, AssistActionKind kind, ReadOnlySpan<char> targetName, ushort sequenceId = 0)
        {
            if (destination.Length < AssistChannelSubPacketSize)
                throw new ArgumentException($"Destination must be at least {AssistChannelSubPacketSize} bytes.", nameof(destination));

            destination.Slice(0, AssistChannelSubPacketSize).Clear();
            ushort words = (ushort)(AssistChannelSubPacketSize / 4);
            ushort headerWord = (ushort)(0x0B7 | (words << 9));
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(0, 2), headerWord);
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(2, 2), sequenceId);

            destination[4] = (byte)kind;
            destination[5] = 0x00;

            if (!targetName.IsEmpty)
            {
                int nameLen = Math.Min(15, Encoding.ASCII.GetByteCount(targetName));
                Encoding.ASCII.GetBytes(targetName.Slice(0, Math.Min(targetName.Length, 15)), destination.Slice(6, nameLen));
            }

            destination[21] = 0x20; // Mes field is always single space ' ' (0x20)

            return AssistChannelSubPacketSize;
        }

        public static byte[] BuildAssistChannel(AssistActionKind kind, string targetName, ushort sequenceId = 0)
        {
            byte[] packet = new byte[AssistChannelSubPacketSize];
            BuildAssistChannel(packet.AsSpan(), kind, targetName.AsSpan(), sequenceId);
            return packet;
        }

        /// <summary>
        /// Builds C2S 0x02B (GP_CLI_COMMAND_TRANSLATE): Request auto-translation term lookup.
        /// Protocol specification referenced from LandSandBoat (src/map/packets/c2s/0x02b_translate.h).
        /// </summary>
        public static int BuildTranslateRequest(
            Span<byte> destination,
            TranslateLanguage fromLang,
            TranslateLanguage toLang,
            ReadOnlySpan<char> term,
            ushort sequenceId = 0)
        {
            Span<byte> termBytes = stackalloc byte[64];
            int termLen = 0;
            if (!term.IsEmpty)
            {
                try
                {
                    termLen = Encoding.GetEncoding("shift_jis").GetBytes(term, termBytes);
                }
                catch
                {
                    termLen = Encoding.UTF8.GetBytes(term, termBytes);
                }
            }

            int unalignedSize = 4 + 4 + termLen + 1; // Header (4) + From (1) + To (1) + padding (2) + term + null
            int totalSize = Math.Max(12, (unalignedSize + 3) & ~3);

            if (destination.Length < totalSize)
                throw new ArgumentException($"Destination must be at least {totalSize} bytes.", nameof(destination));

            destination.Slice(0, totalSize).Clear();
            ushort words = (ushort)(totalSize / 4);
            ushort headerWord = (ushort)(0x02B | (words << 9));
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(0, 2), headerWord);
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(2, 2), sequenceId);

            destination[4] = (byte)fromLang;
            destination[5] = (byte)toLang;
            destination[6] = 0x00;
            destination[7] = 0x00;

            if (termLen > 0)
            {
                termBytes.Slice(0, termLen).CopyTo(destination.Slice(8));
            }
            destination[8 + termLen] = 0x00;

            return totalSize;
        }

        public static byte[] BuildTranslateRequest(
            TranslateLanguage fromLang,
            TranslateLanguage toLang,
            string term,
            ushort sequenceId = 0)
        {
            Span<byte> buffer = stackalloc byte[128];
            int size = BuildTranslateRequest(buffer, fromLang, toLang, term.AsSpan(), sequenceId);
            byte[] result = new byte[size];
            buffer.Slice(0, size).CopyTo(result);
            return result;
        }

        /// <summary>
        /// Builds C2S 0x0E0 (GP_CLI_COMMAND_SET_USERMSG): Set player search message / comment.
        /// Protocol specification referenced from LandSandBoat (src/map/packets/c2s/0x0e0_set_usermsg.h).
        /// </summary>
        public static void BuildSetUserMsg(
            Span<byte> destination,
            ReadOnlySpan<char> message,
            SearchMessageType msgType,
            uint installTime = 0,
            uint srvExCode = 0,
            uint cliExCode = 0,
            ushort sequenceId = 0)
        {
            if (destination.Length < UserMsgSubPacketSize)
                throw new ArgumentException($"Destination must be at least {UserMsgSubPacketSize} bytes.", nameof(destination));

            destination.Slice(0, UserMsgSubPacketSize).Clear();
            ushort words = (ushort)(UserMsgSubPacketSize / 4);
            ushort headerWord = (ushort)(0x0E0 | (words << 9));
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(0, 2), headerWord);
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(2, 2), sequenceId);

            if (!message.IsEmpty)
            {
                Span<byte> msgBytes = stackalloc byte[128];
                int msgLen = 0;
                try
                {
                    msgLen = Encoding.GetEncoding("shift_jis").GetBytes(message, msgBytes);
                }
                catch
                {
                    msgLen = Encoding.UTF8.GetBytes(message, msgBytes);
                }
                msgBytes.Slice(0, Math.Min(127, msgLen)).CopyTo(destination.Slice(4, Math.Min(127, msgLen)));
            }

            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(132, 4), installTime);
            destination[136] = (byte)'W';
            destination[137] = (byte)'I';
            destination[138] = (byte)'N';
            destination[139] = 0;

            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(140, 4), srvExCode);
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(144, 4), cliExCode);
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(148, 4), (uint)msgType);
        }

        public static byte[] BuildSetUserMsg(
            string message,
            SearchMessageType msgType,
            uint installTime = 0,
            uint srvExCode = 0,
            uint cliExCode = 0,
            ushort sequenceId = 0)
        {
            byte[] packet = new byte[UserMsgSubPacketSize];
            BuildSetUserMsg(packet.AsSpan(), message.AsSpan(), msgType, installTime, srvExCode, cliExCode, sequenceId);
            return packet;
        }

        /// <summary>
        /// Builds C2S 0x0E1 (GP_CLI_COMMAND_GET_LSMSG): Requests linkshell MOTD message.
        /// Protocol specification referenced from LandSandBoat (src/map/packets/c2s/0x0e1_get_lsmsg.h).
        /// </summary>
        public static void BuildGetLsMsg(Span<byte> destination, LinkshellSlot slot, ushort sequenceId = 0)
        {
            if (destination.Length < LinkshellSubPacketSize)
                throw new ArgumentException($"Destination must be at least {LinkshellSubPacketSize} bytes.", nameof(destination));

            destination.Slice(0, LinkshellSubPacketSize).Clear();
            ushort words = (ushort)(LinkshellSubPacketSize / 4);
            ushort headerWord = (ushort)(0x0E1 | (words << 9));
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(0, 2), headerWord);
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(2, 2), sequenceId);

            destination[5] = (byte)(((int)slot & 0x03) << 6);
        }

        public static byte[] BuildGetLsMsg(LinkshellSlot slot, ushort sequenceId = 0)
        {
            byte[] packet = new byte[LinkshellSubPacketSize];
            BuildGetLsMsg(packet.AsSpan(), slot, sequenceId);
            return packet;
        }

        /// <summary>
        /// Builds C2S 0x0E2 (GP_CLI_COMMAND_SET_LSMSG): Sets linkshell MOTD message and access permissions.
        /// Protocol specification referenced from LandSandBoat (src/map/packets/c2s/0x0e2_set_lsmsg.h).
        /// </summary>
        public static void BuildSetLsMsg(
            Span<byte> destination,
            LinkshellSlot slot,
            ReadOnlySpan<char> message,
            LinkshellWriteLevel writeLevel = LinkshellWriteLevel.Linkshell,
            ushort sequenceId = 0)
        {
            if (destination.Length < LinkshellSubPacketSize)
                throw new ArgumentException($"Destination must be at least {LinkshellSubPacketSize} bytes.", nameof(destination));

            destination.Slice(0, LinkshellSubPacketSize).Clear();
            ushort words = (ushort)(LinkshellSubPacketSize / 4);
            ushort headerWord = (ushort)(0x0E2 | (words << 9));
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(0, 2), headerWord);
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(2, 2), sequenceId);

            byte byte1 = (byte)((((int)slot & 0x03) << 6) | (((int)writeLevel & 0x03) << 2));
            destination[5] = byte1;

            if (!message.IsEmpty)
            {
                Span<byte> msgBytes = stackalloc byte[128];
                int msgLen = 0;
                try
                {
                    msgLen = Encoding.GetEncoding("shift_jis").GetBytes(message, msgBytes);
                }
                catch
                {
                    msgLen = Encoding.UTF8.GetBytes(message, msgBytes);
                }
                msgBytes.Slice(0, Math.Min(127, msgLen)).CopyTo(destination.Slice(16, Math.Min(127, msgLen)));
            }
        }

        public static byte[] BuildSetLsMsg(
            LinkshellSlot slot,
            string message,
            LinkshellWriteLevel writeLevel = LinkshellWriteLevel.Linkshell,
            ushort sequenceId = 0)
        {
            byte[] packet = new byte[LinkshellSubPacketSize];
            BuildSetLsMsg(packet.AsSpan(), slot, message.AsSpan(), writeLevel, sequenceId);
            return packet;
        }

        /// <summary>
        /// Builds C2S 0x0E4 (GP_CLI_COMMAND_GET_LSPRIV): Queries linkshell privileges.
        /// Protocol specification referenced from LandSandBoat (src/map/packets/c2s/0x0e4_get_lspriv.h).
        /// </summary>
        public static void BuildGetLsPriv(Span<byte> destination, LinkshellSlot slot, ushort sequenceId = 0)
        {
            if (destination.Length < LinkshellSubPacketSize)
                throw new ArgumentException($"Destination must be at least {LinkshellSubPacketSize} bytes.", nameof(destination));

            destination.Slice(0, LinkshellSubPacketSize).Clear();
            ushort words = (ushort)(LinkshellSubPacketSize / 4);
            ushort headerWord = (ushort)(0x0E4 | (words << 9));
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(0, 2), headerWord);
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(2, 2), sequenceId);

            destination[5] = (byte)(((int)slot & 0x03) << 6);
        }

        public static byte[] BuildGetLsPriv(LinkshellSlot slot, ushort sequenceId = 0)
        {
            byte[] packet = new byte[LinkshellSubPacketSize];
            BuildGetLsPriv(packet.AsSpan(), slot, sequenceId);
            return packet;
        }
    }

    #endregion
}
