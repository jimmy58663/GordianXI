// tests/Gordian.Engine.Tests/Input/ZoneArrivalFacingTests.cs
using System;
using System.Buffers.Binary;
using System.Numerics;
using Gordian.Core.Network;
using Gordian.Core.Network.Packets;
using Gordian.Core.World;
using Gordian.Engine.Sessions;
using Xunit;

namespace Gordian.Engine.Tests.Input
{
    /// <summary>
    /// #588: a zone change on the real session path. The 0x00B clears the world (the locomotion tick makes a stand-in
    /// entity facing east), ticks run while the screen is black, then a real S2C 0x00A goes through the parser. The
    /// entity, the published tick and the camera must all take its heading, and keep it through the reveal.
    /// </summary>
    public class ZoneArrivalFacingTests
    {
        private const uint Id = 0x01000001;

        private static byte[] LoginAck(byte direction, Vector3 position, ushort zone)
        {
            byte[] p = new byte[0x100];
            BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(0), Id);
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(4), 1024);
            p[7] = direction;
            BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(8), position.X);
            BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(12), position.Y);
            BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(16), position.Z);
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(44), zone);
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(0x40), 0x0302);
            System.Text.Encoding.ASCII.GetBytes("Me").CopyTo(p.AsSpan(0x80));
            return p;
        }

        [Fact]
        public void ZoneIn_TheEntityTickAndCamera_TakeThe0x00AHeading_ThroughTheReveal()
        {
            var net = new SessionNetworkManager("127.0.0.1", 54001) { CurrentState = SessionState.ActiveInWorld };
            var session = new CharacterSession("Me", Id, "acc", net);
            var world = net.World;
            var locomotion = session.Locomotion;
            net.Parser.LocalPlayer.ServerId = Id;
            world.UpsertEntity(new PlayerEntity(Id, 1024) { Position = new Vector3(10, 0, 10), Direction = 0, IsSpawned = true, SpeedBase = 50 });
            for (int i = 0; i < 3; i++) locomotion.Update(TimeSpan.FromMilliseconds(16));

            world.Clear(); // S2C 0x00B
            locomotion.InputBlocked = true;
            for (int i = 0; i < 3; i++) locomotion.Update(TimeSpan.FromMilliseconds(16));

            // Port Jeuno -> Lower Jeuno: LandSandBoat's 3.653041 rad arrival facing is wire 148 (208 degrees).
            byte[] ack = LoginAck(148, new Vector3(27.9f, -2.6f, 42.4f), 245);
            net.Parser.Dispatcher.Dispatch(new PacketHeader(S2C_0x00A_LoginAck.PacketId, (ushort)(ack.Length + 4), 1), ack);
            for (int i = 0; i < 10; i++) locomotion.Update(TimeSpan.FromMilliseconds(16));
            Assert.True(world.TryGetByServerId(Id, out var me));
            Assert.Equal(148, me!.Direction);
            Assert.Equal(208.125f, locomotion.CameraYaw, 2);
            Assert.Equal(208.125f, locomotion.LastTick!.PlayerHeadingRadians * 180.0f / MathF.PI, 2);
            Assert.Equal(1, locomotion.ArrivalFacingCount);

            locomotion.InputBlocked = false; // the screen is revealed
            for (int i = 0; i < 5; i++) locomotion.Update(TimeSpan.FromMilliseconds(16));
            Assert.Equal(148, me.Direction);
            Assert.Equal(208.125f, locomotion.CameraYaw, 2);
            Assert.Equal(2, locomotion.ArrivalFacingCount); // turned behind it again at the reveal
        }
    }
}
