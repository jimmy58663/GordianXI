// src/Gordian.App/Graphics/GpuTextureCache.cs
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Gordian.Core.Diagnostics;
using Gordian.Core.Resources.Graphics;
using Veldrid;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// Thread-safe GPU texture, texture view, and resource set cache for Veldrid rendering.
    /// Converts CPU DecodedTexture buffers to GPU textures and manages bilinear samplers.
    /// Clean-room implementation referencing FFXI texture resolution conventions.
    /// </summary>
    public sealed class GpuTextureCache : IDisposable
    {
        private readonly GraphicsDevice _gd;
        private readonly ResourceLayout _textureLayout;
        private readonly Sampler _sampler;

        private readonly ConcurrentDictionary<string, (Texture Tex, TextureView View, ResourceSet Set)> _cache =
            new(StringComparer.OrdinalIgnoreCase);

        private Texture _defaultTexture = null!;
        private TextureView _defaultTextureView = null!;
        private ResourceSet _defaultResourceSet = null!;
        private bool _disposed;

        public ResourceSet DefaultResourceSet => _defaultResourceSet;
        public Sampler Sampler => _sampler;

        public GpuTextureCache(GraphicsDevice gd, ResourceLayout textureLayout)
        {
            _gd = gd ?? throw new ArgumentNullException(nameof(gd));
            _textureLayout = textureLayout ?? throw new ArgumentNullException(nameof(textureLayout));

            var factory = _gd.ResourceFactory;

            // Shared bilinear sampler with wrap addressing (standard for repeating terrain/dungeon tiles)
            _sampler = factory.CreateSampler(new SamplerDescription(
                SamplerAddressMode.Wrap,
                SamplerAddressMode.Wrap,
                SamplerAddressMode.Wrap,
                SamplerFilter.Anisotropic,
                ComparisonKind.Never,
                maximumAnisotropy: 4,
                minimumLod: 0,
                maximumLod: 0,
                lodBias: 0,
                borderColor: SamplerBorderColor.TransparentBlack));

            CreateDefaultTexture();
        }

        private void CreateDefaultTexture()
        {
            var factory = _gd.ResourceFactory;

            // 2x2 neutral checkerboard fallback texture (light-gray and white)
            _defaultTexture = factory.CreateTexture(TextureDescription.Texture2D(
                2, 2, 1, 1,
                PixelFormat.R8_G8_B8_A8_UNorm,
                TextureUsage.Sampled));

            byte[] defaultPixels =
            {
                210, 210, 215, 255,   240, 240, 245, 255,
                240, 240, 245, 255,   210, 210, 215, 255
            };

            _gd.UpdateTexture(_defaultTexture, defaultPixels, 0, 0, 0, 2, 2, 1, 0, 0);

            _defaultTextureView = factory.CreateTextureView(_defaultTexture);
            _defaultResourceSet = factory.CreateResourceSet(new ResourceSetDescription(
                _textureLayout,
                _defaultTextureView,
                _sampler));
        }

        /// <summary>
        /// Retrieves or creates a GPU ResourceSet for the given texture name from decoded texture tables.
        /// Falls back to the default neutral checkerboard if the texture is missing.
        /// </summary>
        public ResourceSet GetOrCreateResourceSet(
            string? textureName,
            IReadOnlyDictionary<string, DecodedTexture>? decodedTextures)
        {
            if (string.IsNullOrWhiteSpace(textureName))
            {
                return _defaultResourceSet;
            }

            if (_cache.TryGetValue(textureName, out var entry))
            {
                return entry.Set;
            }

            if (decodedTextures != null && decodedTextures.TryGetValue(textureName, out var decoded) && decoded != null)
            {
                try
                {
                    var factory = _gd.ResourceFactory;
                    uint width = (uint)Math.Max(1, decoded.Width);
                    uint height = (uint)Math.Max(1, decoded.Height);

                    var tex = factory.CreateTexture(TextureDescription.Texture2D(
                        width, height, 1, 1,
                        PixelFormat.R8_G8_B8_A8_UNorm,
                        TextureUsage.Sampled));

                    _gd.UpdateTexture(tex, decoded.RgbaPixels, 0, 0, 0, width, height, 1, 0, 0);

                    var view = factory.CreateTextureView(tex);
                    var set = factory.CreateResourceSet(new ResourceSetDescription(
                        _textureLayout,
                        view,
                        _sampler));

                    _cache[textureName] = (tex, view, set);
                    return set;
                }
                catch (Exception ex)
                {
                    GordianLog.Warning("Graphics", $"Failed to upload GPU texture '{textureName}': {ex.Message}");
                }
            }

            return _defaultResourceSet;
        }

        public void Clear()
        {
            foreach (var kvp in _cache)
            {
                kvp.Value.Set.Dispose();
                kvp.Value.View.Dispose();
                kvp.Value.Tex.Dispose();
            }
            _cache.Clear();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            Clear();

            _defaultResourceSet.Dispose();
            _defaultTextureView.Dispose();
            _defaultTexture.Dispose();
            _sampler.Dispose();
        }
    }
}
