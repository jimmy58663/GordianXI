// src/Gordian.Core/Animation/AnimationCategory.cs
namespace Gordian.Core.Animation
{
    /// <summary>
    /// High-level locomotion/combat animation categories driven by live WorldEntity state.
    /// </summary>
    public enum AnimationCategory
    {
        Idle,
        Walk,
        Run,
        Combat,
        Death,
        CombatWalk,
        CombatRun,
        MoveBackward,
        MoveLeft,
        MoveRight,
        CombatMoveBackward,
        CombatMoveLeft,
        CombatMoveRight,

        /// <summary>Resting with /heal (server status 33): sat down in the healing pose (<see cref="RestPosture"/>).</summary>
        Healing,

        /// <summary>Sitting with /sit (server status 47): sat on the ground (<see cref="RestPosture"/>).</summary>
        Sitting
    }
}
