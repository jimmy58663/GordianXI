// tests/Gordian.App.Tests/Graphics/GraphicsBackendSelectorTests.cs
using System;
using Gordian.App.Graphics;
using NeoVeldrid;
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
                Assert.Equal(GraphicsBackend.Vulkan, candidates[0]);
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
        public void LegacyMetalPreference_SelectsVulkan()
        {
            // NeoVeldrid has no Metal backend; a saved Metal preference runs Vulkan (MoltenVK on macOS).
            var candidates = VeldridDeviceManager.GetBackendCandidates(GraphicsBackendPreference.Metal);
            Assert.Equal(GraphicsBackend.Vulkan, candidates[0]);
        }

        [Fact]
        public void LegacyOpenGLPreference_MeansAuto()
        {
            // OpenGL is no longer a choice (#390): a saved OpenGL preference runs the automatic candidates, no fallback.
            Assert.Equal(VeldridDeviceManager.GetBackendCandidates(GraphicsBackendPreference.Auto),
                VeldridDeviceManager.GetBackendCandidates(GraphicsBackendPreference.OpenGL));
            Assert.Null(SharedGraphicsDevice.FallbackNote(GraphicsBackendPreference.OpenGL, GraphicsBackend.Direct3D11));
        }

        [Theory]
        [InlineData(GraphicsBackendPreference.Auto, GraphicsBackend.Vulkan, false)]
        [InlineData(GraphicsBackendPreference.Direct3D11, GraphicsBackend.Direct3D11, false)]
        [InlineData(GraphicsBackendPreference.Vulkan, GraphicsBackend.Direct3D11, true)]
        [InlineData(GraphicsBackendPreference.Metal, GraphicsBackend.Vulkan, false)]
        public void FallbackNote_OnlyWhenTheRunningBackendIsNotThePreferredOne(GraphicsBackendPreference preference, GraphicsBackend running, bool expected)
        {
            Assert.Equal(expected, SharedGraphicsDevice.FallbackNote(preference, running) != null);
        }
    }
}
