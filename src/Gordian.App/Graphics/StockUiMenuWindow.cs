// src/Gordian.App/Graphics/StockUiMenuWindow.cs
using System;
using Gordian.Core.Resources.Ui;
using Gordian.Core.Ui;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// Draws one open stock menu (see <see cref="StockUiMenuController"/>): the DAT frame and button label sprites,
    /// the marker of the option in effect, a prompt's message, and the animated cursor. The cursor is the frame's
    /// kind-6 element-group reference ("anc_s": the six-frame gold arrow, 32 x 20, authored to the left of its
    /// origin), drawn at the selected button's origin plus the frame's and button's cursor offsets; it plays
    /// forward then backward at the target cursor's 67 ms step.
    /// </summary>
    public static class StockUiMenuWindow
    {
        // Retail underlines the option in effect on config pages with a red bar the width of the button, just under
        // it (seen in a retail capture, 2026-09-26). Drawn from the white centre of the "colorbal" ball, tinted red.
        private const string BarTexture = "colorbal";
        private const float BarSourceX = 7, BarSourceY = 23, BarThickness = 2;
        private static readonly UiColor BarColor = new(0x80, 0x18, 0x18, 0x80);

        /// <summary>
        /// Draws one open menu: the frame, its buttons' label sprites (the page arrows only on a paged menu; the
        /// selected entry tinted orange, glyphs and capsule, as retail does), the red bar under the
        /// option in effect, a prompt's message, and the animated DAT cursor (the frame's kind-6 reference, "anc_s" on
        /// list menus) at the selected button's origin plus the cursor offsets.
        /// </summary>
        public static void Draw(StockUiRenderer renderer, UiResourceLibrary library, UiFont? font, StockUiOpenMenu menu, StockUiPlacement placement,
            long timestamp)
        {
            var definition = menu.Menu;
            var frame = definition.Frame;
            float s = placement.Scale;
            renderer.DrawMenu(definition, placement, includeButtons: false);

            foreach (var button in definition.Buttons)
            {
                bool outsideFrame = button.X < 0 || button.X >= frame.Width;
                if (outsideFrame && menu.PageRing.Count <= 1) continue; // page arrows

                // The selected entry is tinted orange by the client (retail): the glyphs a light orange, the capsule
                // a deeper one; the darkening glyph shadows keep their colour. A button's alternate (kind 4) image (Synthesis, Party, Gamepad) is
                // not the selected look; its meaning is still unknown, so it is not drawn.
                bool isSelected = button.ButtonId == menu.SelectedButtonId;
                float bx = placement.X + button.X * s, by = placement.Y + button.Y * s;
                foreach (var shape in button.Shapes)
                {
                    if (shape.Kind != 0 || !library.TryGetImage(shape, out var image)) continue;
                    if (!isSelected)
                    {
                        renderer.DrawImage(image, bx, by, s);
                        continue;
                    }
                    foreach (var part in image.Parts)
                    {
                        UiColor? tint = part.BlendMode != UiBlendMode.Alpha ? null : IsGlyph(part) ? SelectedGlyphTint : SelectedCapsuleTint;
                        renderer.DrawPart(part, bx, by, s, tint);
                    }
                    break;
                }

                if (menu.MarkedButtonId == button.ButtonId)
                {
                    renderer.DrawTextureRect(BarTexture, BarSourceX, BarSourceY, 1, 1, bx, by + button.Height * s,
                        button.Width * s, BarThickness * s, BarColor);
                }
            }

            if (menu.Message is { Length: > 0 } message && font != null)
            {
                float textWidth = font.MeasureWidth(message) * s;
                float x = placement.X + Math.Max(8 * s, (frame.Width * s - textWidth) * 0.5f);
                renderer.DrawText(font, message, x, placement.Y + 24 * s, s);
            }

            var selected = menu.SelectedButton;
            if (selected != null) DrawMenuCursor(renderer, library, frame, selected, placement, timestamp);
        }

        /// <summary>
        /// Half-scale tint of the selected entry's glyphs: a retail capture's highlighted label peaks at FFC05C, a
        /// white glyph at about (1.0, 0.75, 0.36).
        /// </summary>
        private static readonly UiColor SelectedGlyphTint = new(0x80, 0x60, 0x2E, 0x80);

        /// <summary>
        /// Half-scale tint of the selected entry's capsule and any other alpha-blended part: the same capture's capsule
        /// goes from (127, 133, 186) unselected to (133, 87, 48) selected, about (1.05, 0.65, 0.26).
        /// </summary>
        private static readonly UiColor SelectedCapsuleTint = new(0x86, 0x53, 0x21, 0x80);

        /// <summary>
        /// The glyph parts of a label: alpha-blended parts sampling a font sheet ("font", "menu2fon", "mn6font"...)
        /// or the key-cap digits ("keytop"; a retail capture shows a selected Window Type digit tinted whole).
        /// Other alpha parts (the "buttonto" capsule) take the capsule tint; the darkening glyph shadows are left alone.
        /// </summary>
        private static bool IsGlyph(UiSpritePart part)
        {
            if (part.BlendMode != UiBlendMode.Alpha) return false;
            string texture = UiResourceLibrary.TrimResourceName(part.TextureName);
            return texture.Contains("fon", StringComparison.OrdinalIgnoreCase) || texture.StartsWith("keytop", StringComparison.OrdinalIgnoreCase);
        }

        private const string DefaultCursorGroup = "anc_s";

        public static void DrawMenuCursor(StockUiRenderer renderer, UiResourceLibrary library, UiMenuFrame frame, UiMenuButton button,
            StockUiPlacement placement, long timestamp)
        {
            string groupName = DefaultCursorGroup;
            foreach (var shape in frame.Shapes)
            {
                if (shape.Kind == 6) { groupName = shape.GroupId; break; }
            }
            if (!library.TryGetGroup(groupName, out var group) || group.Images.Count == 0) return;
            var image = StockUiTargetWindow.SelectCursorFrame(group, timestamp);
            float x = placement.X + (frame.CursorOffsetX + button.X + button.CursorOffsetX) * placement.Scale;
            float y = placement.Y + (frame.CursorOffsetY + button.Y + button.CursorOffsetY) * placement.Scale;
            renderer.DrawImage(image, x, y, placement.Scale);
        }
    }
}
