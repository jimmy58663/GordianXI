// src/Gordian.App/Audio/SoundLibrary.cs
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Gordian.Core.Audio;
using Gordian.Core.Diagnostics;

namespace Gordian.App.Audio
{
    /// <summary>
    /// Loads retail sounds by id: sound effects are decoded whole once and cached (they are small and replayed often),
    /// music is streamed from the file's bytes, both through <see cref="FfxiSoundDecoder"/>. Files that cannot be decoded (ATRAC3 while no decoder is registered, the encrypted <c>.spw</c> variant)
    /// are logged once and play as silence.
    /// </summary>
    public sealed class SoundLibrary
    {
        private const long MaxCachedSamples = 96L * 1024 * 1024 / sizeof(short);

        private readonly FfxiSoundLocator _locator;
        private readonly ConcurrentDictionary<int, Task<PcmClip?>> _effects = new();
        private readonly ConcurrentDictionary<int, byte> _reported = new();
        private long _cachedSamples;

        /// <summary>Builds a library over a locator.</summary>
        public SoundLibrary(FfxiSoundLocator locator)
        {
            _locator = locator;
        }

        /// <summary>The locator in use.</summary>
        public FfxiSoundLocator Locator => _locator;

        /// <summary>Loads (once, on a worker) and returns a sound effect's clip; the task's result is null when it cannot play.</summary>
        public Task<PcmClip?> GetEffectAsync(int soundId)
        {
            if (soundId <= 0)
            {
                return Task.FromResult<PcmClip?>(null);
            }

            if (Volatile.Read(ref _cachedSamples) > MaxCachedSamples)
            {
                // Rarely reached (the whole effect library is ~1 GB of PCM); start over rather than grow without bound.
                _effects.Clear();
                Interlocked.Exchange(ref _cachedSamples, 0);
            }

            return _effects.GetOrAdd(soundId, id => Task.Run(() => LoadEffect(id)));
        }

        /// <summary>Opens a music track as a stream, or null when it is missing or cannot be decoded.</summary>
        public IPcmSource? OpenMusic(int musicId, bool? loop = null)
        {
            string? path = _locator.FindMusic(musicId);
            if (path is null)
            {
                Report(-musicId, $"music {musicId} not found in any sound root.");
                return null;
            }

            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                IPcmSource? stream = FfxiSoundDecoder.Open(bytes, loop);
                if (stream is null)
                {
                    ReportUndecodable(-musicId, $"music {musicId}", bytes);
                }

                return stream;
            }
            catch (Exception ex)
            {
                Report(-musicId, $"music {musicId} failed to load: {ex.Message}");
                return null;
            }
        }

        private PcmClip? LoadEffect(int soundId)
        {
            string? path = _locator.FindEffect(soundId);
            if (path is null)
            {
                Report(soundId, $"sound effect {soundId} not found in any sound root.");
                return null;
            }

            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                PcmClip? clip = FfxiSoundDecoder.DecodeClip(bytes);
                if (clip is null)
                {
                    ReportUndecodable(soundId, $"sound effect {soundId}", bytes);
                    return null;
                }

                Interlocked.Add(ref _cachedSamples, clip.Samples.Length);
                return clip;
            }
            catch (Exception ex)
            {
                Report(soundId, $"sound effect {soundId} failed to load: {ex.Message}");
                return null;
            }
        }

        private void ReportUndecodable(int key, string what, byte[] bytes)
        {
            string reason = FfxiSoundHeader.TryParse(bytes, out FfxiSoundHeader h)
                ? h.IsEncrypted ? "encrypted variant" : h.Format == FfxiSampleFormat.Atrac3 && FfxiSoundDecoder.Atrac3 is null ? "no ATRAC3 decoder is registered" : $"{h.Format} did not decode"
                : "unknown header";
            Report(key, $"{what} cannot play: {reason}.");
        }

        private void Report(int key, string message)
        {
            if (_reported.TryAdd(key, 0))
            {
                GordianLog.Info("AUDIO", message);
            }
        }
    }
}
