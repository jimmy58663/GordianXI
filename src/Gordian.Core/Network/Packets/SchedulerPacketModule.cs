// src/Gordian.Core/Network/Packets/SchedulerPacketModule.cs
using System;
using Gordian.Core.Diagnostics;
using Gordian.Core.World;

namespace Gordian.Core.Network.Packets
{
    /// <summary>
    /// Packet domain module for the actor and magic scheduler packets: S2C 0x038 and 0x03A are decoded into
    /// <see cref="SchedulerState"/>. Decode only; playback is not wired (S2C 0x039 map schedulers are handled by
    /// <see cref="EntityPacketModule"/>).
    /// </summary>
    public sealed class SchedulerPacketModule
    {
        private readonly SchedulerState _state;
        private readonly WorldState _world;

        public SchedulerState State => _state;

        public SchedulerPacketModule(SchedulerState state, WorldState world)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _world = world ?? throw new ArgumentNullException(nameof(world));
        }

        public void Register(IPacketDispatcher dispatcher)
        {
            ArgumentNullException.ThrowIfNull(dispatcher);
            dispatcher.Register(S2C_0x038_Schedulor.PacketId, HandleSchedulor);
            dispatcher.Register(S2C_0x03A_MagicSchedulor.PacketId, HandleMagicSchedulor);
        }

        public void Unregister(IPacketDispatcher dispatcher)
        {
            ArgumentNullException.ThrowIfNull(dispatcher);
            dispatcher.Unregister(S2C_0x038_Schedulor.PacketId);
            dispatcher.Unregister(S2C_0x03A_MagicSchedulor.PacketId);
        }

        private void HandleSchedulor(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var p = new S2C_0x038_Schedulor(payload);
            if (!p.IsValid) return;

            GordianLog.Debug("SCHEDULER", $"Actor scheduler '{p.Routine}' (0x{p.RoutineId:X8}): caster 0x{p.CasterServerId:X8} [{p.CasterIndex}] -> target 0x{p.TargetServerId:X8} [{p.TargetIndex}].");
            _state.PostActor(new ActorSchedulerRequest(_world.CurrentZoneId, p.Routine, p.RoutineId, p.CasterServerId, p.TargetServerId, p.CasterIndex, p.TargetIndex));
        }

        private void HandleMagicSchedulor(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var p = new S2C_0x03A_MagicSchedulor(payload);
            if (!p.IsValid) return;

            GordianLog.Debug("SCHEDULER", $"Magic scheduler file {p.FileNumber} type {p.TypeId}: caster 0x{p.CasterServerId:X8} [{p.CasterIndex}] -> target 0x{p.TargetServerId:X8} [{p.TargetIndex}].");
            _state.PostMagic(new MagicSchedulerRequest(_world.CurrentZoneId, p.FileNumber, p.TypeId, p.CasterServerId, p.TargetServerId, p.CasterIndex, p.TargetIndex));
        }
    }
}
