using System.Buffers.Binary;
using System.Numerics;
using Gordian.Core.Input;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.World;
using Gordian.Core.World.Collision;

namespace Gordian.Core.Tests.World.Collision
{
    public class ZoneCollisionTests
    {
        private static readonly Vector3 Up = new(0.0f, -1.0f, 0.0f); // internal space: -Y is up

        /// <summary>
        /// Two up-facing triangles covering [x0, x1] x [z0, z1] at height y (internal space).
        /// </summary>
        private static IEnumerable<CollisionTriangle> Floor(float x0, float z0, float x1, float z1, float y,
                                                            CollisionTerrain terrain = CollisionTerrain.Grass)
        {
            yield return new CollisionTriangle(new(x0, y, z0), new(x1, y, z0), new(x1, y, z1), Up, false, terrain, false);
            yield return new CollisionTriangle(new(x0, y, z0), new(x1, y, z1), new(x0, y, z1), Up, false, terrain, false);
        }

        [Fact]
        public void TryGetGround_ReturnsFloorBelowFeet()
        {
            var mesh = new ZoneCollisionMesh(Floor(-10, -10, 10, 10, 2.0f).ToList());

            Assert.True(mesh.TryGetGround(new Vector3(1.0f, -3.0f, 1.0f), 0.75f, 60.0f, out var hit));
            Assert.Equal(2.0f, hit.Height, 3);
            Assert.Equal(CollisionTerrain.Grass, hit.Terrain);
        }

        [Fact]
        public void TryGetGround_StepsUpOntoLowRiseButIgnoresSurfaceOverhead()
        {
            // Ground at y = 0, a bridge deck 5 yalms up (y = -5) and a 0.5-yalm step (y = -0.5) off to the side.
            var triangles = Floor(-10, -10, 10, 10, 0.0f).ToList();
            triangles.AddRange(Floor(-10, -2, 10, 2, -5.0f, CollisionTerrain.Wood));
            triangles.AddRange(Floor(5, 5, 9, 9, -0.5f, CollisionTerrain.Stone));
            var mesh = new ZoneCollisionMesh(triangles);

            Assert.True(mesh.TryGetGround(new Vector3(0.0f, 0.0f, 0.0f), 0.75f, 60.0f, out var underBridge));
            Assert.Equal(0.0f, underBridge.Height, 3);

            Assert.True(mesh.TryGetGround(new Vector3(6.0f, 0.0f, 6.0f), 0.75f, 60.0f, out var step));
            Assert.Equal(-0.5f, step.Height, 3);
            Assert.Equal(CollisionTerrain.Stone, step.Terrain);

            Assert.True(mesh.TryGetGround(new Vector3(0.0f, -5.0f, 0.0f), 0.75f, 60.0f, out var onBridge));
            Assert.Equal(-5.0f, onBridge.Height, 3);
        }

        [Fact]
        public void TryGetGround_IgnoresWallsCeilingsAndOutOfRangeDrops()
        {
            var triangles = new List<CollisionTriangle>
            {
                // A vertical wall and a downward-facing ceiling over the query point.
                new(new(0, 0, -5), new(0, -5, -5), new(0, -5, 5), Vector3.UnitX, true, CollisionTerrain.Stone, false),
                new(new(-5, -3, -5), new(5, -3, -5), new(5, -3, 5), Vector3.UnitY, false, CollisionTerrain.Stone, false),
            };
            triangles.AddRange(Floor(-5, -5, 5, 5, 100.0f));
            var mesh = new ZoneCollisionMesh(triangles);

            Assert.False(mesh.TryGetGround(new Vector3(1.0f, 0.0f, 1.0f), 0.75f, 60.0f, out _));
            Assert.True(mesh.TryGetGround(new Vector3(1.0f, 50.0f, 1.0f), 0.75f, 60.0f, out var deep));
            Assert.Equal(100.0f, deep.Height, 3);
        }

        /// <summary>
        /// Eight 0.25-yalm steps with 0.5-yalm treads rising toward +X, starting at x = 0.
        /// </summary>
        private static ZoneCollisionMesh Staircase()
        {
            var triangles = Floor(-10, -5, 0, 5, 0.0f).ToList();
            for (int i = 0; i < 8; i++) triangles.AddRange(Floor(i * 0.5f, -5, (i + 1) * 0.5f, 5, -0.25f * (i + 1)));
            triangles.AddRange(Floor(4, -5, 14, 5, -2.0f));
            return new ZoneCollisionMesh(triangles);
        }

        [Fact]
        public void TryGetSteppedGround_ClimbsAndDescendsStairsAsARamp()
        {
            var mesh = Staircase();
            float Walk(float from, float to, float y)
            {
                float largestJump = 0.0f;
                var feet = new Vector3(from, y, 0.0f);
                float step = MathF.Sign(to - from) * 0.05f;
                for (float x = from; MathF.Abs(x - to) > 0.01f; x += step)
                {
                    feet.X = x;
                    Assert.True(mesh.TryGetSteppedGround(feet, 0.75f, 60.0f, 0.9f, out var hit));
                    largestJump = MathF.Max(largestJump, MathF.Abs(hit.Height - feet.Y));
                    feet.Y = hit.Height;
                }
                Assert.Equal(from < to ? -2.0f : 0.0f, feet.Y, 3);
                return largestJump;
            }

            // Every 0.05-yalm move changes the height by well under a riser (snapping jumps the full 0.25).
            Assert.InRange(Walk(-3.0f, 7.0f, 0.0f), 0.0f, 0.1f);
            Assert.InRange(Walk(7.0f, -3.0f, -2.0f), 0.0f, 0.1f);
        }

        [Fact]
        public void TryGetSteppedGround_DoesNotFloatOnSlopes()
        {
            var up = Vector3.Normalize(new Vector3(0.5f, -1.0f, 0.0f));
            var mesh = new ZoneCollisionMesh(new List<CollisionTriangle>
            {
                new(new(-10, 5, -5), new(10, -5, -5), new(10, -5, 5), up, false, CollisionTerrain.Grass, false),
                new(new(-10, 5, -5), new(10, -5, 5), new(-10, 5, 5), up, false, CollisionTerrain.Grass, false),
            });

            Assert.True(mesh.TryGetSteppedGround(new Vector3(2.0f, -1.0f, 0.0f), 0.75f, 60.0f, 0.9f, out var hit));
            Assert.Equal(-1.0f, hit.Height, 3);
        }

        [Fact]
        public void TryGetGround_OutsideTheMeshReturnsFalse()
        {
            var mesh = new ZoneCollisionMesh(Floor(0, 0, 4, 4, 0.0f).ToList());
            Assert.False(mesh.TryGetGround(new Vector3(50.0f, 0.0f, 50.0f), 0.75f, 60.0f, out _));
            Assert.False(new ZoneCollisionMesh(Array.Empty<CollisionTriangle>()).TryGetGround(Vector3.Zero, 1, 1, out _));
        }

        [Fact]
        public void Decode_PlacesMeshByTransformAndReadsMaterialBits()
        {
            byte[] payload = BuildCollisionPayload(translation: new Vector3(10.0f, 2.0f, -4.0f));

            var triangles = ZoneCollisionDecoder.DecodeTriangles(payload);

            Assert.NotNull(triangles);
            var t = Assert.Single(triangles!);
            Assert.Equal(new Vector3(10.0f, 2.0f, -4.0f), t.A);
            Assert.Equal(new Vector3(10.0f, 2.0f, -2.0f), t.B);
            Assert.Equal(new Vector3(12.0f, 2.0f, -4.0f), t.C);
            Assert.Equal(-1.0f, t.Normal.Y, 4);
            Assert.True(t.IsWall);
            Assert.Equal(CollisionTerrain.Grass, t.Terrain);
            Assert.True(t.CameraTransparent);

            var mesh = ZoneCollisionDecoder.Decode(payload);
            Assert.True(mesh!.TryGetGround(new Vector3(10.5f, 1.0f, -3.5f), 0.75f, 60.0f, out var hit));
            Assert.Equal(2.0f, hit.Height, 3);
        }

        [Fact]
        public void Decode_NoCollisionBlockReturnsNull()
        {
            Assert.Null(ZoneCollisionDecoder.Decode(new byte[0x40]));
            Assert.Null(ZoneCollisionDecoder.Decode(ReadOnlySpan<byte>.Empty));
        }

        [Fact]
        public void Locomotion_WalkingUpARampFollowsTheGround()
        {
            var world = new WorldState();
            var player = new LocalPlayerState { ServerId = 0x1001 };
            var localEnt = new PlayerEntity(player.ServerId, 1) { Position = new Vector3(0.0f, -3.0f, 0.0f), IsSpawned = true };
            world.UpsertEntity(localEnt);

            // Ramp rising 1 yalm per 4 along +X (internal height = -x / 4).
            var up = Vector3.Normalize(new Vector3(0.25f, -1.0f, 0.0f));
            world.Collision = new ZoneCollisionMesh(new List<CollisionTriangle>
            {
                new(new(-2, 0.5f, -5), new(20, -5.0f, -5), new(20, -5.0f, 5), up, false, CollisionTerrain.Grass, false),
                new(new(-2, 0.5f, -5), new(20, -5.0f, 5), new(-2, 0.5f, 5), up, false, CollisionTerrain.Grass, false),
            });

            var input = new InputState();
            var controller = new PlayerLocomotionController(input, InputProfile.CreateCompact(), world, player);

            // Standing still holds a height placed in mid-air (like /moveto); the first step drops onto the ramp.
            controller.Update(TimeSpan.FromSeconds(1.0 / 60.0));
            Assert.Equal(-3.0f, localEnt.Position.Y, 3);

            input.SetKeyDown(GordianKey.W);
            for (int i = 0; i < 60; i++) controller.Update(TimeSpan.FromSeconds(1.0 / 60.0));

            Assert.InRange(localEnt.Position.X, 4.9f, 5.1f);
            Assert.Equal(-localEnt.Position.X / 4.0f, localEnt.Position.Y, 2);

            // Disabled, the height stays put.
            controller.Collision.Requested &= ~CollisionLayers.Ground;
            float y = localEnt.Position.Y;
            for (int i = 0; i < 30; i++) controller.Update(TimeSpan.FromSeconds(1.0 / 60.0));
            Assert.Equal(y, localEnt.Position.Y);
        }

        /// <summary>
        /// A ZoneDef payload holding one collision mesh (one triangle) placed once by a translation transform.
        /// </summary>
        private static byte[] BuildCollisionPayload(Vector3 translation)
        {
            const int Block = 0x40, Transform = 0x80, Mesh = 0x140, Positions = 0x150, Normals = 0x174, Indices = 0x180,
                Groups = 0x190;
            var payload = new byte[0x1A0];
            void U32(int at, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(at, 4), v);
            void U16(int at, ushort v) => BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(at, 2), v);
            void F32(int at, float v) => BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(at, 4), v);
            void Vec(int at, Vector3 v) { F32(at, v.X); F32(at + 4, v.Y); F32(at + 8, v.Z); }
            void Matrix(int at, Matrix4x4 m)
            {
                float[] f = { m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24,
                              m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44 };
                for (int i = 0; i < 16; i++) F32(at + (i * 4), f[i]);
            }

            U32(0x08, Block);
            U32(Block + 0x00, 1);      // mesh count
            U32(Block + 0x04, Mesh);   // first mesh
            U32(Block + 0x08, 1);      // group count
            U32(Block + 0x0C, Groups);

            var toWorld = Matrix4x4.CreateTranslation(translation);
            Matrix4x4.Invert(toWorld, out var toLocal);
            Matrix(Transform, toWorld);
            Matrix(Transform + 0x40, toLocal);

            U32(Mesh + 0x00, Positions);
            U32(Mesh + 0x04, Normals);
            U32(Mesh + 0x08, Indices);
            U16(Mesh + 0x0C, 1);
            U16(Mesh + 0x0E, 1); // flagged mesh: wall triangles are camera-transparent

            Vec(Positions, Vector3.Zero);
            Vec(Positions + 12, new Vector3(0, 0, 2));
            Vec(Positions + 24, new Vector3(2, 0, 0));
            Vec(Normals, Up);

            // Vertex indices 0, 1, 2 and normal 0; wall bit (third word 0x4000) and terrain 2 (second word bit 15).
            U16(Indices + 0, 0x0000);
            U16(Indices + 2, 0x8001);
            U16(Indices + 4, 0x4002);
            U16(Indices + 6, 0x0000);

            U32(Groups, 1);                // one pair
            U32(Groups + 4, Transform);
            U32(Groups + 8, Mesh);
            U32(Groups + 12, 0);           // terminator
            return payload;
        }
    }
}
