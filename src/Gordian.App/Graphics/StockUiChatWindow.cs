// src/Gordian.App/Graphics/StockUiChatWindow.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Gordian.Core.Resources.Ui;
using Gordian.Core.Ui;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// Draws the stock chat (Tier 2 chunk 5): a log window (frame, title over the top border, text rows) and the chat
    /// input line (<c>inline</c>) with the chat mode's tab above it. Frames are DAT menus; the text is composed by
    /// the client in the log font (<see cref="StockUiLogFont"/>).
    /// <para>
    /// Layout measured from a retail capture at 1:1 (2026-09-27, 2560 x 1440, two log windows side by side): a log
    /// window is its DAT frame ("log1".."log8": 22 px + 16 per extra line) plus a 16-px band at the top, so eight
    /// lines make a 150-px window; rows are 16 px from 18 px below the frame top, text 6 px in; the title ends 45 px
    /// from the right edge, centred on the top border, which breaks around it. The input line takes the bottom 22 px
    /// of Window 1 and the rows that no longer fit above it are not shown (the log keeps its top).
    /// </para>
    /// </summary>
    public static class StockUiChatWindow
    {
        /// <summary>Extra height over the DAT log frame (the title band), layout pixels.</summary>
        public const float TitleBand = 16;

        /// <summary>First row's top below the frame top, row pitch, and the text's side insets (layout pixels).</summary>
        public const float RowTop = 18, RowPitch = 16, TextLeft = 6, TextRight = 6, BottomPadding = 3;

        /// <summary>The title's right end (from the frame's right edge), its top (from the frame top), and the border break around it.</summary>
        public const float TitleRightInset = 45, TitleTop = -3, TitleGap = 4;

        /// <summary>
        /// The chat mode's tab (a 64 x 12 <c>fep</c> pill): the DAT's <c>insay</c>/<c>inshout</c>/<c>intell</c>/
        /// <c>inparty</c>/<c>inlink</c> menus place it at (16, 400), the input line being at (16, 410), so it sits
        /// 10 px above the line's top, overlapping its edge.
        /// </summary>
        public const float TabLeft = 0, TabTop = -10, TabHeight = 12;

        // The caret is a 1-px bar from the pale texel of the "gauge" strip (the chat filter scrollbar's thumb colour).
        private const string CaretTexture = "gauge";
        private const float CaretSourceX = 30, CaretSourceY = 12, CaretHeight = 13;
        private static readonly UiColor Neutral = new(0x80, 0x80, 0x80, 0x80);

        // The "kaipage" page-wait arrow (anc "btwait", 10 x 11) marks a window scrolled back from the newest line.
        private const float MoreMarkerInset = 14;

        /// <summary>
        /// Log text colours by channel (half scale, 0x80 = 1.0). From retail captures (2026-09-27): say and system
        /// messages white; server messages (welcome text) violet, about (200, 100, 255); your own tell pink, about
        /// (255, 150, 255). The rest are approximations of the default Font Colors page, not yet captured.
        /// </summary>
        public static UiColor ChannelColor(ChatLogChannel channel) => channel switch
        {
            ChatLogChannel.Shout => new UiColor(0x7F, 0x5C, 0x40, 0x7F),
            ChatLogChannel.Yell => new UiColor(0x7F, 0x6C, 0x48, 0x7F),
            ChatLogChannel.Tell => new UiColor(0x7F, 0x4B, 0x7F, 0x7F),
            ChatLogChannel.Party => new UiColor(0x50, 0x70, 0x7F, 0x7F),
            ChatLogChannel.Linkshell => new UiColor(0x58, 0x7F, 0x50, 0x7F),
            ChatLogChannel.Linkshell2 => new UiColor(0x48, 0x7F, 0x68, 0x7F),
            ChatLogChannel.Unity => new UiColor(0x7F, 0x74, 0x48, 0x7F),
            ChatLogChannel.AssistJ or ChatLogChannel.AssistE => new UiColor(0x60, 0x7F, 0x7F, 0x7F),
            ChatLogChannel.Emote => new UiColor(0x70, 0x70, 0x68, 0x7F),
            ChatLogChannel.ServerMessage => new UiColor(0x64, 0x32, 0x7F, 0x7F),
            ChatLogChannel.Notice => new UiColor(0x68, 0x70, 0x7F, 0x7F),
            ChatLogChannel.Error => new UiColor(0x7F, 0x48, 0x48, 0x7F),
            _ => new UiColor(0x7F, 0x7F, 0x7F, 0x7F),
        };

        /// <summary>Timestamp colour: pale yellow, about (255, 255, 228), in a retail capture (2026-09-27).</summary>
        public static readonly UiColor TimestampColor = new(0x7F, 0x7F, 0x72, 0x7F);

        /// <summary>Rows that fit in a window of <paramref name="textBottom"/> layout pixels (from its top) of text area.</summary>
        public static int RowsThatFit(float textBottom, int maxRows)
        {
            int rows = (int)Math.Floor((textBottom - RowTop) / RowPitch);
            return Math.Clamp(rows, 0, maxRows);
        }

        /// <summary>
        /// Draws a log window: the frame (<paramref name="frameWidth"/> x <paramref name="frameHeight"/>, opaque when
        /// <paramref name="selected"/> for scrolling), its title over the top border, and <paramref name="rows"/>
        /// rows of text from the top, the newest line on the last row; the page-wait arrow when scrolled back.
        /// </summary>
        public static void DrawLog(StockUiRenderer renderer, UiResourceLibrary library, UiMenuDefinition frame, StockUiLogFont logFont,
            UiFont? titleFallback, StockUiPlacement placement, float frameWidth, float frameHeight, int rows,
            IReadOnlyList<ChatLogLine> lines, int timestampMode, bool scrolledBack, string title, bool selected)
        {
            float s = placement.Scale;
            var titles = StockUiTitleText.For(library);
            float titleWidth = string.IsNullOrEmpty(title) ? 0 : titles.Measure(title, titleFallback);
            float titleLeft = frameWidth - TitleRightInset - titleWidth;
            (float, float)? gap = titleWidth > 0 ? (titleLeft - TitleGap, frameWidth - TitleRightInset + TitleGap) : null;
            renderer.DrawMenu(frame, placement, includeButtons: false, frameWidth, opaqueBody: selected, frameHeight: frameHeight,
                opaqueTop: 0, topBorderGap: gap);
            if (titleWidth > 0) titles.Draw(renderer, title, placement.X + titleLeft * s, placement.Y + TitleTop * s, s, titleFallback);

            float textWidth = frameWidth - TextLeft - TextRight;
            if (rows <= 0 || textWidth <= 0) return;

            // Collect wrapped rows from the newest line back until the window is full.
            var visible = new List<(string Text, ChatLogChannel Channel, bool FirstRow)>(rows);
            for (int i = lines.Count - 1; i >= 0 && visible.Count < rows; i--)
            {
                var wrapped = StockUiChatLog.GetWrappedRows(lines[i], logFont.GetAdvance, textWidth, timestampMode);
                for (int r = wrapped.Count - 1; r >= 0 && visible.Count < rows; r--) visible.Add((wrapped[r], lines[i].Channel, r == 0));
            }

            // A line's timestamp has its own colour whatever the line's (retail).
            int stamp = StockUiChatLog.TimestampLength(timestampMode);
            for (int k = 0; k < visible.Count; k++)
            {
                int row = rows - 1 - k;
                float x = placement.X + TextLeft * s, y = placement.Y + (RowTop + row * RowPitch) * s;
                var text = visible[k].Text.AsSpan();
                if (visible[k].FirstRow && stamp > 0 && text.Length >= stamp)
                {
                    x = logFont.Draw(renderer, text[..stamp], x, y, s, TimestampColor);
                    text = text[stamp..];
                }
                logFont.Draw(renderer, text, x, y, s, ChannelColor(visible[k].Channel));
            }

            if (scrolledBack && library.TryGetGroup("kaipage", out var marker) && marker.Images.Count > 0)
            {
                float bottom = RowTop + rows * RowPitch;
                renderer.DrawImage(marker.Images[0], placement.X + (frameWidth - MoreMarkerInset) * s,
                    placement.Y + (bottom - MoreMarkerInset + 2) * s, s);
            }
        }

        /// <summary>
        /// Draws the chat input line: the <c>inline</c> strip opaque (as menus are) and stretched to
        /// <paramref name="frameWidth"/>, the chat mode's tab on its top edge (while the line is not a slash
        /// command), the text in the log font (scrolled left so the caret stays in view) and a blinking caret.
        /// </summary>
        public static void DrawInput(StockUiRenderer renderer, UiResourceLibrary library, StockUiLogFont logFont, UiMenuDefinition inline,
            StockUiPlacement placement, float frameWidth, StockUiChatInput input, long timestamp)
        {
            float s = placement.Scale;
            renderer.DrawMenu(inline, placement, includeButtons: false, frameWidth, opaqueBody: true, opaqueTop: 0);

            string text = input.Text;
            if (!text.StartsWith('/') && library.TryGetGroup("fep", out var fep) && (int)input.Mode < fep.Images.Count)
            {
                renderer.DrawImage(fep.Images[(int)input.Mode], placement.X + TabLeft * s, placement.Y + TabTop * s, s);
            }

            int caret = Math.Clamp(input.Caret, 0, text.Length);
            float available = frameWidth - TextLeft - TextRight;
            int start = VisibleStart(logFont, text, caret, available);
            int end = start;
            float width = 0;
            while (end < text.Length && width + logFont.GetAdvance(text[end]) <= available) width += logFont.GetAdvance(text[end++]);

            float textX = placement.X + TextLeft * s;
            float rowY = placement.Y + (inline.Frame.Height - StockUiLogFont.CellHeight) * 0.5f * s;
            logFont.Draw(renderer, text.AsSpan(start, end - start), textX, rowY, s, ChannelColor(ChatLogChannel.Say));

            // Blink at 0.5 s on, 0.5 s off.
            if (Stopwatch.GetElapsedTime(0, timestamp).TotalMilliseconds % 1000 < 500)
            {
                float caretX = textX + (logFont.MeasureWidth(text.AsSpan(start, caret - start)) + 1) * s;
                float caretY = rowY + (StockUiLogFont.CellHeight - CaretHeight) * 0.5f * s;
                renderer.DrawTextureRect(CaretTexture, CaretSourceX, CaretSourceY, 1, 1, caretX, caretY, Math.Max(1, s), CaretHeight * s, Neutral);
            }
        }

        /// <summary>First character drawn so the caret fits in <paramref name="available"/> layout pixels.</summary>
        public static int VisibleStart(StockUiLogFont font, string text, int caret, float available)
        {
            int start = 0;
            float width = font.MeasureWidth(text.AsSpan(0, caret));
            while (start < caret && width > available) width -= font.GetAdvance(text[start++]);
            return start;
        }
    }
}
