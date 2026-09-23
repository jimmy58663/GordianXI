using System;
using Veldrid;
using Xunit;

namespace Gordian.App.Tests.Graphics
{
    public class VeldridSmokeTests
    {
        [Fact]
        public void TestHeadlessD3D11()
        {
            if (!OperatingSystem.IsWindows()) return;
            var gd = GraphicsDevice.CreateD3D11(new GraphicsDeviceOptions(false, PixelFormat.R32_Float, false, ResourceBindingModel.Improved, true, true));
            Assert.NotNull(gd);
            gd.Dispose();
        }
    }
}
