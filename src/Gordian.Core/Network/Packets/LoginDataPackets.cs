// src/Gordian.Core/Network/Packets/LoginDataPackets.cs
using System;
using System.Buffers.Binary;
using System.Text;

namespace Gordian.Core.Network.Packets
{
    /// <summary>
    /// The Alter Ego (Trust) upgrade categories: the <c>Kind</c> of C2S 0x0C1 and the index into the <c>count</c> /
    /// <c>next</c> arrays of S2C 0x08E.
    /// Values 8-16 referenced from XiPackets (https://github.com/atom0s/XiPackets), <c>world/client/0x00C1</c>; 17 and 18
    /// from LandSandBoat (https://github.com/LandSandBoat/server), <c>src/map/enums/alter_ego_points.h</c>.
    /// </summary>
    public enum AlterEgoCategory : ushort
    {
        MaxHp = 8,
        MaxMp = 9,
        Str = 10,
        Dex = 11,
        Vit = 12,
        Agi = 13,
        Int = 14,
        Mnd = 15,
        Chr = 16,
        /// <summary>LandSandBoat only; not listed by XiPackets.</summary>
        CombatSkills = 17,
        /// <summary>LandSandBoat only; not listed by XiPackets.</summary>
        MagicSkills = 18
    }

    /// <summary>
    /// The automaton equipment slots of C2S 0x102 (Puppetmaster): head, frame, then the 12 attachment slots, left to
    /// right from the top row.
    /// Values referenced from XiPackets (https://github.com/atom0s/XiPackets), <c>world/client/0x0102</c>.
    /// </summary>
    public enum AutomatonSlot : byte
    {
        Head = 0,
        Frame = 1,
        Attachment1 = 2,
        Attachment2 = 3,
        Attachment3 = 4,
        Attachment4 = 5,
        Attachment5 = 6,
        Attachment6 = 7,
        Attachment7 = 8,
        Attachment8 = 9,
        Attachment9 = 10,
        Attachment10 = 11,
        Attachment11 = 12,
        Attachment12 = 13
    }

    /// <summary>
    /// S2C 0x0AE (mount data, 12 bytes): the unlocked mounts, a 64-bit table copied by the client into its mount
    /// system. Bit n (byte n / 8, bit n % 8) is mount n, in the order of the mount names DAT (NA file id 55681): 0
    /// Chocobo, 1 Raptor, 2 Tiger ... 0x21 Noble Chocobo, 0x22 Ixion, 0x23 Phuabo. LandSandBoat copies the first 8 bytes
    /// of key item table 6 (key items 3072 and up, <c>CHOCOBO_COMPANION</c> = 3072), so mount n is key item 3072 + n;
    /// it sends the packet on every zone-in (<c>0x00c_gameok.cpp</c>) and when a mount key item is given.
    /// Packet structure referenced from XiPackets (https://github.com/atom0s/XiPackets), <c>world/server/0x00AE</c>;
    /// server side referenced from LandSandBoat (https://github.com/LandSandBoat/server), <c>s2c/0x0ae_mount_data.cpp</c>.
    /// </summary>
    public readonly ref struct S2C_0x0AE_MountData
    {
        public const ushort PacketId = 0x0AE;
        public const int TableLength = 8;
        public const int MountCount = TableLength * 8;

        public bool IsValid { get; }

        /// <summary>The 8-byte mount bit table.</summary>
        public ReadOnlySpan<byte> MountTable { get; }

        public S2C_0x0AE_MountData(ReadOnlySpan<byte> payload)
        {
            this = default;
            if (payload.Length < TableLength) return;
            MountTable = payload.Slice(0, TableLength);
            IsValid = true;
        }

        /// <summary>True when mount <paramref name="mountIndex"/> (0 = Chocobo) is unlocked.</summary>
        public bool HasMount(int mountIndex) => LoginDataBits.Test(MountTable, mountIndex);
    }

    /// <summary>
    /// S2C 0x0AD (dungeon, 132 bytes): the Moblin Maze Mongers vouchers and runes the character has unlocked. Payload
    /// offsets: 0 <c>Vouchers[8]</c> (64 bits, bit n = item 28736 + n), 8 <c>Runes[64]</c> (512 bits, bit n = item 28800 + n),
    /// 72 <c>unused[56]</c>. The client reads the bits when a Maze Tabula is opened. LandSandBoat sends it on every
    /// zone-in and when an unlock is saved.
    /// Packet structure referenced from XiPackets (https://github.com/atom0s/XiPackets), <c>world/server/0x00AD</c>;
    /// server side referenced from LandSandBoat (https://github.com/LandSandBoat/server), <c>s2c/0x0ad_dungeon.cpp</c>.
    /// </summary>
    public readonly ref struct S2C_0x0AD_Dungeon
    {
        public const ushort PacketId = 0x0AD;
        public const int VoucherBytes = 8;
        public const int RuneBytes = 64;
        public const int MinPayloadLength = VoucherBytes + RuneBytes;
        public const int VoucherCount = VoucherBytes * 8;
        public const int RuneCount = RuneBytes * 8;

        /// <summary>The item id of voucher bit 0.</summary>
        public const ushort FirstVoucherItemId = 28736;

        /// <summary>The item id of rune bit 0.</summary>
        public const ushort FirstRuneItemId = 28800;

        public bool IsValid { get; }
        public ReadOnlySpan<byte> Vouchers { get; }
        public ReadOnlySpan<byte> Runes { get; }

        public S2C_0x0AD_Dungeon(ReadOnlySpan<byte> payload)
        {
            this = default;
            if (payload.Length < MinPayloadLength) return;
            Vouchers = payload.Slice(0, VoucherBytes);
            Runes = payload.Slice(VoucherBytes, RuneBytes);
            IsValid = true;
        }

        public bool HasVoucher(int index) => LoginDataBits.Test(Vouchers, index);
        public bool HasRune(int index) => LoginDataBits.Test(Runes, index);
    }

    /// <summary>
    /// S2C 0x08E (Alter Ego points, 104 bytes): the character's Trust points and the upgrade level and next cost of
    /// each category. Payload offsets: 0 u16 points, 2 padding, 4 <c>count[32]</c> (u8 upgrade levels), 36
    /// <c>next[32]</c> (u16 points needed for the next level). Both arrays are indexed by <see cref="AlterEgoCategory"/>.
    /// LandSandBoat sends it on every zone-in and after C2S 0x0C1, with the points from its <c>alter_ego_points</c>
    /// currency and both arrays still zero (its upgrades are not implemented yet).
    /// Packet structure referenced from XiPackets (https://github.com/atom0s/XiPackets), <c>world/server/0x008E</c>;
    /// server side referenced from LandSandBoat (https://github.com/LandSandBoat/server), <c>s2c/0x08e_alter_ego_points.cpp</c>.
    /// </summary>
    public readonly ref struct S2C_0x08E_AlterEgoPoints
    {
        public const ushort PacketId = 0x08E;
        public const int CategorySlots = 32;
        public const int MinPayloadLength = 4 + CategorySlots + CategorySlots * 2;

        public bool IsValid { get; }
        public ushort Points { get; }

        private readonly ReadOnlySpan<byte> _counts;
        private readonly ReadOnlySpan<byte> _next;

        public S2C_0x08E_AlterEgoPoints(ReadOnlySpan<byte> payload)
        {
            this = default;
            if (payload.Length < MinPayloadLength) return;
            Points = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(0, 2));
            _counts = payload.Slice(4, CategorySlots);
            _next = payload.Slice(4 + CategorySlots, CategorySlots * 2);
            IsValid = true;
        }

        /// <summary>The upgrade level of category <paramref name="index"/> (0-31).</summary>
        public byte GetUpgrade(int index) => (uint)index < CategorySlots && !_counts.IsEmpty ? _counts[index] : (byte)0;

        /// <summary>The points needed to upgrade category <paramref name="index"/> (0-31) once more.</summary>
        public ushort GetNextCost(int index)
            => (uint)index < CategorySlots && !_next.IsEmpty ? BinaryPrimitives.ReadUInt16LittleEndian(_next.Slice(index * 2, 2)) : (ushort)0;
    }

    /// <summary>
    /// S2C 0x044 (extended job data, 160 bytes): job-specific data for the main job or the support job. Payload offsets:
    /// 0 <c>JobId</c>, 1 <c>IsSubJob</c>; the rest depends on the job. The client copies the packet into its main- or
    /// sub-job buffer and ignores it when <c>JobId</c> is not that job. LandSandBoat sends one per Blue Mage or
    /// Puppetmaster main / support job on every zone-in and after C2S 0x102, and a Monstrosity one (main job 23) instead
    /// while the character is a monster (<c>charutils::SendExtendedJobPackets</c>). Layouts (payload offsets) from
    /// LandSandBoat; XiPackets leaves the data unreversed:
    /// <list type="bullet">
    /// <item>Blue Mage (16): 4 <c>SetSpells[20]</c>, each a spell id less 512 (0 = empty slot).</item>
    /// <item>Puppetmaster (18): 4 head, 5 frame, 6 <c>Attachments[12]</c>, 20 u32 unlocked heads, 24 u32 unlocked frames,
    /// 52 u32 <c>UnlockedAttachments[8]</c>, 84 <c>Name[16]</c>, 100 u16 HP, MaxHP, MP, MaxMP, 108 u16 melee skill and cap,
    /// ranged skill and cap, magic skill and cap, 124 u16 STR, STR bonus, DEX ... CHR bonus (14 words), 152 elemental
    /// capacity bonus. Head and frame are item ids less 8192, attachments item ids less 8448.</item>
    /// <item>Monstrosity (23): 4 u16 species, 8 u16 <c>EquippedInstincts[12]</c>.</item>
    /// </list>
    /// Packet structure referenced from XiPackets (https://github.com/atom0s/XiPackets), <c>world/server/0x0044</c>;
    /// data layouts referenced from LandSandBoat (https://github.com/LandSandBoat/server),
    /// <c>s2c/0x044_extended_job_blu.h</c>, <c>0x044_extended_job_pup.h</c> and <c>0x044_extended_job_mon.h</c>.
    /// </summary>
    public readonly ref struct S2C_0x044_ExtendedJob
    {
        public const ushort PacketId = 0x044;
        public const int MinPayloadLength = 4;
        public const int FullPayloadLength = 156;

        /// <summary>The job number LandSandBoat uses for Monstrosity (not a <see cref="JobId"/>).</summary>
        public const byte MonstrosityJobNo = 23;

        public const int BlueSpellSlots = 20;
        public const int AutomatonAttachmentSlots = 12;
        public const int MonstrosityInstinctSlots = 12;

        /// <summary>Added to a head or frame value to get its item id (XiPackets C2S 0x102).</summary>
        public const ushort AutomatonFrameItemBase = 8192;

        /// <summary>Added to an attachment value to get its item id (XiPackets C2S 0x102).</summary>
        public const ushort AutomatonAttachmentItemBase = 8448;

        /// <summary>Added to a set spell value to get its spell id (XiPackets C2S 0x102).</summary>
        public const ushort BlueSpellIdBase = 512;

        public bool IsValid { get; }
        public byte JobNo { get; }
        public bool IsSubJob { get; }

        /// <summary>The whole payload (padded to <see cref="FullPayloadLength"/> by the accessors when shorter).</summary>
        public ReadOnlySpan<byte> Payload { get; }

        public JobId Job => (JobId)JobNo;
        public bool IsBlueMage => JobNo == (byte)JobId.BlueMage;
        public bool IsPuppetmaster => JobNo == (byte)JobId.Puppetmaster;
        public bool IsMonstrosity => JobNo == MonstrosityJobNo;

        public S2C_0x044_ExtendedJob(ReadOnlySpan<byte> payload)
        {
            this = default;
            if (payload.Length < MinPayloadLength) return;
            JobNo = payload[0];
            IsSubJob = payload[1] != 0;
            Payload = payload;
            IsValid = true;
        }

        private byte U8(int offset) => offset < Payload.Length ? Payload[offset] : (byte)0;

        private ushort U16(int offset)
            => offset + 2 <= Payload.Length ? BinaryPrimitives.ReadUInt16LittleEndian(Payload.Slice(offset, 2)) : (ushort)0;

        private uint U32(int offset)
            => offset + 4 <= Payload.Length ? BinaryPrimitives.ReadUInt32LittleEndian(Payload.Slice(offset, 4)) : 0u;

        // ---- Blue Mage ----

        /// <summary>The spell set in Blue Mage slot <paramref name="slot"/> (0-19), as its id less 512; 0 = empty.</summary>
        public byte GetBlueSpell(int slot) => (uint)slot < BlueSpellSlots ? U8(4 + slot) : (byte)0;

        // ---- Puppetmaster ----

        public byte AutomatonHead => U8(4);
        public byte AutomatonFrame => U8(5);

        /// <summary>The attachment in slot <paramref name="slot"/> (0-11), as its item id less 8448; 0 = empty.</summary>
        public byte GetAutomatonAttachment(int slot) => (uint)slot < AutomatonAttachmentSlots ? U8(6 + slot) : (byte)0;

        public uint UnlockedHeads => U32(20);
        public uint UnlockedFrames => U32(24);

        /// <summary>Word <paramref name="index"/> (0-7) of the unlocked attachment bits (bit n = attachment n).</summary>
        public uint GetUnlockedAttachmentWord(int index) => (uint)index < 8 ? U32(52 + index * 4) : 0u;

        public string AutomatonName
        {
            get
            {
                if (Payload.Length < 85) return string.Empty;
                var name = Payload.Slice(84, Math.Min(16, Payload.Length - 84));
                int end = name.IndexOf((byte)0);
                if (end < 0) end = name.Length;
                return Encoding.ASCII.GetString(name.Slice(0, end));
            }
        }

        public ushort AutomatonHp => U16(100);
        public ushort AutomatonMaxHp => U16(102);
        public ushort AutomatonMp => U16(104);
        public ushort AutomatonMaxMp => U16(106);
        public ushort AutomatonMeleeSkill => U16(108);
        public ushort AutomatonMeleeSkillCap => U16(110);
        public ushort AutomatonRangedSkill => U16(112);
        public ushort AutomatonRangedSkillCap => U16(114);
        public ushort AutomatonMagicSkill => U16(116);
        public ushort AutomatonMagicSkillCap => U16(118);

        /// <summary>Automaton stat <paramref name="stat"/> (0 STR, 1 DEX, 2 VIT, 3 AGI, 4 INT, 5 MND, 6 CHR).</summary>
        public ushort GetAutomatonStat(int stat) => (uint)stat < 7 ? U16(124 + stat * 4) : (ushort)0;

        /// <summary>The bonus to automaton stat <paramref name="stat"/> (same order as <see cref="GetAutomatonStat"/>).</summary>
        public short GetAutomatonStatBonus(int stat) => (uint)stat < 7 ? (short)U16(126 + stat * 4) : (short)0;

        public byte AutomatonElementalCapacityBonus => U8(152);

        // ---- Monstrosity ----

        public ushort MonstrositySpecies => U16(4);

        /// <summary>The instinct in slot <paramref name="slot"/> (0-11).</summary>
        public ushort GetMonstrosityInstinct(int slot) => (uint)slot < MonstrosityInstinctSlots ? U16(8 + slot * 2) : (ushort)0;
    }

    /// <summary>LSB-first bit tests over the bit tables of S2C 0x0AE and 0x0AD (bit n = byte n / 8, bit n % 8).</summary>
    public static class LoginDataBits
    {
        public static bool Test(ReadOnlySpan<byte> table, int index)
            => index >= 0 && (index >> 3) < table.Length && (table[index >> 3] & (1 << (index & 7))) != 0;
    }

    /// <summary>
    /// Builders for the login-time data requests: Alter Ego upgrades (C2S 0x0C1), Moblin Maze Mongers parameters (0x0D8),
    /// Blue Mage spells and automaton equipment (0x102), the job mastery display (0x11B) and map markers (0x114). Every
    /// size is the one LandSandBoat's <c>ValidatedPacketHandler</c> expects (the struct rounded up to 4 bytes).
    /// </summary>
    public static class LoginDataPacketBuilder
    {
        public const int ExtendedJobPacketLength = 0xA4;
        public const int DungeonParamPacketLength = 0x28;
        public const int DungeonDataLength = 24;

        private static byte[] BuildWords(ushort opcode, int wordCount, ushort sequenceId)
        {
            var packet = new byte[wordCount * 4];
            PacketHeader.Write(packet, opcode, (ushort)wordCount, sequenceId);
            return packet;
        }

        /// <summary>
        /// Builds C2S 0x0C1 (Alter Ego points, 8 bytes): upgrades one Trust category. Layout: 4 u16 <c>Kind</c>, 6 padding.
        /// LandSandBoat only accepts it in the Mog House, with the Cipher Bracelet key item and a level 99 main job, and
        /// for now only answers with S2C 0x08E (no upgrade is applied).
        /// Packet structure referenced from XiPackets (https://github.com/atom0s/XiPackets), <c>world/client/0x00C1</c>;
        /// server side referenced from LandSandBoat (https://github.com/LandSandBoat/server), <c>c2s/0x0c1_alter_ego_points.cpp</c>.
        /// </summary>
        public static byte[] BuildAlterEgoUpgrade(AlterEgoCategory category, ushort sequenceId = 0)
        {
            var packet = BuildWords(0x0C1, 2, sequenceId);
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), (ushort)category);
            return packet;
        }

        /// <summary>
        /// Builds C2S 0x0D8 (dungeon parameters, 40 bytes): a Moblin Maze Mongers tabula change. Layout: 4 u16
        /// <c>ActIndex</c> (the character's own), 6 u16 <c>Param1</c>, 8 u8 <c>Param2</c> (both from the event script),
        /// 9 padding, 12 u32 <c>UniqueNo</c> (the character's id), 16 <c>Data[24]</c> (bit-packed, not reversed).
        /// LandSandBoat checks the id and index match the character and otherwise only logs the packet.
        /// Packet structure referenced from XiPackets (https://github.com/atom0s/XiPackets), <c>world/client/0x00D8</c>;
        /// server side referenced from LandSandBoat (https://github.com/LandSandBoat/server), <c>c2s/0x0d8_dungeon_param.cpp</c>.
        /// </summary>
        public static byte[] BuildDungeonParam(uint uniqueNo, ushort actIndex, ushort param1, byte param2, ReadOnlySpan<byte> data, ushort sequenceId = 0)
        {
            var packet = BuildWords(0x0D8, DungeonParamPacketLength / 4, sequenceId);
            var span = packet.AsSpan();
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(4, 2), actIndex);
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(6, 2), param1);
            span[8] = param2;
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(12, 4), uniqueNo);
            data.Slice(0, Math.Min(data.Length, DungeonDataLength)).CopyTo(span.Slice(16, DungeonDataLength));
            return packet;
        }

        /// <summary>
        /// Builds C2S 0x102 (extended job, 164 bytes) for Blue Mage set spells. Layout: 4 <c>SpellId</c> (the spell being
        /// set, as its id less 512, or 0 when removing), 5 unknown (0), 8 <c>JobIndex</c> (16), 9 <c>SupportJobFlg</c>,
        /// 12 <c>Spells[20]</c>: the slots being changed hold the spell (set or removed); the rest are 0. To set, pass the
        /// spell as <paramref name="spellId"/> and in its slot; to remove, pass 0 and the removed spell in its slot (several
        /// slots at once clear them all). LandSandBoat answers with S2C 0x044, 0x0AC and 0x061.
        /// Packet structure referenced from XiPackets (https://github.com/atom0s/XiPackets), <c>world/client/0x0102</c>;
        /// server side referenced from LandSandBoat (https://github.com/LandSandBoat/server), <c>c2s/0x102_extended_job.cpp</c>.
        /// </summary>
        public static byte[] BuildBlueSpells(byte spellId, ReadOnlySpan<byte> slots, bool subJob, ushort sequenceId = 0)
        {
            var packet = BuildWords(0x102, ExtendedJobPacketLength / 4, sequenceId);
            packet[4] = spellId;
            packet[8] = (byte)JobId.BlueMage;
            packet[9] = subJob ? (byte)1 : (byte)0;
            slots.Slice(0, Math.Min(slots.Length, S2C_0x044_ExtendedJob.BlueSpellSlots)).CopyTo(packet.AsSpan(12));
            return packet;
        }

        /// <summary>Builds C2S 0x102 that sets <paramref name="spellId"/> (id less 512) into Blue Mage slot <paramref name="slot"/> (0-19).</summary>
        public static byte[] BuildSetBlueSpell(int slot, byte spellId, bool subJob, ushort sequenceId = 0)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(slot);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(slot, S2C_0x044_ExtendedJob.BlueSpellSlots);
            Span<byte> slots = stackalloc byte[S2C_0x044_ExtendedJob.BlueSpellSlots];
            slots[slot] = spellId;
            return BuildBlueSpells(spellId, slots, subJob, sequenceId);
        }

        /// <summary>Builds C2S 0x102 that removes <paramref name="setSpellId"/> (the spell now set, id less 512) from slot <paramref name="slot"/>.</summary>
        public static byte[] BuildRemoveBlueSpell(int slot, byte setSpellId, bool subJob, ushort sequenceId = 0)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(slot);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(slot, S2C_0x044_ExtendedJob.BlueSpellSlots);
            Span<byte> slots = stackalloc byte[S2C_0x044_ExtendedJob.BlueSpellSlots];
            slots[slot] = setSpellId;
            return BuildBlueSpells(0, slots, subJob, sequenceId);
        }

        /// <summary>
        /// Builds C2S 0x102 (extended job, 164 bytes) for the automaton. Layout: 4 <c>ItemId</c> (the part being equipped,
        /// head / frame as item id less 8192, attachments less 8448, or 0 when removing), 5 unknown (0), 8 <c>JobIndex</c>
        /// (18), 9 <c>SupportJobFlg</c>, 12 <c>Slots[14]</c> in <see cref="AutomatonSlot"/> order: the slots being changed
        /// hold the part (equipped or removed). LandSandBoat refuses it while an automaton is out and only removes
        /// attachments (a head or frame is always equipped); it answers with S2C 0x044.
        /// Packet structure referenced from XiPackets (https://github.com/atom0s/XiPackets), <c>world/client/0x0102</c>;
        /// server side referenced from LandSandBoat (https://github.com/LandSandBoat/server), <c>c2s/0x102_extended_job.cpp</c>.
        /// </summary>
        public static byte[] BuildAutomatonParts(byte itemId, ReadOnlySpan<byte> slots, bool subJob, ushort sequenceId = 0)
        {
            var packet = BuildWords(0x102, ExtendedJobPacketLength / 4, sequenceId);
            packet[4] = itemId;
            packet[8] = (byte)JobId.Puppetmaster;
            packet[9] = subJob ? (byte)1 : (byte)0;
            slots.Slice(0, Math.Min(slots.Length, 14)).CopyTo(packet.AsSpan(12));
            return packet;
        }

        /// <summary>Builds C2S 0x102 that equips <paramref name="partId"/> (normalized as for <see cref="BuildAutomatonParts"/>) in <paramref name="slot"/>.</summary>
        public static byte[] BuildEquipAutomatonPart(AutomatonSlot slot, byte partId, bool subJob, ushort sequenceId = 0)
        {
            Span<byte> slots = stackalloc byte[14];
            slots[(int)slot] = partId;
            return BuildAutomatonParts(partId, slots, subJob, sequenceId);
        }

        /// <summary>Builds C2S 0x102 that removes the attachment <paramref name="attachmentId"/> (now equipped) from <paramref name="slot"/>.</summary>
        public static byte[] BuildRemoveAutomatonAttachment(AutomatonSlot slot, byte attachmentId, bool subJob, ushort sequenceId = 0)
        {
            if (slot < AutomatonSlot.Attachment1) throw new ArgumentOutOfRangeException(nameof(slot), "Only attachments can be removed.");
            Span<byte> slots = stackalloc byte[14];
            slots[(int)slot] = attachmentId;
            return BuildAutomatonParts(0, slots, subJob, sequenceId);
        }

        /// <summary>
        /// Builds C2S 0x11B (mastery display, 8 bytes): <c>/jobmasterdisp</c>. Layout: 4 u8 <c>Mode</c> (0 off, 1 on). LandSandBoat
        /// saves the setting and, when it changed, answers with S2C 0x037 and 0x067.
        /// Packet structure referenced from XiPackets (https://github.com/atom0s/XiPackets), <c>world/client/0x011B</c>;
        /// server side referenced from LandSandBoat (https://github.com/LandSandBoat/server), <c>c2s/0x11b_mastery_display.cpp</c>.
        /// </summary>
        public static byte[] BuildMasteryDisplay(bool on, ushort sequenceId = 0)
        {
            var packet = BuildWords(0x11B, 2, sequenceId);
            packet[4] = on ? (byte)1 : (byte)0;
            return packet;
        }

        /// <summary>
        /// Builds C2S 0x114 (map markers, 4 bytes, header only): asks for the current map's markers (home points, survival
        /// guides, waypoints ...). LandSandBoat answers with S2C 0x063 type 6 (the teleport masks).
        /// Packet structure referenced from XiPackets (https://github.com/atom0s/XiPackets), <c>world/client/0x0114</c>;
        /// server side referenced from LandSandBoat (https://github.com/LandSandBoat/server), <c>c2s/0x114_map_markers.cpp</c>.
        /// </summary>
        public static byte[] BuildMapMarkers(ushort sequenceId = 0) => BuildWords(0x114, 1, sequenceId);
    }
}
