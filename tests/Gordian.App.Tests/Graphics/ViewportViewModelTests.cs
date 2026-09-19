// tests/Gordian.App.Tests/Graphics/ViewportViewModelTests.cs
using System;
using System.ComponentModel;
using System.IO;
using Gordian.App.Graphics;
using Gordian.App.ViewModels;
using Xunit;

namespace Gordian.App.Tests.Graphics
{
    public class ViewportViewModelTests
    {
        private static (ViewportViewModel vm, string tempFile) CreateIsolatedViewModel()
        {
            string tempFile = Path.Combine(Path.GetTempPath(), $"gordian_viewport_test_{Guid.NewGuid():N}.json");
            var vm = new ViewportViewModel(tempFile);
            return (vm, tempFile);
        }

        [Fact]
        public void DefaultState_HasAutoBackendAndTelemetryDefaults()
        {
            var (vm, tempFile) = CreateIsolatedViewModel();
            try
            {
                Assert.Equal(GraphicsBackendPreference.Auto, vm.SelectedBackend);
                Assert.True(vm.IsVsyncEnabled);
                Assert.Contains(GraphicsBackendPreference.Direct3D11, vm.AvailableBackends);
                Assert.Contains(GraphicsBackendPreference.Vulkan, vm.AvailableBackends);
                Assert.Contains(GraphicsBackendPreference.Metal, vm.AvailableBackends);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public void PropertyChanges_EmitPropertyChangedNotifications()
        {
            var (vm, tempFile) = CreateIsolatedViewModel();
            try
            {
                string? changedProp = null;
                vm.PropertyChanged += (s, e) => changedProp = e.PropertyName;

                vm.SelectedBackend = GraphicsBackendPreference.Direct3D11;
                Assert.Equal(nameof(vm.SelectedBackend), changedProp);

                vm.Fps = 60.0;
                Assert.Equal(nameof(vm.Fps), changedProp);

                vm.FrameTimeMs = 1.25;
                Assert.Equal(nameof(vm.FrameTimeMs), changedProp);

                vm.ActiveBackend = "Direct3D 11";
                Assert.Equal(nameof(vm.ActiveBackend), changedProp);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public void DefaultState_HasBorderlessDisplayModeAndAvailableModes()
        {
            var (vm, tempFile) = CreateIsolatedViewModel();
            try
            {
                Assert.Equal(ViewportDisplayMode.BorderlessWindow, vm.SelectedDisplayMode);
                Assert.True(vm.AutoLaunchOnConnect);
                Assert.Contains(ViewportDisplayMode.BorderlessWindow, vm.AvailableDisplayModes);
                Assert.Contains(ViewportDisplayMode.Windowed, vm.AvailableDisplayModes);
                Assert.Contains(ViewportDisplayMode.Fullscreen, vm.AvailableDisplayModes);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public void DisplayModeChanged_RaisesEventAndPropertyNotification()
        {
            var (vm, tempFile) = CreateIsolatedViewModel();
            try
            {
                ViewportDisplayMode? notifiedMode = null;
                vm.DisplayModeChanged += (_, mode) => notifiedMode = mode;

                vm.SelectedDisplayMode = ViewportDisplayMode.Windowed;

                Assert.Equal(ViewportDisplayMode.Windowed, vm.SelectedDisplayMode);
                Assert.Equal(ViewportDisplayMode.Windowed, notifiedMode);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public void PropertyChanges_AutoSavesAndPersistsAcrossInstances()
        {
            string tempFile = Path.Combine(Path.GetTempPath(), $"gordian_viewport_test_{Guid.NewGuid():N}.json");
            try
            {
                var vm1 = new ViewportViewModel(tempFile);
                vm1.SelectedBackend = GraphicsBackendPreference.Vulkan;
                vm1.SelectedDisplayMode = ViewportDisplayMode.Fullscreen;
                vm1.SelectedTabStyle = ViewportTabStyle.SideRail;
                vm1.AutoLaunchOnConnect = false;
                vm1.IsPipEnabled = true;
                vm1.MaxPipStreams = 3;
                vm1.IsVsyncEnabled = false;

                Assert.True(File.Exists(tempFile));

                // Reinstantiate to simulate application restart
                var vm2 = new ViewportViewModel(tempFile);
                Assert.Equal(GraphicsBackendPreference.Vulkan, vm2.SelectedBackend);
                Assert.Equal(ViewportDisplayMode.Fullscreen, vm2.SelectedDisplayMode);
                Assert.Equal(ViewportTabStyle.SideRail, vm2.SelectedTabStyle);
                Assert.False(vm2.AutoLaunchOnConnect);
                Assert.True(vm2.IsPipEnabled);
                Assert.Equal(3, vm2.MaxPipStreams);
                Assert.False(vm2.IsVsyncEnabled);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public void CharacterTabs_AddAndCycleThroughSessions()
        {
            var (vm, tempFile) = CreateIsolatedViewModel();
            try
            {
                var netManager1 = new Gordian.Core.Network.SessionNetworkManager("127.0.0.1", 54230);
                var netManager2 = new Gordian.Core.Network.SessionNetworkManager("127.0.0.1", 54230);
                var session1 = new Gordian.Core.Network.CharacterSession("Cybin", 1, "cybin_user", netManager1);
                var session2 = new Gordian.Core.Network.CharacterSession("Sylphie", 2, "sylph_user", netManager2);

                var tab1 = vm.AddSession(session1);
                Assert.Single(vm.CharacterTabs);
                Assert.Equal(tab1, vm.ActiveTab);
                Assert.True(tab1.IsActive);

                var tab2 = vm.AddSession(session2);
                Assert.Equal(2, vm.CharacterTabs.Count);
                Assert.Equal(tab1, vm.ActiveTab); // First remains active until switched

                // Cycle to next
                vm.CycleNextCharacter();
                Assert.Equal(tab2, vm.ActiveTab);
                Assert.True(tab2.IsActive);
                Assert.False(tab1.IsActive);

                // Cycle back
                vm.CyclePreviousCharacter();
                Assert.Equal(tab1, vm.ActiveTab);

                // Pop out event trigger
                ViewportCharacterTabViewModel? popped = null;
                vm.TabPoppedOut += (_, t) => popped = t;
                tab2.PopOutCommand.Execute(null);
                Assert.Equal(tab2, popped);

                // Remove session
                vm.RemoveSession(session1);
                Assert.Single(vm.CharacterTabs);
                Assert.Equal(tab2, vm.ActiveTab);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public void TabStyle_DefaultsToFloatingPill_AndUpdatesHelperBooleans()
        {
            var (vm, tempFile) = CreateIsolatedViewModel();
            try
            {
                Assert.Equal(ViewportTabStyle.FloatingPill, vm.SelectedTabStyle);
                Assert.True(vm.IsFloatingPill);
                Assert.False(vm.IsTopRibbon);
                Assert.False(vm.IsSideRail);
                Assert.False(vm.IsHotkeysOnly);

                vm.SelectedTabStyle = ViewportTabStyle.TopRibbon;
                Assert.True(vm.IsTopRibbon);
                Assert.False(vm.IsFloatingPill);

                vm.SelectedTabStyle = ViewportTabStyle.SideRail;
                Assert.True(vm.IsSideRail);

                vm.SelectedTabStyle = ViewportTabStyle.HotkeysOnly;
                Assert.True(vm.IsHotkeysOnly);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public void PipStreaming_PopulatesUpToFiveBackgroundCharacters()
        {
            var (vm, tempFile) = CreateIsolatedViewModel();
            try
            {
                var sessions = new Gordian.Core.Network.CharacterSession[6];
                for (int i = 0; i < 6; i++)
                {
                    var net = new Gordian.Core.Network.SessionNetworkManager("127.0.0.1", 54230);
                    sessions[i] = new Gordian.Core.Network.CharacterSession($"Char{i}", (uint)(i + 1), $"user{i}", net);
                    vm.AddSession(sessions[i]);
                }

                // Total 6 characters: 1 active (Char0) + 5 PiP thumbnails (Char1..Char5)
                Assert.Equal(6, vm.CharacterTabs.Count);
                Assert.Equal("Char0", vm.ActiveTab?.CharacterName);
                Assert.Equal(5, vm.PipThumbnails.Count);
                Assert.DoesNotContain(vm.PipThumbnails, t => t.CharacterName == "Char0");

                // Swapping active tab to Char1 should update PiP thumbnails
                vm.ActiveTab = vm.CharacterTabs[1];
                Assert.Equal("Char1", vm.ActiveTab.CharacterName);
                Assert.Equal(5, vm.PipThumbnails.Count);
                Assert.DoesNotContain(vm.PipThumbnails, t => t.CharacterName == "Char1");
                Assert.Contains(vm.PipThumbnails, t => t.CharacterName == "Char0");
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }
    }
}
