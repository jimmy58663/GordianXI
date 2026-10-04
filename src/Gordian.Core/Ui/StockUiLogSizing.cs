// src/Gordian.Core/Ui/StockUiLogSizing.cs
using System;

namespace Gordian.Core.Ui
{
    /// <summary>
    /// A log window's reactive sizing (config Window 1 / Window 2: "Reactive window sizing", "Minimum lines displayed",
    /// "Maximum lines displayed", "Resize Time"): the window shows the minimum lines while the log is quiet, grows a
    /// line for each new line up to the maximum, and after the resize time without new lines shrinks back to the
    /// minimum a line at a time. With reactive sizing OFF it always shows the maximum, as it does while the player
    /// reads it (the window selected with keypad +, scrolled back, or the input line open).
    /// <para>
    /// Retail grows and shrinks its log this way; the timings are not captured yet (#48), so
    /// <see cref="ResizeSeconds"/> (the slider's value / 10: the default 50 is five seconds) and
    /// <see cref="ShrinkStepSeconds"/> are provisional. New lines count one line each, however many rows they wrap to.
    /// </para>
    /// </summary>
    public sealed class StockUiLogSizing
    {
        /// <summary>Seconds between the shrinking window's steps of one line.</summary>
        public const double ShrinkStepSeconds = 0.1;

        private long _seen = -1;
        private double _lastActivity, _lastShrink;

        /// <summary>The lines the window shows now (0 before the first update).</summary>
        public int Lines { get; private set; }

        /// <summary>The "Resize Time" slider (0-100) in seconds without new lines before the window shrinks.</summary>
        public static double ResizeSeconds(int sliderValue) => Math.Max(0, sliderValue) / 10.0;

        /// <summary>
        /// Advances the sizing to <paramref name="now"/> (seconds, any monotonic clock) given the window's arrived-line
        /// counter (<see cref="StockUiChatLog.AddedCount"/>) and its settings; returns the lines to show.
        /// </summary>
        public int Update(double now, long addedCount, bool reactive, int minLines, int maxLines, double resizeSeconds, bool expanded)
        {
            maxLines = Math.Max(1, maxLines);
            minLines = Math.Clamp(minLines, 1, maxLines);
            long added = _seen < 0 ? 0 : Math.Max(0, addedCount - _seen);
            _seen = addedCount;
            if (Lines == 0) Lines = reactive ? minLines : maxLines;

            if (!reactive || expanded)
            {
                Lines = maxLines;
                _lastActivity = now;
                _lastShrink = now;
                return Lines;
            }
            if (added > 0)
            {
                Lines = (int)Math.Min(maxLines, Math.Max(Lines, minLines) + added);
                _lastActivity = now;
                _lastShrink = now;
            }
            else if (Lines > minLines && now - _lastActivity >= resizeSeconds && now - _lastShrink >= ShrinkStepSeconds)
            {
                Lines--;
                _lastShrink = now;
            }
            Lines = Math.Clamp(Lines, minLines, maxLines);
            return Lines;
        }
    }
}
