// src/Gordian.App/Graphics/EntityRenderer.cs
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using Gordian.Core.Diagnostics;
using Gordian.Core.Graphics;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Models;
using Gordian.Core.World;
using Veldrid;
using Veldrid.SPIRV;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// Hardware-accelerated 3D entity renderer for GordianXI.
    /// Renders players, NPCs, monsters, and trusts at live WorldEntity coordinates in bind pose
    /// with authentic FFXI orientation, lighting, and distance fog.
    /// Clean-room implementation referencing FFXI entity rendering conventions and xi-model-viewer (https://github.com/vekien/xi-model-viewer).
    /// </summary>
    public sealed class EntityRenderer : IDisposable
    {
        private readonly GraphicsDevice _gd;
        private readonly DeviceBuffer _entityUniformBuffer;
        private readonly ResourceLayout _sceneLayout;
        private readonly ResourceLayout _textureLayout;
        private readonly ResourceSet _entityResourceSet;
        private readonly Pipeline _pipeline;
        private readonly GpuTextureCache _textureCache;

        // FFXI Entity DAT -> Screen transform: 180-degree turn about X axis: diag(1, -1, -1, 1).
        // Entity geometry is authored Y-down; this maps it to Y-up without reflection or mirroring.
        private static readonly Matrix4x4 EntityRotMatrix = new(
            1.0f,  0.0f,  0.0f, 0.0f,
            0.0f, -1.0f,  0.0f, 0.0f,
            0.0f,  0.0f, -1.0f, 0.0f,
            0.0f,  0.0f,  0.0f, 1.0f
        );

        private readonly ConcurrentDictionary<string, GpuEntityModel> _gpuModelCache = new();
        private GpuEntityModel? _fallbackPlayerProxy;
        private GpuEntityModel? _fallbackNpcProxy;
        private GpuEntityModel? _fallbackMonsterProxy;
        private bool _disposed;

        public int DrawCalls { get; private set; }
        public int VisibleEntities { get; private set; }
        public int CulledEntities { get; private set; }

        private sealed class GpuSubmesh : IDisposable
        {
            public string TextureName { get; init; } = string.Empty;
            public DeviceBuffer VertexBuffer { get; init; } = null!;
            public DeviceBuffer IndexBuffer { get; init; } = null!;
            public uint IndexCount { get; init; }

            public void Dispose()
            {
                VertexBuffer?.Dispose();
                IndexBuffer?.Dispose();
            }
        }

        private sealed class GpuEntityModel : IDisposable
        {
            public List<GpuSubmesh> Submeshes { get; } = new();
            public Vector3 MinBounds { get; init; } = -Vector3.One;
            public Vector3 MaxBounds { get; init; } = Vector3.One;
            public IReadOnlyDictionary<string, DecodedTexture>? Textures { get; init; }

            public void Dispose()
            {
                for (int i = 0; i < Submeshes.Count; i++)
                {
                    Submeshes[i].Dispose();
                }
                Submeshes.Clear();
            }
        }

        public EntityRenderer(GraphicsDevice gd)
        {
            _gd = gd ?? throw new ArgumentNullException(nameof(gd));
            var factory = _gd.ResourceFactory;

            _entityUniformBuffer = factory.CreateBuffer(new BufferDescription(
                288,
                BufferUsage.UniformBuffer | BufferUsage.Dynamic));

            _sceneLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("ZoneSceneUniforms", ResourceKind.UniformBuffer, ShaderStages.Vertex | ShaderStages.Fragment)));

            _textureLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("uTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("uSampler", ResourceKind.Sampler, ShaderStages.Fragment)));

            _entityResourceSet = factory.CreateResourceSet(new ResourceSetDescription(_sceneLayout, _entityUniformBuffer));
            _textureCache = new GpuTextureCache(_gd, _textureLayout);

            var vsDesc = new ShaderDescription(
                ShaderStages.Vertex,
                Encoding.UTF8.GetBytes(ZoneShaders.VertexShaderGlsl),
                "main");
            var fsDesc = new ShaderDescription(
                ShaderStages.Fragment,
                Encoding.UTF8.GetBytes(ZoneShaders.FragmentShaderGlsl),
                "main");

            Shader[] shaders = factory.CreateFromSpirv(vsDesc, fsDesc);

            var vertexLayout = new VertexLayoutDescription(
                new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
                new VertexElementDescription("Normal", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
                new VertexElementDescription("TexCoord", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2),
                new VertexElementDescription("Color", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Byte4_Norm));

            var pipelineDesc = new GraphicsPipelineDescription
            {
                BlendState = BlendStateDescription.SingleOverrideBlend,
                DepthStencilState = new DepthStencilStateDescription(
                    depthTestEnabled: true,
                    depthWriteEnabled: true,
                    comparisonKind: ComparisonKind.LessEqual),
                RasterizerState = new RasterizerStateDescription(
                    cullMode: FaceCullMode.None,
                    fillMode: PolygonFillMode.Solid,
                    frontFace: FrontFace.Clockwise,
                    depthClipEnabled: true,
                    scissorTestEnabled: false),
                PrimitiveTopology = PrimitiveTopology.TriangleList,
                ResourceLayouts = new[] { _sceneLayout, _textureLayout },
                ShaderSet = new ShaderSetDescription(new[] { vertexLayout }, shaders),
                Outputs = _gd.SwapchainFramebuffer.OutputDescription
            };

            _pipeline = factory.CreateGraphicsPipeline(pipelineDesc);

            BuildFallbackProxies();
        }

        /// <summary>
        /// Renders all active, spawned entities in the world into the active command list.
        /// </summary>
        public void RenderEntities(
            CommandList cl,
            ViewportCamera camera,
            ZoneEnvironmentSettings environment,
            IEnumerable<WorldEntity> entities,
            ResourceManager? resourceManager)
        {
            if (_disposed || cl == null || entities == null) return;

            int draws = 0;
            int visible = 0;
            int culled = 0;

            float fogRange = Math.Max(0.001f, environment.FogEnd - environment.FogStart);
            var frustum = camera.Frustum;

            cl.SetPipeline(_pipeline);

            foreach (var entity in entities)
            {
                if (!entity.IsSpawned) continue;

                // Approximate or actual bounding box in world space
                Vector3 pos = entity.Position;
                Vector3 minBox = pos + new Vector3(-1.0f, -0.2f, -1.0f);
                Vector3 maxBox = pos + new Vector3(1.0f, 2.2f, 1.0f);

                if (!frustum.IntersectsBox(minBox, maxBox))
                {
                    culled++;
                    continue;
                }

                // Resolve or build GPU model
                GpuEntityModel? gpuModel = null;
                if (resourceManager != null && resourceManager.TryLoadEntityModel(entity, out var entityModel) && entityModel != null)
                {
                    gpuModel = GetOrUploadGpuModel(entityModel);
                }

                if (gpuModel == null || gpuModel.Submeshes.Count == 0)
                {
                    gpuModel = entity.Type switch
                    {
                        EntityType.Player => _fallbackPlayerProxy,
                        EntityType.Monster => _fallbackMonsterProxy,
                        _ => _fallbackNpcProxy
                    };
                }

                if (gpuModel == null || gpuModel.Submeshes.Count == 0) continue;

                visible++;

                // Compute authentic entity world transform
                // 1. Heading angle: FFXI Direction 0=East(+X), 64=South(+Z), 128=West(-X), 192=North(-Z)
                float headingRad = (entity.Direction / 256.0f) * MathF.PI * 2.0f;
                var headingRot = Matrix4x4.CreateRotationY(-headingRad);

                bool isFallback = ReferenceEquals(gpuModel, _fallbackPlayerProxy) ||
                                  ReferenceEquals(gpuModel, _fallbackNpcProxy) ||
                                  ReferenceEquals(gpuModel, _fallbackMonsterProxy);
                var rotMatrix = isFallback ? Matrix4x4.Identity : EntityRotMatrix;

                var worldMatrix = rotMatrix * headingRot * Matrix4x4.CreateTranslation(pos);

                var uniform = new ZoneSceneUniform
                {
                    World = worldMatrix,
                    View = camera.ViewMatrix,
                    Projection = camera.ProjectionMatrix,
                    SunDirection = new Vector4(environment.SunDirection, 0.0f),
                    SunColor = new Vector4(environment.SunColor, 1.0f),
                    AmbientColor = new Vector4(environment.AmbientColor, 1.0f),
                    FogColor = environment.FogColor,
                    FogParams = new Vector4(environment.FogStart, environment.FogEnd, 1.0f / fogRange, environment.FogDensity),
                    EyePosition = new Vector4(camera.Position, 1.0f)
                };

                cl.UpdateBuffer(_entityUniformBuffer, 0, ref uniform);
                cl.SetGraphicsResourceSet(0, _entityResourceSet);

                for (int m = 0; m < gpuModel.Submeshes.Count; m++)
                {
                    var submesh = gpuModel.Submeshes[m];
                    var texSet = _textureCache.GetOrCreateResourceSet(submesh.TextureName, gpuModel.Textures);

                    cl.SetGraphicsResourceSet(1, texSet);
                    cl.SetVertexBuffer(0, submesh.VertexBuffer);
                    cl.SetIndexBuffer(submesh.IndexBuffer, IndexFormat.UInt16);
                    cl.DrawIndexed(submesh.IndexCount, 1, 0, 0, 0);
                    draws++;
                }
            }

            DrawCalls = draws;
            VisibleEntities = visible;
            CulledEntities = culled;
        }

        private GpuEntityModel GetOrUploadGpuModel(EntityModel model)
        {
            string key = string.IsNullOrEmpty(model.Name) ? model.GetHashCode().ToString() : model.Name;
            if (_gpuModelCache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var factory = _gd.ResourceFactory;
            var gpuModel = new GpuEntityModel
            {
                MinBounds = model.MinBounds,
                MaxBounds = model.MaxBounds,
                Textures = model.Textures
            };

            for (int i = 0; i < model.MeshGroups.Count; i++)
            {
                var mg = model.MeshGroups[i];
                if (mg.Vertices.Length == 0 || mg.Indices.Length == 0) continue;

                var vb = factory.CreateBuffer(new BufferDescription(
                    (uint)(mg.Vertices.Length * 36),
                    BufferUsage.VertexBuffer));
                _gd.UpdateBuffer(vb, 0, mg.Vertices);

                var ushortIndices = new ushort[mg.Indices.Length];
                for (int k = 0; k < mg.Indices.Length; k++)
                {
                    ushortIndices[k] = (ushort)mg.Indices[k];
                }

                var ib = factory.CreateBuffer(new BufferDescription(
                    (uint)(ushortIndices.Length * sizeof(ushort)),
                    BufferUsage.IndexBuffer));
                _gd.UpdateBuffer(ib, 0, ushortIndices);

                gpuModel.Submeshes.Add(new GpuSubmesh
                {
                    TextureName = mg.TextureName,
                    VertexBuffer = vb,
                    IndexBuffer = ib,
                    IndexCount = (uint)ushortIndices.Length
                });
            }

            _gpuModelCache[key] = gpuModel;
            return gpuModel;
        }

        private void BuildFallbackProxies()
        {
            _fallbackPlayerProxy = CreateStylizedProxy(0xFFE08020);  // Cyan/Blue
            _fallbackNpcProxy = CreateStylizedProxy(0xFF20C0E0);     // Amber/Gold
            _fallbackMonsterProxy = CreateStylizedProxy(0xFF2020E0); // Crimson/Red
        }

        private GpuEntityModel CreateStylizedProxy(uint colorRgba)
        {
            var factory = _gd.ResourceFactory;
            var verts = new List<MeshVertex>();
            var indices = new List<ushort>();

            // Humanoid stylized mannequin marker:
            // 1. Torso/Body (prism from Y=0.0 to Y=1.4)
            // 2. Head (box from Y=1.4 to Y=1.8)
            const float halfW = 0.28f;
            const float halfD = 0.20f;
            const float hBody = 1.40f;
            const float hHead = 1.80f;

            void AddQuad(Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3, Vector3 norm, uint? quadColor = null)
            {
                uint col = quadColor ?? colorRgba;
                ushort baseIdx = (ushort)verts.Count;
                verts.Add(new MeshVertex(v0, norm, new Vector2(0, 0), col));
                verts.Add(new MeshVertex(v1, norm, new Vector2(1, 0), col));
                verts.Add(new MeshVertex(v2, norm, new Vector2(1, 1), col));
                verts.Add(new MeshVertex(v3, norm, new Vector2(0, 1), col));

                indices.Add(baseIdx);
                indices.Add((ushort)(baseIdx + 1));
                indices.Add((ushort)(baseIdx + 2));

                indices.Add(baseIdx);
                indices.Add((ushort)(baseIdx + 2));
                indices.Add((ushort)(baseIdx + 3));
            }

            // Torso
            var b0 = new Vector3(-halfW, 0, -halfD);
            var b1 = new Vector3(halfW, 0, -halfD);
            var b2 = new Vector3(halfW, 0, halfD);
            var b3 = new Vector3(-halfW, 0, halfD);

            var t0 = new Vector3(-halfW, hBody, -halfD);
            var t1 = new Vector3(halfW, hBody, -halfD);
            var t2 = new Vector3(halfW, hBody, halfD);
            var t3 = new Vector3(-halfW, hBody, halfD);

            AddQuad(t0, t1, b1, b0, -Vector3.UnitZ); // Front
            AddQuad(t1, t2, b2, b1, Vector3.UnitX);  // Right
            AddQuad(t2, t3, b3, b2, Vector3.UnitZ);  // Back
            AddQuad(t3, t0, b0, b3, -Vector3.UnitX); // Left
            AddQuad(t0, t3, t2, t1, Vector3.UnitY);  // Top

            // Head (accent visor)
            const float headW = 0.16f;
            var hB0 = new Vector3(-headW, hBody, -headW);
            var hB1 = new Vector3(headW, hBody, -headW);
            var hB2 = new Vector3(headW, hBody, headW);
            var hB3 = new Vector3(-headW, hBody, headW);

            var hT0 = new Vector3(-headW, hHead, -headW);
            var hT1 = new Vector3(headW, hHead, -headW);
            var hT2 = new Vector3(headW, hHead, headW);
            var hT3 = new Vector3(-headW, hHead, headW);

            uint visorColor = 0xFFFFFFFF; // White visor
            AddQuad(hT0, hT1, hB1, hB0, -Vector3.UnitZ, visorColor); // Face
            AddQuad(hT1, hT2, hB2, hB1, Vector3.UnitX);
            AddQuad(hT2, hT3, hB3, hB2, Vector3.UnitZ);
            AddQuad(hT3, hT0, hB0, hB3, -Vector3.UnitX);
            AddQuad(hT0, hT3, hT2, hT1, Vector3.UnitY);

            var vb = factory.CreateBuffer(new BufferDescription((uint)(verts.Count * 36), BufferUsage.VertexBuffer));
            _gd.UpdateBuffer(vb, 0, verts.ToArray());

            var ib = factory.CreateBuffer(new BufferDescription((uint)(indices.Count * sizeof(ushort)), BufferUsage.IndexBuffer));
            _gd.UpdateBuffer(ib, 0, indices.ToArray());

            var model = new GpuEntityModel
            {
                MinBounds = new Vector3(-halfW, 0, -halfD),
                MaxBounds = new Vector3(halfW, hHead, halfD)
            };
            model.Submeshes.Add(new GpuSubmesh
            {
                TextureName = string.Empty,
                VertexBuffer = vb,
                IndexBuffer = ib,
                IndexCount = (uint)indices.Count
            });

            return model;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            foreach (var kvp in _gpuModelCache)
            {
                kvp.Value.Dispose();
            }
            _gpuModelCache.Clear();

            _fallbackPlayerProxy?.Dispose();
            _fallbackNpcProxy?.Dispose();
            _fallbackMonsterProxy?.Dispose();

            _entityUniformBuffer?.Dispose();
            _sceneLayout?.Dispose();
            _textureLayout?.Dispose();
            _entityResourceSet?.Dispose();
            _pipeline?.Dispose();
            _textureCache?.Dispose();
        }
    }
}
