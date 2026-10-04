// src/Gordian.App/Audio/MusicDirector.cs
using System;
using Gordian.Core.Audio;
using Gordian.Core.World;

namespace Gordian.App.Audio
{
    /// <summary>What the music depends on, sampled once a frame from the local player.</summary>
    /// <param name="Status">The player's server animation status (<c>WorldEntity.AnimationState</c>: 1 engaged, 3 dead, 5 chocobo, 6 / 38-62 fishing, 85 mount).</param>
    /// <param name="InParty">Whether the player is in a party (picks the party battle slot).</param>
    /// <param name="VanaHour">Vana'diel time of day in hours, 0-24.</param>
    /// <param name="InMogHouse">Whether the player is in the Mog House.</param>
    public readonly record struct MusicContext(byte Status, bool InParty, float VanaHour, bool InMogHouse = false);

    /// <summary>
    /// Picks the music slot for the player's situation and plays its track: zone day / night, solo or party battle while
    /// engaged, mount, dead, Mog House, fishing. A slot the server left at 0 falls back to the zone music. A track change
    /// fades the old track out and then starts the new one from the top; the same track in the new slot keeps playing.
    /// An event can override the zone's choice (<see cref="SetOverride"/>, opcodes 0x5C / 0x5D).
    /// </summary>
    /// <remarks>
    /// Slot meanings referenced from XiPackets <c>world/server/0x005F</c> (https://github.com/atom0s/XiPackets).
    /// Provisional, not yet compared with retail: night music from 18:00 to 06:00, battle music only while the local player
    /// is engaged, the 1.5 s fade-out and the 0x060 time unit (1/60 s).
    /// </remarks>
    public sealed class MusicDirector
    {
        /// <summary>Night starts at this Vana'diel hour (provisional).</summary>
        public const float NightStartHour = 18f;

        /// <summary>Day starts at this Vana'diel hour (provisional).</summary>
        public const float DayStartHour = 6f;

        /// <summary>Fade-out before a different track starts (provisional).</summary>
        public const float TrackFadeSeconds = 1.5f;

        private readonly AudioMixer _mixer;
        private readonly Func<int, IPcmSource?> _openMusic;
        private readonly Action<Action> _runInBackground;
        private int _currentTrack;
        private int _currentHandle;
        private int _startToken;
        private int _loggedTrack = -1;
        private int _loading;
        private int _pendingTrack;
        private double _pendingDelay;
        private int _overrideTrack = -1;
        private int _volumeVersion = -1;

        /// <summary>Builds a director over the mixer and a music opener (usually <see cref="SoundLibrary.OpenMusic"/>).</summary>
        /// <param name="mixer">The mixer.</param>
        /// <param name="openMusic">Opens a track; it reads a multi-megabyte file, so it runs through <paramref name="runInBackground"/>.</param>
        /// <param name="runInBackground">Runs the open off the frame thread; null uses the thread pool.</param>
        public MusicDirector(AudioMixer mixer, Func<int, IPcmSource?> openMusic, Action<Action>? runInBackground = null)
        {
            _mixer = mixer;
            _openMusic = openMusic;
            _runInBackground = runInBackground ?? (work => System.Threading.Tasks.Task.Run(work));
        }

        /// <summary>The track playing (or about to), 0 for silence.</summary>
        public int CurrentTrack => _pendingDelay > 0 ? _pendingTrack : _currentTrack;

        /// <summary>An event's master volume (opcodes 0x69 / 0x6A, mask 0x08), applied to the music bus with the music volume.</summary>
        public float ScriptMaster { get; private set; } = 1f;

        /// <summary>
        /// Whether the music has settled on its track: no fade-out pending and the track loaded (or silence). Event opcode
        /// 0x9A waits for this.
        /// </summary>
        public bool IsSettled => _pendingDelay <= 0 && System.Threading.Volatile.Read(ref _loading) == 0;

        /// <summary>Sets the event master volume; the music bus is re-levelled on the next <see cref="Update"/>.</summary>
        public void SetScriptMaster(float gain)
        {
            ScriptMaster = Math.Clamp(gain, 0f, 1f);
            _volumeVersion = -1;
        }

        /// <summary>The slot the last <see cref="Update"/> chose.</summary>
        public MusicSlot CurrentSlot { get; private set; }

        /// <summary>
        /// Plays <paramref name="musicNum"/> instead of the zone's choice until <see cref="ClearOverride"/>; 0 is silence.
        /// </summary>
        public void SetOverride(int musicNum) => _overrideTrack = Math.Max(0, musicNum);

        /// <summary>Returns to the zone's music.</summary>
        public void ClearOverride() => _overrideTrack = -1;

        /// <summary>Whether an event override is active.</summary>
        public bool HasOverride => _overrideTrack >= 0;

        /// <summary>The slot the retail situation calls for, before the empty-slot fallback.</summary>
        public static MusicSlot ChooseSlot(in MusicContext context)
        {
            byte s = context.Status;
            if (s == 3)
            {
                return MusicSlot.Dead;
            }

            if (s == 1)
            {
                return context.InParty ? MusicSlot.BattleParty : MusicSlot.BattleSolo;
            }

            if (s == 5 || s == 85)
            {
                return MusicSlot.Mount;
            }

            if (s == 6 || (s >= 38 && s <= 62))
            {
                return MusicSlot.Fishing;
            }

            if (context.InMogHouse)
            {
                return MusicSlot.MogHouse;
            }

            return IsNight(context.VanaHour) ? MusicSlot.ZoneNight : MusicSlot.ZoneDay;
        }

        /// <summary>Whether a Vana'diel hour plays the night music.</summary>
        public static bool IsNight(float hour) => hour >= NightStartHour || hour < DayStartHour;

        /// <summary>The track for the situation: the chosen slot's, else the zone's day / night track.</summary>
        public static int ChooseTrack(ZoneMusicState music, in MusicContext context, out MusicSlot slot)
        {
            slot = ChooseSlot(context);
            int track = music.Get(slot);
            if (track == 0 && slot is not (MusicSlot.ZoneDay or MusicSlot.ZoneNight))
            {
                slot = IsNight(context.VanaHour) ? MusicSlot.ZoneNight : MusicSlot.ZoneDay;
                track = music.Get(slot);
            }

            if (track == 0 && slot == MusicSlot.ZoneNight)
            {
                track = music.Get(MusicSlot.ZoneDay);
            }

            return track;
        }

        /// <summary>Advances fades and switches tracks; call once a frame.</summary>
        public void Update(ZoneMusicState music, in MusicContext context, double deltaSeconds)
        {
            int volumeVersion = music.Version;
            if (volumeVersion != _volumeVersion)
            {
                _volumeVersion = volumeVersion;
                _mixer.FadeCategory(AudioCategory.Music, ScriptMaster * music.Volume / ZoneMusicState.MaxVolume, music.VolumeFadeTime / 60f);
            }

            int wanted;
            if (_overrideTrack >= 0)
            {
                wanted = _overrideTrack;
            }
            else
            {
                wanted = ChooseTrack(music, context, out MusicSlot slot);
                CurrentSlot = slot;
            }

            if (_pendingDelay > 0)
            {
                _pendingTrack = wanted;
                _pendingDelay -= deltaSeconds;
                if (_pendingDelay <= 0)
                {
                    Start(_pendingTrack);
                }

                return;
            }

            if (wanted == _currentTrack)
            {
                return;
            }

            if (wanted != _loggedTrack)
            {
                _loggedTrack = wanted;
                Gordian.Core.Diagnostics.GordianLog.Info("AUDIO", $"Music: slot {(_overrideTrack >= 0 ? "override" : CurrentSlot.ToString())} → track {wanted} (was {_currentTrack}).");
            }

            int handle = System.Threading.Volatile.Read(ref _currentHandle);
            if (handle != 0 && _mixer.IsPlaying(handle))
            {
                _mixer.Stop(handle, TrackFadeSeconds);
                System.Threading.Volatile.Write(ref _currentHandle, 0);
                _pendingTrack = wanted;
                _pendingDelay = TrackFadeSeconds;
                return;
            }

            Start(wanted);
        }

        /// <summary>Stops the music at once (zone change, logout).</summary>
        public void StopAll(float fadeSeconds = 0f)
        {
            System.Threading.Interlocked.Increment(ref _startToken);
            int handle = System.Threading.Interlocked.Exchange(ref _currentHandle, 0);
            if (handle != 0)
            {
                _mixer.Stop(handle, fadeSeconds);
            }

            _currentTrack = 0;
            _pendingDelay = 0;
        }

        private void Start(int track)
        {
            _pendingDelay = 0;
            _currentTrack = track;
            System.Threading.Volatile.Write(ref _currentHandle, 0);
            int token = System.Threading.Interlocked.Increment(ref _startToken);
            if (track <= 0)
            {
                return;
            }

            System.Threading.Interlocked.Increment(ref _loading);
            _runInBackground(() =>
            {
                IPcmSource? source;
                try
                {
                    source = _openMusic(track);
                }
                finally
                {
                    System.Threading.Interlocked.Decrement(ref _loading);
                }

                if (source is null || System.Threading.Volatile.Read(ref _startToken) != token)
                {
                    return;
                }

                int handle = _mixer.Play(source, AudioCategory.Music);
                if (System.Threading.Interlocked.CompareExchange(ref _currentHandle, handle, 0) != 0
                    || System.Threading.Volatile.Read(ref _startToken) != token)
                {
                    // Superseded while loading.
                    _mixer.Stop(handle);
                }
            });
        }
    }
}
