// src/Gordian.App/Audio/OpenAlAudioOutput.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Gordian.Core.Diagnostics;
using Silk.NET.Core.Contexts;
using Silk.NET.OpenAL;

namespace Gordian.App.Audio
{
    /// <summary>
    /// Plays the mixer's output through OpenAL Soft (Silk.NET.OpenAL with the <c>Silk.NET.OpenAL.Soft.Native</c> library,
    /// LGPL-2.1, loaded dynamically and replaceable): one streaming source fed by a queue of 16-bit stereo buffers.
    /// </summary>
    /// <remarks>
    /// The source is listener-relative at the origin and the buffers are stereo, which OpenAL never spatialises, so the
    /// mix reaches the speakers exactly as <see cref="AudioMixer"/> made it: retail's distance volume and panning are
    /// ours (legacy parity). OpenAL's own 3D sources, HRTF and doppler are not used.
    /// </remarks>
    public sealed unsafe class OpenAlAudioOutput : IAudioOutput
    {
        /// <summary>Most buffers kept in flight; a <see cref="Queue"/> beyond this drops the data (the mixer outran the device).</summary>
        public const int MaxBuffers = 16;

        private const int BytesPerFrame = 4;

        private ALContext? _alc;
        private AL? _al;
        private Device* _device;
        private Context* _context;
        private uint _source;
        private readonly Queue<uint> _free = new();
        private readonly Dictionary<uint, int> _framesInBuffer = new();
        private int _queuedFrames;
        private bool _open;

        /// <inheritdoc />
        public int SampleRate { get; private set; } = 48000;

        /// <inheritdoc />
        public bool Open(int requestedRate)
        {
            try
            {
                (_alc, _al) = LoadApis();
                _device = _alc.OpenDevice(string.Empty);
                if (_device == null)
                {
                    GordianLog.Info("AUDIO", "OpenAL Soft found no audio device; sound is silent this session.");
                    Dispose();
                    return false;
                }

                // ALC_FREQUENCY: ask for the mixer rate; OpenAL Soft resamples to the device if it differs.
                int* attributes = stackalloc int[] { 0x1007, requestedRate, 0 };
                _context = _alc.CreateContext(_device, attributes);
                if (_context == null || !_alc.MakeContextCurrent(_context))
                {
                    GordianLog.Warn("AUDIO", "OpenAL Soft could not create a context; sound is silent this session.");
                    Dispose();
                    return false;
                }

                SampleRate = requestedRate;
                _source = _al.GenSource();
                _al.SetSourceProperty(_source, SourceBoolean.SourceRelative, true);
                _al.SetSourceProperty(_source, SourceVector3.Position, 0f, 0f, 0f);
                _al.SetSourceProperty(_source, SourceFloat.Gain, 1f);
                _al.SetSourceProperty(_source, SourceBoolean.Looping, false);
                for (int i = 0; i < MaxBuffers; i++)
                {
                    _free.Enqueue(_al.GenBuffer());
                }

                AudioError error = _al.GetError();
                if (error != AudioError.NoError)
                {
                    GordianLog.Warn("AUDIO", $"OpenAL Soft setup failed ({error}); sound is silent this session.");
                    Dispose();
                    return false;
                }

                _open = true;
                string name = _alc.GetContextProperty(_device, GetContextString.DeviceSpecifier) ?? "default device";
                GordianLog.Info("AUDIO", $"OpenAL Soft opened '{name}' at {SampleRate} Hz stereo (streaming, {MaxBuffers} buffers).");
                return true;
            }
            catch (Exception ex)
            {
                GordianLog.Info("AUDIO", $"OpenAL Soft is not available ({ex.GetType().Name}: {ex.Message}); sound is silent this session.");
                Dispose();
                return false;
            }
        }

        /// <inheritdoc />
        public int QueuedFrames
        {
            get
            {
                if (!_open)
                {
                    return 0;
                }

                Reclaim();
                return _queuedFrames;
            }
        }

        /// <inheritdoc />
        public void Queue(ReadOnlySpan<short> interleaved)
        {
            if (!_open || _al is null || interleaved.IsEmpty)
            {
                return;
            }

            Reclaim();
            if (_free.Count == 0)
            {
                return;
            }

            uint buffer = _free.Dequeue();
            int frames = interleaved.Length / 2;
            fixed (short* data = interleaved)
            {
                _al.BufferData(buffer, BufferFormat.Stereo16, data, frames * BytesPerFrame, SampleRate);
            }

            _al.SourceQueueBuffers(_source, 1, &buffer);
            _framesInBuffer[buffer] = frames;
            _queuedFrames += frames;

            // Start, or restart after an underrun (a starved source stops by itself).
            _al.GetSourceProperty(_source, GetSourceInteger.SourceState, out int state);
            if (state != (int)SourceState.Playing)
            {
                _al.SourcePlay(_source);
            }
        }

        /// <summary>Takes the buffers OpenAL has finished playing back into the free pool.</summary>
        private void Reclaim()
        {
            if (_al is null)
            {
                return;
            }

            _al.GetSourceProperty(_source, GetSourceInteger.BuffersProcessed, out int processed);
            while (processed-- > 0)
            {
                uint buffer;
                _al.SourceUnqueueBuffers(_source, 1, &buffer);
                if (_framesInBuffer.Remove(buffer, out int frames))
                {
                    _queuedFrames -= frames;
                }

                _free.Enqueue(buffer);
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _open = false;
            try
            {
                if (_al is not null && _context != null)
                {
                    if (_source != 0)
                    {
                        _al.SourceStop(_source);
                        _al.SetSourceProperty(_source, SourceInteger.Buffer, 0);
                        _al.DeleteSource(_source);
                        _source = 0;
                    }

                    foreach (uint buffer in _free)
                    {
                        _al.DeleteBuffer(buffer);
                    }

                    foreach (uint buffer in _framesInBuffer.Keys)
                    {
                        _al.DeleteBuffer(buffer);
                    }

                    _free.Clear();
                    _framesInBuffer.Clear();
                }

                if (_alc is not null)
                {
                    if (_context != null)
                    {
                        _alc.MakeContextCurrent(null);
                        _alc.DestroyContext(_context);
                        _context = null;
                    }

                    if (_device != null)
                    {
                        _alc.CloseDevice(_device);
                        _device = null;
                    }
                }
            }
            catch (Exception ex)
            {
                GordianLog.Warn("AUDIO", $"OpenAL Soft shutdown failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Loads OpenAL Soft through Silk.NET's lookup, falling back to the copy NuGet placed under
        /// <c>runtimes/&lt;rid&gt;/native</c> beside the app (a foreign host such as the test runner does not describe it).
        /// </summary>
        private static (ALContext Alc, AL Al) LoadApis()
        {
            try
            {
                return (ALContext.GetApi(true), AL.GetApi(true));
            }
            catch (Exception) when (FindBundledLibrary() is string path)
            {
                GordianLog.Info("AUDIO", $"Loading bundled OpenAL Soft from {path}.");
                return (new ALContext(new DefaultNativeContext(path)), new AL(new DefaultNativeContext(path)));
            }
        }

        /// <summary>The bundled native library for this OS and architecture, or null.</summary>
        internal static string? FindBundledLibrary()
        {
            string os = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux";
            string fileName = os switch
            {
                "win" => "soft_oal.dll",
                "osx" => "libopenal.dylib",
                _ => "libopenal.so",
            };
            string arch = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
            foreach (string rid in new[] { RuntimeInformation.RuntimeIdentifier, $"{os}-{arch}" })
            {
                string candidate = Path.Combine(AppContext.BaseDirectory, "runtimes", rid, "native", fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }
    }
}
