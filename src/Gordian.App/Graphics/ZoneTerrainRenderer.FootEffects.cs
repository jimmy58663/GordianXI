// src/Gordian.App/Graphics/ZoneTerrainRenderer.FootEffects.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using Gordian.Core.Animation;
using Gordian.Core.Audio;
using Gordian.Core.Graphics;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Models;
using Gordian.Core.World;
using Gordian.Core.World.Collision;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// Footprints and foot effects (#40). Where a walking actor's foot lands (<see cref="FootfallDetector"/>, the footfall
    /// hook the step sounds share) the client starts, at the landing foot on the ground and turned to the actor's facing:
    /// <list type="bullet">
    /// <item>the zone's foot effect for the terrain under the foot (<see cref="ZoneFootEffects"/>: <c>fses/fefs</c>, in the
    /// rain <c>fser/fefr</c>), e.g. the sand puff <c>0300</c>;</item>
    /// <item>on sand and snow, the footprint <c>fmrk</c> of the shared effects DAT <c>ROM/0/0</c>
    /// (<c>/syst/effe/fbib</c>: the sprite sheet <c>fmk1</c> laid flat, 0.15 x 0.19, alpha fading from 0.31 to 0 over its
    /// 180-frame life).</item>
    /// </list>
    /// Each footfall's emitters are fixed in the world where the foot landed: the prints stay behind the runner. Only
    /// walking models leave them (<see cref="FootwearInfo.LeavesFootprints"/>), and the config page's Footstep Effects
    /// option turns them off (<see cref="EnableFootEffects"/>; the maintainer, #40, 2026-10-07).
    /// Footprint, foot effect and gating referenced from xi-tools (https://github.com/vekien/xi-tools,
    /// docs/sounds/footsteps.md §2-§4, after xim <c>ZoneDrawer.emitFootSteps</c>).
    /// </summary>
    public sealed partial class ZoneTerrainRenderer
    {
        /// <summary>At most this many footfalls' effects live at once; the oldest give way.</summary>
        private const int MaxFootEffects = 96;

        /// <summary>Actors further than this from the camera leave no footprints (provisional).</summary>
        public const float FootEffectRange = 40f;

        /// <summary>A footfall's effects still alive after this many frames are dropped (a guard; fmrk lives 180).</summary>
        private const float MaxFootEffectFrames = 60f * 8f;

        /// <summary>
        /// The skeleton references of the two feet: on every race skeleton 8 and 9 sit at ground level, 0.16-0.17 to either
        /// side (read from the race skeletons' bind poses, 2026-10-20). The foot effects themselves attach to references
        /// 52 / 53, which every race skeleton leaves at the model origin; the client places them at the foot.
        /// </summary>
        private static readonly int[] FootReferences = [8, 9];

        /// <summary>How far above the ground a footfall's effects sit, so the flat print does not fight the terrain's depth.</summary>
        public const float FootprintLift = 0.03f;

        /// <summary>The generator of the footprint in <c>ROM/0/0</c>.</summary>
        public const string FootprintGenerator = "fmrk";

        private sealed class FootEffectSlot
        {
            public required ActorEffectSet Effects;
            public required List<(WeatherSkyLayer Layer, ZoneParticleEmitter Emitter)> Emitters;
            public Matrix4x4 ToWorld;
            public Matrix4x4 WorldToLocal;
            public float Age;
        }

        private readonly List<FootEffectSlot> _footEffects = new();
        private readonly FootfallDetector _footfalls = new();
        private readonly Dictionary<ActorEffectSet, Dictionary<ZoneEmitterTemplate, WeatherSkyLayer>> _footLayersByTemplate = new(ReferenceEqualityComparer.Instance);
        private WorldState? _footEffectsWorld;
        private ActorEffectSet? _uploadedZoneFootEffects;
        private int _footEffectSeed;

        /// <summary>Whether footprints and foot effects are drawn: the config page's Footstep Effects option.</summary>
        public bool EnableFootEffects { get; set; } = true;

        /// <summary>Footfalls whose effects are alive (tests and the performance overlay).</summary>
        internal int FootEffectCount => _footEffects.Count;

        /// <summary>Live foot-effect particles (tests).</summary>
        internal int FootParticleCount
        {
            get
            {
                int count = 0;
                foreach (var slot in _footEffects)
                {
                    foreach (var (_, emitter) in slot.Emitters) count += emitter.Particles.Count;
                }
                return count;
            }
        }

        private void ClearFootEffects()
        {
            _footEffects.Clear();
            _footfalls.Clear();
        }

        /// <summary>
        /// A zone is shown (under the device lock): its footfalls' effects go, and the last zone's foot-effect meshes are freed.
        /// </summary>
        private void ResetFootEffects(ZoneGeometry? zone)
        {
            ClearFootEffects();
            var shown = zone?.FootEffects?.Effects;
            if (_uploadedZoneFootEffects != null && !ReferenceEquals(_uploadedZoneFootEffects, shown) &&
                _actorEffectMeshes.Remove(_uploadedZoneFootEffects, out var meshes))
            {
                foreach (var list in meshes.Values)
                {
                    foreach (var mesh in list) mesh.Dispose();
                }
                _footLayersByTemplate.Remove(_uploadedZoneFootEffects);
                _uploadedZoneFootEffects = null;
            }
        }

        /// <summary>Starts the effects of this frame's footfalls and advances the live ones in their fixed frames.</summary>
        private void UpdateFootEffects(ViewportCamera camera, ZoneEnvironmentSettings environment, IEnumerable<WorldEntity>? entities,
            ResourceManager? resourceManager, float deltaSeconds)
        {
            if (!EnableZoneEffects || !EnableFootEffects || _entityRenderer == null)
            {
                ClearFootEffects();
                return;
            }

            // Another character's world is shown: its actors are other objects.
            if (!ReferenceEquals(World, _footEffectsWorld))
            {
                _footEffectsWorld = World;
                ClearFootEffects();
            }

            var collision = LoadedZone?.Collision;
            if (entities != null && resourceManager != null && collision != null)
            {
                bool wet = ZoneSoundTable.IsWetWeather(environment.WeatherId);
                Vector3 cameraInternal = new(-camera.Position.X, -camera.Position.Y, camera.Position.Z);
                foreach (var entity in entities)
                {
                    if (!entity.IsSpawned || !entity.IsDrawn ||
                        Vector3.DistanceSquared(entity.Position, cameraInternal) > FootEffectRange * FootEffectRange ||
                        !_footfalls.Step(entity) ||
                        !_entityRenderer.ActorAnchors.TryGetValue(entity.ServerId, out var anchor) ||
                        !FootwearCache.Shared.Of(entity, resourceManager).LeavesFootprints ||
                        !TryFindLandingFoot(anchor, out Vector3 foot)) continue;
                    StartFootfall(foot, entity.HeadingRadians, collision, wet, resourceManager);
                }
                _footfalls.EndFrame();
            }

            if (_footEffects.Count == 0) return;
            float frames = Math.Clamp(deltaSeconds, 0.0f, 0.25f) * 60.0f;
            float dayFraction = environment.TimeOfDayHours / 24.0f;
            Vector3 daylight = StrongestLight(environment);
            int dayOfWeek = CurrentDayOfWeek();
            int moonPhase = CurrentMoonPhaseIndex();
            for (int i = _footEffects.Count - 1; i >= 0; i--)
            {
                var slot = _footEffects[i];
                var frame = new ZoneParticleFrame(Vector3.Transform(camera.Position, slot.WorldToLocal), dayFraction, daylight,
                    Vector3.Normalize(Vector3.TransformNormal(camera.Forward, slot.WorldToLocal)), dayOfWeek, moonPhase);
                bool alive = false;
                foreach (var (_, emitter) in slot.Emitters)
                {
                    emitter.Update(frames, frame);
                    alive |= emitter.Particles.Count > 0;
                }
                slot.Age += frames;
                // The root emits once when it is started, so once its particles and their children are gone it is over.
                if ((!alive && slot.Age >= 10f) || slot.Age > MaxFootEffectFrames) _footEffects.RemoveAt(i);
            }
        }

        /// <summary>
        /// Starts a footfall's effects at a foot (display space): on the ground under it, the zone's foot effect for its
        /// terrain and, on sand or snow, the footprint. Returns the terrain, or null when no ground is found under the foot.
        /// </summary>
        internal CollisionTerrain? StartFootfall(Vector3 foot, float headingRadians, ZoneCollisionMesh collision, bool wet, ResourceManager resourceManager)
        {
            if (!TryFindGround(foot, collision, out Vector3 ground, out Vector3 normal, out CollisionTerrain terrain)) return null;
            Matrix4x4 toWorld = FootEffectFrame(headingRadians, ground, normal);
            if (LoadedZone?.FootEffects is { } zoneFeet && zoneFeet.RootFor((int)terrain, wet) is { } root) StartFootEffect(zoneFeet.Effects, root, toWorld);
            if (terrain is CollisionTerrain.Sand or CollisionTerrain.Snow &&
                resourceManager.GetSharedRoutineEffects() is { } shared && FindLayer(shared.Effects, FootprintGenerator) is { } print)
            {
                StartFootEffect(shared.Effects, print, toWorld);
            }
            return terrain;
        }

        /// <summary>The landing foot (display space): the lower of the two foot references. False without a posed skeleton.</summary>
        private static bool TryFindLandingFoot(ActorAnchor anchor, out Vector3 foot)
        {
            foot = default;
            if (anchor.Skeleton == null) return false;
            var pose = anchor.Pose;
            bool found = false;
            foreach (int reference in FootReferences)
            {
                if (reference >= anchor.Skeleton.References.Count) continue;
                Vector3 joint = ActorEffectInstance.ResolveJointReference(anchor.Skeleton, pose.Translations, pose.Rotations, pose.Scales, reference);
                Vector3 world = Vector3.Transform(joint, anchor.ModelToWorld);
                if (!found || world.Y < foot.Y) foot = world;
                found = true;
            }
            return found;
        }

        /// <summary>The ground under a foot (display space): its point, normal and collision terrain; false when none.</summary>
        private static bool TryFindGround(Vector3 foot, ZoneCollisionMesh collision, out Vector3 ground, out Vector3 normal, out CollisionTerrain terrain)
        {
            ground = normal = default;
            terrain = CollisionTerrain.Object;
            // Display space is (-x, -y, z) of the internal space the collision uses (internal Y is height, -Y up).
            var internalFoot = new Vector3(-foot.X, -foot.Y, foot.Z);
            if (!collision.TryGetGround(internalFoot, 1.0f, 2.0f, out GroundHit hit)) return false;
            ground = new Vector3(foot.X, -hit.Height, foot.Z);
            normal = Vector3.Normalize(new Vector3(-hit.Normal.X, -hit.Normal.Y, hit.Normal.Z));
            if (!(normal.Y > 0.1f)) normal = Vector3.UnitY;
            terrain = hit.Terrain;
            return true;
        }

        /// <summary>
        /// The model frame a footfall's effects play in: the actor's facing (as an entity is placed), tilted onto the ground's
        /// slope, at the ground point under the foot lifted by <see cref="FootprintLift"/>.
        /// </summary>
        internal static Matrix4x4 FootEffectFrame(float headingRadians, Vector3 ground, Vector3 normal)
        {
            Matrix4x4 tilt = Matrix4x4.Identity;
            Vector3 axis = Vector3.Cross(Vector3.UnitY, normal);
            float sin = axis.Length();
            if (sin > 1e-4f)
            {
                float angle = MathF.Atan2(sin, Vector3.Dot(Vector3.UnitY, normal));
                tilt = Matrix4x4.CreateFromAxisAngle(axis / sin, angle);
            }
            return EntityModelFrame * Matrix4x4.CreateRotationY(-headingRadians - MathF.PI) * tilt *
                   Matrix4x4.CreateTranslation(ground + normal * FootprintLift);
        }

        /// <summary>Starts a foot effect: the root generator once, with emitters for the children it spawns.</summary>
        private void StartFootEffect(ActorEffectSet effects, WeatherSkyLayer root, Matrix4x4 toWorld)
        {
            if (root.Emitter == null || !Matrix4x4.Invert(toWorld, out var worldToLocal)) return;
            UploadActorEffectMeshes(effects);
            if (effects.Layers.Count > 0 && LoadedZone?.FootEffects is { } zoneFeet && ReferenceEquals(zoneFeet.Effects, effects)) _uploadedZoneFootEffects = effects;
            if (!_footLayersByTemplate.TryGetValue(effects, out var byTemplate))
            {
                byTemplate = new Dictionary<ZoneEmitterTemplate, WeatherSkyLayer>(ReferenceEqualityComparer.Instance);
                foreach (var layer in effects.Layers)
                {
                    if (layer.Emitter != null) byTemplate.TryAdd(layer.Emitter, layer);
                }
                _footLayersByTemplate[effects] = byTemplate;
            }

            var emitters = new List<(WeatherSkyLayer Layer, ZoneParticleEmitter Emitter)>();
            var byEmitterTemplate = new Dictionary<ZoneEmitterTemplate, ZoneParticleEmitter>(ReferenceEqualityComparer.Instance);
            int seed = unchecked(++_footEffectSeed * 7919);
            void Add(WeatherSkyLayer layer, int depth)
            {
                if (layer.Emitter == null || byEmitterTemplate.ContainsKey(layer.Emitter)) return;
                var emitter = new ZoneParticleEmitter(layer.Emitter, seed + emitters.Count)
                {
                    ChildResolver = template => byEmitterTemplate.GetValueOrDefault(template)
                };
                byEmitterTemplate[layer.Emitter] = emitter;
                emitters.Add((layer, emitter));
                if (depth >= 4) return;
                foreach (var child in layer.Emitter.Children.Values)
                {
                    if (byTemplate.TryGetValue(child, out var childLayer)) Add(childLayer, depth + 1);
                }
            }
            Add(root, 0);
            emitters[0].Emitter.Trigger(0, 0);

            if (_footEffects.Count >= MaxFootEffects) _footEffects.RemoveAt(0);
            _footEffects.Add(new FootEffectSlot { Effects = effects, Emitters = emitters, ToWorld = toWorld, WorldToLocal = worldToLocal });
        }

        private static WeatherSkyLayer? FindLayer(ActorEffectSet effects, string generator)
        {
            foreach (var layer in effects.Layers)
            {
                if (layer.Emitter != null && !layer.Emitter.ChildOnly && string.Equals(layer.Name, generator, StringComparison.OrdinalIgnoreCase)) return layer;
            }
            return null;
        }

        /// <summary>Queues the live foot-effect particles, each footfall in its fixed frame.</summary>
        private void AddFootEffectDraws(ViewportCamera camera)
        {
            foreach (var slot in _footEffects)
            {
                if (!_actorEffectMeshes.TryGetValue(slot.Effects, out var byLayer)) continue;
                Matrix4x4 frame = DisplayFlip * slot.ToWorld;
                float distance = Vector3.Distance(camera.Position, slot.ToWorld.Translation);
                foreach (var (layer, emitter) in slot.Emitters)
                {
                    if (emitter.Particles.Count == 0 || !byLayer.TryGetValue(layer, out var meshes)) continue;
                    foreach (var mesh in meshes) _effectDrawList.Add(new EffectDraw(mesh, distance, emitter, frame));
                }
            }
        }
    }
}
