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
                Assert.DoesNotContain(GraphicsBackendPreference.Metal, vm.AvailableBackends);
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
        public void EnableAutoSave_False_DoesNotWriteToDisk()
        {
            string tempFile = Path.Combine(Path.GetTempPath(), $"gordian_viewport_no_autosave_{Guid.NewGuid():N}.json");
            try
            {
                var vm = new ViewportViewModel(tempFile, enableAutoSave: false);
                vm.SelectedBackend = GraphicsBackendPreference.Vulkan;
                vm.AutoLaunchOnConnect = false;
                vm.SelectedDisplayMode = ViewportDisplayMode.Fullscreen;

                Assert.False(File.Exists(tempFile));
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

        private static Gordian.Core.Network.CharacterSession NewSession(string name, uint id) =>
            new(name, id, $"user_{name}", new Gordian.Core.Network.SessionNetworkManager("127.0.0.1", 54230));

        [Fact]
        public void CycleCharacter_WrapsBothWays()
        {
            var vm = new ViewportViewModel(enableAutoSave: false);
            var a = vm.AddSession(NewSession("Gordian", 3));
            var b = vm.AddSession(NewSession("Knot", 1));
            var c = vm.AddSession(NewSession("Claude", 4));

            vm.CycleNextCharacter();
            Assert.Same(b, vm.ActiveTab);
            vm.CycleNextCharacter();
            Assert.Same(c, vm.ActiveTab);
            vm.CycleNextCharacter();
            Assert.Same(a, vm.ActiveTab); // wraps
            vm.CyclePreviousCharacter();
            Assert.Same(c, vm.ActiveTab); // wraps back
        }

        [Fact]
        public void CycleCharacter_IntoAPoppedOutCharacter_FocusesItsWindow()
        {
            var vm = new ViewportViewModel(enableAutoSave: false);
            var a = vm.AddSession(NewSession("Gordian", 3));
            var b = vm.AddSession(NewSession("Knot", 1));
            vm.AddSession(NewSession("Claude", 4));
            b.IsPoppedOut = true; // Knot has its own window
            ViewportCharacterTabViewModel? focused = null;
            vm.TabPoppedOut += (_, t) => focused = t; // the window manager activates the pop-out on this

            vm.CycleNextCharacter();

            Assert.Same(b, focused);
            Assert.Same(a, vm.ActiveTab); // the main window keeps its character
        }

        [Fact]
        public void CycleCharacter_FromAPopOut_FollowsTheMainWindowsTabOrder()
        {
            var main = new ViewportViewModel(enableAutoSave: false);
            var a = main.AddSession(NewSession("Gordian", 3));
            var b = main.AddSession(NewSession("Knot", 1));
            var c = main.AddSession(NewSession("Claude", 4));
            b.IsPoppedOut = true;
            Assert.Same(a, main.ActiveTab);

            var popOut = new ViewportViewModel(enableAutoSave: false) { IsPrimary = false, SwitchTarget = main };
            popOut.AddSession(b.Session);
            int mainActivations = 0;
            main.WindowActivationRequested += (_, _) => mainActivations++;

            popOut.CycleNextCharacter(); // Knot -> Claude, shown in the main window

            Assert.Same(c, main.ActiveTab);
            Assert.Equal(1, mainActivations);
        }

        [Fact]
        public void SwitcherClick_RequestsTheWindowFocus()
        {
            var vm = new ViewportViewModel(enableAutoSave: false);
            vm.AddSession(NewSession("Gordian", 3));
            var b = vm.AddSession(NewSession("Knot", 1));
            int activations = 0;
            vm.WindowActivationRequested += (_, _) => activations++;

            b.SelectTabCommand.Execute(null);

            Assert.Same(b, vm.ActiveTab);
            Assert.Equal(1, activations);
        }

        [Fact]
        public void CycleCharacter_WithOneTab_StaysPut()
        {
            var vm = new ViewportViewModel(enableAutoSave: false);
            var a = vm.AddSession(NewSession("Gordian", 3));
            vm.CycleNextCharacter();
            vm.CyclePreviousCharacter();
            Assert.Same(a, vm.ActiveTab);
        }

        [Fact]
        public void SwitchingCharacter_ReleasesTheKeysHeldForThePreviousOne()
        {
            var vm = new ViewportViewModel(enableAutoSave: false);
            var a = vm.AddSession(NewSession("Gordian", 3));
            vm.AddSession(NewSession("Knot", 1));

            // Ctrl is down for Ctrl+Tab and W keeps the character running; their key-ups go to the new character.
            a.Session.InputState.SetKeyDown(Gordian.Core.Input.GordianKey.LeftCtrl);
            a.Session.InputState.SetKeyDown(Gordian.Core.Input.GordianKey.W);
            vm.CycleNextCharacter();

            Assert.False(a.Session.InputState.IsKeyHeld(Gordian.Core.Input.GordianKey.W));
            Assert.False(a.Session.InputState.IsKeyHeld(Gordian.Core.Input.GordianKey.LeftCtrl));
        }

        [Fact]
        public void PopOutWindowViewModel_DoesNotTakeThePrimaryRenderingSession()
        {
            var primary = new ViewportViewModel(enableAutoSave: false);
            var main = primary.AddSession(NewSession("Gordian", 3));
            Assert.Same(main.Session, Gordian.Core.Network.SessionRegistry.Default.PrimaryRenderingSession);

            var popOut = new ViewportViewModel(enableAutoSave: false) { IsPrimary = false };
            var knot = popOut.AddSession(NewSession("Knot", 1));

            Assert.Same(knot, popOut.ActiveTab);
            Assert.NotSame(knot.Session, Gordian.Core.Network.SessionRegistry.Default.PrimaryRenderingSession);
        }

        [Fact]
        public void MoveOffPoppedOutTab_ShowsTheNextCharacterLeftInTheWindow()
        {
            var vm = new ViewportViewModel(enableAutoSave: false) { IsPipEnabled = true };
            var a = vm.AddSession(NewSession("Gordian", 3));
            var b = vm.AddSession(NewSession("Knot", 1));

            a.IsPoppedOut = true;
            vm.MoveOffPoppedOutTab();

            Assert.Same(b, vm.ActiveTab);
            Assert.Empty(vm.PipThumbnails); // the popped-out character has its own window, not a thumbnail
            Assert.False(vm.ShowPipDeck);

            b.IsPoppedOut = true; // every character popped out: the window keeps the one it shows
            vm.MoveOffPoppedOutTab();
            Assert.Same(b, vm.ActiveTab);
            Assert.Equal("2 chars", vm.CharacterCountText);
        }

        [Fact]
        public void SwitcherVisibility_FollowsStyleTabsAndLobby()
        {
            var vm = new ViewportViewModel(enableAutoSave: false) { SelectedTabStyle = ViewportTabStyle.FloatingPill };
            Assert.False(vm.ShowFloatingPill); // no character yet

            vm.AddSession(NewSession("Gordian", 3));
            Assert.True(vm.ShowFloatingPill);
            Assert.False(vm.ShowSideRail);
            Assert.False(vm.ShowTopRibbon);

            vm.SelectedTabStyle = ViewportTabStyle.SideRail;
            Assert.False(vm.ShowFloatingPill);
            Assert.True(vm.ShowSideRail);

            vm.SelectedTabStyle = ViewportTabStyle.TopRibbon;
            Assert.True(vm.ShowTopRibbon);
            Assert.False(vm.ShowSideRail);

            vm.SelectedTabStyle = ViewportTabStyle.HotkeysOnly;
            Assert.False(vm.ShowFloatingPill || vm.ShowSideRail || vm.ShowTopRibbon);

            vm.SelectedTabStyle = ViewportTabStyle.FloatingPill;
            vm.IsReturningToLobby = true; // black on the way back to character select
            Assert.False(vm.ShowFloatingPill);
            vm.IsReturningToLobby = false;
            Assert.True(vm.ShowFloatingPill);
        }

        [Fact]
        public void SwitcherVisibility_RaisesPropertyChangedWhenTabsArrive()
        {
            var vm = new ViewportViewModel(enableAutoSave: false);
            var raised = new System.Collections.Generic.List<string?>();
            vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

            vm.AddSession(NewSession("Gordian", 3));

            Assert.Contains(nameof(ViewportViewModel.ShowFloatingPill), raised);
        }

        [Fact]
        public void CameraModeButton_SwitchesTheActiveCharactersCamera()
        {
            var vm = new ViewportViewModel(enableAutoSave: false);
            var a = vm.AddSession(NewSession("Gordian", 3));
            Assert.Equal(Gordian.Core.Graphics.CameraMode.ThirdPersonOrbital, a.Session.Locomotion.CameraMode);

            vm.ToggleFreeCamCommand.Execute(null);
            Assert.Equal(Gordian.Core.Graphics.CameraMode.FreeCam, a.Session.Locomotion.CameraMode);
            Assert.True(vm.IsFreeCamActive);

            // A key press changed it in game: the switcher follows on the next sync.
            a.Session.Locomotion.CameraMode = Gordian.Core.Graphics.CameraMode.FirstPerson;
            vm.SyncFromActiveSession();
            Assert.Equal(Gordian.Core.Graphics.CameraMode.FirstPerson, vm.ActiveCameraMode);
            Assert.False(vm.IsFreeCamActive);
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
