// src/Gordian.App/HandoffPipeServer.cs
using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Gordian.Core.Network;

namespace Gordian.App
{
    public sealed class HandoffPipeServer : IDisposable
    {
        private const string PipeName = "GordianXI_Handoff";
        private CancellationTokenSource? _cts;
        private Task? _listenTask;
        private bool _isDisposed;

        /// <summary>
        /// Fires immediately when a valid session token payload is intercepted and piped into the application process boundary.
        /// </summary>
        public event EventHandler<SessionHandoffArgs>? SessionReceived;

        /// <summary>
        /// Spins up the background IPC listener thread loop.
        /// </summary>
        public void Start()
        {
            if (_listenTask != null) return;

            _cts = new CancellationTokenSource();
            _listenTask = Task.Run(() => ServerListenLoopAsync(_cts.Token), _cts.Token);
            System.Diagnostics.Debug.WriteLine("[GordianXI IPC] Named Pipe Handoff Server running.");
        }

        private async Task ServerListenLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                NamedPipeServerStream? pipeStream = null;
                try
                {
                    // Instantiate a cross-platform secure Named Pipe instance
                    pipeStream = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.In,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    // Wait asynchronously for a proxy bootloader connection to connect to this channel
                    await pipeStream.WaitForConnectionAsync(token).ConfigureAwait(false);

                    using var reader = new StreamReader(pipeStream, Encoding.UTF8);
                    string rawJsonPayload = await reader.ReadToEndAsync(token).ConfigureAwait(false);

                    if (!string.IsNullOrWhiteSpace(rawJsonPayload))
                    {
                        var args = JsonSerializer.Deserialize<SessionHandoffArgs>(rawJsonPayload);
                        if (args != null)
                        {
                            // Marshall the session notification event directly to listening application modules
                            SessionReceived?.Invoke(this, args);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[GordianXI IPC] Named pipe exception encountered: {ex.Message}");
                }
                finally
                {
                    pipeStream?.Dispose();
                }
            }
        }

        /// <summary>
        /// Gracefully requests termination of the background pipe engine tasks.
        /// </summary>
        public void Stop()
        {
            _cts?.Cancel();
            _listenTask = null;
        }

        public void Dispose()
        {
            if (_isDisposed) return;

            Stop();
            _cts?.Dispose();
            _isDisposed = true;
        }
    }
}
