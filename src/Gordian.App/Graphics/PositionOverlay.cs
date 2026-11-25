// src/Gordian.App/Graphics/PositionOverlay.cs
using System;
using System.Globalization;
using System.Numerics;
using System.Text.Unicode;
using Gordian.Engine.Sessions;
using Hexa.NET.ImGui;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// What the position overlay shows, read from the drawn character's session once a frame: the local player's position
    /// (internal axes, Y = height) and heading byte, and the displayed target's (the sub-target while one is picked).
    /// </summary>
    public readonly record struct PositionOverlayInfo(bool Shown, Vector3 Player, byte Heading, bool HasTarget, Vector3 Target, string TargetName)
    {
        /// <summary>The overlay's values for <paramref name="session"/>; not shown without a session or a spawned local player.</summary>
        public static PositionOverlayInfo From(CharacterSession? session)
        {
            if (session == null || !session.World.TryGetByServerId(session.LocalPlayer.ServerId, out var me)) return default;
            var target = session.ActionService.DisplayedTarget;
            return target == null || ReferenceEquals(target, me)
                ? new PositionOverlayInfo(true, me.Position, me.Direction, false, default, string.Empty)
                : new PositionOverlayInfo(true, me.Position, me.Direction, true, target.Position, target.Name);
        }

        /// <summary>Straight-line distance from the player to the target, in yalms (as <c>/target</c> info reports it).</summary>
        public float Distance => Vector3.Distance(Player, Target);
    }

    /// <summary>
    /// The viewport position overlay, drawn with ImGui in the top-left corner while <see cref="ViewportRenderSettings.PositionOverlay"/>
    /// is on (<c>/coords</c>, Ctrl+Shift+F10): the local player's X, height and Y in server order (as <c>/pos</c> and
    /// LandSandBoat's <c>!pos</c> write them), its heading (the wire byte, 0 = east, 64 = south, clockwise, and in
    /// degrees), and with a target its name, position and distance. A testing aid for collision and range checks; text is
    /// formatted into a stack buffer, so it allocates nothing per frame. The window takes no input.
    /// </summary>
    public static unsafe class PositionOverlay
    {
        private const ImGuiWindowFlags OverlayFlags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoInputs
            | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing
            | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoMove;

        /// <summary>Distance from the screen's top-left corner, in pixels.</summary>
        private const float Margin = 12f;

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static readonly Vector4 Dim = new(0.65f, 0.70f, 0.78f, 1f);

        /// <summary>Draws the overlay (nothing when <see cref="PositionOverlayInfo.Shown"/> is false).</summary>
        public static void Draw(PositionOverlayInfo info)
        {
            if (!info.Shown) return;
            ImGui.SetNextWindowPos(new Vector2(Margin, Margin), ImGuiCond.Always, Vector2.Zero);
            if (ImGui.Begin("##gordian_position"u8, OverlayFlags))
            {
                Span<byte> buffer = stackalloc byte[160];
                var p = info.Player;
                Line(buffer, Inv, $"X {p.X,8:0.000}   Height {p.Y,7:0.000}   Y {p.Z,8:0.000}");
                ImGui.PushStyleColor(ImGuiCol.Text, Dim);
                Line(buffer, Inv, $"Heading {info.Heading} ({HeadingDegrees(info.Heading):0.0} deg, {Compass(info.Heading)})");
                ImGui.PopStyleColor();
                if (info.HasTarget)
                {
                    ImGui.Separator();
                    Line(buffer, Inv, $"{info.TargetName}   distance {info.Distance:0.00}");
                    var t = info.Target;
                    ImGui.PushStyleColor(ImGuiCol.Text, Dim);
                    Line(buffer, Inv, $"X {t.X,8:0.000}   Height {t.Y,7:0.000}   Y {t.Z,8:0.000}");
                    ImGui.PopStyleColor();
                }
            }
            ImGui.End();
        }

        /// <summary>The heading byte in degrees: 0 = east, clockwise seen from above (90 = south, 270 = north).</summary>
        public static float HeadingDegrees(byte heading) => heading * (360f / 256f);

        /// <summary>The nearest of the eight compass points to the heading byte.</summary>
        public static string Compass(byte heading) => ((heading + 16) / 32 % 8) switch
        {
            0 => "E",
            1 => "SE",
            2 => "S",
            3 => "SW",
            4 => "W",
            5 => "NW",
            6 => "N",
            _ => "NE",
        };

        /// <summary>
        /// Handles <c>/coords</c>: no argument (or <c>toggle</c>) flips the overlay, <c>on</c> / <c>off</c> set it. The setting
        /// is process-wide (every viewport window) and not saved. Returns the reply.
        /// </summary>
        public static string HandleCommand(string args)
        {
            bool shown;
            switch ((args ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "" or "toggle": shown = !ViewportRenderSettings.PositionOverlay; break;
                case "on" or "show": shown = true; break;
                case "off" or "hide": shown = false; break;
                default: return "Usage: /coords [on | off] (Ctrl+Shift+F10 toggles).";
            }
            ViewportRenderSettings.PositionOverlay = shown;
            return shown ? "Position overlay: shown." : "Position overlay: hidden.";
        }

        /// <summary>Formats one line into <paramref name="buffer"/> as UTF-8 (invariant culture) and draws it.</summary>
        private static void Line(Span<byte> buffer, IFormatProvider provider,
            [System.Runtime.CompilerServices.InterpolatedStringHandlerArgument(nameof(buffer), nameof(provider))] ref Utf8.TryWriteInterpolatedStringHandler text)
        {
            if (!Utf8.TryWrite(buffer, provider, ref text, out int written)) written = 0;
            fixed (byte* b = buffer)
            {
                ImGui.TextUnformatted(b, b + written);
            }
        }
    }
}
