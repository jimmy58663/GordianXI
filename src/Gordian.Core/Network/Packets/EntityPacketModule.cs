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
        private readonly PartyState? _party;
        private readonly Func<ReadOnlyMemory<byte>, bool, Task> _sendChunkCallback;
        private readonly Action<PacketDirection, ushort, ushort, ReadOnlySpan<byte>>? _logPacketCallback;
        private readonly Dictionary<ushort, DateTime> _pendingEntityRequests = new();

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
            Action<PacketDirection, ushort, ushort, ReadOnlySpan<byte>>? logPacketCallback = null,
            PartyState? party = null)
        {
            _party = party;
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
            dispatcher.Register(S2C_0x01B_JobInfo.PacketId, HandleJobInfo);
            dispatcher.Register(S2C_0x037_CharStatus.PacketId, HandleCharStatus);
            dispatcher.Register(S2C_0x061_CliStatus.PacketId, HandleCliStatus);
            dispatcher.Register(S2C_0x062_CliStatus2.PacketId, HandleCliStatus2);
            dispatcher.Register(S2C_0x076_GroupEffects.PacketId, HandleGroupEffects);
            dispatcher.Register(S2C_0x077_EntityVis.PacketId, HandleEntityVis);
            dispatcher.Register(S2C_0x0DF_GroupAttr.PacketId, HandleGroupAttr);
        }

        /// <summary>
        /// Monotonic clock (seconds) used to timestamp remote position samples; must match the clock the render thread
        /// plays them back with. Injectable for deterministic tests.
        /// </summary>
        public Func<double> Clock { get; set; } = () => WorldEntity.ClockSeconds;

        /// <summary>
        /// Adds a server position to a remote entity's motion timeline (see <see cref="WorldEntity.AddServerSample"/>).
        /// A stop clears <see cref="WorldEntity.Speed"/> once playback reaches the final position, so the locomotion
        /// animation plays until then.
        /// </summary>
        private void AddMotionSample(WorldEntity entity, Vector3 newPos, bool isMoving, ushort movTime, byte direction)
        {
            entity.AddServerSample(newPos, isMoving, movTime, direction, Clock());
            entity.StopOnArrival = !isMoving;
        }

        private void HandleCharPc(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var pc = new S2C_0x00D_CharPc(payload);
            if (!pc.IsValid) return;

            if (pc.IsDespawn)
            {
                GordianLog.Debug("ENTITY", $"PC despawn: ServerId=0x{pc.UniqueNo:X8}, Index={pc.ActorIndex}");
                _pendingEntityRequests.Remove(pc.ActorIndex);
                _world.RemoveEntity(pc.UniqueNo);
                return;
            }

            PlayerEntity player;
            bool isNew = false;
            if (!_world.TryGetByServerId(pc.UniqueNo, out var existing) || existing is not PlayerEntity existingPlayer)
            {
                player = new PlayerEntity(pc.UniqueNo, pc.ActorIndex);
                isNew = true;
            }
            else
            {
                player = existingPlayer;
            }

            player.TargetIndex = pc.ActorIndex;
            player.IsSpawned = true;
            player.LastUpdatedUtc = DateTime.UtcNow;

            ushort movTime = pc.MovTime;
            bool movTimeChanged = player.LastMovTime != 0 && movTime != player.LastMovTime;
            bool isMovingByMovTime = S2C_0x00D_CharPc.IsMovingMovTime(movTime) && (!S2C_0x00D_CharPc.IsMovingMovTime(player.LastMovTime) || movTimeChanged);
            player.LastMovTime = movTime;

            if (pc.HasPosition)
            {
                if (pc.UniqueNo != _localPlayer.ServerId)
                {
                    var newPos = new Vector3(pc.X, pc.Y, pc.Z);

                    if (!isNew && player.IsSpawned)
                    {
                        float dist = Vector3.Distance(newPos, player.TargetPosition);
                        DateTime now = DateTime.UtcNow;
                        double dtMs = player.LastPositionChangeUtc != DateTime.MinValue
                            ? (now - player.LastPositionChangeUtc).TotalMilliseconds
                            : 0;

                        if (S2C_0x00D_CharPc.IsMovingMovTime(movTime) && (dist > 0.05f || isMovingByMovTime))
                        {
                            byte prevSpeed = player.Speed;
                            player.Speed = pc.Speed > 0 ? pc.Speed : (byte)50;
                            player.StopOnArrival = false;
                            if (pc.SpeedBase > 0)
                            {
                                player.SpeedBase = pc.SpeedBase;
                            }

                            player.LastPositionChangeUtc = now;
                            AddMotionSample(player, newPos, isMoving: true, movTime, pc.Direction);

                            string phase = prevSpeed == 0 ? "MOVE START" : "MOVE PACKET";
                            GordianLog.Info("Locomotion", $"[0x00D PC 0x{pc.UniqueNo:X8}:{player.Name}] {phase}: pos=({newPos.X:F2},{newPos.Y:F2},{newPos.Z:F2}), dist={dist:F2}, movTime={movTime}, speed={player.Speed}, packetDelta={dtMs:F0}ms, sampleAge={player.LastSampleAgeSeconds:F2}s, delay={player.PlaybackDelaySeconds:F2}s, behind={Vector3.Distance(newPos, player.Position):F2}");
                        }
                        else if (S2C_0x00D_CharPc.IsMovingMovTime(movTime))
                        {
                            // Repeat of the last movement sample (e.g. a status refresh mid-run): nothing new to play back.
                        }
                        else
                        {
                            // Stopped (or a non-movement update): let the character finish walking to the final position
                            // rather than snapping, and drop to idle once it gets there.
                            if (player.Speed > 0)
                            {
                                GordianLog.Info("Locomotion", $"[0x00D PC 0x{pc.UniqueNo:X8}:{player.Name}] MOVE STOP PACKET: pos=({newPos.X:F2},{newPos.Y:F2},{newPos.Z:F2}), dist={dist:F2}, movTime={movTime}, remaining={Vector3.Distance(newPos, player.Position):F2}");
                            }
                            float horizontal = Vector2.Distance(new Vector2(newPos.X, newPos.Z),
                                                                new Vector2(player.TargetPosition.X, player.TargetPosition.Z));
                            if (horizontal < 0.05f && player.Speed == 0)
                            {
                                // Only the height changed while standing (e.g. riding a lift): no step to walk.
                                player.Warp(newPos, pc.Direction, WorldEntity.ClockSeconds);
                            }
                            else
                            {
                                if (dist > 0.05f)
                                {
                                    player.LastPositionChangeUtc = now;
                                }
                                AddMotionSample(player, newPos, isMoving: false, movTime, pc.Direction);
                            }
                        }
                    }
                    else
                    {
                        player.Position = newPos;
                        player.TargetPosition = newPos;
                        player.Speed = 0;
                    }

                    if (!player.HasMotionTimeline)
                    {
                        // Once the entity has a motion timeline, facing is played back from it in step with position.
                        player.Direction = pc.Direction;
                    }
                    if (isNew)
                    {
                        player.RenderHeadingRadians = player.HeadingRadians;
                    }
                }
                else if (pc.IsCharmed)
                {
                    // Charmed: the server moves the local player and ignores the client's reported position.
                    _localPlayer.RequestPositionCorrection(new Vector3(pc.X, pc.Y, pc.Z), pc.Direction);
                }
                player.SpeedBase = pc.SpeedBase;
            }

            if (isNew || (pc.UpdateFlags & EntityUpdateFlags.General) != 0)
            {
                player.Hpp = pc.Hpp;
                player.AnimationState = pc.ServerStatus;
            }
            else if (pc.Hpp > 0)
            {
                player.Hpp = pc.Hpp;
            }
            if (isNew || (pc.UpdateFlags & EntityUpdateFlags.ClaimStatus) != 0)
            {
                player.ClaimServerId = pc.BtTargetId;
            }

            player.GmLevel = pc.GmLevel;
            if (pc.UniqueNo == _localPlayer.ServerId)
            {
                _localPlayer.GmLevel = pc.GmLevel;
            }
            player.IsSeekingParty = pc.IsSeekingParty;
            player.IsAnonymous = pc.IsAnonymous;
            player.IsAway = pc.IsAway;
            player.IsInvisible = pc.IsInvisible;
            player.GraphSize = pc.GraphSize;
            player.IsHidden = pc.IsHidden;
            player.IsNonBlocking = pc.IsNonBlocking;
            if (pc.HasPosition) player.IgnoresWorldCollision = pc.IgnoresWorldCollision;
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
                    _pendingEntityRequests.Remove(pc.ActorIndex);
                }
            }

            if (string.IsNullOrEmpty(player.Name))
            {
                TryRequestEntityInfo(pc.ActorIndex);
            }

            _world.UpsertEntity(player);
        }

        private void HandleCharNpc(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var npcPacket = new S2C_0x00E_CharNpc(payload);
            if (!npcPacket.IsValid) return;

            if (npcPacket.IsDespawn)
            {
                GordianLog.Debug("ENTITY", $"NPC/Mob despawn: ServerId=0x{npcPacket.UniqueNo:X8}, Index={npcPacket.ActorIndex}");
                _pendingEntityRequests.Remove(npcPacket.ActorIndex);
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

            WorldEntity entity;
            bool isNew = false;
            if (!_world.TryGetByServerId(npcPacket.UniqueNo, out var existing) || existing == null)
            {
                entity = new WorldEntity(npcPacket.UniqueNo, npcPacket.ActorIndex, type);
                isNew = true;
            }
            else
            {
                entity = existing;
            }

            entity.TargetIndex = npcPacket.ActorIndex;
            entity.Type = type;
            entity.IsSpawned = true;
            entity.LastUpdatedUtc = DateTime.UtcNow;

            // For NPCs/Monsters, Flags0 bits 0..12 contain static database flags in LandSandBoat, not a movement timer.
            // NPCs/monsters determine motion strictly from position displacement.
            entity.LastMovTime = 0;

            if (npcPacket.HasPosition)
            {
                var newPos = new Vector3(npcPacket.X, npcPacket.Y, npcPacket.Z);

                if (!isNew && entity.IsSpawned)
                {
                    float dist = Vector3.Distance(newPos, entity.TargetPosition);
                    DateTime now = DateTime.UtcNow;
                    double dtMs = entity.LastPositionChangeUtc != DateTime.MinValue
                        ? (now - entity.LastPositionChangeUtc).TotalMilliseconds
                        : 0;

                    if (dist > 0.05f)
                    {
                        byte prevSpeed = entity.Speed;
                        entity.Speed = npcPacket.Speed > 0 ? npcPacket.Speed : (byte)40;
                        if (npcPacket.SpeedBase > 0)
                        {
                            entity.SpeedBase = npcPacket.SpeedBase;
                        }

                        entity.StopOnArrival = false;
                        entity.LastPositionChangeUtc = now;
                        AddMotionSample(entity, newPos, isMoving: true, movTime: 0, npcPacket.Direction);

                        string phase = prevSpeed == 0 ? "MOVE START" : "MOVE PACKET";
                        GordianLog.Info("Locomotion", $"[0x00E NPC 0x{npcPacket.UniqueNo:X8}:{entity.Name}] {phase}: pos=({newPos.X:F2},{newPos.Y:F2},{newPos.Z:F2}), dist={dist:F2}, speed={entity.Speed}, packetDelta={dtMs:F0}ms, delay={entity.PlaybackDelaySeconds:F2}s, behind={Vector3.Distance(newPos, entity.Position):F2}");
                    }
                    else
                    {
                        if (entity.Speed > 0)
                        {
                            GordianLog.Info("Locomotion", $"[0x00E NPC 0x{npcPacket.UniqueNo:X8}:{entity.Name}] MOVE STOP PACKET: pos=({newPos.X:F2},{newPos.Y:F2},{newPos.Z:F2}), remaining={Vector3.Distance(newPos, entity.Position):F2}");
                        }
                        AddMotionSample(entity, newPos, isMoving: false, movTime: 0, npcPacket.Direction);
                    }
                }
                else
                {
                    entity.Position = newPos;
                    entity.TargetPosition = newPos;
                    entity.Speed = 0;
                }

                if (!entity.HasMotionTimeline)
                {
                    // Once the entity has a motion timeline, facing is played back from it in step with position.
                    entity.Direction = npcPacket.Direction;
                }
                if (isNew)
                {
                    entity.RenderHeadingRadians = entity.HeadingRadians;
                }
                entity.SpeedBase = npcPacket.SpeedBase;
            }

            if (isNew || (npcPacket.UpdateFlags & (EntityUpdateFlags.Position | EntityUpdateFlags.General)) != 0)
            {
                entity.GraphSize = npcPacket.GraphSize;
                entity.IsHidden = npcPacket.IsHidden;
                entity.IsInvisible = npcPacket.IsInvisible;
                if (npcPacket.HasPosition) entity.IgnoresWorldCollision = npcPacket.IgnoresWorldCollision;
            }
            if (isNew || (npcPacket.UpdateFlags & EntityUpdateFlags.General) != 0)
            {
                entity.IsNonBlocking = npcPacket.IsNonBlocking;
                entity.Hpp = npcPacket.Hpp;
                entity.AnimationState = npcPacket.ServerStatus;
                entity.AnimationSub = npcPacket.AnimationSub;
            }
            else if (npcPacket.Hpp > 0)
            {
                entity.Hpp = npcPacket.Hpp;
            }
            if (isNew || (npcPacket.UpdateFlags & EntityUpdateFlags.ClaimStatus) != 0)
            {
                entity.ClaimServerId = npcPacket.ClaimId;
            }

            if (npcPacket.TryGetTransport(out string transportId, out uint legStart, out byte travel))
            {
                double arrival = VanaTime.GetEarthSecondsSinceEpoch(DateTime.UtcNow);
                if (!isNew && legStart != entity.TransportStartSeconds)
                {
                    // A new leg is sent as it starts: play it from here (see MovingPlatforms.LegStart).
                    entity.TransportObservedSeconds = arrival;
                    _world.TransportClockSkewSeconds = global::Gordian.Core.World.Collision.MovingPlatforms.RefineClockSkew(_world.TransportClockSkewSeconds, arrival, legStart);
                }
                GordianLog.Debug("Elevator", $"[0x00E 0x{npcPacket.UniqueNo:X8}] id={transportId} flags={npcPacket.UpdateFlags} anim={entity.AnimationState} " +
                    $"stamp={legStart} (was {entity.TransportStartSeconds}) travel={travel} arrival={arrival:F3} (arrival-stamp={arrival - legStart:F3}) " +
                    $"observed={entity.TransportObservedSeconds:F3} isNew={isNew} pos=({entity.Position.X:F2},{entity.Position.Y:F2},{entity.Position.Z:F2})");
                entity.TransportId = transportId;
                entity.TransportStartSeconds = legStart;
                entity.TransportTravelSeconds = travel;
            }

            if (npcPacket.TryGetEquippedLook(out _, out _, out var grapTable))
            {
                entity.Appearance.GrapIdTable = grapTable;
                entity.Appearance.ModelId = 0;
            }
            else
            {
                uint modelId = npcPacket.GetModelId();
                if (modelId != 0)
                {
                    entity.Appearance.ModelId = modelId;
                }
            }

            if (npcPacket.HasName)
            {
                string name = npcPacket.GetName();
                if (!string.IsNullOrEmpty(name))
                {
                    entity.Name = name;
                    _pendingEntityRequests.Remove(npcPacket.ActorIndex);
                }
            }

            if (string.IsNullOrEmpty(entity.Name) && type != EntityType.Elevator && type != EntityType.Ship && type != EntityType.Door)
            {
                TryRequestEntityInfo(npcPacket.ActorIndex);
            }

            _world.UpsertEntity(entity);
        }

        private void HandleCharStatus(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var charStatus = new S2C_0x037_CharStatus(payload);
            if (!charStatus.IsValid) return;

            _localPlayer.UpdateFromCharStatus(charStatus);
            if (_world.TryGetByServerId(_localPlayer.ServerId, out var localEnt) && localEnt != null)
            {
                if (charStatus.Speed > 0)
                {
                    localEnt.Speed = (byte)Math.Min((ushort)255, charStatus.Speed);
                }
                if (charStatus.SpeedBase > 0)
                {
                    localEnt.SpeedBase = charStatus.SpeedBase;
                }
            }
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

        private void HandleJobInfo(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var jobInfo = new S2C_0x01B_JobInfo(payload);
            if (!jobInfo.IsValid) return;

            _localPlayer.UpdateFromJobInfo(jobInfo);
            GordianLog.Debug("ENTITY", $"Updated job info: {jobInfo.MainJob} Lv{jobInfo.MainJobLevel}/{jobInfo.SubJob} Lv{jobInfo.SubJobLevel}, MaxHP={jobInfo.HpMax}, MaxMP={jobInfo.MpMax}");
        }

        private void HandleGroupAttr(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var groupAttr = new S2C_0x0DF_GroupAttr(payload);
            if (!groupAttr.IsValid) return;

            if (_localPlayer.ServerId == 0 || groupAttr.UniqueNo == _localPlayer.ServerId)
            {
                if (_localPlayer.ServerId == 0)
                {
                    _localPlayer.ServerId = groupAttr.UniqueNo;
                }
                _localPlayer.UpdateFromGroupAttr(groupAttr);
                GordianLog.Debug("ENTITY", $"Updated local player vitals from GroupAttr: HP={groupAttr.Hp}, MP={groupAttr.Mp}, TP={groupAttr.Tp}, HPP={groupAttr.Hpp}%, Job={groupAttr.MainJob} Lv{groupAttr.MainJobLevel}");
            }

            // 0x0DF also carries every party member's vitals as they change (0x0DD only on roster changes).
            _party?.UpdateVitals(groupAttr.UniqueNo, groupAttr.Hp, groupAttr.Mp, groupAttr.Tp, groupAttr.Hpp, groupAttr.Mpp);

            if (_world.TryGetByServerId(groupAttr.UniqueNo, out var entity) && entity != null)
            {
                entity.Hpp = groupAttr.Hpp;
                entity.LastUpdatedUtc = DateTime.UtcNow;
            }
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

        public async Task RequestClientStatusAsync()
        {
            byte[] packet = EntityOutboundPackets.BuildCliStatus();
            if (LogOutboundOnRoute)
            {
                _logPacketCallback?.Invoke(PacketDirection.Outbound, 0x061, 0, packet);
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

        private void TryRequestEntityInfo(ushort actorIndex)
        {
            DateTime now = DateTime.UtcNow;
            if (!_pendingEntityRequests.TryGetValue(actorIndex, out var lastReq) || (now - lastReq).TotalSeconds >= 2.0)
            {
                _pendingEntityRequests[actorIndex] = now;
                _ = RequestEntityInfoAsync(actorIndex);
            }
        }
    }
}
