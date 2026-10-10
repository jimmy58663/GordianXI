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
    /// <param name="IsAttackRound">Whether the playing action is a melee attack (a swing or counter, S2C 0x028 basic attack).</param>
    /// <param name="OffHand">Whether the playing action is an off-hand attack (the result's <c>sub_kind</c> 1).</param>
    public readonly record struct ActorSoundState(uint Id, Vector3 Position, EntityModel? Model, string? Routine, int ActionSerial,
        float ActionTicks, int ReactionSerial, HitReaction LastReaction, bool IsDead, bool IsAttackRound = false, bool OffHand = false);

    /// <summary>
    /// Combat and action sounds (#41): plays the sounds an actor's motion routines carry, on the routine clock, so they
    /// follow the motions the action playback (Phase 5D.1) starts from S2C 0x028: the swing's whoosh and the weapon's
    /// draw / sheathe, the attack cry (<c>atk0</c>), a hit's sounds (the target's <c>damg</c>, which runs the attacker's
    /// <c>chit</c>: its weapon's or its own hit sound), guard / parry / block, and the death cry of <c>dead</c>. Which
    /// routine holds which sound is documented on <see cref="Gordian.Core.Resources.Graphics.RoutineSoundCollector"/>.
    /// </summary>
    /// <remarks>
    /// Retail runs <c>atk0</c> for every melee attack: it links the cry <c>vatk</c> and picks the swing with op 0x24, which
    /// GordianXI's playback does instead (it plays the swing routine directly), so the tracker adds <c>atk0</c>'s sounds when
    /// a melee swing starts; before that no attack cry was ever heard (in-game round 1, Pinetorum Mandragora).
    /// With dual wield an off-hand swing or hit uses the sub weapon's routines (<see cref="EntityModel.GetRoutineSounds"/>).
    /// Provisional: a random choice (op 0x3D / 0x3E) is uniform, silent members included (a Mandragora's <c>vatk</c>: four
    /// cries, three silent, 57 %, close to the 60-70 % of rounds heard in retail); every sound plays at its actor; the
    /// reaction's routines play from their start when the reaction is applied, with the hand of the attacker's current swing.
    /// Not done: sounds the shared <c>ROM/0/0</c> hit routines spawn through sound generators (picked by conditional ops on
    /// the action result, which are not interpreted yet), and the spell, ability and weapon skill effect DATs.
    /// </remarks>
    public sealed class ActionSoundTracker
    {
        /// <summary>
        /// Actors further than this from the listener are not tracked: retail combat sounds fade with distance and are
        /// silent by about 25-30 yalms (the maintainer's retail check, 2026-10-07; 30 chosen).
        /// </summary>
        public const float HearingRange = 30f;

        /// <summary>The routine retail runs for each melee attack (its sounds: the attack cry).</summary>
        public const string AttackRoundRoutine = "atk0";

        private readonly Dictionary<uint, Tracked> _actors = new();
        private readonly HashSet<uint> _seen = new();
        private readonly Func<int, int> _random;
        private readonly Func<EntityModel, string, bool, IReadOnlyList<RoutineSoundCue>> _sounds;

        /// <param name="random">Picks an index below its argument (a random choice); <see cref="Random.Shared"/> by default.</param>
        /// <param name="sounds">A model's sounds for a routine and hand; <see cref="EntityModel.GetRoutineSounds"/> by default.</param>
        public ActionSoundTracker(Func<int, int>? random = null, Func<EntityModel, string, bool, IReadOnlyList<RoutineSoundCue>>? sounds = null)
        {
            _random = random ?? Random.Shared.Next;
            _sounds = sounds ?? ((model, name, offHand) => model.GetRoutineSounds(name, offHand));
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

        /// <summary>
        /// Snapshot of a world entity. Only a routine of the actor's own model counts: an event gesture from a motion
        /// bank is not one, and the model's routine of the same name would be the wrong sounds.
        /// </summary>
        public static ActorSoundState Snapshot(WorldEntity entity)
        {
            EntityAnimationState a = entity.Animation;
            MotionRoutine? active = a.ActiveRoutine;
            string? routine = active is not null && a.Model is { } model && model.MotionRoutines.TryGetValue(active.Name, out var own)
                && ReferenceEquals(own, active) ? active.Name : null;
            ActionRequest? request = a.ActiveRequest;
            bool attack = request is { Motion: ActionMotion.Swing or ActionMotion.Counter };
            return new ActorSoundState(entity.ServerId, entity.Position, a.Model, routine, a.ActionSerial, a.ActionTicks,
                a.ReactionSerial, a.LastReaction, a.Current == AnimationCategory.Death, attack, attack && request!.SubKind == 1);
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
                bool started = actor.ActionSerial != last.ActionSerial;
                float from = started ? -1f : last.ActionTicks;
                Play(_sounds(model, routine, actor.OffHand), from, actor.ActionTicks, actor.Position, into);
                if (started && actor.IsAttackRound)
                {
                    Play(_sounds(model, AttackRoundRoutine, actor.OffHand), float.NegativeInfinity, float.PositiveInfinity, actor.Position, into);
                }
            }

            if (model is not null && actor.ReactionSerial != last.ReactionSerial && ReactionRoutine(actor.LastReaction.Resolution) is { } reaction)
            {
                foreach (RoutineSoundCue cue in _sounds(model, reaction, false))
                {
                    if (!cue.IsTargetLink)
                    {
                        Emit(cue, actor.Position, into);
                    }
                    else if (lookup(actor.LastReaction.AttackerId) is { Model: { } attackerModel } attacker)
                    {
                        // damg runs chit on the attacker: the hand's weapon (or its own) hit sound, heard where the hit lands.
                        foreach (RoutineSoundCue hit in _sounds(attackerModel, cue.TargetRoutine, attacker.OffHand))
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
                Play(_sounds(model, "dead", false), float.NegativeInfinity, float.PositiveInfinity, actor.Position, into);
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

        private void Play(IReadOnlyList<RoutineSoundCue> cues, float after, float upTo, Vector3 position, List<ActionSoundEvent> into)
        {
            foreach (RoutineSoundCue cue in cues)
            {
                if (cue.Tick > after && cue.Tick <= upTo && !cue.IsTargetLink)
                {
                    Emit(cue, position, into);
                }
            }
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
