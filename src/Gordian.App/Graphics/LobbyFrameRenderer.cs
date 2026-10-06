// src/Gordian.App/Graphics/LobbyFrameRenderer.cs
using System;
using System.Diagnostics;
using System.Numerics;
using Gordian.Core.Graphics;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Ui;
using Gordian.Core.Ui.Lobby;
using NeoVeldrid;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// One frame of the character lobby: a black clear, the lobby backgrounds (<see cref="StockUiLobby.DrawBackground"/>),
    /// the preview character through the entity renderer with a fixed camera (<see cref="StockUiLobby.PreviewCamera"/>),
    /// then the lobby windows (<see cref="StockUiLobby.DrawForeground"/>). The two UI passes use separate renderers so the
    /// second does not overwrite the first's vertex buffer while it is in flight.
    /// </summary>
    public sealed class LobbyFrameRenderer : IDisposable
    {
        private readonly GraphicsDevice _gd;
        private readonly StockUiRenderer _background;
        private readonly StockUiRenderer _foreground;
        private readonly CommandList _commands;
        private readonly ViewportCamera _camera = new() { FarClip = 200f, NearClip = 0.1f };
        private readonly ZoneEnvironmentSettings _environment = ZoneEnvironmentSettings.CreateDay();
        private UiResourceLibrary? _fontLibrary;
        private UiFont? _font;
        private StockUiLogFont? _logFont;
        private bool _disposed;

        public LobbyFrameRenderer(GraphicsDevice gd, OutputDescription outputs)
        {
            _gd = gd ?? throw new ArgumentNullException(nameof(gd));
            _background = new StockUiRenderer(gd, outputs);
            _foreground = new StockUiRenderer(gd, outputs);
            _commands = gd.ResourceFactory.CreateCommandList();
            _environment.FogEnabled = false;
        }

        /// <summary>The preview's camera as last placed (for tests).</summary>
        public ViewportCamera Camera => _camera;

        /// <summary>
        /// Draws one lobby frame. <paramref name="brightness"/> below 1 darkens the finished frame toward black (0 = black):
        /// the fade in when the lobby appears.
        /// </summary>
        public void Render(LobbyController lobby, LobbyPreview preview, EntityRenderer? entities, ResourceManager? resources,
            Framebuffer framebuffer, uint width, uint height, float deltaSeconds, float brightness = 1f)
        {
            if (_disposed || width == 0 || height == 0) return;
            var library = lobby.Library;
            if (!ReferenceEquals(library, _fontLibrary))
            {
                _fontLibrary = library;
                _font = library != null ? UiFont.FromLibrary(library) : null;
                _logFont = library != null ? StockUiLogFont.FromLibrary(library) : null;
            }

            bool creating = lobby.Screen == LobbyScreen.Creation;
            if (creating && !lobby.IsRenaming) preview.Show(lobby.PreviewCreation);
            else preview.Show(lobby.PreviewCharacter ?? lobby.RenamingCharacter);

            _commands.Begin();
            _commands.SetFramebuffer(framebuffer);
            _commands.ClearColorTarget(0, RgbaFloat.Black);
            _commands.ClearDepthStencil(1f);
            _commands.End();
            _gd.SubmitCommands(_commands);

            if (library != null)
            {
                _background.Begin(library);
                StockUiLobby.DrawBackground(_background, lobby, width, height);
                _background.End(framebuffer, width, height);
            }

            if (entities != null && preview.Entity != null)
            {
                var rect = StockUiLobby.Fit(width, height);
                var (centerX, feetY, topY) = StockUiLobby.PreviewArea(rect, creating && !lobby.IsRenaming);
                var (eye, target, fov) = StockUiLobby.PreviewCamera(centerX, feetY, topY, width, height);
                _camera.SetEventView(eye, target, fov, 0f, width / (float)height);
                _commands.Begin();
                _commands.SetFramebuffer(framebuffer);
                entities.RenderEntities(_commands, _camera, _environment, preview.World.Entities, resources, deltaSeconds, LobbyPreview.EntityId);
                _commands.End();
                _gd.SubmitCommands(_commands);
            }

            if (library != null)
            {
                _foreground.Begin(library);
                long now = Stopwatch.GetTimestamp();
                StockUiLobby.DrawForeground(_foreground, lobby, _font, width, height, now, _logFont);
                if (lobby.Screen == LobbyScreen.Entering)
                {
                    // A character was chosen: the lobby fades to black, and the session's zone-in stays black (#36).
                    double seconds = (now - lobby.EnteredTimestamp) / (double)Stopwatch.Frequency;
                    float black = (float)Math.Clamp(seconds / Gordian.Core.Ui.ZoneLoadingScreen.FadeOutSeconds, 0, 1);
                    _foreground.DrawScreenTint(width, height, new Vector3(1f - black));
                }
                if (brightness < 1f) _foreground.DrawScreenTint(width, height, new Vector3(Math.Clamp(brightness, 0f, 1f)));
                _foreground.End(framebuffer, width, height);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _background.Dispose();
            _foreground.Dispose();
            _commands.Dispose();
        }
    }
}
