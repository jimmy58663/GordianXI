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

        private readonly ViewportCamera _stageCamera = new();
        private bool _stagePreloaded;

        /// <summary>
        /// Whether the lobby screen shows the fly-through: the title menu (the licence page keeps retail's own backdrop;
        /// character select, deletion and creation show the stage, <see cref="LobbyStage"/>).
        /// </summary>
        internal static bool ShowsTitleScene(LobbyController lobby) =>
            !lobby.IsLicencePending && lobby.Screen == LobbyScreen.MainMenu;

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
                    GordianLog.Warn(LogCategories.Graphics, "Title fly-through: ROM/0/23.DAT could not be read; the lobby draws its plain backdrop.");
                    return null;
                }
                _title = new TitleFlythrough(scene, zone => rm.TryResolveFile(ZoneDataLoader.GetZoneModelFileId(zone), out _));
                if (_title.Segment == null) _titleUnavailable = true;
                GordianLog.Info(LogCategories.Graphics, $"Title fly-through: {scene.Segments.Count} segments, {scene.Groups.Count} choices; opening segment {_title.Segment?.Number} (zone {_title.ZoneId}).");
            }
            catch (Exception ex)
            {
                _titleUnavailable = true;
                GordianLog.Warn(LogCategories.Graphics, $"Title fly-through unavailable: {ex.Message}");
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
            PreloadStage();
            bool zoneOnScreen = _loadedZoneId == title.ZoneId && _currentZoneGeom != null;
            if (!shown) return null;

            title.Update(deltaSeconds, zoneOnScreen);
            _titleSceneShown = true;
            drawn = zoneOnScreen && !title.IsWaitingForZone;
            if (drawn)
            {
                EnvironmentKeyframe? own = title.Environment is { } name && title.Scene.TryGetEnvironment(name, out var keyframe) ? keyframe : null;
                LightLobbyScene(title.ZoneId, title.Weather, title.Hour, title.Environment, own, deltaSeconds);
            }
            return title;
        }

        /// <summary>
        /// Before a lobby frame on character select, deletion or creation: loads the stage (<see cref="LobbyStage"/>) and
        /// lights it at the screen's hour. Returns whether it is on screen to draw.
        /// </summary>
        private bool UpdateStageScene(LobbyController lobby, float deltaSeconds)
        {
            _pendingZoneLoad = LobbyStage.ZoneId != _loadedZoneId ? LobbyStage.ZoneId : 0;
            _ungatedZoneSwap = true;
            CheckAndLoadPendingZone(lobby: true);
            // The title's zone loads meanwhile, for the way back.
            if (TitleScene() is { ZoneId: > 0 } title && title.ZoneId != _titlePreloadedZone && ResourceManager is { } rm && _renderer is { } renderer)
            {
                _titlePreloadedZone = title.ZoneId;
                renderer.SharedResources.Zones.PreloadInBackground(rm, title.ZoneId);
            }
            if (_loadedZoneId != LobbyStage.ZoneId || _currentZoneGeom == null) return false;
            _titleSceneShown = true;
            LightLobbyScene(LobbyStage.ZoneId, LobbyStage.Weather, LobbyStage.HourOf(lobby), null, null, deltaSeconds);
            return true;
        }

        /// <summary>Loads the stage in the background once, so the title menu's buttons open it without a wait.</summary>
        private void PreloadStage()
        {
            if (_stagePreloaded || ResourceManager is not { } rm || _renderer is not { } renderer) return;
            _stagePreloaded = true;
            if (!rm.TryResolveFile(ZoneDataLoader.GetZoneModelFileId(LobbyStage.ZoneId), out _))
            {
                GordianLog.Warn(LogCategories.Graphics, $"Lobby stage: zone {LobbyStage.ZoneId} (ROM/1/5.DAT) is not installed; character select shows a black backdrop.");
                return;
            }
            renderer.SharedResources.Zones.PreloadInBackground(rm, LobbyStage.ZoneId);
        }

        /// <summary>Draws the stage into the frame behind the preview character (render thread, under the render lock).</summary>
        private void RenderStageZone(LobbyController lobby, NeoVeldrid.Framebuffer framebuffer, uint width, uint height, float deltaSeconds)
        {
            if (_renderer == null) return;
            _renderer.World = null;
            _renderer.EventPresentation = null;
            _renderer.LocalPlayerRidingPlatformId = string.Empty;
            _renderer.Furniture = null;
            _renderer.HideSceneryAbove = null;
            _renderer.AoeGuide = null;
            PlaceStageCamera(_stageCamera, lobby, Environment, width, height);
            _renderer.Render(_stageCamera, Environment, deltaSeconds, width, height, entities: null, ResourceManager,
                present: false, targetFramebuffer: framebuffer);
        }

        /// <summary>
        /// The stage camera: the lobby preview's camera (<see cref="StockUiLobby.PreviewCamera"/>, which frames the model in
        /// its window) turned and moved onto <see cref="LobbyStage.Spot"/>, so the model drawn over the scene stands on it.
        /// </summary>
        internal static void PlaceStageCamera(ViewportCamera camera, LobbyController lobby, ZoneEnvironmentSettings environment, uint width, uint height)
        {
            var rect = StockUiLobby.Fit(width, height);
            var (centerX, feetY, topY) = StockUiLobby.PreviewArea(rect, lobby.Screen == LobbyScreen.Creation && !lobby.IsRenaming);
            var (eye, target, fov) = StockUiLobby.PreviewCamera(centerX, feetY, topY, width, height);
            var (stageEye, stageTarget) = LobbyStage.Place(eye, target);
            camera.FarClip = ZoneVisibility.GlobalDrawDistance(environment.DrawDistance, environment.DrawDistanceMultiplier);
            camera.SetEventView(stageEye, stageTarget, fov, 0f, width / (float)Math.Max(1u, height));
        }

        /// <summary>Asks for the segment's zone and preloads the next segment's, once each.</summary>
        private void RequestTitleZones(TitleFlythrough title)
        {
            // Only the fly-through's zone loads while the lobby is up (a character under it asks again when it leaves).
            _pendingZoneLoad = title.ZoneId != _loadedZoneId ? title.ZoneId : 0;
            // A lobby shows no session's zone: never wait for a session's fade before showing the title's.
            _ungatedZoneSwap = true;
            CheckAndLoadPendingZone(lobby: true);

            int next = title.NextZoneId;
            if (next != 0 && next != title.ZoneId && next != _titlePreloadedZone && ResourceManager is { } rm && _renderer is { } renderer)
            {
                _titlePreloadedZone = next;
                if (!renderer.SharedResources.Zones.PreloadInBackground(rm, next))
                {
                    GordianLog.Info(LogCategories.Graphics, $"Title fly-through: zone {next} not preloaded (already loading, or over the zone cache budget); it loads when its segment starts if need be.");
                }
            }
        }

        /// <summary>
        /// The lobby scene's weather and hour on the zone's 0x2F environment, or <paramref name="own"/> (the title file's own
        /// environment, op 0x7E) when given.
        /// </summary>
        private void LightLobbyScene(int zoneId, string weather, float hour, string? environmentName, EnvironmentKeyframe? own, float deltaSeconds)
        {
            if (_renderer == null) return;
            var key = (zoneId, weather, hour, environmentName);
            bool zoneChanged = zoneId != _titleLighting.Zone;
            if (zoneChanged) _weatherFade.Reset(weather);
            bool fading = _weatherFade.Advance(weather, deltaSeconds);
            if (!zoneChanged && !fading && key == _titleLighting) return;
            _titleLighting = key;

            EnvironmentKeyframe? keyframe = own;
            if (keyframe == null && _currentZoneGeom?.EnvironmentData is { } data) keyframe = _weatherFade.Resolve(data, hour);
            if (keyframe == null) return;
            Environment.ApplyKeyframe(keyframe);
            Environment.SetTimeOfDay(hour);
            Environment.WeatherId = weather;
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
