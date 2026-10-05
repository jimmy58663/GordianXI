// src/Gordian.Core/Network/Packets/SocialPackets.cs
using System;
using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace Gordian.Core.Network.Packets
{
    #region Enums

    /// <summary>
    /// The delivery box (PBX) command of C2S 0x04D and S2C 0x04B.
    /// Values referenced from XiPackets (https://github.com/atom0s/XiPackets), <c>world/client/0x004D</c> and
    /// <c>world/server/0x004B</c>, and LandSandBoat (https://github.com/LandSandBoat/server), <c>c2s/0x04d_pbx.h</c>.
    /// </summary>
    public enum DeliveryCommand : byte
    {
        None = 0x00,
        /// <summary>COM_WORK: refresh the contents of one slot of a box.</summary>
        Work = 0x01,
        /// <summary>COM_SET: put an inventory item into an outgoing slot.</summary>
        Set = 0x02,
        /// <summary>COM_SEND: send an outgoing slot.</summary>
        Send = 0x03,
        /// <summary>COM_CANCEL: take a sent item back.</summary>
        Cancel = 0x04,
        /// <summary>COM_CHECK: count the items in a box.</summary>
        Check = 0x05,
        /// <summary>COM_RECV: receive an incoming item into the box.</summary>
        Recv = 0x06,
        Confirm = 0x07,
        /// <summary>COM_ACCEPT: accept an incoming item.</summary>
        Accept = 0x08,
        /// <summary>COM_REJECT: return an incoming item to its sender.</summary>
        Reject = 0x09,
        /// <summary>COM_GET: take an item out of a box into the inventory.</summary>
        Get = 0x0A,
        /// <summary>COM_CLEAR: clear a slot.</summary>
        Clear = 0x0B,
        /// <summary>COM_QUERY: ask whether a recipient name exists.</summary>
        Query = 0x0C,
        /// <summary>COM_DELI_OPEN: enter delivery mode (the outgoing box).</summary>
        DeliOpen = 0x0D,
        /// <summary>COM_POST_OPEN: enter post mode (the incoming box).</summary>
        PostOpen = 0x0E,
        /// <summary>COM_POSTAL_CLOSE: leave delivery and post mode.</summary>
        PostalClose = 0x0F
    }

    /// <summary>
    /// The delivery box a PBX packet addresses (<c>BoxNo</c>): 1 is the incoming box, 2 the outgoing box, -1 none.
    /// Values referenced from XiPackets (https://github.com/atom0s/XiPackets), <c>world/client/0x004D</c>.
    /// </summary>
    public enum DeliveryBox : sbyte
    {
        None = -1,
        Incoming = 1,
        Outgoing = 2
    }

    /// <summary>
    /// The <c>Result</c> of S2C 0x04B: 0 fail, 1 success, 2 interim, and the negative error codes (0xFF-0xB9).
    /// Values referenced from XiPackets (https://github.com/atom0s/XiPackets), <c>world/server/0x004B</c>.
    /// </summary>
    public enum DeliveryResultCode : byte
    {
        Fail = 0x00,
        Success = 0x01,
        Interim = 0x02,
        ProbInventoryFull = 0xB9,
        YouAlreadyHaveLore = 0xBA,
        NotPostableItem = 0xBB,
        LessItemStacks = 0xBC,
        ItemWorkLocked = 0xBD,
        ItemWorkInUse = 0xBE,
        NoItemInWork = 0xBF,
        ErrorInItemSys = 0xC0,
        ConfirmUnexpectedSubject = 0xD2,
        ConfirmNotMatchContent = 0xD3,
        ConfirmNotMatchTime = 0xD4,
        ConfirmNotMatchId = 0xD5,
        ConfirmNotMatchNames = 0xD6,
        ConfirmIllegalPworkState = 0xD7,
        ConfirmIllegalPworkNo = 0xD8,
        CancelNotMatchContent = 0xDB,
        CancelNotMatchPworkNo = 0xDC,
        CancelNotMatchTime = 0xDD,
        CancelNotMatchId = 0xDE,
        CancelNotMatchNames = 0xDF,
        CancelNotFound = 0xE0,
        RcptEmpty = 0xE9,
        PostEmpty = 0xEA,
        PboxNotForGet = 0xEB,
        PboxNotForReject = 0xEC,
        PboxNotForAccept = 0xED,
        PboxWasNotSend = 0xEE,
        PboxNotForSend = 0xEF,
        PboxInUse = 0xF0,
        /// <summary>"The delivery service is currently unavailable." (XiPackets lists it as unknown.)</summary>
        ServiceUnavailable = 0xF9,
        /// <summary>"Please try again in a little while."</summary>
        Limited = 0xFA,
        NoAddress = 0xFB,
        NoBox = 0xFC,
        BoxEmpty = 0xFD,
        BoxFull = 0xFE,
        OnRequest = 0xFF
    }

    /// <summary>
    /// The state of a delivery box slot (<c>Stat</c> of the S2C 0x04B item state).
    /// Values referenced from XiPackets (https://github.com/atom0s/XiPackets), <c>world/server/0x004B</c>.
    /// </summary>
    public enum DeliveryItemStat : uint
    {
        None = 0x00,
        Set = 0x01,
        SendGoing = 0x02,
        SendDone = 0x03,
        CancelGoing = 0x04,
        CancelDone = 0x05,
        RecvInc = 0x06,
        RecvDone = 0x07,
        AcceptedBy = 0x08,
        RejectedBy = 0x09,
        Accepting = 0x0A,
        Accept = 0x0B,
        Rejecting = 0x0C,
        Reject = 0x0D,
        Get = 0x0E
    }

    /// <summary>
    /// The mode of C2S 0x03D / S2C 0x042 (blacklist edit): add 0, delete 1, and 2 for the server's error answer.
    /// Values referenced from XiPackets (https://github.com/atom0s/XiPackets), <c>world/client/0x003D</c> and
    /// <c>world/server/0x0042</c>, and LandSandBoat (https://github.com/LandSandBoat/server), <c>s2c/0x042_black_edit.h</c>.
    /// </summary>
    public enum BlacklistEditMode : sbyte
    {
        Add = 0,
        Delete = 1,
        /// <summary>LandSandBoat only: the edit failed (unknown name, or the database refused); the packet carries no entry.</summary>
        Error = 2
    }

    /// <summary>
    /// The <c>Para</c> of C2S 0x01B (the world pass vendor): begin or confirm buying a pass.
    /// Values referenced from XiPackets (https://github.com/atom0s/XiPackets), <c>world/client/0x001B</c>.
    /// </summary>
    public enum FriendPassPara : ushort
    {
        BeginPurchase = 0,
        ConfirmPurchase = 1,
        BeginGoldPurchase = 2,
        ConfirmGoldPurchase = 3
    }

    /// <summary>The client language of C2S 0x02C (<c>/itemsearch</c>).</summary>
    public enum ItemSearchLanguage : byte
    {
        Japanese = 0,
        English = 1,
        French = 2,
        German = 3
    }

    #endregion

    #region Inbound Decoders (readonly ref struct)

    /// <summary>
    /// S2C 0x04B (GP_SERV_COMMAND_PBX_RESULT): the answer to a delivery box command. 16 bytes in its short form (the
    /// <c>Represent</c> word is junk) and 88 with a <c>GP_POST_BOX_STATE</c>, which carries one slot's item, its sender or
    /// recipient and its state. Payload offsets are the XiPackets packet offsets minus the 4-byte header.
    /// <para>
    /// Packet structure referenced from XiPackets (https://github.com/atom0s/XiPackets), <c>world/server/0x004B</c>;
    /// server side referenced from LandSandBoat (https://github.com/LandSandBoat/server), <c>s2c/0x04b_pbx_result.cpp</c>.
    /// </para>
    /// </summary>
    public readonly ref struct S2C_0x04B_PbxResult
    {
        public const ushort PacketId = 0x04B;

        /// <summary>The short form: the 12 fixed bytes and the unused <c>Represent</c> word.</summary>
        public const int ShortPayloadLength = 16;

        /// <summary>The full form: the 12 fixed bytes and a 72-byte <c>GP_POST_BOX_STATE</c>.</summary>
        public const int FullPayloadLength = 84;

        private readonly ReadOnlySpan<byte> _payload;

        public bool IsValid { get; }
        public DeliveryCommand Command { get; }
        public sbyte BoxNo { get; }

        /// <summary>The slot the answer is about (0-7), or -1.</summary>
        public sbyte PostWorkNo { get; }

        /// <summary>The inventory slot of the item involved in a Set / Send, or -1 (LandSandBoat).</summary>
        public sbyte ItemWorkNo { get; }

        public int ItemStacks { get; }
        public sbyte Result { get; }

        /// <summary>The first result parameter; for Check / Query / full answers LandSandBoat puts a count here.</summary>
        public sbyte ResParam1 { get; }

        /// <summary>The second result parameter; for a Check of the incoming box, the number of items.</summary>
        public sbyte ResParam2 { get; }

        /// <summary>The third result parameter; for a Check of the outgoing box, the number of items.</summary>
        public sbyte ResParam3 { get; }

        /// <summary>The result as the named code the client switches on.</summary>
        public DeliveryResultCode ResultCode => (DeliveryResultCode)(byte)Result;

        /// <summary>True when the packet carries a <c>GP_POST_BOX_STATE</c> (the 88-byte form).</summary>
        public bool HasItemState => IsValid && _payload.Length >= FullPayloadLength;

        public S2C_0x04B_PbxResult(ReadOnlySpan<byte> payload)
        {
            _payload = payload;
            if (payload.Length < ShortPayloadLength)
            {
                IsValid = false;
                Command = DeliveryCommand.None;
                BoxNo = 0;
                PostWorkNo = 0;
                ItemWorkNo = 0;
                ItemStacks = 0;
                Result = 0;
                ResParam1 = 0;
                ResParam2 = 0;
                ResParam3 = 0;
                return;
            }

            Command = (DeliveryCommand)payload[0];
            BoxNo = (sbyte)payload[1];
            PostWorkNo = (sbyte)payload[2];
            ItemWorkNo = (sbyte)payload[3];
            ItemStacks = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(4, 4));
            Result = (sbyte)payload[8];
            ResParam1 = (sbyte)payload[9];
            ResParam2 = (sbyte)payload[10];
            ResParam3 = (sbyte)payload[11];
            IsValid = true;
        }

        /// <summary>The slot state (payload 12 <c>Stat</c>); None when the packet is the short form.</summary>
        public DeliveryItemStat Stat => HasItemState ? (DeliveryItemStat)BinaryPrimitives.ReadUInt32LittleEndian(_payload.Slice(12, 4)) : DeliveryItemStat.None;

        /// <summary>
        /// The other party's name (payload 16, 16 bytes): the recipient (<c>To</c>) of an outgoing item or the sender
        /// (<c>From</c>) of an incoming one. The client disables Return when it starts with "AH".
        /// </summary>
        public string GetName() => HasItemState ? PacketStrings.Read(_payload, 16, 16) : string.Empty;

        /// <summary>The request id (payload 32) of the box state.</summary>
        public uint RequestId => HasItemState ? BinaryPrimitives.ReadUInt32LittleEndian(_payload.Slice(32, 4)) : 0u;

        /// <summary>The request time (payload 36) of the box state.</summary>
        public uint RequestTime => HasItemState ? BinaryPrimitives.ReadUInt32LittleEndian(_payload.Slice(36, 4)) : 0u;

        /// <summary>
        /// The last word of the box state union (payload 40): <c>ItemWorkNo</c> of an outgoing item, or the opponent's box
        /// number (<c>OpponentPBoxNo</c>) of an incoming one. LandSandBoat writes the item's sub id here.
        /// </summary>
        public int StateWord => HasItemState ? BinaryPrimitives.ReadInt32LittleEndian(_payload.Slice(40, 4)) : 0;

        /// <summary>The item id (payload 44).</summary>
        public ushort ItemId => HasItemState ? BinaryPrimitives.ReadUInt16LittleEndian(_payload.Slice(44, 2)) : (ushort)0;

        /// <summary>The item kind (payload 48).</summary>
        public int Kind => HasItemState ? BinaryPrimitives.ReadInt32LittleEndian(_payload.Slice(48, 4)) : 0;

        /// <summary>The item quantity (payload 52).</summary>
        public uint Quantity => HasItemState ? BinaryPrimitives.ReadUInt32LittleEndian(_payload.Slice(52, 4)) : 0u;

        /// <summary>The item's 28 bytes of extra data (payload 56): augments, charges, signature and so on.</summary>
        public ReadOnlySpan<byte> ExtData => HasItemState ? _payload.Slice(56, 28) : ReadOnlySpan<byte>.Empty;
    }

    /// <summary>
    /// S2C 0x041 (GP_SERV_COMMAND_BLACK_LIST): a page of the character's blacklist, up to 12 entries of (character id,
    /// name). <c>Stat</c> bit 0 starts a new list (clear the old one) and bit 1 marks the last page. LandSandBoat sends
    /// the whole list at every login (and one empty page, both bits set, for an empty list).
    /// <para>
    /// Packet structure referenced from XiPackets (https://github.com/atom0s/XiPackets), <c>world/server/0x0041</c>;
    /// server side referenced from LandSandBoat (https://github.com/LandSandBoat/server), <c>s2c/0x041_black_list.cpp</c>.
    /// </para>
    /// </summary>
    public readonly ref struct S2C_0x041_BlackList
    {
        public const ushort PacketId = 0x041;
        public const int MaxEntries = 12;
        public const int EntrySize = 20;
        public const int PayloadLength = 244;

        private readonly ReadOnlySpan<byte> _payload;

        public bool IsValid { get; }

        /// <summary>The <c>Stat</c> byte (payload 240).</summary>
        public byte Stat { get; }

        /// <summary>The number of entries in this page (payload 241), clamped to 12.</summary>
        public int Count { get; }

        /// <summary>Stat bit 0: this is the first page, clear the stored list.</summary>
        public bool ResetsList => (Stat & 0x01) != 0;

        /// <summary>Stat bit 1: this is the last page, the list is complete.</summary>
        public bool IsLastPage => (Stat & 0x02) != 0;

        public S2C_0x041_BlackList(ReadOnlySpan<byte> payload)
        {
            _payload = payload;
            if (payload.Length < PayloadLength)
            {
                IsValid = false;
                Stat = 0;
                Count = 0;
                return;
            }

            Stat = payload[240];
            Count = Math.Clamp((int)(sbyte)payload[241], 0, MaxEntries);
            IsValid = true;
        }

        /// <summary>The character id of entry <paramref name="index"/>.</summary>
        public uint GetId(int index) =>
            IsValid && index >= 0 && index < Count ? BinaryPrimitives.ReadUInt32LittleEndian(_payload.Slice(index * EntrySize, 4)) : 0u;

        /// <summary>The name of entry <paramref name="index"/> (16 bytes, NUL terminated).</summary>
        public string GetName(int index) =>
            IsValid && index >= 0 && index < Count ? PacketStrings.Read(_payload, (index * EntrySize) + 4, 16) : string.Empty;
    }

    /// <summary>
    /// S2C 0x042 (GP_SERV_COMMAND_BLACK_EDIT): one entry added to or removed from the blacklist, or (LandSandBoat) a
    /// failed edit with <see cref="BlacklistEditMode.Error"/> and no entry.
    /// <para>
    /// Packet structure referenced from XiPackets (https://github.com/atom0s/XiPackets), <c>world/server/0x0042</c>;
    /// server side referenced from LandSandBoat (https://github.com/LandSandBoat/server), <c>s2c/0x042_black_edit.cpp</c>.
    /// </para>
    /// </summary>
    public readonly ref struct S2C_0x042_BlackEdit
    {
        public const ushort PacketId = 0x042;
        public const int PayloadLength = 24;

        private readonly ReadOnlySpan<byte> _payload;

        public bool IsValid { get; }

        /// <summary>The character id of the entry (payload 0).</summary>
        public uint Id { get; }

        public BlacklistEditMode Mode { get; }

        public S2C_0x042_BlackEdit(ReadOnlySpan<byte> payload)
        {
            _payload = payload;
            if (payload.Length < PayloadLength)
            {
                IsValid = false;
                Id = 0;
                Mode = BlacklistEditMode.Error;
                return;
            }

            Id = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0, 4));
            Mode = (BlacklistEditMode)(sbyte)payload[20];
            IsValid = true;
        }

        /// <summary>The name of the entry (payload 4, 16 bytes).</summary>
        public string GetName() => IsValid ? PacketStrings.Read(_payload, 4, 16) : string.Empty;
    }

    /// <summary>
    /// S2C 0x059 (GP_SERV_COMMAND_FRIENDPASS): the world pass vendor's answer. <see cref="Type"/> says what the event
    /// shows next (see XiPackets); LandSandBoat sends a stub (price 10000, a random 10-digit pass for odd requests).
    /// <para>
    /// Packet structure referenced from XiPackets (https://github.com/atom0s/XiPackets), <c>world/server/0x0059</c>;
    /// server side referenced from LandSandBoat (https://github.com/LandSandBoat/server), <c>s2c/0x059_friendpass.cpp</c>.
    /// </para>
    /// </summary>
    public readonly ref struct S2C_0x059_FriendPass
    {
        public const ushort PacketId = 0x059;
        public const int PayloadLength = 32;

        private readonly ReadOnlySpan<byte> _payload;

        public bool IsValid { get; }

        /// <summary>The uses left on the current world pass.</summary>
        public int LeftNum { get; }

        /// <summary>The time left on the pass, in hours.</summary>
        public int LeftHours { get; }

        /// <summary>The pass's price in gil.</summary>
        public int Price { get; }

        /// <summary>The packet type: what the vendor event does next.</summary>
        public byte Type { get; }

        public byte Unknown21 { get; }

        public S2C_0x059_FriendPass(ReadOnlySpan<byte> payload)
        {
            _payload = payload;
            if (payload.Length < PayloadLength)
            {
                IsValid = false;
                LeftNum = 0;
                LeftHours = 0;
                Price = 0;
                Type = 0;
                Unknown21 = 0;
                return;
            }

            LeftNum = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(0, 4));
            LeftHours = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(4, 4));
            Price = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(8, 4));
            Type = payload[28];
            Unknown21 = payload[29];
            IsValid = true;
        }

        /// <summary>The world pass string (payload 12, 16 bytes): the pass number.</summary>
        public string GetPass() => IsValid ? PacketStrings.Read(_payload, 12, 16) : string.Empty;
    }

    /// <summary>
    /// S2C 0x049: the answer to <c>/itemsearch</c> (C2S 0x02C). With <see cref="IsAsync"/> clear, <see cref="ItemId"/> is the
    /// item the server found (0 when none); with it set the client ignores the id and searches its own containers by the
    /// echoed name, one container per frame.
    /// <para>
    /// Packet structure referenced from XiPackets (https://github.com/atom0s/XiPackets), <c>world/server/0x0049</c>;
    /// server side referenced from LandSandBoat (https://github.com/LandSandBoat/server), <c>s2c/0x049_itemsearch.cpp</c>.
    /// </para>
    /// </summary>
    public readonly ref struct S2C_0x049_ItemSearch
    {
        public const ushort PacketId = 0x049;
        public const int PayloadLength = 68;

        private readonly ReadOnlySpan<byte> _payload;

        public bool IsValid { get; }
        public ushort ItemId { get; }

        /// <summary>The response flag: non-zero asks for the name search done over several frames.</summary>
        public bool IsAsync { get; }

        public S2C_0x049_ItemSearch(ReadOnlySpan<byte> payload)
        {
            _payload = payload;
            if (payload.Length < PayloadLength)
            {
                IsValid = false;
                ItemId = 0;
                IsAsync = false;
                return;
            }

            ItemId = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(0, 2));
            IsAsync = payload[2] != 0;
            IsValid = true;
        }

        /// <summary>The searched item name (payload 4, 64 bytes), echoed from the request.</summary>
        public string GetItemName() => IsValid ? PacketStrings.Read(_payload, 4, 64) : string.Empty;
    }

    /// <summary>
    /// S2C 0x0E1 (GP_SERV_COMMAND_GROUP_CHECKID): the server-side id of the character's party, the key the search server
    /// wants for party member queries (see <see cref="SocialPacketBuilder.BuildGroupCheckId"/>). 0 when not in a party.
    /// <para>
    /// Packet structure referenced from XiPackets (https://github.com/atom0s/XiPackets), <c>world/server/0x00E1</c>;
    /// server side referenced from LandSandBoat (https://github.com/LandSandBoat/server), <c>s2c/0x0e1_group_checkid.cpp</c>.
    /// </para>
    /// </summary>
    public readonly ref struct S2C_0x0E1_GroupCheckId
    {
        public const ushort PacketId = 0x0E1;

        public bool IsValid { get; }
        public uint GroupId { get; }

        public S2C_0x0E1_GroupCheckId(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < 4)
            {
                IsValid = false;
                GroupId = 0;
                return;
            }

            GroupId = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0, 4));
            IsValid = true;
        }
    }

    /// <summary>
    /// S2C 0x0A0 (GP_SERV_COMMAND_MAP_GROUP): one party or alliance member's position for the map, one packet per member
    /// in the same zone, answering C2S 0x0D2. The position is in the wire order of LandSandBoat (<c>x</c>, <c>y</c> the
    /// height, <c>z</c>), which is GordianXI's canonical order.
    /// <para>
    /// Packet structure referenced from XiPackets (https://github.com/atom0s/XiPackets), <c>world/server/0x00A0</c>;
    /// server side referenced from LandSandBoat (https://github.com/LandSandBoat/server), <c>s2c/0x0a0_map_group.cpp</c>.
    /// </para>
    /// </summary>
    public readonly ref struct S2C_0x0A0_MapGroup
    {
        public const ushort PacketId = 0x0A0;
        public const int PayloadLength = 20;

        public bool IsValid { get; }

        /// <summary>The member's server id.</summary>
        public uint UniqueId { get; }

        /// <summary>The member's zone id.</summary>
        public short Zone { get; }

        public Vector3 Position { get; }

        public S2C_0x0A0_MapGroup(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < PayloadLength)
            {
                IsValid = false;
                UniqueId = 0;
                Zone = 0;
                Position = default;
                return;
            }

            UniqueId = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0, 4));
            Zone = BinaryPrimitives.ReadInt16LittleEndian(payload.Slice(4, 2));
            Position = new Vector3(
                BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(8, 4)),
                BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(12, 4)),
                BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(16, 4)));
            IsValid = true;
        }
    }

    /// <summary>
    /// One linkshell the concierge lists (an entry of an S2C 0x048 record page): the linkshell's id, key, colour and
    /// decoded name, and the advertisement the owner registered.
    /// </summary>
    /// <param name="SlotIndex">The concierge slot (0-15).</param>
    /// <param name="GroupId">The linkshell id.</param>
    /// <param name="GroupKey">The linkshell key.</param>
    /// <param name="Color">The colour as the 4-bit RGBA word of the linkshell item (<c>r | g &lt;&lt; 4 | b &lt;&lt; 8 | a &lt;&lt; 12</c>).</param>
    /// <param name="Flag">The linkshell item flag byte.</param>
    /// <param name="Name">The linkshell name (6-bit decoded).</param>
    /// <param name="Active">The advertisement is active.</param>
    /// <param name="LanguageJapanese">The linkshell speaks Japanese.</param>
    /// <param name="LanguageEnglish">The linkshell speaks English.</param>
    /// <param name="LanguageOther">The linkshell speaks another language.</param>
    /// <param name="MembersGoal">The member count the linkshell wants (1-10).</param>
    /// <param name="ActiveTier">The activity tier: 0 for 1-6 hours, 1 for 7-18, 2 for 19 and more.</param>
    /// <param name="Characteristics">The 16 characteristic flags.</param>
    public readonly record struct ConciergeLinkshell(
        byte SlotIndex, uint GroupId, ushort GroupKey, ushort Color, byte Flag, string Name,
        bool Active, bool LanguageJapanese, bool LanguageEnglish, bool LanguageOther,
        byte MembersGoal, byte ActiveTier, ushort Characteristics);

    /// <summary>
    /// S2C 0x048: the linkshell concierge's answer, in two forms told apart by the first four bytes. The header form
    /// (<c>FE FE FE FE</c>) says which concierge slot is the character's own listing and how many days ago it was posted;
    /// the record form lists up to four linkshells. Both are 128 bytes (124 of payload); the format is known only from
    /// LandSandBoat, which sends it, XiPackets calls the packet unknown.
    /// <para>
    /// Packet structure referenced from LandSandBoat (https://github.com/LandSandBoat/server),
    /// <c>s2c/0x048_link_concierge_header.h</c> and <c>s2c/0x048_link_concierge_record.h</c>; XiPackets
    /// (https://github.com/atom0s/XiPackets), <c>world/server/0x0048</c> documents only the size.
    /// </para>
    /// </summary>
    public readonly ref struct S2C_0x048_LinkConcierge
    {
        public const ushort PacketId = 0x048;
        public const int PayloadLength = 124;
        public const int RecordSlots = 4;
        private const int BodyOffset = 12;
        private const int BodySize = 24;
        private const int AttrsOffset = BodyOffset + (RecordSlots * BodySize);

        private readonly ReadOnlySpan<byte> _payload;

        public bool IsValid { get; }

        /// <summary>True for the header form (sentinel <c>FE FE FE FE</c>).</summary>
        public bool IsHeader { get; }

        /// <summary>Header: the concierge slot of the character's own registration (payload 4).</summary>
        public ushort SlotIndex => IsHeader ? BinaryPrimitives.ReadUInt16LittleEndian(_payload.Slice(4, 2)) : (ushort)0;

        /// <summary>Header: 0xFFFF when the character is registered (payload 6).</summary>
        public ushort ListingFlag => IsHeader ? BinaryPrimitives.ReadUInt16LittleEndian(_payload.Slice(6, 2)) : (ushort)0;

        /// <summary>Header: the days since the listing was posted, parsed from the 3 ASCII characters like <c>22d</c> (payload 24); 0 if absent.</summary>
        public int PostedDays
        {
            get
            {
                if (!IsHeader) return 0;
                int days = 0;
                for (int i = 0; i < 3; i++)
                {
                    byte c = _payload[24 + i];
                    if (c < (byte)'0' || c > (byte)'9') break;
                    days = (days * 10) + (c - '0');
                }
                return days;
            }
        }

        /// <summary>Header: the character has an active registration (payload 44).</summary>
        public bool Registered => IsHeader && _payload[44] != 0;

        public S2C_0x048_LinkConcierge(ReadOnlySpan<byte> payload)
        {
            _payload = payload;
            if (payload.Length < PayloadLength)
            {
                IsValid = false;
                IsHeader = false;
                return;
            }

            IsHeader = payload[0] == 0xFE && payload[1] == 0xFE && payload[2] == 0xFE && payload[3] == 0xFE;
            IsValid = true;
        }

        /// <summary>
        /// Record form: reads linkshell <paramref name="index"/> (0-3); false for an empty slot (index 0xFF) or a header packet.
        /// </summary>
        public bool TryGetLinkshell(int index, out ConciergeLinkshell linkshell)
        {
            linkshell = default;
            if (!IsValid || IsHeader || index < 0 || index >= RecordSlots) return false;
            byte slot = _payload[index];
            if (slot == 0xFF) return false;

            ReadOnlySpan<byte> body = _payload.Slice(BodyOffset + (index * BodySize), BodySize);
            uint attrs = BinaryPrimitives.ReadUInt32LittleEndian(_payload.Slice(AttrsOffset + (index * 4), 4));
            linkshell = new ConciergeLinkshell(
                SlotIndex: slot,
                GroupId: BinaryPrimitives.ReadUInt32LittleEndian(body.Slice(0, 4)),
                GroupKey: BinaryPrimitives.ReadUInt16LittleEndian(body.Slice(4, 2)),
                Color: BinaryPrimitives.ReadUInt16LittleEndian(body.Slice(6, 2)),
                Flag: body[8],
                Name: LinkshellNameCodec.Decode(body.Slice(9, 15)),
                Active: (attrs & 0x01) != 0,
                LanguageJapanese: (attrs & 0x02) != 0,
                LanguageEnglish: (attrs & 0x04) != 0,
                LanguageOther: (attrs & 0x20) != 0,
                MembersGoal: (byte)((attrs >> 6) & 0x0F),
                ActiveTier: (byte)((attrs >> 14) & 0x03),
                Characteristics: (ushort)(attrs >> 16));
            return true;
        }
    }

    #endregion

    #region Outbound C2S Builders

    /// <summary>
    /// Builders for the social client packets: the delivery box (0x04D), the blacklist (0x03C, 0x03D), the world pass
    /// (0x01B), <c>/itemsearch</c> (0x02C), the linkshell items (0x0C3, 0x0C4), the party group id (0x078) and the party
    /// map positions (0x0D2). Every packet is the size LandSandBoat's <c>ValidatedPacketHandler</c> expects (the struct
    /// size rounded up to 4).
    /// <para>
    /// Packet structures referenced from XiPackets (https://github.com/atom0s/XiPackets), <c>world/client/</c>; sizes and
    /// constraints referenced from LandSandBoat (https://github.com/LandSandBoat/server), <c>src/map/packets/c2s/</c>.
    /// </para>
    /// </summary>
    public static class SocialPacketBuilder
    {
        /// <summary>The slots of a delivery box (0-7).</summary>
        public const int DeliverySlotCount = 8;

        /// <summary>The most names the blacklist holds.</summary>
        public const int BlacklistCapacity = 300;

        /// <summary>The longest <c>/itemsearch</c> name the packet holds.</summary>
        public const int ItemSearchNameLength = 64;

        private static byte[] NewPacket(ushort id, int size, ushort sequenceId)
        {
            var packet = new byte[size];
            PacketHeader.Write(packet, id, (ushort)(size / 4), sequenceId);
            return packet;
        }

        private static void WriteName(Span<byte> field, string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            int n = Encoding.ASCII.GetBytes(name.AsSpan(0, Math.Min(name.Length, field.Length)), field);
            if (n < field.Length) field[n] = 0;
        }

        /// <summary>
        /// Builds C2S 0x04D (GP_CLI_COMMAND_PBX, 32 bytes), the delivery box packet. The arguments are the retail
        /// client's: <c>Result</c> and <c>ResParam1-3</c> stay 0 (LandSandBoat rejects anything else) and <c>ItemStacks</c>
        /// defaults to -1. The recipient name's first letter is upper-cased, as the client does.
        /// </summary>
        public static byte[] BuildPbx(DeliveryCommand command, DeliveryBox box, int postWorkNo, int itemWorkNo, int itemStacks = -1,
            string targetName = "", ushort sequenceId = 0)
        {
            var packet = NewPacket(0x04D, 32, sequenceId);
            packet[4] = (byte)command;
            packet[5] = (byte)(sbyte)box;
            packet[6] = (byte)(sbyte)postWorkNo;
            packet[7] = (byte)(sbyte)itemWorkNo;
            BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(8, 4), itemStacks);
            if (!string.IsNullOrEmpty(targetName))
            {
                WriteName(packet.AsSpan(16, 16), char.ToUpperInvariant(targetName[0]) + targetName[1..]);
            }
            return packet;
        }

        /// <summary>COM_WORK: asks for the contents of slot <paramref name="slot"/> of a box.</summary>
        public static byte[] BuildPbxWork(DeliveryBox box, int slot, ushort sequenceId = 0) =>
            BuildPbx(DeliveryCommand.Work, box, slot, -1, sequenceId: sequenceId);

        /// <summary>COM_SET: puts <paramref name="count"/> of the inventory item at <paramref name="inventoryIndex"/> into outgoing slot <paramref name="slot"/> for <paramref name="recipient"/>.</summary>
        public static byte[] BuildPbxSet(int slot, int inventoryIndex, int count, string recipient, ushort sequenceId = 0) =>
            BuildPbx(DeliveryCommand.Set, DeliveryBox.Outgoing, slot, inventoryIndex, count, recipient, sequenceId);

        /// <summary>COM_SEND: sends outgoing slot <paramref name="slot"/>.</summary>
        public static byte[] BuildPbxSend(int slot, ushort sequenceId = 0) =>
            BuildPbx(DeliveryCommand.Send, DeliveryBox.Outgoing, slot, -1, sequenceId: sequenceId);

        /// <summary>COM_CANCEL: takes outgoing slot <paramref name="slot"/> back.</summary>
        public static byte[] BuildPbxCancel(int slot, ushort sequenceId = 0) =>
            BuildPbx(DeliveryCommand.Cancel, DeliveryBox.Outgoing, slot, -1, sequenceId: sequenceId);

        /// <summary>COM_CHECK: counts the items of a box (the answer is in <c>ResParam2</c> for the incoming box, <c>ResParam3</c> for the outgoing).</summary>
        public static byte[] BuildPbxCheck(DeliveryBox box, ushort sequenceId = 0) =>
            BuildPbx(DeliveryCommand.Check, box, -1, -1, sequenceId: sequenceId);

        /// <summary>COM_RECV: receives incoming slot <paramref name="slot"/>.</summary>
        public static byte[] BuildPbxRecv(int slot, ushort sequenceId = 0) =>
            BuildPbx(DeliveryCommand.Recv, DeliveryBox.Incoming, slot, 1, sequenceId: sequenceId);

        /// <summary>COM_CONFIRM: confirms the pending action.</summary>
        public static byte[] BuildPbxConfirm(ushort sequenceId = 0) =>
            BuildPbx(DeliveryCommand.Confirm, DeliveryBox.None, -1, -1, sequenceId: sequenceId);

        /// <summary>COM_ACCEPT: accepts incoming slot <paramref name="slot"/>.</summary>
        public static byte[] BuildPbxAccept(int slot, ushort sequenceId = 0) =>
            BuildPbx(DeliveryCommand.Accept, DeliveryBox.Incoming, slot, -1, sequenceId: sequenceId);

        /// <summary>COM_REJECT: returns incoming slot <paramref name="slot"/> to its sender.</summary>
        public static byte[] BuildPbxReject(int slot, ushort sequenceId = 0) =>
            BuildPbx(DeliveryCommand.Reject, DeliveryBox.Incoming, slot, -1, sequenceId: sequenceId);

        /// <summary>COM_GET: takes the item of a slot into the inventory.</summary>
        public static byte[] BuildPbxGet(DeliveryBox box, int slot, ushort sequenceId = 0) =>
            BuildPbx(DeliveryCommand.Get, box, slot, -1, sequenceId: sequenceId);

        /// <summary>COM_CLEAR: clears a slot.</summary>
        public static byte[] BuildPbxClear(DeliveryBox box, int slot, ushort sequenceId = 0) =>
            BuildPbx(DeliveryCommand.Clear, box, slot, -1, sequenceId: sequenceId);

        /// <summary>COM_QUERY: asks whether <paramref name="name"/> exists as a recipient.</summary>
        public static byte[] BuildPbxQuery(string name, ushort sequenceId = 0) =>
            BuildPbx(DeliveryCommand.Query, DeliveryBox.None, -1, -1, -1, name, sequenceId);

        /// <summary>COM_DELI_OPEN / COM_POST_OPEN / COM_POSTAL_CLOSE: enters delivery mode, post mode or leaves them.</summary>
        public static byte[] BuildPbxMode(DeliveryCommand command, ushort sequenceId = 0) =>
            BuildPbx(command, DeliveryBox.None, -1, -1, sequenceId: sequenceId);

        /// <summary>
        /// Builds C2S 0x03C (GP_CLI_COMMAND_BLACK_LIST, 28 bytes): asks for the blacklist. The retail client sends it with
        /// every field 0 when its list is not initialised; LandSandBoat answers with S2C 0x041.
        /// </summary>
        public static byte[] BuildBlackList(ushort sequenceId = 0) => NewPacket(0x03C, 28, sequenceId);

        /// <summary>
        /// Builds C2S 0x03D (GP_CLI_COMMAND_BLACK_EDIT, 28 bytes): adds or removes <paramref name="name"/>. The entry's id
        /// is 0 from the client; LandSandBoat looks the name up.
        /// </summary>
        public static byte[] BuildBlackEdit(string name, BlacklistEditMode mode, ushort sequenceId = 0)
        {
            var packet = NewPacket(0x03D, 28, sequenceId);
            WriteName(packet.AsSpan(8, 16), name);
            packet[24] = (byte)(sbyte)mode;
            return packet;
        }

        /// <summary>Builds C2S 0x01B (GP_CLI_COMMAND_FRIENDPASS, 28 bytes): one step of buying a world pass.</summary>
        public static byte[] BuildFriendPass(FriendPassPara para, ushort sequenceId = 0)
        {
            var packet = NewPacket(0x01B, 28, sequenceId);
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), (ushort)para);
            return packet;
        }

        /// <summary>
        /// Builds C2S 0x02C (<c>/itemsearch</c>, 72 bytes): a language byte and the item name (64 bytes). LandSandBoat
        /// looks the name up in that language and answers with S2C 0x049.
        /// </summary>
        public static byte[] BuildItemSearch(string itemName, ItemSearchLanguage language = ItemSearchLanguage.English, ushort sequenceId = 0)
        {
            var packet = NewPacket(0x02C, 72, sequenceId);
            packet[4] = (byte)language;
            WriteName(packet.AsSpan(8, ItemSearchNameLength), itemName);
            return packet;
        }

        /// <summary>
        /// Builds C2S 0x0C3 (GP_CLI_COMMAND_GROUP_COMLINK_MAKE, 8 bytes): makes a linkpearl from the linkshell worn in slot
        /// 1 or 2 (<c>/makelinkpearl</c>); LandSandBoat needs the pearlsack rank in that linkshell.
        /// </summary>
        public static byte[] BuildComlinkMake(byte linkshellSlot, ushort sequenceId = 0)
        {
            var packet = NewPacket(0x0C3, 8, sequenceId);
            packet[4] = 0;                  // State: anything but 0 makes LandSandBoat ignore it
            packet[5] = linkshellSlot;
            return packet;
        }

        /// <summary>
        /// Builds C2S 0x0C4 (GP_CLI_COMMAND_GROUP_COMLINK_ACTIVE, 28 bytes) to equip or unequip the linkshell item at
        /// <paramref name="itemIndex"/> of <paramref name="category"/> in slot 1 or 2. The name is the string "dummy" as the
        /// retail client sends it.
        /// </summary>
        public static byte[] BuildComlinkActive(bool equip, byte linkshellSlot, byte itemIndex, ContainerId category, ushort sequenceId = 0)
        {
            var packet = NewPacket(0x0C4, 28, sequenceId);
            WriteComlinkActive(packet, equip, linkshellSlot, itemIndex, category, 15, 15, 15);
            WriteName(packet.AsSpan(12, 15), "dummy");
            return packet;
        }

        /// <summary>
        /// Builds C2S 0x0C4 to create a new linkshell out of the "New Linkshell" item at <paramref name="itemIndex"/>: the
        /// same packet as equipping, with the name packed to 6 bits per character (<see cref="LinkshellNameCodec"/>, up to
        /// 20 characters) and a 4-bit-per-channel colour. Returns null when the name cannot be encoded.
        /// </summary>
        public static byte[]? BuildComlinkCreate(string linkshellName, byte red, byte green, byte blue, byte linkshellSlot, byte itemIndex,
            ContainerId category, ushort sequenceId = 0)
        {
            var packet = NewPacket(0x0C4, 28, sequenceId);
            WriteComlinkActive(packet, true, linkshellSlot, itemIndex, category, red, green, blue);
            if (!LinkshellNameCodec.TryEncode(linkshellName, packet.AsSpan(12, 15))) return null;
            return packet;
        }

        private static void WriteComlinkActive(byte[] packet, bool equip, byte slot, byte itemIndex, ContainerId category,
            byte red, byte green, byte blue)
        {
            // r, g, b and a are 4-bit fields; LandSandBoat requires a == 15 (XiPackets writes 0x15 for it).
            ushort color = (ushort)((red & 0x0F) | ((green & 0x0F) << 4) | ((blue & 0x0F) << 8) | (0x0F << 12));
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), color);
            packet[6] = itemIndex;
            packet[7] = (byte)category;
            packet[8] = (byte)(equip ? 1 : 0);
            packet[27] = slot;
        }

        /// <summary>
        /// Builds C2S 0x078 (GP_CLI_COMMAND_GROUP_CHECKID, 4 bytes): asks for the party's server-side group id, which the
        /// party member queries of the search server need. The answer is S2C 0x0E1.
        /// </summary>
        public static byte[] BuildGroupCheckId(ushort sequenceId = 0) => NewPacket(0x078, 4, sequenceId);

        /// <summary>
        /// Builds C2S 0x0D2 (GP_CLI_COMMAND_MAP_GROUP, 8 bytes): asks for the positions of the party and alliance members
        /// in <paramref name="zoneId"/>, which must be the character's current zone. LandSandBoat answers with one S2C 0x0A0
        /// per member in the zone.
        /// </summary>
        public static byte[] BuildMapGroup(uint zoneId, ushort sequenceId = 0)
        {
            var packet = NewPacket(0x0D2, 8, sequenceId);
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4, 4), zoneId);
            return packet;
        }
    }

    #endregion
}
