// src/Gordian.App/Graphics/EntityRenderer.cs
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Gordian.Core.Animation;
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
    /// Renders players, NPCs, monsters, and trusts at live WorldEntity coordinates with GPU
    /// joint-palette skeletal skinning, authentic FFXI orientation, lighting, and distance fog.
    /// Clean-room implementation referencing FFXI entity rendering conventions and xi-model-viewer (https://github.com/vekien/xi-model-viewer).
    /// </summary>
    public sealed class EntityRenderer : IDisposable
    {
        private readonly GraphicsDevice _gd;
        private readonly DeviceBuffer _entityUniformBuffer;
        private readonly ResourceLayout _sceneLayout;
        private readonly ResourceLayout _textureLayout;
        private readonly ResourceLayout _jointPaletteLayout;
        private readonly ResourceSet _entityResourceSet;
        private readonly Pipeline _pipeline;
        private readonly Pipeline _skinnedPipeline;
        private readonly GpuTextureCache _textureCache;

        // FFXI Entity DAT -> Screen transform: 180-degree turn about X axis: diag(1, -1, -1, 1).
        // Entity geometry is authored Y-down; this maps it to Y-up without reflection or mirroring.
        private static readonly Matrix4x4 EntityRotMatrix = new(
            1.0f,  0.0f,  0.0f, 0.0f,
            0.0f, -1.0f,  0.0f, 0.0f,
            0.0f,  0.0f, -1.0f, 0.0f,
            0.0f,  0.0f,  0.0f, 1.0f
        );

        private static readonly (AnimationCategory Category, string ClipName)[] CategoryClipNames =
        {
            (AnimationCategory.Idle, "idl"),
            (AnimationCategory.Walk, "wlk"),
            (AnimationCategory.Run, "run"),
            (AnimationCategory.Combat, "cmb"),
            (AnimationCategory.Death, "dth"),
        };

        private readonly ConcurrentDictionary<string, GpuEntityModel> _gpuModelCache = new();
        private readonly ConcurrentDictionary<uint, JointPaletteEntry> _jointPaletteByEntity = new();
        private readonly Vector4[] _paletteScratch = new Vector4[ZoneShaders.MaxPaletteJoints * 2];
        private GpuEntityModel? _fallbackPlayerProxy;
        private GpuEntityModel? _fallbackNpcProxy;
        private GpuEntityModel? _fallbackMonsterProxy;
        private bool _loggedPaletteOverflow;
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
            public bool IsSkinned { get; init; }

            public void Dispose()
            {
                for (int i = 0; i < Submeshes.Count; i++)
                {
                    Submeshes[i].Dispose();
                }
                Submeshes.Clear();
            }
        }

        private readonly record struct JointPaletteEntry(DeviceBuffer Buffer, ResourceSet Set)
        {
            public void Dispose()
            {
                Buffer.Dispose();
                Set.Dispose();
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SkinnedGpuVertex
        {
            public Vector3 Position0;
            public Vector3 Position1;
            public Vector3 Normal0;
            public Vector3 Normal1;
            public float Weight0;
            public float Weight1;
            public float Joint0;
            public float Joint1;
            public Vector2 TexCoord;
            public uint ColorRgba;
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

            _jointPaletteLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("JointPalette", ResourceKind.UniformBuffer, ShaderStages.Vertex)));

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

            var skinnedVsDesc = new ShaderDescription(
                ShaderStages.Vertex,
                Encoding.UTF8.GetBytes(ZoneShaders.SkinnedVertexShaderGlsl),
                "main");
            Shader[] skinnedShaders = factory.CreateFromSpirv(skinnedVsDesc, fsDesc);

            var skinnedVertexLayout = new VertexLayoutDescription(
                new VertexElementDescription("Position0", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
                new VertexElementDescription("Position1", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
                new VertexElementDescription("Normal0", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
                new VertexElementDescription("Normal1", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
                new VertexElementDescription("Weights", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2),
                new VertexElementDescription("Joints", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2),
                new VertexElementDescription("TexCoord", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2),
                new VertexElementDescription("Color", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Byte4_Norm));

            var skinnedPipelineDesc = new GraphicsPipelineDescription
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
                ResourceLayouts = new[] { _sceneLayout, _textureLayout, _jointPaletteLayout },
                ShaderSet = new ShaderSetDescription(new[] { skinnedVertexLayout }, skinnedShaders),
                Outputs = _gd.SwapchainFramebuffer.OutputDescription
            };

            _skinnedPipeline = factory.CreateGraphicsPipeline(skinnedPipelineDesc);

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
            ResourceManager? resourceManager,
            float deltaSeconds = 0f,
            uint localPlayerServerId = 0,
            bool isLocalPlayerEngaged = false)
        {
            if (_disposed || cl == null || entities == null) return;

            int draws = 0;
            int visible = 0;
            int culled = 0;

            float fogRange = Math.Max(0.001f, environment.FogEnd - environment.FogStart);
            var frustum = camera.Frustum;

            foreach (var entity in entities)
            {
                if (!entity.IsSpawned)
                {
                    if (_jointPaletteByEntity.TryRemove(entity.ServerId, out var stalePalette))
                    {
                        stalePalette.Dispose();
                    }
                    continue;
                }

                // Server position is in FFXI coordinates: (x, y, z).
                // Mapped to terrain display coordinates: (-x, -y, z).
                Vector3 pos = new Vector3(-entity.Position.X, -entity.Position.Y, entity.Position.Z);
                Vector3 minBox = pos + new Vector3(-1.0f, -0.2f, -1.0f);
                Vector3 maxBox = pos + new Vector3(1.0f, 2.2f, 1.0f);

                if (!frustum.IntersectsBox(minBox, maxBox))
                {
                    culled++;
                    continue;
                }

                // Resolve or build GPU model
                GpuEntityModel? gpuModel = null;
                EntityModel? entityModel = null;
                if (resourceManager != null && resourceManager.TryLoadEntityModel(entity, out entityModel) && entityModel != null)
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
                    entityModel = null;
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

                bool isSkinned = gpuModel.IsSkinned && entityModel?.Skeleton != null && entityModel.Skeleton.Count > 0;

                cl.SetPipeline(isSkinned ? _skinnedPipeline : _pipeline);
                cl.SetGraphicsResourceSet(0, _entityResourceSet);

                if (isSkinned)
                {
                    bool isLocalPlayer = entity.ServerId == localPlayerServerId;
                    bool engaged = isLocalPlayer ? isLocalPlayerEngaged : entity.ClaimServerId != 0;
                    var category = AnimationStateClassifier.Classify(entity, engaged);
                    entity.Animation.Advance(deltaSeconds, category);

                    AnimationClip? clip = null;
                    for (int c = 0; c < CategoryClipNames.Length; c++)
                    {
                        if (CategoryClipNames[c].Category == category)
                        {
                            entityModel!.Animations.TryGetValue(CategoryClipNames[c].ClipName, out clip);
                            break;
                        }
                    }

                    bool loop = category != AnimationCategory.Death;
                    var palette = _jointPaletteByEntity.GetOrAdd(entity.ServerId, _ => CreateJointPalette());
                    UpdateJointPalette(cl, palette.Buffer, entityModel!.Skeleton!, clip, entity.Animation.ElapsedSeconds, loop, entityModel.ParentOverrides);
                    cl.SetGraphicsResourceSet(2, palette.Set);
                }

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

        private JointPaletteEntry CreateJointPalette()
        {
            var factory = _gd.ResourceFactory;
            uint bufferSize = (uint)(ZoneShaders.MaxPaletteJoints * 16 * 2); // vec4 uRot[N] + vec4 uTrans[N]
            var buffer = factory.CreateBuffer(new BufferDescription(bufferSize, BufferUsage.UniformBuffer | BufferUsage.Dynamic));
            var set = factory.CreateResourceSet(new ResourceSetDescription(_jointPaletteLayout, buffer));
            return new JointPaletteEntry(buffer, set);
        }

        private void UpdateJointPalette(CommandList cl, DeviceBuffer buffer, Skeleton skeleton, AnimationClip? clip, float timeSeconds, bool loop, IReadOnlyDictionary<int, int>? parentOverrides)
        {
            var pose = SkeletonPoseEvaluator.EvaluatePose(skeleton, clip, timeSeconds, loop, parentOverrides);
            int count = pose.Rotations.Length;

            if (count > ZoneShaders.MaxPaletteJoints)
            {
                if (!_loggedPaletteOverflow)
                {
                    GordianLog.Warning("GFX", $"Skeleton has {count} joints, exceeding MaxPaletteJoints ({ZoneShaders.MaxPaletteJoints}); clamping.");
                    _loggedPaletteOverflow = true;
                }
                count = ZoneShaders.MaxPaletteJoints;
            }

            for (int i = 0; i < count; i++)
            {
                var r = pose.Rotations[i];
                _paletteScratch[i] = new Vector4(r.X, r.Y, r.Z, r.W);
                var t = pose.Translations[i];
                _paletteScratch[ZoneShaders.MaxPaletteJoints + i] = new Vector4(t.X, t.Y, t.Z, 0f);
            }
            for (int i = count; i < ZoneShaders.MaxPaletteJoints; i++)
            {
                _paletteScratch[i] = new Vector4(0f, 0f, 0f, 1f);
                _paletteScratch[ZoneShaders.MaxPaletteJoints + i] = Vector4.Zero;
            }

            cl.UpdateBuffer(buffer, 0, _paletteScratch);
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
                Textures = model.Textures,
                IsSkinned = true
            };

            for (int i = 0; i < model.AnimatedMeshGroups.Count; i++)
            {
                var mg = model.AnimatedMeshGroups[i];
                if (mg.Vertices.Length == 0 || mg.Indices.Length == 0) continue;

                var gpuVerts = new SkinnedGpuVertex[mg.Vertices.Length];
                for (int v = 0; v < mg.Vertices.Length; v++)
                {
                    var sv = mg.Vertices[v];
                    gpuVerts[v] = new SkinnedGpuVertex
                    {
                        Position0 = sv.Position0,
                        Position1 = sv.Position1,
                        Normal0 = sv.Normal0,
                        Normal1 = sv.Normal1,
                        Weight0 = sv.Weight0,
                        Weight1 = sv.Weight1,
                        Joint0 = sv.Joint0,
                        Joint1 = sv.Joint1,
                        TexCoord = sv.TexCoord,
                        ColorRgba = sv.ColorRgba
                    };
                }

                var vb = factory.CreateBuffer(new BufferDescription(
                    (uint)(gpuVerts.Length * Marshal.SizeOf<SkinnedGpuVertex>()),
                    BufferUsage.VertexBuffer));
                _gd.UpdateBuffer(vb, 0, gpuVerts);

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
                MaxBounds = new Vector3(halfW, hHead, halfD),
                IsSkinned = false
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

            foreach (var kvp in _jointPaletteByEntity)
            {
                kvp.Value.Dispose();
            }
            _jointPaletteByEntity.Clear();

            _fallbackPlayerProxy?.Dispose();
            _fallbackNpcProxy?.Dispose();
            _fallbackMonsterProxy?.Dispose();

            _entityUniformBuffer?.Dispose();
            _sceneLayout?.Dispose();
            _textureLayout?.Dispose();
            _jointPaletteLayout?.Dispose();
            _entityResourceSet?.Dispose();
            _pipeline?.Dispose();
            _skinnedPipeline?.Dispose();
            _textureCache?.Dispose();
        }
    }
}
