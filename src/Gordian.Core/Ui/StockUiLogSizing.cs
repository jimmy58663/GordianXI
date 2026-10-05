// src/Gordian.Core/Ui/StockUiLogSizing.cs
using System;

namespace Gordian.Core.Ui
{
    /// <summary>
    /// A log window's reactive sizing (config Window 1 / Window 2: "Reactive window sizing", "Minimum lines displayed",
    /// "Maximum lines displayed", "Resize Time"): the window grows a line for each new line up to the maximum, and once
    /// the log is quiet it gives back one line per Resize Time (wait, drop a line, wait, drop the next) down to the
    /// minimum, which may be 0 (the window closes). With reactive sizing OFF it always shows the maximum, as it does
    /// while the player reads it (the window selected with keypad +, scrolled back, or the input line open).
    /// <para>
    /// The window's height follows the line count smoothly (<see cref="DisplayLines"/> moves towards
    /// <see cref="Lines"/> at <see cref="LinesPerSecond"/>, easing in the last line). Behaviour from the maintainer's
    /// in-game comparison (2026-10-04, #48); the timings are GordianXI's (the maintainer: they need not match retail):
    /// Resize Time is the slider value / 20 seconds per line (the default 50 = 2.5 s). New lines count one line each,
    /// however many rows they wrap to.
    /// </para>
    /// </summary>
    public sealed class StockUiLogSizing
    {
        /// <summary>How fast the drawn height follows the line count, in lines per second.</summary>
        public const double LinesPerSecond = 8;

        private long _seen = -1;
        private double _lastStep, _lastUpdate = double.NaN;
        private bool _started;

        /// <summary>The lines the window is sized for (its target).</summary>
        public int Lines { get; private set; }

        /// <summary>The lines' worth of height drawn now, moving smoothly towards <see cref="Lines"/>.</summary>
        public float DisplayLines { get; private set; }

        /// <summary>The "Resize Time" slider (0-100) as the seconds the window waits before giving back each line.</summary>
        public static double ResizeSeconds(int sliderValue) => Math.Max(0, sliderValue) / 20.0;

        /// <summary>
        /// Advances the sizing to <paramref name="now"/> (seconds, any monotonic clock) given the window's arrived-line
        /// counter (<see cref="StockUiChatLog.AddedCount"/>) and its settings; returns the target line count. Read
        /// <see cref="DisplayLines"/> for the height to draw.
        /// </summary>
        public int Update(double now, long addedCount, bool reactive, int minLines, int maxLines, double resizeSeconds, bool expanded)
        {
            maxLines = Math.Max(1, maxLines);
            minLines = Math.Clamp(minLines, 0, maxLines);
            long added = _seen < 0 ? 0 : Math.Max(0, addedCount - _seen);
            _seen = addedCount;
            double elapsed = double.IsNaN(_lastUpdate) ? 0 : Math.Max(0, now - _lastUpdate);
            _lastUpdate = now;

            if (!_started)
            {
                _started = true;
                Lines = reactive && !expanded ? minLines : maxLines;
                DisplayLines = Lines;
                _lastStep = now;
            }

            if (!reactive || expanded)
            {
                Lines = maxLines;
                _lastStep = now;
            }
            else if (added > 0)
            {
                Lines = (int)Math.Min(maxLines, Math.Max(Lines, minLines) + added);
                _lastStep = now;
            }
            else if (Lines > minLines && now - _lastStep >= resizeSeconds)
            {
                // One line per Resize Time: wait, drop a line, wait again.
                Lines--;
                _lastStep = now;
            }
            Lines = Math.Clamp(Lines, minLines, maxLines);

            float target = Lines;
            float step = (float)(elapsed * LinesPerSecond);
            float gap = target - DisplayLines;
            // Ease the last line: the step shrinks with the gap so the edge settles rather than stops.
            float move = Math.Min(Math.Abs(gap), Math.Max(step * Math.Min(1f, Math.Abs(gap)), step * 0.25f));
            DisplayLines += Math.Sign(gap) * move;
            if (Math.Abs(target - DisplayLines) < 0.01f) DisplayLines = target;
            return Lines;
        }
    }
}
