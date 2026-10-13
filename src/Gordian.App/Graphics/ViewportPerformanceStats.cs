// src/Gordian.App/Graphics/ViewportPerformanceStats.cs
using System;

namespace Gordian.App.Graphics
{
    /// <summary>A summary of the recent frames: rates in frames per second, times in milliseconds.</summary>
    public readonly record struct FrameTimeSummary(int Frames, float AverageMs, float MinMs, float MaxMs, float OnePercentLowFps)
    {
        /// <summary>Frames per second from the average frame interval (0 with no frames).</summary>
        public float Fps => AverageMs > 0f ? 1000f / AverageMs : 0f;
    }

    /// <summary>
    /// One viewport window's performance numbers for the overlay (#30): a ring of recent frame intervals (the pacing
    /// graph), the CPU and GPU time of each pass, the draw counters and sampled memory figures. Every buffer is allocated
    /// once here, so recording and summarising allocate nothing per frame. Written and read on the window's render thread.
    /// </summary>
    public sealed class ViewportPerformanceStats
    {
        /// <summary>Frames the pacing graph and the summary cover (about four seconds at 60 FPS).</summary>
        public const int Capacity = 240;

        /// <summary>Weight of the newest sample in the smoothed pass times.</summary>
        public const float Smoothing = 0.1f;

        private readonly float[] _intervals = new float[Capacity];
        private readonly float[] _sorted = new float[Capacity];
        private int _next;
        private int _count;

        /// <summary>Frame intervals in milliseconds, oldest at <see cref="GraphOffset"/> once the ring is full.</summary>
        public float[] Intervals => _intervals;

        /// <summary>Frames recorded so far (at most <see cref="Capacity"/>).</summary>
        public int Count => _count;

        /// <summary>The index of the oldest interval (for a graph that scrolls left).</summary>
        public int GraphOffset => _count < Capacity ? 0 : _next;

        /// <summary>The last frame's CPU time on the render thread (start of the frame to before present), milliseconds.</summary>
        public float LastCpuFrameMs { get; private set; }

        /// <summary>Smoothed CPU time of each pass (recording and submitting it), by <see cref="ViewportPass"/>.</summary>
        public float[] CpuPassMs { get; } = new float[GpuPassTimer.PassCount];

        /// <summary>Smoothed GPU time of each pass, by <see cref="ViewportPass"/>; valid while <see cref="GpuTimesValid"/>.</summary>
        public float[] GpuPassMs { get; } = new float[GpuPassTimer.PassCount];

        public bool GpuTimesValid { get; private set; }

        /// <summary>How the GPU times are measured, or why they are not.</summary>
        public string GpuTimerDescription { get; set; } = "not measured";

        public int SceneDrawCalls { get; set; }
        public int VisibleMeshes { get; set; }
        public int CulledMeshes { get; set; }
        public int StockUiDrawCalls { get; set; }
        public int StockUiQuads { get; set; }
        public int OverlayDrawCalls { get; set; }

        public long HeapBytes { get; private set; }
        public double AllocatedBytesPerSecond { get; private set; }
        public int Gen0Collections { get; private set; }
        public int Gen1Collections { get; private set; }
        public int Gen2Collections { get; private set; }

        private long _lastMemorySample;
        private long _lastAllocated;

        /// <summary>Records one frame's interval since the previous frame started (the pacing graph).</summary>
        public void RecordFrame(float intervalMs)
        {
            if (!(intervalMs > 0f) || float.IsInfinity(intervalMs)) return;
            _intervals[_next] = intervalMs;
            _next = (_next + 1) % Capacity;
            if (_count < Capacity) _count++;
        }

        /// <summary>Forgets the recorded frames (the overlay was shown again after being hidden).</summary>
        public void ResetFrames()
        {
            _next = 0;
            _count = 0;
            Array.Clear(_intervals);
        }

        /// <summary>Records the last frame's CPU time on the render thread (start of the frame to before present).</summary>
        public void RecordCpuFrame(float ms)
        {
            if (float.IsFinite(ms) && ms >= 0f) LastCpuFrameMs = ms;
        }

        /// <summary>Folds a pass's CPU time into its smoothed value.</summary>
        public void RecordCpuPass(ViewportPass pass, float ms) => Smooth(CpuPassMs, (int)pass, ms);

        /// <summary>Folds a frame's GPU pass times (one per pass) into the smoothed values.</summary>
        public void RecordGpuPasses(ReadOnlySpan<float> passMs)
        {
            for (int i = 0; i < GpuPassMs.Length && i < passMs.Length; i++)
            {
                if (GpuTimesValid) Smooth(GpuPassMs, i, passMs[i]);
                else GpuPassMs[i] = passMs[i];
            }
            GpuTimesValid = true;
        }

        /// <summary>Forgets the GPU times (the detailed overlay closed, so the timer stopped).</summary>
        public void ClearGpuPasses()
        {
            GpuTimesValid = false;
            Array.Clear(GpuPassMs);
        }

        private static void Smooth(float[] values, int index, float sample)
        {
            if (!float.IsFinite(sample) || sample < 0f) return;
            values[index] = values[index] == 0f ? sample : values[index] + (sample - values[index]) * Smoothing;
        }

        /// <summary>
        /// The recorded frames' average, fastest and slowest interval, and the "1% low": the frame rate of the slowest 1%
        /// of frames (the 99th percentile interval), which shows hitches an average hides.
        /// </summary>
        public FrameTimeSummary Summarize()
        {
            int n = _count;
            if (n == 0) return new FrameTimeSummary(0, 0f, 0f, 0f, 0f);
            Array.Copy(_intervals, _sorted, n);
            var sorted = _sorted.AsSpan(0, n);
            sorted.Sort();
            double sum = 0;
            foreach (float v in sorted) sum += v;
            int p99 = Math.Clamp((int)Math.Ceiling(n * 0.99) - 1, 0, n - 1);
            float slow = sorted[p99];
            return new FrameTimeSummary(n, (float)(sum / n), sorted[0], sorted[n - 1], slow > 0f ? 1000f / slow : 0f);
        }

        /// <summary>
        /// Samples the managed heap, allocation rate and GC counts at most every <paramref name="intervalSeconds"/>
        /// (timestamps in <see cref="System.Diagnostics.Stopwatch"/> ticks).
        /// </summary>
        public void SampleMemory(long timestamp, double intervalSeconds = 0.5)
        {
            if (_lastMemorySample != 0 && (timestamp - _lastMemorySample) < intervalSeconds * System.Diagnostics.Stopwatch.Frequency) return;
            long allocated = GC.GetTotalAllocatedBytes(false);
            if (_lastMemorySample != 0)
            {
                double seconds = (timestamp - _lastMemorySample) / (double)System.Diagnostics.Stopwatch.Frequency;
                AllocatedBytesPerSecond = seconds > 0 ? (allocated - _lastAllocated) / seconds : 0;
            }
            _lastAllocated = allocated;
            _lastMemorySample = timestamp;
            HeapBytes = GC.GetTotalMemory(false);
            Gen0Collections = GC.CollectionCount(0);
            Gen1Collections = GC.CollectionCount(1);
            Gen2Collections = GC.CollectionCount(2);
        }
    }
}
