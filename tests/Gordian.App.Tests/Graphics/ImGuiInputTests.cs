// tests/Gordian.App.Tests/Graphics/ImGuiInputTests.cs
using System;
using System.Numerics;
using Avalonia.Input;
using Gordian.App.Graphics;
using Hexa.NET.ImGui;
using Xunit;

namespace Gordian.App.Tests.Graphics
{
    /// <summary>
    /// Mouse and keyboard input for the interactive Tier 3 ImGui overlays (#380): what is queued and when, the key map,
    /// and ImGui itself consuming the queue offscreen on D3D11. The registry is process-wide, so these share one class
    /// (xUnit runs a class's tests one at a time).
    /// </summary>
    [Collection("ImGuiOverlayRegistry")]
    public class ImGuiInputTests
    {
        private sealed class FakeOverlay : IImGuiOverlay
        {
            public bool IsVisible { get; set; } = true;
            public int Clicks;
            public string Typed = string.Empty;

            public void Draw()
            {
                ImGui.SetNextWindowPos(new Vector2(100, 100));
                ImGui.SetNextWindowSize(new Vector2(300, 120));
                if (ImGui.Begin("input test"u8, ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize))
                {
                    if (ImGui.Button("Click"u8, new Vector2(100, 40))) Clicks++;
                    ImGui.InputText("##text", ref Typed, 64);
                }
                ImGui.End();
            }
        }

        private static void Frames(ImGuiOverlayLayer layer, ImGuiOverlayRenderTests.Target target, ImGuiInputBridge input, int count)
        {
            for (int i = 0; i < count; i++)
            {
                target.Clear();
                layer.Render(target.Framebuffer, 1 / 60f, 0, _ => ImGuiOverlayRegistry.DrawAll(), input);
            }
        }

        [Fact]
        public void Bridge_QueuesNothingWhileNoOverlayIsVisible()
        {
            var overlay = new FakeOverlay { IsVisible = false };
            ImGuiOverlayRegistry.Add(overlay);
            try
            {
                var input = new ImGuiInputBridge();
                input.MouseMove(5, 5);
                input.MouseButton(0, true);
                input.Key(ImGuiKey.A, true);
                input.Text("a");
                Assert.Empty(input.Drain());
                Assert.False(input.WantCaptureMouse);
                Assert.False(input.WantCaptureKeyboard);

                overlay.IsVisible = true;
                input.MouseMove(5, 5);
                input.MouseButton(0, true);
                input.MouseWheel(0, 1);
                input.Key(ImGuiKey.A, true);
                input.Text("a");
                input.Focus(true);
                var events = input.Drain();
                Assert.Equal(6, events.Count);
                Assert.Equal(ImGuiInputKind.MousePos, events[0].Kind);
                Assert.Equal(5f, events[0].X);
                Assert.Equal(ImGuiInputKind.MouseButton, events[1].Kind);
                Assert.True(events[1].Down);
                Assert.Equal("a", events[4].Text);
                Assert.Empty(input.Drain());
            }
            finally
            {
                ImGuiOverlayRegistry.Remove(overlay);
            }
        }

        [Fact]
        public void Bridge_DropsEventsPastTheCap_AndResetClearsTheCapture()
        {
            var overlay = new FakeOverlay();
            ImGuiOverlayRegistry.Add(overlay);
            try
            {
                var input = new ImGuiInputBridge();
                for (int i = 0; i < ImGuiInputBridge.MaxQueued + 50; i++) input.MouseMove(i, i);
                Assert.Equal(ImGuiInputBridge.MaxQueued, input.Drain().Count);

                input.Publish(true, true, true, ImGuiMouseCursor.TextInput);
                Assert.True(input.WantCaptureMouse);
                Assert.True(input.WantCaptureKeyboard);
                Assert.True(input.WantTextInput);
                Assert.Equal(ImGuiMouseCursor.TextInput, input.MouseCursor);

                input.MouseMove(1, 1);
                input.Reset();
                Assert.False(input.WantCaptureMouse);
                Assert.False(input.WantCaptureKeyboard);
                Assert.False(input.WantTextInput);
                Assert.Equal(ImGuiMouseCursor.Arrow, input.MouseCursor);
                Assert.Empty(input.Drain());
            }
            finally
            {
                ImGuiOverlayRegistry.Remove(overlay);
            }
        }

        [Fact]
        public void Registry_AnyVisibleFollowsTheOverlays()
        {
            Assert.False(ImGuiOverlayRegistry.AnyVisible);
            var overlay = new FakeOverlay { IsVisible = false };
            ImGuiOverlayRegistry.Add(overlay);
            ImGuiOverlayRegistry.Add(overlay); // twice is once
            try
            {
                Assert.False(ImGuiOverlayRegistry.AnyVisible);
                overlay.IsVisible = true;
                Assert.True(ImGuiOverlayRegistry.AnyVisible);
            }
            finally
            {
                ImGuiOverlayRegistry.Remove(overlay);
            }
            Assert.False(ImGuiOverlayRegistry.AnyVisible);
        }

        [Theory]
        [InlineData(Key.A, ImGuiKey.A)]
        [InlineData(Key.Z, ImGuiKey.Z)]
        [InlineData(Key.D0, ImGuiKey.Key0)]
        [InlineData(Key.D9, ImGuiKey.Key9)]
        [InlineData(Key.NumPad3, ImGuiKey.Keypad3)]
        [InlineData(Key.F5, ImGuiKey.F5)]
        [InlineData(Key.Back, ImGuiKey.Backspace)]
        [InlineData(Key.Enter, ImGuiKey.Enter)]
        [InlineData(Key.Escape, ImGuiKey.Escape)]
        [InlineData(Key.Left, ImGuiKey.LeftArrow)]
        [InlineData(Key.LeftCtrl, ImGuiKey.LeftCtrl)]
        [InlineData(Key.RightShift, ImGuiKey.RightShift)]
        [InlineData(Key.OemComma, ImGuiKey.Comma)]
        [InlineData(Key.MediaPlayPause, ImGuiKey.None)]
        public void KeyMap_MapsAvaloniaKeys(Key key, ImGuiKey expected)
        {
            Assert.Equal(expected, ImGuiKeyMap.FromAvalonia(key));
        }

        [Fact]
        public void KeyMap_CursorShapes()
        {
            Assert.Equal(StandardCursorType.Ibeam, ImGuiKeyMap.ToAvaloniaCursor(ImGuiMouseCursor.TextInput));
            Assert.Equal(StandardCursorType.Arrow, ImGuiKeyMap.ToAvaloniaCursor(ImGuiMouseCursor.Arrow));
            Assert.Equal(StandardCursorType.SizeWestEast, ImGuiKeyMap.ToAvaloniaCursor(ImGuiMouseCursor.ResizeEw));
        }

        [Fact]
        public void Layer_ClicksAndTypesInAnInteractiveWindow_AndCapturesOnlyOverIt()
        {
            using var target = ImGuiOverlayRenderTests.Target.TryCreate();
            if (target == null) return;
            var overlay = new FakeOverlay();
            ImGuiOverlayRegistry.Add(overlay);
            try
            {
                using var layer = new ImGuiOverlayLayer(target.Device, target.Framebuffer.OutputDescription);
                var input = new ImGuiInputBridge();

                // Over the button: the window exists after the first frame, so hover is known from the second.
                input.MouseMove(150, 120);
                Frames(layer, target, input, 3);
                Assert.True(input.WantCaptureMouse, "the pointer is over an ImGui window");

                input.MouseButton(0, true);
                Frames(layer, target, input, 1);
                Assert.True(input.WantCaptureMouse);
                input.MouseButton(0, false);
                Frames(layer, target, input, 2);
                Assert.Equal(1, overlay.Clicks);

                // Click into the text field, then type.
                input.MouseMove(150, 165);
                Frames(layer, target, input, 2);
                input.MouseButton(0, true);
                Frames(layer, target, input, 1);
                input.MouseButton(0, false);
                Frames(layer, target, input, 2);
                Assert.True(input.WantTextInput, "a text field is active");
                input.Text("Hi");
                Frames(layer, target, input, 2);
                Assert.Equal("Hi", overlay.Typed);
                input.Key(ImGuiKey.Backspace, true);
                Frames(layer, target, input, 1);
                input.Key(ImGuiKey.Backspace, false);
                Frames(layer, target, input, 2);
                Assert.Equal("H", overlay.Typed);

                // Off the window: the game keeps the pointer.
                input.MouseMove(20, 20);
                Frames(layer, target, input, 2);
                Assert.False(input.WantCaptureMouse);

                // A click outside ends the text editing.
                input.MouseButton(0, true);
                Frames(layer, target, input, 1);
                input.MouseButton(0, false);
                Frames(layer, target, input, 2);
                Assert.False(input.WantTextInput);
                Assert.False(input.WantCaptureKeyboard);
            }
            finally
            {
                ImGuiOverlayRegistry.Remove(overlay);
            }
        }

        private sealed class ScrollOverlay : IImGuiOverlay
        {
            public bool IsVisible => true;
            public float ScrollY;

            public void Draw()
            {
                ImGui.SetNextWindowPos(new Vector2(100, 100));
                ImGui.SetNextWindowSize(new Vector2(300, 120));
                if (ImGui.Begin("scroll test"u8, ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize))
                {
                    for (int i = 0; i < 60; i++) ImGui.Text($"Line {i}");
                    ScrollY = ImGui.GetScrollY();
                }
                ImGui.End();
            }
        }

        [Fact]
        public void Layer_WheelScrollsAnImGuiWindow()
        {
            using var target = ImGuiOverlayRenderTests.Target.TryCreate();
            if (target == null) return;
            var overlay = new ScrollOverlay();
            ImGuiOverlayRegistry.Add(overlay);
            try
            {
                using var layer = new ImGuiOverlayLayer(target.Device, target.Framebuffer.OutputDescription);
                var input = new ImGuiInputBridge();
                input.MouseMove(200, 150);
                Frames(layer, target, input, 3);
                Assert.True(input.WantCaptureMouse);
                Assert.Equal(0f, overlay.ScrollY);
                input.MouseWheel(0, -1); // down, as Avalonia reports it
                Frames(layer, target, input, 10);
                Assert.True(overlay.ScrollY > 0f, $"scroll {overlay.ScrollY}");
            }
            finally
            {
                ImGuiOverlayRegistry.Remove(overlay);
            }
        }

        [Fact]
        public void Layer_PerformanceOverlayAloneCapturesNothing()
        {
            using var target = ImGuiOverlayRenderTests.Target.TryCreate();
            if (target == null) return;
            using var layer = new ImGuiOverlayLayer(target.Device, target.Framebuffer.OutputDescription);
            var input = new ImGuiInputBridge();
            var stats = new ViewportPerformanceStats();
            var info = new PerformanceOverlayInfo(PerformanceOverlayMode.Fps, stats, "Direct3D11", "Test GPU", ImGuiOverlayRenderTests.Width, ImGuiOverlayRenderTests.Height, true);
            // No interactive overlay is visible, so the viewport passes no bridge; even given one, the overlay's NoInputs windows take nothing.
            for (int i = 0; i < 3; i++)
            {
                target.Clear();
                layer.Render(target.Framebuffer, 1 / 60f, info, PerformanceOverlay.Draw, input);
            }
            Assert.False(input.WantCaptureMouse);
            Assert.False(input.WantCaptureKeyboard);
        }
    }
}
