// src/Gordian.Core/Network/PacketBuffer.cs
using System;
using System.Buffers;

namespace Gordian.Core.Network
{
    /// <summary>
    /// A high-performance, non-allocating network buffer memory wrapper optimized for heavy multi-character streams.
    /// Uses ArrayPool rentals to eliminate Garbage Collection overhead on the heap.
    /// </summary>
    public sealed class PacketBuffer : IDisposable
    {
        private byte[]? _rentedArray;
        private readonly int _validLength;
        private bool _isDisposed;

        /// <summary>
        /// Gets a high-performance memory slice of the active valid bytes inside the buffer pool.
        /// </summary>
        public ReadOnlySpan<byte> Data
        {
            get
            {
                ObjectDisposedException.ThrowIf(_isDisposed, this);
                return _rentedArray.AsSpan(0, _validLength);
            }
        }

        /// <summary>
        /// Gets a mutable span slice allowing the underlying network stream components to overwrite data directly in place.
        /// </summary>
        public Span<byte> WritableData
        {
            get
            {
                ObjectDisposedException.ThrowIf(_isDisposed, this);
                return _rentedArray.AsSpan(0, _validLength);
            }
        }

        /// <summary>
        /// Gets a mutable Memory slice allowing asynchronous network methods to read data across thread boundaries safely.
        /// </summary>
        public Memory<byte> WritableMemory
        {
            get
            {
                ObjectDisposedException.ThrowIf(_isDisposed, this);
                return _rentedArray.AsMemory(0, _validLength);
            }
        }

        /// <summary>
        /// Initializes a new high-performance memory boundary allocation block by renting an active byte array structure from the shared pool.
        /// </summary>
        /// <param name="size">The exact byte window parameter required for the active socket packet segment.</param>
        public PacketBuffer(int size)
        {
            if (size <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(size), "Buffer allocation block size parameters must be greater than zero.");
            }

            // Renting memory from the system shared pool completely avoids GC allocations on the heap
            _rentedArray = ArrayPool<byte>.Shared.Rent(size);
            _validLength = size;
            _isDisposed = false;
        }

        /// <summary>
        /// Safely returns the allocated memory space back to the shared pool and resets references.
        /// </summary>
        public void Dispose()
        {
            if (_isDisposed) return;

            if (_rentedArray != null)
            {
                // Return memory array blocks to the pool for fast extraction by other character threads later
                ArrayPool<byte>.Shared.Return(_rentedArray);
                _rentedArray = null;
            }

            _isDisposed = true;
            GC.SuppressFinalize(this);
        }
    }
}
