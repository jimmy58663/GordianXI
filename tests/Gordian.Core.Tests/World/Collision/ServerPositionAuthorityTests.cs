using System.Buffers.Binary;
using System.Numerics;
using Gordian.Core.Input;
using Gordian.Core.Network;
using Gordian.Core.Network.Packets;
using Gordian.Core.World;

namespace Gordian.Core.Tests.World.Collision
{
    /// <summary>
    /// Server-set positions (WPOS 0x05B) reach the right entity and win over local movement.
    /// </summary>
    public class ServerPositionAuthorityTests
    {
        private const uint LocalId = 0x00100001;
        private const uint OtherId = 0x00100002;

        private sealed class Harness : IDisposable
        {
            public SessionNetworkManager Net { get; } = new("127.0.0.1", 54239);
            public PlayerEntity Local { get; }
            public PlayerLocomotionController Controller { get; }
            public InputState Input { get; } = new();

            public Harness()
            {
                Net.Parser.LocalPlayer.ServerId = LocalId;
                Local = new PlayerEntity(LocalId, 1) { Position = new Vector3(10, 0, 10), IsSpawned = true };
                Net.Parser.World.UpsertEntity(Local);
                Controller = new PlayerLocomotionController(Input, InputProfile.CreateCompact(), Net.Parser.World, Net.Parser.LocalPlayer);
            }

            public void Wpos(uint uniqueNo, Vector3 position, PosMode mode, byte direction = 64)
            {
                byte[] payload = new byte[24];
                BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(0, 4), position.X);
                BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(4, 4), position.Y);
                BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(8, 4), position.Z);
                BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(12, 4), uniqueNo);
                payload[18] = (byte)mode;
                payload[19] = direction;
                Net.Parser.Dispatcher.Dispatch(new PacketHeader(0x05B, 28, 1), payload);
            }

            public void Tick(int count = 1)
            {
                for (int i = 0; i < count; i++) Controller.Update(TimeSpan.FromSeconds(1.0 / 60.0));
            }

            public void Dispose() => Net.Dispose();
        }

        [Fact]
        public void LocalWarp_WinsOverAMovementTickInFlight()
        {
            using var h = new Harness();
            h.Wpos(LocalId, new Vector3(50, 0, -20), PosMode.Normal, direction: 128);

            // A movement tick that read the old position before the packet landed writes it back.
            h.Local.Position = new Vector3(10.1f, 0, 10);
            h.Tick();

            Assert.Equal(new Vector3(50, 0, -20), h.Local.Position);
            Assert.Equal(128, h.Local.Direction);
            Assert.Equal(50f, h.Net.PositionX);
        }

        [Fact]
        public void OtherEntitysWarp_MovesThatEntityNotTheLocalPlayer()
        {
            using var h = new Harness();
            var other = new PlayerEntity(OtherId, 2) { Position = new Vector3(0, 0, 0), IsSpawned = true };
            h.Net.Parser.World.UpsertEntity(other);

            h.Wpos(OtherId, new Vector3(-30, 1, 5), PosMode.Normal);
            h.Tick();

            Assert.Equal(new Vector3(10, 0, 10), h.Local.Position);
            Assert.Equal(new Vector3(-30, 1, 5), other.TargetPosition);
            Assert.True(other.SnapToTargetPending);
        }

        [Fact]
        public void RotateOnlyTurnsAndLockHoldsThePlayerUntilUnlocked()
        {
            using var h = new Harness();
            h.Wpos(LocalId, new Vector3(99, 99, 99), PosMode.Rotate, direction: 192);
            h.Tick();
            Assert.Equal(new Vector3(10, 0, 10), h.Local.Position);
            Assert.Equal(192, h.Local.Direction);

            h.Wpos(LocalId, Vector3.Zero, PosMode.Lock);
            h.Input.SetKeyDown(GordianKey.W);
            h.Tick(30);
            Assert.Equal(new Vector3(10, 0, 10), h.Local.Position);

            h.Wpos(LocalId, Vector3.Zero, PosMode.Unlock);
            h.Tick(30);
            Assert.NotEqual(new Vector3(10, 0, 10), h.Local.Position);
        }

        [Fact]
        public void ZoningClearsAStuckLock()
        {
            using var h = new Harness();
            h.Wpos(LocalId, Vector3.Zero, PosMode.Lock);
            Assert.True(h.Net.Parser.LocalPlayer.IsMovementLocked);

            h.Net.Parser.World.CurrentZoneId = 230;
            Assert.False(h.Net.Parser.LocalPlayer.IsMovementLocked);
        }

        [Fact]
        public void CharmedPlayer_IgnoresInputAndTakesServerPositions()
        {
            using var h = new Harness();
            h.Local.IsCharmed = true;
            h.Input.SetKeyDown(GordianKey.W);
            h.Tick(30);
            Assert.Equal(new Vector3(10, 0, 10), h.Local.Position);

            h.Net.Parser.LocalPlayer.RequestPositionCorrection(new Vector3(12, 0, 11), 32);
            h.Tick();
            Assert.Equal(new Vector3(12, 0, 11), h.Local.Position);
        }
    }
}
