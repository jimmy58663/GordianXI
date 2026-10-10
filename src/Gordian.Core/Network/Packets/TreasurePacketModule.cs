// src/Gordian.Core/Network/Packets/TreasurePacketModule.cs
using System;
using System.Buffers.Binary;
using System.Threading.Tasks;
using Gordian.Core.Diagnostics;
using Gordian.Core.World;

namespace Gordian.Core.Network.Packets
{
    /// <summary>
    /// Packet domain module for the treasure pool: S2C 0x0D2 (an item or gil found) and 0x0D3 (a lot, pass or
    /// judgement) fill <see cref="TreasurePoolState"/>; C2S 0x041 lots and 0x042 passes.
    /// </summary>
    public sealed class TreasurePacketModule
    {
        private readonly TreasurePoolState _pool;
        private readonly LocalPlayerState _localPlayerState;
        private readonly InventoryState? _inventory;
        private readonly Func<ReadOnlyMemory<byte>, bool, Task> _sendChunkCallback;
        private readonly Action<PacketDirection, ushort, ushort, ReadOnlySpan<byte>>? _logPacketCallback;
        private ushort _sequenceNumber;

        public TreasurePoolState Pool => _pool;
        public bool LogOutboundOnRoute { get; set; } = true;

        public TreasurePacketModule(
            TreasurePoolState pool,
            LocalPlayerState localPlayerState,
            InventoryState? inventory,
            Func<ReadOnlyMemory<byte>, bool, Task> sendChunkCallback,
            Action<PacketDirection, ushort, ushort, ReadOnlySpan<byte>>? logPacketCallback = null)
        {
            _pool = pool ?? throw new ArgumentNullException(nameof(pool));
            _localPlayerState = localPlayerState ?? throw new ArgumentNullException(nameof(localPlayerState));
            _inventory = inventory;
            _sendChunkCallback = sendChunkCallback ?? throw new ArgumentNullException(nameof(sendChunkCallback));
            _logPacketCallback = logPacketCallback;
        }

        public void Register(IPacketDispatcher dispatcher)
        {
            ArgumentNullException.ThrowIfNull(dispatcher);
            dispatcher.Register(S2C_0x0D2_TrophyList.PacketId, HandleTrophyList);
            dispatcher.Register(S2C_0x0D3_TrophySolution.PacketId, HandleTrophySolution);
        }

        public void Unregister(IPacketDispatcher dispatcher)
        {
            ArgumentNullException.ThrowIfNull(dispatcher);
            dispatcher.Unregister(S2C_0x0D2_TrophyList.PacketId);
            dispatcher.Unregister(S2C_0x0D3_TrophySolution.PacketId);
        }

        private void HandleTrophyList(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var list = new S2C_0x0D2_TrophyList(payload);
            if (!list.IsValid) return;

            GordianLog.Debug("TREASURE", $"Pool item: slot={list.Slot}, item={list.ItemId}, gil={list.Gold}, dropper=0x{list.DropperId:X8}, entry={list.Entry}");
            _pool.ApplyFound(in list);
        }

        private void HandleTrophySolution(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var solution = new S2C_0x0D3_TrophySolution(payload);
            if (!solution.IsValid) return;

            GordianLog.Debug("TREASURE", $"Pool update: slot={solution.Slot}, judge={solution.Judge}, entry={solution.EntryName} lot={solution.EntryLot} isLot={solution.EntryIsLot}");
            _pool.ApplySolution(in solution, _localPlayerState.ServerId);
        }

        private ushort NextSequence() => unchecked(++_sequenceNumber);

        /// <summary>Sends C2S 0x041: lots on a pool slot. The server answers with S2C 0x0D3.</summary>
        public Task SendLotAsync(byte slot)
        {
            byte index = FirstEmptyInventoryIndex();
            byte[] packet = TreasurePacketBuilder.BuildLot(slot, index, NextSequence());
            GordianLog.Info("TREASURE", $"Sent C2S 0x041 lot: slot={slot} inventoryIndex={index}{(index == 0 ? " (bag full or unknown)" : string.Empty)}");
            LogOutbound(0x041, packet);
            return _sendChunkCallback(packet, true);
        }

        /// <summary>Sends C2S 0x042: passes on a pool slot. The server answers with S2C 0x0D3.</summary>
        public Task SendPassAsync(byte slot)
        {
            byte[] packet = TreasurePacketBuilder.BuildPass(slot, NextSequence());
            GordianLog.Info("TREASURE", $"Sent C2S 0x042 pass: slot={slot}");
            LogOutbound(0x042, packet);
            return _sendChunkCallback(packet, true);
        }

        /// <summary>
        /// The first empty bag slot from 1 (slot 0 is gil), as the retail client finds it for the lot packet; 0 when the
        /// bag is full or unknown.
        /// </summary>
        private byte FirstEmptyInventoryIndex()
        {
            if (_inventory == null) return 0;
            var bag = _inventory.GetContainer(ContainerId.Inventory);
            for (int i = 1; i <= bag.MaxSize; i++)
            {
                if (!bag.Items.TryGetValue((byte)i, out var item) || item.ItemId == 0) return (byte)i;
            }
            return 0;
        }

        private void LogOutbound(ushort packetId, ReadOnlySpan<byte> packet)
        {
            if (LogOutboundOnRoute && _logPacketCallback != null)
            {
                ushort seq = packet.Length >= 4 ? BinaryPrimitives.ReadUInt16LittleEndian(packet.Slice(2, 2)) : (ushort)0;
                var payload = packet.Length >= 4 ? packet.Slice(4) : ReadOnlySpan<byte>.Empty;
                _logPacketCallback(PacketDirection.Outbound, packetId, seq, payload);
            }
        }
    }
}
