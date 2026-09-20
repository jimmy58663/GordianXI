// src/Gordian.Core/Animation/EntityAnimationState.cs
using System;
using Gordian.Core.Resources.Models;

namespace Gordian.Core.Animation
{
    /// <summary>
    /// Per-entity animation playback state: multi-channel cross-fading, stance transitions,
    /// and continuous clip advancement. Lives on WorldEntity so its lifetime matches the entity's own.
    /// Clean-room implementation referencing FFXI animation blending specifications in xi-model-viewer (https://github.com/vekien/xi-model-viewer).
    /// </summary>
    public sealed class EntityAnimationState
    {
        public const float DefaultBlendDuration = 0.18f; // 180ms blend window

        public AnimationCategory Current { get; private set; } = AnimationCategory.Idle;
        public byte SubAnimation { get; private set; }
        public byte CurrentStance { get; private set; }

        public float ElapsedSeconds { get; private set; }
        public AnimationClip? CurrentClip { get; private set; }

        public AnimationClip? PreviousClip { get; private set; }
        public float PreviousElapsedSeconds { get; private set; }
        public float BlendWeight { get; private set; } = 1.0f;
        public float BlendDuration { get; set; } = DefaultBlendDuration;

        public bool IsBlending => PreviousClip != null && BlendWeight < 1.0f;

        public bool IsPlayingTransition { get; private set; }
        public AnimationClip? TransitionClip { get; private set; }

        /// <summary>
        /// Advances playback time by dt using generalized stance resolution and dual-channel blending.
        /// Priority: Death > Locomotion (Walk/Run) > Stance Transitions > Combat/Idle.
        /// </summary>
        public void Advance(float dt, AnimationCategory newCategory, byte subAnimation, EntityModel? model)
        {
            if (model == null)
            {
                AdvanceLegacy(dt, newCategory, subAnimation);
                return;
            }

            byte targetStance = NpcStanceResolver.ResolveEffectiveStance(model, subAnimation);
            bool categoryChanged = newCategory != Current;
            bool stanceChanged = targetStance != CurrentStance;

            // 1. Check if an interrupt should cancel an in-flight stance transition immediately
            // Locomotion (Walk/Run) and Death always interrupt a transition clip
            bool isLocomotionOrDeath = newCategory is AnimationCategory.Walk or AnimationCategory.Run or AnimationCategory.Death;
            if (IsPlayingTransition && isLocomotionOrDeath)
            {
                IsPlayingTransition = false;
                TransitionClip = null;
            }

            // 2. Handle state or stance shifts
            if (categoryChanged || stanceChanged)
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
                else
                {
                    // Continuous clip switch
                    AnimationClip? targetClip = NpcStanceResolver.ResolveTargetClip(model, newCategory, targetStance);
                    SwitchToClip(targetClip);
                }
            }
            else if (CurrentClip == null)
            {
                // First evaluation initialization
                AnimationClip? targetClip = NpcStanceResolver.ResolveTargetClip(model, Current, CurrentStance);
                if (targetClip != null)
                {
                    CurrentClip = targetClip;
                }
            }

            // 3. Advance active playback
            ElapsedSeconds += dt;

            // Advance cross-fade blend weight
            if (IsBlending)
            {
                PreviousElapsedSeconds += dt;
                float duration = BlendDuration > 0.001f ? BlendDuration : DefaultBlendDuration;
                BlendWeight = Math.Clamp(BlendWeight + (dt / duration), 0f, 1f);
                if (BlendWeight >= 1.0f)
                {
                    PreviousClip = null;
                }
            }

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
                    SwitchToClip(targetClip);
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

        private void StartTransition(AnimationClip transClip)
        {
            IsPlayingTransition = true;
            TransitionClip = transClip;

            // Initiate smooth blend into transition clip from whatever clip was previously playing
            if (CurrentClip != null && CurrentClip != transClip)
            {
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

        private void SwitchToClip(AnimationClip? targetClip)
        {
            if (targetClip == null) return;

            if (CurrentClip != null && CurrentClip != targetClip)
            {
                PreviousClip = CurrentClip;
                PreviousElapsedSeconds = ElapsedSeconds;
                BlendWeight = 0f;
            }

            CurrentClip = targetClip;
            ElapsedSeconds = 0f;
        }
    }
}
