// src/Gordian.Core/Network/SessionNetworkManager.cs
using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Gordian.Core.Config;

namespace Gordian.Core.Network
{
    /// <summary>
    /// Manages the low-level asynchronous network connection pipeline, inbound parsing loops,
    /// and a high-performance outbound chunk-bundling ring queue for a single character session.
    /// </summary>
    public sealed class SessionNetworkManager : IDisposable
    {
        private readonly string _serverAddress;
        private readonly int _serverPort;
        private readonly PacketParser _parser;
        private TcpClient? _tcpClient;
        private NetworkStream? _networkStream;
        private CancellationTokenSource? _cts;
        private Task? _readTask;
        private Task? _writeFlushTask;
        private bool _isDisposed;

        // 🔒 Outbound Network Buffering Blocks
        private readonly SemaphoreSlim _writeLock = new SemaphoreSlim(1, 1);
        private readonly byte[] _outboundQueueBuffer = new byte[1360]; // FFXI MTU-safe maximum packet window boundary
        private int _currentBufferLength = 0;
        private const int NetworkTickIntervalMs = 250; // 4Hz standard FFXI PacketFlow update tick frequency
        private uint _outboundSequenceId = 1;

        /// <summary>
        /// Gets the isolated, instance-level configuration matrix for this specific character session.
        /// </summary>
        public SessionProfile Profile { get; } = new SessionProfile();

        /// <summary>
        /// Gets the real-time operational lifecycle status of this active network connection.
        /// </summary>
        public SessionState CurrentState { get; private set; } = SessionState.Disconnected;

        public SessionNetworkManager(string serverAddress, int serverPort)
        {
            _serverAddress = serverAddress ?? throw new ArgumentNullException(nameof(serverAddress));
            _serverPort = serverPort;
            _parser = new PacketParser(this.Profile, this.QueueChunkAsync);
        }

        /// <summary>
        /// Establishes a non-blocking connection to the target server gate and initializes the two-way background processing worker tasks.
        /// </summary>
        public async Task ConnectAsync()
        {
            if (CurrentState != SessionState.Disconnected)
            {
                throw new InvalidOperationException("This network session is already active or processing an authentication task.");
            }

            _tcpClient = new TcpClient();
            _cts = new CancellationTokenSource();

            try
            {
                CurrentState = SessionState.ConnectingToGameServer;
                
                await _tcpClient.ConnectAsync(_serverAddress, _serverPort, _cts.Token).ConfigureAwait(false);
                _networkStream = _tcpClient.GetStream();

                CurrentState = SessionState.ExchangingCryptoKeys;
                CurrentState = SessionState.LoadingWorldData;

                // Launch the parallel, high-performance background execution worker tasks
                _readTask = Task.Run(() => InboundNetworkReadLoopAsync(_networkStream, _cts.Token), _cts.Token);
                _writeFlushTask = Task.Run(() => OutboundNetworkFlushLoopAsync(_networkStream, _cts.Token), _cts.Token);
            }
            catch (Exception)
            {
                Disconnect();
                throw;
            }
        }

        /// <summary>
        /// Bitpacks a logical command chunk into the outbound staging buffer queue thread-safely.
        /// Can be flagged as high priority to bypass the standard network tick interval and flush instantly.
        /// </summary>
        /// <param name="chunkData">The raw binary logic array holding the specific action command payload parameters.</param>
        /// <param name="isHighPriority">If true, forces the engine to bypass the network tick timer and flush the packet immediately.</param>
        public async Task QueueChunkAsync(ReadOnlyMemory<byte> chunkData, bool isHighPriority = false)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);

            if (CurrentState == SessionState.Disconnected || _networkStream == null)
            {
                throw new InvalidOperationException("Cannot queue action payloads; the network socket stream is disconnected.");
            }

            // Secure the outbound memory gate to prevent multi-threaded data collisions
            await _writeLock.WaitAsync().ConfigureAwait(false);

            try
            {
                // If the incoming chunk cannot physically fit in the remaining window space, force a flush first
                if (_currentBufferLength + chunkData.Length > _outboundQueueBuffer.Length)
                {
                    await FlushBundledPacketAsync(_networkStream, _cts!.Token).ConfigureAwait(false);
                }

                // Append the logical chunk data straight onto the continuous staging buffer array
                chunkData.Span.CopyTo(_outboundQueueBuffer.AsSpan(_currentBufferLength));
                _currentBufferLength += chunkData.Length;

                // ⚡ THE HIGH PRIORITY BYPASS BYPASS GATE:
                // If this chunk is marked as time-critical (e.g., weapon skills or emergency casts),
                // we execute an immediate flush down the wire right now instead of waiting for the 150ms timer loop.
                if (isHighPriority)
                {
                    await FlushBundledPacketAsync(_networkStream, _cts!.Token).ConfigureAwait(false);
                }
            }
            finally
            {
                _writeLock.Release();
            }
        }

        /// <summary>
        /// Consolidates, encrypts, and transmits all bundled staging chunks down the wire as a unified network frame.
        /// Must be executed entirely within an active _writeLock perimeter constraint.
        /// </summary>
        private async Task FlushBundledPacketAsync(NetworkStream stream, CancellationToken token)
        {
            if (_currentBufferLength == 0) return;

            try
            {
                // Rent an optimization buffer window to hold the compressed output bytes safely.
                // The maximum possible size of an uncompressed block matches our queue buffer.
                using var huffmanTargetBuffer = new PacketBuffer(_outboundQueueBuffer.Length);
                int compressedLength = 0;

                // Enforce an explicit scope blocks:
                // Synchronous execution layers are allowed to use Span<byte> structures freely.
                {
                    ReadOnlySpan<byte> bundledChunks = _outboundQueueBuffer.AsSpan(0, _currentBufferLength);
                    Span<byte> destinationSpan = huffmanTargetBuffer.WritableData;

                    // Perform the in-place bitpacking compression safely onto our rented memory space
                    compressedLength = HuffmanEncoder.Compress(bundledChunks, destinationSpan);
                }

                // Allocate your Master Envelope payload wrapper.
                // FFXI Master packet structure requires: 4 bytes (Sequence ID) + data payload length
                int masterEnvelopeSize = compressedLength + 4;
                using var encryptedWireBuffer = new PacketBuffer(masterEnvelopeSize);

                // Write your Outbound Sequence ID straight into the first 4 bytes of the network header
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(
                    encryptedWireBuffer.WritableData.Slice(0, 4),
                    _outboundSequenceId
                );

                // Copy the compressed data payload into the remainder of the wire envelope block
                huffmanTargetBuffer.WritableData.Slice(0, compressedLength)
                    .CopyTo(encryptedWireBuffer.WritableData.Slice(4));

                // Apply your character-specific Blowfish encryption directly to the finished memory layout
                _parser.GetCryptoEngine().EncryptCbc(encryptedWireBuffer.WritableData); // Re-uses our optimized in-place engine

                // Native cross-platform async transmission down the active socket wire via safe Memory<byte>
                await stream.WriteAsync(encryptedWireBuffer.WritableMemory, token).ConfigureAwait(false);

                // Advance tracking indices for the next frame interval sequence match
                _outboundSequenceId++;
            }
            finally
            {
                // Instantly clear the tracking counter to clear the array for the next tick frame cycle
                _currentBufferLength = 0;
            }
        }

        /// <summary>
        /// The permanent outbound tick worker loop. Evaluates the staging arrays and forces an execution
        /// flush down the socket stream every 150 milliseconds.
        /// </summary>
        private async Task OutboundNetworkFlushLoopAsync(NetworkStream stream, CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    // Sleep the task asynchronously to maintain our targeted network tick interval frequency
                    await Task.Delay(NetworkTickIntervalMs, token).ConfigureAwait(false);

                    // Await access to the queue buffer thread-safely
                    await _writeLock.WaitAsync(token).ConfigureAwait(false);
                    try
                    {
                        await FlushBundledPacketAsync(stream, token).ConfigureAwait(false);
                    }
                    finally
                    {
                        _writeLock.Release();
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NET_TRACE] Outbound write flush failure: {ex.Message}");
            }
        }

        /// <summary>
        /// The inbound read worker loop. Streams, decrypts, and routes incoming bytes continuously.
        /// </summary>
        private async Task InboundNetworkReadLoopAsync(NetworkStream stream, CancellationToken token)
        {
            const int readBufferSize = 2048;

            try
            {
                while (!token.IsCancellationRequested)
                {
                    using var packetBuffer = new PacketBuffer(readBufferSize);
                    int bytesRead = await stream.ReadAsync(packetBuffer.WritableMemory, token).ConfigureAwait(false);

                    if (bytesRead == 0) break;

                    Span<byte> activeChunk = packetBuffer.WritableData.Slice(0, bytesRead);
                    _parser.ProcessIncomingChunk(activeChunk);

                    if (CurrentState == SessionState.LoadingWorldData)
                    {
                        CurrentState = SessionState.ActiveInWorld;
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NET_TRACE] Inbound read loop failure: {ex.Message}");
            }
            finally
            {
                CurrentState = SessionState.Disconnecting;
                ExecuteSocketCleanup();
                CurrentState = SessionState.Disconnected;
            }
        }

        /// <summary>
        /// Gracefully requests termination of the active connection session worker loops.
        /// </summary>
        public void Disconnect()
        {
            if (CurrentState == SessionState.Disconnected) return;

            CurrentState = SessionState.Disconnecting;
            _cts?.Cancel();
            ExecuteSocketCleanup();
            CurrentState = SessionState.Disconnected;
        }

        private void ExecuteSocketCleanup()
        {
            _networkStream?.Dispose();
            _networkStream = null;

            _tcpClient?.Dispose();
            _tcpClient = null;

            _cts?.Dispose();
            _cts = null;
        }

        public void Dispose()
        {
            if (_isDisposed) return;

            Disconnect();
            _writeLock.Dispose();
            _isDisposed = true;
        }
    }
}
