// tests/Gordian.App.Tests/Graphics/ViewportShellTests.cs
using System;
using System.IO;
using Gordian.App.Graphics;
using Gordian.App.ViewModels;
using Gordian.Core.Network;
using Gordian.Engine.Sessions;
using NeoVeldrid;
using Xunit;

namespace Gordian.App.Tests.Graphics
{
    /// <summary>The viewport shell: the graphics backend "reopen to apply" rule (#319, #320) and the live PiP deck (#302).</summary>
    [Collection("SessionRegistry")]
    public class ViewportShellTests
    {
        [Theory]
        // Nothing to apply: the selection is what the device was created for, or what runs satisfies it.
        [InlineData(GraphicsBackendPreference.Auto, GraphicsBackendPreference.Auto, GraphicsBackend.Direct3D11, false)]
        [InlineData(GraphicsBackendPreference.Direct3D11, GraphicsBackendPreference.Auto, GraphicsBackend.Direct3D11, false)]
        [InlineData(GraphicsBackendPreference.Auto, GraphicsBackendPreference.Vulkan, GraphicsBackend.Vulkan, false)]
        [InlineData(GraphicsBackendPreference.OpenGL, GraphicsBackendPreference.Auto, GraphicsBackend.Direct3D11, false)]
        // A fallback is not flagged again while the same choice stays selected (Vulkan asked, Direct3D 11 running).
        [InlineData(GraphicsBackendPreference.Vulkan, GraphicsBackendPreference.Vulkan, GraphicsBackend.Direct3D11, false)]
        // The selection changed to something the open device is not running.
        [InlineData(GraphicsBackendPreference.Vulkan, GraphicsBackendPreference.Direct3D11, GraphicsBackend.Direct3D11, true)]
        [InlineData(GraphicsBackendPreference.Direct3D11, GraphicsBackendPreference.Vulkan, GraphicsBackend.Vulkan, true)]
        [InlineData(GraphicsBackendPreference.Direct3D11, GraphicsBackendPreference.Auto, GraphicsBackend.Vulkan, true)]
        public void ReopenNeeded_ComparesWithWhatWasRequestedAndWhatRuns(GraphicsBackendPreference selected, GraphicsBackendPreference requested,
            GraphicsBackend running, bool expected)
        {
            Assert.Equal(expected, SharedGraphicsDevice.ReopenNeeded(selected, requested, running));
        }

        [Fact]
        public void ReopenViewportCommand_OnAPopOutAsksThePrimaryToReopen()
        {
            string file = Path.Combine(Path.GetTempPath(), $"gordian_shell_test_{Guid.NewGuid():N}.json");
            try
            {
                var primary = new ViewportViewModel(file, enableAutoSave: false);
                var popOut = new ViewportViewModel(enableAutoSave: false) { IsPrimary = false, SwitchTarget = primary };
                int requests = 0;
                primary.ViewportReopenRequested += (_, _) => requests++;
                popOut.ReopenViewportCommand.Execute(null);
                primary.ReopenViewportCommand.Execute(null);
                Assert.Equal(2, requests);

                // The pill's drop-down in a pop-out sets the backend for the whole app.
                popOut.SelectedBackend = GraphicsBackendPreference.Vulkan;
                Assert.Equal(GraphicsBackendPreference.Vulkan, primary.SelectedBackend);
            }
            finally
            {
                if (File.Exists(file)) File.Delete(file);
            }
        }

        [Fact]
        public void BackendReopenNeeded_IsFalseWithoutAnOpenDevice()
        {
            var vm = new ViewportViewModel(enableAutoSave: false) { SelectedBackend = GraphicsBackendPreference.Vulkan };
            vm.RefreshBackendState();
            Assert.False(vm.BackendReopenNeeded);
        }

        private static CharacterSession NewSession(int i) =>
            new($"Char{i}", (uint)(i + 1), $"user{i}", new SessionNetworkManager("127.0.0.1", 54230));

        [Fact]
        public void PipDeck_KeepsTheCardsThatStayWhenItRefreshes()
        {
            var vm = new ViewportViewModel(enableAutoSave: false);
            var tabs = new ViewportCharacterTabViewModel[4];
            for (int i = 0; i < tabs.Length; i++) tabs[i] = vm.AddSession(NewSession(i));
            Assert.Equal(new[] { tabs[1], tabs[2], tabs[3] }, vm.PipThumbnails);

            var changes = 0;
            vm.PipThumbnails.CollectionChanged += (_, _) => changes++;

            // Nothing changed: the cards (each with a running thumbnail) are not touched.
            vm.RefreshPipThumbnails();
            Assert.Equal(0, changes);
            Assert.Equal(new[] { tabs[1], tabs[2], tabs[3] }, vm.PipThumbnails);

            // Swapping tab 2 in drops its card and adds the old active one; the others stay.
            vm.ActiveTab = tabs[2];
            Assert.DoesNotContain(tabs[2], vm.PipThumbnails);
            Assert.Contains(tabs[0], vm.PipThumbnails);
            Assert.Contains(tabs[1], vm.PipThumbnails);
            Assert.Contains(tabs[3], vm.PipThumbnails);
            Assert.Equal(3, vm.PipThumbnails.Count);
        }

        [Fact]
        public void LivePipThumbnails_AreOnByDefault_AndTheSettingTurnsThemOff()
        {
            string file = Path.Combine(Path.GetTempPath(), $"gordian_shell_test_{Guid.NewGuid():N}.json");
            try
            {
                var vm = new ViewportViewModel(file);
                Assert.True(vm.LivePipThumbnails);
                var first = vm.AddSession(NewSession(0));
                var second = vm.AddSession(NewSession(1));
                Assert.True(second.ShowLiveThumbnail);
                Assert.Same(second.Session, second.LiveSession);
                Assert.False(second.ShowStatusFill);

                vm.LivePipThumbnails = false;
                Assert.False(second.ShowLiveThumbnail);
                Assert.Null(second.LiveSession); // the thumbnail control shows no character and draws nothing
                Assert.True(second.ShowStatusFill);
                Assert.Equal(5, vm.MaxPipStreams);

                var reloaded = new ViewportViewModel(file);
                Assert.False(reloaded.LivePipThumbnails);
                Assert.Equal(ViewportRenderSettings.DefaultPipThumbnailFrameRate, ViewportSettings.LoadOrDefault(file).PipThumbnailFrameRate);
                Assert.Same(first.Session, first.Session);
            }
            finally
            {
                if (File.Exists(file)) File.Delete(file);
            }
        }
    }
}
