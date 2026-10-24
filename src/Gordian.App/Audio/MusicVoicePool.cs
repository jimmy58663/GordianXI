// src/Gordian.App/Audio/MusicVoicePool.cs
using System;
using System.Collections.Generic;
using Gordian.Engine.Audio;

namespace Gordian.App.Audio
{
    /// <summary>
    /// The music voices of every logged-in character, one per track (round 2 of #368 / #377: music continuity across
    /// character switches). Each character's <see cref="MusicDirector"/> holds the track its situation calls for; characters
    /// wanting the same track (the same zone, the same slot) share one voice. Every held track keeps playing, the ones not
    /// heard faded to silence, so a switch to another character picks its music up where it is now rather than from the
    /// top, and a switch between characters on the same track does not touch the music at all. A track nobody holds any
    /// more stops. GordianXI behaviour (retail has one character); the crossfade length is provisional.
    /// </summary>
    public sealed class MusicVoicePool
    {
        /// <summary>Crossfade between two characters' tracks on a switch, and fade-in when joining a track already playing.</summary>
        public const float CrossfadeSeconds = 0.75f;

        private sealed class Entry
        {
            public required int Track;
            public int Handle;
            public int Token;
            public bool Loading;
            public readonly HashSet<object> Owners = new();
        }

        private readonly AudioMixer _mixer;
        private readonly Func<int, IPcmSource?> _openMusic;
        private readonly Action<Action> _runInBackground;
        private readonly Dictionary<int, Entry> _tracks = new();
        private readonly object _lock = new();
        private int _audible;
        private int _nextToken;

        /// <param name="mixer">The mixer.</param>
        /// <param name="openMusic">Opens a track; it reads a multi-megabyte file, so it runs through <paramref name="runInBackground"/>.</param>
        /// <param name="runInBackground">Runs the open off the frame thread; null uses the thread pool.</param>
        public MusicVoicePool(AudioMixer mixer, Func<int, IPcmSource?> openMusic, Action<Action>? runInBackground = null)
        {
            _mixer = mixer ?? throw new ArgumentNullException(nameof(mixer));
            _openMusic = openMusic ?? throw new ArgumentNullException(nameof(openMusic));
            _runInBackground = runInBackground ?? (work => System.Threading.Tasks.Task.Run(work));
        }

        /// <summary>The track heard (0: none).</summary>
        public int AudibleTrack
        {
            get
            {
                lock (_lock) return _audible;
            }
        }

        /// <summary>The tracks held (playing or loading), for diagnostics and tests.</summary>
        public int[] Tracks
        {
            get
            {
                lock (_lock)
                {
                    var tracks = new int[_tracks.Count];
                    _tracks.Keys.CopyTo(tracks, 0);
                    Array.Sort(tracks);
                    return tracks;
                }
            }
        }

        /// <summary>The mixer handle playing <paramref name="track"/>, 0 while it loads or when nobody holds it.</summary>
        public int HandleOf(int track)
        {
            lock (_lock) return _tracks.TryGetValue(track, out var entry) ? entry.Handle : 0;
        }

        /// <summary>Whether a held track is still being opened.</summary>
        public bool IsLoading(int track)
        {
            lock (_lock) return _tracks.TryGetValue(track, out var entry) && entry.Loading;
        }

        /// <summary>
        /// <paramref name="owner"/> wants <paramref name="track"/>. Returns true when that started the track (from the top,
        /// audible at once if it is the heard track); false when it joined a track already playing.
        /// </summary>
        public bool Acquire(object owner, int track)
        {
            ArgumentNullException.ThrowIfNull(owner);
            if (track <= 0) return false;
            Entry entry;
            int token;
            lock (_lock)
            {
                if (_tracks.TryGetValue(track, out var existing))
                {
                    existing.Owners.Add(owner);
                    return false;
                }

                token = ++_nextToken;
                entry = new Entry { Track = track, Token = token, Loading = true };
                entry.Owners.Add(owner);
                _tracks[track] = entry;
            }

            _runInBackground(() =>
            {
                IPcmSource? source = null;
                try
                {
                    source = _openMusic(track);
                }
                finally
                {
                    lock (_lock) entry.Loading = false;
                }

                if (source is null) return;
                lock (_lock)
                {
                    if (!_tracks.TryGetValue(track, out var current) || current.Token != token)
                    {
                        return; // Released while loading.
                    }

                    int handle = _mixer.Play(source, AudioCategory.Music);
                    // A track no one hears starts silent; queued before the first mix, so nothing leaks out.
                    if (_audible != track) _mixer.FadeVoice(handle, 0f, 0f);
                    current.Handle = handle;
                }
            });
            return true;
        }

        /// <summary>
        /// <paramref name="owner"/> no longer wants <paramref name="track"/>; the last one to let go stops it, fading over
        /// <paramref name="fadeSeconds"/>.
        /// </summary>
        public void Release(object owner, int track, float fadeSeconds)
        {
            ArgumentNullException.ThrowIfNull(owner);
            lock (_lock)
            {
                if (!_tracks.TryGetValue(track, out var entry) || !entry.Owners.Remove(owner) || entry.Owners.Count > 0) return;
                _tracks.Remove(track);
                if (entry.Handle != 0) _mixer.Stop(entry.Handle, fadeSeconds);
            }
        }

        /// <summary>
        /// Makes <paramref name="track"/> the one heard (0: none): the track heard until now fades to silence over
        /// <paramref name="fadeOutSeconds"/> and keeps playing while someone holds it, the new one fades in over
        /// <paramref name="fadeInSeconds"/> (a track still loading starts audible).
        /// </summary>
        public void SetAudible(int track, float fadeOutSeconds, float fadeInSeconds)
        {
            track = Math.Max(0, track);
            lock (_lock)
            {
                if (track == _audible) return;
                if (_tracks.TryGetValue(_audible, out var old) && old.Handle != 0) _mixer.FadeVoice(old.Handle, 0f, fadeOutSeconds);
                _audible = track;
                if (_tracks.TryGetValue(track, out var heard) && heard.Handle != 0) _mixer.FadeVoice(heard.Handle, 1f, fadeInSeconds);
            }
        }
    }
}
