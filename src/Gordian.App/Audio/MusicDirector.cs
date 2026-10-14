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
    /// An event can override the zone's choice (<see cref="SetOverride"/>, opcodes 0x5C / 0x5D). Every logged-in character
    /// has one; they play through a shared <see cref="MusicVoicePool"/>, where only the <see cref="Heard"/> one is audible.
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
        private readonly MusicVoicePool _pool;
        private int _currentTrack;
        private int _loggedTrack = -1;
        private int _pendingTrack;
        private double _pendingDelay;
        private int _overrideTrack = -1;
        private int _volumeVersion = -1;
        private bool _heard = true;
        private bool _becameHeard = true;

        /// <summary>A director with a voice pool of its own (one character; tests).</summary>
        /// <param name="mixer">The mixer.</param>
        /// <param name="openMusic">Opens a track; it reads a multi-megabyte file, so it runs through <paramref name="runInBackground"/>.</param>
        /// <param name="runInBackground">Runs the open off the frame thread; null uses the thread pool.</param>
        public MusicDirector(AudioMixer mixer, Func<int, IPcmSource?> openMusic, Action<Action>? runInBackground = null)
            : this(mixer, new MusicVoicePool(mixer, openMusic, runInBackground))
        {
        }

        /// <summary>One character's director, its tracks played through the voices every character shares.</summary>
        public MusicDirector(AudioMixer mixer, MusicVoicePool pool)
        {
            _mixer = mixer ?? throw new ArgumentNullException(nameof(mixer));
            _pool = pool ?? throw new ArgumentNullException(nameof(pool));
        }

        /// <summary>The voices this director plays through.</summary>
        public MusicVoicePool Pool => _pool;

        /// <summary>
        /// Whether this character's music is the one heard. A director not heard still follows its character's situation
        /// and holds its track, silent, so it is where it should be when the character is shown again; only the heard one
        /// sets the music bus volume and picks the audible track.
        /// </summary>
        public bool Heard
        {
            get => _heard;
            set
            {
                if (value && !_heard)
                {
                    _becameHeard = true;
                    _volumeVersion = -1;
                }

                _heard = value;
            }
        }

        /// <summary>The track that should be heard now: none while the last track fades out before the next starts.</summary>
        public int AudibleTrack => _pendingDelay > 0 ? 0 : _currentTrack;

        /// <summary>The track playing (or about to), 0 for silence.</summary>
        public int CurrentTrack => _pendingDelay > 0 ? _pendingTrack : _currentTrack;

        /// <summary>An event's master volume (opcodes 0x69 / 0x6A, mask 0x08), applied to the music bus with the music volume.</summary>
        public float ScriptMaster { get; private set; } = 1f;

        /// <summary>
        /// Whether the music has settled on its track: no fade-out pending and the track loaded (or silence). Event opcode
        /// 0x9A waits for this.
        /// </summary>
        public bool IsSettled => _pendingDelay <= 0 && !_pool.IsLoading(_currentTrack);

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

        /// <summary>Advances fades and switches tracks; call once a frame (for every character, heard or not).</summary>
        public void Update(ZoneMusicState music, in MusicContext context, double deltaSeconds)
        {
            int volumeVersion = music.Version;
            if (_heard && volumeVersion != _volumeVersion)
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

            Advance(music, wanted, deltaSeconds);
            if (_heard)
            {
                // A switch to this character crossfades from the last one's track; its own track change fades the old one
                // out first (the pending delay) and then starts or joins the next.
                float fadeOut = _becameHeard ? MusicVoicePool.CrossfadeSeconds : TrackFadeSeconds;
                _pool.SetAudible(AudibleTrack, fadeOut, MusicVoicePool.CrossfadeSeconds);
                _becameHeard = false;
            }
        }

        private void Advance(ZoneMusicState music, int wanted, double deltaSeconds)
        {
            if (_pendingDelay > 0)
            {
                _pendingTrack = wanted;
                _pendingDelay -= deltaSeconds;
                if (_pendingDelay <= 0)
                {
                    Start(music, _pendingTrack);
                }

                return;
            }

            if (wanted == _currentTrack)
            {
                return;
            }

            if (_heard && wanted != _loggedTrack)
            {
                _loggedTrack = wanted;
                Gordian.Core.Diagnostics.GordianLog.Info("AUDIO", $"Music: slot {(_overrideTrack >= 0 ? "override" : CurrentSlot.ToString())} → track {wanted} (was {_currentTrack}).");
            }

            if (_currentTrack > 0)
            {
                // The old track fades out, then the new one starts from the top (or joins another character's).
                _pool.Release(this, _currentTrack, TrackFadeSeconds);
                _currentTrack = 0;
                _pendingTrack = wanted;
                _pendingDelay = TrackFadeSeconds;
                return;
            }

            Start(music, wanted);
        }

        /// <summary>Stops this character's music (the character left, the service closes): its track is let go.</summary>
        public void StopAll(float fadeSeconds = 0f)
        {
            if (_currentTrack > 0) _pool.Release(this, _currentTrack, fadeSeconds);
            _currentTrack = 0;
            _pendingDelay = 0;
        }

        private void Start(ZoneMusicState music, int track)
        {
            _pendingDelay = 0;
            _currentTrack = track;
            if (track <= 0)
            {
                return;
            }

            // Only a track that starts from the top is a start for the server music volume (ZoneMusicState.TrackStarted).
            if (_pool.Acquire(this, track)) music.TrackStarted();
        }
    }
}
