// tests/Gordian.App.Tests/Graphics/TestCubeGeometryTests.cs
using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Gordian.App.Graphics;
using Veldrid;
using Xunit;

namespace Gordian.App.Tests.Graphics
{
    public class TestCubeGeometryTests
    {
        [Fact]
        public void TestCubeVertex_HasExpectedMemoryLayout()
        {
            // Vertex: Vector3 Position (12 bytes) + RgbaFloat Color (16 bytes) = 28 bytes
            int size = Marshal.SizeOf<TestCubeVertex>();
            Assert.Equal(28, size);
        }

        [Fact]
        public void ModelViewProjUniform_HasExpectedStd140Alignment()
        {
            // Uniform Buffer: 3x Matrix4x4 (3 * 64 bytes = 192 bytes)
            int size = Marshal.SizeOf<ModelViewProjUniform>();
            Assert.Equal(192, size);
        }

        [Fact]
        public void ModelViewProjUniform_TransformsCoordinatesCorrectly()
        {
            var world = Matrix4x4.Identity;
            var view = Matrix4x4.CreateLookAt(new Vector3(0, 0, 5), Vector3.Zero, Vector3.UnitY);
            var proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 4f, 16f / 9f, 0.1f, 100f);

            var uniform = new ModelViewProjUniform
            {
                World = world,
                View = view,
                Projection = proj
            };

            var mvp = uniform.World * uniform.View * uniform.Projection;
            var point = new Vector4(0, 0, 0, 1.0f);
            var transformed = Vector4.Transform(point, mvp);

            Assert.True(transformed.W > 0);
        }
    }
}
