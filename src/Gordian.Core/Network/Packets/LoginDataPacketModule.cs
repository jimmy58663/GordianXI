// src/Gordian.Core/Network/Packets/LoginDataPacketModule.cs
using System;
using System.Buffers.Binary;
using System.Threading.Tasks;
using Gordian.Core.Diagnostics;
using Gordian.Core.World;

namespace Gordian.Core.Network.Packets
{
    /// <summary>
    /// Packet domain module for the data LandSandBoat sends on every zone-in that no menu reads yet: the unlocked mounts
    /// (S2C 0x0AE), Moblin Maze Mongers vouchers and runes (0x0AD), Alter Ego points (0x08E) and the extended job data
    /// (0x044: Blue Mage spells, the automaton, Monstrosity). The first three go to <see cref="ProgressionState"/>, the
    /// last to <see cref="LocalPlayerState"/>. It also sends their requests: C2S 0x0C1, 0x0D8, 0x102, 0x11B and 0x114.
    /// </summary>
    public sealed class LoginDataPacketModule
    {
        private readonly ProgressionState _progression;
        private readonly LocalPlayerState _localPlayer;
        private readonly Func<ReadOnlyMemory<byte>, bool, Task> _sendChunkCallback;
        private readonly Action<PacketDirection, ushort, ushort, ReadOnlySpan<byte>>? _logPacketCallback;
        private ushort _sequenceNumber;

        public bool LogOutboundOnRoute { get; set; } = true;

        public LoginDataPacketModule(
            ProgressionState progression,
            LocalPlayerState localPlayer,
            Func<ReadOnlyMemory<byte>, bool, Task> sendChunkCallback,
            Action<PacketDirection, ushort, ushort, ReadOnlySpan<byte>>? logPacketCallback = null)
        {
            _progression = progression ?? throw new ArgumentNullException(nameof(progression));
            _localPlayer = localPlayer ?? throw new ArgumentNullException(nameof(localPlayer));
            _sendChunkCallback = sendChunkCallback ?? throw new ArgumentNullException(nameof(sendChunkCallback));
            _logPacketCallback = logPacketCallback;
        }

        public void Register(IPacketDispatcher dispatcher)
        {
            ArgumentNullException.ThrowIfNull(dispatcher);
            dispatcher.Register(S2C_0x0AE_MountData.PacketId, HandleMountData);
            dispatcher.Register(S2C_0x0AD_Dungeon.PacketId, HandleDungeon);
            dispatcher.Register(S2C_0x08E_AlterEgoPoints.PacketId, HandleAlterEgoPoints);
            dispatcher.Register(S2C_0x044_ExtendedJob.PacketId, HandleExtendedJob);
        }

        public void Unregister(IPacketDispatcher dispatcher)
        {
            ArgumentNullException.ThrowIfNull(dispatcher);
            dispatcher.Unregister(S2C_0x0AE_MountData.PacketId);
            dispatcher.Unregister(S2C_0x0AD_Dungeon.PacketId);
            dispatcher.Unregister(S2C_0x08E_AlterEgoPoints.PacketId);
            dispatcher.Unregister(S2C_0x044_ExtendedJob.PacketId);
        }

        #region Inbound

        private void HandleMountData(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var mounts = new S2C_0x0AE_MountData(payload);
            if (!mounts.IsValid) return;
            _progression.UpdateMounts(mounts);
            GordianLog.Debug("PROGRESSION", $"Mount data 0x0AE: table={Convert.ToHexString(mounts.MountTable)} unlocked={_progression.GetUnlockedMounts().Count}");
        }

        private void HandleDungeon(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var dungeon = new S2C_0x0AD_Dungeon(payload);
            if (!dungeon.IsValid) return;
            _progression.UpdateMazeUnlocks(dungeon);
            GordianLog.Debug("PROGRESSION", $"Maze data 0x0AD: vouchers={_progression.GetMazeVoucherItemIds().Count} runes={_progression.GetMazeRuneItemIds().Count}");
        }

        private void HandleAlterEgoPoints(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var points = new S2C_0x08E_AlterEgoPoints(payload);
            if (!points.IsValid) return;
            _progression.UpdateAlterEgoPoints(points);
            GordianLog.Debug("PROGRESSION", $"Alter Ego points 0x08E: points={points.Points}");
        }

        private void HandleExtendedJob(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var job = new S2C_0x044_ExtendedJob(payload);
            if (!job.IsValid) return;
            bool applied = _localPlayer.ApplyExtendedJob(job);
            string slot = job.IsSubJob ? "sub" : "main";
            if (!applied)
            {
                GordianLog.Debug("PROGRESSION", $"Extended job 0x044: job {job.JobNo} ({slot}) ignored, not the current {slot} job.");
                return;
            }

            if (job.IsBlueMage)
            {
                int set = 0;
                for (int i = 0; i < S2C_0x044_ExtendedJob.BlueSpellSlots; i++) if (job.GetBlueSpell(i) != 0) set++;
                GordianLog.Debug("PROGRESSION", $"Extended job 0x044: Blue Mage ({slot}) {set} spells set.");
            }
            else if (job.IsPuppetmaster)
            {
                GordianLog.Debug("PROGRESSION", $"Extended job 0x044: Puppetmaster ({slot}) automaton '{job.AutomatonName}' head={job.AutomatonHead} frame={job.AutomatonFrame} HP={job.AutomatonHp}/{job.AutomatonMaxHp}.");
            }
            else if (job.IsMonstrosity)
            {
                GordianLog.Debug("PROGRESSION", $"Extended job 0x044: Monstrosity species={job.MonstrositySpecies}.");
            }
            else
            {
                GordianLog.Debug("PROGRESSION", $"Extended job 0x044: job {job.JobNo} ({slot}), {payload.Length} bytes kept raw.");
            }
        }

        #endregion

        #region Outbound

        private ushort NextSequence() => unchecked(++_sequenceNumber);

        private Task Send(ushort opcode, byte[] packet)
        {
            LogOutbound(opcode, packet);
            return _sendChunkCallback(packet, true);
        }

        /// <summary>Sends C2S 0x0C1: upgrades an Alter Ego category (answered by S2C 0x08E). No menu calls it yet.</summary>
        public Task SendAlterEgoUpgradeAsync(AlterEgoCategory category)
            => Send(0x0C1, LoginDataPacketBuilder.BuildAlterEgoUpgrade(category, NextSequence()));

        /// <summary>Sends C2S 0x0D8: a Moblin Maze Mongers tabula change. No menu calls it yet; LandSandBoat only logs it.</summary>
        public Task SendDungeonParamAsync(uint uniqueNo, ushort actIndex, ushort param1, byte param2, ReadOnlyMemory<byte> data)
            => Send(0x0D8, LoginDataPacketBuilder.BuildDungeonParam(uniqueNo, actIndex, param1, param2, data.Span, NextSequence()));

        /// <summary>Sends C2S 0x102: sets <paramref name="spellId"/> (id less 512) in Blue Mage slot <paramref name="slot"/> (answered by S2C 0x044).</summary>
        public Task SendSetBlueSpellAsync(int slot, byte spellId, bool subJob)
            => Send(0x102, LoginDataPacketBuilder.BuildSetBlueSpell(slot, spellId, subJob, NextSequence()));

        /// <summary>Sends C2S 0x102: removes the spell <paramref name="setSpellId"/> (id less 512) from Blue Mage slot <paramref name="slot"/>.</summary>
        public Task SendRemoveBlueSpellAsync(int slot, byte setSpellId, bool subJob)
            => Send(0x102, LoginDataPacketBuilder.BuildRemoveBlueSpell(slot, setSpellId, subJob, NextSequence()));

        /// <summary>Sends C2S 0x102: equips an automaton head, frame or attachment (answered by S2C 0x044).</summary>
        public Task SendEquipAutomatonPartAsync(AutomatonSlot slot, byte partId, bool subJob)
            => Send(0x102, LoginDataPacketBuilder.BuildEquipAutomatonPart(slot, partId, subJob, NextSequence()));

        /// <summary>Sends C2S 0x102: removes an automaton attachment.</summary>
        public Task SendRemoveAutomatonAttachmentAsync(AutomatonSlot slot, byte attachmentId, bool subJob)
            => Send(0x102, LoginDataPacketBuilder.BuildRemoveAutomatonAttachment(slot, attachmentId, subJob, NextSequence()));

        /// <summary>Sends C2S 0x11B: <c>/jobmasterdisp on|off</c>.</summary>
        public Task SendMasteryDisplayAsync(bool on)
            => Send(0x11B, LoginDataPacketBuilder.BuildMasteryDisplay(on, NextSequence()));

        /// <summary>Sends C2S 0x114: asks for the map markers (answered by S2C 0x063 type 6). No map calls it yet.</summary>
        public Task SendMapMarkersRequestAsync()
            => Send(0x114, LoginDataPacketBuilder.BuildMapMarkers(NextSequence()));

        private void LogOutbound(ushort packetId, ReadOnlySpan<byte> packet)
        {
            if (LogOutboundOnRoute && _logPacketCallback != null)
            {
                ushort seq = packet.Length >= 4 ? BinaryPrimitives.ReadUInt16LittleEndian(packet.Slice(2, 2)) : (ushort)0;
                var payload = packet.Length >= 4 ? packet.Slice(4) : ReadOnlySpan<byte>.Empty;
                _logPacketCallback(PacketDirection.Outbound, packetId, seq, payload);
            }
        }

        #endregion
    }
}
