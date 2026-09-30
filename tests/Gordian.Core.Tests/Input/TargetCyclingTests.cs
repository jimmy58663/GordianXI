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

        private static uint Pick(uint current, TargetCycleMode mode) => TargetCycling.Pick(Scene, Self, current, mode);

        [Theory]
        [InlineData(TargetCycleMode.TabRight)]
        [InlineData(TargetCycleMode.TabLeft)]
        [InlineData(TargetCycleMode.Closest)]
        public void Pick_TabOrConfirm_NothingTargeted_TakesClosestButNotSelf(TargetCycleMode mode)
        {
            Assert.Equal(0xAu, Pick(0, mode));
        }

        [Fact]
        public void Pick_Tab_StepsRightSkippingSelf_AndWrapsToLeftMost()
        {
            Assert.Equal(0xBu, Pick(0xA, TargetCycleMode.TabRight));
            Assert.Equal(0xCu, Pick(0xB, TargetCycleMode.TabRight));
            Assert.Equal(0xAu, Pick(0xC, TargetCycleMode.TabRight));
        }

        [Fact]
        public void Pick_ShiftTab_StepsLeft_AndWrapsToRightMost()
        {
            Assert.Equal(0xBu, Pick(0xC, TargetCycleMode.TabLeft));
            Assert.Equal(0xCu, Pick(0xA, TargetCycleMode.TabLeft));
        }

        [Fact]
        public void Pick_Tab_OnSelf_TakesClosest()
        {
            Assert.Equal(0xAu, Pick(Self, TargetCycleMode.TabRight));
        }

        [Fact]
        public void Pick_Tab_NothingButSelf_PicksNothing()
        {
            Assert.Equal(0u, TargetCycling.Pick([new Candidate(Self, 0.0f, 0, true)], Self, 0, TargetCycleMode.TabRight));
        }

        [Theory]
        [InlineData(TargetCycleMode.CursorRight)]
        [InlineData(TargetCycleMode.CursorLeft)]
        public void Pick_DPad_NothingTargeted_TargetsSelf(TargetCycleMode mode)
        {
            Assert.Equal(Self, Pick(0, mode));
            Assert.Equal(Self, Pick(0x77, mode)); // an off-screen target counts as nothing targeted
        }

        [Fact]
        public void Pick_DPadRight_StepsRightFromSelf_AndReturnsToSelfPastTheEdge()
        {
            Assert.Equal(0xCu, Pick(Self, TargetCycleMode.CursorRight));
            Assert.Equal(Self, Pick(0xC, TargetCycleMode.CursorRight));
            Assert.Equal(Self, Pick(0xB, TargetCycleMode.CursorRight));
        }

        [Fact]
        public void Pick_DPadLeft_StepsLeftFromSelf_AndReturnsToSelfPastTheEdge()
        {
            Assert.Equal(0xBu, Pick(Self, TargetCycleMode.CursorLeft));
            Assert.Equal(0xAu, Pick(0xB, TargetCycleMode.CursorLeft));
            Assert.Equal(Self, Pick(0xA, TargetCycleMode.CursorLeft));
        }

        [Fact]
        public void Pick_DPad_SelfOffScreen_SitsAtScreenCentre()
        {
            var noSelf = Scene.FindAll(c => !c.IsSelf);
            Assert.Equal(0xCu, TargetCycling.Pick(noSelf, Self, Self, TargetCycleMode.CursorRight));
            Assert.Equal(0xBu, TargetCycling.Pick(noSelf, Self, Self, TargetCycleMode.CursorLeft));
        }

        [Fact]
        public void Pick_SameScreenPosition_OrdersNearestFirst()
        {
            var stacked = new List<Candidate> { new(0x2, 0.3f, 400, false), new(0x3, 0.3f, 100, false), new(0x4, -0.3f, 50, false) };
            Assert.Equal(0x3u, TargetCycling.Pick(stacked, Self, 0x4, TargetCycleMode.TabRight));
            Assert.Equal(0x2u, TargetCycling.Pick(stacked, Self, 0x3, TargetCycleMode.TabRight));
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
