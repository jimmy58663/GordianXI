// tests/Gordian.App.Tests/Audio/FootstepTrackerTests.cs
using Gordian.Core.Animation;
using Xunit;

namespace Gordian.App.Tests.Audio
{
    /// <summary>Foot landing detection from the gait clip's phase (#40), the footfall hook the step sounds and footprints share.</summary>
    public class FootstepTrackerTests
    {
        [Theory]
        [InlineData(-1f, 0.1f, false)] // first frame seen
        [InlineData(0.1f, 0.3f, false)]
        [InlineData(0.4f, 0.55f, true)] // second foot
        [InlineData(0.9f, 0.05f, true)] // wrapped: first foot
        [InlineData(0.6f, 0.8f, false)]
        public void CrossedLanding_AtTheStartAndMiddleOfTheCycle(float previous, float current, bool expected) =>
            Assert.Equal(expected, FootfallDetector.CrossedLanding(previous, current));

        [Fact]
        public void Gaits()
        {
            Assert.True(FootfallDetector.IsStepping(AnimationCategory.Run));
            Assert.True(FootfallDetector.IsStepping(AnimationCategory.CombatMoveLeft));
            Assert.False(FootfallDetector.IsStepping(AnimationCategory.Idle));
            Assert.True(FootfallDetector.IsRunning(AnimationCategory.CombatRun));
            Assert.False(FootfallDetector.IsRunning(AnimationCategory.Walk));
        }
    }
}
