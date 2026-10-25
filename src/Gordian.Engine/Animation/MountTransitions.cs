// src/Gordian.Engine/Animation/MountTransitions.cs
using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Gordian.Core.Animation;
using Gordian.Core.World;

namespace Gordian.Engine.Animation
{
    /// <summary>
    /// What a player's mount looks like this frame (#437): the mount to draw (the one ridden, or the one leaving after a
    /// dismount), whether the rider is still drawn from its riding pack, and how far the leaving mount has run off and how
    /// opaque it still is.
    /// </summary>
    /// <param name="Mount">The mount to draw; <see cref="MountAppearance.None"/> for nothing.</param>
    /// <param name="RiderSeated">The rider plays its riding pack (mounted, or a chocobo dismount's <c>coff</c>).</param>
    /// <param name="Departing">The mount is leaving: drawn on its own, running off.</param>
    /// <param name="ForwardYalms">How far ahead of the rider a leaving mount is.</param>
    /// <param name="Alpha">The leaving mount's opacity (1 opaque, 0 gone).</param>
    public readonly record struct MountFrame(MountAppearance Mount, bool RiderSeated, bool Departing = false, float ForwardYalms = 0f, float Alpha = 1f)
    {
        public static readonly MountFrame None = new(MountAppearance.None, false);
    }

    /// <summary>
    /// Plays a player's mount-up and dismount (#437) from the riding routines in the retail data, and keeps a dismounted
    /// player's mount drawn while it leaves.
    /// <list type="bullet">
    /// <item><b>Mount-up</b> (a chocobo): the rider plays its pack's <c>chor</c> and the chocobo its own <c>chor</c>, both
    /// <c>chh</c> for 168 ticks (<c>ROM/90/14</c> <c>hm_c/chor</c>, <c>ROM/169/11</c> <c>cyl_/chor</c>). <c>chh</c> starts and
    /// ends seated (Hume male hips 1.78 yalms at both ends, 1.40 mid-flourish), so nothing pops. Other mounts carry no
    /// <c>chor</c> (the raptor, file 102706, and the mount rider pack <c>ROM/351/61</c> have only <c>coff</c> / <c>cdam</c>).</item>
    /// <item><b>Dismount, chocobo</b>: the rider plays <c>coff</c> (<c>ch1</c> 104 ticks, then <c>ch2</c> 72, 180 in all), which
    /// ends standing at the rider's origin (Hume male hips 1.04, 0.06 behind). The chocobo's <c>coff</c> plays <c>ch1</c> 0-104,
    /// <c>run</c> from 100, op 0x6E from tick 102 for 72 ticks with 3.0, and op 0x29 to alpha 0 from tick 150 over 12.</item>
    /// <item><b>Dismount, other mounts</b>: the rider's <c>coff</c> only stands idle (<c>idl</c>, 80 ticks), so it is on foot at
    /// once; the mount's <c>moun/coff</c> plays <c>run</c>, op 0x6E from tick 2 for 72 ticks with 10.0, and fades over 50-74.</item>
    /// </list>
    /// *Inference*: op 0x6E is read as moving the actor forward by its float in yalms over its duration; 10 yalms in 72 ticks
    /// is 8.3 yalms/s, the mounted speed the retail capture measures (Mounts.mp4: 7.8-8.2). No reference names the op.
    /// <para>
    /// A player first seen already mounted (zone-in, coming into range) plays nothing. Shared by every viewport and timed in
    /// wall time, so two windows drawing one world play each transition once.
    /// </para>
    /// </summary>
    public static class MountTransitions
    {
        /// <summary>The mount-up routine (chocobo and its rider).</summary>
        public const string MountUpRoutine = "chor";

        /// <summary>The dismount routine (every mount and rider pack).</summary>
        public const string DismountRoutine = "coff";

        /// <summary>The chocobo rider's <c>coff</c>: <c>ch1</c> 104 ticks then <c>ch2</c> 72.</summary>
        public const int ChocoboRiderDismountTicks = 180;

        /// <summary>How a leaving mount moves and fades (60 Hz ticks, yalms), read from its <c>coff</c>.</summary>
        public readonly record struct DepartProfile(int TotalTicks, int MoveStart, int MoveTicks, float MoveYalms, int FadeStart, int FadeTicks);

        /// <summary>The chocobo's <c>cyl_/coff</c> (<c>ROM/169/11</c>).</summary>
        public static readonly DepartProfile Chocobo = new(174, 102, 72, 3.0f, 150, 12);

        /// <summary>Every other mount's <c>moun/coff</c> (checked on the raptor, file 102706).</summary>
        public static readonly DepartProfile OtherMount = new(74, 2, 72, 10.0f, 50, 24);

        private sealed class State
        {
            public MountAppearance Last;
            public MountAppearance Departing;
            public long DepartStart;
            public bool RiderLeft;
        }

        private static readonly ConditionalWeakTable<WorldEntity, State> States = new();

        /// <summary>
        /// The mount to draw for <paramref name="entity"/> this frame, starting a mount-up or dismount when its mount changed
        /// since the last call. <paramref name="moving"/> ends a chocobo rider's dismount pose early (it walks off on foot).
        /// </summary>
        public static MountFrame Observe(WorldEntity entity, bool moving) => Observe(entity, moving, Stopwatch.GetTimestamp());

        internal static MountFrame Observe(WorldEntity entity, bool moving, long now)
        {
            ArgumentNullException.ThrowIfNull(entity);
            var current = entity.Mount;
            if (!States.TryGetValue(entity, out var state))
            {
                States.AddOrUpdate(entity, new State { Last = current });
                return current.IsMounted ? new MountFrame(current, true) : MountFrame.None;
            }

            lock (state)
            {
                if (current.IsMounted != state.Last.IsMounted)
                {
                    if (current.IsMounted) StartMountUp(entity, current, state);
                    else StartDismount(entity, state.Last, state, now);
                }
                state.Last = current;

                if (current.IsMounted) return new MountFrame(current, true);
                if (!state.Departing.IsMounted) return MountFrame.None;

                float ticks = (float)((now - state.DepartStart) / (double)Stopwatch.Frequency * EntityAnimationState.RoutineTicksPerSecond);
                var profile = state.Departing.RidesChocobo ? Chocobo : OtherMount;
                bool riderSeated = state.Departing.RidesChocobo && !state.RiderLeft && ticks < ChocoboRiderDismountTicks;
                if (riderSeated && moving)
                {
                    // Walking off ends the rider's dismount pose: its clips belong to the riding pack, not the body on foot.
                    state.RiderLeft = true;
                    riderSeated = false;
                    entity.Animation.EnqueueAction(new ActionRequest
                    {
                        ActorId = entity.ServerId,
                        Motion = ActionMotion.EventMotionStop,
                        Routine = DismountRoutine,
                        ReceivedTimestamp = Stopwatch.GetTimestamp(),
                    });
                }
                if (ticks >= profile.TotalTicks && !riderSeated)
                {
                    state.Departing = MountAppearance.None;
                    return MountFrame.None;
                }
                return new MountFrame(state.Departing, riderSeated, Departing: true, Forward(profile, ticks), Alpha(profile, ticks));
            }
        }

        /// <summary>How far ahead of its rider a leaving mount is <paramref name="ticks"/> into its <c>coff</c>.</summary>
        public static float Forward(in DepartProfile profile, float ticks) =>
            profile.MoveYalms * Math.Clamp((ticks - profile.MoveStart) / profile.MoveTicks, 0f, 1f);

        /// <summary>A leaving mount's opacity <paramref name="ticks"/> into its <c>coff</c>.</summary>
        public static float Alpha(in DepartProfile profile, float ticks) =>
            ticks >= profile.TotalTicks ? 0f : 1f - Math.Clamp((ticks - profile.FadeStart) / profile.FadeTicks, 0f, 1f);

        private static void StartMountUp(WorldEntity entity, in MountAppearance mount, State state)
        {
            state.Departing = MountAppearance.None;
            if (!mount.RidesChocobo) return;
            Play(entity.Animation, entity.ServerId, MountUpRoutine);
            Play(entity.MountAnimation, entity.ServerId, MountUpRoutine);
        }

        private static void StartDismount(WorldEntity entity, in MountAppearance mount, State state, long now)
        {
            state.Departing = mount;
            state.DepartStart = now;
            state.RiderLeft = false;
            if (mount.RidesChocobo) Play(entity.Animation, entity.ServerId, DismountRoutine);
            Play(entity.MountAnimation, entity.ServerId, DismountRoutine);
        }

        private static void Play(EntityAnimationState animation, uint serverId, string routine) =>
            animation.EnqueueAction(new ActionRequest
            {
                ActorId = serverId,
                Motion = ActionMotion.EventMotion,
                Routine = routine,
                TargetId = serverId,
                ReceivedTimestamp = Stopwatch.GetTimestamp(),
            });
    }
}
