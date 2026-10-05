// src/Gordian.Core/Network/Packets/SocialPacketModule.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Threading.Tasks;
using Gordian.Core.Diagnostics;
using Gordian.Core.World;

namespace Gordian.Core.Network.Packets
{
    /// <summary>
    /// Packet domain module for the social packets: the delivery box (S2C 0x04B, C2S 0x04D), the blacklist (S2C 0x041 /
    /// 0x042, C2S 0x03C / 0x03D), the world pass vendor (S2C 0x059, C2S 0x01B), <c>/itemsearch</c> (S2C 0x049, C2S 0x02C),
    /// the linkshell concierge (S2C 0x048), the party group id (S2C 0x0E1, C2S 0x078), the party map positions (S2C 0x0A0,
    /// C2S 0x0D2) and the linkshell item requests (C2S 0x0C3, 0x0C4).
    /// </summary>
    public sealed class SocialPacketModule
    {
        private readonly DeliveryBoxState _delivery;
        private readonly BlacklistState _blacklist;
        private readonly SocialState _social;
        private readonly InventoryState? _inventory;
        private readonly Func<ReadOnlyMemory<byte>, bool, Task> _sendChunkCallback;
        private readonly Action<PacketDirection, ushort, ushort, ReadOnlySpan<byte>>? _logPacketCallback;
        private ushort _sequenceNumber;

        public DeliveryBoxState Delivery => _delivery;
        public BlacklistState Blacklist => _blacklist;
        public SocialState Social => _social;
        public bool LogOutboundOnRoute { get; set; } = true;

        public SocialPacketModule(
            DeliveryBoxState delivery,
            BlacklistState blacklist,
            SocialState social,
            InventoryState? inventory,
            Func<ReadOnlyMemory<byte>, bool, Task> sendChunkCallback,
            Action<PacketDirection, ushort, ushort, ReadOnlySpan<byte>>? logPacketCallback = null)
        {
            _delivery = delivery ?? throw new ArgumentNullException(nameof(delivery));
            _blacklist = blacklist ?? throw new ArgumentNullException(nameof(blacklist));
            _social = social ?? throw new ArgumentNullException(nameof(social));
            _inventory = inventory;
            _sendChunkCallback = sendChunkCallback ?? throw new ArgumentNullException(nameof(sendChunkCallback));
            _logPacketCallback = logPacketCallback;
        }

        public void Register(IPacketDispatcher dispatcher)
        {
            ArgumentNullException.ThrowIfNull(dispatcher);
            dispatcher.Register(S2C_0x04B_PbxResult.PacketId, HandlePbxResult);
            dispatcher.Register(S2C_0x041_BlackList.PacketId, HandleBlackList);
            dispatcher.Register(S2C_0x042_BlackEdit.PacketId, HandleBlackEdit);
            dispatcher.Register(S2C_0x059_FriendPass.PacketId, HandleFriendPass);
            dispatcher.Register(S2C_0x049_ItemSearch.PacketId, HandleItemSearch);
            dispatcher.Register(S2C_0x0E1_GroupCheckId.PacketId, HandleGroupCheckId);
            dispatcher.Register(S2C_0x0A0_MapGroup.PacketId, HandleMapGroup);
            dispatcher.Register(S2C_0x048_LinkConcierge.PacketId, HandleLinkConcierge);
        }

        public void Unregister(IPacketDispatcher dispatcher)
        {
            ArgumentNullException.ThrowIfNull(dispatcher);
            dispatcher.Unregister(S2C_0x04B_PbxResult.PacketId);
            dispatcher.Unregister(S2C_0x041_BlackList.PacketId);
            dispatcher.Unregister(S2C_0x042_BlackEdit.PacketId);
            dispatcher.Unregister(S2C_0x059_FriendPass.PacketId);
            dispatcher.Unregister(S2C_0x049_ItemSearch.PacketId);
            dispatcher.Unregister(S2C_0x0E1_GroupCheckId.PacketId);
            dispatcher.Unregister(S2C_0x0A0_MapGroup.PacketId);
            dispatcher.Unregister(S2C_0x048_LinkConcierge.PacketId);
        }

        #region Inbound Handlers

        private void HandlePbxResult(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var result = new S2C_0x04B_PbxResult(payload);
            if (!result.IsValid) return;

            GordianLog.Debug("DELIVERY", $"PBX answer: {result.Command} box={result.BoxNo} slot={result.PostWorkNo} result={result.ResultCode} params={result.ResParam1},{result.ResParam2},{result.ResParam3} item={result.ItemId} stat={result.Stat}");
            _delivery.Apply(in result);
        }

        private void HandleBlackList(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var page = new S2C_0x041_BlackList(payload);
            if (!page.IsValid) return;

            GordianLog.Debug("BLACKLIST", $"Blacklist page: {page.Count} entries, stat=0x{page.Stat:X2}");
            _blacklist.Apply(in page);
        }

        private void HandleBlackEdit(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var edit = new S2C_0x042_BlackEdit(payload);
            if (!edit.IsValid) return;

            GordianLog.Debug("BLACKLIST", $"Blacklist edit: {edit.Mode} id=0x{edit.Id:X8} name='{edit.GetName()}'");
            _blacklist.Apply(in edit);
        }

        private void HandleFriendPass(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var pass = new S2C_0x059_FriendPass(payload);
            if (!pass.IsValid) return;

            GordianLog.Debug("FRIENDPASS", $"World pass answer: type={pass.Type}, price={pass.Price}, uses={pass.LeftNum}, hours={pass.LeftHours}");
            _social.SetFriendPass(new FriendPassInfo(pass.LeftNum, pass.LeftHours, pass.Price, pass.GetPass(), pass.Type));
        }

        private void HandleItemSearch(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var search = new S2C_0x049_ItemSearch(payload);
            if (!search.IsValid) return;

            var containers = new List<(ContainerId, uint)>();
            if (_inventory != null && !search.IsAsync && search.ItemId != 0)
            {
                for (int c = 0; c < (int)ContainerId.Count; c++)
                {
                    uint count = 0;
                    foreach (var item in _inventory.SnapshotItems((ContainerId)c))
                    {
                        if (item.ItemId == search.ItemId) count += Math.Max(1u, item.Count);
                    }
                    if (count > 0) containers.Add(((ContainerId)c, count));
                }
            }

            GordianLog.Debug("ITEMSEARCH", $"/itemsearch answer: item={search.ItemId}, async={search.IsAsync}, name='{search.GetItemName()}', held in {containers.Count} containers");
            _social.SetItemSearch(new ItemSearchResult(search.ItemId, search.GetItemName(), search.IsAsync, containers));
        }

        private void HandleGroupCheckId(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var id = new S2C_0x0E1_GroupCheckId(payload);
            if (!id.IsValid) return;

            GordianLog.Debug("PARTY", $"Party group id: {id.GroupId}");
            _social.SetGroupId(id.GroupId);
        }

        private void HandleMapGroup(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var map = new S2C_0x0A0_MapGroup(payload);
            if (!map.IsValid) return;

            _social.SetMapGroup(new MapGroupPosition(map.UniqueId, map.Zone, map.Position, DateTime.UtcNow));
        }

        private void HandleLinkConcierge(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var concierge = new S2C_0x048_LinkConcierge(payload);
            if (!concierge.IsValid) return;

            GordianLog.Debug("LINKSHELL", concierge.IsHeader
                ? $"Concierge header: own slot={concierge.SlotIndex}, registered={concierge.Registered}, posted {concierge.PostedDays}d ago"
                : "Concierge records");
            _social.ApplyConcierge(in concierge);
        }

        #endregion

        #region Outbound

        private ushort NextSequence() => unchecked(++_sequenceNumber);

        private Task Send(ushort packetId, byte[] packet)
        {
            if (LogOutboundOnRoute && _logPacketCallback != null)
            {
                ushort seq = packet.Length >= 4 ? BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(2, 2)) : (ushort)0;
                var payload = packet.Length >= 4 ? packet.AsSpan(4) : ReadOnlySpan<byte>.Empty;
                _logPacketCallback(PacketDirection.Outbound, packetId, seq, payload);
            }
            return _sendChunkCallback(packet, true);
        }

        // ---- delivery box ----

        /// <summary>Enters delivery mode (C2S 0x04D COM_DELI_OPEN); the answer is S2C 0x04B.</summary>
        public Task OpenDeliveryAsync() => Send(0x04D, SocialPacketBuilder.BuildPbxMode(DeliveryCommand.DeliOpen, NextSequence()));

        /// <summary>Enters post mode (C2S 0x04D COM_POST_OPEN).</summary>
        public Task OpenPostAsync() => Send(0x04D, SocialPacketBuilder.BuildPbxMode(DeliveryCommand.PostOpen, NextSequence()));

        /// <summary>Leaves delivery and post mode (C2S 0x04D COM_POSTAL_CLOSE).</summary>
        public Task ClosePostalAsync() => Send(0x04D, SocialPacketBuilder.BuildPbxMode(DeliveryCommand.PostalClose, NextSequence()));

        /// <summary>Counts the items of a box (COM_CHECK); the count lands in <see cref="DeliveryBoxState.IncomingCount"/> or <see cref="DeliveryBoxState.OutgoingCount"/>.</summary>
        public Task CheckBoxAsync(DeliveryBox box) => Send(0x04D, SocialPacketBuilder.BuildPbxCheck(box, NextSequence()));

        /// <summary>Asks for slot <paramref name="slot"/> of a box (COM_WORK).</summary>
        public Task RefreshSlotAsync(DeliveryBox box, int slot) => Send(0x04D, SocialPacketBuilder.BuildPbxWork(box, slot, NextSequence()));

        /// <summary>Asks for every slot of a box.</summary>
        public async Task RefreshBoxAsync(DeliveryBox box)
        {
            for (int slot = 0; slot < DeliveryBoxState.SlotsPerBox; slot++) await RefreshSlotAsync(box, slot).ConfigureAwait(false);
        }

        /// <summary>Puts an inventory item into an outgoing slot for a recipient (COM_SET).</summary>
        public Task SetOutgoingAsync(int slot, int inventoryIndex, int count, string recipient) =>
            Send(0x04D, SocialPacketBuilder.BuildPbxSet(slot, inventoryIndex, count, recipient, NextSequence()));

        /// <summary>Sends an outgoing slot (COM_SEND).</summary>
        public Task SendOutgoingAsync(int slot) => Send(0x04D, SocialPacketBuilder.BuildPbxSend(slot, NextSequence()));

        /// <summary>Takes a sent item back (COM_CANCEL).</summary>
        public Task CancelOutgoingAsync(int slot) => Send(0x04D, SocialPacketBuilder.BuildPbxCancel(slot, NextSequence()));

        /// <summary>Receives an incoming slot (COM_RECV).</summary>
        public Task ReceiveIncomingAsync(int slot) => Send(0x04D, SocialPacketBuilder.BuildPbxRecv(slot, NextSequence()));

        /// <summary>Accepts an incoming slot (COM_ACCEPT).</summary>
        public Task AcceptIncomingAsync(int slot) => Send(0x04D, SocialPacketBuilder.BuildPbxAccept(slot, NextSequence()));

        /// <summary>Returns an incoming slot to its sender (COM_REJECT).</summary>
        public Task RejectIncomingAsync(int slot) => Send(0x04D, SocialPacketBuilder.BuildPbxReject(slot, NextSequence()));

        /// <summary>Takes a slot's item into the inventory (COM_GET).</summary>
        public Task TakeItemAsync(DeliveryBox box, int slot) => Send(0x04D, SocialPacketBuilder.BuildPbxGet(box, slot, NextSequence()));

        /// <summary>Clears a slot (COM_CLEAR).</summary>
        public Task ClearSlotAsync(DeliveryBox box, int slot) => Send(0x04D, SocialPacketBuilder.BuildPbxClear(box, slot, NextSequence()));

        /// <summary>Asks whether a recipient exists (COM_QUERY); the answer lands in <see cref="DeliveryBoxState.LastQueryFound"/>.</summary>
        public Task QueryRecipientAsync(string name)
        {
            _delivery.NoteQuery(name);
            return Send(0x04D, SocialPacketBuilder.BuildPbxQuery(name, NextSequence()));
        }

        // ---- blacklist ----

        /// <summary>Asks for the blacklist (C2S 0x03C); LandSandBoat answers with S2C 0x041 pages.</summary>
        public Task RequestBlacklistAsync() => Send(0x03C, SocialPacketBuilder.BuildBlackList(NextSequence()));

        /// <summary>Adds a character to the blacklist (C2S 0x03D); the server confirms with S2C 0x042.</summary>
        public Task AddToBlacklistAsync(string name) => Send(0x03D, SocialPacketBuilder.BuildBlackEdit(name, BlacklistEditMode.Add, NextSequence()));

        /// <summary>Removes a character from the blacklist (C2S 0x03D).</summary>
        public Task RemoveFromBlacklistAsync(string name) => Send(0x03D, SocialPacketBuilder.BuildBlackEdit(name, BlacklistEditMode.Delete, NextSequence()));

        // ---- the rest ----

        /// <summary>One step of buying a world pass (C2S 0x01B); the answer is S2C 0x059.</summary>
        public Task SendFriendPassAsync(FriendPassPara para) => Send(0x01B, SocialPacketBuilder.BuildFriendPass(para, NextSequence()));

        /// <summary><c>/itemsearch</c> (C2S 0x02C); the answer is S2C 0x049 in <see cref="SocialState.LastItemSearch"/>.</summary>
        public Task SendItemSearchAsync(string itemName, ItemSearchLanguage language = ItemSearchLanguage.English) =>
            Send(0x02C, SocialPacketBuilder.BuildItemSearch(itemName, language, NextSequence()));

        /// <summary>Makes a linkpearl from the linkshell in slot 1 or 2 (C2S 0x0C3).</summary>
        public Task MakeLinkpearlAsync(byte linkshellSlot) => Send(0x0C3, SocialPacketBuilder.BuildComlinkMake(linkshellSlot, NextSequence()));

        /// <summary>Equips or unequips a linkshell item (C2S 0x0C4); LandSandBoat answers with S2C 0x0E0 and an item update.</summary>
        public Task SetLinkshellEquippedAsync(bool equip, byte linkshellSlot, byte itemIndex, ContainerId container) =>
            Send(0x0C4, SocialPacketBuilder.BuildComlinkActive(equip, linkshellSlot, itemIndex, container, NextSequence()));

        /// <summary>
        /// Creates a linkshell from a "New Linkshell" item (C2S 0x0C4 with the packed name). Returns false without sending when
        /// the name cannot be encoded (letters and digits only, up to 20).
        /// </summary>
        public async Task<bool> CreateLinkshellAsync(string name, byte red, byte green, byte blue, byte linkshellSlot, byte itemIndex, ContainerId container)
        {
            byte[]? packet = SocialPacketBuilder.BuildComlinkCreate(name, red, green, blue, linkshellSlot, itemIndex, container, NextSequence());
            if (packet == null) return false;
            await Send(0x0C4, packet).ConfigureAwait(false);
            return true;
        }

        /// <summary>Asks for the party's group id (C2S 0x078); the answer, S2C 0x0E1, lands in <see cref="SocialState.GroupId"/>.</summary>
        public Task RequestGroupIdAsync() => Send(0x078, SocialPacketBuilder.BuildGroupCheckId(NextSequence()));

        /// <summary>Asks for the positions of the party members in <paramref name="zoneId"/> (C2S 0x0D2); one S2C 0x0A0 comes per member.</summary>
        public Task RequestMapGroupAsync(uint zoneId)
        {
            _social.ClearMapGroup();
            return Send(0x0D2, SocialPacketBuilder.BuildMapGroup(zoneId, NextSequence()));
        }

        #endregion
    }
}
