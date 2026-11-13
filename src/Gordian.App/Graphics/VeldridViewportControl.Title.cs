// src/Gordian.App/Graphics/VeldridViewportControl.Title.cs
using System;
using System.Numerics;
using Gordian.Core.Diagnostics;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Events;
using Gordian.Core.Resources.Graphics;
using Gordian.Engine.Graphics;
using Gordian.Engine.Ui.Lobby;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// The title fly-through behind the lobby (#552): the zones of <c>ROM/0/23.DAT</c> (<see cref="TitleSceneResource"/>),
    /// played by <see cref="TitleFlythrough"/>, loaded through the viewport's own zone loading and GPU zone cache and drawn
    /// by the zone renderer with the segment's cutscene presentation before the lobby's windows.
    /// </summary>
    public partial class VeldridViewportControl
    {
        private TitleFlythrough? _title;
        private bool _titleUnavailable;
        private readonly ViewportCamera _titleCamera = new();
        private int _titlePreloadedZone;
        private LobbyController? _titleLobby;
        private bool _titleSceneShown;

        /// <summary>What the lobby's environment was last set from (zone, weather, hour, environment).</summary>
        private (int Zone, string Weather, float Hour, string? Environment) _titleLighting;

        /// <summary>
        /// Whether the lobby screen shows the fly-through: every screen but the licence page (which keeps retail's own
        /// backdrop). Character creation and deletion show it too until retail's scene for them is known.
        /// </summary>
        internal static bool ShowsTitleScene(LobbyController lobby) =>
            !lobby.IsLicencePending && lobby.Screen is LobbyScreen.MainMenu or LobbyScreen.CharacterList or LobbyScreen.Creation or LobbyScreen.Entering;

        /// <summary>The fly-through, made on first use (null when the title file or zones cannot be read).</summary>
        private TitleFlythrough? TitleScene()
        {
            if (_title != null || _titleUnavailable) return _title;
            var rm = ResourceManager;
            if (rm == null) return null;
            try
            {
                var bytes = rm.LoadDatBytesByFileId(TitleSceneResource.FileId);
                var scene = bytes != null ? TitleSceneResource.Parse(bytes) : null;
                if (scene == null)
                {
                    _titleUnavailable = true;
                    GordianLog.Warning("Graphics", "Title fly-through: ROM/0/23.DAT could not be read; the lobby draws its plain backdrop.");
                    return null;
                }
                _title = new TitleFlythrough(scene, zone => rm.TryResolveFile(ZoneDataLoader.GetZoneModelFileId(zone), out _));
                if (_title.Segment == null) _titleUnavailable = true;
                GordianLog.Info("Graphics", $"Title fly-through: {scene.Segments.Count} segments, {scene.Groups.Count} choices; opening segment {_title.Segment?.Number} (zone {_title.ZoneId}).");
            }
            catch (Exception ex)
            {
                _titleUnavailable = true;
                GordianLog.Warning("Graphics", $"Title fly-through unavailable: {ex.Message}");
            }
            return _title;
        }

        /// <summary>
        /// Before a lobby frame: loads the segment's zone (and the next one in the background), advances the fly-through and
        /// lights the scene. Returns the fly-through when the lobby shows it this frame, else null; <paramref name="drawn"/>
        /// says whether its zone is on screen to draw (otherwise the screen behind the windows is black).
        /// </summary>
        private TitleFlythrough? UpdateTitleScene(LobbyController lobby, float deltaSeconds, out bool drawn)
        {
            drawn = false;
            var title = TitleScene();
            if (title == null || title.ZoneId == 0) return null;

            // A new visit to the title screen (after a Log Out) shows the next zone, as retail does.
            if (!ReferenceEquals(_titleLobby, lobby))
            {
                if (_titleLobby != null) title.Skip();
                _titleLobby = lobby;
            }

            // The licence page keeps its backdrop, but the opening zone loads behind it.
            bool shown = ShowsTitleScene(lobby);
            RequestTitleZones(title);
            bool zoneOnScreen = _loadedZoneId == title.ZoneId && _currentZoneGeom != null;
            if (!shown) return null;

            title.Update(deltaSeconds, zoneOnScreen);
            _titleSceneShown = true;
            drawn = zoneOnScreen && !title.IsWaitingForZone;
            if (drawn) LightTitleScene(title, deltaSeconds);
            return title;
        }

        /// <summary>Asks for the segment's zone and preloads the next segment's, once each.</summary>
        private void RequestTitleZones(TitleFlythrough title)
        {
            if (title.ZoneId != _loadedZoneId && _pendingZoneLoad != title.ZoneId) _pendingZoneLoad = title.ZoneId;
            // A lobby shows no session's zone: never wait for a session's fade before showing the title's.
            _ungatedZoneSwap = true;
            CheckAndLoadPendingZone(lobby: true);

            int next = title.NextZoneId;
            if (next != 0 && next != title.ZoneId && next != _titlePreloadedZone && ResourceManager is { } rm && _renderer is { } renderer)
            {
                _titlePreloadedZone = next;
                if (!renderer.SharedResources.Zones.PreloadInBackground(rm, next))
                {
                    GordianLog.Info("Graphics", $"Title fly-through: zone {next} not preloaded (zone cache budget); it loads when its segment starts.");
                }
            }
        }

        /// <summary>The segment's weather and hour on the zone's 0x2F environment, or the title file's own environment (op 0x7E).</summary>
        private void LightTitleScene(TitleFlythrough title, float deltaSeconds)
        {
            if (_renderer == null) return;
            var key = (title.ZoneId, title.Weather, title.Hour, title.Environment);
            bool zoneChanged = key.ZoneId != _titleLighting.Zone;
            if (zoneChanged) _weatherFade.Reset(title.Weather);
            bool fading = _weatherFade.Advance(title.Weather, deltaSeconds);
            if (!zoneChanged && !fading && key == _titleLighting) return;
            _titleLighting = key;

            EnvironmentKeyframe? keyframe = null;
            if (title.Environment is { } own && title.Scene.TryGetEnvironment(own, out var ownKeyframe)) keyframe = ownKeyframe;
            else if (_currentZoneGeom?.EnvironmentData is { } data) keyframe = _weatherFade.Resolve(data, title.Hour);
            if (keyframe == null) return;
            Environment.ApplyKeyframe(keyframe);
            Environment.SetTimeOfDay(title.Hour);
            Environment.WeatherId = title.Weather;
            _weatherFade.ApplyTo(Environment);
            _renderer.SkyDomeRenderer?.UpdateDome(Environment);
            // The session's own lighting is set again when it takes the view.
            _lastVanaHour = -1f;
        }

        /// <summary>Draws the fly-through's zone into the frame (render thread, under the render lock).</summary>
        private void RenderTitleZone(TitleFlythrough title, NeoVeldrid.Framebuffer framebuffer, uint width, uint height, float deltaSeconds)
        {
            if (_renderer == null) return;
            _renderer.World = null;
            _renderer.EventPresentation = title.Presentation;
            _renderer.LocalPlayerRidingPlatformId = string.Empty;
            _renderer.Furniture = null;
            _renderer.HideSceneryAbove = null;
            _renderer.AoeGuide = null;
            float aspect = Math.Max(0.1f, width / (float)Math.Max(1u, height));
            if (title.Presentation.TryGetCamera(out var pose)) PlaceTitleCamera(_titleCamera, pose, Environment, aspect);
            _renderer.Render(_titleCamera, Environment, deltaSeconds, width, height, entities: null, ResourceManager,
                present: false, targetFramebuffer: framebuffer);
        }

        /// <summary>
        /// Puts <paramref name="camera"/> on a fly-through shot as an event's cutscene camera is placed: the route's points
        /// (internal axes) mirrored in X and Y for the renderer, the far clip at the zone environment's draw distance.
        /// </summary>
        internal static void PlaceTitleCamera(ViewportCamera camera, CameraRoutePose pose, ZoneEnvironmentSettings environment, float aspect)
        {
            camera.FarClip = ZoneVisibility.GlobalDrawDistance(environment.DrawDistance, environment.DrawDistanceMultiplier);
            camera.SetEventView(new Vector3(-pose.Eye.X, -pose.Eye.Y, pose.Eye.Z), new Vector3(-pose.LookAt.X, -pose.LookAt.Y, pose.LookAt.Z),
                pose.FieldOfView, pose.Roll, aspect);
        }

        /// <summary>
        /// The first frame without the lobby after the fly-through showed: the session's zone and lighting are asked for
        /// again, as the lobby's zone replaced them.
        /// </summary>
        private void LeaveTitleScene()
        {
            if (!_titleSceneShown) return;
            _titleSceneShown = false;
            _titleLighting = default;
            _lastVanaHour = -1f;
            if ((_activeSession?.World ?? WorldState) is { } world)
            {
                ushort zone = world.SceneZoneId != 0 ? world.SceneZoneId : world.MapZoneId;
                if (zone != 0 && zone != _loadedZoneId) _pendingZoneLoad = zone;
                _weatherFade.Reset(world.WeatherId ?? "fine");
            }
        }
    }
}
