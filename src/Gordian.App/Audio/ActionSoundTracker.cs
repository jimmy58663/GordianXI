// src/Gordian.App/Audio/ActionSoundTracker.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using Gordian.Core.Animation;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources.Models;
using Gordian.Core.World;

namespace Gordian.App.Audio
{
    /// <summary>A combat or action sound to play: which sound, where.</summary>
    public readonly record struct ActionSoundEvent(int SoundId, Vector3 Position);

    /// <summary>What <see cref="ActionSoundTracker"/> needs of an actor each frame.</summary>
    /// <param name="Id">Server id.</param>
    /// <param name="Position">Position (internal space).</param>
    /// <param name="Model">The model its animation last used, or null.</param>
    /// <param name="Routine">The name of the action routine playing, or null.</param>
    /// <param name="ActionSerial">Changes with every routine start (<see cref="EntityAnimationState.ActionSerial"/>).</param>
    /// <param name="ActionTicks">Routine ticks since the start.</param>
    /// <param name="ReactionSerial">Changes with every hit reaction (<see cref="EntityAnimationState.ReactionSerial"/>).</param>
    /// <param name="LastReaction">The last hit reaction.</param>
    /// <param name="IsDead">Whether the actor is dead (its death motion plays).</param>
    public readonly record struct ActorSoundState(uint Id, Vector3 Position, EntityModel? Model, string? Routine, int ActionSerial,
        float ActionTicks, int ReactionSerial, HitReaction LastReaction, bool IsDead);

    /// <summary>
    /// Combat and action sounds (#41): plays the sounds an actor's motion routines carry, on the routine clock, so they
    /// follow the motions the action playback (Phase 5D.1) starts from S2C 0x028: the swing's whoosh and the weapon's
    /// draw / sheathe, cries, a hit's sounds (the target's <c>damg</c>, which runs the attacker's <c>chit</c>: its weapon's
    /// or its own hit sound), guard / parry / block, and the death cry of <c>dead</c>. Which routine holds which sound is
    /// documented on <see cref="Gordian.Core.Resources.Graphics.RoutineSoundCollector"/>.
    /// </summary>
    /// <remarks>
    /// Provisional: a random choice (op 0x3D / 0x3E) is uniform, silent members included; every sound plays at its actor
    /// with the cutscene sound range; the reaction's routines play from their start at the moment the reaction is applied.
    /// Not done: sounds the shared <c>ROM/0/0</c> hit routines spawn through sound generators (picked by conditional ops on
    /// the action result, which are not interpreted yet), and the spell, ability and weapon skill effect DATs.
    /// </remarks>
    public sealed class ActionSoundTracker
    {
        /// <summary>Actors further than this from the listener are not tracked (the far end of the sound range).</summary>
        public const float HearingRange = 60f;

        private readonly Dictionary<uint, Tracked> _actors = new();
        private readonly HashSet<uint> _seen = new();
        private readonly Func<int, int> _random;
        private readonly Func<EntityModel, string, IReadOnlyList<RoutineSoundCue>> _sounds;

        /// <param name="random">Picks an index below its argument (a random choice); <see cref="Random.Shared"/> by default.</param>
        /// <param name="sounds">A model's sounds for a routine; <see cref="EntityModel.GetRoutineSounds"/> by default.</param>
        public ActionSoundTracker(Func<int, int>? random = null, Func<EntityModel, string, IReadOnlyList<RoutineSoundCue>>? sounds = null)
        {
            _random = random ?? Random.Shared.Next;
            _sounds = sounds ?? ((model, name) => model.GetRoutineSounds(name));
        }

        /// <summary>The routine whose sounds a hit reaction plays, by its resolution.</summary>
        public static string? ReactionRoutine(ActionResolution resolution) => resolution switch
        {
            ActionResolution.Hit => "damg",
            ActionResolution.Guard => "gurd",
            ActionResolution.Parry => "pary",
            ActionResolution.Block => "gur1",
            _ => null,
        };

        /// <summary>Snapshot of a world entity.</summary>
        public static ActorSoundState Snapshot(WorldEntity entity)
        {
            EntityAnimationState a = entity.Animation;
            return new ActorSoundState(entity.ServerId, entity.Position, a.Model, a.ActiveRoutine?.Name, a.ActionSerial, a.ActionTicks,
                a.ReactionSerial, a.LastReaction, a.Current == AnimationCategory.Death);
        }

        /// <summary>Checks every drawn actor near the listener and adds the sounds due this frame.</summary>
        public void Update(IEnumerable<WorldEntity> entities, Vector3 listener, Func<uint, ActorSoundState?> lookup, List<ActionSoundEvent> into)
        {
            _seen.Clear();
            foreach (WorldEntity entity in entities)
            {
                if (entity.IsDrawn && Vector3.DistanceSquared(entity.Position, listener) <= HearingRange * HearingRange)
                {
                    Step(Snapshot(entity), lookup, into);
                }
            }

            Forget();
        }

        /// <summary>Advances one actor (tests drive this directly); call <see cref="Forget"/> after a frame's steps.</summary>
        public void Step(ActorSoundState actor, Func<uint, ActorSoundState?> lookup, List<ActionSoundEvent> into)
        {
            _seen.Add(actor.Id);
            if (!_actors.TryGetValue(actor.Id, out Tracked last))
            {
                // First seen: nothing replays from before it came into range.
                _actors[actor.Id] = new Tracked(actor.ActionSerial, actor.ActionTicks, actor.ReactionSerial, actor.IsDead);
                return;
            }

            EntityModel? model = actor.Model;
            if (model is not null && actor.Routine is { Length: > 0 } routine)
            {
                float from = actor.ActionSerial != last.ActionSerial ? -1f : last.ActionTicks;
                foreach (RoutineSoundCue cue in _sounds(model, routine))
                {
                    if (cue.Tick > from && cue.Tick <= actor.ActionTicks && !cue.IsTargetLink)
                    {
                        Emit(cue, actor.Position, into);
                    }
                }
            }

            if (model is not null && actor.ReactionSerial != last.ReactionSerial && ReactionRoutine(actor.LastReaction.Resolution) is { } reaction)
            {
                foreach (RoutineSoundCue cue in _sounds(model, reaction))
                {
                    if (!cue.IsTargetLink)
                    {
                        Emit(cue, actor.Position, into);
                    }
                    else if (lookup(actor.LastReaction.AttackerId) is { Model: { } attackerModel })
                    {
                        // damg runs chit on the attacker: its weapon's (or its own) hit sound, heard where the hit lands.
                        foreach (RoutineSoundCue hit in _sounds(attackerModel, cue.TargetRoutine))
                        {
                            if (!hit.IsTargetLink)
                            {
                                Emit(hit, actor.Position, into);
                            }
                        }
                    }
                }
            }

            if (model is not null && actor.IsDead && !last.IsDead)
            {
                foreach (RoutineSoundCue cue in _sounds(model, "dead"))
                {
                    if (!cue.IsTargetLink)
                    {
                        Emit(cue, actor.Position, into);
                    }
                }
            }

            _actors[actor.Id] = new Tracked(actor.ActionSerial, actor.ActionTicks, actor.ReactionSerial, actor.IsDead);
        }

        /// <summary>Drops the actors not stepped since the last call.</summary>
        public void Forget()
        {
            if (_actors.Count > _seen.Count)
            {
                var stale = new List<uint>();
                foreach (uint id in _actors.Keys)
                {
                    if (!_seen.Contains(id))
                    {
                        stale.Add(id);
                    }
                }

                foreach (uint id in stale)
                {
                    _actors.Remove(id);
                }
            }

            _seen.Clear();
        }

        private void Emit(RoutineSoundCue cue, Vector3 position, List<ActionSoundEvent> into)
        {
            if (cue.Choices.Count == 0)
            {
                return;
            }

            int id = cue.Choices.Count == 1 ? cue.Choices[0] : cue.Choices[Math.Clamp(_random(cue.Choices.Count), 0, cue.Choices.Count - 1)];
            if (id > 0)
            {
                into.Add(new ActionSoundEvent(id, position));
            }
        }

        private readonly record struct Tracked(int ActionSerial, float ActionTicks, int ReactionSerial, bool IsDead);
    }
}
