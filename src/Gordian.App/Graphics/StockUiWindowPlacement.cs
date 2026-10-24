// src/Gordian.App/Graphics/StockUiWindowPlacement.cs
using Gordian.Core.Resources.Ui;
using Gordian.Engine.Ui;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// Where a window goes relative to another or to the log (#418: one rule each instead of a copy per window). The menu DAT
    /// authors a window's companions (the help bar, the item info window, the gil window, the list beside a grid) at layout
    /// places in the same 512 x 448 space as the window itself, so a companion keeps its authored offset from the window
    /// wherever the window was moved; a window at the bottom left sits on Window 1's top edge and rises with it.
    /// </summary>
    public static class StockUiWindowPlacement
    {
        /// <summary>
        /// Gap between the screen's bottom and a window placed at the bottom while the log is hidden (PROVISIONAL: the
        /// capture shows the description close to the bottom; its exact margin depends on how the capture was cropped).
        /// </summary>
        public const float BottomMargin = 16;

        /// <summary>
        /// A window authored at layout (<paramref name="layoutX"/>, <paramref name="layoutY"/>) placed beside a parent window
        /// drawn at <paramref name="parent"/> whose authored frame is <paramref name="parentFrame"/>: the authored offset
        /// between the two, at the parent's scale.
        /// </summary>
        public static StockUiPlacement Beside(StockUiPlacement parent, UiMenuFrame parentFrame, float layoutX, float layoutY)
        {
            float s = parent.Scale;
            return new StockUiPlacement(parent.X + (layoutX - parentFrame.X) * s, parent.Y + (layoutY - parentFrame.Y) * s, s, false);
        }

        /// <summary>A companion window at its own authored place beside the parent (<see cref="Beside(StockUiPlacement, UiMenuFrame, float, float)"/>).</summary>
        public static StockUiPlacement Beside(StockUiPlacement parent, UiMenuFrame parentFrame, UiMenuFrame companion) =>
            Beside(parent, parentFrame, companion.X, companion.Y);

        /// <summary>
        /// The screen y a window at the bottom ends on: Window 1's top edge, or the screen's bottom less
        /// <see cref="BottomMargin"/> while the log is hidden (0 without a screen).
        /// </summary>
        public static float BottomOf(StockUiScreen screen, float scale) =>
            screen.Window1Top ?? (screen.Height > 0 ? screen.Height - BottomMargin * scale : 0);

        /// <summary>Whether the screen is known (a size or the log's edge), so windows can be placed against it.</summary>
        public static bool HasScreen(StockUiScreen screen) => screen.Height > 0 || screen.Window1Top != null;

        /// <summary>
        /// The top of a window <paramref name="height"/> screen px tall with its bottom on Window 1's top edge; while the log
        /// is hidden, the screen's bottom (<see cref="BottomOf"/>), or <paramref name="hiddenLogTop"/> when the window has
        /// its own place then; without a screen, <paramref name="fallbackTop"/>. Never above the screen's top.
        /// </summary>
        public static float TopOnLog(StockUiScreen screen, float scale, float height, float fallbackTop, float? hiddenLogTop = null)
        {
            float y = screen.Window1Top is { } top ? top - height
                : screen.Height > 0 ? hiddenLogTop ?? BottomOf(screen, scale) - height
                : fallbackTop;
            return y < 0 ? 0 : y;
        }
    }
}
