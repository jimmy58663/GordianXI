// src/Gordian.App/Graphics/SkyDomeRenderer.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Gordian.Core.Graphics;
using Gordian.Core.Resources.Graphics;
using Veldrid;
using Veldrid.SPIRV;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// Vertex definition for celestial sky dome geometry (Position + RGBA float color).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct SkyDomeVertex
    {
        public readonly Vector3 Position;
        public readonly Vector4 Color;

        public SkyDomeVertex(Vector3 position, Vector4 color)
        {
            Position = position;
            Color = color;
        }
    }

    /// <summary>
    /// Renders an authentic celestial sky dome with smooth horizon-to-zenith color gradients.
    /// Centered dynamically at the camera eye position with depth writing disabled so all
    /// zone geometry, mountains, and models draw cleanly over it.
    /// </summary>
    public sealed class SkyDomeRenderer : IDisposable
    {
        private readonly GraphicsDevice _gd;
        private Pipeline _pipeline = null!;
        private DeviceBuffer? _vertexBuffer;
        private uint _vertexCount;
        private bool _disposed;

        public uint VertexCount => _vertexCount;
        public bool HasGeometry => _vertexBuffer != null && _vertexCount > 0;

        public SkyDomeRenderer(GraphicsDevice gd, ResourceLayout sceneLayout, OutputDescription outputDesc)
        {
            _gd = gd ?? throw new ArgumentNullException(nameof(gd));
            InitializePipeline(sceneLayout, outputDesc);
        }

        private void InitializePipeline(ResourceLayout sceneLayout, OutputDescription outputDesc)
        {
            var factory = _gd.ResourceFactory;

            var vsDesc = new ShaderDescription(
                ShaderStages.Vertex,
                Encoding.UTF8.GetBytes(ZoneShaders.SkyDomeVertexShaderGlsl),
                "main");
            var fsDesc = new ShaderDescription(
                ShaderStages.Fragment,
                Encoding.UTF8.GetBytes(ZoneShaders.SkyDomeFragmentShaderGlsl),
                "main");

            Shader[] shaders = factory.CreateFromSpirv(vsDesc, fsDesc);

            // Stride: 28 bytes (Pos 12B + Color 16B)
            var vertexLayout = new VertexLayoutDescription(
                new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
                new VertexElementDescription("Color", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4));

            var pipelineDesc = new GraphicsPipelineDescription
            {
                BlendState = BlendStateDescription.SingleDisabled,
                DepthStencilState = new DepthStencilStateDescription(
                    depthTestEnabled: true,
                    depthWriteEnabled: false,
                    comparisonKind: ComparisonKind.LessEqual),
                RasterizerState = new RasterizerStateDescription(
                    cullMode: FaceCullMode.None,
                    fillMode: PolygonFillMode.Solid,
                    frontFace: FrontFace.Clockwise,
                    depthClipEnabled: true,
                    scissorTestEnabled: false),
                PrimitiveTopology = PrimitiveTopology.TriangleList,
                ResourceLayouts = new[] { sceneLayout },
                ShaderSet = new ShaderSetDescription(new[] { vertexLayout }, shaders),
                Outputs = outputDesc
            };

            _pipeline = factory.CreateGraphicsPipeline(pipelineDesc);
        }

        /// <summary>
        /// Generates or rebuilds the celestial sky dome mesh from environment settings or Section 0x2F slices.
        /// </summary>
        public void UpdateDome(ZoneEnvironmentSettings environment)
        {
            if (_disposed || environment == null) return;

            var vertices = GenerateDomeVertices(environment);
            if (vertices.Length == 0) return;

            var factory = _gd.ResourceFactory;
            uint bufferSize = (uint)(vertices.Length * Marshal.SizeOf<SkyDomeVertex>());

            if (_vertexBuffer == null || _vertexBuffer.SizeInBytes < bufferSize)
            {
                _vertexBuffer?.Dispose();
                _vertexBuffer = factory.CreateBuffer(new BufferDescription(
                    bufferSize,
                    BufferUsage.VertexBuffer));
            }

            _gd.UpdateBuffer(_vertexBuffer, 0, vertices);
            _vertexCount = (uint)vertices.Length;
        }

        /// <summary>
        /// Generates the dome vertex array from environment settings.
        /// </summary>
        public static SkyDomeVertex[] GenerateDomeVertices(ZoneEnvironmentSettings environment)
        {
            float radius = 950f;
            int spokes = Math.Clamp((int)environment.Spokes, 12, 64);
            if (spokes == 0) spokes = 24;

            var slices = new List<(float Elevation, Vector4 Color)>();

            if (environment.SkySlices.Count >= 2)
            {
                // Use explicit DAT Section 0x2F slices
                for (int i = 0; i < environment.SkySlices.Count; i++)
                {
                    var s = environment.SkySlices[i];
                    slices.Add((s.Elevation, s.Color));
                }
            }
            else
            {
                // Procedural celestial dome gradient: Skirt (-0.15), Horizon (0.0), Mid-sky, Zenith (1.0)
                float[] elevs = { -0.15f, 0.0f, 0.20f, 0.45f, 0.70f, 1.0f };
                for (int i = 0; i < elevs.Length; i++)
                {
                    float e = elevs[i];
                    Vector4 color;
                    if (e <= 0.0f)
                    {
                        color = environment.SkyHorizonColor;
                    }
                    else
                    {
                        float t = MathF.Pow(e, 0.75f);
                        color = Vector4.Lerp(environment.SkyHorizonColor, environment.SkyZenithColor, t);
                    }
                    slices.Add((e, color));
                }
            }

            var vertexList = new List<SkyDomeVertex>();

            for (int i = 0; i < slices.Count - 1; i++)
            {
                var lo = slices[i];
                var hi = slices[i + 1];

                // Elevation angles (0 = horizon, pi/2 = zenith)
                float aLo = 0.5f * MathF.PI * lo.Elevation;
                float aHi = 0.5f * MathF.PI * hi.Elevation;

                float yLo = radius * MathF.Sin(aLo);
                float rLo = radius * MathF.Cos(aLo);

                float yHi = radius * MathF.Sin(aHi);
                float rHi = radius * MathF.Cos(aHi);

                for (int j = 0; j < spokes; j++)
                {
                    float t0 = (2f * MathF.PI * j) / spokes;
                    float t1 = (2f * MathF.PI * (j + 1)) / spokes;

                    var vLo0 = new SkyDomeVertex(new Vector3(rLo * MathF.Cos(t0), yLo, rLo * MathF.Sin(t0)), lo.Color);
                    var vLo1 = new SkyDomeVertex(new Vector3(rLo * MathF.Cos(t1), yLo, rLo * MathF.Sin(t1)), lo.Color);
                    var vHi0 = new SkyDomeVertex(new Vector3(rHi * MathF.Cos(t0), yHi, rHi * MathF.Sin(t0)), hi.Color);
                    var vHi1 = new SkyDomeVertex(new Vector3(rHi * MathF.Cos(t1), yHi, rHi * MathF.Sin(t1)), hi.Color);

                    // Triangle 1: vLo0 -> vLo1 -> vHi1
                    vertexList.Add(vLo0);
                    vertexList.Add(vLo1);
                    vertexList.Add(vHi1);

                    // Triangle 2: vLo0 -> vHi1 -> vHi0
                    vertexList.Add(vLo0);
                    vertexList.Add(vHi1);
                    vertexList.Add(vHi0);
                }
            }

            return vertexList.ToArray();
        }

        /// <summary>
        /// Renders the celestial sky dome to the active command list.
        /// Must be called after clearing the framebuffer and before opaque terrain.
        /// </summary>
        public void Render(CommandList commandList, ResourceSet sceneResourceSet)
        {
            if (_disposed || _vertexBuffer == null || _vertexCount == 0) return;

            commandList.SetPipeline(_pipeline);
            commandList.SetGraphicsResourceSet(0, sceneResourceSet);
            commandList.SetVertexBuffer(0, _vertexBuffer);
            commandList.Draw(_vertexCount, 1, 0, 0);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _vertexBuffer?.Dispose();
                _pipeline?.Dispose();
            }
        }
    }
}
