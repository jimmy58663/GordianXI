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
        DateTime TimestampUtc
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

        // Rolling rate calculation state
        private long _lastSampleStopwatchTicks;
        private long _lastSamplePacketsIn;
        private long _lastSamplePacketsOut;
        private long _lastSampleBytesIn;
        private long _lastSampleBytesOut;

        private double _packetsInRate;
        private double _packetsOutRate;
        private double _bytesInRate;
        private double _bytesOutRate;

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

            lock (_rateLock)
            {
                double elapsedSeconds = (double)(currentTicks - _lastSampleStopwatchTicks) / Stopwatch.Frequency;
                if (elapsedSeconds >= 0.25) // Update rolling rates every 250ms or greater
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
                ManagedHeapSizeBytes: GC.GetTotalMemory(false),
                Gen0Collections: GC.CollectionCount(0),
                Gen1Collections: GC.CollectionCount(1),
                Gen2Collections: GC.CollectionCount(2),
                TimestampUtc: DateTime.UtcNow
            );
        }
    }
}
