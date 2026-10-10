// src/Gordian.App/Audio/SoundControls.cs
using System;
using System.Threading;

namespace Gordian.App.Audio
{
    /// <summary>
    /// The live state of the GordianXI sound controls (#265): the settings the desktop shell publishes, and whether a
    /// GordianXI window (and a viewport window) has focus. Static and free of the audio device, so the shell can publish
    /// to it without opening one; <see cref="GameAudioService"/> reads it every frame. The decisions are pure functions
    /// so they can be tested.
    /// </summary>
    public static class SoundControls
    {
        private static SoundControlSettings _current = new();
        private static int _anyWindowActive = 1;
        private static int _viewportWindowActive = 1;
        private static int _version;

        /// <summary>The published settings (a copy: never edited after publishing).</summary>
        public static SoundControlSettings Current => Volatile.Read(ref _current);

        /// <summary>Changes whenever the settings or the focus change.</summary>
        public static int Version => Volatile.Read(ref _version);

        /// <summary>Whether any GordianXI window has focus.</summary>
        public static bool AnyWindowActive => Volatile.Read(ref _anyWindowActive) != 0;

        /// <summary>Whether a viewport window has focus.</summary>
        public static bool ViewportWindowActive => Volatile.Read(ref _viewportWindowActive) != 0;

        /// <summary>Publishes a copy of <paramref name="settings"/>.</summary>
        public static void Publish(SoundControlSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);
            Volatile.Write(ref _current, settings.Clone());
            Interlocked.Increment(ref _version);
        }

        /// <summary>Records which kinds of GordianXI window have focus.</summary>
        public static void SetFocus(bool anyWindowActive, bool viewportWindowActive)
        {
            int any = anyWindowActive ? 1 : 0;
            int viewport = viewportWindowActive ? 1 : 0;
            if (Interlocked.Exchange(ref _anyWindowActive, any) != any | Interlocked.Exchange(ref _viewportWindowActive, viewport) != viewport)
            {
                Interlocked.Increment(ref _version);
            }
        }

        /// <summary>Whether the app counts as active for <paramref name="settings"/>' scope.</summary>
        public static bool IsActive(SoundControlSettings settings, bool anyWindowActive, bool viewportWindowActive) =>
            settings.ActiveScope == SoundActiveScope.ViewportWindow ? viewportWindowActive : anyWindowActive;

        /// <summary>
        /// The control gain of a bus (1 = as retail, 0 = muted): 0 when sound is off, the bus is off, or the app is
        /// inactive with "mute when inactive" on and the bus is not an exception.
        /// </summary>
        public static float CategoryGain(SoundControlSettings settings, AudioCategory category, bool active)
        {
            if (!settings.SoundEnabled)
            {
                return 0f;
            }

            SoundCategoryRule rule = settings.Rule(category);
            if (!rule.Enabled)
            {
                return 0f;
            }

            return settings.MuteWhenInactive && !active && !rule.PlayWhenInactive ? 0f : 1f;
        }

        /// <summary>
        /// Whether a viewport's character is the one the policy prefers (and so takes the sound from the
        /// focused window). Always false for <see cref="MultiBoxSoundPolicy.FocusedWindow"/>.
        /// </summary>
        /// <param name="settings">The settings.</param>
        /// <param name="isPrimaryRendering">Whether the viewport's character is the primary rendering session (main viewport).</param>
        public static bool IsPreferred(SoundControlSettings settings, bool isPrimaryRendering) =>
            settings.MultiBoxPolicy == MultiBoxSoundPolicy.PrimaryViewport && isPrimaryRendering;

        /// <summary>
        /// Whether an incoming tell of a character plays its cue: always for the character heard, and for the others
        /// only with <see cref="SoundControlSettings.NotificationsFromAllCharacters"/>. The bus gain still applies.
        /// </summary>
        public static bool PlaysTellCue(SoundControlSettings settings, bool isHeardCharacter) =>
            isHeardCharacter || settings.NotificationsFromAllCharacters;
    }
}
