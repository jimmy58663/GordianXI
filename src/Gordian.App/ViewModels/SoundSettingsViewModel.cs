// src/Gordian.App/ViewModels/SoundSettingsViewModel.cs
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Gordian.App.Audio;
using Gordian.Core.Diagnostics;

namespace Gordian.App.ViewModels
{
    /// <summary>
    /// The desktop shell's Sound tab (#265): GordianXI-only sound controls on top of the retail mix. Every change is saved
    /// to <c>sound_settings.json</c> and published to <see cref="SoundControls"/>, which the audio service reads each
    /// frame. The retail volume sliders stay on the in-game config page.
    /// </summary>
    public sealed class SoundSettingsViewModel : ViewModelBase
    {
        private readonly string _settingsPath;
        private readonly bool _enableAutoSave;
        private readonly bool _publish;
        private readonly SoundControlSettings _settings;

        /// <summary>Loads the saved settings (or the defaults) and publishes them.</summary>
        /// <param name="customSettingsPath">The file; null for the default path.</param>
        /// <param name="enableAutoSave">Whether changes are written to the file.</param>
        /// <param name="publish">Whether the settings are published to <see cref="SoundControls"/> (off in tests).</param>
        public SoundSettingsViewModel(string? customSettingsPath = null, bool enableAutoSave = true, bool publish = true)
        {
            _settingsPath = customSettingsPath ?? SoundControlSettings.GetDefaultSettingsPath();
            _enableAutoSave = enableAutoSave;
            _publish = publish;
            _settings = SoundControlSettings.LoadOrDefault(_settingsPath);
            foreach (AudioCategory category in Enum.GetValues<AudioCategory>())
            {
                Categories.Add(new SoundCategoryRowViewModel(category, _settings.Rule(category), Changed));
            }

            if (_publish)
            {
                SoundControls.Publish(_settings);
            }
        }

        /// <summary>The settings as edited (tests read it).</summary>
        public SoundControlSettings Settings => _settings;

        /// <summary>The per-bus rows.</summary>
        public ObservableCollection<SoundCategoryRowViewModel> Categories { get; } = new();

        /// <summary>The multi-box policies, for the combo box.</summary>
        public IReadOnlyList<MultiBoxSoundPolicy> AvailablePolicies { get; } = Enum.GetValues<MultiBoxSoundPolicy>();

        /// <summary>The active scopes, for the combo box.</summary>
        public IReadOnlyList<SoundActiveScope> AvailableScopes { get; } = Enum.GetValues<SoundActiveScope>();

        /// <summary>Master sound switch.</summary>
        public bool SoundEnabled
        {
            get => _settings.SoundEnabled;
            set => Set(_settings.SoundEnabled, value, v => _settings.SoundEnabled = v);
        }

        /// <summary>Mute while GordianXI is not active.</summary>
        public bool MuteWhenInactive
        {
            get => _settings.MuteWhenInactive;
            set => Set(_settings.MuteWhenInactive, value, v => _settings.MuteWhenInactive = v);
        }

        /// <summary>Which windows count as active.</summary>
        public SoundActiveScope ActiveScope
        {
            get => _settings.ActiveScope;
            set => Set(_settings.ActiveScope, value, v => _settings.ActiveScope = v);
        }

        /// <summary>Which character is heard with several logged in.</summary>
        public MultiBoxSoundPolicy MultiBoxPolicy
        {
            get => _settings.MultiBoxPolicy;
            set => Set(_settings.MultiBoxPolicy, value, v => _settings.MultiBoxPolicy = v);
        }

        /// <summary>Play every character's tell cue, not only the heard one's.</summary>
        public bool NotificationsFromAllCharacters
        {
            get => _settings.NotificationsFromAllCharacters;
            set => Set(_settings.NotificationsFromAllCharacters, value, v => _settings.NotificationsFromAllCharacters = v);
        }

        private bool Set<T>(T current, T value, Action<T> assign, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
        {
            if (EqualityComparer<T>.Default.Equals(current, value))
            {
                return false;
            }

            assign(value);
            OnPropertyChanged(name);
            Changed();
            return true;
        }

        private void Changed()
        {
            if (_publish)
            {
                SoundControls.Publish(_settings);
            }

            if (!_enableAutoSave)
            {
                return;
            }

            try
            {
                _settings.SaveToFile(_settingsPath);
            }
            catch (Exception ex)
            {
                GordianLog.Warn("AUDIO", $"Saving sound settings failed: {ex.Message}");
            }
        }
    }

    /// <summary>One bus row of the Sound tab.</summary>
    public sealed class SoundCategoryRowViewModel : ViewModelBase
    {
        private readonly SoundCategoryRule _rule;
        private readonly Action _changed;

        internal SoundCategoryRowViewModel(AudioCategory category, SoundCategoryRule rule, Action changed)
        {
            Category = category;
            _rule = rule;
            _changed = changed;
        }

        /// <summary>The bus.</summary>
        public AudioCategory Category { get; }

        /// <summary>Display name.</summary>
        public string Name => Category switch
        {
            AudioCategory.Music => "Music",
            AudioCategory.Effects => "Sound effects (footsteps, combat, cutscenes)",
            AudioCategory.System => "System (menus, targeting)",
            AudioCategory.Zone => "Zone ambience",
            AudioCategory.Notification => "Notifications (incoming tells)",
            _ => Category.ToString(),
        };

        /// <summary>Whether the bus plays.</summary>
        public bool Enabled
        {
            get => _rule.Enabled;
            set
            {
                if (_rule.Enabled != value)
                {
                    _rule.Enabled = value;
                    OnPropertyChanged();
                    _changed();
                }
            }
        }

        /// <summary>Whether the bus keeps playing while GordianXI is inactive.</summary>
        public bool PlayWhenInactive
        {
            get => _rule.PlayWhenInactive;
            set
            {
                if (_rule.PlayWhenInactive != value)
                {
                    _rule.PlayWhenInactive = value;
                    OnPropertyChanged();
                    _changed();
                }
            }
        }
    }
}
