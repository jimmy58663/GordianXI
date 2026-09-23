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

        public override string ToString() =>
            $"WeatherSkyLayer [{Name}] Weather: '{WeatherId ?? "Universal"}' Celestial: {IsCelestial} (Attach: {AttachType}, UVScroll: {UVScroll})";
    }
}
