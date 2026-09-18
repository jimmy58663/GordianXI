using System;
using Veldrid;
using Xunit;

namespace Gordian.App.Tests.Graphics
{
    public class VeldridSmokeTests
    {
        [Fact]
        public void VerifyCreateD3D11WithSwapchainDescSignature()
        {
            var options = new GraphicsDeviceOptions(false, PixelFormat.R16_UNorm, true, ResourceBindingModel.Improved, true, true);
            // Verify method info exists
            var method = typeof(GraphicsDevice).GetMethod("CreateD3D11", new[] { typeof(GraphicsDeviceOptions), typeof(SwapchainDescription) });
            Assert.NotNull(method);

            var vulkanMethod = typeof(GraphicsDevice).GetMethod("CreateVulkan", new[] { typeof(GraphicsDeviceOptions), typeof(SwapchainDescription) });
            Assert.NotNull(vulkanMethod);
        }
    }
}
