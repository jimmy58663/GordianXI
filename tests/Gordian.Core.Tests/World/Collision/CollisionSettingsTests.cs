using System.Numerics;
using Gordian.Core.Actions;
using Gordian.Core.Config;
using Gordian.Core.Graphics;
using Gordian.Core.Network;
using Gordian.Core.Network.Packets;
using Gordian.Core.World;
using Gordian.Core.World.Collision;

namespace Gordian.Core.Tests.World.Collision
{
    public class CollisionSettingsTests
    {
        private static PlayerActionService CreateService(SessionProfile profile)
        {
            var world = new WorldState();
            var localPlayer = new LocalPlayerState { ServerId = 0x01020304 };
            Task Send(ReadOnlyMemory<byte> chunk, bool urgent) => Task.CompletedTask;
            return new PlayerActionService(profile, world, localPlayer,
                new CombatPacketModule(new CombatState(), localPlayer, Send), new ChatPacketModule(Send),
                new PartyPacketModule(new PartyState(), Send), new EntityPacketModule(world, localPlayer, Send),
                new LifecyclePacketModule(profile, Send), Send);
        }

        [Fact]
        public void ServerRestrictionsKeepProtectedLayersOn()
        {
            var settings = new CollisionSettings { Requested = CollisionLayers.None };
            var profile = new SessionProfile();
            Assert.Equal(CollisionLayers.None, settings.GetEffective(profile));

            profile.FeatureRestrictions = FeatureRestrictions.WallCollisionOverride;
            Assert.Equal(CollisionLayers.Ground | CollisionLayers.Walls, settings.GetEffective(profile));

            profile.FeatureRestrictions = FeatureRestrictions.EntityCollisionOverride;
            Assert.Equal(CollisionLayers.Entities, settings.GetEffective(profile));
        }

        [Fact]
        public void CollisionCommand_TogglesLayersAndReportsState()
        {
            var service = CreateService(new SessionProfile());

            var status = service.ApplyCollisionCommand("");
            Assert.Contains("ground on, walls on, entities on", status.Message);

            service.ApplyCollisionCommand("entities");
            Assert.Equal(CollisionLayers.Ground | CollisionLayers.Walls, service.Collision.Requested);

            service.ApplyCollisionCommand("walls off");
            Assert.Equal(CollisionLayers.Ground, service.Collision.Requested);

            service.ApplyCollisionCommand("on");
            Assert.Equal(CollisionLayers.All, service.Collision.Requested);

            Assert.False(service.ApplyCollisionCommand("sideways").Success);
        }

        [Fact]
        public void CollisionCommand_ReportsServerEnforcedLayers()
        {
            var profile = new SessionProfile { FeatureRestrictions = FeatureRestrictions.WallCollisionOverride };
            var service = CreateService(profile);

            var result = service.ApplyCollisionCommand("walls off");

            Assert.Contains("walls on (server-enforced)", result.Message);
            Assert.Equal(CollisionLayers.Ground | CollisionLayers.Walls, service.Collision.GetEffective(profile) & ~CollisionLayers.Entities);
        }

        [Fact]
        public void CameraFollowHeight_EasesStepsAndSnapsTeleports()
        {
            var camera = new ViewportCamera();
            camera.Update(Vector3.Zero, 15, 0, 6, 1.6f, 1.0f / 60.0f);
            float startTarget = camera.Target.Y;

            // A 0.5-yalm step up: the view rises only part of the way in one frame, and nearly all of it in half a second.
            camera.Update(new Vector3(0, 0.5f, 0), 15, 0, 6, 1.6f, 1.0f / 60.0f);
            float afterFrame = camera.Target.Y - startTarget;
            Assert.InRange(afterFrame, 0.01f, 0.1f);
            for (int i = 0; i < 30; i++) camera.Update(new Vector3(0, 0.5f, 0), 15, 0, 6, 1.6f, 1.0f / 60.0f);
            Assert.InRange(camera.Target.Y - startTarget, 0.47f, 0.5f);

            // A teleport is followed at once.
            camera.Update(new Vector3(0, 40.0f, 0), 15, 0, 6, 1.6f, 1.0f / 60.0f);
            Assert.Equal(40.0f + camera.EyeOffset.Y, camera.Target.Y, 3);
        }

        [Fact]
        public void TryGetNearestGround_PicksTheLevelClosestToTheReferenceHeight()
        {
            var up = new Vector3(0, -1, 0);
            CollisionTriangle Tri(float y, int k) => k == 0
                ? new(new(-5, y, -5), new(5, y, -5), new(5, y, 5), up, false, CollisionTerrain.Stone, false)
                : new(new(-5, y, -5), new(5, y, 5), new(-5, y, 5), up, false, CollisionTerrain.Stone, false);
            var mesh = new ZoneCollisionMesh(new List<CollisionTriangle> { Tri(0, 0), Tri(0, 1), Tri(-10, 0), Tri(-10, 1) });

            Assert.True(mesh.TryGetNearestGround(1, 1, -8.0f, out var upper));
            Assert.Equal(-10.0f, upper.Height, 3);
            Assert.True(mesh.TryGetNearestGround(1, 1, -2.0f, out var lower));
            Assert.Equal(0.0f, lower.Height, 3);
        }

        [Fact]
        public async Task CollisionService_HandsMeshToTheWorldOnlyWhileItIsStillInThatZone()
        {
            var mesh = new ZoneCollisionMesh(Array.Empty<CollisionTriangle>());
            using var service = new ZoneCollisionService(new SessionRegistry(), zoneId => zoneId == 237 ? mesh : null);
            var world = new WorldState { CurrentZoneId = 237 };

            await service.LoadForAsync(world, 237);
            Assert.Same(mesh, world.Collision);

            world.CurrentZoneId = 236; // zoning clears it
            Assert.Null(world.Collision);
            await service.LoadForAsync(world, 237); // a late load for the old zone is dropped
            Assert.Null(world.Collision);
        }
    }
}
