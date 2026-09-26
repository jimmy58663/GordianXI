// src/Gordian.Core/Network/Packets/ConfigPackets.cs
using System;
using System.Buffers.Binary;
using System.Threading.Tasks;
using Gordian.Core.Diagnostics;
using Gordian.Core.World;

namespace Gordian.Core.Network.Packets
{
    /// <summary>
    /// The player configuration flag word (the first dword of the server's SAVE_CONF record): bit positions shared
    /// by S2C 0x0B4 (the server's copy), C2S 0x0DC (set/clear one flag) and C2S 0x0DB (which echoes the word).
    /// <para>
    /// Layout referenced from XiPackets (https://github.com/atom0s/XiPackets, world/server/0x00B4 and
    /// world/client/0x00DC) and LandSandBoat (https://github.com/LandSandBoat/server, <c>SAVE_CONF</c> in
    /// <c>src/common/mmo.h</c>); confirmed 2026-09-26.
    /// </para>
    /// </summary>
    [Flags]
    public enum PlayerConfigFlags : uint
    {
        None = 0,

        /// <summary>Seeking a party (/invite).</summary>
        Invite = 1u << 0,

        /// <summary>Away (/away): tells are refused.</summary>
        Away = 1u << 1,

        /// <summary>Character information hidden (/anon; the Misc. page's "Character Information: Hide").</summary>
        Anonymity = 1u << 2,

        /// <summary>Two-bit language field (bits 3-4); retail characters carry 3.</summary>
        LanguageMask = 3u << 3,

        /// <summary>Two-bit system message filter level (bits 11-12; see <see cref="ConfigOutboundPackets.SystemMessageFilterLevelShift"/>).</summary>
        SysMesFilterLevelMask = 3u << 11,

        /// <summary>Auto-target during battle is off (the Gameplay page's "Auto-target: OFF").</summary>
        AutoTargetOff = 1u << 14,

        /// <summary>Looking for a party through the auto-group system.</summary>
        AutoParty = 1u << 15,

        MentorUnlocked = 1u << 24,
        Mentor = 1u << 25,

        /// <summary>The "New Adventurer" mark (red question mark) has been turned off; the server never turns it back on.</summary>
        NewAdventurerOff = 1u << 26,

        /// <summary>Headgear hidden (/displayhead off).</summary>
        DisplayHeadOff = 1u << 27,

        /// <summary>Accepting recruitment requests (/rec).</summary>
        Recruit = 1u << 29,
    }

    /// <summary>
    /// The first chat-filter word: a set bit hides that message kind from the log. Bit meanings referenced from
    /// LandSandBoat (<c>filters1_t</c>, <c>src/common/mmo.h</c>).
    /// </summary>
    [Flags]
    public enum ChatFilter1 : uint
    {
        None = 0,
        Say = 1u << 0,
        Shout = 1u << 1,
        Emotes = 1u << 3,
        SpecialActionsStartedByYou = 1u << 4,
        SpecialActionEffectsByYou = 1u << 5,
        AttacksByYou = 1u << 6,
        MissedAttacksByYou = 1u << 7,
        AttacksYouEvade = 1u << 8,
        DamageYouTake = 1u << 9,
        SpecialActionEffectsByNpcs = 1u << 10,
        AttacksByNpcs = 1u << 11,
        MissedAttacksByNpcs = 1u << 12,
        SpecialActionEffectsByParty = 1u << 13,
        AttacksByParty = 1u << 14,
        MissedAttacksByParty = 1u << 15,
        AttacksEvadedByParty = 1u << 16,
        DamageTakenByParty = 1u << 17,
        SpecialActionEffectsByAllies = 1u << 18,
        AttacksByAllies = 1u << 19,
        MissedAttacksByAllies = 1u << 20,
        AttacksEvadedByAllies = 1u << 21,
        DamageTakenByAllies = 1u << 22,
        SpecialActionsStartedByParty = 1u << 23,
        SpecialActionsStartedByAllies = 1u << 24,
        SpecialActionsStartedByNpcs = 1u << 25,
        OthersSynthesisAndFishingResults = 1u << 26,
        LotResults = 1u << 27,
        AttacksByOthers = 1u << 28,
        MissedAttacksByOthers = 1u << 29,
    }

    /// <summary>
    /// The second chat-filter word. Bit meanings referenced from LandSandBoat (<c>filters2_t</c>, <c>src/common/mmo.h</c>).
    /// </summary>
    [Flags]
    public enum ChatFilter2 : uint
    {
        None = 0,
        AttacksEvadedByOthers = 1u << 0,
        DamageTakenByOthers = 1u << 1,
        SpecialActionEffectsByOthers = 1u << 2,
        SpecialActionsStartedByOthers = 1u << 3,
        AttacksByFoes = 1u << 4,
        MissedAttacksByFoes = 1u << 5,
        AttacksEvadedByFoes = 1u << 6,
        DamageTakenByFoes = 1u << 7,
        SpecialActionEffectsByFoes = 1u << 8,
        SpecialActionsStartedByFoes = 1u << 9,
        CampaignRelatedData = 1u << 10,
        TellMessagesDeemedSpam = 1u << 11,
        ShoutYellMessagesDeemedSpam = 1u << 12,
        JobSpecificEmote = 1u << 15,
        Yell = 1u << 16,
        MessagesFromAlterEgos = 1u << 17,
        AssistJ = 1u << 19,
        AssistE = 1u << 20,
    }

    /// <summary>Party-search languages (C2S 0x0DB kind 1 parameter; S2C 0x0B4). Referenced from LandSandBoat.</summary>
    [Flags]
    public enum PartyLanguages : byte
    {
        None = 0,
        Japanese = 1 << 0,
        English = 1 << 1,
        German = 1 << 2,
        French = 1 << 3,
        Other = 1 << 4,
    }

    /// <summary>
    /// S2C 0x0B4 (GP_SERV_COMMAND_CONFIG): the server's copy of the character's configuration, sent on login and
    /// after every C2S 0x0DB / 0x0DC.
    /// <para>
    /// Packet structure referenced from XiPackets (https://github.com/atom0s/XiPackets, world/server/0x00B4) and
    /// LandSandBoat (<c>packets/s2c/0x0b4_config.h</c>): after the 4-byte header, SAVE_CONF (flag word, two filter
    /// words, PvpFlg, AreaCode: 15 bytes), one unknown byte, the party languages byte and 3 bytes of padding.
    /// </para>
    /// </summary>
    public readonly ref struct S2C_0x0B4_Config
    {
        public const ushort PacketId = 0x0B4;
        public const int MinimumPayloadLength = 17;

        public uint Flags { get; }
        public uint MessageFilter1 { get; }
        public uint MessageFilter2 { get; }
        public ushort PvpFlags { get; }
        public byte AreaCode { get; }
        public PartyLanguages PartyLanguages { get; }
        public bool IsValid { get; }

        public S2C_0x0B4_Config(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < MinimumPayloadLength)
            {
                Flags = 0;
                MessageFilter1 = 0;
                MessageFilter2 = 0;
                PvpFlags = 0;
                AreaCode = 0;
                PartyLanguages = PartyLanguages.None;
                IsValid = false;
                return;
            }

            Flags = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0, 4));
            MessageFilter1 = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(4, 4));
            MessageFilter2 = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(8, 4));
            PvpFlags = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(12, 2));
            AreaCode = payload[14];
            PartyLanguages = (PartyLanguages)payload[16];
            IsValid = true;
        }
    }

    /// <summary>
    /// Builders for the client configuration packets.
    /// <para>
    /// C2S 0x0DC (GP_CLI_COMMAND_CONFIG, 0x14 bytes): a flag word naming the flag(s) to change, two unused dwords,
    /// then SetFlg (1 = on, 2 = off). C2S 0x0DB (GP_CLI_COMMAND_CONFIG_LANGUAGE, 0x28 bytes): two zero bytes, Kind
    /// (0 = the configuration words: SAVE_CONF flag word and both chat-filter words in ConfigSys[0..2]; 1 = party
    /// search languages in Param), a padding byte, ConfigSys[3], four padding dwords and Param. Structures
    /// referenced from XiPackets (world/client/0x00DC, world/client/0x00DB) and LandSandBoat
    /// (<c>packets/c2s/0x0dc_config.h</c>, <c>0x0db_config_language.h</c>); confirmed 2026-09-26.
    /// </para>
    /// </summary>
    public static class ConfigOutboundPackets
    {
        public const int ConfigSubPacketSize = 0x14;
        public const int ConfigLanguageSubPacketSize = 0x28;

        /// <summary>Bit position of SysMesFilterLevel in the flag word.</summary>
        public const int SystemMessageFilterLevelShift = 11;

        /// <summary>The flag word with its system message filter level replaced (0-3).</summary>
        public static uint WithSystemMessageFilterLevel(uint flags, int level) =>
            (flags & ~(uint)PlayerConfigFlags.SysMesFilterLevelMask) | ((uint)Math.Clamp(level, 0, 3) << SystemMessageFilterLevelShift);

        /// <summary>The 0x0DB kind that carries the configuration words (flags and chat filters).</summary>
        public const byte ConfigLanguageKindConfigWords = 0;

        /// <summary>The 0x0DB kind that carries the party-search languages.</summary>
        public const byte ConfigLanguageKindPartyLanguages = 1;

        private const byte SetFlagOn = 1;
        private const byte SetFlagOff = 2;

        /// <summary>Builds C2S 0x0DC setting (or clearing) the given flag(s).</summary>
        public static void BuildConfig(Span<byte> destination, PlayerConfigFlags flags, bool on, ushort sequenceId = 0)
        {
            if (destination.Length < ConfigSubPacketSize)
                throw new ArgumentException($"Destination must be at least {ConfigSubPacketSize} bytes.", nameof(destination));

            destination.Slice(0, ConfigSubPacketSize).Clear();
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(0, 2), (ushort)(0x0DC | ((ConfigSubPacketSize / 4) << 9)));
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(2, 2), sequenceId);
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(4, 4), (uint)flags);
            destination[16] = on ? SetFlagOn : SetFlagOff;
        }

        public static byte[] BuildConfig(PlayerConfigFlags flags, bool on, ushort sequenceId = 0)
        {
            byte[] packet = new byte[ConfigSubPacketSize];
            BuildConfig(packet, flags, on, sequenceId);
            return packet;
        }

        /// <summary>
        /// Builds C2S 0x0DB kind 0 carrying the configuration words. <paramref name="flags"/> must be the server's
        /// current flag word (S2C 0x0B4): LandSandBoat copies the whole word, so a zero would clear /invite, /away,
        /// /anon and the auto-target setting.
        /// </summary>
        public static void BuildChatFilters(Span<byte> destination, uint flags, uint messageFilter1, uint messageFilter2, ushort sequenceId = 0)
        {
            if (destination.Length < ConfigLanguageSubPacketSize)
                throw new ArgumentException($"Destination must be at least {ConfigLanguageSubPacketSize} bytes.", nameof(destination));

            destination.Slice(0, ConfigLanguageSubPacketSize).Clear();
            WriteConfigLanguageHeader(destination, ConfigLanguageKindConfigWords, sequenceId);
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(8, 4), flags);
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(12, 4), messageFilter1);
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(16, 4), messageFilter2);
        }

        public static byte[] BuildChatFilters(uint flags, uint messageFilter1, uint messageFilter2, ushort sequenceId = 0)
        {
            byte[] packet = new byte[ConfigLanguageSubPacketSize];
            BuildChatFilters(packet, flags, messageFilter1, messageFilter2, sequenceId);
            return packet;
        }

        /// <summary>Builds C2S 0x0DB kind 1 carrying the party-search languages.</summary>
        public static void BuildPartyLanguages(Span<byte> destination, PartyLanguages languages, ushort sequenceId = 0)
        {
            if (destination.Length < ConfigLanguageSubPacketSize)
                throw new ArgumentException($"Destination must be at least {ConfigLanguageSubPacketSize} bytes.", nameof(destination));

            destination.Slice(0, ConfigLanguageSubPacketSize).Clear();
            WriteConfigLanguageHeader(destination, ConfigLanguageKindPartyLanguages, sequenceId);
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(36, 4), (uint)languages);
        }

        public static byte[] BuildPartyLanguages(PartyLanguages languages, ushort sequenceId = 0)
        {
            byte[] packet = new byte[ConfigLanguageSubPacketSize];
            BuildPartyLanguages(packet, languages, sequenceId);
            return packet;
        }

        private static void WriteConfigLanguageHeader(Span<byte> destination, byte kind, ushort sequenceId)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(0, 2), (ushort)(0x0DB | ((ConfigLanguageSubPacketSize / 4) << 9)));
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(2, 2), sequenceId);
            destination[6] = kind;
        }
    }

    /// <summary>
    /// Packet domain module for the character's configuration: keeps <see cref="PlayerConfigState"/> from S2C
    /// 0x0B4 and sends the flag (0x0DC) and chat-filter / language (0x0DB) changes the config menu makes.
    /// </summary>
    public sealed class ConfigPacketModule
    {
        private readonly PlayerConfigState _state;
        private readonly Func<ReadOnlyMemory<byte>, bool, Task> _sendChunkCallback;
        private readonly Action<PacketDirection, ushort, ushort, ReadOnlySpan<byte>>? _logPacketCallback;

        public PlayerConfigState State => _state;
        public bool LogOutboundOnRoute { get; set; } = true;

        public ConfigPacketModule(
            PlayerConfigState state,
            Func<ReadOnlyMemory<byte>, bool, Task> sendChunkCallback,
            Action<PacketDirection, ushort, ushort, ReadOnlySpan<byte>>? logPacketCallback = null)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _sendChunkCallback = sendChunkCallback ?? throw new ArgumentNullException(nameof(sendChunkCallback));
            _logPacketCallback = logPacketCallback;
        }

        public void Register(IPacketDispatcher dispatcher)
        {
            ArgumentNullException.ThrowIfNull(dispatcher);
            dispatcher.Register(S2C_0x0B4_Config.PacketId, HandleConfig);
        }

        public void Unregister(IPacketDispatcher dispatcher)
        {
            ArgumentNullException.ThrowIfNull(dispatcher);
            dispatcher.Unregister(S2C_0x0B4_Config.PacketId);
        }

        private void HandleConfig(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var config = new S2C_0x0B4_Config(payload);
            if (!config.IsValid)
            {
                GordianLog.Warning("NET", $"S2C 0x0B4 Config payload too short ({payload.Length} bytes).");
                return;
            }
            _state.Apply(config);
            GordianLog.Debug("NET", $"S2C 0x0B4 Config: flags=0x{config.Flags:X8} filters=0x{config.MessageFilter1:X8}/0x{config.MessageFilter2:X8} languages={config.PartyLanguages}");
        }

        /// <summary>Sets or clears one player flag (C2S 0x0DC); the server answers with 0x0B4.</summary>
        public async Task SetFlagAsync(PlayerConfigFlags flag, bool on)
        {
            byte[] packet = ConfigOutboundPackets.BuildConfig(flag, on);
            uint expected = on ? _state.Flags | (uint)flag : _state.Flags & ~(uint)flag;
            _state.Expect(flags: expected);
            if (LogOutboundOnRoute) _logPacketCallback?.Invoke(PacketDirection.Outbound, 0x0DC, 0, packet);
            await _sendChunkCallback(packet, true).ConfigureAwait(false);
        }

        /// <summary>Sends both chat-filter words (C2S 0x0DB kind 0), echoing the server's current flag word.</summary>
        public async Task SetChatFiltersAsync(uint messageFilter1, uint messageFilter2)
        {
            byte[] packet = ConfigOutboundPackets.BuildChatFilters(_state.Flags, messageFilter1, messageFilter2);
            _state.Expect(filter1: messageFilter1, filter2: messageFilter2);
            if (LogOutboundOnRoute) _logPacketCallback?.Invoke(PacketDirection.Outbound, 0x0DB, 0, packet);
            await _sendChunkCallback(packet, true).ConfigureAwait(false);
        }

        /// <summary>
        /// Sets the 2-bit system message filter level (0-3) by sending the configuration words (C2S 0x0DB kind 0)
        /// with the level replaced in the echoed flag word; LandSandBoat stores the whole word.
        /// </summary>
        public async Task SetSystemMessageFilterLevelAsync(int level)
        {
            uint flags = ConfigOutboundPackets.WithSystemMessageFilterLevel(_state.Flags, level);
            byte[] packet = ConfigOutboundPackets.BuildChatFilters(flags, _state.MessageFilter1, _state.MessageFilter2);
            _state.Expect(flags: flags);
            if (LogOutboundOnRoute) _logPacketCallback?.Invoke(PacketDirection.Outbound, 0x0DB, 0, packet);
            await _sendChunkCallback(packet, true).ConfigureAwait(false);
        }

        /// <summary>Sends the party-search languages (C2S 0x0DB kind 1).</summary>
        public async Task SetPartyLanguagesAsync(PartyLanguages languages)
        {
            byte[] packet = ConfigOutboundPackets.BuildPartyLanguages(languages);
            if (LogOutboundOnRoute) _logPacketCallback?.Invoke(PacketDirection.Outbound, 0x0DB, 0, packet);
            await _sendChunkCallback(packet, true).ConfigureAwait(false);
        }
    }
}
