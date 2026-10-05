using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using Gordian.Core.Graphics;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Models;
using Gordian.Core.World;
using Gordian.Core.World.Collision;

namespace Gordian.Core.Tests.World.Collision
{
    /// <summary>Zone doors (#15): decoders, the door animator, closed-door blocking and the Metalworks data.</summary>
    public class ZoneDoorTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
        private readonly ITestOutputHelper _output;

        public ZoneDoorTests(ITestOutputHelper output) => _output = output;

        // Metalworks' _6l0 guild door: a 2.01 x 2.83 x 0.07 doorway turned 270 degrees (the leaves span world Z).
        private static readonly DoorBlocker GuildDoor = new("_6l0", new Vector3(-96.27f, 0.59f, -18.39f), 4.712389f, new Vector3(2.01f, 2.83f, 0.07f));

        private const float Radius = 0.53f, StepUp = 0.5f, BodyHeight = 1.6f;

        private static WorldState WorldWithDoor(byte status, byte eventStatus = 0)
        {
            var world = new WorldState { Collision = new ZoneCollisionMesh(Array.Empty<CollisionTriangle>()) { Doors = new[] { GuildDoor } } };
            world.UpsertEntity(new WorldEntity(0x0100F000, 0x100, EntityType.Door)
            {
                TransportId = "_6l0",
                AnimationState = status,
                EventStatus = eventStatus,
                IsSpawned = true,
            });
            return world;
        }

        [Fact]
        public void ZoneInteractionDecoder_ReadsTheRidEntries()
        {
            byte[] payload = new byte[0x20 + 16 + (2 * 0x40)];
            Encoding.ASCII.GetBytes("RID").CopyTo(payload, 0);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0x10), 0x20);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0x20), 2);
            void Entry(int index, string id, Vector3 center, float ry, Vector3 size)
            {
                var e = payload.AsSpan(0x30 + (index * 0x40), 0x40);
                BinaryPrimitives.WriteSingleLittleEndian(e, center.X);
                BinaryPrimitives.WriteSingleLittleEndian(e.Slice(4), center.Y);
                BinaryPrimitives.WriteSingleLittleEndian(e.Slice(8), center.Z);
                BinaryPrimitives.WriteSingleLittleEndian(e.Slice(0x10), ry);
                BinaryPrimitives.WriteSingleLittleEndian(e.Slice(0x18), size.X);
                BinaryPrimitives.WriteSingleLittleEndian(e.Slice(0x1C), size.Y);
                BinaryPrimitives.WriteSingleLittleEndian(e.Slice(0x20), size.Z);
                Encoding.ASCII.GetBytes(id).CopyTo(e.Slice(0x24));
            }
            Entry(0, "_6l0", new Vector3(-96.27f, 0.59f, -18.39f), 4.712f, new Vector3(2.01f, 2.83f, 0.07f));
            Entry(1, "@6l0", new Vector3(-56f, -13.1f, -12f), 0f, new Vector3(6f, 20f, 6f));
            BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(0x30 + 0x40 + 0x34), 3856); // the lift's two stops
            BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(0x30 + 0x40 + 0x36), 798);

            var records = ZoneInteractionDecoder.Decode(payload);
            Assert.Equal(2, records.Count);
            Assert.True(records[0].IsDoor);
            Assert.False(records[1].IsDoor);
            Assert.True(records[1].IsLift);
            Assert.False(records[0].TryGetLiftFloors(out _, out _));
            Assert.True(records[1].TryGetLiftFloors(out float floor0, out float floor1));
            Assert.Equal(1.9625f, floor0, 3);
            Assert.Equal(-9.983f, floor1, 3);
            Assert.Equal(new Vector3(2.01f, 2.83f, 0.07f), records[0].Size);
            var blocker = Assert.Single(ZoneDoors.CreateBlockers(records));
            Assert.Equal("_6l0", blocker.Id);

            Assert.Empty(ZoneInteractionDecoder.Decode(payload.AsSpan(0, 0x18))); // short
            payload[0] = (byte)'X';
            Assert.Empty(ZoneInteractionDecoder.Decode(payload)); // no magic
        }

        /// <summary>A one-entry RID payload.</summary>
        private static byte[] RidPayload(string id, short floor0 = 0, short floor1 = 0)
        {
            byte[] payload = new byte[0x20 + 16 + 0x40];
            Encoding.ASCII.GetBytes("RID").CopyTo(payload, 0);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0x10), 0x20);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0x20), 1);
            Encoding.ASCII.GetBytes(id).CopyTo(payload.AsSpan(0x30 + 0x24));
            BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(0x30 + 0x34), floor0);
            BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(0x30 + 0x36), floor1);
            return payload;
        }

        /// <summary>A DAT section: a 16-byte header (id, type | 16-byte units &lt;&lt; 7) and the padded payload.</summary>
        private static byte[] Section(string datId, byte type, byte[] payload)
        {
            int size = 16 + ((payload.Length + 15) & ~15);
            byte[] section = new byte[size];
            Encoding.ASCII.GetBytes(datId).CopyTo(section, 0);
            BinaryPrimitives.WriteUInt32LittleEndian(section.AsSpan(4), (uint)type | ((uint)(size / 16) << 7));
            payload.CopyTo(section, 16);
            return section;
        }

        [Fact]
        public void ZoneInteractionDecoder_ReadsLiftsFromEveryTable_DoorsFromTheFirst()
        {
            // Metalworks keeps its doors in the first 0x36 table and its lifts in another (e237); a bad table in between is skipped.
            byte[] dat = Section("t_ba", 0x36, RidPayload("_6l0"))
                .Concat(Section("bad", 0x36, new byte[] { (byte)'X', 0, 0, 0 }))
                .Concat(Section("e237", 0x36, RidPayload("@6l0", 3856, 798)))
                .ToArray();

            Assert.Equal("_6l0", Assert.Single(ZoneInteractionDecoder.DecodeFromDat(dat)).Id);
            var all = ZoneInteractionDecoder.DecodeAllFromDat(dat);
            Assert.Equal(new[] { "_6l0", "@6l0" }, all.Select(r => r.Id));
            Assert.Equal((short)3856, all[1].LiftFloor0);
            Assert.Equal((short)798, all[1].LiftFloor1);

            Assert.Empty(ZoneInteractionDecoder.DecodeAllFromDat(ReadOnlySpan<byte>.Empty));
            Assert.Empty(ZoneInteractionDecoder.DecodeAllFromDat(new byte[] { 1, 2, 3 }));
        }

        [Fact]
        public void DoorRoutineDecoder_ReadsRotateAndSlideMoves()
        {
            // Modelled on Metalworks _6l0/open: 01 ; 0B sound ; 0D part 0 -80 degrees ; 0D part 1 +80 degrees (70 frames) ; 0C slide.
            var commands = new List<byte>();
            void Command(byte op, int sizeDwords, int delay, int duration, params uint[] body)
            {
                commands.Add(op);
                commands.Add((byte)sizeDwords);
                commands.Add(0);
                commands.Add(0);
                commands.AddRange(BitConverter.GetBytes((ushort)delay));
                commands.AddRange(BitConverter.GetBytes((ushort)duration));
                foreach (uint word in body) commands.AddRange(BitConverter.GetBytes(word));
            }
            uint F(float f) => BitConverter.SingleToUInt32Bits(f);
            Command(0x01, 2, 0, 0);
            Command(0x0B, 8, 0, 0, 0x31323039, 0, 0, F(10), F(3), 0);
            Command(0x0D, 6, 0, 70, 0, F(-1.396f), 0, 0);
            Command(0x0D, 6, 70, 70, 0, F(1.396f), 0, 1);
            Command(0x0C, 6, 10, 20, 0, F(-3.5f), 0, 2);
            Command(0x00, 1, 0, 0);
            byte[] payload = new byte[0x20 + commands.Count];
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0x14), 0x20 + 16);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0x1C), 70);
            commands.CopyTo(payload, 0x20);

            var routine = DoorRoutineDecoder.Decode(payload)!;
            Assert.Equal(80, routine.TotalFrames); // the slide ends 10 frames after the authored 70
            Assert.Equal(3, routine.Moves.Count);
            Assert.Equal(new DoorMove(DoorMoveKind.Rotate, 0, 70, new Vector3(0, -1.396f, 0), 0), routine.Moves[0]);
            Assert.Equal(new DoorMove(DoorMoveKind.Rotate, 0, 70, new Vector3(0, 1.396f, 0), 1), routine.Moves[1]);
            Assert.Equal(new DoorMove(DoorMoveKind.Slide, 70, 20, new Vector3(0, -3.5f, 0), 2), routine.Moves[2]);
        }

        private static ZoneDoorRoutines GuildRoutines() => new()
        {
            Open = new DoorRoutine
            {
                TotalFrames = 70,
                Moves = new[]
                {
                    new DoorMove(DoorMoveKind.Rotate, 0, 70, new Vector3(0, -1.4f, 0), 0),
                    new DoorMove(DoorMoveKind.Rotate, 0, 70, new Vector3(0, 1.4f, 0), 1),
                },
            },
            Close = new DoorRoutine
            {
                TotalFrames = 70,
                Moves = new[]
                {
                    new DoorMove(DoorMoveKind.Rotate, 0, 70, Vector3.Zero, 0),
                    new DoorMove(DoorMoveKind.Rotate, 0, 70, Vector3.Zero, 1),
                },
            },
        };

        [Fact]
        public void DoorAnimator_PlaysOpenAndClose_FromWhereTheLeavesAre()
        {
            var animator = new DoorAnimator(new Dictionary<string, ZoneDoorRoutines> { ["_6l0"] = GuildRoutines() });
            animator.SetOpen("_6l0", false, 0.0); // first sight: closed, stays shut
            Assert.Equal(DoorLeafPose.Shut, animator.GetPose("_6l0", 0, 0.5));

            animator.SetOpen("_6l0", true, 1.0);
            Assert.Equal(-0.7f, animator.GetPose("_6l0", 0, 1.0 + (35 / 60.0)).Rotation.Y, 3); // half way
            Assert.Equal(1.4f, animator.GetPose("_6l0", 1, 3.0).Rotation.Y, 3);
            Assert.True(animator.IsMoving("_6l0", 1.5));
            Assert.False(animator.IsMoving("_6l0", 3.0));

            // Closed again half way through the close, then reopened: it opens from the half-closed pose.
            animator.SetOpen("_6l0", false, 4.0);
            animator.SetOpen("_6l0", true, 4.0 + (35 / 60.0));
            Assert.Equal(-0.7f, animator.GetPose("_6l0", 0, 4.0 + (35 / 60.0)).Rotation.Y, 3);
            Assert.Equal(-1.4f, animator.GetPose("_6l0", 0, 10.0).Rotation.Y, 3);
        }

        [Fact]
        public void DoorAnimator_StartsAtIntc_AndTakesIntoWhenFirstSeenOpen()
        {
            var routines = GuildRoutines();
            routines.InitClose = new DoorRoutine { TotalFrames = 2, Moves = new[] { new DoorMove(DoorMoveKind.Slide, 0, 2, new Vector3(0, 0.25f, 0), 0) } };
            routines.InitOpen = new DoorRoutine { TotalFrames = 2, Moves = new[] { new DoorMove(DoorMoveKind.Rotate, 0, 2, new Vector3(0, -1.4f, 0), 0) } };
            var animator = new DoorAnimator(new Dictionary<string, ZoneDoorRoutines> { ["_6l0"] = routines });
            Assert.Equal(new Vector3(0, 0.25f, 0), animator.GetPose("_6l0", 0, 0.0).Slide);

            animator.SetOpen("_6l0", true, 5.0); // already open when sent: no animation
            Assert.False(animator.IsMoving("_6l0", 5.0));
            Assert.Equal(-1.4f, animator.GetPose("_6l0", 0, 5.0).Rotation.Y, 3);
        }

        [Fact]
        public void DoorAnimator_FollowsTheEntity_EventStatusFirst()
        {
            var animator = new DoorAnimator(new Dictionary<string, ZoneDoorRoutines> { ["_6l0"] = GuildRoutines() });
            var world = WorldWithDoor(ZoneDoors.StatusClosed);
            animator.Update(world, 0.0);
            world.TryGetByServerId(0x0100F000, out var door);

            door!.EventStatus = ZoneDoors.StatusOpen; // 0x4C during an event
            animator.Update(world, 1.0);
            Assert.True(animator.IsMoving("_6l0", 1.1));

            door.EventStatus = 0; // the event ended: back to the server's closed status
            animator.Update(world, 3.0);
            Assert.Equal(0.0f, animator.GetPose("_6l0", 0, 10.0).Rotation.Y, 3);
        }

        [Fact]
        public void LeafTransform_TurnsTheLeafAboutItsPlacementOrigin()
        {
            var placement = new ZonePlacement("leaf", new Vector3(10, 0, 5), new Vector3(0, MathF.PI / 2, 0), Vector3.One, 100f, BlockId: "_abc");
            var transform = DoorAnimator.LeafTransform(placement, new DoorLeafPose(new Vector3(0, 0.5f, 0), Vector3.Zero));
            Assert.Equal(placement.Position, Vector3.Transform(placement.Position, transform)); // the hinge stays put
            var tip = Vector3.Transform(new Vector3(1, 0, 0), ZoneDefDecoder.CreateTrsMatrix(placement.Position, placement.Rotation, placement.Scale));
            var moved = Vector3.Transform(tip, transform);
            Assert.Equal(1.0f, Vector3.Distance(moved, placement.Position), 3);
            Assert.NotEqual(tip, moved);
            Assert.Equal(Matrix4x4.Identity, DoorAnimator.LeafTransform(placement, DoorLeafPose.Shut));
        }

        [Fact]
        public void ClosedDoor_StopsTheBodyAtItsFace_AndLetsItSlide()
        {
            var closed = new[] { GuildDoor };
            float floor = 1.96f; // the doorway's floor (internal +Y down)
            var start = new Vector3(-98.0f, floor, -18.39f);

            // Straight through: stopped a radius (plus half the door) short of the doorway's centre plane.
            var end = ZoneDoors.Resolve(closed, start, new Vector3(-94.5f, floor, -18.39f), Radius, StepUp, BodyHeight);
            Assert.InRange(end.X, -96.27f - 0.035f - Radius - 0.01f, -96.27f - 0.035f - Radius + 0.01f);

            // At an angle: the move along the door is kept.
            end = ZoneDoors.Resolve(closed, start, new Vector3(-94.5f, floor, -17.89f), Radius, StepUp, BodyHeight);
            Assert.Equal(-17.89f, end.Z, 3);
            Assert.True(end.X < -96.27f - Radius);

            // Beside the doorway, a body far below (another floor), and a body caught inside are not stopped.
            Assert.Equal(-94.5f, ZoneDoors.Resolve(closed, new Vector3(-98.0f, floor, -21.0f), new Vector3(-94.5f, floor, -21.0f), Radius, StepUp, BodyHeight).X, 3);
            Assert.Equal(-94.5f, ZoneDoors.Resolve(closed, new Vector3(-98.0f, floor + 12.0f, -18.39f), new Vector3(-94.5f, floor + 12.0f, -18.39f), Radius, StepUp, BodyHeight).X, 3);
            Assert.Equal(-95.0f, ZoneDoors.Resolve(closed, new Vector3(-96.27f, floor, -18.39f), new Vector3(-95.0f, floor, -18.39f), Radius, StepUp, BodyHeight).X, 3);
        }

        [Fact]
        public void EvaluateClosed_FollowsTheDoorsStatus()
        {
            Assert.Single(ZoneDoors.EvaluateClosed(WorldWithDoor(ZoneDoors.StatusClosed).Collision, WorldWithDoor(ZoneDoors.StatusClosed)));
            var open = WorldWithDoor(ZoneDoors.StatusOpen);
            Assert.Empty(ZoneDoors.EvaluateClosed(open.Collision, open));
            var eventOpen = WorldWithDoor(ZoneDoors.StatusClosed, ZoneDoors.StatusOpen); // 0x4C opened it for the event
            Assert.Empty(ZoneDoors.EvaluateClosed(eventOpen.Collision, eventOpen));
            var eventClosed = WorldWithDoor(ZoneDoors.StatusOpen, ZoneDoors.StatusClosed);
            Assert.Single(ZoneDoors.EvaluateClosed(eventClosed.Collision, eventClosed));

            // No door entity (never sent, or out of range): the doorway does not block.
            var empty = new WorldState { Collision = new ZoneCollisionMesh(Array.Empty<CollisionTriangle>()) { Doors = new[] { GuildDoor } } };
            Assert.Empty(ZoneDoors.EvaluateClosed(empty.Collision, empty));
        }

        [Fact]
        public void S2C_0x00E_CharNpc_DoorCarriesItsDoorId()
        {
            byte[] payload = new byte[0x34];
            payload[0x2C] = (byte)EntitySubKind.Door;
            Encoding.ASCII.GetBytes("_6l0").CopyTo(payload.AsSpan(0x30));
            var npc = new S2C_0x00E_CharNpc(payload);
            Assert.True(npc.TryGetDoorObjectId(out string id));
            Assert.Equal("_6l0", id);
            Assert.False(npc.TryGetTransport(out _, out _, out _));

            payload[0x2C] = (byte)EntitySubKind.Elevator;
            Assert.False(new S2C_0x00E_CharNpc(payload).TryGetDoorObjectId(out _));
        }

        [Fact]
        public void Doors_AreTargetable_TransportsAreNot()
        {
            // LSB names its doors ("Door:House"); retail targets them like NPCs, the cursor at the door's middle.
            Assert.True(Gordian.Core.Input.TargetCycling.IsTargetable(new WorldEntity(1, 1, EntityType.Door) { Name = "Door:House", IsSpawned = true }));
            Assert.False(Gordian.Core.Input.TargetCycling.IsTargetable(new WorldEntity(2, 2, EntityType.Elevator) { Name = "_6l0", IsSpawned = true }));
        }

        [Fact]
        public void OrderDoorParts_PutsTheLeftLeafFirst()
        {
            static DoorLeaf Leaf(int part, string mesh) => new(part, new ZonePlacement(mesh, Vector3.Zero, Vector3.Zero, Vector3.One, 1f, BlockId: "_6le"), new List<MeshGroup>());
            var leaves = new List<DoorLeaf> { Leaf(0, "plgdoor2r"), Leaf(1, "plgdoor2l") };
            ZoneDataLoader.OrderDoorParts(leaves);
            Assert.Equal(("plgdoor2l", 0), (leaves[0].Placement.MeshId, leaves[0].Part));
            Assert.Equal(("plgdoor2r", 1), (leaves[1].Placement.MeshId, leaves[1].Part));

            var same = new List<DoorLeaf> { Leaf(0, "door01"), Leaf(1, "door01") };
            ZoneDataLoader.OrderDoorParts(same);
            Assert.Equal(0, same[0].Part);
        }

        [Fact]
        public void Metalworks_DoorsLoadFromBothLoaders_AndTheirLeavesSwingClear()
        {
            if (!Directory.Exists(GameDirectory)) return;

            var collisionOnly = new ResourceManager(GameDirectory);
            collisionOnly.InitializeFileTable();
            var headless = collisionOnly.TryLoadZoneCollision(237)!;
            var full = new ResourceManager(GameDirectory);
            full.InitializeFileTable();
            Assert.True(full.TryLoadZone(237, out var zone, out _));

            // 19 doorways (_6l0-_6li), each with its leaves and its open / close routines.
            Assert.Equal(19, headless.Doors.Count);
            Assert.Equal(19, zone!.Collision!.Doors.Count);
            var guild = zone.Collision.Doors.Single(d => d.Id == "_6l0");
            Assert.Equal(2.01f, guild.Size.X, 2);
            Assert.Equal(2, zone.DoorLeaves["_6l0"].Count);
            Assert.Equal(70, zone.DoorRoutines["_6l0"].Open!.TotalFrames);
            Assert.DoesNotContain(zone.MeshGroups, g => g.Name.StartsWith("guilddoor", StringComparison.OrdinalIgnoreCase));

            // The collision soup has no leaves: a closed doorway is open in it, and blocks only through ZoneDoors.
            var through = new Vector3(-98.0f, 1.96f, -18.39f);
            Assert.False(zone.Collision.TryRaycast(new Vector3(-98.0f, 0.59f, -18.39f), new Vector3(-94.5f, 0.59f, -18.39f), false, out _));
            Assert.True(ZoneDoors.Resolve(new[] { guild }, through, through with { X = -94.5f }, Radius, StepUp, BodyHeight).X < -96.27f);

            // Opened, every leaf leaves the doorway's plane, both leaves of a door to the same side.
            var animator = new DoorAnimator(zone.DoorRoutines);
            foreach (var door in zone.Collision.Doors)
            {
                if (!zone.DoorLeaves.TryGetValue(door.Id, out var leaves) || leaves.Count != 2) continue;
                animator.SetOpen(door.Id, false, 0.0);
                animator.SetOpen(door.Id, true, 0.0);
                var sides = new List<float>();
                foreach (var leaf in leaves)
                {
                    var vertices = leaf.Submeshes.SelectMany(m => m.Vertices).Select(v => new Vector3(-v.Position.X, -v.Position.Y, v.Position.Z)).ToList();
                    var centre = vertices.Aggregate(Vector3.Zero, (a, b) => a + b) / vertices.Count;
                    var opened = Vector3.Transform(centre, DoorAnimator.LeafTransform(leaf.Placement, animator.GetPose(door.Id, leaf.Part, 10.0)));
                    float depth = Vector2.Dot(new Vector2(opened.X - door.Center.X, opened.Z - door.Center.Z), door.DepthAxis);
                    _output.WriteLine($"{door.Id} {leaf.Placement.MeshId}#{leaf.Part}: depth {depth:F2}");
                    Assert.True(MathF.Abs(depth) > 0.3f);
                    sides.Add(MathF.Sign(depth));
                }
                Assert.Equal(sides[0], sides[1]);
            }
        }
    }
}
