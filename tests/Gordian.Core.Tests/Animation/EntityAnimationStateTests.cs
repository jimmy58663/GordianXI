// tests/Gordian.Core.Tests/Animation/EntityAnimationStateTests.cs
using Gordian.Core.Animation;
using Xunit;

namespace Gordian.Core.Tests.Animation
{
    public class EntityAnimationStateTests
    {
        [Fact]
        public void Advance_StartsAtIdleWithZeroElapsed()
        {
            var state = new EntityAnimationState();
            Assert.Equal(AnimationCategory.Idle, state.Current);
            Assert.Equal(0f, state.ElapsedSeconds);
        }

        [Fact]
        public void Advance_SameCategory_AccumulatesElapsedSeconds()
        {
            var state = new EntityAnimationState();
            state.Advance(0.5f, AnimationCategory.Walk); // transition into Walk: resets to 0
            state.Advance(0.25f, AnimationCategory.Walk);

            Assert.Equal(AnimationCategory.Walk, state.Current);
            Assert.Equal(0.25f, state.ElapsedSeconds, 3);
        }

        [Fact]
        public void Advance_CategoryChange_ResetsElapsedSecondsToZero()
        {
            var state = new EntityAnimationState();

            // First transition into Run: dt is discarded on the very frame the category changes,
            // so playback starts cleanly at frame 0 rather than mid-clip.
            state.Advance(2.0f, AnimationCategory.Run);
            Assert.Equal(AnimationCategory.Run, state.Current);
            Assert.Equal(0f, state.ElapsedSeconds);

            state.Advance(0.3f, AnimationCategory.Run);
            Assert.Equal(0.3f, state.ElapsedSeconds, 3);

            // Switching to Combat resets again.
            state.Advance(0.1f, AnimationCategory.Combat);

            Assert.Equal(AnimationCategory.Combat, state.Current);
            Assert.Equal(0f, state.ElapsedSeconds);
        }
    }
}
