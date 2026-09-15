// src/Gordian.Core/Diagnostics/SessionPerformanceTracker.cs
using System;
using System.Diagnostics;
using System.Threading;

namespace Gordian.Core.Diagnostics
{
    /// <summary>
    /// Immutable snapshot of session performance metrics at a specific point in time.
    /// </summary>
    public readonly record struct SessionPerformanceSnapshot(
        long PacketsReceivedTotal,
        long PacketsSentTotal,
        long BytesReceivedTotal,
        long BytesSentTotal,
        double PacketsReceivedPerSecond,
        double PacketsSentPerSecond,
        double BytesReceivedPerSecond,
        double BytesSentPerSecond,
        long SequenceDiscrepancies,
        long ManagedHeapSizeBytes,
        int Gen0Collections,
        int Gen1Collections,
        int Gen2Collections,
        DateTime TimestampUtc,
        long DuplicateDatagramsDropped = 0,
        double LastDispatchLatencyMicroseconds = 0,
        double AverageDispatchLatencyMicroseconds = 0,
        double PacketsReceived30SecAverage = 0,
        double PacketsSent30SecAverage = 0,
        double BytesReceived30SecAverage = 0,
        double BytesSent30SecAverage = 0
    )
    {
        /// <summary>
        /// Gets managed heap memory formatted in Megabytes.
        /// </summary>
        public double ManagedHeapMegaBytes => ManagedHeapSizeBytes / (1024.0 * 1024.0);

        /// <summary>
        /// Gets inbound throughput in Kilobytes per second.
        /// </summary>
        public double KilobytesReceivedPerSecond => BytesReceivedPerSecond / 1024.0;

        /// <summary>
        /// Gets outbound throughput in Kilobytes per second.
        /// </summary>
        public double KilobytesSentPerSecond => BytesSentPerSecond / 1024.0;

        /// <summary>
        /// Gets 30-second average inbound throughput in Kilobytes per second.
        /// </summary>
        public double KilobytesReceived30SecAverage => BytesReceived30SecAverage / 1024.0;

        /// <summary>
        /// Gets 30-second average outbound throughput in Kilobytes per second.
        /// </summary>
        public double KilobytesSent30SecAverage => BytesSent30SecAverage / 1024.0;
    }

    /// <summary>
    /// Thread-safe, lock-free performance and datagram metrics tracker for an active character session.
    /// Utilizes atomic operations to minimize overhead in the zero-allocation hot path.
    /// </summary>
    public sealed class SessionPerformanceTracker
    {
        private long _packetsReceivedTotal;
        private long _packetsSentTotal;
        private long _bytesReceivedTotal;
        private long _bytesSentTotal;
        private long _sequenceDiscrepancies;
        private long _duplicateDatagramsDropped;

        // Microsecond dispatch latency profiling
        private long _lastDispatchLatencyTicks;
        private long _totalDispatchLatencyTicks;
        private long _dispatchCount;
        private long _peakDispatchLatencyTicks;

        // Rolling rate calculation state (live 250ms window)
        private long _lastSampleStopwatchTicks;
        private long _lastSamplePacketsIn;
        private long _lastSamplePacketsOut;
        private long _lastSampleBytesIn;
        private long _lastSampleBytesOut;

        private double _packetsInRate;
        private double _packetsOutRate;
        private double _bytesInRate;
        private double _bytesOutRate;

        // 30-second rolling sliding window history
        private readonly struct HistorySample
        {
            public readonly long Ticks;
            public readonly long PacketsIn;
            public readonly long PacketsOut;
            public readonly long BytesIn;
            public readonly long BytesOut;

            public HistorySample(long ticks, long packetsIn, long packetsOut, long bytesIn, long bytesOut)
            {
                Ticks = ticks;
                PacketsIn = packetsIn;
                PacketsOut = packetsOut;
                BytesIn = bytesIn;
                BytesOut = bytesOut;
            }
        }

        private readonly HistorySample[] _rollingSamples = new HistorySample[31];
        private int _rollingSampleCount;
        private int _rollingSampleIndex;
        private long _lastHistorySampleTicks;

        private double _packetsIn30SecAvg;
        private double _packetsOut30SecAvg;
        private double _bytesIn30SecAvg;
        private double _bytesOut30SecAvg;

        private readonly object _rateLock = new object();

        public SessionPerformanceTracker()
        {
            _lastSampleStopwatchTicks = Stopwatch.GetTimestamp();
        }

        /// <summary>
        /// Records newly received inbound datagram bytes.
        /// </summary>
        public void RecordInboundDatagram(int byteCount)
        {
            if (byteCount > 0)
            {
                Interlocked.Add(ref _bytesReceivedTotal, byteCount);
            }
        }

        /// <summary>
        /// Records a parsed inbound packet chunk.
        /// </summary>
        public void RecordInboundPacket()
        {
            Interlocked.Increment(ref _packetsReceivedTotal);
        }

        /// <summary>
        /// Records multiple parsed inbound packet chunks.
        /// </summary>
        public void RecordInboundPackets(int count)
        {
            if (count > 0)
            {
                Interlocked.Add(ref _packetsReceivedTotal, count);
            }
        }

        /// <summary>
        /// Records transmitted outbound datagram bytes.
        /// </summary>
        public void RecordOutboundDatagram(int byteCount)
        {
            if (byteCount > 0)
            {
                Interlocked.Add(ref _bytesSentTotal, byteCount);
            }
        }

        /// <summary>
        /// Records a transmitted outbound packet chunk.
        /// </summary>
        public void RecordOutboundPacket()
        {
            Interlocked.Increment(ref _packetsSentTotal);
        }

        /// <summary>
        /// Records multiple transmitted outbound packet chunks.
        /// </summary>
        public void RecordOutboundPackets(int count)
        {
            if (count > 0)
            {
                Interlocked.Add(ref _packetsSentTotal, count);
            }
        }

        /// <summary>
        /// Records a sequence number discrepancy or detected gap in the UDP packet sequence.
        /// </summary>
        public void RecordSequenceDiscrepancy()
        {
            Interlocked.Increment(ref _sequenceDiscrepancies);
        }

        /// <summary>
        /// Records a dropped duplicate or retransmitted server UDP datagram.
        /// </summary>
        public void RecordDuplicateDatagram()
        {
            Interlocked.Increment(ref _duplicateDatagramsDropped);
        }

        /// <summary>
        /// Gets the total count of dropped duplicate or retransmitted datagrams.
        /// </summary>
        public long DuplicateDatagramsDropped => Interlocked.Read(ref _duplicateDatagramsDropped);

        /// <summary>
        /// Records the high-resolution elapsed stopwatch ticks for a packet decode and dispatch cycle.
        /// Zero-allocation atomic update.
        /// </summary>
        public void RecordDispatchLatencyTicks(long elapsedTicks)
        {
            if (elapsedTicks > 0)
            {
                Interlocked.Exchange(ref _lastDispatchLatencyTicks, elapsedTicks);
                Interlocked.Add(ref _totalDispatchLatencyTicks, elapsedTicks);
                Interlocked.Increment(ref _dispatchCount);

                long currentPeak = Interlocked.Read(ref _peakDispatchLatencyTicks);
                while (elapsedTicks > currentPeak)
                {
                    long prev = Interlocked.CompareExchange(ref _peakDispatchLatencyTicks, elapsedTicks, currentPeak);
                    if (prev == currentPeak) break;
                    currentPeak = prev;
                }
            }
        }

        /// <summary>
        /// Calculates rolling rates and generates an immutable snapshot of current session performance.
        /// </summary>
        public SessionPerformanceSnapshot GetSnapshot()
        {
            long currentTicks = Stopwatch.GetTimestamp();
            long totalIn = Interlocked.Read(ref _packetsReceivedTotal);
            long totalOut = Interlocked.Read(ref _packetsSentTotal);
            long bytesIn = Interlocked.Read(ref _bytesReceivedTotal);
            long bytesOut = Interlocked.Read(ref _bytesSentTotal);
            long seqDiscrepancies = Interlocked.Read(ref _sequenceDiscrepancies);
            long dupDropped = Interlocked.Read(ref _duplicateDatagramsDropped);

            long lastTicks = Interlocked.Read(ref _lastDispatchLatencyTicks);
            long totalLatencyTicks = Interlocked.Read(ref _totalDispatchLatencyTicks);
            long dispatches = Interlocked.Read(ref _dispatchCount);

            double lastMicroseconds = (double)lastTicks * 1_000_000.0 / Stopwatch.Frequency;
            double avgMicroseconds = dispatches > 0 ? ((double)totalLatencyTicks * 1_000_000.0 / Stopwatch.Frequency) / dispatches : 0;

            lock (_rateLock)
            {
                double elapsedSeconds = (double)(currentTicks - _lastSampleStopwatchTicks) / Stopwatch.Frequency;
                if (elapsedSeconds >= 0.25) // Update live rolling rates every 250ms or greater
                {
                    _packetsInRate = Math.Max(0, (totalIn - _lastSamplePacketsIn) / elapsedSeconds);
                    _packetsOutRate = Math.Max(0, (totalOut - _lastSamplePacketsOut) / elapsedSeconds);
                    _bytesInRate = Math.Max(0, (bytesIn - _lastSampleBytesIn) / elapsedSeconds);
                    _bytesOutRate = Math.Max(0, (bytesOut - _lastSampleBytesOut) / elapsedSeconds);

                    _lastSamplePacketsIn = totalIn;
                    _lastSamplePacketsOut = totalOut;
                    _lastSampleBytesIn = bytesIn;
                    _lastSampleBytesOut = bytesOut;
                    _lastSampleStopwatchTicks = currentTicks;
                }

                // Update 30-second rolling history once per second
                double historyElapsed = (double)(currentTicks - _lastHistorySampleTicks) / Stopwatch.Frequency;
                if (_lastHistorySampleTicks == 0 || historyElapsed >= 1.0)
                {
                    _rollingSamples[_rollingSampleIndex] = new HistorySample(currentTicks, totalIn, totalOut, bytesIn, bytesOut);
                    _rollingSampleIndex = (_rollingSampleIndex + 1) % _rollingSamples.Length;
                    if (_rollingSampleCount < _rollingSamples.Length)
                    {
                        _rollingSampleCount++;
                    }
                    _lastHistorySampleTicks = currentTicks;

                    int oldestIndex = _rollingSampleCount < _rollingSamples.Length
                        ? 0
                        : _rollingSampleIndex;

                    ref readonly HistorySample oldest = ref _rollingSamples[oldestIndex];
                    double spanSeconds = (double)(currentTicks - oldest.Ticks) / Stopwatch.Frequency;
                    if (spanSeconds >= 0.5)
                    {
                        _packetsIn30SecAvg = Math.Max(0, (totalIn - oldest.PacketsIn) / spanSeconds);
                        _packetsOut30SecAvg = Math.Max(0, (totalOut - oldest.PacketsOut) / spanSeconds);
                        _bytesIn30SecAvg = Math.Max(0, (bytesIn - oldest.BytesIn) / spanSeconds);
                        _bytesOut30SecAvg = Math.Max(0, (bytesOut - oldest.BytesOut) / spanSeconds);
                    }
                    else
                    {
                        _packetsIn30SecAvg = _packetsInRate;
                        _packetsOut30SecAvg = _packetsOutRate;
                        _bytesIn30SecAvg = _bytesInRate;
                        _bytesOut30SecAvg = _bytesOutRate;
                    }
                }
            }

            return new SessionPerformanceSnapshot(
                PacketsReceivedTotal: totalIn,
                PacketsSentTotal: totalOut,
                BytesReceivedTotal: bytesIn,
                BytesSentTotal: bytesOut,
                PacketsReceivedPerSecond: _packetsInRate,
                PacketsSentPerSecond: _packetsOutRate,
                BytesReceivedPerSecond: _bytesInRate,
                BytesSentPerSecond: _bytesOutRate,
                SequenceDiscrepancies: seqDiscrepancies,
                DuplicateDatagramsDropped: dupDropped,
                ManagedHeapSizeBytes: GC.GetTotalMemory(false),
                Gen0Collections: GC.CollectionCount(0),
                Gen1Collections: GC.CollectionCount(1),
                Gen2Collections: GC.CollectionCount(2),
                TimestampUtc: DateTime.UtcNow,
                LastDispatchLatencyMicroseconds: lastMicroseconds,
                AverageDispatchLatencyMicroseconds: avgMicroseconds,
                PacketsReceived30SecAverage: _packetsIn30SecAvg,
                PacketsSent30SecAverage: _packetsOut30SecAvg,
                BytesReceived30SecAverage: _bytesIn30SecAvg,
                BytesSent30SecAverage: _bytesOut30SecAvg
            );
        }
    }
}
