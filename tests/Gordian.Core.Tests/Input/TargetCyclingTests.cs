// tests/Gordian.Core.Tests/Input/TargetCyclingTests.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using Gordian.Core.Graphics;
using Gordian.Core.Input;
using Gordian.Core.World;
using Xunit;
using Candidate = Gordian.Core.Input.TargetCycling.Candidate;

namespace Gordian.Core.Tests.Input
{
    public sealed class TargetCyclingTests
    {
        private const uint Self = 1;

        // A (near, left), B (far, centre-left), C (mid, right), the player just below the centre.
        private static readonly List<Candidate> Scene =
        [
            new Candidate(0xA, -0.6f, 4, false),
            new Candidate(0xB, -0.05f, 900, false),
            new Candidate(0xC, 0.5f, 100, false),
            new Candidate(Self, 0.0f, 0, true),
        ];

        [Fact]
        public void Pick_NothingTargeted_TakesNearestOnPressedSide()
        {
            Assert.Equal(0xCu, TargetCycling.Pick(Scene, 0, +1));
            Assert.Equal(0xAu, TargetCycling.Pick(Scene, 0, -1));
        }

        [Fact]
        public void Pick_Confirm_TakesNearestOnEitherSideButNotSelf()
        {
            Assert.Equal(0xAu, TargetCycling.Pick(Scene, 0, 0));
        }

        [Fact]
        public void Pick_NothingOnPressedSide_TakesNearestOnOtherSide()
        {
            var leftOnly = new List<Candidate> { new(0xA, -0.6f, 400, false), new(0xB, -0.2f, 100, false), new(Self, 0.0f, 0, true) };
            Assert.Equal(0xBu, TargetCycling.Pick(leftOnly, 0, +1));
        }

        [Fact]
        public void Pick_OnlySelfOnScreen_TargetsSelf()
        {
            Assert.Equal(Self, TargetCycling.Pick([new Candidate(Self, 0.0f, 0, true)], 0, +1));
        }

        [Fact]
        public void Pick_Empty_ReturnsNone()
        {
            Assert.Equal(0u, TargetCycling.Pick([], 0, +1));
        }

        [Fact]
        public void Pick_Right_StepsAcrossTheScreenVisitingEveryoneIncludingSelf_AndWraps()
        {
            var visited = new List<uint>();
            uint current = 0xA;
            for (int i = 0; i < 4; i++)
            {
                current = TargetCycling.Pick(Scene, current, +1);
                visited.Add(current);
            }
            Assert.Equal([0xBu, Self, 0xCu, 0xAu], visited);
        }

        [Fact]
        public void Pick_Left_StepsBackAndWraps()
        {
            Assert.Equal(0xCu, TargetCycling.Pick(Scene, 0xA, -1));
            Assert.Equal(Self, TargetCycling.Pick(Scene, 0xC, -1));
        }

        [Fact]
        public void Pick_SameScreenPosition_OrdersNearestFirst()
        {
            var stacked = new List<Candidate> { new(0x2, 0.3f, 400, false), new(0x1, 0.3f, 100, false), new(0x3, -0.3f, 50, false) };
            Assert.Equal(0x1u, TargetCycling.Pick(stacked, 0x3, +1));
            Assert.Equal(0x2u, TargetCycling.Pick(stacked, 0x1, +1));
        }

        [Fact]
        public void Pick_TargetOffScreen_ActsAsNothingTargeted()
        {
            Assert.Equal(0xCu, TargetCycling.Pick(Scene, 0x77, +1));
        }

        private static ViewportCamera CameraAt(Vector3 player)
        {
            var camera = new ViewportCamera { AspectRatio = 16.0f / 9.0f };
            camera.Update(player, 10.0f, 30.0f, 6.0f, camera.AspectRatio);
            return camera;
        }

        private static WorldEntity Mob(uint id, Vector3 position) =>
            new(id, (ushort)id, EntityType.Monster) { Name = $"Mob{id}", Position = position, IsSpawned = true };

        [Fact]
        public void Gather_KeepsOnScreenTargetables_WithScreenSide()
        {
            var player = new Vector3(100, 0, 50);
            var camera = CameraAt(player);
            var flatForward = Vector3.Normalize(camera.Forward with { Y = 0 });
            var right = camera.Right;
            var self = new PlayerEntity(Self, 1) { Name = "Me", Position = player, IsSpawned = true };
            var entities = new List<WorldEntity>
            {
                self,
                Mob(0x10, player + flatForward * 10 + right * 3),   // ahead, right
                Mob(0x11, player + flatForward * 10 - right * 3),   // ahead, left
                Mob(0x12, player - flatForward * 20),               // behind the camera
                Mob(0x13, player + right * 60),                     // off the right edge and out of range
                Mob(0x14, player + flatForward * 10),               // hidden
                new WorldEntity(0x15, 0x15, EntityType.Elevator) { Name = "Lift", Position = player + flatForward * 5, IsSpawned = true },
            };
            entities[5].IsHidden = true;

            var candidates = TargetCycling.Gather(entities, Self, player, camera);

            Assert.Equal([Self, 0x10u, 0x11u], candidates.ConvertAll(c => c.ServerId));
            Assert.True(candidates[1].ScreenX > 0);
            Assert.True(candidates[2].ScreenX < 0);
            Assert.True(candidates[0].IsSelf);
        }
    }
}
