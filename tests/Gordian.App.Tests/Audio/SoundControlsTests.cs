// tests/Gordian.App.Tests/Audio/SoundControlsTests.cs
using System;
using System.IO;
using System.Linq;
using Gordian.App.Audio;
using Gordian.App.ViewModels;
using Gordian.Core.Audio;
using Xunit;

namespace Gordian.App.Tests.Audio
{
    /// <summary>The GordianXI sound controls (#265): opt-in, retail mix by default.</summary>
    public class SoundControlsTests
    {
        private static readonly AudioCategory[] AllCategories = Enum.GetValues<AudioCategory>();

        [Fact]
        public void Defaults_LeaveEveryBusAtUnity_ActiveOrNot()
        {
            var settings = new SoundControlSettings();
            foreach (AudioCategory category in AllCategories)
            {
                Assert.Equal(1f, SoundControls.CategoryGain(settings, category, active: true));
                Assert.Equal(1f, SoundControls.CategoryGain(settings, category, active: false));
            }

            Assert.Equal(MultiBoxSoundPolicy.FocusedWindow, settings.MultiBoxPolicy);
            Assert.False(SoundControls.IsPreferred(settings, isPrimaryRendering: true));
        }

        [Fact]
        public void SoundOff_SilencesEverything_EvenExceptions()
        {
            var settings = new SoundControlSettings { SoundEnabled = false };
            settings.Notification.PlayWhenInactive = true;
            Assert.All(AllCategories, c => Assert.Equal(0f, SoundControls.CategoryGain(settings, c, active: true)));
        }

        [Fact]
        public void MuteWhenInactive_KeepsExceptions()
        {
            var settings = new SoundControlSettings { MuteWhenInactive = true };
            settings.Notification.PlayWhenInactive = true;
            Assert.Equal(1f, SoundControls.CategoryGain(settings, AudioCategory.Music, active: true));
            Assert.Equal(0f, SoundControls.CategoryGain(settings, AudioCategory.Music, active: false));
            Assert.Equal(1f, SoundControls.CategoryGain(settings, AudioCategory.Notification, active: false));
        }

        [Fact]
        public void MusicOff_TellStillPlays()
        {
            var settings = new SoundControlSettings();
            settings.Music.Enabled = false;
            Assert.Equal(0f, SoundControls.CategoryGain(settings, AudioCategory.Music, active: true));
            Assert.Equal(1f, SoundControls.CategoryGain(settings, AudioCategory.Notification, active: true));
        }

        [Fact]
        public void ActiveScope_PicksTheWindowKind()
        {
            var any = new SoundControlSettings();
            var viewport = new SoundControlSettings { ActiveScope = SoundActiveScope.ViewportWindow };
            // The main window has focus, no viewport does.
            Assert.True(SoundControls.IsActive(any, anyWindowActive: true, viewportWindowActive: false));
            Assert.False(SoundControls.IsActive(viewport, anyWindowActive: true, viewportWindowActive: false));
        }

        [Fact]
        public void Policies_PickThePreferredCharacter()
        {
            var primary = new SoundControlSettings { MultiBoxPolicy = MultiBoxSoundPolicy.PrimaryViewport };
            Assert.True(SoundControls.IsPreferred(primary, isPrimaryRendering: true));
            Assert.False(SoundControls.IsPreferred(primary, isPrimaryRendering: false));
            Assert.Equal(new[] { MultiBoxSoundPolicy.FocusedWindow, MultiBoxSoundPolicy.PrimaryViewport }, Enum.GetValues<MultiBoxSoundPolicy>());
        }

        [Fact]
        public void TellCue_OtherCharactersOnlyWhenAllowed()
        {
            var settings = new SoundControlSettings();
            Assert.True(SoundControls.PlaysTellCue(settings, isHeardCharacter: true));
            Assert.False(SoundControls.PlaysTellCue(settings, isHeardCharacter: false));
            settings.NotificationsFromAllCharacters = true;
            Assert.True(SoundControls.PlaysTellCue(settings, isHeardCharacter: false));
        }

        [Fact]
        public void Settings_RoundTripThroughJson()
        {
            string dir = Path.Combine(Path.GetTempPath(), "gordian_sound_" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(dir, "sound_settings.json");
            try
            {
                var settings = new SoundControlSettings
                {
                    MuteWhenInactive = true,
                    MultiBoxPolicy = MultiBoxSoundPolicy.PrimaryViewport,
                    NotificationsFromAllCharacters = true,
                };
                settings.Music.Enabled = false;
                settings.Notification.PlayWhenInactive = true;
                settings.SaveToFile(path);

                SoundControlSettings loaded = SoundControlSettings.LoadOrDefault(path);
                Assert.True(loaded.MuteWhenInactive);
                Assert.Equal(MultiBoxSoundPolicy.PrimaryViewport, loaded.MultiBoxPolicy);
                Assert.True(loaded.NotificationsFromAllCharacters);
                Assert.False(loaded.Music.Enabled);
                Assert.True(loaded.Notification.PlayWhenInactive);
                Assert.True(loaded.Effects.Enabled);
            }
            finally
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                }
            }
        }

        [Fact]
        public void MissingOrBrokenFile_GivesDefaults()
        {
            string dir = Path.Combine(Path.GetTempPath(), "gordian_sound_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "sound_settings.json");
            try
            {
                Assert.True(SoundControlSettings.LoadOrDefault(path).SoundEnabled);
                File.WriteAllText(path, "{ not json");
                Assert.True(SoundControlSettings.LoadOrDefault(path).SoundEnabled);
                File.WriteAllText(path, "{ \"Music\": null }");
                Assert.True(SoundControlSettings.LoadOrDefault(path).Music.Enabled);
                // The removed named-character policy reads as the default, keeping the file's other settings.
                File.WriteAllText(path, "{ \"MuteWhenInactive\": true, \"MultiBoxPolicy\": \"NamedCharacter\" }");
                SoundControlSettings old = SoundControlSettings.LoadOrDefault(path);
                Assert.Equal(MultiBoxSoundPolicy.FocusedWindow, old.MultiBoxPolicy);
                Assert.True(old.MuteWhenInactive);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void Clone_IsDeep()
        {
            var settings = new SoundControlSettings();
            SoundControlSettings copy = settings.Clone();
            settings.Music.Enabled = false;
            Assert.True(copy.Music.Enabled);
        }

        [Fact]
        public void ViewModel_EditsAndSaves()
        {
            string dir = Path.Combine(Path.GetTempPath(), "gordian_sound_" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(dir, "sound_settings.json");
            try
            {
                var vm = new SoundSettingsViewModel(path, enableAutoSave: true, publish: false);
                Assert.Equal(AllCategories.Length, vm.Categories.Count);
                vm.MuteWhenInactive = true;
                vm.MultiBoxPolicy = MultiBoxSoundPolicy.PrimaryViewport;
                vm.Categories.Single(c => c.Category == AudioCategory.Music).Enabled = false;

                SoundControlSettings saved = SoundControlSettings.LoadOrDefault(path);
                Assert.True(saved.MuteWhenInactive);
                Assert.Equal(MultiBoxSoundPolicy.PrimaryViewport, saved.MultiBoxPolicy);
                Assert.False(saved.Music.Enabled);
            }
            finally
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                }
            }
        }

        [Fact]
        public void Mixer_ControlGainMultipliesAndFades()
        {
            var mixer = new AudioMixer(1000);
            var clip = new PcmClip(Enumerable.Repeat((short)10000, 100000).ToArray(), 1, 1000, 0);
            mixer.Play(clip.Open(), AudioCategory.Music);
            mixer.FadeControl(AudioCategory.Music, 0f, 0f);
            var output = new short[200];
            mixer.Mix(output);
            Assert.Equal(0, output[100]);

            mixer.FadeControl(AudioCategory.Music, 1f, 1f);
            output = new short[1200];
            mixer.Mix(output); // one 600-frame block
            mixer.Mix(output);
            Assert.InRange(output[100], 9990, 10010);

            // Another bus is untouched.
            mixer.FadeControl(AudioCategory.Effects, 0f, 0f);
            mixer.Mix(output);
            Assert.InRange(output[100], 9990, 10010);
        }

        [Fact]
        public void VolumeMix_NotificationFollowsTheEffectsSlider()
        {
            var gains = VolumeMix.CategoryGains(50, 20).ToDictionary(g => g.Category, g => g.Gain);
            Assert.Equal(0.2f, gains[AudioCategory.Notification]);
            Assert.Equal(AudioMixer.CategoryCount, gains.Count);
        }
    }
}
