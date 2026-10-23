// src/Gordian.App/Graphics/ImGuiInputTestWindow.cs
using System.Numerics;
using System.Text;
using Hexa.NET.ImGui;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// A small interactive ImGui window for checking Tier 3 input (#380): a button, a slider, a checkbox and a text field.
    /// Ctrl+Shift+F11 in a viewport window shows or hides it. Nothing else uses it; addon canvases register their own
    /// <see cref="IImGuiOverlay"/>.
    /// </summary>
    public sealed class ImGuiInputTestWindow : IImGuiOverlay
    {
        public static ImGuiInputTestWindow Instance { get; } = new();

        private volatile bool _visible;
        private int _clicks;
        private float _slider = 0.5f;
        private bool _check;
        private float _wheel;
        private readonly byte[] _text = new byte[128];

        private ImGuiInputTestWindow()
        {
        }

        public bool IsVisible => _visible;

        /// <summary>Shows the window if hidden and hides it if shown; returns the new state.</summary>
        public bool Toggle()
        {
            ImGuiOverlayRegistry.Add(this);
            _visible = !_visible;
            return _visible;
        }

        public unsafe void Draw()
        {
            ImGui.SetNextWindowPos(new Vector2(60, 80), ImGuiCond.FirstUseEver);
            ImGui.SetNextWindowSize(new Vector2(320, 0), ImGuiCond.FirstUseEver);
            bool open = _visible;
            if (ImGui.Begin("ImGui input test"u8, ref open, ImGuiWindowFlags.NoSavedSettings))
            {
                ImGui.TextUnformatted("Mouse, wheel and typing go here, not to the game."u8);
                if (ImGui.Button("Click me"u8)) _clicks++;
                ImGui.SameLine();
                ImGui.Text($"clicks: {_clicks}");
                ImGui.SliderFloat("slider"u8, ref _slider, 0f, 1f);
                ImGui.Checkbox("check"u8, ref _check);
                fixed (byte* text = _text) ImGui.InputText("text"u8, text, (nuint)_text.Length);
                ImGui.TextUnformatted(Encoding.UTF8.GetString(_text).TrimEnd('\0'));
                // Scrollable content, so the wheel has something to move; the readout shows the wheel ImGui last saw.
                ImGui.Text($"wheel: {_wheel:0.##} (scroll {ImGui.GetScrollY():0})");
                if (ImGui.BeginChild("##list"u8, new Vector2(0, 110), ImGuiChildFlags.Borders))
                {
                    for (int i = 1; i <= 40; i++) ImGui.Text($"Line {i}");
                    if (ImGui.IsWindowHovered())
                    {
                        float wheel = ImGui.GetIO().MouseWheel;
                        if (wheel != 0) _wheel = wheel;
                    }
                }
                ImGui.EndChild();
            }
            ImGui.End();
            _visible = open;
        }
    }
}
