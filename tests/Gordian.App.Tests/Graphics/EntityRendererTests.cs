// tests/Gordian.App.Tests/Graphics/EntityRendererTests.cs
using System;
using System.Numerics;
using Gordian.App.Graphics;
using Gordian.Core.Animation;
using Gordian.Core.Graphics;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Models;
using Gordian.Core.World;
using Xunit;

namespace Gordian.App.Tests.Graphics
{
    public class EntityRendererTests
    {
        private static readonly Matrix4x4 EntityRotMatrix = new(
            1.0f,  0.0f,  0.0f, 0.0f,
            0.0f, -1.0f,  0.0f, 0.0f,
            0.0f,  0.0f, -1.0f, 0.0f,
            0.0f,  0.0f,  0.0f, 1.0f
        );

        [Fact]
        public void EntityTransformMatrix_HasPositiveDeterminant_NoReflection()
        {
            // Determinant must be +1.0 (proper rotation, not a reflection that flips handedness)
            float det = EntityRotMatrix.GetDeterminant();
            Assert.Equal(1.0f, det, 4);
        }

        [Fact]
        public void EntityTransformMatrix_MapsYDownModelToYUpWorld()
        {
            var entityPos = new Vector3(10f, 20f, 30f);
            var world = EntityRotMatrix * Matrix4x4.Identity * Matrix4x4.CreateTranslation(entityPos);

            // A point at (0, -1.5, 0) in FFXI model coordinates (1.5 yalms up from feet in Y-down)
            var modelHead = new Vector4(0f, -1.5f, 0f, 1.0f);
            var worldHead = Vector4.Transform(modelHead, world);

            // World Y should be 20 + 1.5 = 21.5
            Assert.Equal(10f, worldHead.X, 3);
            Assert.Equal(21.5f, worldHead.Y, 3);
            Assert.Equal(30f, worldHead.Z, 3);
        }

        [Fact]
        public void EntityFrustumCulling_AccuratelyCullsOffScreenEntities()
        {
            var camera = new ViewportCamera();
            camera.Update(
                targetPosition: Vector3.Zero,
                pitch: 0f,
                yaw: 0f,
                distance: 10f,
                aspectRatio: 16f / 9f);

            var frustum = camera.Frustum;

            // Entity right at target (0, 0, 0) should be visible
            var visibleMin = new Vector3(-1f, 0f, -1f);
            var visibleMax = new Vector3(1f, 2f, 1f);
            Assert.True(frustum.IntersectsBox(visibleMin, visibleMax));

            // Entity far behind camera (at Z = -200) should be culled
            var culledMin = new Vector3(-1f, 0f, -201f);
            var culledMax = new Vector3(1f, 2f, -199f);
            Assert.False(frustum.IntersectsBox(culledMin, culledMax));
        }

        [Fact]
        public void EntityHeading_RotatesAroundYAxis()
        {
            // Direction 64 = 90 degrees = South (-Z); display space keeps Z unchanged.
            // In display space, entity model at rest faces (+1, 0, 0); EntityRenderer rotates by (-heading - pi).
            byte dir = 64;
            float headingRad = (dir / 256.0f) * MathF.PI * 2.0f;
            var rotY = Matrix4x4.CreateRotationY(-headingRad - MathF.PI);

            var forward = new Vector4(1f, 0f, 0f, 0f);
            var turned = Vector4.Transform(forward, rotY);

            Assert.Equal(0f, turned.X, 2);
            Assert.Equal(0f, turned.Y, 2);
            Assert.Equal(-1f, turned.Z, 2);
        }

        [Fact]
        public void EntityHeading_RotationTracksHeadingDeltaConsistently()
        {
            // Turning the heading by a given amount turns the rendered facing by that same amount
            // in a consistent direction around the Y-axis.
            float prevAngle = 0f;
            bool first = true;

            foreach (byte dir in new byte[] { 0, 32, 64, 96, 128, 160, 192, 224 })
            {
                float headingRad = (dir / 256.0f) * MathF.PI * 2.0f;
                var rotY = Matrix4x4.CreateRotationY(-headingRad - MathF.PI);

                var forward = new Vector4(1f, 0f, 0f, 0f);
                var turned = Vector4.Transform(forward, rotY);
                float angle = MathF.Atan2(turned.Z, turned.X);

                if (!first)
                {
                    float delta = angle - prevAngle;
                    if (delta < -MathF.PI) delta += 2 * MathF.PI;
                    if (delta > MathF.PI) delta -= 2 * MathF.PI;

                    // Each step in the loop advances heading by 32/256 of a turn (45 degrees)
                    Assert.Equal(MathF.PI / 4.0f, MathF.Abs(delta), 3);
                }

                prevAngle = angle;
                first = false;
            }
        }

        [Fact]
        public void ResolveClip_AnimationSub5_ResolvesInShellBreathingStance_1tl0_OverGuardAndDamageClips()
        {
            // Uragnites and shell/alternate-stance mobs (Adamantoise, Exoplates) have:
            // - idl0 / btl0: standard open/active stance (17 frames)
            // - 1tl0: Mode 1 alternate/in-shell breathing stance (17 frames)
            // - gud0: 2-frame guard block impact clamp
            // - dfi0 / dbi0: 2-frame damage flinch impact
            // When AnimationSub == 5 (in shell), ResolveClip must select 1tl0 (or 1tl) for both Idle and Combat,
            // giving the relaxed, breathing in-shell animation rather than the static 2-frame guard clamp (gud0)
            // or damage flinch (dfi0 / dbi0).
            var model = new EntityModel { Name = "Uragnite" };
            model.Animations["idl0"] = new AnimationClip { Name = "idl0", NumFrames = 17 };
            model.Animations["btl0"] = new AnimationClip { Name = "btl0", NumFrames = 17 };
            model.Animations["1tl0"] = new AnimationClip { Name = "1tl0", NumFrames = 17 };
            model.Animations["dfi0"] = new AnimationClip { Name = "dfi0", NumFrames = 2 };
            model.Animations["dbi0"] = new AnimationClip { Name = "dbi0", NumFrames = 2 };
            model.Animations["gud0"] = new AnimationClip { Name = "gud0", NumFrames = 2 };

            // When AnimationSub == 5 (in shell):
            // Idle category must resolve 1tl0 (breathing in-shell stance), NOT gud0 or dfi0
            var idleInShell = EntityRenderer.ResolveClip(model, AnimationCategory.Idle, animationSub: 5);
            Assert.NotNull(idleInShell);
            Assert.Equal("1tl0", idleInShell.Name);

            // Combat category must also resolve 1tl0 (breathing in-shell stance), NOT gud0 or dbi0
            var combatInShell = EntityRenderer.ResolveClip(model, AnimationCategory.Combat, animationSub: 5);
            Assert.NotNull(combatInShell);
            Assert.Equal("1tl0", combatInShell.Name);

            // Walk / Run without specific gdm clip should fall back to 1tl0
            var walkInShell = EntityRenderer.ResolveClip(model, AnimationCategory.Walk, animationSub: 5);
            Assert.NotNull(walkInShell);
            Assert.Equal("1tl0", walkInShell.Name);

            // When AnimationSub == 4 (Uragnite open / out of shell) or 0:
            var idleOpen = EntityRenderer.ResolveClip(model, AnimationCategory.Idle, animationSub: 4);
            Assert.NotNull(idleOpen);
            Assert.Equal("idl0", idleOpen.Name);

            var combatOpen = EntityRenderer.ResolveClip(model, AnimationCategory.Combat, animationSub: 4);
            Assert.NotNull(combatOpen);
            Assert.Equal("btl0", combatOpen.Name);
        }

        [Fact]
        public void ResolveClip_AnimationSub5_FallsBackToGidThenGudWhen1tlNotPresent()
        {
            // Mobs without 1tl0 that have guard idle (gid0) should resolve gid0 before gud0
            var modelWithGid = new EntityModel { Name = "GuardMob" };
            modelWithGid.Animations["gid0"] = new AnimationClip { Name = "gid0", NumFrames = 15 };
            modelWithGid.Animations["gud0"] = new AnimationClip { Name = "gud0", NumFrames = 2 };

            var clipGid = EntityRenderer.ResolveClip(modelWithGid, AnimationCategory.Idle, animationSub: 5);
            Assert.NotNull(clipGid);
            Assert.Equal("gid0", clipGid.Name);

            // Mobs with only gud0 should fall back to gud0
            var modelWithGudOnly = new EntityModel { Name = "TurtleMob" };
            modelWithGudOnly.Animations["gud0"] = new AnimationClip { Name = "gud0", NumFrames = 2 };

            var clipGud = EntityRenderer.ResolveClip(modelWithGudOnly, AnimationCategory.Idle, animationSub: 5);
            Assert.NotNull(clipGud);
            Assert.Equal("gud0", clipGud.Name);
        }

        [Fact]
        public void ResolveClip_AnimationSub5_PrefersMovingGuardClipWhenAvailable()
        {
            var model = new EntityModel { Name = "DefendingMob" };
            model.Animations["1tl0"] = new AnimationClip { Name = "1tl0", NumFrames = 17 };
            model.Animations["gud0"] = new AnimationClip { Name = "gud0", NumFrames = 2 };
            model.Animations["gdm1"] = new AnimationClip { Name = "gdm1", NumFrames = 2 };

            var walkClip = EntityRenderer.ResolveClip(model, AnimationCategory.Walk, animationSub: 5);
            Assert.NotNull(walkClip);
            Assert.Equal("gdm1", walkClip.Name);

            var idleClip = EntityRenderer.ResolveClip(model, AnimationCategory.Idle, animationSub: 5);
            Assert.NotNull(idleClip);
            Assert.Equal("1tl0", idleClip.Name);
        }

        [Fact]
        public void Uragnite_ClosedShell_1tl0_TentaclesTuckedInFront()
        {
            string path = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI\ROM\259\15.DAT";
            if (!System.IO.File.Exists(path)) return;
            byte[] bytes = System.IO.File.ReadAllBytes(path);
            var container = Gordian.Core.Resources.EntityModelLoader.ParseDatContainer(bytes, "Uragnite");
            var skel = container.Skeleton;
            Assert.NotNull(skel);

            var c1tl = container.Animations.Find(c => c.Name == "1tl0");
            Assert.NotNull(c1tl);

            var pose = SkeletonPoseEvaluator.EvaluatePose(skel, c1tl, 0f, true);
            Assert.Equal(skel.Count, pose.Scales.Length);

            // Stalk bones (joints 30-33 and 44-47) are contracted via bone scale channels
            Assert.True(pose.Scales[30].X < 0.9f, "Left tentacle stalk should be scaled down");
            Assert.True(pose.Scales[44].X < 0.9f, "Right tentacle stalk should be scaled down");

            // Tips of tentacles (joints 39 and 53) are pulled into resting position in front of the shell
            // (~1.15 yalms apart with bone scale applied, compared to ~2.64 yalms when unscaled)
            float tipDistance = Vector3.Distance(pose.Translations[39], pose.Translations[53]);
            Assert.InRange(tipDistance, 1.0f, 1.3f);
            Assert.InRange(pose.Translations[39].X, 0.1f, 0.5f);
            Assert.InRange(pose.Translations[53].X, 0.1f, 0.5f);
        }
    }
}
