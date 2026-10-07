// src/Gordian.App/Audio/SoundControlSettings.cs
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Gordian.Core.Config;
using Gordian.Core.Diagnostics;

namespace Gordian.App.Audio
{
    /// <summary>Which character is heard when several are logged in to one GordianXI (#265).</summary>
    public enum MultiBoxSoundPolicy
    {
        /// <summary>The character shown in the viewport window that was activated last (the default, as before #265).</summary>
        FocusedWindow = 0,

        /// <summary>The character of the main viewport window (the primary rendering session), even while a pop-out has focus.</summary>
        PrimaryViewport = 1,

        /// <summary>A named character, whichever window shows it; the focused window while it is not shown.</summary>
        NamedCharacter = 2,
    }

    /// <summary>What counts as "the window is active" for <see cref="SoundControlSettings.MuteWhenInactive"/>.</summary>
    public enum SoundActiveScope
    {
        /// <summary>Any GordianXI window (main window, viewport, pop-outs, chat) has focus.</summary>
        AnyGordianWindow = 0,

        /// <summary>A viewport window (the main viewport or a pop-out) has focus.</summary>
        ViewportWindow = 1,
    }

    /// <summary>The GordianXI switches of one mixer bus (#265). The defaults change nothing.</summary>
    public sealed class SoundCategoryRule
    {
        /// <summary>Whether the bus plays at all (independent of the retail volume sliders).</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>Whether the bus keeps playing while the window is inactive (an exception to <see cref="SoundControlSettings.MuteWhenInactive"/>).</summary>
        public bool PlayWhenInactive { get; set; }

        /// <summary>A copy.</summary>
        public SoundCategoryRule Clone() => new() { Enabled = Enabled, PlayWhenInactive = PlayWhenInactive };
    }

    /// <summary>
    /// GordianXI-only sound controls that retail does not have (#265): master sound off, mute while the window is inactive,
    /// the multi-box policy (which character is heard) and per-bus exceptions. Every default keeps the retail mix: all
    /// sound on, playing whether or not the window has focus, the focused window's character heard. Saved for the whole
    /// app (not per character) in <c>sound_settings.json</c> beside <c>viewport_settings.json</c>.
    /// </summary>
    public sealed class SoundControlSettings
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        };

        /// <summary>Master sound switch. Off silences every bus without touching the retail volume sliders.</summary>
        public bool SoundEnabled { get; set; } = true;

        /// <summary>Fade out the sound while GordianXI is not the active application (opt-in).</summary>
        public bool MuteWhenInactive { get; set; }

        /// <summary>Which windows count as active for <see cref="MuteWhenInactive"/>.</summary>
        public SoundActiveScope ActiveScope { get; set; } = SoundActiveScope.AnyGordianWindow;

        /// <summary>Fade time when the sound is muted or unmuted by these controls, in seconds.</summary>
        public float FadeSeconds { get; set; } = 0.5f;

        /// <summary>Which character is heard with several logged in.</summary>
        public MultiBoxSoundPolicy MultiBoxPolicy { get; set; } = MultiBoxSoundPolicy.FocusedWindow;

        /// <summary>The character heard with <see cref="MultiBoxSoundPolicy.NamedCharacter"/>.</summary>
        public string PreferredCharacter { get; set; } = string.Empty;

        /// <summary>Play the tell cue of every logged-in character, not only the one heard (opt-in).</summary>
        public bool NotificationsFromAllCharacters { get; set; }

        /// <summary>Music bus switches.</summary>
        public SoundCategoryRule Music { get; set; } = new();

        /// <summary>Effects bus switches (footsteps, combat, cutscene sounds).</summary>
        public SoundCategoryRule Effects { get; set; } = new();

        /// <summary>System bus switches (menu and target cues).</summary>
        public SoundCategoryRule System { get; set; } = new();

        /// <summary>Zone bus switches (ambient loops, zone sound generators).</summary>
        public SoundCategoryRule Zone { get; set; } = new();

        /// <summary>Notification bus switches (the incoming tell cue).</summary>
        public SoundCategoryRule Notification { get; set; } = new();

        /// <summary>The rule of a bus.</summary>
        public SoundCategoryRule Rule(AudioCategory category) => category switch
        {
            AudioCategory.Music => Music,
            AudioCategory.Effects => Effects,
            AudioCategory.System => System,
            AudioCategory.Zone => Zone,
            AudioCategory.Notification => Notification,
            _ => throw new ArgumentOutOfRangeException(nameof(category), category, null),
        };

        /// <summary>A deep copy (the audio thread reads a published copy, never the one the UI edits).</summary>
        public SoundControlSettings Clone() => new()
        {
            SoundEnabled = SoundEnabled,
            MuteWhenInactive = MuteWhenInactive,
            ActiveScope = ActiveScope,
            FadeSeconds = FadeSeconds,
            MultiBoxPolicy = MultiBoxPolicy,
            PreferredCharacter = PreferredCharacter,
            NotificationsFromAllCharacters = NotificationsFromAllCharacters,
            Music = Music.Clone(),
            Effects = Effects.Clone(),
            System = System.Clone(),
            Zone = Zone.Clone(),
            Notification = Notification.Clone(),
        };

        /// <summary>The default file: <c>%LocalAppData%/GordianXI/sound_settings.json</c> (or the storage root).</summary>
        public static string GetDefaultSettingsPath()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrWhiteSpace(localAppData))
            {
                return Path.Combine(localAppData, "GordianXI", "sound_settings.json");
            }

            return Path.Combine(GordianStorage.RootDataDirectory, "sound_settings.json");
        }

        /// <summary>Writes the settings as JSON.</summary>
        public void SaveToFile(string filePath)
        {
            ArgumentNullException.ThrowIfNull(filePath);
            string? dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(filePath, JsonSerializer.Serialize(this, JsonOptions));
        }

        /// <summary>Reads the settings, or the defaults when the file is missing or unreadable.</summary>
        public static SoundControlSettings LoadOrDefault(string filePath)
        {
            ArgumentNullException.ThrowIfNull(filePath);
            if (File.Exists(filePath))
            {
                try
                {
                    SoundControlSettings? settings = JsonSerializer.Deserialize<SoundControlSettings>(File.ReadAllText(filePath), JsonOptions);
                    if (settings is not null)
                    {
                        settings.Music ??= new();
                        settings.Effects ??= new();
                        settings.System ??= new();
                        settings.Zone ??= new();
                        settings.Notification ??= new();
                        settings.PreferredCharacter ??= string.Empty;
                        return settings;
                    }
                }
                catch (Exception ex)
                {
                    GordianLog.Warn("AUDIO", $"Failed to load sound settings from '{filePath}': {ex.Message}. Using defaults.");
                }
            }

            return new SoundControlSettings();
        }
    }
}
