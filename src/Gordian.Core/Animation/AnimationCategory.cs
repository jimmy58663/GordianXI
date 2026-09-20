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
        Death
    }
}
