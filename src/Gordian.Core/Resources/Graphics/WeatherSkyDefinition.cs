// src/Gordian.Core/Resources/Graphics/WeatherSkyDefinition.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using Gordian.Core.Resources.Models;

namespace Gordian.Core.Resources.Graphics
{
    /// <summary>
    /// Represents an attachment target for a particle generator or celestial body.
    /// Derived from community specifications in xi-model-viewer (https://github.com/vekien/xi-model-viewer)
    /// and xi-tools (xim AttachType).
    /// </summary>
    public enum ParticleAttachType : byte
    {
        None = 0x0,
        SourceActor = 0x1,
        TargetActor = 0x2,
        SourceToTargetBasis = 0x3,
        TargetActorSourceFacing = 0x4,
        SourceActorTargetFacing = 0x5,
        TargetToSourceBasis = 0x6,
        SourceActorWeapon = 0x9,
        ZoneActor0xA = 0xA,
        ZoneActor0xB = 0xB,
        ZoneActor0xC = 0xC,
        Sun = 0xE,
        Moon = 0xF
    }

    /// <summary>
    /// Represents a decoded weather sky layer (cloud shell, drifting weather plane, or celestial body disc).
    /// Used by the 3D viewport renderer to composite dynamic cloud layers and celestial discs over the sky dome.
    /// Protocol specification referenced from xi-model-viewer (https://github.com/vekien/xi-model-viewer)
    /// and xi-tools (xim ZoneDrawer / ParticleDrawer).
    /// </summary>
    public sealed class WeatherSkyLayer
    {
        /// <summary>
        /// Mesh or generator identifier name.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// DAT section DatId (4 characters).
        /// </summary>
        public string DatId { get; set; } = string.Empty;

        /// <summary>
        /// Weather ID under which this sky layer was declared (e.g. "fine", "suny", "clod", "mist", "rain"),
        /// or null if this is a weather-agnostic celestial body (Sun, Moon, Stars).
        /// </summary>
        public string? WeatherId { get; set; }

        /// <summary>
        /// Every weather directory this layer is authored under (celestial bodies are duplicated per weather that shows
        /// them, e.g. only `fine`/`suny` carry `star` and `moon`); empty means the layer is not weather-scoped.
        /// </summary>
        public HashSet<string> WeatherIds { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// True if this layer represents a celestial body (Sun, Moon, Stars, celestial sphere)
        /// rather than a cloud layer or weather precipitation plane.
        /// </summary>
        public bool IsCelestial { get; set; }

        /// <summary>
        /// Generator attachment type (e.g. Sun, Moon, or None).
        /// </summary>
        public ParticleAttachType AttachType { get; set; } = ParticleAttachType.None;

        /// <summary>
        /// Continuous horizontal (U) and vertical (V) drift velocity for texture coordinates.
        /// Extracted from Section 0x05 Section 3 TextureCoordinateUpdater opcodes (0x27 / 0x28).
        /// </summary>
        public Vector2 UVScroll { get; set; } = Vector2.Zero;

        /// <summary>
        /// Base position offset or authored world coordinates.
        /// </summary>
        public Vector3 Position { get; set; } = Vector3.Zero;

        /// <summary>
        /// 3D scale vector applied to this sky layer (default 1, 1, 1).
        /// </summary>
        public Vector3 Scale { get; set; } = Vector3.One;

        /// <summary>
        /// Decoded 3D mesh geometry groups for this sky layer (from Section 0x2E ZoneMesh or 0x1F ParticleMesh).
        /// </summary>
        public List<MeshGroup> MeshGroups { get; } = new();

        /// <summary>
        /// Primary texture name associated with this sky layer.
        /// </summary>
        public string TextureName { get; set; } = string.Empty;

        /// <summary>
        /// True if the geometry follows / centers on the camera eye position (standard for sky shells and cloud domes).
        /// </summary>
        public bool FollowCamera { get; set; } = true;

        /// <summary>
        /// True if atmospheric distance fog affects this layer.
        /// Celestial bodies (Sun, Moon, Stars) set this to false so they glow through atmospheric fog.
        /// </summary>
        public bool FogEnabled { get; set; } = true;

        /// <summary>
        /// True if this layer uses alpha blending.
        /// </summary>
        public bool IsBlend { get; set; } = true;

        /// <summary>
        /// True if backface culling should be disabled for this layer.
        /// </summary>
        public bool NoCull { get; set; } = false;

        /// <summary>
        /// DatId of the Section 0x05 generator that draws this layer, if one was matched.
        /// </summary>
        public string? GeneratorId { get; set; }

        /// <summary>
        /// Generator initial rotation in radians, raw DAT axes.
        /// </summary>
        public Vector3 Rotation { get; set; } = Vector3.Zero;

        /// <summary>
        /// Generator base color (Section 2 Opcode 0x16), half-range: 0.5 (0x80) is neutral.
        /// </summary>
        public Vector4 BaseColor { get; set; } = Vector4.One;

        /// <summary>
        /// Generator blend function (Section 2 Opcode 0x1E).
        /// </summary>
        public ParticleBlendFunc BlendFunc { get; set; } = ParticleBlendFunc.SrcOneAdd;

        /// <summary>
        /// Rotation velocity in radians per 60 Hz frame, raw DAT axes.
        /// </summary>
        public Vector3 RotationVelocity { get; set; } = Vector3.Zero;

        /// <summary>
        /// Time-of-day curves that set the color's R, G and B (null entries keep the base color channel), or null.
        /// </summary>
        public KeyFrameCurve?[]? ClockColorCurves { get; set; }

        /// <summary>
        /// Time-of-day curves for the X, Y and Z position offset in raw DAT axes (null entries are zero), or null.
        /// </summary>
        public KeyFrameCurve?[]? ClockPositionCurves { get; set; }

        /// <summary>
        /// Time-of-day curves that replace the X, Y and Z scale in raw DAT axes (null entries keep <see cref="Scale"/>), or null.
        /// </summary>
        public KeyFrameCurve?[]? ClockScaleCurves { get; set; }

        /// <summary>
        /// Painter's order: the drawing generator's position among its weather directory's generators in the DAT.
        /// The client draws sky generators in authored order (e.g. every weather authors its daytime sun glow before
        /// its clouds, so the sun shines through them), not by the generator's projection-bias weight.
        /// </summary>
        public int AuthoredOrder { get; set; } = int.MaxValue;

        /// <summary>
        /// Time-of-day alpha curve (Section 0x19) sampled over the 24-hour Vana'diel clock, or null.
        /// </summary>
        public KeyFrameCurve? ClockAlphaCurve { get; set; }

        /// <summary>
        /// Per-weekday modulate-2x tints (8 entries), or null.
        /// </summary>
        public Vector4[]? DayOfWeekColors { get; set; }

        /// <summary>
        /// Per-moon-phase modulate-2x tints (12 entries), or null.
        /// </summary>
        public Vector4[]? MoonPhaseColors { get; set; }

        /// <summary>
        /// True if this layer is a Section 0x21 sprite sheet whose MeshGroups are camera-facing cards,
        /// of which one is drawn (card 0, or the moon phase when <see cref="IsMoonPhaseSpriteSheet"/>).
        /// </summary>
        public bool IsSpriteSheet { get; set; }

        /// <summary>
        /// True if the drawn sprite-sheet card is selected by the current moon phase.
        /// </summary>
        public bool IsMoonPhaseSpriteSheet { get; set; }

        /// <summary>
        /// True if this layer is a screen-space lens flare: every card is drawn, positioned along the line from the
        /// light source's screen position through the screen centre by <see cref="FlareOffsets"/>.
        /// </summary>
        public bool IsLensFlare { get; set; }

        /// <summary>
        /// Per-card lens-flare offsets (0 = on the source, 0.5 = screen centre, 1 = opposite side).
        /// </summary>
        public IReadOnlyList<float> FlareOffsets { get; set; } = Array.Empty<float>();

        /// <summary>
        /// True if this layer is a world-space zone effect (e.g. a sea surface) rather than a sky layer: it is drawn
        /// with real depth among the zone's translucent geometry instead of at the far plane.
        /// </summary>
        public bool IsWorldEffect { get; set; }

        /// <summary>
        /// Generator lighting flag (Section 2 Opcode 0x01): vertex colors are lit by the zone's ambient, sun and moon.
        /// </summary>
        public bool LightingEnabled { get; set; }

        /// <summary>
        /// The texture's alpha channel is treated as opaque, so only the vertex alpha and the texture factor set the
        /// layer's coverage: generator render-state bit 0x1000 (Section 2 Opcode 0x01), or an alpha override
        /// (Opcode 0x1E) on a zone generator. Bibiki Bay's cave-mouth gradients (<c>ent1</c>-<c>ent4</c>) need it:
        /// their rock atlas carries a blocky alpha mask for decal sub-meshes that would otherwise cut the light-to-dark
        /// gradient into tiles. Referenced from xi-model-viewer (https://github.com/vekien/xi-model-viewer,
        /// ui/js/particle/ops/initializers.js and ui/js/particleDrawer.js, after xim).
        /// </summary>
        public bool IgnoreTextureAlpha { get; set; }

        /// <summary>
        /// Fixed texture-factor alpha (0-255) from the generator's blend opcode (Section 2 Opcode 0x1E, bit 0x20),
        /// replacing the authored colour's alpha; null when the generator has none.
        /// </summary>
        public byte? AlphaOverride { get; set; }

        /// <summary>
        /// Generator depth-mask flag (Section 2 Opcode 0x01): the layer writes depth.
        /// </summary>
        public bool DepthWrite { get; set; }

        /// <summary>
        /// True if the geometry is a Section 0x1F particle mesh. Alpha-blended particle meshes whose texture-factor alpha
        /// reaches 127/255 draw fully opaque (the client's behavior on Bibiki Bay's ocean).
        /// </summary>
        public bool IsParticleMesh { get; set; }

        /// <summary>
        /// Camera-distance alpha fade from the generator (Section 3 Opcode 0x2E): 1 within FadeNear, 0 beyond FadeFar.
        /// Both zero when the generator has no fade.
        /// </summary>
        public float FadeNear { get; set; }

        /// <inheritdoc cref="FadeNear"/>
        public float FadeFar { get; set; }

        /// <summary>
        /// For a finite-life particle emitter (e.g. shoreline surf), the generator to simulate; its particles supply
        /// position, orientation, scale, color and UV per frame. Null for static layers.
        /// </summary>
        public Gordian.Core.Graphics.ZoneEmitterTemplate? Emitter { get; set; }

        /// <summary>
        /// For a point-light generator (linked type 0x47): its zero-based slot in the ZoneDef light table, whose placements
        /// it lights. -1 for every other layer.
        /// </summary>
        public int PointLightSlot { get; set; } = -1;

        public override string ToString() =>
            $"WeatherSkyLayer [{Name}] Weather: '{WeatherId ?? "Universal"}' Celestial: {IsCelestial} (Attach: {AttachType}, UVScroll: {UVScroll})";
    }
}
