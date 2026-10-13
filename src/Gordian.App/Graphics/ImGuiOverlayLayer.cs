// src/Gordian.App/Graphics/ImGuiOverlayLayer.cs
using System;
using System.Numerics;
using Hexa.NET.ImGui;
using NeoVeldrid;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// One viewport window's Tier 3 ImGui layer (#30): its own Dear ImGui context (Hexa.NET.ImGui, chosen in
    /// docs/reference/dependencies.md) and <see cref="ImGuiRenderer"/>, created the first time an overlay is shown. A
    /// frame runs NewFrame, the overlays' drawing, Render and the GPU draw in one go under the device's GPU lock, so the
    /// windows' render threads never share ImGui's current context. The overlays take no input yet (no mouse or keyboard
    /// is fed in), so nothing here touches the game's input path.
    /// </summary>
    public sealed unsafe class ImGuiOverlayLayer : IDisposable
    {
        private readonly ImGuiRenderer _renderer;
        private ImGuiContextPtr _context;
        private bool _disposed;

        /// <summary>Indexed draws the last frame submitted.</summary>
        public int LastDrawCalls => _renderer.LastDrawCalls;

        public ImGuiOverlayLayer(GraphicsDevice gd, OutputDescription outputs)
        {
            _renderer = new ImGuiRenderer(gd, outputs);
            var previous = ImGui.GetCurrentContext();
            _context = ImGui.CreateContext();
            ImGui.SetCurrentContext(_context);
            var io = ImGui.GetIO();
            // No imgui.ini or log beside the executable: overlay windows are placed by code every frame.
            io.IniFilename = null;
            io.LogFilename = null;
            io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset | ImGuiBackendFlags.RendererHasTextures;
            io.ConfigFlags |= ImGuiConfigFlags.NoMouseCursorChange | ImGuiConfigFlags.NoMouse;
            ApplyStyle(ImGui.GetStyle());
            ImGui.SetCurrentContext(previous);
        }

        /// <summary>
        /// GordianXI's overlay look (AGENTS.md): ImGui's dark theme, translucent backgrounds and 6 px rounded corners.
        /// </summary>
        public static void ApplyStyle(ImGuiStylePtr style)
        {
            ImGui.StyleColorsDark(style);
            style.WindowRounding = 6.0f;
            style.ChildRounding = 6.0f;
            style.FrameRounding = 4.0f;
            style.PopupRounding = 6.0f;
            style.WindowBorderSize = 1.0f;
            style.WindowPadding = new Vector2(10, 8);
            style.ItemSpacing = new Vector2(8, 3);
            style.Colors[(int)ImGuiCol.WindowBg] = new Vector4(0.06f, 0.07f, 0.09f, 0.72f);
            style.Colors[(int)ImGuiCol.Border] = new Vector4(0.45f, 0.55f, 0.75f, 0.30f);
            style.Colors[(int)ImGuiCol.Separator] = new Vector4(0.45f, 0.55f, 0.75f, 0.30f);
            style.Colors[(int)ImGuiCol.FrameBg] = new Vector4(0.10f, 0.12f, 0.16f, 0.60f);
            style.Colors[(int)ImGuiCol.PlotLines] = new Vector4(0.45f, 0.85f, 0.55f, 1.0f);
            style.Colors[(int)ImGuiCol.PlotHistogram] = new Vector4(0.45f, 0.85f, 0.55f, 1.0f);
        }

        /// <summary>
        /// Runs one ImGui frame at the framebuffer's size: <paramref name="draw"/> issues the overlays' ImGui calls, then the
        /// result is drawn over the framebuffer. Call under the GPU lock.
        /// </summary>
        public void Render<TState>(Framebuffer framebuffer, float deltaSeconds, TState state, Action<TState> draw)
        {
            if (_disposed || framebuffer.Width == 0 || framebuffer.Height == 0) return;
            var previous = ImGui.GetCurrentContext();
            ImGui.SetCurrentContext(_context);
            try
            {
                var io = ImGui.GetIO();
                io.DisplaySize = new Vector2(framebuffer.Width, framebuffer.Height);
                io.DisplayFramebufferScale = Vector2.One;
                io.DeltaTime = Math.Clamp(deltaSeconds, 1e-4f, 1.0f);
                ImGui.NewFrame();
                draw(state);
                ImGui.Render();
                _renderer.Render(ImGui.GetDrawData(), framebuffer);
            }
            finally
            {
                ImGui.SetCurrentContext(previous);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            var previous = ImGui.GetCurrentContext();
            bool wasCurrent = previous.Handle == _context.Handle;
            ImGui.SetCurrentContext(_context);
            _renderer.DestroyTextures(ImGui.GetPlatformIO().Textures);
            ImGui.DestroyContext(_context);
            _context = ImGuiContextPtr.Null;
            ImGui.SetCurrentContext(wasCurrent ? ImGuiContextPtr.Null : previous);
            _renderer.Dispose();
        }
    }
}
