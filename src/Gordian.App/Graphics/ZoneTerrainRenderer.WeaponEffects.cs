// src/Gordian.App/Graphics/ZoneTerrainRenderer.WeaponEffects.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Tables;
using Gordian.Core.World;
using Gordian.Engine.Graphics;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// Effects a character's worn weapons carry in their model DATs (#8): the Afterglow of the REMA and Prime weapons, one
    /// instance per drawn character and weapon slot, running its DAT's equip routines (<see cref="ActorEffectSet.EquippedRoutines"/>)
    /// while the slot holds that model, drawn at the wearer's joint references like a model's own effects. A weapon taken
    /// off or changed stops emitting, and its particles finish their lives (its <c>!kl?</c> stops the generators with op
    /// 0x2D); a character that leaves drops its instances at once.
    /// </summary>
    public sealed partial class ZoneTerrainRenderer
    {
        private static readonly CharacterSlot[] WeaponEffectSlots = { CharacterSlot.Main, CharacterSlot.Sub, CharacterSlot.Ranged };

        private readonly Dictionary<(uint ServerId, CharacterSlot Slot), ActorEffectInstance> _weaponEffects = new();
        private readonly List<(uint ServerId, ActorEffectInstance Instance)> _stoppingWeaponEffects = new();
        private readonly HashSet<(uint ServerId, CharacterSlot Slot)> _liveWeaponEffects = new();
        private readonly List<(uint ServerId, CharacterSlot Slot)> _staleWeaponEffects = new();

        private void UpdateWeaponEffects(ViewportCamera camera, ZoneEnvironmentSettings environment, IEnumerable<WorldEntity>? entities, ResourceManager? resourceManager, float deltaSeconds)
        {
            _liveWeaponEffects.Clear();
            float frames = Math.Clamp(deltaSeconds, 0.0f, 0.25f) * 60.0f;
            float dayFraction = environment.TimeOfDayHours / 24.0f;
            Vector3 daylight = StrongestLight(environment);
            int dayOfWeek = CurrentDayOfWeek();
            int moonPhase = CurrentMoonPhaseIndex();
            if (EnableZoneEffects && entities != null && resourceManager != null && _entityRenderer != null)
            {
                foreach (var entity in entities)
                {
                    // A character drawn from its grap table (a costume draws a fixed model and no gear).
                    var grap = entity.Appearance.GrapIdTable;
                    if (!entity.IsSpawned || !entity.IsDrawn || entity.Appearance.ModelId != 0 || grap == null || grap.Length <= (int)CharacterSlot.Ranged ||
                        !_entityRenderer.ActorAnchors.TryGetValue(entity.ServerId, out var anchor)) continue;
                    var race = (CharacterRace)(entity.Appearance.FaceModel >> 8);
                    if (race == CharacterRace.Unknown) continue;

                    ZoneParticleFrame? frame = null;
                    foreach (var slot in WeaponEffectSlots)
                    {
                        var effects = resourceManager.GetGearEffects(race, slot, (ushort)(grap[(int)slot] & 0x0FFF));
                        if (effects == null) continue;
                        var key = (entity.ServerId, slot);
                        if (!_weaponEffects.TryGetValue(key, out var instance) || !ReferenceEquals(instance.Effects, effects))
                        {
                            if (instance != null) StopWeaponEffect(entity.ServerId, instance);
                            instance = new ActorEffectInstance(effects, unchecked((int)entity.ServerId + (int)slot));
                            _weaponEffects[key] = instance;
                            UploadActorEffectMeshes(effects);
                        }
                        _liveWeaponEffects.Add(key);

                        if (frame == null)
                        {
                            if (!Matrix4x4.Invert(anchor.ModelToWorld, out var worldToModel)) break;
                            frame = new ZoneParticleFrame(Vector3.Transform(camera.Position, worldToModel), dayFraction, daylight,
                                Vector3.Normalize(Vector3.TransformNormal(camera.Forward, worldToModel)), dayOfWeek, moonPhase);
                        }
                        instance.Update(frames, frame.Value);
                    }
                }
            }

            _staleWeaponEffects.Clear();
            foreach (var (key, instance) in _weaponEffects)
            {
                if (_liveWeaponEffects.Contains(key)) continue;
                _staleWeaponEffects.Add(key);
                // The weapon came off (or the character's look changed): its particles fade out on the wearer. A character
                // that is gone takes them along.
                if (_entityRenderer != null && _entityRenderer.ActorAnchors.ContainsKey(key.ServerId)) StopWeaponEffect(key.ServerId, instance);
            }
            foreach (var key in _staleWeaponEffects) _weaponEffects.Remove(key);

            for (int i = _stoppingWeaponEffects.Count - 1; i >= 0; i--)
            {
                var (serverId, instance) = _stoppingWeaponEffects[i];
                if (_entityRenderer == null || !_entityRenderer.ActorAnchors.TryGetValue(serverId, out var anchor) || !instance.HasParticles ||
                    !Matrix4x4.Invert(anchor.ModelToWorld, out var worldToModel))
                {
                    _stoppingWeaponEffects.RemoveAt(i);
                    continue;
                }
                instance.Update(frames, new ZoneParticleFrame(Vector3.Transform(camera.Position, worldToModel), dayFraction, daylight,
                    Vector3.Normalize(Vector3.TransformNormal(camera.Forward, worldToModel)), dayOfWeek, moonPhase));
            }
        }

        private void StopWeaponEffect(uint serverId, ActorEffectInstance instance)
        {
            instance.Stop();
            if (instance.HasParticles) _stoppingWeaponEffects.Add((serverId, instance));
        }

        private int WeaponEffectCount => _weaponEffects.Count + _stoppingWeaponEffects.Count;

        private void ClearWeaponEffects()
        {
            _weaponEffects.Clear();
            _stoppingWeaponEffects.Clear();
        }

        /// <summary>Queues the live weapon-effect particles for the effect pass, placed as <see cref="AddActorEffectDraws"/> places a model's.</summary>
        private void AddWeaponEffectDraws(ViewportCamera camera)
        {
            if (_entityRenderer == null) return;
            foreach (var ((serverId, _), instance) in _weaponEffects) AddActorInstanceDraws(camera, serverId, instance);
            foreach (var (serverId, instance) in _stoppingWeaponEffects) AddActorInstanceDraws(camera, serverId, instance);
        }
    }
}
