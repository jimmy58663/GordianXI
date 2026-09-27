// src/Gordian.App/Graphics/StockUiDragOverlay.cs
using System;
using System.Collections.Generic;
using Gordian.Core.Resources.Ui;
using Gordian.Core.Ui;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// The unlocked stock UI's editing overlay (Tier 2 chunk 4b, opt-in): an outline and the window id over every
    /// draggable window, brighter under the pointer and while dragged; placeholders (windows drawn only because the
    /// UI is unlocked, with no content) get a faint fill so an empty frame such as the status icon grid is visible.
    /// Not a retail look: retail cannot drag its windows.
    /// </summary>
    public static class StockUiDragOverlay
    {
        // A 1 x 1 pale texel of the "gauge" strip (the chat caret's) stands in for a solid colour.
        private const string SolidTexture = "gauge";
        private const float SolidX = 30, SolidY = 12;
        private const float OutlineThickness = 2;

        // Half-scale colours (0x80 = 1.0): the resting outline is a translucent white, the hovered window pale yellow,
        // the dragged window orange; placeholders are filled with a faint blue.
        private static readonly UiColor Resting = new(0x80, 0x80, 0x80, 0x48);
        private static readonly UiColor Hovered = new(0x80, 0x78, 0x40, 0x78);
        private static readonly UiColor Dragging = new(0x80, 0x58, 0x18, 0x80);
        private static readonly UiColor PlaceholderFill = new(0x30, 0x38, 0x70, 0x14);
        private static readonly UiColor LabelColor = new(0x80, 0x80, 0x80, 0x80);

        /// <summary>
        /// Draws the overlay for the regions registered this frame (each window once, even when it registered
        /// several rectangles, as a split log window does).
        /// </summary>
        public static void Draw(StockUiRenderer renderer, UiFont? font, IReadOnlyList<StockUiDragRegion> regions,
            string? hovered, string? dragging)
        {
            var labelled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var region in regions)
            {
                bool isDragging = dragging != null && string.Equals(dragging, region.WindowId, StringComparison.OrdinalIgnoreCase);
                bool isHovered = hovered != null && string.Equals(hovered, region.WindowId, StringComparison.OrdinalIgnoreCase);
                var color = isDragging ? Dragging : isHovered ? Hovered : Resting;

                if (region.Placeholder) Fill(renderer, region.X, region.Y, region.Width, region.Height, PlaceholderFill);
                Outline(renderer, region.X, region.Y, region.Width, region.Height, color);

                if (font != null && labelled.Add(region.WindowId))
                {
                    string label = region.Placeholder ? region.WindowId + " (empty)" : region.WindowId;
                    float scale = region.Scale * 0.875f;
                    renderer.DrawText(font, label, region.X + OutlineThickness + 2, region.Y + OutlineThickness + 1, scale, LabelColor);
                }
            }
        }

        /// <summary>Draws a rectangle outline of <see cref="OutlineThickness"/> pixels inside the rectangle.</summary>
        public static void Outline(StockUiRenderer renderer, float x, float y, float width, float height, UiColor color)
        {
            float t = Math.Min(OutlineThickness, Math.Min(width, height) * 0.5f);
            Fill(renderer, x, y, width, t, color);                       // top
            Fill(renderer, x, y + height - t, width, t, color);          // bottom
            Fill(renderer, x, y + t, t, height - 2 * t, color);          // left
            Fill(renderer, x + width - t, y + t, t, height - 2 * t, color); // right
        }

        private static void Fill(StockUiRenderer renderer, float x, float y, float width, float height, UiColor color) =>
            renderer.DrawTextureRect(SolidTexture, SolidX, SolidY, 1, 1, x, y, width, height, color);
    }
}
