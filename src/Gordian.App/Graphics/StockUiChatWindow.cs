// src/Gordian.App/Graphics/StockUiChatWindow.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Gordian.Core.Resources.Ui;
using Gordian.Core.Ui;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// Draws the stock chat (Tier 2 chunk 5): a log window's text over its frame, the chat input line
    /// (<c>inline</c>) with its mode label, text and caret, and the chat-mode list (<c>fep</c>). The frames are
    /// DAT menus; the text inside is composed by the client in the stock <c>fontshp</c> font, one 16-px row per
    /// line as the <c>log1</c>..<c>log8</c> frames are sized (22 px for one line, 16 more per line).
    /// </summary>
    public static class StockUiChatWindow
    {
        /// <summary>Log text rows: first row's top, row pitch and the text's left inset (layout pixels).</summary>
        public const float RowTop = 3, RowPitch = 16, TextLeft = 6, TextRight = 6;

        /// <summary>The input line's mode label (a 68 x 16 <c>fep</c> image) and where its text starts.</summary>
        public const float ModeLabelLeft = 3, ModeLabelTop = 3, InputTextLeft = 76;

        // The caret is a 1-px bar from the pale texel of the "gauge" strip (the chat filter scrollbar's thumb colour).
        private const string CaretTexture = "gauge";
        private const float CaretSourceX = 30, CaretSourceY = 12;
        private static readonly UiColor Neutral = new(0x80, 0x80, 0x80, 0x80);

        // The "fep" list: rows are its 68 x 16 buttons from (3, 4); the frame's kind-6 image (fep#2, a brown bar)
        // is the highlight behind the selected row.
        private const float ModeRowLeft = 3, ModeRowTop = 4;
        private const int ModeCursorImage = 2;

        // The "kaipage" page-wait arrow (anc "btwait", 10 x 11) marks a window scrolled back from the newest line.
        private const float MoreMarkerInset = 14;

        /// <summary>
        /// Log text colours by channel (half scale, 0x80 = 1.0). Not yet calibrated against a retail capture:
        /// approximations of the default Font Colors page (say white, shout orange, tell pink, party cyan, linkshell
        /// green, system pale yellow); combat lines white.
        /// </summary>
        public static UiColor ChannelColor(ChatLogChannel channel) => channel switch
        {
            ChatLogChannel.Shout => new UiColor(0x7F, 0x5C, 0x40, 0x7F),
            ChatLogChannel.Yell => new UiColor(0x7F, 0x6C, 0x48, 0x7F),
            ChatLogChannel.Tell => new UiColor(0x7F, 0x50, 0x70, 0x7F),
            ChatLogChannel.Party => new UiColor(0x50, 0x70, 0x7F, 0x7F),
            ChatLogChannel.Linkshell => new UiColor(0x58, 0x7F, 0x50, 0x7F),
            ChatLogChannel.Linkshell2 => new UiColor(0x48, 0x7F, 0x68, 0x7F),
            ChatLogChannel.Unity => new UiColor(0x7F, 0x74, 0x48, 0x7F),
            ChatLogChannel.AssistJ or ChatLogChannel.AssistE => new UiColor(0x60, 0x7F, 0x7F, 0x7F),
            ChatLogChannel.Emote => new UiColor(0x70, 0x70, 0x68, 0x7F),
            ChatLogChannel.System => new UiColor(0x7F, 0x7F, 0x60, 0x7F),
            ChatLogChannel.Notice => new UiColor(0x68, 0x70, 0x7F, 0x7F),
            ChatLogChannel.Error => new UiColor(0x7F, 0x48, 0x48, 0x7F),
            _ => new UiColor(0x7F, 0x7F, 0x7F, 0x7F),
        };

        /// <summary>
        /// Draws a log window's rows over its frame (drawn by the caller): the newest lines at the bottom, each
        /// wrapped to the window width, the oldest rows cut at the top; the page-wait arrow when the window is
        /// scrolled back.
        /// </summary>
        public static void DrawLog(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiPlacement placement,
            float frameWidth, int rows, IReadOnlyList<ChatLogLine> lines, int timestampMode, bool scrolledBack)
        {
            float s = placement.Scale;
            float textWidth = frameWidth - TextLeft - TextRight;
            if (rows <= 0 || textWidth <= 0) return;

            // Collect wrapped rows from the newest line back until the window is full.
            var visible = new List<(string Text, ChatLogChannel Channel)>(rows);
            for (int i = lines.Count - 1; i >= 0 && visible.Count < rows; i--)
            {
                var wrapped = StockUiChatLog.GetWrappedRows(lines[i], font, textWidth, timestampMode);
                for (int r = wrapped.Count - 1; r >= 0 && visible.Count < rows; r--) visible.Add((wrapped[r], lines[i].Channel));
            }

            float lineOffset = (RowPitch - font.LineHeight) * 0.5f;
            for (int k = 0; k < visible.Count; k++)
            {
                int row = rows - 1 - k;
                float y = placement.Y + (RowTop + row * RowPitch + lineOffset) * s;
                renderer.DrawText(font, visible[k].Text, placement.X + TextLeft * s, y, s, ChannelColor(visible[k].Channel));
            }

            if (scrolledBack && library.TryGetGroup("kaipage", out var marker) && marker.Images.Count > 0)
            {
                float height = RowTop * 2 + rows * RowPitch;
                renderer.DrawImage(marker.Images[0], placement.X + (frameWidth - MoreMarkerInset) * s,
                    placement.Y + (height - MoreMarkerInset) * s, s);
            }
        }

        /// <summary>
        /// Draws the chat input line: the <c>inline</c> strip stretched to <paramref name="frameWidth"/>, the chat
        /// mode's label, the text (scrolled left so the caret stays in view) and a blinking caret.
        /// </summary>
        public static void DrawInput(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, UiMenuDefinition inline,
            StockUiPlacement placement, float frameWidth, StockUiChatInput input, long timestamp)
        {
            float s = placement.Scale;
            renderer.DrawMenu(inline, placement, includeButtons: false, frameWidth);

            if (library.TryGetGroup("fep", out var fep) && (int)input.Mode < fep.Images.Count)
            {
                renderer.DrawImage(fep.Images[(int)input.Mode], placement.X + ModeLabelLeft * s, placement.Y + ModeLabelTop * s, s);
            }

            string text = input.Text;
            int caret = Math.Clamp(input.Caret, 0, text.Length);
            float available = frameWidth - InputTextLeft - TextRight;
            int start = VisibleStart(font, text, caret, available);
            int end = start;
            float width = 0;
            while (end < text.Length && width + font.GetAdvance(text[end]) <= available) width += font.GetAdvance(text[end++]);

            float textX = placement.X + InputTextLeft * s;
            float textY = placement.Y + (inline.Frame.Height - font.LineHeight) * 0.5f * s;
            renderer.DrawText(font, text.AsSpan(start, end - start), textX, textY, s);

            // Blink at 0.5 s on, 0.5 s off.
            if (Stopwatch.GetElapsedTime(0, timestamp).TotalMilliseconds % 1000 < 500)
            {
                float caretX = textX + (font.MeasureWidth(text.AsSpan(start, caret - start)) + 1) * s; // clear of the glyph outline
                renderer.DrawTextureRect(CaretTexture, CaretSourceX, CaretSourceY, 1, 1, caretX, textY, Math.Max(1, s), font.LineHeight * s, Neutral);
            }
        }

        /// <summary>First character drawn so the caret fits in <paramref name="available"/> layout pixels.</summary>
        public static int VisibleStart(UiFont font, string text, int caret, float available)
        {
            int start = 0;
            float width = font.MeasureWidth(text.AsSpan(0, caret));
            while (start < caret && width > available) width -= font.GetAdvance(text[start++]);
            return start;
        }

        /// <summary>
        /// Draws the chat-mode list (<c>fep</c>): the frame and its row strips, the highlight bar behind the row
        /// under the cursor, and each mode's label (the orange variant on the cursor's row).
        /// </summary>
        public static void DrawModeList(StockUiRenderer renderer, UiResourceLibrary library, UiMenuDefinition fepMenu,
            StockUiPlacement placement, StockUiChatInput input)
        {
            if (!library.TryGetGroup("fep", out var fep)) return;
            float s = placement.Scale;
            renderer.DrawMenu(fepMenu, placement, includeButtons: true);

            var modes = StockUiChatInput.Modes;
            int first = input.ModeListFirstRow;
            for (int row = 0; row < StockUiChatInput.ModeListRows && first + row < modes.Count; row++)
            {
                int index = first + row;
                bool selected = index == input.ModeListIndex;
                float x = placement.X + ModeRowLeft * s, y = placement.Y + (ModeRowTop + row * RowPitch) * s;
                if (selected && ModeCursorImage < fep.Images.Count) renderer.DrawImage(fep.Images[ModeCursorImage], x, y, s);
                int image = selected ? StockUiChatInput.SelectedLabelImage(modes[index]) : (int)modes[index];
                if (image < fep.Images.Count) renderer.DrawImage(fep.Images[image], x, y, s);
            }
        }
    }
}
