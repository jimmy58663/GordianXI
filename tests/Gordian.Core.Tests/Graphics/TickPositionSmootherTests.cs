using System.Numerics;
using Gordian.Core.Graphics;

namespace Gordian.Core.Tests.Graphics
{
    public class TickPositionSmootherTests
    {
        [Fact]
        public void IrregularTicks_RenderAsEvenMotion()
        {
            // A runner at 5 yalms/s whose position advances on ticks alternating 16 and 31 ms, rendered at 144 fps.
            var smoother = new TickPositionSmoother();
            double tickTime = 0.0, nextTick = 0.0;
            bool longTick = false;
            float lastShown = 0.0f, largestStep = 0.0f, smallestStep = float.MaxValue;
            for (int frame = 0; frame < 144; frame++)
            {
                double now = frame / 144.0;
                while (nextTick <= now)
                {
                    tickTime = nextTick;
                    nextTick += longTick ? 0.031 : 0.016;
                    longTick = !longTick;
                }
                var shown = smoother.Update(new Vector3((float)(tickTime * 5.0), 0, 0), tickTime, now);
                if (frame > 20)
                {
                    float step = shown.X - lastShown;
                    largestStep = MathF.Max(largestStep, step);
                    smallestStep = MathF.Min(smallestStep, step);
                }
                lastShown = shown.X;
            }

            // Raw ticks move 0 or up to 0.155 yalms between frames; smoothed frames all move 5/144 = 0.035.
            Assert.InRange(smallestStep, 0.033f, 0.036f);
            Assert.InRange(largestStep, 0.033f, 0.036f);
        }

        [Fact]
        public void StoppingSettlesOnTheLastTickAndTeleportsSnap()
        {
            var smoother = new TickPositionSmoother();
            smoother.Update(Vector3.Zero, 0.0, 0.0);
            smoother.Update(new Vector3(0.1f, 0, 0), 0.016, 0.016);
            Assert.Equal(new Vector3(0.1f, 0, 0), smoother.Update(new Vector3(0.1f, 0, 0), 0.032, 0.2));

            Assert.Equal(new Vector3(50, 0, 0), smoother.Update(new Vector3(50, 0, 0), 0.21, 0.21));
        }
    }
}
