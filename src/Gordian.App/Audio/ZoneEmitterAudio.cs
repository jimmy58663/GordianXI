// src/Gordian.App/Audio/ZoneEmitterAudio.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Gordian.Core.Audio;

namespace Gordian.App.Audio
{
    /// <summary>
    /// Plays the current zone's auto-run sound generators (<see cref="ZoneSoundEmitter"/>) around the listener: a source
    /// starts when the listener comes within its range and its time-of-day volume is above zero, follows its path, and
    /// stops (with a short fade) when either ends. Looped sound files loop; one-shot files are replayed when they end,
    /// standing in for the generator emitting again (provisional).
    /// </summary>
    public sealed class ZoneEmitterAudio
    {
        /// <summary>Extra distance past the range before a source stops, so the edge does not stutter.</summary>
        public const float StopMargin = 5f;

        /// <summary>Fade when a source stops (provisional).</summary>
        public const float FadeSeconds = 0.5f;

        private readonly AudioMixer _mixer;
        private readonly Func<int, Task<PcmClip?>> _loadClip;
        private IReadOnlyList<ZoneSoundEmitter> _emitters = Array.Empty<ZoneSoundEmitter>();
        private Slot[] _slots = Array.Empty<Slot>();

        /// <summary>Builds the player over the mixer and a clip loader (<see cref="SoundLibrary.GetEffectAsync"/>).</summary>
        public ZoneEmitterAudio(AudioMixer mixer, Func<int, Task<PcmClip?>> loadClip)
        {
            _mixer = mixer;
            _loadClip = loadClip;
        }

        /// <summary>Sources currently playing (diagnostics).</summary>
        public int PlayingCount
        {
            get
            {
                int n = 0;
                foreach (Slot s in _slots)
                {
                    n += s.Active ? 1 : 0;
                }

                return n;
            }
        }

        /// <summary>Replaces the zone's sources, stopping every playing one.</summary>
        public void SetEmitters(IReadOnlyList<ZoneSoundEmitter> emitters)
        {
            StopAll(FadeSeconds);
            _emitters = emitters;
            _slots = new Slot[emitters.Count];
            for (int i = 0; i < _slots.Length; i++)
            {
                _slots[i] = new Slot();
            }
        }

        /// <summary>Starts, moves, re-levels and stops sources for the listener and time of day.</summary>
        /// <param name="listener">The listener position (internal space).</param>
        /// <param name="dayFraction">Vana'diel time of day as a fraction (0 = midnight).</param>
        public void Update(Vector3 listener, float dayFraction)
        {
            IReadOnlyList<ZoneSoundEmitter> emitters = _emitters;
            Slot[] slots = _slots;
            for (int i = 0; i < emitters.Count && i < slots.Length; i++)
            {
                ZoneSoundEmitter e = emitters[i];
                Slot slot = slots[i];
                if (!e.AutoRun)
                {
                    continue;
                }

                Vector3 source = e.SourceFor(listener);
                float distance = Vector3.Distance(source, listener);
                float volume = e.VolumeAt(dayFraction);
                bool wanted = distance <= e.Far + (slot.Active ? StopMargin : 0f) && volume > 0.001f;
                var emitter = new AudioEmitter(source, e.Near, e.Far);
                if (!wanted)
                {
                    if (slot.Active)
                    {
                        Stop(slot, FadeSeconds);
                    }

                    continue;
                }

                int handle = Volatile.Read(ref slot.Handle);
                if (slot.Active && handle != 0)
                {
                    if (_mixer.IsPlaying(handle))
                    {
                        _mixer.SetEmitter(handle, emitter);
                        _mixer.SetVolume(handle, volume);
                        continue;
                    }

                    // A one-shot ended: emit again.
                    Volatile.Write(ref slot.Handle, 0);
                    slot.Active = false;
                }

                if (!slot.Active)
                {
                    Start(slot, e.SoundId, volume, emitter);
                }
            }
        }

        /// <summary>Stops every source.</summary>
        public void StopAll(float fadeSeconds)
        {
            foreach (Slot slot in _slots)
            {
                if (slot.Active)
                {
                    Stop(slot, fadeSeconds);
                }
            }
        }

        private void Start(Slot slot, int soundId, float volume, AudioEmitter emitter)
        {
            slot.Active = true;
            int token = Interlocked.Increment(ref slot.Token);
            _ = _loadClip(soundId).ContinueWith(t =>
            {
                PcmClip? clip = t.Status == TaskStatus.RanToCompletion ? t.Result : null;
                if (clip is null || Volatile.Read(ref slot.Token) != token)
                {
                    return;
                }

                int handle = _mixer.Play(clip.Open(), AudioCategory.Zone, volume, emitter, fadeInSeconds: clip.IsLooped ? FadeSeconds : 0f);
                Volatile.Write(ref slot.Handle, handle);
                if (Volatile.Read(ref slot.Token) != token)
                {
                    _mixer.Stop(handle);
                }
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        private void Stop(Slot slot, float fadeSeconds)
        {
            slot.Active = false;
            Interlocked.Increment(ref slot.Token);
            int handle = Interlocked.Exchange(ref slot.Handle, 0);
            if (handle != 0)
            {
                _mixer.Stop(handle, fadeSeconds);
            }
        }

        private sealed class Slot
        {
            public bool Active;
            public int Handle;
            public int Token;
        }
    }
}
