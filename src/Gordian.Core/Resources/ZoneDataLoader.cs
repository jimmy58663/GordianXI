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
            string? weather)
        {
            (ParticleGeneratorDefinition? Generator, string? Weather) best = (null, null);
            foreach (var (_, genWeather, _, gen) in generators)
            {
                var setup = gen.Setup;
                if (setup == null) continue;
                if (setup.LinkedDataType is ParticleLinkedDataType.SpriteSheet or ParticleLinkedDataType.LensFlare) continue;
                if (!string.Equals(setup.LinkedDataId, meshDatId, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(setup.LinkedDataId, meshName, StringComparison.OrdinalIgnoreCase)) continue;

                if (string.Equals(genWeather, weather, StringComparison.OrdinalIgnoreCase)) return (gen, genWeather);
                best.Generator ??= gen;
                best.Weather ??= genWeather;
            }
            return best;
        }

        /// <summary>
        /// Copies a generator's render state (rotation, color, blend, clock alpha curve and celestial tints) onto a sky layer.
        /// </summary>
        private static void ApplyGeneratorRenderState(
            WeatherSkyLayer layer,
            ParticleGeneratorDefinition? gen,
            string? genWeather,
            ZoneEnvironmentData envData,
            IReadOnlyDictionary<ParticleGeneratorDefinition, int> authoredOrder)
        {
            if (gen == null) return;

            layer.GeneratorId = gen.DatId;
            layer.Rotation = gen.Rotation;
            layer.RotationVelocity = gen.RotationVelocity;
            layer.BaseColor = gen.BaseColor;
            layer.BlendFunc = gen.BlendFunc;
            layer.AuthoredOrder = authoredOrder.TryGetValue(gen, out int order) ? order : int.MaxValue;
            layer.DayOfWeekColors = gen.DayOfWeekColors;
            layer.MoonPhaseColors = gen.MoonPhaseColors;
            layer.ClockAlphaCurve = ResolveCurve(gen.ClockAlphaKeyFrameId);
            layer.ClockColorCurves = ResolveCurves(gen.ClockColorKeyFrameIds);
            layer.ClockPositionCurves = ResolveCurves(gen.ClockPositionKeyFrameIds);
            layer.ClockScaleCurves = ResolveCurves(gen.ClockScaleKeyFrameIds);

            KeyFrameCurve?[]? ResolveCurves(string?[] ids)
            {
                if (Array.TrueForAll(ids, string.IsNullOrEmpty)) return null;
                return Array.ConvertAll(ids, ResolveCurve);
            }

            KeyFrameCurve? ResolveCurve(string? id)
            {
                if (string.IsNullOrEmpty(id)) return null;
                KeyFrameCurve? curve = null;
                if (!string.IsNullOrEmpty(genWeather)) envData.KeyFrameCurves.TryGetValue($"{genWeather}/{id}", out curve);
                if (curve == null) envData.KeyFrameCurves.TryGetValue(id, out curve);
                return curve;
            }
        }

        private static bool IsSunMesh(string meshName, string datId) =>
            (ZoneDefDecoder.IsCelestialMesh(meshName) && meshName.StartsWith("sun", StringComparison.OrdinalIgnoreCase)) ||
            (ZoneDefDecoder.IsCelestialMesh(datId) && datId.StartsWith("sun", StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Adds a sky layer's geometry converted to display coordinates (-x, -y, z), matching world placements.
        /// </summary>
        private static void AddDisplayMeshGroups(WeatherSkyLayer layer, List<MeshGroup> submeshes, string fallbackTexture)
        {
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
                    minBounds = Vector3.Min(minBounds, displayPos);
                    maxBounds = Vector3.Max(maxBounds, displayPos);
                    dstVerts[v] = new MeshVertex(displayPos, new Vector3(-sv.Normal.X, -sv.Normal.Y, sv.Normal.Z), sv.TexCoord, sv.ColorRgba);
                }

                layer.MeshGroups.Add(new MeshGroup
                {
                    Name = submesh.Name,
                    TextureName = !string.IsNullOrWhiteSpace(submesh.TextureName) ? submesh.TextureName : fallbackTexture,
                    Vertices = dstVerts,
                    Indices = (int[])submesh.Indices.Clone(),
                    MinBounds = minBounds,
                    MaxBounds = maxBounds,
                    IsBlend = true,
                    NoCull = true
                });
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
            var particleMeshes = new Dictionary<string, List<MeshGroup>>(StringComparer.OrdinalIgnoreCase);
            var zoneRoutines = new List<(string? ParentDir, EffectRoutine Routine)>();
            var zoneMeshSections = new Dictionary<string, List<MeshGroup>>(StringComparer.OrdinalIgnoreCase);
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
                            string? currentWeather = ResolveCurrentWeather(dirStack);
                            if (!string.IsNullOrEmpty(currentWeather))
                            {
                                zoneMeshSections.TryAdd($"{currentWeather}/{header.DatId}", submeshes);
                            }
                            zoneMeshSections.TryAdd(header.DatId, submeshes);

                            if (ZoneDefDecoder.IsSkyMesh(primaryName) || ZoneDefDecoder.IsSkyMesh(header.DatId))
                            {
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
                            // Curve ids repeat across directories (umi2/uma1 vs umi5/uma1); generators resolve their own first.
                            if (dirStack.Count > 0) envData.AddKeyFrameCurve(DirectoryCurveKey(dirStack.Peek(), header.DatId), curve);
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

                    case DatSectionType.ParticleMesh:
                    {
                        var meshes = ParticleMeshDecoder.Decode(payload, header.DatId);
                        if (meshes != null && meshes.Count > 0)
                        {
                            string? weather = ResolveCurrentWeather(dirStack);
                            if (!string.IsNullOrEmpty(weather))
                            {
                                particleMeshes.TryAdd($"{weather}/{header.DatId}", meshes);
                            }
                            particleMeshes.TryAdd(header.DatId, meshes);
                        }
                        break;
                    }

                    case DatSectionType.EffectRoutine:
                    {
                        // Ambient zone routines (outside the weather directories) schedule their directory's generators.
                        if (!string.IsNullOrEmpty(ResolveCurrentWeather(dirStack))) break;
                        var routine = EffectRoutineDecoder.Decode(payload, header.DatId);
                        if (routine != null && routine.Spawns.Count > 0)
                        {
                            zoneRoutines.Add((dirStack.Count > 0 ? dirStack.Peek() : null, routine));
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

            // Sky painter's order: each generator's position among its weather directory's generators in the DAT.
            var authoredOrder = new Dictionary<ParticleGeneratorDefinition, int>(ReferenceEqualityComparer.Instance);
            var generatorsPerWeather = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var (_, orderWeather, _, orderGen) in generatorPlacements)
            {
                string weatherKey = orderWeather ?? string.Empty;
                generatorsPerWeather.TryGetValue(weatherKey, out int ordinal);
                authoredOrder[orderGen] = ordinal;
                generatorsPerWeather[weatherKey] = ordinal + 1;
            }

            // Resolve celestial sky meshes (stars, stardust, moon halo). Clouds and the sun are generator-driven below.
            var celestialByMesh = new Dictionary<string, WeatherSkyLayer>(StringComparer.OrdinalIgnoreCase);
            for (int s = 0; s < pendingSkyMeshes.Count; s++)
            {
                var (meshName, datId, weather, submeshes) = pendingSkyMeshes[s];
                bool isCelestial = ZoneDefDecoder.IsCelestialMesh(meshName) || ZoneDefDecoder.IsCelestialMesh(datId);
                if (!isCelestial || IsSunMesh(meshName, datId)) continue;

                // Star/moon/sun are duplicated under each weather directory that shows them (not every weather:
                // clod/mist author none); keep one layer and record every weather it belongs to.
                if (celestialByMesh.TryGetValue(meshName, out var existingCelestial))
                {
                    if (!string.IsNullOrEmpty(weather)) existingCelestial.WeatherIds.Add(weather);
                    continue;
                }
                string? authoredWeather = weather;
                weather = null;

                // Match the generator that draws this mesh: a generator names its geometry by LinkedDataId,
                // and its own DatId may coincide with an unrelated mesh (weat/*/star: generator 'star' draws
                // mesh 'sta1', generator 'sta1' draws mesh 'star').
                var (matchedGen, matchedGenWeather) = FindDrawingGenerator(generatorPlacements, datId, meshName, weather);
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
                else if (meshName.Contains("moon", StringComparison.OrdinalIgnoreCase) || datId.Contains("moon", StringComparison.OrdinalIgnoreCase) ||
                         meshName.Contains("kasa", StringComparison.OrdinalIgnoreCase) || datId.Contains("kasa", StringComparison.OrdinalIgnoreCase))
                {
                    attachType = ParticleAttachType.Moon;
                }

                // Untextured celestial meshes (e.g. the moon halo disc) are authentic and draw with a white texel.
                string textureName = submeshes.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s.TextureName))?.TextureName ?? string.Empty;
                Vector3 layerScale = matchedGen?.Scale ?? Vector3.One;
                Vector3 rawBasePos = matchedGen?.Setup?.BasePosition ?? Vector3.Zero;
                Vector3 displayBasePos = new Vector3(-rawBasePos.X, -rawBasePos.Y, rawBasePos.Z);

                var layer = new WeatherSkyLayer
                {
                    Name = meshName,
                    DatId = datId,
                    WeatherId = weather,
                    IsCelestial = true,
                    AttachType = attachType,
                    UVScroll = matchedGen?.UVScrollVelocity ?? Vector2.Zero,
                    Position = displayBasePos,
                    Scale = layerScale,
                    TextureName = textureName,
                    FollowCamera = matchedGen?.Setup?.FollowCamera ?? true,
                    FogEnabled = matchedGen?.Setup?.FogEnabled ?? false,
                    IsBlend = true,
                    NoCull = true
                };
                ApplyGeneratorRenderState(layer, matchedGen, matchedGenWeather, envData, authoredOrder);
                AddDisplayMeshGroups(layer, submeshes, string.Empty);
                if (!string.IsNullOrEmpty(authoredWeather)) layer.WeatherIds.Add(authoredWeather);
                celestialByMesh[meshName] = layer;

                zone.WeatherSkyLayers.Add(layer);
                envData.AddWeatherSkyLayer(layer);
            }

            // Sun shells: every Sun-attached generator declared directly in a weather directory that draws a 0x2E sun mesh
            // is its own layer (e.g. weat/fine: sun1 daytime glow, sun2 sunset disc, sun3 clock-scaled sunset corona).
            // Weathers author different sun sets (clod/mist draw a single large 'sun2' glow), so these are not merged.
            foreach (var (genId, genWeather, parentDir, gen) in generatorPlacements)
            {
                if (gen.AttachType != ParticleAttachType.Sun || string.IsNullOrEmpty(genWeather) ||
                    !string.Equals(parentDir, genWeather, StringComparison.OrdinalIgnoreCase)) continue;
                var setup = gen.Setup;
                if (setup == null || setup.LinkedDataType != ParticleLinkedDataType.StaticMesh) continue;

                if (!zoneMeshSections.TryGetValue($"{genWeather}/{setup.LinkedDataId}", out var sunMeshes) &&
                    !zoneMeshSections.TryGetValue(setup.LinkedDataId, out sunMeshes)) continue;
                string sunMeshName = sunMeshes[0].Name;
                if (!IsSunMesh(sunMeshName, setup.LinkedDataId)) continue;

                var sun = new WeatherSkyLayer
                {
                    Name = sunMeshName,
                    DatId = setup.LinkedDataId,
                    WeatherId = genWeather,
                    IsCelestial = true,
                    AttachType = ParticleAttachType.Sun,
                    Scale = gen.Scale,
                    TextureName = sunMeshes.FirstOrDefault(m => !string.IsNullOrWhiteSpace(m.TextureName))?.TextureName ?? string.Empty,
                    FollowCamera = true,
                    FogEnabled = false,
                    IsBlend = true,
                    NoCull = true
                };
                ApplyGeneratorRenderState(sun, gen, genWeather, envData, authoredOrder);
                sun.WeatherIds.Add(genWeather);
                AddDisplayMeshGroups(sun, sunMeshes, string.Empty);

                zone.WeatherSkyLayers.Add(sun);
                envData.AddWeatherSkyLayer(sun);
            }

            // Cloud shells: every camera-following generator declared directly in a weather directory that draws a
            // Section 0x2E cloud mesh becomes its own layer, so a mesh drawn by several generators (fine: cld1 + cld2
            // over 'cld_') composites as authored. Rain, lightning and smoke generators live alongside them but draw
            // non-sky meshes and are not cloud shells.
            foreach (var (genId, genWeather, parentDir, gen) in generatorPlacements)
            {
                if (string.IsNullOrEmpty(genWeather) || !string.Equals(parentDir, genWeather, StringComparison.OrdinalIgnoreCase)) continue;
                var setup = gen.Setup;
                if (setup == null || setup.LinkedDataType != ParticleLinkedDataType.StaticMesh || !setup.FollowCamera) continue;

                if (!zoneMeshSections.TryGetValue($"{genWeather}/{setup.LinkedDataId}", out var cloudMeshes) &&
                    !zoneMeshSections.TryGetValue(setup.LinkedDataId, out cloudMeshes)) continue;
                string cloudMeshName = cloudMeshes[0].Name;
                if (!ZoneDefDecoder.IsSkyMesh(cloudMeshName) || ZoneDefDecoder.IsCelestialMesh(cloudMeshName)) continue;

                Vector3 rawBase = setup.BasePosition;
                var cloud = new WeatherSkyLayer
                {
                    Name = cloudMeshName,
                    DatId = setup.LinkedDataId,
                    WeatherId = genWeather,
                    IsCelestial = false,
                    AttachType = gen.AttachType,
                    UVScroll = gen.UVScrollVelocity,
                    Position = new Vector3(-rawBase.X, -rawBase.Y, rawBase.Z),
                    Scale = gen.Scale,
                    TextureName = cloudMeshes.FirstOrDefault(m => !string.IsNullOrWhiteSpace(m.TextureName))?.TextureName ?? string.Empty,
                    FollowCamera = true,
                    FogEnabled = setup.FogEnabled,
                    IsBlend = true,
                    NoCull = true
                };
                ApplyGeneratorRenderState(cloud, gen, genWeather, envData, authoredOrder);
                cloud.WeatherIds.Add(genWeather);
                AddDisplayMeshGroups(cloud, cloudMeshes, string.Empty);

                zone.WeatherSkyLayers.Add(cloud);
                envData.AddWeatherSkyLayer(cloud);
            }

            // Celestial sprite sheets and lens flares: generators in a weather's star/moon directory that draw a
            // Section 0x21 sheet (the moon disc's twelve phases, the pole star), and sun/moon lens-flare sheets
            // (weat/*/lf01..lf03 for the sun, kas1 for the moon).
            // Sheets resolve from the weather directory, then the zone, then the shared ROM/0/0.DAT effects.
            var spriteLayersByGenerator = new Dictionary<string, WeatherSkyLayer>(StringComparer.OrdinalIgnoreCase);
            foreach (var (genId, genWeather, parentDir, gen) in generatorPlacements)
            {
                if (gen.Setup == null) continue;
                bool isFlare = gen.Setup.LinkedDataType == ParticleLinkedDataType.LensFlare;
                bool inCelestialDirectory = string.Equals(parentDir, "star", StringComparison.OrdinalIgnoreCase) ||
                                            string.Equals(parentDir, "moon", StringComparison.OrdinalIgnoreCase);
                if (isFlare
                    ? gen.AttachType is not (ParticleAttachType.Sun or ParticleAttachType.Moon) || string.IsNullOrEmpty(genWeather)
                    : gen.Setup.LinkedDataType != ParticleLinkedDataType.SpriteSheet || !inCelestialDirectory) continue;
                if (spriteLayersByGenerator.TryGetValue(genId, out var existingSprite))
                {
                    if (!string.IsNullOrEmpty(genWeather)) existingSprite.WeatherIds.Add(genWeather);
                    continue;
                }

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
                ApplyGeneratorRenderState(layer, gen, genWeather, envData, authoredOrder);
                if (!string.IsNullOrEmpty(genWeather)) layer.WeatherIds.Add(genWeather);
                spriteLayersByGenerator[genId] = layer;

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

            // Phase 2b: World effects from zone-anchored Section 0x05 generators drawing a Section 0x1F / 0x2E mesh.
            // A generator whose particle never expires (max life span 0: sea surfaces, sunset glints on the water) keeps
            // one static effect mesh, drawn with its generator's color, alpha, blend, UV scroll and lighting.
            // A generator outside the weather directories whose particles have a finite life (shoreline surf, wave
            // crests) becomes a particle emitter simulated by ZoneParticleEmitter when it auto-runs, or when a looping
            // ambient Section 0x07 routine in its directory starts it (e.g. Bibiki Bay's umi2/s000 rolls kwa1..kwa3 in).
            // Generator semantics referenced from xi-model-viewer (https://github.com/vekien/xi-model-viewer,
            // ui/js/particle/runtime.js, ui/js/particle/system.js registerZoneEffects and ops/initializers.js, after xim).
            var layerDirectories = new Dictionary<WeatherSkyLayer, string?>(ReferenceEqualityComparer.Instance);
            foreach (var (genId, genWeather, parentDir, gen) in generatorPlacements)
            {
                var setup = gen.Setup;
                if (setup == null) continue;
                bool isSprite = setup.LinkedDataType == ParticleLinkedDataType.SpriteSheet;
                if (setup.LinkedDataType != ParticleLinkedDataType.StaticMesh && !isSprite) continue;
                if (gen.AttachType != ParticleAttachType.None || setup.FollowCamera) continue;
                // Sprite-sheet particles always run through the emitter (billboarding and card selection are per particle).
                bool isEmitter = setup.MaxLifeSpan != 0 || isSprite;
                if (isEmitter && !string.IsNullOrEmpty(genWeather)) continue;

                // A non-auto-running generator only runs when a looping ambient routine in its directory starts it.
                List<EffectRoutineSpawn>? schedule = null;
                int scheduleLoop = 0;
                if (isEmitter && !gen.AutoRun)
                {
                    foreach (var (routineDir, routine) in zoneRoutines)
                    {
                        if (!string.Equals(routineDir, parentDir, StringComparison.OrdinalIgnoreCase)) continue;
                        foreach (var spawn in routine.Spawns)
                        {
                            if (!string.Equals(spawn.GeneratorId, genId, StringComparison.OrdinalIgnoreCase)) continue;
                            schedule ??= new List<EffectRoutineSpawn>();
                            schedule.Add(spawn);
                            scheduleLoop = routine.TotalFrames;
                        }
                    }
                    if (schedule == null) continue;
                }

                var effect = BuildEffectLayer(genId, genWeather, parentDir, gen, isEmitter, schedule, scheduleLoop, childOnly: false);
                if (effect == null) continue;
                zone.EffectLayers.Add(effect);
                layerDirectories[effect] = parentDir;
            }

            // Child generators (0x3C once at birth, 0x44 / 0x53 / 0x6A for the parent's life, expiration 0x01 on death):
            // each gets a child-only emitter layer that its parents spawn into, resolved from the parent's directory first.
            var childLayers = new Dictionary<ParticleGeneratorDefinition, WeatherSkyLayer>(ReferenceEqualityComparer.Instance);
            var pendingParents = new Queue<WeatherSkyLayer>(zone.EffectLayers.Where(l => l.Emitter != null));
            while (pendingParents.Count > 0)
            {
                var parentLayer = pendingParents.Dequeue();
                var parentTemplate = parentLayer.Emitter!;
                layerDirectories.TryGetValue(parentLayer, out string? parentLayerDir);
                foreach (string childId in ChildGeneratorIds(parentTemplate.Definition))
                {
                    if (parentTemplate.Children.ContainsKey(childId)) continue;

                    (string Id, string? Weather, string? ParentDir, ParticleGeneratorDefinition Generator)? match = null;
                    foreach (var placement in generatorPlacements)
                    {
                        if (!string.Equals(placement.DatId, childId, StringComparison.OrdinalIgnoreCase)) continue;
                        if (match == null || string.Equals(placement.ParentDir, parentLayerDir, StringComparison.OrdinalIgnoreCase)) match = placement;
                        if (string.Equals(placement.ParentDir, parentLayerDir, StringComparison.OrdinalIgnoreCase)) break;
                    }
                    if (match is not { } child) continue;

                    if (!childLayers.TryGetValue(child.Generator, out var childLayer))
                    {
                        var built = BuildEffectLayer(child.Id, child.Weather, child.ParentDir, child.Generator, isEmitter: true, null, 0, childOnly: true);
                        if (built == null) continue;
                        childLayer = built;
                        childLayers[child.Generator] = childLayer;
                        layerDirectories[childLayer] = child.ParentDir;
                        zone.EffectLayers.Add(childLayer);
                        pendingParents.Enqueue(childLayer);
                    }
                    parentTemplate.Children[childId] = childLayer.Emitter!;
                }
            }

            WeatherSkyLayer? BuildEffectLayer(
                string genId,
                string? genWeather,
                string? parentDir,
                ParticleGeneratorDefinition gen,
                bool isEmitter,
                List<EffectRoutineSpawn>? schedule,
                int scheduleLoop,
                bool childOnly)
            {
                var setup = gen.Setup;
                if (setup == null) return null;
                bool isSprite = setup.LinkedDataType == ParticleLinkedDataType.SpriteSheet;
                if (setup.LinkedDataType != ParticleLinkedDataType.StaticMesh && !isSprite) return null;

                string linkId = setup.LinkedDataId;
                if (string.IsNullOrWhiteSpace(linkId) || ZoneDefDecoder.IsSkyMesh(linkId)) return null;

                bool isParticleMesh = true;
                List<MeshGroup>? effectMeshes = null;
                if (isSprite)
                {
                    effectMeshes = ResolveSpriteCards(linkId, genWeather, spriteSheets, sharedEffects, outTextures);
                    if (effectMeshes == null) return null;
                }
                if (effectMeshes == null && !string.IsNullOrEmpty(genWeather)) particleMeshes.TryGetValue($"{genWeather}/{linkId}", out effectMeshes);
                if (effectMeshes == null) particleMeshes.TryGetValue(linkId, out effectMeshes);
                if (effectMeshes == null)
                {
                    isParticleMesh = false;
                    if (!string.IsNullOrEmpty(genWeather)) zoneMeshSections.TryGetValue($"{genWeather}/{linkId}", out effectMeshes);
                    if (effectMeshes == null) zoneMeshSections.TryGetValue(linkId, out effectMeshes);
                }
                if (effectMeshes == null || effectMeshes.Count == 0) return null;

                Vector3 rawBase = setup.BasePosition;
                var effect = new WeatherSkyLayer
                {
                    Name = genId,
                    DatId = linkId,
                    WeatherId = genWeather,
                    IsWorldEffect = true,
                    AttachType = ParticleAttachType.None,
                    UVScroll = gen.UVScrollVelocity,
                    Position = new Vector3(-rawBase.X, -rawBase.Y, rawBase.Z),
                    Scale = gen.Scale,
                    TextureName = effectMeshes.FirstOrDefault(m => !string.IsNullOrWhiteSpace(m.TextureName))?.TextureName ?? string.Empty,
                    FollowCamera = false,
                    FogEnabled = setup.FogEnabled,
                    LightingEnabled = setup.LightingEnabled,
                    DepthWrite = setup.DepthMask,
                    IsParticleMesh = isParticleMesh || isSprite,
                    FadeNear = gen.FadeNear,
                    FadeFar = gen.FadeFar,
                    IsBlend = true,
                    NoCull = true
                };
                ApplyGeneratorRenderState(effect, gen, genWeather, envData, authoredOrder);
                if (!string.IsNullOrEmpty(genWeather)) effect.WeatherIds.Add(genWeather);
                if (isSprite) effect.IsSpriteSheet = true;
                if (isEmitter || isSprite)
                {
                    effect.Emitter = new Gordian.Core.Graphics.ZoneEmitterTemplate(gen, ResolveEmitterCurves(gen, genWeather, parentDir, envData),
                        schedule, scheduleLoop, isSprite ? effectMeshes.Count : 0, childOnly);
                }
                AddDisplayMeshGroups(effect, effectMeshes, string.Empty);
                return effect;
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

        /// <summary>
        /// Resolves a generator's Section 2 keyframe links to Section 0x19 curves by allocation slot, preferring curves
        /// declared in the generator's own directory, then its weather directory, then any.
        /// </summary>
        private static Dictionary<ushort, KeyFrameCurve> ResolveEmitterCurves(ParticleGeneratorDefinition gen, string? genWeather, string? parentDir, ZoneEnvironmentData envData)
        {
            var curves = new Dictionary<ushort, KeyFrameCurve>();
            foreach (var (slot, link) in gen.KeyFrameLinks)
            {
                KeyFrameCurve? curve = null;
                if (!string.IsNullOrEmpty(parentDir)) envData.KeyFrameCurves.TryGetValue(DirectoryCurveKey(parentDir, link.CurveId), out curve);
                if (curve == null && !string.IsNullOrEmpty(genWeather)) envData.KeyFrameCurves.TryGetValue($"{genWeather}/{link.CurveId}", out curve);
                if (curve == null) envData.KeyFrameCurves.TryGetValue(link.CurveId, out curve);
                if (curve != null) curves[slot] = curve;
            }
            return curves;
        }

        private static string DirectoryCurveKey(string directory, string curveId) => $"dir:{directory}/{curveId}";

        /// <summary>
        /// DatIds of the child generators a generator's particles spawn: Section 2 opcodes 0x3C (once at birth) and
        /// 0x44 / 0x53 / 0x6A (for the particle's life), and Section 4 expiration handler 0x01 (on death).
        /// </summary>
        private static IEnumerable<string> ChildGeneratorIds(ParticleGeneratorDefinition gen)
        {
            foreach (var op in gen.Initializers)
            {
                if (op.OpCode is 0x3C or 0x44 or 0x53 or 0x6A)
                {
                    string id = op.Id(1);
                    if (id.Length > 0) yield return id;
                }
            }
            foreach (var op in gen.ExpirationOpcodes)
            {
                if (op.OpCode != 0x01) continue;
                string id = op.Id(1);
                if (id.Length > 0) yield return id;
            }
        }

        /// <summary>
        /// Resolves a Section 0x21 sprite sheet (weather directory, zone, then the shared ROM/0/0.DAT effects) into one
        /// raw-space triangle-list mesh per card, registering a shared sheet's texture with the zone's textures.
        /// </summary>
        private static List<MeshGroup>? ResolveSpriteCards(
            string linkId,
            string? genWeather,
            Dictionary<string, SpriteSheetMesh> spriteSheets,
            SharedEffectResources? sharedEffects,
            Dictionary<string, DecodedTexture>? outTextures)
        {
            SpriteSheetMesh? sheet = null;
            if (!string.IsNullOrEmpty(genWeather)) spriteSheets.TryGetValue($"{genWeather}/{linkId}", out sheet);
            if (sheet == null) spriteSheets.TryGetValue(linkId, out sheet);
            if (sheet == null && sharedEffects != null && sharedEffects.SpriteSheets.TryGetValue(linkId, out sheet) &&
                outTextures != null && sharedEffects.Textures.TryGetValue(sheet.TextureName, out var sharedTexture))
            {
                outTextures.TryAdd(sheet.TextureName, sharedTexture);
            }
            if (sheet == null || sheet.IsLensFlare || sheet.Cards.Count == 0) return null;

            var cards = new List<MeshGroup>(sheet.Cards.Count);
            for (int c = 0; c < sheet.Cards.Count; c++)
            {
                var card = sheet.Cards[c];
                var indices = new int[card.Length];
                for (int v = 0; v < card.Length; v++) indices[v] = v;
                cards.Add(new MeshGroup
                {
                    Name = $"{linkId}#{c}",
                    TextureName = sheet.TextureName,
                    Vertices = card,
                    Indices = indices,
                    IsBlend = true,
                    NoCull = true
                });
            }
            return cards;
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
