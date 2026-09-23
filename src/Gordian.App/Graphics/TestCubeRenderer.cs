// src/Gordian.App/Graphics/TestCubeRenderer.cs
using System;
using System.Numerics;
using System.Text;
using Veldrid;
using Veldrid.SPIRV;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// Represents a colored 3D vertex for viewport test rendering.
    /// </summary>
    public readonly record struct TestCubeVertex(Vector3 Position, RgbaFloat Color);

    /// <summary>
    /// Uniform buffer structure containing Model-View-Projection matrices.
    /// Matched to GLSL std140 layout (three 64-byte 4x4 column-major matrices).
    /// </summary>
    public struct ModelViewProjUniform
    {
        public Matrix4x4 World;
        public Matrix4x4 View;
        public Matrix4x4 Projection;
    }

    /// <summary>
    /// Self-contained 3D test renderer demonstrating Veldrid hardware acceleration, SPIR-V cross-compilation,
    /// and swapchain presentation.
    /// </summary>
    public sealed class TestCubeRenderer : IDisposable
    {
        private readonly GraphicsDevice _gd;
        private DeviceBuffer _vertexBuffer = null!;
        private DeviceBuffer _indexBuffer = null!;
        private DeviceBuffer _mvpBuffer = null!;
        private Pipeline _pipeline = null!;
        private ResourceSet _resourceSet = null!;
        private CommandList _commandList = null!;
        private float _rotation;
        private bool _disposed;

        public TestCubeRenderer(GraphicsDevice gd)
        {
            _gd = gd ?? throw new ArgumentNullException(nameof(gd));
            InitializeResources();
        }

        private void InitializeResources()
        {
            var factory = _gd.ResourceFactory;

            // 1. Cube Vertices (8 corners with distinct vibrant colors)
            var vertices = new TestCubeVertex[]
            {
                // Front face
                new(new Vector3(-1, -1,  1), new RgbaFloat(1f, 0f, 0f, 1f)), // Bottom-Left: Red
                new(new Vector3( 1, -1,  1), new RgbaFloat(0f, 1f, 0f, 1f)), // Bottom-Right: Green
                new(new Vector3( 1,  1,  1), new RgbaFloat(0f, 0f, 1f, 1f)), // Top-Right: Blue
                new(new Vector3(-1,  1,  1), new RgbaFloat(1f, 1f, 0f, 1f)), // Top-Left: Yellow
                // Back face
                new(new Vector3(-1, -1, -1), new RgbaFloat(1f, 0f, 1f, 1f)), // Bottom-Left: Magenta
                new(new Vector3( 1, -1, -1), new RgbaFloat(0f, 1f, 1f, 1f)), // Bottom-Right: Cyan
                new(new Vector3( 1,  1, -1), new RgbaFloat(1f, 1f, 1f, 1f)), // Top-Right: White
                new(new Vector3(-1,  1, -1), new RgbaFloat(0.3f, 0.3f, 0.3f, 1f)) // Top-Left: Dark Gray
            };

            _vertexBuffer = factory.CreateBuffer(new BufferDescription(
                (uint)(vertices.Length * 28), // 12 bytes Position (Vec3) + 16 bytes Color (Vec4) = 28 bytes
                BufferUsage.VertexBuffer));
            _gd.UpdateBuffer(_vertexBuffer, 0, vertices);

            // 2. Cube Indices (12 triangles / 36 indices)
            ushort[] indices =
            {
                // Front
                0, 1, 2, 0, 2, 3,
                // Right
                1, 5, 6, 1, 6, 2,
                // Back
                5, 4, 7, 5, 7, 6,
                // Left
                4, 0, 3, 4, 3, 7,
                // Top
                3, 2, 6, 3, 6, 7,
                // Bottom
                4, 5, 1, 4, 1, 0
            };

            _indexBuffer = factory.CreateBuffer(new BufferDescription(
                (uint)(indices.Length * sizeof(ushort)),
                BufferUsage.IndexBuffer));
            _gd.UpdateBuffer(_indexBuffer, 0, indices);

            // 3. MVP Uniform Buffer (3x 64 bytes = 192 bytes)
            _mvpBuffer = factory.CreateBuffer(new BufferDescription(
                192,
                BufferUsage.UniformBuffer | BufferUsage.Dynamic));

            // 4. Shaders (Cross-compiled from GLSL to HLSL / MSL / Vulkan via SPIR-V)
            string vertexShaderGlsl = @"#version 450
layout(location = 0) in vec3 Position;
layout(location = 1) in vec4 Color;
layout(location = 0) out vec4 fsin_Color;

layout(set = 0, binding = 0) uniform ModelViewProj
{
    mat4 World;
    mat4 View;
    mat4 Projection;
};

void main()
{
    gl_Position = Projection * View * World * vec4(Position, 1.0);
    fsin_Color = Color;
}
";

            string fragmentShaderGlsl = @"#version 450
layout(location = 0) in vec4 fsin_Color;
layout(location = 0) out vec4 fsout_Color;

void main()
{
    fsout_Color = fsin_Color;
}
";

            var vsDesc = new ShaderDescription(
                ShaderStages.Vertex,
                Encoding.UTF8.GetBytes(vertexShaderGlsl),
                "main");
            var fsDesc = new ShaderDescription(
                ShaderStages.Fragment,
                Encoding.UTF8.GetBytes(fragmentShaderGlsl),
                "main");

            Shader[] shaders = factory.CreateFromSpirv(vsDesc, fsDesc);

            // 5. Resource Layout & Resource Set
            var resourceLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("ModelViewProj", ResourceKind.UniformBuffer, ShaderStages.Vertex)));

            _resourceSet = factory.CreateResourceSet(new ResourceSetDescription(resourceLayout, _mvpBuffer));

            // 6. Graphics Pipeline
            var vertexLayout = new VertexLayoutDescription(
                new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3, 0),
                new VertexElementDescription("Color", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4, 12));

            var pipelineDesc = new GraphicsPipelineDescription
            {
                BlendState = BlendStateDescription.SingleOverrideBlend,
                DepthStencilState = new DepthStencilStateDescription(
                    depthTestEnabled: true,
                    depthWriteEnabled: true,
                    comparisonKind: ComparisonKind.LessEqual),
                RasterizerState = new RasterizerStateDescription(
                    cullMode: FaceCullMode.Back,
                    fillMode: PolygonFillMode.Solid,
                    frontFace: FrontFace.Clockwise,
                    depthClipEnabled: true,
                    scissorTestEnabled: false),
                PrimitiveTopology = PrimitiveTopology.TriangleList,
                ResourceLayouts = new[] { resourceLayout },
                ShaderSet = new ShaderSetDescription(new[] { vertexLayout }, shaders),
                Outputs = _gd.SwapchainFramebuffer.OutputDescription
            };

            _pipeline = factory.CreateGraphicsPipeline(pipelineDesc);
            _commandList = factory.CreateCommandList();
        }

        /// <summary>
        /// Renders one frame of the animated test cube to the device's main swapchain.
        /// </summary>
        public void Render(float deltaSeconds, uint width, uint height)
        {
            if (_disposed || _gd == null || _gd.MainSwapchain == null)
            {
                return;
            }

            _rotation += deltaSeconds * 1.2f;

            // Compute MVP Matrices
            float aspect = Math.Max(0.1f, (float)width / Math.Max(1, height));
            var world = Matrix4x4.CreateFromYawPitchRoll(_rotation, _rotation * 0.7f, 0f);
            var view = Matrix4x4.CreateLookAt(new Vector3(0, 1.5f, 4.5f), Vector3.Zero, Vector3.UnitY);
            var proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 4f, aspect, 0.1f, 100f);

            var uniform = new ModelViewProjUniform
            {
                World = world,
                View = view,
                Projection = proj
            };

            _gd.UpdateBuffer(_mvpBuffer, 0, ref uniform);

            // Record Render Commands
            _commandList.Begin();
            _commandList.SetFramebuffer(_gd.SwapchainFramebuffer);
            // Sleek dark background matching GordianXI HUD palette (#11141c)
            _commandList.ClearColorTarget(0, new RgbaFloat(0.067f, 0.078f, 0.11f, 1.0f));
            _commandList.ClearDepthStencil(1.0f);

            _commandList.SetPipeline(_pipeline);
            _commandList.SetGraphicsResourceSet(0, _resourceSet);
            _commandList.SetVertexBuffer(0, _vertexBuffer);
            _commandList.SetIndexBuffer(_indexBuffer, IndexFormat.UInt16);
            _commandList.DrawIndexed(36, 1, 0, 0, 0);

            _commandList.End();

            // Submit & Present
            _gd.SubmitCommands(_commandList);
            _gd.SwapBuffers();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _commandList.Dispose();
            _pipeline.Dispose();
            _resourceSet.Dispose();
            _mvpBuffer.Dispose();
            _vertexBuffer.Dispose();
            _indexBuffer.Dispose();
        }
    }
}
