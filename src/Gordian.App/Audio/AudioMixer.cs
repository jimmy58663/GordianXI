// src/Gordian.App/Audio/AudioMixer.cs
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using Gordian.Core.Audio;

namespace Gordian.App.Audio
{
    /// <summary>How a positional voice fades with distance from the listener.</summary>
    /// <param name="Position">World position (internal axes: Y is height).</param>
    /// <param name="Near">Distance inside which the voice plays at full volume.</param>
    /// <param name="Far">Distance at and beyond which the voice is silent.</param>
    public readonly record struct AudioEmitter(Vector3 Position, float Near, float Far);

    /// <summary>
    /// A backend-neutral software mixer: voices of <see cref="IPcmSource"/> at any rate are resampled (linear) to the
    /// output rate, scaled by voice, fade, category and master gains, panned, summed and clipped to 16-bit stereo.
    /// </summary>
    /// <remarks>
    /// Every public method is thread safe: calls from the game thread queue a command that the mixing thread applies at
    /// the start of its next <see cref="Mix"/>, so the voice list is only ever touched by the mixing thread and
    /// <see cref="Mix"/> does not allocate.
    /// </remarks>
    public sealed class AudioMixer
    {
        /// <summary>Number of <see cref="AudioCategory"/> buses.</summary>
        public const int CategoryCount = 5;

        private const int MaxVoices = 96;
        private const int ScratchFrames = 1024;

        private readonly ConcurrentQueue<Action> _commands = new();
        private readonly List<Voice> _voices = new();
        private readonly float[] _categoryGain = { 1f, 1f, 1f, 1f, 1f };
        private readonly float[] _categoryFade = { 1f, 1f, 1f, 1f, 1f };
        private readonly float[] _categoryFadeTarget = { 1f, 1f, 1f, 1f, 1f };
        private readonly float[] _categoryFadeStep = new float[CategoryCount];
        private readonly float[] _controlGain = { 1f, 1f, 1f, 1f, 1f };
        private readonly float[] _controlTarget = { 1f, 1f, 1f, 1f, 1f };
        private readonly float[] _controlStep = new float[CategoryCount];
        private readonly short[] _scratch = new short[ScratchFrames * 2 + 4];
        private float[] _accumulator = Array.Empty<float>();
        private float _masterGain = 1f;
        private Vector3 _listenerPosition;
        private Vector3 _listenerRight = Vector3.UnitX;
        private int _nextHandle;
        private int _activeVoices;

        /// <summary>Builds a mixer for a stereo output at <paramref name="outputRate"/> Hz.</summary>
        public AudioMixer(int outputRate = 48000)
        {
            OutputRate = outputRate;
        }

        /// <summary>Output sample rate in Hz.</summary>
        public int OutputRate { get; }

        /// <summary>Voices playing after the last mix (diagnostics).</summary>
        public int ActiveVoices => Volatile.Read(ref _activeVoices);

        /// <summary>Starts a voice. Returns its handle (never 0).</summary>
        /// <param name="source">The PCM to play; the mixer owns it from now on.</param>
        /// <param name="category">The bus it plays on.</param>
        /// <param name="volume">Linear gain, 0-1 (more amplifies).</param>
        /// <param name="emitter">For a positional voice, where it is and how it fades; null plays it centred.</param>
        /// <param name="fadeInSeconds">Fade-in time; 0 starts at full volume.</param>
        /// <param name="pan">Constant pan, -1 left to 1 right, for a non-positional voice.</param>
        public int Play(IPcmSource source, AudioCategory category, float volume = 1f, AudioEmitter? emitter = null,
            float fadeInSeconds = 0f, float pan = 0f)
        {
            int handle = Interlocked.Increment(ref _nextHandle);
            if (handle == 0)
            {
                handle = Interlocked.Increment(ref _nextHandle);
            }

            _commands.Enqueue(() =>
            {
                if (_voices.Count >= MaxVoices)
                {
                    // Drop the quietest non-music voice to make room.
                    int victim = -1;
                    float quietest = float.MaxValue;
                    for (int i = 0; i < _voices.Count; i++)
                    {
                        Voice v = _voices[i];
                        if (v.Category != AudioCategory.Music && v.LastGain < quietest)
                        {
                            quietest = v.LastGain;
                            victim = i;
                        }
                    }

                    if (victim < 0)
                    {
                        MarkFinished(handle);
                        return;
                    }

                    RemoveAt(victim);
                }

                var voice = new Voice(handle, source, category, OutputRate)
                {
                    Volume = volume,
                    Emitter = emitter,
                    Pan = Math.Clamp(pan, -1f, 1f),
                };
                if (fadeInSeconds > 0)
                {
                    voice.Fade = 0f;
                    voice.FadeTarget = 1f;
                    voice.FadeStep = 1f / (fadeInSeconds * OutputRate);
                }

                _voices.Add(voice);
            });
            return handle;
        }

        /// <summary>Stops a voice, fading it out over <paramref name="fadeSeconds"/> (0 = at once).</summary>
        public void Stop(int handle, float fadeSeconds = 0f) =>
            _commands.Enqueue(() =>
            {
                Voice? v = Find(handle);
                if (v is null)
                {
                    return;
                }

                if (fadeSeconds <= 0)
                {
                    RemoveAt(_voices.IndexOf(v));
                    return;
                }

                v.FadeTarget = 0f;
                v.FadeStep = Math.Max(v.Fade, 1e-6f) / (fadeSeconds * OutputRate);
                v.StopWhenFaded = true;
            });

        /// <summary>Stops every voice of a category.</summary>
        public void StopCategory(AudioCategory category, float fadeSeconds = 0f) =>
            _commands.Enqueue(() =>
            {
                for (int i = _voices.Count - 1; i >= 0; i--)
                {
                    Voice v = _voices[i];
                    if (v.Category != category)
                    {
                        continue;
                    }

                    if (fadeSeconds <= 0)
                    {
                        RemoveAt(i);
                    }
                    else
                    {
                        v.FadeTarget = 0f;
                        v.FadeStep = Math.Max(v.Fade, 1e-6f) / (fadeSeconds * OutputRate);
                        v.StopWhenFaded = true;
                    }
                }
            });

        /// <summary>Changes a voice's gain.</summary>
        public void SetVolume(int handle, float volume) =>
            _commands.Enqueue(() =>
            {
                if (Find(handle) is Voice v)
                {
                    v.Volume = volume;
                }
            });

        /// <summary>Moves a positional voice.</summary>
        public void SetEmitter(int handle, AudioEmitter emitter) =>
            _commands.Enqueue(() =>
            {
                if (Find(handle) is Voice v)
                {
                    v.Emitter = emitter;
                }
            });

        /// <summary>Sets where the listener is and which way is its right (internal axes, Y up).</summary>
        public void SetListener(Vector3 position, Vector3 right) =>
            _commands.Enqueue(() =>
            {
                _listenerPosition = position;
                float len = right.Length();
                _listenerRight = len > 1e-5f ? right / len : Vector3.UnitX;
            });

        /// <summary>Sets the master gain (0-1).</summary>
        public void SetMasterVolume(float gain) =>
            _commands.Enqueue(() => _masterGain = Math.Clamp(gain, 0f, 1f));

        /// <summary>Sets a category's user gain (0-1), the setting the volume sliders control.</summary>
        public void SetCategoryVolume(AudioCategory category, float gain) =>
            _commands.Enqueue(() => _categoryGain[(int)category] = Math.Clamp(gain, 0f, 1f));

        /// <summary>
        /// Eases a category's script gain (0-1) to <paramref name="target"/> over <paramref name="seconds"/>; the event
        /// VM and the server's music volume packet use this, on top of the user's slider.
        /// </summary>
        public void FadeCategory(AudioCategory category, float target, float seconds) =>
            _commands.Enqueue(() => StartFade(_categoryFade, _categoryFadeTarget, _categoryFadeStep, (int)category, target, seconds));

        /// <summary>
        /// Eases a category's control gain (0-1) to <paramref name="target"/> over <paramref name="seconds"/>: the GordianXI
        /// sound controls (#265: master sound off, mute while inactive, per-category switches). It is a third gain of its
        /// own, so it never disturbs the retail sliders or an event's script fade; it stays 1 unless the user opts in.
        /// </summary>
        public void FadeControl(AudioCategory category, float target, float seconds) =>
            _commands.Enqueue(() => StartFade(_controlGain, _controlTarget, _controlStep, (int)category, target, seconds));

        private void StartFade(float[] value, float[] targets, float[] steps, int c, float target, float seconds)
        {
            target = Math.Clamp(target, 0f, 1f);
            targets[c] = target;
            if (seconds <= 0)
            {
                value[c] = target;
                steps[c] = 0;
                return;
            }

            steps[c] = Math.Abs(target - value[c]) / (seconds * OutputRate);
        }

        private static void AdvanceFade(float[] value, float[] targets, float[] steps, int c, int frames)
        {
            if (steps[c] <= 0)
            {
                return;
            }

            float step = steps[c] * frames;
            float cur = value[c];
            float tgt = targets[c];
            cur = cur < tgt ? Math.Min(tgt, cur + step) : Math.Max(tgt, cur - step);
            value[c] = cur;
            if (cur == tgt)
            {
                steps[c] = 0;
            }
        }

        /// <summary>Whether a voice is still playing (as of the last mix).</summary>
        public bool IsPlaying(int handle)
        {
            lock (_finished)
            {
                return handle > 0 && handle <= Volatile.Read(ref _nextHandle) && !_finished.Contains(handle);
            }
        }

        private readonly HashSet<int> _finished = new();

        private void MarkFinished(int handle)
        {
            lock (_finished)
            {
                if (_finished.Count > 8192)
                {
                    _finished.Clear();
                }

                _finished.Add(handle);
            }
        }

        private void RemoveAt(int index)
        {
            MarkFinished(_voices[index].Handle);
            _voices.RemoveAt(index);
        }

        /// <summary>
        /// Mixes <paramref name="output"/>.Length / 2 stereo frames of interleaved 16-bit PCM. Called from the mixing thread.
        /// </summary>
        public void Mix(Span<short> output)
        {
            while (_commands.TryDequeue(out Action? command))
            {
                command();
            }

            int frames = output.Length / 2;
            if (_accumulator.Length < frames * 2)
            {
                _accumulator = new float[frames * 2];
            }

            Span<float> acc = _accumulator.AsSpan(0, frames * 2);
            acc.Clear();

            // Category script and control fades advance per frame block (fine-grained enough for second-long fades).
            Span<float> busGain = stackalloc float[CategoryCount];
            for (int c = 0; c < CategoryCount; c++)
            {
                AdvanceFade(_categoryFade, _categoryFadeTarget, _categoryFadeStep, c, frames);
                AdvanceFade(_controlGain, _controlTarget, _controlStep, c, frames);
                busGain[c] = _categoryGain[c] * _categoryFade[c] * _controlGain[c] * _masterGain;
            }

            for (int i = _voices.Count - 1; i >= 0; i--)
            {
                Voice v = _voices[i];
                bool alive = MixVoice(v, acc, frames, busGain[(int)v.Category]);
                if (!alive)
                {
                    RemoveAt(i);
                }
            }

            Volatile.Write(ref _activeVoices, _voices.Count);
            for (int i = 0; i < acc.Length; i++)
            {
                float s = acc[i] * 32767f;
                output[i] = s >= 32767f ? short.MaxValue : s <= -32768f ? short.MinValue : (short)s;
            }
        }

        private bool MixVoice(Voice v, Span<float> acc, int frames, float bus)
        {
            float left;
            float right;
            if (v.Emitter is AudioEmitter e)
            {
                Vector3 offset = e.Position - _listenerPosition;
                float distance = offset.Length();
                float attenuation = Attenuation(distance, e.Near, e.Far);
                float pan = distance > 1e-3f ? Math.Clamp(Vector3.Dot(offset / distance, _listenerRight), -1f, 1f) : 0f;
                // Keep some of the far ear so sounds never vanish from one side (provisional).
                pan *= 0.8f;
                (left, right) = PanGains(pan);
                left *= attenuation;
                right *= attenuation;
            }
            else
            {
                (left, right) = PanGains(v.Pan);
            }

            float gain = v.Volume * bus;
            v.LastGain = gain * Math.Max(left, right);
            int channels = v.Source.Channels;
            double step = v.Step;
            for (int f = 0; f < frames; f++)
            {
                // Fill the two-frame interpolation window.
                while (v.Frac >= 1.0)
                {
                    if (!v.Advance(_scratch))
                    {
                        return false;
                    }

                    v.Frac -= 1.0;
                }

                float t = (float)v.Frac;
                float a = v.Prev0 + (v.Cur0 - v.Prev0) * t;
                float b = channels == 2 ? v.Prev1 + (v.Cur1 - v.Prev1) * t : a;

                if (v.FadeStep > 0)
                {
                    v.Fade = v.Fade < v.FadeTarget ? Math.Min(v.FadeTarget, v.Fade + v.FadeStep) : Math.Max(v.FadeTarget, v.Fade - v.FadeStep);
                    if (v.Fade == v.FadeTarget)
                    {
                        v.FadeStep = 0;
                        if (v.StopWhenFaded && v.Fade <= 0f)
                        {
                            return false;
                        }
                    }
                }

                float g = gain * v.Fade * (1f / 32768f);
                acc[f * 2] += a * g * left;
                acc[f * 2 + 1] += b * g * right;
                v.Frac += step;
            }

            return true;
        }

        /// <summary>Distance attenuation: full inside <paramref name="near"/>, linear down to silence at <paramref name="far"/> (provisional curve).</summary>
        public static float Attenuation(float distance, float near, float far)
        {
            if (distance <= near)
            {
                return 1f;
            }

            if (far <= near || distance >= far)
            {
                return 0f;
            }

            return 1f - (distance - near) / (far - near);
        }

        /// <summary>Constant-power pan gains for -1 (left) to 1 (right).</summary>
        public static (float Left, float Right) PanGains(float pan)
        {
            // Constant power, scaled so the centre is unity on both sides like an unpanned voice.
            float angle = (Math.Clamp(pan, -1f, 1f) + 1f) * MathF.PI / 4f;
            return (Math.Min(1f, MathF.Cos(angle) * MathF.Sqrt(2f)), Math.Min(1f, MathF.Sin(angle) * MathF.Sqrt(2f)));
        }

        private Voice? Find(int handle)
        {
            foreach (Voice v in _voices)
            {
                if (v.Handle == handle)
                {
                    return v;
                }
            }

            return null;
        }

        private sealed class Voice
        {
            private readonly short[] _buffer;
            private int _count;
            private int _pos;
            private bool _ended;

            public Voice(int handle, IPcmSource source, AudioCategory category, int outputRate)
            {
                Handle = handle;
                Source = source;
                Category = category;
                Step = source.SampleRate / (double)outputRate;
                _buffer = new short[Math.Max(1, source.Channels) * 512];
                Frac = 2.0; // forces the first two frames to load
            }

            public int Handle { get; }
            public IPcmSource Source { get; }
            public AudioCategory Category { get; }
            public double Step { get; }
            public float Volume { get; set; } = 1f;
            public float Pan { get; set; }
            public AudioEmitter? Emitter { get; set; }
            public float Fade { get; set; } = 1f;
            public float FadeTarget { get; set; } = 1f;
            public float FadeStep { get; set; }
            public bool StopWhenFaded { get; set; }
            public float LastGain { get; set; } = 1f;
            public double Frac;
            public float Prev0, Prev1, Cur0, Cur1;

            /// <summary>Shifts the interpolation window one source frame. False when the source has ended.</summary>
            public bool Advance(short[] scratch)
            {
                int channels = Source.Channels;
                if (_pos >= _count)
                {
                    if (_ended)
                    {
                        return false;
                    }

                    _count = Source.Read(_buffer);
                    _pos = 0;
                    if (_count <= 0)
                    {
                        _ended = true;
                        // Let the window run out through one silent frame so the tail is not clicked off.
                        Prev0 = Cur0;
                        Prev1 = Cur1;
                        Cur0 = 0;
                        Cur1 = 0;
                        return true;
                    }
                }

                Prev0 = Cur0;
                Prev1 = Cur1;
                Cur0 = _buffer[_pos];
                Cur1 = channels == 2 ? _buffer[_pos + 1] : Cur0;
                _pos += channels;
                return true;
            }
        }
    }
}
