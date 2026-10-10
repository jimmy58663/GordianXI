// src/Gordian.Core/Network/Packets/EquipInspectPackets.cs
using System;
using System.Buffers.Binary;

namespace Gordian.Core.Network.Packets
{
    /// <summary>
    /// The <c>OptionFlag</c> of S2C 0x0C9: how the rest of the packet reads. Anything else is a single item (mode 0).
    /// Referenced from XiPackets (https://github.com/atom0s/XiPackets/tree/main/world/server/0x00C9).
    /// </summary>
    public enum EquipInspectMode : byte
    {
        SingleItem = 0,
        General = 1,
        ItemList = 2,
        Equipment = 3,
    }

    /// <summary>
    /// One equipped item of a checked character (S2C 0x0C9): its id and slot. The slot numbers are the client's
    /// <c>SAVE_EQUIP_KIND</c>, the same order as <see cref="EquipSlotId"/> (11-14 = Ear1, Ear2, Ring1, Ring2).
    /// </summary>
    public readonly record struct EquipInspectItem(ushort ItemId, EquipSlotId Slot);

    /// <summary>
    /// S2C 0x0C9 (GP_SERV_COMMAND_EQUIP_INSPECT): the reply to checking another player (C2S 0x0DD kind 0). LandSandBoat
    /// sends S2C 0x0CA (name, bazaar message, title) first, then this packet with mode 3 (the equipment, up to eight items
    /// a packet, as many packets as needed) and last mode 1 (the general information). Payload (after the 4-byte
    /// header): 0 u32 <c>UniqNo</c>, 4 u16 <c>ActIndex</c>, 6 u8 <c>OptionFlag</c>, then by mode:
    /// <list type="bullet">
    /// <item>1 (General, 80 bytes): 10 u16 linkshell item id, 12 <c>sComLinkName[16]</c> (the 6-bit packed linkshell name),
    /// 28 u16 linkshell colour (4 bits each of r, g, b, a from the low bits), 30 main and sub job, 32 their levels (all 0
    /// when the character is anonymous), 34 the mastery job, 35 its level, 36 mastery flags (1 = unlocked, 2 = capped), 40
    /// u32 Ballista chevrons, 44 chevron flags, 46 u16 Ballista flags, 48 u32 message id, 52 five s32 parameters.</item>
    /// <item>3 (Equipment): 7 u8 <c>EquipCount</c>, then that many 28-byte <c>checkitem_t</c> from 8: u16 item id, u8 slot,
    /// a pad byte, 24 bytes of the item's extra data (charges and timers or augments, the signature at 12).</item>
    /// <item>2 (ItemList, older): 7 u16 item ids[8], 23 u8 slots[8], ending at the first id of 0.</item>
    /// <item>else (single item, older): 8 u16 item id, 10 u8 slot.</item>
    /// </list>
    /// Only occupied slots are sent; an item id of 0 ends a list early. Packet structure referenced from XiPackets
    /// (https://github.com/atom0s/XiPackets/tree/main/world/server/0x00C9) and LandSandBoat
    /// (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x0c9_equip_inspect_general.cpp,
    /// <c>0x0c9_equip_inspect_equipment.cpp</c>).
    /// </summary>
    public readonly ref struct S2C_0x0C9_EquipInspect
    {
        public const ushort PacketId = 0x0C9;

        /// <summary>The shortest payload: the sub-header (UniqNo, ActIndex, OptionFlag) and one byte.</summary>
        public const int HeaderLength = 8;

        /// <summary>The general (mode 1) payload's length.</summary>
        public const int GeneralLength = 80;

        /// <summary>The size of one mode-3 item entry (<c>checkitem_t</c>).</summary>
        public const int EquipEntrySize = 28;

        /// <summary>Most items one mode-2 or mode-3 packet carries.</summary>
        public const int MaxItemsPerPacket = 8;

        public bool IsValid { get; }
        public uint ServerId { get; }
        public ushort TargetIndex { get; }
        public byte OptionFlag { get; }

        private readonly ReadOnlySpan<byte> _payload;

        public S2C_0x0C9_EquipInspect(ReadOnlySpan<byte> payload)
        {
            this = default;
            _payload = payload;
            if (payload.Length < HeaderLength) return;
            ServerId = BinaryPrimitives.ReadUInt32LittleEndian(payload);
            TargetIndex = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(4, 2));
            OptionFlag = payload[6];
            IsValid = Mode != EquipInspectMode.General || payload.Length >= GeneralLength;
        }

        /// <summary>The packet's mode (any unknown flag reads as the single-item mode, as the client does).</summary>
        public EquipInspectMode Mode => OptionFlag is 1 or 2 or 3 ? (EquipInspectMode)OptionFlag : EquipInspectMode.SingleItem;

        /// <summary>
        /// Copies the items this packet carries (modes 0, 2 and 3) into <paramref name="destination"/> and returns how many,
        /// stopping at an item id of 0 or a slot out of range, as the client does.
        /// </summary>
        public int ReadItems(Span<EquipInspectItem> destination)
        {
            if (!IsValid) return 0;
            int count = 0;
            switch (Mode)
            {
                case EquipInspectMode.Equipment:
                {
                    int declared = Math.Min((int)_payload[7], MaxItemsPerPacket);
                    for (int i = 0; i < declared && count < destination.Length; i++)
                    {
                        int at = 8 + i * EquipEntrySize;
                        if (at + 4 > _payload.Length) break;
                        ushort id = BinaryPrimitives.ReadUInt16LittleEndian(_payload.Slice(at, 2));
                        if (id == 0) break;
                        byte slot = _payload[at + 2];
                        if (slot > (byte)EquipSlotId.Back) continue;
                        destination[count++] = new EquipInspectItem(id, (EquipSlotId)slot);
                    }
                    break;
                }
                case EquipInspectMode.ItemList:
                {
                    for (int i = 0; i < MaxItemsPerPacket && count < destination.Length; i++)
                    {
                        int idAt = 7 + i * 2, slotAt = 23 + i;
                        if (slotAt >= _payload.Length) break;
                        ushort id = BinaryPrimitives.ReadUInt16LittleEndian(_payload.Slice(idAt, 2));
                        if (id == 0) break;
                        byte slot = _payload[slotAt];
                        if (slot > (byte)EquipSlotId.Back) continue;
                        destination[count++] = new EquipInspectItem(id, (EquipSlotId)slot);
                    }
                    break;
                }
                case EquipInspectMode.SingleItem:
                {
                    if (_payload.Length < 11 || destination.Length == 0) break;
                    ushort id = BinaryPrimitives.ReadUInt16LittleEndian(_payload.Slice(8, 2));
                    byte slot = _payload[10];
                    if (id != 0 && slot <= (byte)EquipSlotId.Back) destination[count++] = new EquipInspectItem(id, (EquipSlotId)slot);
                    break;
                }
            }
            return count;
        }

        private bool IsGeneral => IsValid && Mode == EquipInspectMode.General;

        /// <summary>The equipped linkshell's item id (mode 1; 0 without one).</summary>
        public ushort LinkshellItemId => IsGeneral ? BinaryPrimitives.ReadUInt16LittleEndian(_payload.Slice(10, 2)) : (ushort)0;

        /// <summary>The equipped linkshell's name (mode 1; empty without one).</summary>
        public string LinkshellName => IsGeneral ? LinkshellNameCodec.Decode(_payload.Slice(12, 16)) : string.Empty;

        /// <summary>The linkshell colour as sent: 4 bits each of red (bits 0-3), green, blue and alpha.</summary>
        public ushort LinkshellColor => IsGeneral ? BinaryPrimitives.ReadUInt16LittleEndian(_payload.Slice(28, 2)) : (ushort)0;

        public byte MainJob => IsGeneral ? _payload[30] : (byte)0;
        public byte SubJob => IsGeneral ? _payload[31] : (byte)0;
        public byte MainJobLevel => IsGeneral ? _payload[32] : (byte)0;
        public byte SubJobLevel => IsGeneral ? _payload[33] : (byte)0;
        public byte MasteryJob => IsGeneral ? _payload[34] : (byte)0;
        public byte MasteryLevel => IsGeneral ? _payload[35] : (byte)0;

        /// <summary>Mastery flags: 0x01 = mastery unlocked on the job (the level shown is then the mastery level), 0x02 = capped.</summary>
        public byte MasteryFlags => IsGeneral ? _payload[36] : (byte)0;

        public uint BallistaChevrons => IsGeneral ? BinaryPrimitives.ReadUInt32LittleEndian(_payload.Slice(40, 4)) : 0u;
        public byte BallistaChevronFlags => IsGeneral ? _payload[44] : (byte)0;
    }
}
