// src/Gordian.App/Graphics/StockUiMogHouseLayoutWindow.cs
using System;
using System.Numerics;
using Gordian.Core.Resources.Tables;
using Gordian.Core.Resources.Ui;
using Gordian.Core.World;
using Gordian.Engine.Ui;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// What the Layout (#93) draws over the 3D room, after the retail video <c>20261104-0517-47</c>: with a piece in hand the purple
    /// grid over the usable floor (none in the block in front of the door) and the piece's footprint in pink, or for a wall piece a
    /// pink frame on its wall section; and the small cursor over the piece under the cursor or in hand. The lines are projected
    /// with the frame's camera, so they lean as the video's do. The help bar reads "Layout" and the client's line for the grid.
    /// </summary>
    public static class StockUiMogHouseLayoutWindow
    {
        private static readonly UiColor GridColor = new(0x48, 0x38, 0x70, 0x60);
        private static readonly UiColor FootprintColor = new(0x78, 0x48, 0x60, 0x80);
        private static readonly UiColor RefusedColor = new(0x58, 0x58, 0x58, 0x80);

        /// <summary>
        /// How far each cell's inner square sits inside its edges where the piece can go: retail draws a placeable footprint (or wall
        /// frame) in pink double lines and a refused one in plain grey single lines (retail, 2026-11-07, an Oak Bed over a mannequin).
        /// </summary>
        private const float InsetYalms = 0.05f;

        /// <summary>A wall section's frame: half its length along the wall and its height range (video at 0:36, <b>provisional</b>).</summary>
        private const float WallHalf = 2.4f, WallBottom = 0.6f, WallTop = 4.4f;

        /// <summary>How far above a floor the lines float, so they do not sink into it.</summary>
        private const float LineLift = 0.01f;

        public const string Title = "Layout";

        /// <summary>The help bar's line through the whole grid, in hand or not (video at 0:05-0:26).</summary>
        public const string HelpLine = "Select furniture to place in your room.";

        public static void Draw(StockUiRenderer renderer, UiResourceLibrary library, MogHouseLayoutState layout, in Matrix4x4 viewProjection, Func<ushort, bool>? hasModel,
            float scale, uint width, uint height, long timestamp)
        {
            if (!layout.Active) return;
            var view = new Projection(viewProjection, width, height);
            float line = Math.Max(1f, scale * 0.75f);
            if (layout.HeldInfo is { } info)
            {
                if (info.IsWall) DrawWallFrame(renderer, layout, view, line);
                else
                {
                    DrawGrid(renderer, layout, view, line);
                    var (w, d) = MogHouseFurniture.Footprint(info, layout.Rotation);
                    float y = layout.HeldLevel() * 0.1f;
                    if (layout.CanPlaceHeld()) DrawDoubleCells(renderer, layout, view, layout.HeldX, layout.HeldZ, w, d, y, line);
                    else DrawCells(renderer, layout, view, layout.HeldX, layout.HeldZ, w, d, y, RefusedColor, line);
                }
            }
            else if (layout.Selected is { } shown && hasModel?.Invoke(shown.ItemId) == false && !layout.InfoFor(shown.ItemId).IsWall)
            {
                // A mannequin or statue the room cannot draw yet still shows where it stands.
                var (w, d) = MogHouseFurniture.Footprint(layout.InfoFor(shown.ItemId), shown.Rotation);
                DrawCells(renderer, layout, view, shown.X, shown.Z, w, d, 0f, FootprintColor, line);
            }

            Vector3? anchor = layout.HeldTransform()?.Translation ?? (layout.Selected is { } piece ? layout.CenterOf(piece) : null);
            if (anchor is { } point && view.ToScreen(point) is { } tip) StockUiTargetWindow.DrawCursor(renderer, library, tip, scale, timestamp);
        }

        private static void DrawGrid(StockUiRenderer renderer, MogHouseLayoutState layout, in Projection view, float line)
        {
            bool second = layout.SecondFloor;
            int rows = MogHouseFurniture.Rows(second);
            for (int z = 0; z <= rows; z++)
            {
                for (int x = 0; x <= MogHouseFurniture.GridWidth; x++)
                {
                    bool here = MogHouseFurniture.IsFloorCell(x, z, second);
                    // Each cell's left and bottom edges, and the edge a usable cell shares with an unusable one.
                    if (here || MogHouseFurniture.IsFloorCell(x, z - 1, second)) Segment(renderer, view, Corner(x, z, second), Corner(x + 1, z, second), GridColor, line);
                    if (here || MogHouseFurniture.IsFloorCell(x - 1, z, second)) Segment(renderer, view, Corner(x, z, second), Corner(x, z + 1, second), GridColor, line);
                }
            }
        }

        private static void DrawCells(StockUiRenderer renderer, MogHouseLayoutState layout, in Projection view, int x0, int z0, int w, int d, float y, UiColor color, float line)
        {
            bool second = layout.SecondFloor;
            for (int x = x0; x <= x0 + w; x++) Segment(renderer, view, Corner(x, z0, second, y), Corner(x, z0 + d, second, y), color, line);
            for (int z = z0; z <= z0 + d; z++) Segment(renderer, view, Corner(x0, z, second, y), Corner(x0 + w, z, second, y), color, line);
        }

        /// <summary>A placeable footprint: its outline, and each cell's square a little inside it.</summary>
        private static void DrawDoubleCells(StockUiRenderer renderer, MogHouseLayoutState layout, in Projection view, int x0, int z0, int w, int d, float y, float line)
        {
            bool second = layout.SecondFloor;
            Rectangle(renderer, view, Corner(x0, z0, second, y), Corner(x0 + w, z0 + d, second, y), 0f, line);
            for (int x = x0; x < x0 + w; x++)
            {
                for (int z = z0; z < z0 + d; z++) Rectangle(renderer, view, Corner(x, z, second, y), Corner(x + 1, z + 1, second, y), InsetYalms, line);
            }
        }

        /// <summary>A flat square between two opposite corners, drawn <paramref name="inset"/> yalms inside them.</summary>
        private static void Rectangle(StockUiRenderer renderer, in Projection view, Vector3 p, Vector3 q, float inset, float line)
        {
            float x0 = MathF.Min(p.X, q.X) + inset, x1 = MathF.Max(p.X, q.X) - inset;
            float z0 = MathF.Min(p.Z, q.Z) + inset, z1 = MathF.Max(p.Z, q.Z) - inset;
            Vector3 a = new(x0, p.Y, z0), b = new(x1, p.Y, z0), c = new(x1, p.Y, z1), d = new(x0, p.Y, z1);
            Segment(renderer, view, a, b, FootprintColor, line);
            Segment(renderer, view, b, c, FootprintColor, line);
            Segment(renderer, view, c, d, FootprintColor, line);
            Segment(renderer, view, d, a, FootprintColor, line);
        }

        private static void DrawWallFrame(StockUiRenderer renderer, MogHouseLayoutState layout, in Projection view, float line)
        {
            if (layout.HeldTransform()?.Translation is not { } at) return;
            bool placeable = layout.CanPlaceHeld();
            var color = placeable ? FootprintColor : RefusedColor;
            WallRectangle(renderer, view, at, 0f, color, line);
            if (placeable) WallRectangle(renderer, view, at, InsetYalms * 4f, color, line);
        }

        private static void WallRectangle(StockUiRenderer renderer, in Projection view, Vector3 at, float inset, UiColor color, float line)
        {
            Vector3 a = new(at.X, WallBottom + inset, at.Z - WallHalf + inset), b = new(at.X, WallBottom + inset, at.Z + WallHalf - inset);
            Vector3 c = new(at.X, WallTop - inset, at.Z + WallHalf - inset), d = new(at.X, WallTop - inset, at.Z - WallHalf + inset);
            Segment(renderer, view, a, b, color, line);
            Segment(renderer, view, b, c, color, line);
            Segment(renderer, view, c, d, color, line);
            Segment(renderer, view, d, a, color, line);
        }

        /// <summary>A grid corner in display space (x west).</summary>
        private static Vector3 Corner(int x, int z, bool secondFloor, float y = 0f)
        {
            var (cx, cz) = MogHouseFurniture.CellCorner(x, z, secondFloor);
            return new Vector3(-cx, y + LineLift, cz);
        }

        private static void Segment(StockUiRenderer renderer, in Projection view, Vector3 from, Vector3 to, UiColor color, float thickness)
        {
            if (view.ToScreen(from) is { } a && view.ToScreen(to) is { } b) renderer.DrawLine(a, b, thickness, color);
        }

        /// <summary>World to screen with the frame's camera.</summary>
        private readonly struct Projection
        {
            private readonly Matrix4x4 _viewProjection;
            private readonly float _width, _height;

            public Projection(in Matrix4x4 viewProjection, uint width, uint height)
            {
                _viewProjection = viewProjection;
                _width = width;
                _height = height;
            }

            public Vector2? ToScreen(Vector3 point)
            {
                var clip = Vector4.Transform(new Vector4(point, 1f), _viewProjection);
                if (clip.W <= 0.01f) return null;
                return new Vector2((clip.X / clip.W + 1f) * 0.5f * _width, (1f - clip.Y / clip.W) * 0.5f * _height);
            }
        }
    }
}
