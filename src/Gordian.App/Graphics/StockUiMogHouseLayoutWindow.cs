// src/Gordian.App/Graphics/StockUiMogHouseLayoutWindow.cs
using System;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Ui;
using Gordian.Core.World;
using Gordian.Engine.Ui;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// The Mog House layout grid (#93) while Layout is open. Retail switches the camera to a top-down view of the room with
    /// a grid overlay (<c>Mog_House.mp4</c> at 0:35); GordianXI does not draw furniture in the room yet, so it draws the
    /// grid seen from above at the top right instead: the usable cells, the first floor's block in front of the door
    /// (darker), each placed piece of the floor shown at its anchor cell (a mannequin in another colour), the cursor cell
    /// and the held piece with an arrow for its rotation. The door side is at the bottom. <b>GordianXI's</b> stand-in until
    /// the room's furniture and the top-down camera are done; the help bar reads retail's help lines (357, 409).
    /// </summary>
    public static class StockUiMogHouseLayoutWindow
    {
        /// <summary>Cell size and the panel's margins, in layout pixels.</summary>
        private const float Cell = 7, Pad = 6, Right = 16, Top = 48, TitleHeight = 14;

        private static readonly DecodedTexture White = new("gordian:white", 1, 1, new byte[] { 255, 255, 255, 255 });

        // 0x80 is full intensity / opacity in the UI's half-scale colours.
        private static readonly UiColor Panel = new(0x08, 0x08, 0x18, 0x60);
        private static readonly UiColor Floor = new(0x30, 0x2C, 0x24, 0x70);
        private static readonly UiColor Blocked = new(0x10, 0x10, 0x10, 0x70);
        private static readonly UiColor Placed = new(0x20, 0x48, 0x78, 0x80);
        private static readonly UiColor Mannequin = new(0x68, 0x30, 0x68, 0x80);
        private static readonly UiColor HeldColor = new(0x80, 0x70, 0x20, 0x80);
        private static readonly UiColor CursorColor = new(0x80, 0x80, 0x80, 0x80);

        public const string Title = "Layout";

        public static void Draw(StockUiRenderer renderer, UiFont font, MogHouseLayoutState layout, float scale, uint width, uint height)
        {
            if (!layout.Active) return;
            int rows = MogHouseFurniture.Rows(layout.SecondFloor);
            float s = scale, cell = Cell * s;
            float panelW = (MogHouseFurniture.GridWidth * Cell + Pad * 2) * s;
            float panelH = (rows * Cell + Pad * 2 + TitleHeight) * s;
            float left = width - Right * s - panelW, top = Top * s;
            Fill(renderer, left, top, panelW, panelH, Panel);
            renderer.DrawWindowBorder(left / s, top / s, panelW / s, panelH / s, s);
            renderer.DrawText(font, layout.SecondFloor ? "2nd floor" : "1st floor", left + Pad * s, top + 3 * s, s);

            float gridLeft = left + Pad * s, gridBottom = top + panelH - Pad * s;
            // Row 0 is the door side, drawn at the bottom.
            float CellX(int x) => gridLeft + x * cell;
            float CellY(int z) => gridBottom - (z + 1) * cell;

            for (int z = 0; z < rows; z++)
            {
                for (int x = 0; x < MogHouseFurniture.GridWidth; x++)
                {
                    Fill(renderer, CellX(x) + s * 0.5f, CellY(z) + s * 0.5f, cell - s, cell - s,
                        MogHouseFurniture.IsFloorCell(x, z, layout.SecondFloor) ? Floor : Blocked);
                }
            }
            foreach (var piece in layout.Pieces)
            {
                if (!layout.IsShown(piece) || piece.X >= MogHouseFurniture.GridWidth || piece.Z >= rows) continue;
                if (layout.Held is { } moving && moving.Container == piece.Container && moving.Slot == piece.Slot) continue;
                Fill(renderer, CellX(piece.X) + s, CellY(piece.Z) + s, cell - 2 * s, cell - 2 * s,
                    MogHouseFurniture.IsMannequin(piece.ItemId) ? Mannequin : Placed);
            }

            float cx = CellX(layout.CursorX), cy = CellY(layout.CursorZ);
            if (layout.Held != null)
            {
                Fill(renderer, cx + s, cy + s, cell - 2 * s, cell - 2 * s, HeldColor);
                // The rotation: a notch on the side the piece faces (0 north, then clockwise).
                float n = 2 * s;
                switch (layout.Rotation & 3)
                {
                    case 0: Fill(renderer, cx + (cell - n) / 2, cy, n, n, CursorColor); break;
                    case 1: Fill(renderer, cx + cell - n, cy + (cell - n) / 2, n, n, CursorColor); break;
                    case 2: Fill(renderer, cx + (cell - n) / 2, cy + cell - n, n, n, CursorColor); break;
                    default: Fill(renderer, cx, cy + (cell - n) / 2, n, n, CursorColor); break;
                }
            }
            Outline(renderer, cx, cy, cell, cell, Math.Max(1f, s * 0.75f), CursorColor);
        }

        /// <summary>The help bar's line: retail's "Place a piece of furniture..." with a piece held, else "Rearrange the furniture...".</summary>
        public static string HelpLine(MogHouseLayoutState layout) => layout.Held != null
            ? "Place a piece of furniture in your Mog House."
            : "Rearrange the furniture in your Mog House.";

        private static void Fill(StockUiRenderer renderer, float x, float y, float w, float h, UiColor color) =>
            renderer.DrawTexture(White.Name, White, x, y, w, h, color);

        private static void Outline(StockUiRenderer renderer, float x, float y, float w, float h, float t, UiColor color)
        {
            Fill(renderer, x, y, w, t, color);
            Fill(renderer, x, y + h - t, w, t, color);
            Fill(renderer, x, y, t, h, color);
            Fill(renderer, x + w - t, y, t, h, color);
        }
    }
}
