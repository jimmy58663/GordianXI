// tests/Gordian.Core.Tests/Network/SchedulerPacketTests.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using Gordian.Core.Network.Packets;
using Gordian.Core.World;
using Xunit;

namespace Gordian.Core.Tests.Network
{
    public class SchedulerPacketTests
    {
        private static byte[] Schedulor(string fourCc, uint caster = 0x01000010, uint target = 0x01000020, ushort casterIdx = 16, ushort targetIdx = 32)
        {
            var p = new byte[16];
            BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(0, 4), caster);
            BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(4, 4), target);
            Encoding.ASCII.GetBytes(fourCc).CopyTo(p.AsSpan(8));
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(12, 2), casterIdx);
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(14, 2), targetIdx);
            return p;
        }

        private static byte[] Magic(ushort fileNum, byte type, uint caster = 0x01000010, uint target = 0x01000020, ushort casterIdx = 16, ushort targetIdx = 32)
        {
            var p = new byte[16];
            BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(0, 4), caster);
            BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(4, 4), target);
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(8, 2), casterIdx);
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(10, 2), targetIdx);
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(12, 2), fileNum);
            p[14] = type;
            return p;
        }

        private static (SchedulerState, WorldState, PacketDispatcher) Create()
        {
            var state = new SchedulerState();
            var world = new WorldState();
            var dispatcher = new PacketDispatcher();
            new SchedulerPacketModule(state, world).Register(dispatcher);
            return (state, world, dispatcher);
        }

        [Fact]
        public void Schedulor_DecodesEveryField()
        {
            var d = new S2C_0x038_Schedulor(Schedulor(SchedulerKeys.FadeOut));

            Assert.True(d.IsValid);
            Assert.Equal(0x01000010u, d.CasterServerId);
            Assert.Equal(0x01000020u, d.TargetServerId);
            Assert.Equal("kesu", d.Routine);
            Assert.Equal(0x7573656Bu, d.RoutineId); // LandSandBoat's FourCC::FadeOut
            Assert.Equal(16, d.CasterIndex);
            Assert.Equal(32, d.TargetIndex);
        }

        [Fact]
        public void Schedulor_NonPrintableKey_KeepsRawIdButNoText()
        {
            var p = Schedulor("abcd");
            p[9] = 0x01;
            var d = new S2C_0x038_Schedulor(p);

            Assert.True(d.IsValid);
            Assert.Equal(string.Empty, d.Routine);
            Assert.NotEqual(0u, d.RoutineId);
        }

        [Fact]
        public void Schedulor_ShortPayload_IsInvalid() =>
            Assert.False(new S2C_0x038_Schedulor(new byte[15]).IsValid);

        [Fact]
        public void MagicSchedulor_DecodesEveryField()
        {
            var d = new S2C_0x03A_MagicSchedulor(Magic(251, 4));

            Assert.True(d.IsValid);
            Assert.Equal(0x01000010u, d.CasterServerId);
            Assert.Equal(0x01000020u, d.TargetServerId);
            Assert.Equal(16, d.CasterIndex);
            Assert.Equal(32, d.TargetIndex);
            Assert.Equal(251, d.FileNumber);
            Assert.Equal(4, d.TypeId);
            Assert.Equal(MagicSchedulerType.Event2, d.Type);
        }

        [Fact]
        public void MagicSchedulor_ShortPayload_IsInvalid() =>
            Assert.False(new S2C_0x03A_MagicSchedulor(new byte[14]).IsValid);

        [Fact]
        public void Handlers_QueueRequestsWithTheCurrentZoneAndRaiseEvents()
        {
            var (state, world, d) = Create();
            world.CurrentZoneId = 230;
            ActorSchedulerRequest? actor = null;
            MagicSchedulerRequest? magic = null;
            state.ActorSchedulerPosted += r => actor = r;
            state.MagicSchedulerPosted += r => magic = r;

            d.Dispatch(new PacketHeader(0x038, 20, 1), Schedulor(SchedulerKeys.Sweating));
            d.Dispatch(new PacketHeader(0x03A, 20, 2), Magic(251, 4));

            Assert.Equal("hitl", actor!.Routine);
            Assert.Equal(230, actor.ZoneId);
            Assert.Equal(251, magic!.FileNumber);
            Assert.Equal(MagicSchedulerType.Event2, magic.Type);
            Assert.Equal(1, state.PendingActorCount);
            Assert.Equal(1, state.PendingMagicCount);
        }

        [Fact]
        public void Take_DrainsInArrivalOrder_AndClearDropsTheRest()
        {
            var (state, _, d) = Create();
            d.Dispatch(new PacketHeader(0x038, 20, 1), Schedulor("aaaa"));
            d.Dispatch(new PacketHeader(0x038, 20, 2), Schedulor("bbbb"));
            d.Dispatch(new PacketHeader(0x03A, 20, 3), Magic(1, 0));

            var actors = new List<ActorSchedulerRequest>();
            state.TakeActorSchedulers(actors);
            Assert.Equal(new[] { "aaaa", "bbbb" }, new[] { actors[0].Routine, actors[1].Routine });
            Assert.Equal(0, state.PendingActorCount);

            state.Clear();
            var magics = new List<MagicSchedulerRequest>();
            state.TakeMagicSchedulers(magics);
            Assert.Empty(magics);
        }

        [Fact]
        public void Queue_IsBounded_DroppingTheOldest()
        {
            var (state, _, d) = Create();
            for (int i = 0; i < SchedulerState.MaxPending + 5; i++)
            {
                d.Dispatch(new PacketHeader(0x03A, 20, 1), Magic((ushort)i, 0));
            }

            var magics = new List<MagicSchedulerRequest>();
            state.TakeMagicSchedulers(magics);
            Assert.Equal(SchedulerState.MaxPending, magics.Count);
            Assert.Equal(5, magics[0].FileNumber);
        }

        [Fact]
        public void PacketParser_ZoneChange_DropsQueuedSchedulers()
        {
            var world = new WorldState();
            var dispatcher = new PacketDispatcher();
            var parser = new Gordian.Core.Network.PacketParser(new Gordian.Core.Config.SessionProfile(), (_, _) => System.Threading.Tasks.Task.CompletedTask, dispatcher: dispatcher, world: world);
            Assert.NotNull(parser.Scheduler);

            dispatcher.Dispatch(new PacketHeader(0x038, 20, 1), Schedulor("kesu"));
            Assert.Equal(1, parser.Scheduler.PendingActorCount);

            world.CurrentZoneId = 100;
            Assert.Equal(0, parser.Scheduler.PendingActorCount);
        }
    }
}
