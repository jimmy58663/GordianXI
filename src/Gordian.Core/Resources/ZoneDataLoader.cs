// src/Gordian.Core/Resources/ZoneDataLoader.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Gordian.Core.Diagnostics;
using Gordian.Core.Resources.Containers;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Models;

namespace Gordian.Core.Resources
{
    /// <summary>
    /// Clean-room loader and section parser for FFXI Zone DAT files.
    /// Extracts 3D zone terrain geometry (Section 0x2E) and texture palettes (Section 0x20).
    /// Derived from community specifications in xi-model-viewer (https://github.com/vekien/xi-model-viewer)
    /// and LandSandBoat (https://github.com/LandSandBoat/server).
    /// </summary>
    public static class ZoneDataLoader
    {
        private const int ModelBaseHi = 0x147B3;

        private static readonly HashSet<string> WeatherDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "fine", "suny", "clod", "mist", "dryw", "heat", "rain", "squl",
            "dust", "sand", "wind", "stom", "snow", "bliz", "thdr", "bolt",
            "aura", "ligt", "fogd", "dark", "even"
        };


        private static string? ResolveCurrentWeather(Stack<string> stack)
        {
            if (stack.Count == 0) return null;
            foreach (var dir in stack)
            {
                if (WeatherDirectoryNames.Contains(dir))
                {
                    return dir.ToLowerInvariant();
                }
            }
            return null;
        }

        /// <summary>
        /// Finds the Section 0x05 generator whose linked geometry is the given Section 0x2E mesh,
        /// preferring generators declared in the same weather directory.
        /// Generator linkage referenced from xi-model-viewer (https://github.com/vekien/xi-model-viewer,
        /// ui/js/particle/system.js linked-data resolution).
        /// </summary>
        private static (ParticleGeneratorDefinition? Generator, string? Weather) FindDrawingGenerator(
            List<(string DatId, string? Weather, string? ParentDir, ParticleGeneratorDefinition Generator)> generators,
            string meshDatId,
            string meshName,
            string? weather,
            bool preferCompactScale)
        {
            (ParticleGeneratorDefinition? Generator, string? Weather) best = (null, null);
            foreach (var (_, genWeather, _, gen) in generators)
            {
                var setup = gen.Setup;
                if (setup == null) continue;
                if (setup.LinkedDataType is ParticleLinkedDataType.SpriteSheet or ParticleLinkedDataType.LensFlare) continue;
                if (!string.Equals(setup.LinkedDataId, meshDatId, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(setup.LinkedDataId, meshName, StringComparison.OrdinalIgnoreCase)) continue;

                // Sun discs share their mesh with giant corona/glare shells (e.g. sun3 scale <40, 30, 100>).
                if (preferCompactScale && (gen.Scale.X > 5.0f || gen.Scale.Y > 5.0f)) continue;

                if (string.Equals(genWeather, weather, StringComparison.OrdinalIgnoreCase)) return (gen, genWeather);
                best.Generator ??= gen;
                best.Weather ??= genWeather;
            }
            return best;
        }

        /// <summary>
        /// Copies a generator's render state (rotation, color, blend, clock alpha curve and celestial tints) onto a sky layer.
        /// </summary>
        private static void ApplyGeneratorRenderState(WeatherSkyLayer layer, ParticleGeneratorDefinition? gen, string? genWeather, ZoneEnvironmentData envData)
        {
            if (gen == null) return;

            layer.GeneratorId = gen.DatId;
            layer.Rotation = gen.Rotation;
            layer.BaseColor = gen.BaseColor;
            layer.BlendMode = gen.BlendMode;
            layer.DayOfWeekColors = gen.DayOfWeekColors;
            layer.MoonPhaseColors = gen.MoonPhaseColors;

            if (!string.IsNullOrEmpty(gen.ClockAlphaKeyFrameId))
            {
                KeyFrameCurve? curve = null;
                if (!string.IsNullOrEmpty(genWeather)) envData.KeyFrameCurves.TryGetValue($"{genWeather}/{gen.ClockAlphaKeyFrameId}", out curve);
                if (curve == null) envData.KeyFrameCurves.TryGetValue(gen.ClockAlphaKeyFrameId, out curve);
                layer.ClockAlphaCurve = curve;
            }
        }

        /// <summary>
        /// Calculates the canonical FFXI ROM File ID for a zone's 3D model container DAT.
        /// </summary>
        public static int GetZoneModelFileId(int zoneId)
        {
            if (zoneId < 256)
            {
                return 100 + zoneId;
            }

            return ModelBaseHi + (zoneId - 256);
        }

        /// <summary>
        /// Parses an entire Zone DAT container buffer into structured ZoneGeometry and DecodedTextures.
        /// </summary>
        public static ZoneGeometry ParseZoneContainer(
            ReadOnlySpan<byte> datBytes,
            int zoneId,
            ReadOnlySpan<byte> table1 = default,
            ReadOnlySpan<byte> table2 = default,
            Dictionary<string, DecodedTexture>? outTextures = null,
            SharedEffectResources? sharedEffects = null)
        {
            var zone = new ZoneGeometry { ZoneId = zoneId };
            var headers = DatSectionWalker.ReadHeaders(datBytes);

            int texCount = 0, meshSectionCount = 0;
            var templates = new Dictionary<string, List<MeshGroup>>(StringComparer.OrdinalIgnoreCase);
            var realMeshNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var raw0x2ESubmeshes = new List<MeshGroup>();
            var pendingSkyMeshes = new List<(string Name, string DatId, string? Weather, List<MeshGroup> Submeshes)>();
            var generatorPlacements = new List<(string DatId, string? Weather, string? ParentDir, ParticleGeneratorDefinition Generator)>();
            var spriteSheets = new Dictionary<string, SpriteSheetMesh>(StringComparer.OrdinalIgnoreCase);
            var dirStack = new Stack<string>();
            var envData = new ZoneEnvironmentData();
            DatSectionHeader? zoneDefHeader = null;

            void RegisterTemplate(string name, List<MeshGroup> meshList, bool isRealName)
            {
                if (string.IsNullOrWhiteSpace(name)) return;
                if (isRealName) realMeshNames.Add(name);

                if (templates.TryGetValue(name, out var existing))
                {
                    int existingVerts = 0;
                    for (int v = 0; v < existing.Count; v++) existingVerts += existing[v].Vertices.Length;
                    int newVerts = 0;
                    for (int v = 0; v < meshList.Count; v++) newVerts += meshList[v].Vertices.Length;

                    if (newVerts > existingVerts)
                    {
                        templates[name] = meshList;
                    }
                }
                else
                {
                    templates[name] = meshList;
                }
            }

            for (int i = 0; i < headers.Count; i++)
            {
                var header = headers[i];
                if (header.DataOffset + header.DataSizeBytes > datBytes.Length)
                {
                    continue;
                }

                var payload = datBytes.Slice(header.DataOffset, header.DataSizeBytes);

                switch (header.TypeCode)
                {
                    case DatSectionType.Texture:
                    {
                        texCount++;
                        var texture = TextureDecoder.DecodeTexture(payload);
                        if (texture != null && outTextures != null)
                        {
                            outTextures[texture.Name] = texture;
                            string trimmed = texture.Name.Trim();
                            if (!string.IsNullOrEmpty(trimmed) && !outTextures.ContainsKey(trimmed))
                            {
                                outTextures[trimmed] = texture;
                            }
                            if (texture.Name.Length > 8)
                            {
                                string shortName = texture.Name.Substring(8).Trim();
                                if (!string.IsNullOrEmpty(shortName) && !outTextures.ContainsKey(shortName))
                                {
                                    outTextures[shortName] = texture;
                                }
                            }
                        }
                        break;
                    }

                    case DatSectionType.ZoneMesh:
                    {
                        meshSectionCount++;
                        byte[] workingCopy = payload.ToArray();

                        // If key tables provided, attempt decryption
                        if (!table1.IsEmpty && !table2.IsEmpty && workingCopy.Length >= 16)
                        {
                            ZoneMeshDecoder.DecryptZoneMesh(workingCopy, table1, table2);
                        }

                        var submeshes = ZoneMeshDecoder.ParseZoneMesh(workingCopy);
                        if (submeshes.Count > 0)
                        {
                            raw0x2ESubmeshes.AddRange(submeshes);
                            string primaryName = submeshes[0].Name;
                            if (ZoneDefDecoder.IsSkyMesh(primaryName) || ZoneDefDecoder.IsSkyMesh(header.DatId))
                            {
                                string? currentWeather = ResolveCurrentWeather(dirStack);
                                pendingSkyMeshes.Add((primaryName, header.DatId, currentWeather, submeshes));
                            }

                            RegisterTemplate(primaryName, submeshes, isRealName: true);

                            int spaceIdx = primaryName.LastIndexOf(' ');
                            if (spaceIdx >= 0 && spaceIdx < primaryName.Length - 1)
                            {
                                string tail = primaryName.Substring(spaceIdx + 1).Trim();
                                if (!string.IsNullOrEmpty(tail))
                                {
                                    RegisterTemplate(tail, submeshes, isRealName: true);
                                }
                            }

                            if (!string.IsNullOrEmpty(header.DatId) && !header.DatId.Equals(primaryName, StringComparison.OrdinalIgnoreCase))
                            {
                                RegisterTemplate(header.DatId, submeshes, isRealName: false);
                            }
                        }
                        break;
                    }

                    case DatSectionType.ParticleKeyFrameData:
                    {
                        var curve = ParticleKeyFrameDecoder.DecodeKeyFrame(payload, header.DatId);
                        if (curve != null)
                        {
                            string? weather = ResolveCurrentWeather(dirStack);
                            if (!string.IsNullOrEmpty(weather))
                            {
                                envData.AddKeyFrameCurve($"{weather}/{header.DatId}", curve);
                            }
                            envData.AddKeyFrameCurve(header.DatId, curve);
                        }
                        break;
                    }

                    case DatSectionType.ParticleGenerator:
                    {
                        var generator = ParticleGeneratorDecoder.DecodeGenerator(payload, header.DatId);
                        if (generator != null)
                        {
                            string? weather = ResolveCurrentWeather(dirStack);
                            if (!string.IsNullOrEmpty(weather))
                            {
                                envData.AddParticleGenerator($"{weather}/{header.DatId}", generator);
                            }
                            envData.AddParticleGenerator(header.DatId, generator);
                            generatorPlacements.Add((header.DatId, weather, dirStack.Count > 0 ? dirStack.Peek() : null, generator));

                            // Detect zone water surface generators with UV scroll velocity
                            if (generator.UVScrollVelocity != Vector2.Zero &&
                                (IsWaterGenerator(header.DatId) || IsWaterGenerator(generator.Setup?.LinkedDataId ?? string.Empty)))
                            {
                                // In retail FFXI, generator UVScrollVelocity is per-frame at 60 FPS (e.g. 0.0005).
                                // Scale to gentle per-second drift velocity (translateAmount * 30f, capped to 0.025f max for natural ocean pace).
                                float vx = Math.Clamp(generator.UVScrollVelocity.X * 30.0f, -0.025f, 0.025f);
                                float vy = Math.Clamp(generator.UVScrollVelocity.Y * 30.0f, -0.025f, 0.025f);
                                envData.WaterUVScroll = new Vector2(vx, vy);
                            }
                        }
                        break;
                    }

                    case DatSectionType.SpriteSheetMesh:
                    {
                        var sheet = SpriteSheetDecoder.Decode(payload, header.DatId);
                        if (sheet != null)
                        {
                            string? weather = ResolveCurrentWeather(dirStack);
                            if (!string.IsNullOrEmpty(weather))
                            {
                                spriteSheets.TryAdd($"{weather}/{header.DatId}", sheet);
                            }
                            spriteSheets.TryAdd(header.DatId, sheet);
                        }
                        break;
                    }

                    case DatSectionType.ZoneDef:
                    {
                        zoneDefHeader ??= header;
                        break;
                    }

                    case DatSectionType.Directory:
                    {
                        dirStack.Push(header.DatId);
                        break;
                    }

                    case DatSectionType.End:
                    {
                        if (dirStack.Count > 0)
                        {
                            dirStack.Pop();
                        }
                        break;
                    }

                    case DatSectionType.Environment:
                    {
                        var keyframe = EnvironmentDecoder.DecodeEnvironmentKeyframe(payload, header.DatId);
                        if (keyframe != null)
                        {
                            // Outdoor celestial weather environments in FFXI reside under the 'weat' directory tree (or root in synthetic tests).
                            // Cave/tunnel sub-environments under 'ev01', 'ev02', etc. have Indoors=true, ClearColor=0, and all-zero
                            // black sky slices; they must not pollute the celestial sky dome.
                            bool isSubEnv = false;
                            bool underWeat = false;
                            foreach (var d in dirStack)
                            {
                                if (string.Equals(d, "weat", StringComparison.OrdinalIgnoreCase))
                                {
                                    underWeat = true;
                                }
                                if (d.StartsWith("ev", StringComparison.OrdinalIgnoreCase))
                                {
                                    isSubEnv = true;
                                }
                            }

                            if ((underWeat || dirStack.Count == 0 || !isSubEnv) && !keyframe.Indoors)
                            {
                                string weather = ResolveCurrentWeather(dirStack) ?? "fine";
                                if (string.Equals(weather, "weat", StringComparison.OrdinalIgnoreCase))
                                {
                                    weather = "fine";
                                }
                                envData.AddKeyframe(weather, keyframe);
                            }
                        }
                        break;
                    }
                }
            }

            // Resolve pending weather sky layers and celestial discs
            var seenCelestialSkyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int s = 0; s < pendingSkyMeshes.Count; s++)
            {
                var (meshName, datId, weather, submeshes) = pendingSkyMeshes[s];
                bool isCelestial = ZoneDefDecoder.IsCelestialMesh(meshName) || ZoneDefDecoder.IsCelestialMesh(datId);

                if (isCelestial)
                {
                    if (!seenCelestialSkyNames.Add(meshName))
                    {
                        continue; // Star/moon/sun are duplicated under every weather folder — keep one
                    }
                    weather = null; // Universal across all weathers
                }

                // Match the generator that draws this mesh: a generator names its geometry by LinkedDataId,
                // and its own DatId may coincide with an unrelated mesh (weat/*/star: generator 'star' draws
                // mesh 'sta1', generator 'sta1' draws mesh 'star').
                bool isSunMesh = isCelestial &&
                    (meshName.StartsWith("sun", StringComparison.OrdinalIgnoreCase) || datId.StartsWith("sun", StringComparison.OrdinalIgnoreCase));
                var (matchedGen, matchedGenWeather) = FindDrawingGenerator(generatorPlacements, datId, meshName, weather, isSunMesh);
                if (matchedGen == null)
                {
                    matchedGenWeather = weather;
                    if (!string.IsNullOrEmpty(weather))
                    {
                        envData.ParticleGenerators.TryGetValue($"{weather}/{datId}", out matchedGen);
                        if (matchedGen == null)
                        {
                            envData.ParticleGenerators.TryGetValue($"{weather}/{meshName}", out matchedGen);
                        }
                    }
                    if (matchedGen == null)
                    {
                        envData.ParticleGenerators.TryGetValue(datId, out matchedGen);
                    }
                    if (matchedGen == null)
                    {
                        envData.ParticleGenerators.TryGetValue(meshName, out matchedGen);
                    }
                }

                ParticleAttachType attachType = ParticleAttachType.None;
                if (matchedGen != null && matchedGen.AttachType != ParticleAttachType.None)
                {
                    attachType = matchedGen.AttachType;
                }
                else if (isCelestial)
                {
                    if (meshName.Contains("moon", StringComparison.OrdinalIgnoreCase) || datId.Contains("moon", StringComparison.OrdinalIgnoreCase) ||
                        meshName.Contains("kasa", StringComparison.OrdinalIgnoreCase) || datId.Contains("kasa", StringComparison.OrdinalIgnoreCase))
                    {
                        attachType = ParticleAttachType.Moon;
                    }
                    else if (meshName.Contains("sun", StringComparison.OrdinalIgnoreCase) || datId.Contains("sun", StringComparison.OrdinalIgnoreCase))
                    {
                        attachType = ParticleAttachType.Sun;
                    }
                    else
                    {
                        // Star domes, stardust, and celestial spheres wrap camera
                        attachType = ParticleAttachType.None;
                    }
                }

                // Default cloud drift velocity: if no generator specified a UV drift and it's not a celestial body,
                // clouds drift gently across the sky dome at canonical speed
                Vector2 uvScroll = matchedGen?.UVScrollVelocity ?? Vector2.Zero;
                if (uvScroll == Vector2.Zero && !isCelestial)
                {
                    uvScroll = new Vector2(0.00015f, 0.00003f);
                }

                // Untextured celestial meshes (e.g. the moon halo disc) are authentic and draw with a white texel.
                string inferredTexture = string.Empty;
                if (submeshes.All(s => string.IsNullOrWhiteSpace(s.TextureName)) &&
                    (meshName.Contains("sun", StringComparison.OrdinalIgnoreCase) || datId.Contains("sun", StringComparison.OrdinalIgnoreCase)))
                {
                    inferredTexture = "sunsphere"; // Celestial sun disc identifier; rendered untextured with radiant golden core
                }

                string textureName = submeshes.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s.TextureName))?.TextureName ?? inferredTexture;

                Vector3 layerScale = matchedGen?.Scale ?? Vector3.One;
                if (isCelestial && (attachType == ParticleAttachType.Sun || meshName.Contains("sun", StringComparison.OrdinalIgnoreCase)))
                {
                    if (layerScale.X > 5.0f || layerScale.Y > 5.0f || layerScale.Z > 5.0f)
                    {
                        layerScale = new Vector3(2.0f, 2.0f, 2.0f);
                    }
                }

                Vector3 rawBasePos = matchedGen?.Setup?.BasePosition ?? Vector3.Zero;
                Vector3 displayBasePos = new Vector3(-rawBasePos.X, MathF.Abs(rawBasePos.Y), rawBasePos.Z);

                var layer = new WeatherSkyLayer
                {
                    Name = meshName,
                    DatId = datId,
                    WeatherId = weather,
                    IsCelestial = isCelestial,
                    AttachType = attachType,
                    UVScroll = uvScroll,
                    Position = displayBasePos,
                    Scale = layerScale,
                    TextureName = textureName,
                    FollowCamera = matchedGen?.Setup?.FollowCamera ?? true,
                    FogEnabled = matchedGen?.Setup?.FogEnabled ?? false,
                    IsBlend = true,
                    NoCull = true
                };
                ApplyGeneratorRenderState(layer, matchedGen, matchedGenWeather, envData);

                // Convert sky geometry to display coordinates (-x, -y, z) matching retail FFXI and world placements
                foreach (var submesh in submeshes)
                {
                    var srcVerts = submesh.Vertices;
                    var dstVerts = new MeshVertex[srcVerts.Length];
                    Vector3 minBounds = new(float.MaxValue);
                    Vector3 maxBounds = new(float.MinValue);

                    for (int v = 0; v < srcVerts.Length; v++)
                    {
                        var sv = srcVerts[v];
                        var displayPos = new Vector3(-sv.Position.X, -sv.Position.Y, sv.Position.Z);
                        var displayNormal = new Vector3(-sv.Normal.X, -sv.Normal.Y, sv.Normal.Z);

                        minBounds = Vector3.Min(minBounds, displayPos);
                        maxBounds = Vector3.Max(maxBounds, displayPos);

                        dstVerts[v] = new MeshVertex(displayPos, displayNormal, sv.TexCoord, sv.ColorRgba);
                    }

                    int[] indices = new int[submesh.Indices.Length];
                    Array.Copy(submesh.Indices, indices, submesh.Indices.Length);

                    layer.MeshGroups.Add(new MeshGroup
                    {
                        Name = submesh.Name,
                        TextureName = !string.IsNullOrWhiteSpace(submesh.TextureName) ? submesh.TextureName : inferredTexture,
                        Vertices = dstVerts,
                        Indices = indices,
                        MinBounds = minBounds,
                        MaxBounds = maxBounds,
                        IsWater = false,
                        IsBlend = true,
                        NoCull = true,
                        IsFoliage = false
                    });
                }

                zone.WeatherSkyLayers.Add(layer);
                envData.AddWeatherSkyLayer(layer);
            }

            // Celestial sprite sheets and lens flares: generators in a weather's star/moon directory that draw a
            // Section 0x21 sheet (the moon disc's twelve phases, the pole star) or a lens-flare sheet (the moon flare).
            // Sheets resolve from the weather directory, then the zone, then the shared ROM/0/0.DAT effects.
            var seenSpriteGenerators = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (genId, genWeather, parentDir, gen) in generatorPlacements)
            {
                if (!string.Equals(parentDir, "star", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(parentDir, "moon", StringComparison.OrdinalIgnoreCase)) continue;
                if (gen.Setup == null) continue;
                bool isFlare = gen.Setup.LinkedDataType == ParticleLinkedDataType.LensFlare;
                if (!isFlare && gen.Setup.LinkedDataType != ParticleLinkedDataType.SpriteSheet) continue;
                if (!seenSpriteGenerators.Add(genId)) continue;

                string linkId = gen.Setup.LinkedDataId;
                SpriteSheetMesh? sheet = null;
                if (!string.IsNullOrEmpty(genWeather)) spriteSheets.TryGetValue($"{genWeather}/{linkId}", out sheet);
                if (sheet == null) spriteSheets.TryGetValue(linkId, out sheet);
                if (sheet == null && sharedEffects != null && sharedEffects.SpriteSheets.TryGetValue(linkId, out sheet) &&
                    outTextures != null && sharedEffects.Textures.TryGetValue(sheet.TextureName, out var sharedTexture))
                {
                    outTextures.TryAdd(sheet.TextureName, sharedTexture);
                }
                if (sheet == null || sheet.IsLensFlare != isFlare || sheet.Cards.Count == 0) continue;

                Vector3 rawBase = gen.Setup.BasePosition;
                var layer = new WeatherSkyLayer
                {
                    Name = genId,
                    DatId = linkId,
                    WeatherId = null,
                    IsCelestial = true,
                    AttachType = gen.AttachType,
                    Position = new Vector3(-rawBase.X, -rawBase.Y, rawBase.Z),
                    Scale = gen.Scale,
                    TextureName = sheet.TextureName,
                    FollowCamera = gen.Setup.FollowCamera,
                    FogEnabled = false,
                    IsSpriteSheet = !isFlare,
                    IsMoonPhaseSpriteSheet = !isFlare && gen.SpriteIndexFromMoonPhase,
                    IsLensFlare = isFlare,
                    FlareOffsets = sheet.FlareOffsets,
                    NoCull = true
                };
                ApplyGeneratorRenderState(layer, gen, genWeather, envData);

                for (int c = 0; c < sheet.Cards.Count; c++)
                {
                    var card = sheet.Cards[c];
                    var dstVerts = new MeshVertex[card.Length];
                    var indices = new int[card.Length];
                    for (int v = 0; v < card.Length; v++)
                    {
                        var sv = card[v];
                        dstVerts[v] = new MeshVertex(new Vector3(-sv.Position.X, -sv.Position.Y, sv.Position.Z), sv.Normal, sv.TexCoord, sv.ColorRgba);
                        indices[v] = v;
                    }

                    layer.MeshGroups.Add(new MeshGroup
                    {
                        Name = $"{genId}#{c}",
                        TextureName = sheet.TextureName,
                        Vertices = dstVerts,
                        Indices = indices,
                        IsBlend = true,
                        NoCull = true
                    });
                }

                zone.WeatherSkyLayers.Add(layer);
                envData.AddWeatherSkyLayer(layer);
            }

            if (envData.WeatherKeyframes.Count > 0 || envData.WeatherSkyLayers.Count > 0 || envData.ParticleGenerators.Count > 0)
            {
                zone.EnvironmentData = envData;
            }

            // Phase 2: World Placement Instancing via Section 0x1C (ZoneDef)
            int placedCount = 0;
            if (zoneDefHeader.HasValue && !table1.IsEmpty)
            {
                var zdHeader = zoneDefHeader.Value;
                byte[] zdPayload = datBytes.Slice(zdHeader.DataOffset, zdHeader.DataSizeBytes).ToArray();
                int nodeCount = ZoneDefDecoder.DecryptZoneObjects(zdPayload, table1);
                var placements = ZoneDefDecoder.ParseZonePlacements(zdPayload, nodeCount);
                zone.Placements.AddRange(placements);

                for (int p = 0; p < placements.Count; p++)
                {
                    var placement = placements[p];
                    if (ZoneDefDecoder.IsSkyMesh(placement.MeshId)) continue;

                    var templateSubmeshes = ZoneDefDecoder.ResolveTemplate(placement.MeshId, templates, realMeshNames);
                    if (templateSubmeshes == null || templateSubmeshes.Count == 0) continue;

                    var trsMatrix = ZoneDefDecoder.CreateTrsMatrix(placement.Position, placement.Rotation, placement.Scale);

                    for (int s = 0; s < templateSubmeshes.Count; s++)
                    {
                        var instantiated = ZoneDefDecoder.InstantiateSubmesh(templateSubmeshes[s], trsMatrix, placement.MeshId);
                        zone.MeshGroups.Add(instantiated);
                        placedCount++;
                    }
                }
            }

            // Phase 2b: Water Surface & Wave Ripple Effect Instancing via Section 0x05 Particle Generators
            // In retail FFXI, shoreline ripples (shi1..shi5), wave crests (hna0, hum1, humt), and localized water ripples (mizu)
            // are positioned dynamically by particle generators rather than static Section 0x1C placements.
            for (int g = 0; g < generatorPlacements.Count; g++)
            {
                var (datId, weather, _, gen) = generatorPlacements[g];
                if (gen.Setup == null || string.IsNullOrWhiteSpace(gen.Setup.LinkedDataId)) continue;

                string linkId = gen.Setup.LinkedDataId;
                if (ZoneDefDecoder.IsSkyMesh(linkId) || ZoneDefDecoder.IsCelestialMesh(linkId) ||
                    linkId.StartsWith("yuk", StringComparison.OrdinalIgnoreCase) ||
                    linkId.StartsWith("yku", StringComparison.OrdinalIgnoreCase) ||
                    linkId.StartsWith("hi0", StringComparison.OrdinalIgnoreCase)) continue;

                var templateSubmeshes = ZoneDefDecoder.ResolveTemplate(linkId, templates, realMeshNames);
                if (templateSubmeshes == null || templateSubmeshes.Count == 0) continue;

                bool isWater = IsWaterGenerator(datId) ||
                               IsWaterGenerator(linkId) ||
                               ZoneDefDecoder.IsWaterMesh(linkId, templateSubmeshes[0].TextureName);

                // Only instantiate generators that represent genuine water ripples, waves, or surface effects.
                // Atmospheric effects, heat shimmer, or horizon sunset glare planes must not be baked into static terrain.
                if (!isWater) continue;

                var trsMatrix = ZoneDefDecoder.CreateTrsMatrix(gen.Setup.BasePosition, Vector3.Zero, gen.Scale);

                for (int s = 0; s < templateSubmeshes.Count; s++)
                {
                    var instantiated = ZoneDefDecoder.InstantiateSubmesh(templateSubmeshes[s], trsMatrix, datId);
                    if (isWater)
                    {
                        instantiated.IsWater = true;
                        instantiated.IsBlend = true;
                        instantiated.NoCull = true;
                    }
                    instantiated.UVScroll = gen.UVScrollVelocity;
                    zone.MeshGroups.Add(instantiated);
                    placedCount++;
                }
            }

            // Fallback: If no placements were instantiated (e.g. non-world DAT or unit test without 0x1C)
            if (placedCount == 0 && raw0x2ESubmeshes.Count > 0)
            {
                for (int i = 0; i < raw0x2ESubmeshes.Count; i++)
                {
                    var raw = raw0x2ESubmeshes[i];
                    var convVerts = new MeshVertex[raw.Vertices.Length];
                    Vector3 minB = new(float.MaxValue);
                    Vector3 maxB = new(float.MinValue);
                    for (int v = 0; v < raw.Vertices.Length; v++)
                    {
                        var sv = raw.Vertices[v];
                        var dp = new Vector3(-sv.Position.X, -sv.Position.Y, sv.Position.Z);
                        var dn = new Vector3(-sv.Normal.X, -sv.Normal.Y, sv.Normal.Z);
                        minB = Vector3.Min(minB, dp);
                        maxB = Vector3.Max(maxB, dp);
                        convVerts[v] = new MeshVertex(dp, dn, sv.TexCoord, sv.ColorRgba);
                    }
                    zone.MeshGroups.Add(new MeshGroup
                    {
                        Name = raw.Name,
                        TextureName = raw.TextureName,
                        Vertices = convVerts,
                        Indices = raw.Indices,
                        MinBounds = minB,
                        MaxBounds = maxB,
                        IsBlend = raw.IsBlend,
                        NoCull = raw.NoCull,
                        IsFoliage = raw.IsFoliage,
                        IsWater = raw.IsWater
                    });
                }
            }

            GordianLog.Debug("RES", $"ParseZoneContainer(zone={zoneId}): {headers.Count} sections, {texCount} textures, {meshSectionCount} ZoneMesh(0x2E) sections, {zone.Placements.Count} placements → {zone.MeshGroups.Count} submeshes (placed={placedCount}). keysProvided={!table1.IsEmpty && !table2.IsEmpty}");

            return zone;
        }

        /// <summary>
        /// Attempts to scan FFXiMain.dll or FFXiMain.dll.orig within the game directory
        /// to locate the two 256-byte substitution decryption key tables.
        /// </summary>
        public static bool TryExtractKeyTables(
            string gameDirectory,
            out byte[] table1,
            out byte[] table2)
        {
            table1 = Array.Empty<byte>();
            table2 = Array.Empty<byte>();

            if (string.IsNullOrWhiteSpace(gameDirectory) || !Directory.Exists(gameDirectory))
            {
                return false;
            }

            string[] candidateDlls =
            {
                Path.Combine(gameDirectory, "FFXiMain.dll"),
                Path.Combine(gameDirectory, "FFXiMain.dll.orig"),
            };

            foreach (var dllPath in candidateDlls)
            {
                if (!File.Exists(dllPath)) continue;

                try
                {
                    byte[] dllBytes = File.ReadAllBytes(dllPath);
                    if (TryExtractFromDllBytes(dllBytes, out table1, out table2))
                    {
                        GordianLog.Info("RES", $"Extracted zone decryption key tables from {Path.GetFileName(dllPath)}");
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    GordianLog.Warning("RES", $"Could not read {dllPath} for key table extraction: {ex.Message}");
                }
            }

            return false;
        }

        /// <summary>
        /// Scans binary DLL memory bytes for Table1Sig and Table2Sig signatures.
        /// </summary>
        public static bool TryExtractFromDllBytes(
            ReadOnlySpan<byte> dllBytes,
            out byte[] table1,
            out byte[] table2)
        {
            table1 = Array.Empty<byte>();
            table2 = Array.Empty<byte>();

            const int scanStart = 0x30000;
            const int tableSize = 256;

            int idx1 = FindSignature(dllBytes, ZoneMeshDecoder.Table1Sig, scanStart);
            if (idx1 < 0) idx1 = FindSignature(dllBytes, ZoneMeshDecoder.Table1Sig, 0);

            int idx2 = FindSignature(dllBytes, ZoneMeshDecoder.Table2Sig, scanStart);
            if (idx2 < 0) idx2 = FindSignature(dllBytes, ZoneMeshDecoder.Table2Sig, 0);

            if (idx1 < 0 || idx2 < 0 || idx1 + tableSize > dllBytes.Length || idx2 + tableSize > dllBytes.Length)
            {
                return false;
            }

            table1 = dllBytes.Slice(idx1, tableSize).ToArray();
            table2 = dllBytes.Slice(idx2, tableSize).ToArray();
            return true;
        }

        private static int FindSignature(ReadOnlySpan<byte> data, ReadOnlySpan<byte> sig, int startOffset)
        {
            if (data.Length < sig.Length) return -1;
            int start = Math.Clamp(startOffset, 0, data.Length - sig.Length);

            for (int i = start; i <= data.Length - sig.Length; i++)
            {
                if (data.Slice(i, sig.Length).SequenceEqual(sig))
                {
                    return i;
                }
            }

            return -1;
        }

        public static bool IsWaterGenerator(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string n = name.ToLowerInvariant();
            return n.StartsWith("umi") || n.StartsWith("shi") || n.StartsWith("sea") ||
                   n.StartsWith("water") || n.StartsWith("ocean") || n.StartsWith("lowsea") ||
                   n.StartsWith("suimen") || n.StartsWith("huw") ||
                   n.StartsWith("ka") || n.StartsWith("kb") || n.StartsWith("hum") ||
                   n.StartsWith("hna") || n.StartsWith("mizu");
        }
    }
}
