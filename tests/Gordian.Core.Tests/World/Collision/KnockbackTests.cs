// tests/Gordian.Core.Tests/World/Collision/KnockbackTests.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Gordian.Core.Config;
using Gordian.Core.Input;
using Gordian.Core.World;
using Gordian.Core.World.Collision;
using Xunit;

namespace Gordian.Core.Tests.World.Collision
{
    public class KnockbackTests
    {
        private static readonly Vector3 Up = new(0.0f, -1.0f, 0.0f);

        private static ZoneCollisionMesh Room(float wallX)
        {
            var triangles = new List<CollisionTriangle>
            {
                new(new(-20, 0, -20), new(20, 0, -20), new(20, 0, 20), Up, false, CollisionTerrain.Stone, false),
                new(new(-20, 0, -20), new(20, 0, 20), new(-20, 0, 20), Up, false, CollisionTerrain.Stone, false),
            };
            var normal = new Vector3(-1, 0, 0);
            triangles.Add(new(new(wallX, 0, -20), new(wallX, -4, -20), new(wallX, -4, 20), normal, true, CollisionTerrain.Stone, false));
            triangles.Add(new(new(wallX, 0, -20), new(wallX, -4, 20), new(wallX, 0, 20), normal, true, CollisionTerrain.Stone, false));
            return new ZoneCollisionMesh(triangles);
        }

        private static (PlayerLocomotionController Controller, PlayerEntity Self) Harness(ZoneCollisionMesh? collision = null)
        {
            var world = new WorldState { Collision = collision };
            var player = new LocalPlayerState { ServerId = 0x1001 };
            var self = new PlayerEntity(player.ServerId, 1) { Position = Vector3.Zero, IsSpawned = true };
            world.UpsertEntity(self);
            var controller = new PlayerLocomotionController(new InputState(), InputProfile.CreateCompact(), world, player);
            return (controller, self);
        }

        private static void Run(PlayerLocomotionController controller, int ticks)
        {
            for (int i = 0; i < ticks; i++) controller.Update(TimeSpan.FromSeconds(1.0 / 60.0));
        }

        [Theory]
        [InlineData(1, 0.3f, 0.45f)]
        [InlineData(4, 0.95f, 1.15f)]
        [InlineData(7, 2.8f, 3.2f)]
        public void Knockback_SlidesThePlayerByTheLevelsDistance(int level, float min, float max)
        {
            var (controller, self) = Harness();

            controller.ApplyKnockback(new Vector2(1f, 0f), level);
            Run(controller, 60);

            Assert.InRange(self.Position.X, min, max);
            Assert.InRange(self.Position.X, KnockbackSettings.ProfileOf(level).Distance - 0.01f, KnockbackSettings.ProfileOf(level).Distance + 0.01f);
            Assert.Equal(0f, self.Position.Z, 3);
            Assert.False(controller.IsKnockedBack);
        }

        [Fact]
        public void Knockback_StopsAtAWall()
        {
            var (controller, self) = Harness(Room(1.5f));

            controller.ApplyKnockback(new Vector2(1f, 0f), 7);
            Run(controller, 60);

            float stop = 1.5f - PlayerLocomotionController.BodyRadius;
            Assert.InRange(self.Position.X, stop - 0.05f, stop + 0.01f);
        }

        [Fact]
        public void Knockback_LevelZeroOrNoDirection_DoesNothing()
        {
            var (controller, self) = Harness();

            controller.ApplyKnockback(new Vector2(1f, 0f), 0);
            controller.ApplyKnockback(Vector2.Zero, 5);
            Run(controller, 30);

            Assert.Equal(Vector3.Zero, self.Position);
        }

        [Fact]
        public void Anchor_IgnoresKnockbackUnlessTheServerForbidsIt()
        {
            var settings = new KnockbackSettings();
            var profile = new SessionProfile();
            Assert.False(settings.IsAnchored(profile)); // off by default

            settings.AnchorRequested = true;
            Assert.True(settings.IsAnchored(profile));

            profile.FeatureRestrictions = FeatureRestrictions.KnockbackOverride;
            Assert.False(settings.IsAnchored(profile));
            Assert.True(KnockbackSettings.IsAnchorLocked(profile));
        }

        [Fact]
        public void ProfileOf_OutOfRangeLevelsHaveNoSlide()
        {
            Assert.Equal(0f, KnockbackSettings.ProfileOf(0).Distance);
            Assert.Equal(0f, KnockbackSettings.ProfileOf(8).Distance);
            Assert.True(Enumerable.Range(1, 6).All(l => KnockbackSettings.ProfileOf(l + 1).Distance >= KnockbackSettings.ProfileOf(l).Distance));
        }
    }
}
