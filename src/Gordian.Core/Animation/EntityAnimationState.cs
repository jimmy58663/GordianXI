// src/Gordian.Core/Animation/EntityAnimationState.cs
namespace Gordian.Core.Animation
{
    /// <summary>
    /// Per-entity animation playback state: which category is currently active and how far
    /// into that category's clip playback has advanced. Lives on WorldEntity so its lifetime
    /// matches the entity's own (no separate cleanup needed).
    /// </summary>
    public sealed class EntityAnimationState
    {
        public AnimationCategory Current { get; private set; } = AnimationCategory.Idle;
        public float ElapsedSeconds { get; private set; }

        /// <summary>
        /// Advances playback time by dt. Switching to a new category resets ElapsedSeconds to 0
        /// so the new clip starts from its first frame instead of continuing at the old offset.
        /// </summary>
        public void Advance(float dt, AnimationCategory newCategory)
        {
            if (newCategory != Current)
            {
                Current = newCategory;
                ElapsedSeconds = 0f;
                return;
            }

            ElapsedSeconds += dt;
        }
    }
}
