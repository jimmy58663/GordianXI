// tests/Gordian.App.Tests/Graphics/GraphicsBackendSelectorTests.cs
using System;
using Gordian.App.Graphics;
using Veldrid;
using Xunit;

namespace Gordian.App.Tests.Graphics
{
    public class GraphicsBackendSelectorTests
    {
        [Fact]
        public void AutoPreference_SelectsPlatformNativeBackends()
        {
            var candidates = VeldridDeviceManager.GetBackendCandidates(GraphicsBackendPreference.Auto);
            Assert.NotEmpty(candidates);

            if (OperatingSystem.IsWindows())
            {
                Assert.Equal(GraphicsBackend.Direct3D11, candidates[0]);
                Assert.Contains(GraphicsBackend.Vulkan, candidates);
            }
            else if (OperatingSystem.IsLinux())
            {
                Assert.Equal(GraphicsBackend.Vulkan, candidates[0]);
            }
            else if (OperatingSystem.IsMacOS())
            {
                Assert.Equal(GraphicsBackend.Metal, candidates[0]);
            }
        }

        [Fact]
        public void Direct3D11Preference_PrioritizesDirect3D11()
        {
            var candidates = VeldridDeviceManager.GetBackendCandidates(GraphicsBackendPreference.Direct3D11);
            Assert.Equal(GraphicsBackend.Direct3D11, candidates[0]);
            Assert.Contains(GraphicsBackend.Vulkan, candidates);
        }

        [Fact]
        public void VulkanPreference_PrioritizesVulkan()
        {
            var candidates = VeldridDeviceManager.GetBackendCandidates(GraphicsBackendPreference.Vulkan);
            Assert.Equal(GraphicsBackend.Vulkan, candidates[0]);
        }

        [Fact]
        public void MetalPreference_PrioritizesMetal()
        {
            var candidates = VeldridDeviceManager.GetBackendCandidates(GraphicsBackendPreference.Metal);
            Assert.Equal(GraphicsBackend.Metal, candidates[0]);
        }
    }
}
