// tests/Gordian.App.Tests/Graphics/ViewportViewModelTests.cs
using System.ComponentModel;
using Gordian.App.Graphics;
using Gordian.App.ViewModels;
using Xunit;

namespace Gordian.App.Tests.Graphics
{
    public class ViewportViewModelTests
    {
        [Fact]
        public void DefaultState_HasAutoBackendAndTelemetryDefaults()
        {
            var vm = new ViewportViewModel();

            Assert.Equal(GraphicsBackendPreference.Auto, vm.SelectedBackend);
            Assert.True(vm.IsVsyncEnabled);
            Assert.Contains(GraphicsBackendPreference.Direct3D11, vm.AvailableBackends);
            Assert.Contains(GraphicsBackendPreference.Vulkan, vm.AvailableBackends);
            Assert.Contains(GraphicsBackendPreference.Metal, vm.AvailableBackends);
        }

        [Fact]
        public void PropertyChanges_EmitPropertyChangedNotifications()
        {
            var vm = new ViewportViewModel();
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
    }
}
