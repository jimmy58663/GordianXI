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

        /// <summary>Uploads by texture source (DAT and section), shared by every texture decoded from it.</summary>
        private readonly ConcurrentDictionary<string, (Texture Tex, TextureView View, ResourceSet Set)> _bySource = new(StringComparer.Ordinal);

        /// <summary>Every upload, for disposal.</summary>
        private readonly ConcurrentBag<(Texture Tex, TextureView View, ResourceSet Set)> _uploads = new();

        /// <summary>Texture names already reported missing (logged once each).</summary>
        private readonly ConcurrentDictionary<string, byte> _missing = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Resource sets by decoded texture instance (<see cref="GetOrCreateResourceSet(DecodedTexture)"/>).</summary>
        private readonly ConcurrentDictionary<DecodedTexture, ResourceSet> _byTexture = new(ReferenceEqualityComparer.Instance);

        private Texture _defaultTexture = null!;
        private TextureView _defaultTextureView = null!;
        private ResourceSet _defaultResourceSet = null!;
        private Texture _defaultWaterTexture = null!;
        private TextureView _defaultWaterTextureView = null!;
        private ResourceSet _defaultWaterResourceSet = null!;
        private Texture _neutralTexture = null!;
        private TextureView _neutralTextureView = null!;
        private ResourceSet _neutralResourceSet = null!;
        private bool _disposed;

        public ResourceSet DefaultResourceSet => _defaultResourceSet;
        public ResourceSet DefaultWaterResourceSet => _defaultWaterResourceSet;

        /// <summary>
        /// Neutral 0x80 grey 1x1 texture (all channels) bound for untextured particle geometry such as the sun dome and
        /// moon halo. The particle stage doubles the texel, so grey is neutral; white would double color and alpha.
        /// Default particle texel referenced from xi-model-viewer (https://github.com/vekien/xi-model-viewer,
        /// ui/js/particleDrawer.js default texture).
        /// </summary>
        public ResourceSet NeutralResourceSet => _neutralResourceSet;
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
            CreateDefaultWaterTexture();

            _neutralTexture = factory.CreateTexture(TextureDescription.Texture2D(1, 1, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));
            _gd.UpdateTexture(_neutralTexture, new byte[] { 0x80, 0x80, 0x80, 0x80 }, 0, 0, 0, 1, 1, 1, 0, 0);
            _neutralTextureView = factory.CreateTextureView(_neutralTexture);
            _neutralResourceSet = factory.CreateResourceSet(new ResourceSetDescription(_textureLayout, _neutralTextureView, _sampler));
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

        private void CreateDefaultWaterTexture()
        {
            var factory = _gd.ResourceFactory;
            const uint width = 128;
            const uint height = 128;

            // 128x128 seamless procedural water normal/ripple texture with authentic ocean crests
            _defaultWaterTexture = factory.CreateTexture(TextureDescription.Texture2D(
                width, height, 1, 1,
                PixelFormat.R8_G8_B8_A8_UNorm,
                TextureUsage.Sampled));

            byte[] waterPixels = new byte[width * height * 4];
            const float tau = MathF.PI * 2f;

            for (uint y = 0; y < height; y++)
            {
                float ny = (float)y / height;
                for (uint x = 0; x < width; x++)
                {
                    float nx = (float)x / width;
                    // Superimposed harmonic sines ensuring seamless periodicity across [0, 1]
                    float w1 = MathF.Sin(tau * (nx * 3f + ny * 2f));
                    float w2 = MathF.Sin(tau * (nx * 2f - ny * 4f));
                    float w3 = MathF.Cos(tau * (nx * 5f + ny * 3f));
                    float combined = (w1 + w2 + w3) / 3f; // [-1, 1]
                    float ripple = (combined + 1f) * 0.5f; // [0, 1]

                    // Authentic FFXI coastal ocean palette matching DAT umi1 texture with sharp wave crests and deep troughs
                    float crest = MathF.Pow(ripple, 3.0f);
                    byte r = (byte)Math.Clamp(28 + (int)(ripple * 45 + crest * 110), 0, 255);
                    byte g = (byte)Math.Clamp(52 + (int)(ripple * 60 + crest * 130), 0, 255);
                    byte b = (byte)Math.Clamp(78 + (int)(ripple * 80 + crest * 155), 0, 255);
                    byte a = (byte)Math.Clamp(110 + (int)(ripple * 50 + crest * 80), 0, 255);

                    int offset = (int)((y * width + x) * 4);
                    waterPixels[offset] = r;
                    waterPixels[offset + 1] = g;
                    waterPixels[offset + 2] = b;
                    waterPixels[offset + 3] = a;
                }
            }

            _gd.UpdateTexture(_defaultWaterTexture, waterPixels, 0, 0, 0, width, height, 1, 0, 0);
            _defaultWaterTextureView = factory.CreateTextureView(_defaultWaterTexture);
            _defaultWaterResourceSet = factory.CreateResourceSet(new ResourceSetDescription(
                _textureLayout,
                _defaultWaterTextureView,
                _sampler));
        }

        /// <summary>
        /// The texture a submesh names in a decoded texture table: the exact name, else the first whose name ends with
        /// it (or it with theirs). Null when none matches. A name is only meaningful inside its own table.
        /// </summary>
        public static DecodedTexture? Resolve(string? textureName, IReadOnlyDictionary<string, DecodedTexture>? decodedTextures)
        {
            if (string.IsNullOrWhiteSpace(textureName) || decodedTextures == null) return null;
            string cleanKey = textureName.Trim();
            if (decodedTextures.TryGetValue(cleanKey, out var decoded) && decoded != null) return decoded;
            foreach (var kvp in decodedTextures)
            {
                if (kvp.Key.EndsWith(cleanKey, StringComparison.OrdinalIgnoreCase) || cleanKey.EndsWith(kvp.Key, StringComparison.OrdinalIgnoreCase))
                {
                    return kvp.Value;
                }
            }
            return null;
        }

        /// <summary>
        /// The GPU resource set of one decoded texture. Uploads are keyed by where the texture came from
        /// (<see cref="DecodedTexture.Source"/>: DAT and section, unique across the install), so every model or zone that
        /// loaded the same section shares one upload; a texture without a source gets its own. Never by name: texture
        /// names repeat across DATs with different pixels (1,009 names among the fixed NPC models alone), and a name key
        /// drew one model's face on another (Balasiel, #163).
        /// </summary>
        public ResourceSet GetOrCreateResourceSet(DecodedTexture texture)
        {
            ArgumentNullException.ThrowIfNull(texture);
            if (_byTexture.TryGetValue(texture, out var known)) return known;
            ResourceSet set;
            if (texture.Source.Length > 0 && _bySource.TryGetValue(texture.Source, out var shared))
            {
                set = shared.Set;
            }
            else
            {
                if (Upload(texture) is not { } uploaded) return _defaultResourceSet;
                if (texture.Source.Length > 0) _bySource[texture.Source] = uploaded;
                set = uploaded.Set;
            }
            _byTexture[texture] = set;
            return set;
        }

        private (Texture Tex, TextureView View, ResourceSet Set)? Upload(DecodedTexture decoded)
        {
            try
            {
                var factory = _gd.ResourceFactory;
                uint width = (uint)Math.Max(1, decoded.Width);
                uint height = (uint)Math.Max(1, decoded.Height);
                GordianLog.Debug("GPU_TEX", $"Uploading GPU texture '{decoded.Name}' from '{decoded.Source}', {width}x{height}.");
                var tex = factory.CreateTexture(TextureDescription.Texture2D(width, height, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));
                _gd.UpdateTexture(tex, decoded.RgbaPixels, 0, 0, 0, width, height, 1, 0, 0);
                var view = factory.CreateTextureView(tex);
                var set = factory.CreateResourceSet(new ResourceSetDescription(_textureLayout, view, _sampler));
                var entry = (tex, view, set);
                _uploads.Add(entry);
                return entry;
            }
            catch (Exception ex)
            {
                GordianLog.Warning("Graphics", $"Failed to upload GPU texture '{decoded.Name}': {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// The resource set of the texture a submesh names in its own texture table (<see cref="Resolve"/>), uploaded
        /// as <see cref="GetOrCreateResourceSet(DecodedTexture)"/> keys it; the default checkerboard when the table has none.
        /// </summary>
        public ResourceSet GetOrCreateResourceSet(
            string? textureName,
            IReadOnlyDictionary<string, DecodedTexture>? decodedTextures)
        {
            if (string.IsNullOrWhiteSpace(textureName)) return _defaultResourceSet;
            if (Resolve(textureName, decodedTextures) is { } decoded) return GetOrCreateResourceSet(decoded);
            if (_missing.TryAdd(textureName.Trim(), 0)) GordianLog.Warning("GPU_TEX", $"TEXTURE NOT FOUND: '{textureName}', returning default checkerboard!");
            return _defaultResourceSet;
        }

        /// <summary>
        /// Retrieves or creates a GPU ResourceSet suitable for ocean/water rendering.
        /// Searches active zone textures for any texture identified as water/sea (e.g. 'sea01', 'water01', 'suimen');
        /// falls back to the procedural tileable ocean wave texture if none is present.
        /// </summary>
        public ResourceSet GetOrCreateWaterResourceSet(IReadOnlyDictionary<string, DecodedTexture>? activeTextures)
        {
            if (activeTextures != null)
            {
                // Priority 1: Full-color primary sea/water textures (umi, sea, quf, water, suimen)
                foreach (var kvp in activeTextures)
                {
                    string key = kvp.Key.ToLowerInvariant();
                    if (key.Contains("umi") || key.Contains("sea") || key.Contains("quf") || key.Contains("water") || key.Contains("suimen"))
                    {
                        var set = GetOrCreateResourceSet(kvp.Key, activeTextures);
                        if (set != _defaultResourceSet)
                        {
                            return set;
                        }
                    }
                }

                // Priority 2: Secondary wave, ripple, shoreline, or river textures
                foreach (var kvp in activeTextures)
                {
                    if (Gordian.Core.Resources.Graphics.ZoneDefDecoder.IsWaterMesh(string.Empty, kvp.Key))
                    {
                        var set = GetOrCreateResourceSet(kvp.Key, activeTextures);
                        if (set != _defaultResourceSet)
                        {
                            return set;
                        }
                    }
                }
            }

            return _defaultWaterResourceSet;
        }

        public void Clear()
        {
            while (_uploads.TryTake(out var entry))
            {
                entry.Set.Dispose();
                entry.View.Dispose();
                entry.Tex.Dispose();
            }
            _bySource.Clear();
            _byTexture.Clear();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            Clear();

            _defaultWaterResourceSet.Dispose();
            _defaultWaterTextureView.Dispose();
            _defaultWaterTexture.Dispose();

            _neutralResourceSet.Dispose();
            _neutralTextureView.Dispose();
            _neutralTexture.Dispose();
            _defaultResourceSet.Dispose();
            _defaultTextureView.Dispose();
            _defaultTexture.Dispose();
            _sampler.Dispose();
        }
    }
}
