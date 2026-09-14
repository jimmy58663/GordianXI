// src/Gordian.Core/Network/Packets/EntityPacketModule.cs
using System;
using System.Numerics;
using System.Threading.Tasks;
using Gordian.Core.Diagnostics;
using Gordian.Core.World;

namespace Gordian.Core.Network.Packets
{
    /// <summary>
    /// Coordinates entity, world state, and character statistics packets:
    /// Inbound decoders (0x00D, 0x00E, 0x037, 0x061, 0x062, 0x076, 0x077) and
    /// outbound builders (0x00F, 0x016, 0x017).
    /// </summary>
    public sealed class EntityPacketModule
    {
        private readonly WorldState _world;
        private readonly LocalPlayerState _localPlayer;
        private readonly Func<ReadOnlyMemory<byte>, bool, Task> _sendChunkCallback;
        private readonly Action<PacketDirection, ushort, ushort, ReadOnlySpan<byte>>? _logPacketCallback;

        public bool LogOutboundOnRoute { get; set; } = true;

        public WorldState World => _world;
        public LocalPlayerState LocalPlayer => _localPlayer;

        public event Action<WorldEntity>? EntitySpawned;
        public event Action<WorldEntity>? EntityUpdated;
        public event Action<WorldEntity>? EntityDespawned;
        public event Action<S2C_0x076_GroupEffects>? PartyBuffsReceived;
        public event Action<S2C_0x077_EntityVis>? EntityVisibilityReceived;

        public EntityPacketModule(
            WorldState world,
            LocalPlayerState localPlayer,
            Func<ReadOnlyMemory<byte>, bool, Task> sendChunkCallback,
            Action<PacketDirection, ushort, ushort, ReadOnlySpan<byte>>? logPacketCallback = null)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _localPlayer = localPlayer ?? throw new ArgumentNullException(nameof(localPlayer));
            _sendChunkCallback = sendChunkCallback ?? throw new ArgumentNullException(nameof(sendChunkCallback));
            _logPacketCallback = logPacketCallback;

            _world.EntitySpawned += (e) => EntitySpawned?.Invoke(e);
            _world.EntityUpdated += (e) => EntityUpdated?.Invoke(e);
            _world.EntityDespawned += (e) => EntityDespawned?.Invoke(e);
        }

        public void Register(IPacketDispatcher dispatcher)
        {
            ArgumentNullException.ThrowIfNull(dispatcher);
            dispatcher.Register(S2C_0x00D_CharPc.PacketId, HandleCharPc);
            dispatcher.Register(S2C_0x00E_CharNpc.PacketId, HandleCharNpc);
            dispatcher.Register(S2C_0x037_CharStatus.PacketId, HandleCharStatus);
            dispatcher.Register(S2C_0x061_CliStatus.PacketId, HandleCliStatus);
            dispatcher.Register(S2C_0x062_CliStatus2.PacketId, HandleCliStatus2);
            dispatcher.Register(S2C_0x076_GroupEffects.PacketId, HandleGroupEffects);
            dispatcher.Register(S2C_0x077_EntityVis.PacketId, HandleEntityVis);
        }

        private void HandleCharPc(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var pc = new S2C_0x00D_CharPc(payload);
            if (!pc.IsValid) return;

            if (pc.IsDespawn)
            {
                GordianLog.Debug("ENTITY", $"PC despawn: ServerId=0x{pc.UniqueNo:X8}, Index={pc.ActorIndex}");
                _world.RemoveEntity(pc.UniqueNo);
                return;
            }

            if (!_world.TryGetByServerId(pc.UniqueNo, out var existing) || existing is not PlayerEntity player)
            {
                player = new PlayerEntity(pc.UniqueNo, pc.ActorIndex);
            }

            player.TargetIndex = pc.ActorIndex;
            player.IsSpawned = true;
            player.LastUpdatedUtc = DateTime.UtcNow;

            if (pc.HasPosition)
            {
                player.Position = new Vector3(pc.X, pc.Y, pc.Z);
                player.Direction = pc.Direction;
                player.Speed = pc.Speed;
                player.SpeedBase = pc.SpeedBase;
            }

            player.Hpp = pc.Hpp;
            player.AnimationState = pc.ServerStatus;
            player.ClaimServerId = pc.BtTargetId;

            player.GmLevel = pc.GmLevel;
            player.IsSeekingParty = pc.IsSeekingParty;
            player.IsAnonymous = pc.IsAnonymous;
            player.IsAway = pc.IsAway;
            player.IsInvisible = pc.IsInvisible;
            player.HasBazaar = pc.HasBazaar;
            player.IsCharmed = pc.IsCharmed;
            player.IsMentor = pc.IsMentor;
            player.IsNewPlayer = pc.IsNewPlayer;

            player.LsColorR = pc.LsColorR;
            player.LsColorG = pc.LsColorG;
            player.LsColorB = pc.LsColorB;

            player.PetActorIndex = pc.PetActorIndex;
            player.Appearance.CostumeId = pc.CostumeId;

            if (pc.HasModel)
            {
                Span<ushort> grap = stackalloc ushort[9];
                if (pc.TryGetGrapIdTable(grap))
                {
                    player.Appearance.CopyFrom(grap);
                }
            }

            if (pc.HasName)
            {
                string name = pc.GetName();
                if (!string.IsNullOrEmpty(name))
                {
                    player.Name = name;
                }
            }

            _world.UpsertEntity(player);
            GordianLog.Debug("ENTITY", $"Updated PC: {player.Name} (ID: 0x{player.ServerId:X8}, Index: {player.TargetIndex}) at {player.Position}");
        }

        private void HandleCharNpc(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var npcPacket = new S2C_0x00E_CharNpc(payload);
            if (!npcPacket.IsValid) return;

            if (npcPacket.IsDespawn)
            {
                GordianLog.Debug("ENTITY", $"NPC/Mob despawn: ServerId=0x{npcPacket.UniqueNo:X8}, Index={npcPacket.ActorIndex}");
                _world.RemoveEntity(npcPacket.UniqueNo);
                return;
            }

            EntityType type = npcPacket.SubKind switch
            {
                EntitySubKind.Elevator => EntityType.Elevator,
                EntitySubKind.Ship => EntityType.Ship,
                EntitySubKind.Door => EntityType.Door,
                EntitySubKind.Automaton => EntityType.Pet,
                _ => (npcPacket.ActorIndex < 1024) ? EntityType.Npc : EntityType.Monster
            };

            if (!_world.TryGetByServerId(npcPacket.UniqueNo, out var entity) || entity == null)
            {
                entity = new WorldEntity(npcPacket.UniqueNo, npcPacket.ActorIndex, type);
            }

            entity.TargetIndex = npcPacket.ActorIndex;
            entity.Type = type;
            entity.IsSpawned = true;
            entity.LastUpdatedUtc = DateTime.UtcNow;

            if (npcPacket.HasPosition)
            {
                entity.Position = new Vector3(npcPacket.X, npcPacket.Y, npcPacket.Z);
                entity.Direction = npcPacket.Direction;
                entity.Speed = npcPacket.Speed;
                entity.SpeedBase = npcPacket.SpeedBase;
            }

            entity.Hpp = npcPacket.Hpp;
            entity.AnimationState = npcPacket.ServerStatus;
            entity.ClaimServerId = npcPacket.ClaimId;

            uint modelId = npcPacket.GetModelId();
            if (modelId != 0)
            {
                entity.Appearance.ModelId = modelId;
            }

            if (npcPacket.HasName)
            {
                string name = npcPacket.GetName();
                if (!string.IsNullOrEmpty(name))
                {
                    entity.Name = name;
                }
            }

            _world.UpsertEntity(entity);
            GordianLog.Debug("ENTITY", $"Updated NPC/Mob: {entity.Name} (ID: 0x{entity.ServerId:X8}, Index: {entity.TargetIndex}) at {entity.Position}");
        }

        private void HandleCharStatus(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var charStatus = new S2C_0x037_CharStatus(payload);
            if (!charStatus.IsValid) return;

            _localPlayer.UpdateFromCharStatus(charStatus);
            GordianLog.Debug("ENTITY", $"Updated active character status: HPP={charStatus.Hpp}%, Speed={charStatus.Speed}");
        }

        private void HandleCliStatus(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var cliStatus = new S2C_0x061_CliStatus(payload);
            if (!cliStatus.IsValid) return;

            _localPlayer.UpdateFromCliStatus(cliStatus);
            GordianLog.Debug("ENTITY", $"Updated character stats: {cliStatus.MainJob} Lv{cliStatus.MainJobLevel}/{cliStatus.SubJob} Lv{cliStatus.SubJobLevel}, HP={cliStatus.HpMax}, MP={cliStatus.MpMax}, Atk={cliStatus.Attack}, Def={cliStatus.Defense}");
        }

        private void HandleCliStatus2(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var cliStatus2 = new S2C_0x062_CliStatus2(payload);
            if (!cliStatus2.IsValid) return;

            _localPlayer.UpdateFromCliStatus2(cliStatus2);
            GordianLog.Debug("ENTITY", "Updated character skills and ability recasts.");
        }

        private void HandleGroupEffects(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var effects = new S2C_0x076_GroupEffects(payload);
            if (!effects.IsValid) return;

            PartyBuffsReceived?.Invoke(effects);
            GordianLog.Debug("ENTITY", $"Received party group effects for {effects.MemberCount} members.");
        }

        private void HandleEntityVis(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var vis = new S2C_0x077_EntityVis(payload);
            if (!vis.IsValid) return;

            EntityVisibilityReceived?.Invoke(vis);
            GordianLog.Debug("ENTITY", $"Received visibility range update for {vis.Count} entities (Flags: {vis.Flags}).");
        }

        public async Task RequestEntityInfoAsync(ushort actIndex)
        {
            byte[] packet = EntityOutboundPackets.BuildCharReq(actIndex);
            if (LogOutboundOnRoute)
            {
                _logPacketCallback?.Invoke(PacketDirection.Outbound, 0x016, 0, packet);
            }
            await _sendChunkCallback(packet, true).ConfigureAwait(false);
        }

        public async Task RequestEntityUnexpectedAsync(
            ushort actIndex,
            uint uniqueNo2 = 0,
            uint uniqueNo3 = 0,
            ushort flg = 0,
            ushort flg2 = 0)
        {
            byte[] packet = EntityOutboundPackets.BuildCharReq2(actIndex, uniqueNo2, uniqueNo3, flg, flg2);
            if (LogOutboundOnRoute)
            {
                _logPacketCallback?.Invoke(PacketDirection.Outbound, 0x017, 0, packet);
            }
            await _sendChunkCallback(packet, true).ConfigureAwait(false);
        }

        public Task SendClientStatusAsync(ReadOnlySpan<uint> stats = default)
        {
            byte[] packet = EntityOutboundPackets.BuildClStat(stats);
            if (LogOutboundOnRoute)
            {
                _logPacketCallback?.Invoke(PacketDirection.Outbound, 0x00F, 0, packet);
            }
            return _sendChunkCallback(packet, true);
        }
    }
}
