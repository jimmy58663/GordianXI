// tests/Gordian.App.Tests/Graphics/EventPoseSmootherTests.cs
using System;
using System.Numerics;
using Gordian.App.Graphics;
using Gordian.Core.World;
using Xunit;

namespace Gordian.App.Tests.Graphics
{
    public class EventPoseSmootherTests
    {
        /// <summary>Without a turn speed a turn eases: the first 60 Hz frame closes 8/60 of the angle.</summary>
        [Fact]
        public void Turn_EasesByDefault()
        {
            var smoother = new EventPoseSmoother(Vector3.Zero, 0f);
            smoother.Advance(new EventPose(Vector3.Zero, MathF.PI / 2, 0f), 1f / 60f);
            Assert.Equal(MathF.PI / 2 * 8f / 60f, smoother.Heading, 4);
        }

        /// <summary>
        /// Round 2 of #334 (Atelloune, Southern San d'Oria): an NPC whose first event pose only turns it to face the player
        /// starts from its drawn heading and eases round, instead of taking the new heading on the first frame.
        /// </summary>
        [Fact]
        public void Start_TurnInPlaceEasesFromTheDrawnHeading()
        {
            var at = new Vector3(123.38f, 0f, 84.67f);
            var smoother = EventPoseSmoother.Start(at, 0f, new EventPose(at + new Vector3(0.2f, 0f, 0f), MathF.PI / 2, 0f));
            Assert.Equal(0f, smoother.Heading);
            Assert.Equal(at + new Vector3(0.2f, 0f, 0f), smoother.Position);
            smoother.Advance(new EventPose(smoother.Position, MathF.PI / 2, 0f), 1f / 60f);
            Assert.Equal(MathF.PI / 2 * 8f / 60f, smoother.Heading, 4);
        }

        /// <summary>A first pose that places the entity elsewhere takes its heading at once, as its position.</summary>
        [Fact]
        public void Start_PlacementTakesTheHeadingAtOnce()
        {
            var smoother = EventPoseSmoother.Start(Vector3.Zero, 0f, new EventPose(new Vector3(10f, 0f, 0f), MathF.PI / 2, 0f));
            Assert.Equal(MathF.PI / 2, smoother.Heading);
            Assert.Equal(new Vector3(10f, 0f, 0f), smoother.Position);
        }

        /// <summary>
        /// A turn speed from 0x59 sub 0 / 1 (#197; 4096ths of a turn per 60 Hz frame) turns at that constant rate: 256 a frame
        /// covers a quarter turn in 4 frames and stops there.
        /// </summary>
        [Fact]
        public void Turn_RunsAtTheEventTurnSpeed()
        {
            var smoother = new EventPoseSmoother(Vector3.Zero, 0f);
            var pose = new EventPose(Vector3.Zero, MathF.PI / 2, 0f);
            smoother.Advance(pose, 1f / 60f, 256);
            Assert.Equal(MathF.PI / 8, smoother.Heading, 4);
            for (int i = 0; i < 4; i++) smoother.Advance(pose, 1f / 60f, 256);
            Assert.Equal(MathF.PI / 2, smoother.Heading, 4);
        }
    }
}
