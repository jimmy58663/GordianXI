// src/Gordian.App/Graphics/ZoneTerrainRenderer.Visibility.cs
using System;
using System.Numerics;
using Gordian.Engine.Graphics;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// Zone visibility (#70): the culling table of the camera's floor, the global draw distance and each placement's own
    /// distance and detail level, applied before frustum culling (<see cref="ZoneVisibility"/>).
    /// </summary>
    public sealed partial class ZoneTerrainRenderer
    {
        /// <summary>
        /// Leaves out scenery wholly above this display height: the Mog House Layout looks down through the room's ceiling
        /// (#93; San d'Oria's <c>m_san01_b</c> is 5-7.4 yalms up, its lamp starts at 4.6 and stays, as in the retail video).
        /// </summary>
        public float? HideSceneryAbove { get; set; }

        /// <summary>
        /// Culling tables, draw distances and detail levels on (default); off draws every placement's high-detail variant
        /// to the frustum, as before #70 (for comparisons and the offscreen harness).
        /// </summary>
        public bool EnableZoneVisibility { get; set; } = true;

        /// <summary>The camera floor's culling table hides what it does not list (default); off keeps the distance and detail rules only.</summary>
        public bool EnableCullingTables { get; set; } = true;

        /// <summary>The culling table the camera's floor picked last frame; -1 for none (everything draws).</summary>
        public int CullingTable => _cullingTable;

        /// <summary>The global draw distance used last frame (yalms): the far clip the viewport should use.</summary>
        public float GlobalDrawDistance => _globalDrawDistance;

        /// <summary>Placed submeshes the culling table, a draw distance or the detail level hid last frame.</summary>
        public int VisibilityCulledMeshes { get; private set; }

        private int _cullingTable = -1;
        private float _globalDrawDistance = float.MaxValue;
        private Vector3 _visibilityEye;
        private Vector3 _sceneryVisibilityEye;
        private Vector3 _cullingTableProbe = new(float.NaN);
        private int _visibilityCulled;
        private float _drawDistanceMultiplier = ZoneVisibility.DefaultSmallDrawDistanceMultiplier;
        private float _lodDistanceScale = ZoneVisibility.DefaultLodDistanceScale;
        private float _pixelsPerYalmAtOne;

        /// <summary>
        /// Skip placed submeshes whose bounds would cover less than <see cref="ZoneDrawPolicy.SubPixelRadius"/> pixels on
        /// screen (#606 round 4; every preset turns it on). Off draws them, however small.
        /// </summary>
        public bool SubPixelCull { get; set; } = true;

        /// <summary>
        /// Yalms from the camera within which swaying meshes move (#606 round 4); farther ones hold their authored pose.
        /// Infinity (retail) sways everything drawn.
        /// </summary>
        public float SwayRange { get; set; } = float.PositiveInfinity;

        /// <summary>Placed submeshes the sub-pixel cull skipped last frame (part of <see cref="VisibilityCulledMeshes"/>).</summary>
        public int SubPixelCulledMeshes { get; private set; }

        private int _subPixelCulled;

        /// <summary>
        /// Works out this frame's camera floor table and global draw distance. <paramref name="eye"/> is the camera eye in
        /// display space; placements are tested in the scenery's own frame, which a voyage moves.
        /// </summary>
        private void UpdateZoneVisibility(Vector3 eye, ZoneEnvironmentSettings environment, float fieldOfView, uint viewportHeight)
        {
            _visibilityCulled = 0;
            _subPixelCulled = 0;
            // A sphere of radius r at distance d covers r * this / d pixels (the vertical field of view across the viewport).
            _pixelsPerYalmAtOne = viewportHeight / (2.0f * MathF.Tan(fieldOfView * 0.5f));
            _visibilityEye = eye;
            _sceneryVisibilityEye = !_sceneryTransform.IsIdentity && Matrix4x4.Invert(_sceneryTransform, out var toScenery)
                ? Vector3.Transform(eye, toScenery)
                : eye;
            _globalDrawDistance = ZoneVisibility.GlobalDrawDistance(environment.DrawDistance, environment.DrawDistanceMultiplier);
            _drawDistanceMultiplier = environment.SmallDrawDistanceMultiplier;
            _lodDistanceScale = environment.LodDistanceScale;

            var zone = LoadedZone;
            if (zone?.CullingTables == null || zone.Collision == null)
            {
                _cullingTable = -1;
                return;
            }
            // The floor only changes when the camera moves: probe again after half a yalm.
            if (Vector3.DistanceSquared(_sceneryVisibilityEye, _cullingTableProbe) < 0.25f) return;
            _cullingTableProbe = _sceneryVisibilityEye;
            // Display space is (-x, -y, z) of the collision's internal space.
            var internalEye = new Vector3(-_sceneryVisibilityEye.X, -_sceneryVisibilityEye.Y, _sceneryVisibilityEye.Z);
            _cullingTable = zone.Collision.CullingTableUnder(internalEye, ZoneVisibility.CameraFloorReach);
        }

        /// <summary>
        /// Whether a placed submesh survives the culling table, draw distance and detail level this frame; counts it when
        /// not. <paramref name="sceneryFrame"/> is false for geometry in the passengers' frame (a voyage's vessel doors).
        /// </summary>
        private bool PassesZoneVisibility(GpuSubmesh submesh, bool sceneryFrame = true)
        {
            if (HideSceneryAbove is { } ceiling && submesh.MinBounds.Y >= ceiling) return false;
            if (submesh.Visibility.IsUnplaced) return true;
            if (!EnableZoneVisibility) return (submesh.Visibility.Slots & Gordian.Core.Resources.Models.LodSlots.High) != 0;
            var tables = EnableCullingTables ? LoadedZone?.CullingTables : null;
            var eye = sceneryFrame ? _sceneryVisibilityEye : _visibilityEye;
            if (!ZoneVisibility.IsVisible(submesh.Visibility, eye, tables, _cullingTable, _globalDrawDistance, _drawDistanceMultiplier, _lodDistanceScale))
            {
                _visibilityCulled++;
                return false;
            }
            if (SubPixelCull && IsSubPixel(submesh, eye))
            {
                _visibilityCulled++;
                _subPixelCulled++;
                return false;
            }
            return true;
        }

        /// <summary>Whether the submesh's bounding sphere, seen from <paramref name="eye"/>, fits inside a pixel.</summary>
        private bool IsSubPixel(GpuSubmesh submesh, Vector3 eye)
        {
            var center = (submesh.MinBounds + submesh.MaxBounds) * 0.5f;
            float radius = (submesh.MaxBounds - submesh.MinBounds).Length() * 0.5f;
            float distance = Vector3.Distance(eye, center);
            if (distance <= radius) return false;
            return radius * _pixelsPerYalmAtOne < ZoneDrawPolicy.SubPixelRadius * (distance - radius);
        }

        /// <summary>Whether a swaying submesh is within <see cref="SwayRange"/> of the camera (its placement origin).</summary>
        private bool WithinSwayRange(GpuSubmesh submesh)
        {
            if (float.IsPositiveInfinity(SwayRange)) return true;
            var at = submesh.Visibility.IsUnplaced ? (submesh.MinBounds + submesh.MaxBounds) * 0.5f : submesh.Visibility.Origin;
            return Vector3.DistanceSquared(_sceneryVisibilityEye, at) <= SwayRange * SwayRange;
        }

        private void ResetZoneVisibility() => _cullingTableProbe = new Vector3(float.NaN);
    }
}
