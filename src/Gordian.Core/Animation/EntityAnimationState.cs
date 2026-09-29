// src/Gordian.Core/Animation/EntityAnimationState.cs
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Models;

namespace Gordian.Core.Animation
{
    /// <summary>
    /// Per-entity animation playback state: multi-channel cross-fading, stance transitions,
    /// and continuous clip advancement. Lives on WorldEntity so its lifetime matches the entity's own.
    /// On top of the continuous (stance / locomotion) channel it plays one-shot actions from S2C 0x028 (swings, chants,
    /// releases) and the weapon draw / sheathe through the model's motion routines, then blends back to the stance, plus a
    /// pose overlay for hit reactions (the flinch toward the damage pose, the guard / parry pose flash).
    /// Clean-room implementation referencing FFXI animation blending specifications in xi-model-viewer (https://github.com/vekien/xi-model-viewer)
    /// and the motion routine rules in xi-tools (docs/ability/mixer.md).
    /// </summary>
    public sealed class EntityAnimationState
    {
        public const float DefaultBlendDuration = 0.18f; // 180ms blend window

        /// <summary>Motion routines run on a 60 Hz tick.</summary>
        public const float RoutineTicksPerSecond = 60f;

        /// <summary>Requests that waited longer than this (the actor was off screen) are skipped; their hits are shown at once.</summary>
        public const float StaleActionSeconds = 2.0f;

        /// <summary>Reactions that waited longer than this (the target was off screen) are dropped.</summary>
        public const float StaleReactionSeconds = 1.0f;

        /// <summary>At most this many actions wait behind the playing one; older ones are skipped.</summary>
        public const int MaxQueuedActions = 3;

        /// <summary>A sustained action (a chant) ends by itself after this long if nothing replaces it.</summary>
        public const float MaxSustainedSeconds = 30f;

        /// <summary>Flinch length when the model has no <c>damg</c> routine to say (the retail routines use 24 ticks).</summary>
        public const int DefaultFlinchTicks = 24;

        /// <summary>Pose flash length when the model's guard routine does not say (the retail PC routines use 16 ticks).</summary>
        public const int DefaultPoseFlashTicks = 16;

        public AnimationCategory Current { get; private set; } = AnimationCategory.Idle;
        public byte SubAnimation { get; private set; }
        public byte CurrentStance { get; private set; }

        public float ElapsedSeconds { get; private set; }
        public AnimationClip? CurrentClip { get; private set; }

        public AnimationClip? PreviousClip { get; private set; }
        public float PreviousElapsedSeconds { get; private set; }
        public float BlendWeight { get; private set; } = 1.0f;

        /// <summary>
        /// Whether <see cref="PreviousClip"/> loops while it fades out: a stance clip does, a finished one-shot (an action
        /// clip, a stance transition) holds its last frame.
        /// </summary>
        public bool PreviousClipLoops { get; private set; } = true;
        public float BlendDuration { get; set; } = DefaultBlendDuration;

        public bool IsBlending => PreviousClip != null && BlendWeight < 1.0f;

        public bool IsPlayingTransition { get; private set; }
        public AnimationClip? TransitionClip { get; private set; }

        /// <summary>The motion routine of the action playing now, or null.</summary>
        public MotionRoutine? ActiveRoutine { get; private set; }

        /// <summary>Whether a one-shot action (swing, chant, release, draw) owns the body.</summary>
        public bool IsPlayingAction => ActiveRoutine != null;

        /// <summary>Actions waiting behind the playing one.</summary>
        public int QueuedActionCount => _queuedActions.Count;

        /// <summary>
        /// Whether the renderer should loop <see cref="CurrentClip"/>: stance and locomotion clips loop, stance transitions and
        /// one-shot action clips play once and hold, a looping action step (a chant) loops.
        /// </summary>
        public bool LoopsCurrentClip
        {
            get
            {
                if (IsPlayingTransition) return false;
                if (ActiveRoutine != null) return _segmentIndex >= 0 && ActiveRoutine.Segments[_segmentIndex].Loops > 1;
                return Current != AnimationCategory.Death;
            }
        }

        /// <summary>
        /// Weapon placement the current action asks for: true in the hands, false at rest (hip / back), null to follow the
        /// engaged state. The draw holds the weapon at rest until halfway, the sheathe holds it in the hands until halfway.
        /// </summary>
        public bool? WeaponGripOverride
        {
            get
            {
                if (ActiveRoutine == null || _weaponMotion == WeaponMotion.None) return null;
                bool firstHalf = _actionTicks < ActiveRoutine.TotalTicks * 0.5f;
                return _weaponMotion == WeaponMotion.Draw ? !firstHalf : firstHalf;
            }
        }

        /// <summary>The pose blended over the clips this frame (hit flinch or guard / parry flash), or null.</summary>
        public SkeletonPoseEvaluator.PoseOverlay? Overlay
        {
            get
            {
                if (_overlayClip == null || _overlayDuration <= 0f) return null;
                float w = _overlayPeak * OverlayEnvelope(_overlayElapsed / _overlayDuration);
                return w > 0.001f ? new SkeletonPoseEvaluator.PoseOverlay(_overlayClip, w, _overlayMask, _overlayReference) : null;
            }
        }

        /// <summary>The last cast chant routine the actor played (<c>cabk</c>, <c>cawh</c>...), which names its release.</summary>
        public string LastChantRoutine { get; private set; } = string.Empty;

        /// <summary>Picks among equal candidates (the swing variants); replaceable for tests.</summary>
        public Func<int, int> RandomIndex { get; set; } = Random.Shared.Next;

        private enum WeaponMotion : byte { None, Draw, Sheathe }

        private readonly ConcurrentQueue<ActionRequest> _incomingActions = new();
        private readonly ConcurrentQueue<HitReaction> _incomingReactions = new();
        private readonly List<ActionRequest> _queuedActions = new();

        private EntityModel? _lastModel;
        private float _activeBlendDuration = DefaultBlendDuration;

        private ActionRequest? _actionRequest;
        private float _actionTicks;
        private int _segmentIndex = -1;
        private bool _actionAllowsLocomotion;
        private int _actionHitTick = -1;
        private WeaponMotion _weaponMotion;

        private AnimationClip? _overlayClip;
        private AnimationClip? _overlayReference;
        private bool[]? _overlayMask;
        private float _overlayPeak;
        private float _overlayElapsed;
        private float _overlayDuration;

        /// <summary>Queues an action for the actor (thread-safe; it plays from the next <c>Advance</c> with a model).</summary>
        public void EnqueueAction(ActionRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            _incomingActions.Enqueue(request);
        }

        /// <summary>Queues a hit reaction for the target (thread-safe).</summary>
        public void EnqueueReaction(HitReaction reaction) => _incomingReactions.Enqueue(reaction);

        /// <summary>
        /// Advances playback time by dt using generalized stance resolution and dual-channel blending.
        /// Priority: Death > Locomotion (Walk/Run) > Stance Transitions > Actions > Combat/Idle.
        /// </summary>
        public void Advance(float dt, AnimationCategory newCategory, byte subAnimation, EntityModel? model)
        {
            if (model == null)
            {
                _lastModel = null;
                DropPendingWork();
                AdvanceLegacy(dt, newCategory, subAnimation);
                return;
            }

            long now = Stopwatch.GetTimestamp();
            AcceptIncoming(now);

            byte targetStance = NpcStanceResolver.ResolveEffectiveStance(model, subAnimation);
            bool modelChanged = !ReferenceEquals(model, _lastModel);
            bool categoryChanged = newCategory != Current;
            bool stanceChanged = targetStance != CurrentStance;

            // 1. Check if an interrupt should cancel an in-flight stance transition immediately
            // Locomotion (Walk/Run/Strafe) and Death always interrupt a transition clip
            bool isLocomotion = IsLocomotion(newCategory);
            bool isLocomotionOrDeath = isLocomotion || newCategory == AnimationCategory.Death;
            if (IsPlayingTransition && isLocomotionOrDeath)
            {
                IsPlayingTransition = false;
                TransitionClip = null;
            }

            AdvanceReactions(dt, model, now);

            // An action keeps the body until it ends. Death and a new model end it at once; moving ends a standing action,
            // and stopping ends a moving swing once its hit has landed (so it does not slide in place).
            if (ActiveRoutine != null)
            {
                bool movingSwingStopped = !isLocomotion && _actionAllowsLocomotion && (_actionHitTick < 0 || _actionTicks >= _actionHitTick);
                if (modelChanged || newCategory == AnimationCategory.Death || (isLocomotion && !_actionAllowsLocomotion) || movingSwingStopped)
                {
                    if (modelChanged) _lastModel = model;
                    EndAction(model, blend: !modelChanged);
                }
                else
                {
                    // The stance the action returns to follows the entity meanwhile.
                    Current = newCategory;
                    SubAnimation = subAnimation;
                    CurrentStance = targetStance;
                    AdvanceAction(dt, model);
                    return;
                }
            }

            // 2. Handle model, state or stance shifts
            bool combatFlipped = !modelChanged && categoryChanged && IsCombat(Current) != IsCombat(newCategory);
            if (modelChanged)
            {
                _lastModel = model;
                Current = newCategory;
                SubAnimation = subAnimation;
                CurrentStance = targetStance;
                AnimationClip? targetClip = NpcStanceResolver.ResolveTargetClip(model, newCategory, targetStance);
                CurrentClip = targetClip;
                PreviousClip = null;
                BlendWeight = 1.0f;
                ElapsedSeconds = 0f;
                IsPlayingTransition = false;
                TransitionClip = null;
            }
            else if (categoryChanged || stanceChanged)
            {
                byte fromStance = CurrentStance;
                Current = newCategory;
                SubAnimation = subAnimation;
                CurrentStance = targetStance;

                // Check for dedicated stance transition clip if not interrupted by locomotion/death
                AnimationClip? transClip = null;
                if (stanceChanged && !isLocomotionOrDeath)
                {
                    transClip = NpcStanceResolver.ResolveTransitionClip(model, fromStance, targetStance);
                }

                if (transClip != null)
                {
                    // Start one-shot transition clip
                    StartTransition(transClip);
                }
                else if (combatFlipped && !isLocomotionOrDeath && TryStartWeaponMotion(model, IsCombat(newCategory), now))
                {
                    // Standing still, the weapon draw / sheathe plays, then blends into the new stance.
                    AdvanceAction(dt, model);
                    return;
                }
                else
                {
                    // Continuous clip switch
                    AnimationClip? targetClip = NpcStanceResolver.ResolveTargetClip(model, newCategory, targetStance);
                    SwitchToClip(targetClip, BlendDuration, previousLoops: true);
                }
            }
            else if (CurrentClip == null)
            {
                // First evaluation initialization
                AnimationClip? targetClip = NpcStanceResolver.ResolveTargetClip(model, Current, CurrentStance);
                if (targetClip != null)
                {
                    CurrentClip = targetClip;
                    ElapsedSeconds = 0f;
                    PreviousClip = null;
                    BlendWeight = 1.0f;
                }
            }

            // 3. Start the next action once the body is free (not dying, not changing stance).
            if (!IsPlayingTransition && newCategory != AnimationCategory.Death && TryStartNextAction(model, isLocomotion, now))
            {
                AdvanceAction(dt, model);
                return;
            }

            // 4. Advance active playback
            ElapsedSeconds += dt;
            AdvanceBlend(dt);

            // Advance transition clip completion
            if (IsPlayingTransition && TransitionClip != null)
            {
                float transDuration = TransitionClip.DurationSeconds;
                if (transDuration > 0.001f && ElapsedSeconds >= transDuration)
                {
                    // Transition completed: smoothly cross-fade to target continuous stance clip
                    IsPlayingTransition = false;
                    TransitionClip = null;
                    AnimationClip? targetClip = NpcStanceResolver.ResolveTargetClip(model, Current, CurrentStance);
                    SwitchToClip(targetClip, BlendDuration, previousLoops: false);
                }
            }
        }

        /// <summary>
        /// Legacy advance overload for headless execution or model-less tests.
        /// </summary>
        public void Advance(float dt, AnimationCategory newCategory, byte subAnimation = 0)
        {
            Advance(dt, newCategory, subAnimation, null);
        }

        private void AdvanceLegacy(float dt, AnimationCategory newCategory, byte subAnimation)
        {
            if (newCategory != Current || subAnimation != SubAnimation)
            {
                Current = newCategory;
                SubAnimation = subAnimation;
                CurrentStance = subAnimation;
                ElapsedSeconds = 0f;
                PreviousClip = null;
                BlendWeight = 1.0f;
                IsPlayingTransition = false;
                TransitionClip = null;
                return;
            }

            ElapsedSeconds += dt;
        }

        #region Actions

        private static bool IsLocomotion(AnimationCategory category) => category is AnimationCategory.Walk or AnimationCategory.Run
            or AnimationCategory.CombatWalk or AnimationCategory.CombatRun
            or AnimationCategory.MoveBackward or AnimationCategory.MoveLeft or AnimationCategory.MoveRight
            or AnimationCategory.CombatMoveBackward or AnimationCategory.CombatMoveLeft or AnimationCategory.CombatMoveRight;

        private static bool IsCombat(AnimationCategory category) => category is AnimationCategory.Combat
            or AnimationCategory.CombatWalk or AnimationCategory.CombatRun
            or AnimationCategory.CombatMoveBackward or AnimationCategory.CombatMoveLeft or AnimationCategory.CombatMoveRight;

        private static float Seconds(long fromTimestamp, long toTimestamp) => (float)(toTimestamp - fromTimestamp) / Stopwatch.Frequency;

        private void DropPendingWork()
        {
            // Without a model nothing can play; the queue's fallback shows the hits.
            while (_incomingActions.TryDequeue(out _)) { }
            while (_incomingReactions.TryDequeue(out _)) { }
            _queuedActions.Clear();
            ActiveRoutine = null;
            _actionRequest = null;
            _weaponMotion = WeaponMotion.None;
            _overlayClip = null;
        }

        private void AcceptIncoming(long now)
        {
            while (_incomingActions.TryDequeue(out var request))
            {
                request.MarkAccepted(now);
                if (request.Motion == ActionMotion.Interrupt)
                {
                    // An interrupted chant or item use stops; queued work stays.
                    if (ActiveRoutine is { IsSustained: true } && _lastModel != null) EndAction(_lastModel, blend: true);
                    request.DeliverHits();
                    continue;
                }

                // A sustained action (a chant) gives way at once to whatever comes next (its release, a swing).
                if (ActiveRoutine is { IsSustained: true } && _queuedActions.Count == 0 && _lastModel != null)
                {
                    EndAction(_lastModel, blend: true);
                }

                _queuedActions.Add(request);
                while (_queuedActions.Count > MaxQueuedActions)
                {
                    _queuedActions[0].DeliverHits();
                    _queuedActions.RemoveAt(0);
                }
            }
        }

        private bool TryStartNextAction(EntityModel model, bool isMoving, long now)
        {
            while (_queuedActions.Count > 0)
            {
                var request = _queuedActions[0];
                _queuedActions.RemoveAt(0);

                if (Seconds(request.ReceivedTimestamp, now) > StaleActionSeconds)
                {
                    request.DeliverHits();
                    continue;
                }

                var (routine, allowsLocomotion) = ResolveRoutine(model, request, isMoving);
                if (routine == null || routine.Segments.Count == 0)
                {
                    // Nothing to show on the body: the result still lands.
                    request.MarkStarted(now, 0);
                    request.DeliverHits();
                    continue;
                }

                StartAction(model, request, routine, allowsLocomotion, WeaponMotion.None, now);
                return true;
            }
            return false;
        }

        private bool TryStartWeaponMotion(EntityModel model, bool drawing, long now)
        {
            if (_queuedActions.Count > 0) return false; // a queued swing takes the weapon out itself
            string name = drawing ? "out0" : "in 0";
            if (!model.MotionRoutines.TryGetValue(name, out var routine) || routine.Segments.Count == 0) return false;
            StartAction(model, null, routine, allowsLocomotion: false, drawing ? WeaponMotion.Draw : WeaponMotion.Sheathe, now);
            return true;
        }

        private void StartAction(EntityModel model, ActionRequest? request, MotionRoutine routine, bool allowsLocomotion, WeaponMotion weaponMotion, long now)
        {
            IsPlayingTransition = false;
            TransitionClip = null;
            ActiveRoutine = routine;
            _actionRequest = request;
            _actionTicks = 0f;
            _segmentIndex = -1;
            _actionAllowsLocomotion = allowsLocomotion;
            _weaponMotion = weaponMotion;
            _actionHitTick = routine.HitTicks.Count > 0 ? routine.HitTicks[0] : DefaultHitTick(request, routine);
            if (routine.IsSustained && routine.Name.StartsWith("ca", StringComparison.Ordinal)) LastChantRoutine = routine.Name;
            request?.MarkStarted(now, _actionHitTick);
            EnterSegment(model, 0);
        }

        /// <summary>
        /// When a routine names no hit (a release, an item use), its result shows partway through: where its second clip step
        /// starts (the release burst after the wind-up), else 36 ticks in.
        /// </summary>
        private static int DefaultHitTick(ActionRequest? request, MotionRoutine routine)
        {
            if (request == null || request.Hits.Count == 0) return -1;
            if (routine.Segments.Count > 1) return routine.Segments[1].StartTick;
            return Math.Min(routine.TotalTicks, 36);
        }

        private void EnterSegment(EntityModel model, int index)
        {
            bool previousLoops = LoopsCurrentClip;
            _segmentIndex = index;
            var segment = ActiveRoutine!.Segments[index];
            if (!model.Animations.TryGetValue(segment.ClipName, out var clip)) return;
            SwitchToClip(clip, segment.BlendInTicks / RoutineTicksPerSecond, previousLoops, restart: true);
        }

        private void AdvanceAction(float dt, EntityModel model)
        {
            var routine = ActiveRoutine!;
            _actionTicks += dt * RoutineTicksPerSecond;

            // Step to the segment the routine clock has reached.
            int next = _segmentIndex + 1;
            while (next < routine.Segments.Count && routine.Segments[next].StartTick <= _actionTicks)
            {
                EnterSegment(model, next);
                next++;
            }

            if (_actionRequest is { HitsDelivered: false } && _actionHitTick >= 0 && _actionTicks >= _actionHitTick)
            {
                _actionRequest.DeliverHits();
            }

            var segment = routine.Segments[Math.Max(0, _segmentIndex)];
            float clipSeconds = (_actionTicks - segment.StartTick) / RoutineTicksPerSecond * segment.Speed;
            if (segment.Loops <= 1 && CurrentClip != null)
            {
                clipSeconds = Math.Min(clipSeconds, CurrentClip.DurationSeconds); // play once, hold the last frame
            }
            ElapsedSeconds = Math.Max(0f, clipSeconds);
            AdvanceBlend(dt);

            if (_actionTicks >= RoutineEndTick(routine))
            {
                EndAction(model, blend: true);
            }
        }

        /// <summary>The tick an action ends at: its routine's length, or never (until replaced) for a sustained chant.</summary>
        private static float RoutineEndTick(MotionRoutine routine)
        {
            if (routine.IsSustained) return MaxSustainedSeconds * RoutineTicksPerSecond;
            var last = routine.Segments[^1];
            return Math.Max(routine.TotalTicks, last.StartTick + Math.Max(last.DurationTicks, 1));
        }

        private void EndAction(EntityModel model, bool blend)
        {
            var routine = ActiveRoutine;
            var request = _actionRequest;
            bool actionClipLoops = LoopsCurrentClip;
            ActiveRoutine = null;
            _actionRequest = null;
            _segmentIndex = -1;
            _weaponMotion = WeaponMotion.None;
            request?.DeliverHits();

            float blendOut = routine != null && routine.Segments.Count > 0
                ? Math.Max(routine.Segments[^1].BlendOutTicks / RoutineTicksPerSecond, 0.1f)
                : BlendDuration;
            AnimationClip? stanceClip = NpcStanceResolver.ResolveTargetClip(model, Current, CurrentStance);
            if (blend)
            {
                SwitchToClip(stanceClip, blendOut, actionClipLoops, restart: true);
            }
            else if (stanceClip != null)
            {
                CurrentClip = stanceClip;
                ElapsedSeconds = 0f;
                PreviousClip = null;
                BlendWeight = 1.0f;
            }
        }

        /// <summary>
        /// Picks the routine an action plays on this model, and whether it may keep playing while the actor moves (the moving
        /// swings are authored for it).
        /// </summary>
        internal (MotionRoutine? Routine, bool AllowsLocomotion) ResolveRoutine(EntityModel model, ActionRequest request, bool isMoving)
        {
            var routines = model.MotionRoutines;
            switch (request.Motion)
            {
                case ActionMotion.Swing:
                    return ResolveSwing(model, "ati", "atf0", "atb0", "atl0", "atr0", isMoving);

                case ActionMotion.Counter:
                    var counter = ResolveSwing(model, "cni", "cnf0", "cnb0", "cnl0", "cnr0", isMoving);
                    return counter.Routine != null ? counter : ResolveSwing(model, "ati", "atf0", "atb0", "atl0", "atr0", isMoving);

                case ActionMotion.Routine:
                    if (routines.TryGetValue(request.Routine, out var named)) return (named, false);
                    // A monster without the specific chant uses its generic one.
                    if (request.Routine.StartsWith("ca", StringComparison.Ordinal) && routines.TryGetValue("cast", out var cast)) return (cast, false);
                    return (null, false);

                case ActionMotion.CastRelease:
                    string suffix = LastChantRoutine.Length == 4 ? LastChantRoutine[2..] : "bk";
                    if (routines.TryGetValue("sh" + suffix, out var release)) return (release, false);
                    if (routines.TryGetValue("shot", out var shot)) return (shot, false);
                    return (routines.TryGetValue("shbk", out var fallback) ? fallback : null, false);

                case ActionMotion.Ability:
                    return (SingleClipRoutine(model, "cm0", "cm0"), false);

                default:
                    return (null, false);
            }
        }

        private (MotionRoutine? Routine, bool AllowsLocomotion) ResolveSwing(EntityModel model, string standingPrefix,
            string forward, string back, string left, string right, bool isMoving)
        {
            var routines = model.MotionRoutines;
            if (isMoving)
            {
                string name = Current switch
                {
                    AnimationCategory.MoveBackward or AnimationCategory.CombatMoveBackward => back,
                    AnimationCategory.MoveLeft or AnimationCategory.CombatMoveLeft => left,
                    AnimationCategory.MoveRight or AnimationCategory.CombatMoveRight => right,
                    _ => forward
                };
                // No moving swing on this model: nothing plays (a standing swing would slide), the hit still lands.
                return routines.TryGetValue(name, out var moving) && moving.Segments.Count > 0 ? (moving, true) : (null, true);
            }

            Span<int> found = stackalloc int[10];
            int count = 0;
            for (int i = 0; i < 10; i++)
            {
                if (routines.TryGetValue(standingPrefix + (char)('0' + i), out var r) && r.Segments.Count > 0) found[count++] = i;
            }
            if (count > 0)
            {
                // Every weapon carries several standing swings; retail shows them in no fixed order.
                int pick = found[Math.Clamp(RandomIndex(count), 0, count - 1)];
                return (routines[standingPrefix + (char)('0' + pick)], false);
            }

            // Models without swing routines: their raw swing clips, if any.
            for (int i = 0; i < 3; i++)
            {
                if (model.Animations.ContainsKey("at" + i)) return (SingleClipRoutine(model, standingPrefix + i, "at" + i), false);
            }
            return (null, false);
        }

        private static MotionRoutine? SingleClipRoutine(EntityModel model, string name, string clipName)
        {
            if (!model.Animations.TryGetValue(clipName, out var clip)) return null;
            int ticks = Math.Max(1, (int)MathF.Round(clip.DurationSeconds * RoutineTicksPerSecond));
            return new MotionRoutine
            {
                Name = name,
                TotalTicks = ticks,
                Segments = [new MotionSegment(clipName, 0, ticks, 10, 20, 1, 1f)]
            };
        }

        #endregion

        #region Reactions

        private void AdvanceReactions(float dt, EntityModel model, long now)
        {
            while (_incomingReactions.TryDequeue(out var reaction))
            {
                if (Seconds(reaction.Timestamp, now) > StaleReactionSeconds) continue;
                ApplyReaction(model, reaction, now);
            }

            if (_overlayClip != null)
            {
                _overlayElapsed += dt;
                if (_overlayElapsed >= _overlayDuration) _overlayClip = null;
            }
        }

        private void ApplyReaction(EntityModel model, HitReaction reaction, long now)
        {
            if (reaction.ReactKind == ActionReactKind.Counter)
            {
                // The target strikes back.
                _queuedActions.Add(new ActionRequest { Motion = ActionMotion.Counter, ReceivedTimestamp = now });
            }

            switch (reaction.Resolution)
            {
                case ActionResolution.Hit when reaction.Distortion > 0f:
                    int flinchTicks = model.MotionRoutines.TryGetValue("damg", out var damg) && damg.FlinchTicks > 0 ? damg.FlinchTicks : DefaultFlinchTicks;
                    StartOverlay(model, reaction.FromFront ? "dfm" : "dbm", reaction.Distortion, flinchTicks);
                    break;

                case ActionResolution.Guard:
                    StartPoseRoutine(model, "gurd", "gurd");
                    break;

                case ActionResolution.Parry:
                    StartPoseRoutine(model, "pary", "gurd");
                    break;

                case ActionResolution.Block:
                    StartPoseRoutine(model, "gur1", "gurd");
                    break;
            }
        }

        /// <summary>
        /// Plays a guard / parry / block routine as a pose flash: the PC routines carry op 0x5A (pose 0 = guard <c>gdm</c>,
        /// 1 = parry <c>pym</c>), the monster ones a two-tick guard pose clip that blends out over its blend-out time.
        /// </summary>
        private void StartPoseRoutine(EntityModel model, string name, string fallbackName)
        {
            if (!model.MotionRoutines.TryGetValue(name, out var routine) && !model.MotionRoutines.TryGetValue(fallbackName, out routine)) return;

            if (routine.PoseFlashIndex >= 0)
            {
                string pose = routine.PoseFlashIndex == 1 ? "pym" : "gdm";
                StartOverlay(model, pose, 1f, routine.PoseFlashTicks > 0 ? routine.PoseFlashTicks : DefaultPoseFlashTicks);
            }
            else if (routine.Segments.Count == 1)
            {
                var segment = routine.Segments[0];
                StartOverlay(model, segment.ClipName, 1f, Math.Max(segment.BlendOutTicks + segment.DurationTicks, 8));
            }
        }

        private void StartOverlay(EntityModel model, string clipName, float peak, int ticks)
        {
            if (!model.Animations.TryGetValue(clipName, out var clip) || ticks <= 0) return;
            _overlayClip = clip;
            _overlayReference = ReferencePose(model, clipName);
            _overlayMask = OverlayMask(model, clip);
            _overlayPeak = Math.Clamp(peak, 0f, 1f);
            _overlayElapsed = 0f;
            _overlayDuration = ticks / RoutineTicksPerSecond;
        }

        /// <summary>
        /// The neutral pose a reaction pose is authored against: the damage poses <c>dfm</c> / <c>dbm</c> and the guard pose
        /// <c>gdm</c> each have an <c>i</c> twin (<c>dfi</c>, <c>dbi</c>, <c>gdi</c>; <c>dfi6</c> etc. on the PC motion
        /// packs) that differs from them only on the joints the reaction bends, so the reaction is that difference added
        /// on top of whatever the actor is doing. A pose without one (a monster's guard clip) is blended absolutely, as the
        /// routine that plays it as a clip would.
        /// </summary>
        private static AnimationClip? ReferencePose(EntityModel model, string poseName)
        {
            if (poseName.Length != 3 || poseName[2] != 'm') return null;
            string stem = poseName[..2];
            foreach (var name in (ReadOnlySpan<string>)[stem + "i", stem + "i6", stem + "i0", "dfi", "dfi6", "dfi0"])
            {
                if (model.Animations.TryGetValue(name, out var reference)) return reference;
            }
            return null;
        }

        /// <summary>
        /// The joints a pose may move: every joint its clip keys, except those the loader grafted in from the idle clip (a PC
        /// battle-pack pose carries the idle's tracks for the joints it does not drive).
        /// </summary>
        private static bool[]? OverlayMask(EntityModel model, AnimationClip clip)
        {
            if (!model.Animations.TryGetValue("idl", out var idle) || ReferenceEquals(idle, clip)) return null;
            int max = 0;
            foreach (var joint in clip.Tracks.Keys) max = Math.Max(max, joint + 1);
            var mask = new bool[max];
            foreach (var (joint, track) in clip.Tracks)
            {
                mask[joint] = !(idle.Tracks.TryGetValue(joint, out var idleTrack) && ReferenceEquals(idleTrack, track));
            }
            return mask;
        }

        /// <summary>The overlay weight over its life (0-1): it rises over the first quarter and eases back over the rest.</summary>
        internal static float OverlayEnvelope(float t)
        {
            if (t <= 0f || t >= 1f) return 0f;
            float x = t < 0.25f ? t / 0.25f : 1f - ((t - 0.25f) / 0.75f);
            return x * x * (3f - (2f * x)); // smoothstep
        }

        #endregion

        private void StartTransition(AnimationClip transClip)
        {
            IsPlayingTransition = true;
            TransitionClip = transClip;
            _activeBlendDuration = BlendDuration;

            // Initiate smooth blend into transition clip from whatever clip was previously playing
            if (CurrentClip != null && CurrentClip != transClip)
            {
                PreviousClipLoops = true;
                PreviousClip = CurrentClip;
                PreviousElapsedSeconds = ElapsedSeconds;
                BlendWeight = 0f;
            }
            else
            {
                PreviousClip = null;
                BlendWeight = 1.0f;
            }

            CurrentClip = transClip;
            ElapsedSeconds = 0f;
        }

        private void SwitchToClip(AnimationClip? targetClip, float blendSeconds, bool previousLoops, bool restart = false)
        {
            if (targetClip == null) return;

            if (CurrentClip != null && (CurrentClip != targetClip || restart) && blendSeconds > 0.001f)
            {
                PreviousClipLoops = previousLoops;
                PreviousClip = CurrentClip;
                PreviousElapsedSeconds = ElapsedSeconds;
                BlendWeight = 0f;
                _activeBlendDuration = blendSeconds;
            }
            else if (blendSeconds <= 0.001f)
            {
                PreviousClip = null;
                BlendWeight = 1.0f;
            }

            CurrentClip = targetClip;
            ElapsedSeconds = 0f;
        }

        private void AdvanceBlend(float dt)
        {
            if (!IsBlending) return;
            PreviousElapsedSeconds += dt;
            float duration = _activeBlendDuration > 0.001f ? _activeBlendDuration : DefaultBlendDuration;
            BlendWeight = Math.Clamp(BlendWeight + (dt / duration), 0f, 1f);
            if (BlendWeight >= 1.0f)
            {
                PreviousClip = null;
            }
        }
    }
}
