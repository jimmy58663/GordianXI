// src/Gordian.Engine/Graphics/TickPositionSmoother.cs
using System;
using System.Numerics;

namespace Gordian.Engine.Graphics
{
    /// <summary>
    /// Smooths a value that a slower, irregular simulation tick advances (the locomotion loop runs on a UI timer that
    /// fires every ~16 or ~31 ms) for a faster render loop. It keeps the last few tick samples with their tick times and
    /// renders slightly in the past (by the longest recent tick interval), interpolating between the two samples around
    /// that moment, so motion shows at an even rate however unevenly the ticks arrive.
    /// <para>
    /// The delay eases toward that interval instead of jumping to it (#547): it rises at <see cref="DelayRiseRate"/> and
    /// falls at <see cref="DelayFallRate"/> seconds per second, so the render clock runs at most 25% slow or 1% fast and
    /// never steps. A delay that jumped by a tick (one 31 ms tick entering or leaving the window of 16 ms ones) moved the
    /// player and the camera back or forward by that much motion in one frame; locked on, the view aims at the target, so
    /// that step turned the whole view.
    /// </para>
    /// </summary>
    public sealed class TickPositionSmoother
    {
        /// <summary>
        /// Default snap distance for world positions: moves larger than this (yalms) are teleports.
        /// </summary>
        public const float DefaultSnapDistance = 8.0f;

        /// <summary>
        /// Longest render delay; ticks further apart than this are treated as a pause.
        /// </summary>
        public const double MaxDelaySeconds = 0.1;

        /// <summary>How fast the render delay grows toward a longer tick interval, in seconds per second (the render clock runs 25% slow meanwhile).</summary>
        public const double DelayRiseRate = 0.25;

        /// <summary>How fast the render delay shrinks toward a shorter tick interval, in seconds per second (the render clock runs 1% fast meanwhile): slow, so the delay holds the longest tick interval of the last second or so and an occasional long tick does not find it shrunk.</summary>
        public const double DelayFallRate = 0.01;

        private const int Capacity = 8;
        private double _delay;
        private double _lastNow = double.NaN;

        private readonly float _snapDistance;
        private readonly double[] _times = new double[Capacity];
        private readonly Vector3[] _values = new Vector3[Capacity];
        private int _count;
        private int _newest = -1;

        /// <param name="snapDistance">Changes larger than this between ticks are shown immediately.</param>
        public TickPositionSmoother(float snapDistance = DefaultSnapDistance) => _snapDistance = snapDistance;

        /// <summary>
        /// Records the value of the tick at <paramref name="tickTime"/> (seconds; a repeated time is the same tick) and
        /// returns the value to render at <paramref name="now"/> on the same clock.
        /// </summary>
        public Vector3 Update(Vector3 value, double tickTime, double now)
        {
            if (_count == 0 || Vector3.DistanceSquared(value, _values[_newest]) > _snapDistance * _snapDistance)
            {
                _count = 0;
                Add(value, tickTime);
                _delay = 0.0;
                _lastNow = now;
                return value;
            }

            if (tickTime > _times[_newest]) Add(value, tickTime);
            else if (tickTime == _times[_newest]) _values[_newest] = value;

            return Sample(now - EaseDelay(now));
        }

        /// <summary>
        /// Moves the render delay toward the longest recent tick interval at <see cref="DelayRiseRate"/> /
        /// <see cref="DelayFallRate"/>; the first interval seen is taken at once.
        /// </summary>
        private double EaseDelay(double now)
        {
            double target = GetDelay();
            double frame = double.IsNaN(_lastNow) ? 0.0 : Math.Clamp(now - _lastNow, 0.0, MaxDelaySeconds);
            _lastNow = now;
            if (_delay <= 0.0) _delay = target;
            else if (target > _delay) _delay = Math.Min(target, _delay + (DelayRiseRate * frame));
            else _delay = Math.Max(target, _delay - (DelayFallRate * frame));
            return _delay;
        }

        /// <summary>
        /// Forgets the history, so the next value is shown as is.
        /// </summary>
        public void Reset()
        {
            _count = 0;
            _newest = -1;
            _delay = 0.0;
            _lastNow = double.NaN;
        }

        private void Add(Vector3 value, double time)
        {
            _newest = (_newest + 1) % Capacity;
            _times[_newest] = time;
            _values[_newest] = value;
            _count = Math.Min(_count + 1, Capacity);
        }

        private int IndexFromNewest(int age) => ((_newest - age) % Capacity + Capacity) % Capacity;

        /// <summary>
        /// The longest recent tick interval: rendering that far back always has a later tick to interpolate toward.
        /// </summary>
        private double GetDelay()
        {
            double delay = 0.0;
            for (int age = 0; age + 1 < _count; age++)
            {
                double interval = _times[IndexFromNewest(age)] - _times[IndexFromNewest(age + 1)];
                if (interval < MaxDelaySeconds) delay = Math.Max(delay, interval);
            }
            return delay;
        }

        private Vector3 Sample(double time)
        {
            if (time >= _times[_newest]) return _values[_newest];
            for (int age = 0; age + 1 < _count; age++)
            {
                int later = IndexFromNewest(age);
                int earlier = IndexFromNewest(age + 1);
                if (time < _times[earlier]) continue;
                double span = _times[later] - _times[earlier];
                if (span <= 0.0 || span >= MaxDelaySeconds) return _values[later];
                return Vector3.Lerp(_values[earlier], _values[later], (float)((time - _times[earlier]) / span));
            }
            return _values[IndexFromNewest(_count - 1)];
        }
    }
}
