// src/Gordian.App/Graphics/StockUiTargetWindow.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Ui;
using Gordian.Core.Ui;
using Gordian.Core.World;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// How the target window colours a target's name.
    /// </summary>
    public enum TargetNameKind
    {
        /// <summary>Players, NPCs and objects.</summary>
        Neutral,

        /// <summary>A monster nobody has claimed.</summary>
        UnclaimedMonster,

        /// <summary>A monster claimed by you or your party.</summary>
        ClaimedByParty,

        /// <summary>A monster claimed by someone else.</summary>
        ClaimedByOther,
    }

    /// <summary>
    /// Draws the target window ("targetwi", 112 x 44 above the party window) and the status-icon row.
    /// <para>
    /// The DAT authors only the target window's frame and title; the client adds the target's name and HP gauge.
    /// Measured from Windower captures (2026-09-25): the name at (3, 13) in fontshp at 7/8 size, the HP gauge (the
    /// party window's) at (28, 28) with no number. Name colours: unclaimed monsters pale yellow, monsters claimed by
    /// you or your party/alliance red (both sampled); monsters claimed by others pink (sampled from an alliance capture).
    /// </para>
    /// <para>
    /// Status icons use the "buff" menu grid (frame (142, 48), top-left; 36 slots of 24 x 24 at a 26-pixel pitch,
    /// nine per row), which matches the icon row of the captures; icons come from <see cref="StatusIconLibrary"/>.
    /// </para>
    /// </summary>
    public static class StockUiTargetWindow
    {
        public static readonly UiColor NeutralNameColor = new(0x7F, 0x7F, 0x7F, 0x7F);
        public static readonly UiColor UnclaimedNameColor = new(0x7F, 0x7F, 0x6F, 0x7F);
        public static readonly UiColor ClaimedNameColor = new(0x7F, 0x48, 0x48, 0x7F);
        /// <summary>Pink (240, 122, 180 in a retail alliance capture of "Tiamat"; half scale 78 3D 5A).</summary>
        public static readonly UiColor OtherClaimNameColor = new(0x78, 0x3D, 0x5A, 0x7F);

        private static readonly UiColor Neutral = new(0x80, 0x80, 0x80, 0x80);

        public static UiColor NameColor(TargetNameKind kind) => kind switch
        {
            TargetNameKind.UnclaimedMonster => UnclaimedNameColor,
            TargetNameKind.ClaimedByParty => ClaimedNameColor,
            TargetNameKind.ClaimedByOther => OtherClaimNameColor,
            _ => NeutralNameColor,
        };

        /// <summary>
        /// Classifies a target: monsters by who holds the claim, everything else neutral.
        /// </summary>
        public static TargetNameKind Classify(WorldEntity target, uint localServerId, IReadOnlyCollection<uint> partyServerIds)
        {
            if (target.Type != EntityType.Monster) return TargetNameKind.Neutral;
            uint claim = target.ClaimServerId;
            if (claim == 0) return TargetNameKind.UnclaimedMonster;
            if (claim == localServerId) return TargetNameKind.ClaimedByParty;
            foreach (uint id in partyServerIds)
            {
                if (id == claim) return TargetNameKind.ClaimedByParty;
            }
            return TargetNameKind.ClaimedByOther;
        }

        public static void Draw(StockUiRenderer renderer, UiFont font, UiMenuDefinition menu, StockUiPlacement placement,
            string name, int hpPercent, TargetNameKind kind)
        {
            float s = placement.Scale;
            StockUiPartyWindow.DrawGauge(renderer, placement.X + 28 * s, placement.Y + 28 * s, 64, withKnob: true,
                StockUiPartyWindow.HpGaugeColor, hpPercent, s);

            float limit = menu.Frame.Width - 6;
            renderer.DrawText(font, StockUiPartyWindow.FitName(font, name, limit), placement.X + 3 * s, placement.Y + 13 * s,
                s * StockUiPartyWindow.TextScale, NameColor(kind));
        }

        /// <summary>
        /// Locked on: draws the "targetlo" menu's overlay (windowps image 212: red corner brackets over the frame,
        /// "Locked" between two arrow key-tops along the bottom edge, glow bars at both sides) over the target window.
        /// </summary>
        public static void DrawLockOverlay(StockUiRenderer renderer, UiResourceLibrary library, StockUiPlacement placement)
        {
            if (!library.TryGetMenu("targetlo", out var locked)) return;
            float s = placement.Scale;
            foreach (var button in locked.Buttons)
            {
                foreach (var shape in button.Shapes)
                {
                    if (shape.Kind == 0 && library.TryGetImage(shape, out var image))
                    {
                        renderer.DrawImage(image, placement.X + button.X * s, placement.Y + button.Y * s, s);
                    }
                }
            }
        }

        /// <summary>Seconds per step of the target cursor's shading cycle.</summary>
        public const double CursorStepSeconds = 0.067;

        /// <summary>
        /// The target cursor: the "anc_s" cursor sprite (a diamond and an arrowhead) turned to point down, its tip at
        /// <paramref name="tip"/> (screen pixels), drawn at <paramref name="scale"/> screen pixels per layout pixel like
        /// the rest of the UI. Retail shades it through the group's six frames and back, about 67 ms a step (a 0.8 s
        /// cycle, measured from a capture, 2026-09-26); <paramref name="timestamp"/> is a Stopwatch timestamp.
        /// </summary>
        public static void DrawCursor(StockUiRenderer renderer, UiResourceLibrary library, Vector2 tip, float scale, long timestamp)
        {
            if (!library.TryGetGroup("anc_s", out var group) || group.Images.Count == 0) return;
            int frames = group.Images.Count;
            int step = (int)(timestamp / (Stopwatch.Frequency * CursorStepSeconds) % (2 * frames));
            var image = group.Images[step < frames ? step : 2 * frames - 1 - step];
            if (image.Parts.Count == 0) return;

            // The authored sprite points right; its tip is the rightmost point, halfway down.
            float right = float.MinValue, top = float.MaxValue, bottom = float.MinValue;
            foreach (var part in image.Parts)
            {
                right = Math.Max(right, Math.Max(part.TopRight.X, part.BottomRight.X));
                top = Math.Min(top, Math.Min(part.TopLeft.Y, part.TopRight.Y));
                bottom = Math.Max(bottom, Math.Max(part.BottomLeft.Y, part.BottomRight.Y));
            }
            var transform = Matrix3x2.CreateTranslation(-right, -(top + bottom) * 0.5f) *
                            Matrix3x2.CreateRotation(MathF.PI / 2) *
                            Matrix3x2.CreateScale(scale) *
                            Matrix3x2.CreateTranslation(tip);
            renderer.DrawImage(image, transform);
        }

        /// <summary>
        /// Draws status icons into the "buff" grid, one per slot in the order given.
        /// </summary>
        public static void DrawStatusIcons(StockUiRenderer renderer, StatusIconLibrary icons, UiMenuDefinition grid,
            StockUiPlacement placement, IReadOnlyList<ushort> statusIds)
        {
            float s = placement.Scale;
            for (int i = 0; i < statusIds.Count && i < grid.Buttons.Count; i++)
            {
                if (!icons.TryGetIcon(statusIds[i], out DecodedTexture icon)) continue;
                var slot = grid.Buttons[i];
                renderer.DrawTexture($"status:{statusIds[i]}", icon, placement.X + slot.X * s, placement.Y + slot.Y * s,
                    slot.Width * s, slot.Height * s, Neutral);
            }
        }
    }
}
