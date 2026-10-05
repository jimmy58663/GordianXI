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

        // The same arrow after a dialog line that waits for Confirm: 2 px past the text, 3 px below the row's top
        // (the retail recording shows it centred on the text's height).
        private const float WaitArrowGap = 2, WaitArrowTop = 3;

        /// <summary>
        /// A channel's default log colour (half scale, 0x80 = 1.0): its Font Colors row's retail default
        /// (<see cref="StockUiFontColors"/>, read from a fresh character's cnf.dat), or the fixed colour of channels
        /// without a row (system text white, server chat-type text violet).
        /// </summary>
        public static UiColor ChannelColor(ChatLogChannel channel) => StockUiFontColors.ForChannel(channel) is { } id
            ? StockUiFontColors.Get(id).Default.ToUiColor()
            : StockUiFontColors.FixedColor(channel);

        /// <summary>
        /// A line's colour: its Font Colors row as the player has set it (<paramref name="settings"/>; the retail default
        /// without one), or its channel's fixed colour.
        /// </summary>
        public static UiColor LineColor(ChatLogLine line, StockUiSettings? settings)
        {
            if (line.FontColor is not { } id) return StockUiFontColors.FixedColor(line.Channel);
            return (settings?.GetFontColor(id) ?? StockUiFontColors.Get(id).Default).ToUiColor();
        }

        /// <summary>
        /// Advances a window's slide: the offset decays at one row per <see cref="RowSlideSeconds"/>, and the rows of
        /// lines added since the last frame are added to it, so they start below the window's bottom edge. Returns
        /// the offset in rows (0 when idle or without a state).
        /// </summary>
        private static float UpdateSlide(LogScrollState? scroll, IReadOnlyList<ChatLogLine> lines, StockUiLogFont logFont, float textWidth,
            int timestampMode, int rows)
        {
            if (scroll == null) return 0;
            long now = Stopwatch.GetTimestamp();
            if (scroll.LastTimestamp != 0)
            {
                double elapsed = (now - scroll.LastTimestamp) / (double)Stopwatch.Frequency;
                scroll.OffsetRows = Math.Max(0, scroll.OffsetRows - (float)(elapsed / RowSlideSeconds));
            }
            scroll.LastTimestamp = now;

            var newest = lines.Count > 0 ? lines[^1] : null;
            if (!ReferenceEquals(newest, scroll.Newest))
            {
                if (scroll.Newest != null && newest != null)
                {
                    int newRows = 0;
                    bool found = false;
                    for (int i = lines.Count - 1; i >= 0 && newRows < rows; i--)
                    {
                        if (ReferenceEquals(lines[i], scroll.Newest))
                        {
                            found = true;
                            break;
                        }
                        newRows += StockUiChatLog.GetWrappedRows(lines[i], logFont.GetAdvance, textWidth, timestampMode).Count;
                    }
                    // The previous newest line may have left the copied window; the rows counted are still the new ones.
                    _ = found;
                    scroll.OffsetRows = Math.Min(rows, scroll.OffsetRows + newRows);
                }
                scroll.Newest = newest;
            }
            return scroll.OffsetRows;
        }

        /// <summary>Timestamp colour: pale yellow, about (255, 255, 228), in a retail capture (2026-09-27).</summary>
        public static readonly UiColor TimestampColor = new(0x7F, 0x7F, 0x72, 0x7F);

        /// <summary>
        /// A log window's height (layout px, title band included) for a fractional line count: the frame "logN" is 22 px
        /// + 16 per line after the first, plus the 16-px band; below one line it shrinks to nothing.
        /// </summary>
        public static float WindowHeight(float lines) => lines >= 1 ? 22 + 16 * (lines - 1) + TitleBand : Math.Max(0, lines) * (22 + TitleBand);

        /// <summary>Rows that fit in a window of <paramref name="textBottom"/> layout pixels (from its top) of text area.</summary>
        public static int RowsThatFit(float textBottom, int maxRows)
        {
            int rows = (int)Math.Floor((textBottom - RowTop) / RowPitch);
            return Math.Clamp(rows, 0, maxRows);
        }

        /// <summary>
        /// The slide of a log window's new rows (retail recording, 2026-09-28): a new row enters from the window's
        /// bottom edge and every row above moves up one pitch over about a third of a second, a wrapped line's rows
        /// one after another. Kept per window by the HUD; <see cref="OffsetRows"/> is how many rows the content
        /// still sits below its resting place.
        /// </summary>
        public sealed class LogScrollState
        {
            public ChatLogLine? Newest;
            public float OffsetRows;
            public long LastTimestamp;
        }

        /// <summary>Seconds a new row takes to slide up one pitch (about ten 30 fps frames in the recording).</summary>
        public const double RowSlideSeconds = 0.3;

        /// <summary>Rows drawn beyond the window's count while sliding, so the rows leaving the top move out rather than vanish.</summary>
        private const int SlideExtraRows = 2;

        /// <summary>
        /// Draws a log window: the frame (<paramref name="frameWidth"/> x <paramref name="frameHeight"/>, opaque when
        /// <paramref name="selected"/> for scrolling), its title over the top border, and <paramref name="rows"/>
        /// rows of text from the top, the newest line on the last row; the page-wait arrow when scrolled back. With a
        /// <paramref name="scroll"/> state, rows that arrived since the last frame slide in from the bottom.
        /// </summary>
        public static void DrawLog(StockUiRenderer renderer, UiResourceLibrary library, UiMenuDefinition frame, StockUiLogFont logFont,
            UiFont? titleFallback, StockUiPlacement placement, float frameWidth, float frameHeight, int rows,
            IReadOnlyList<ChatLogLine> lines, int timestampMode, bool scrolledBack, string title, bool selected, bool dialogWaiting = false,
            LogScrollState? scroll = null, StockUiSettings? settings = null, float riseRows = 0)
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

            // Collect wrapped rows from the newest line back until the window is full (and a little past it, for the
            // rows leaving the top while the content slides).
            int collect = rows + SlideExtraRows;
            var visible = new List<(string Text, ChatLogLine Line, bool FirstRow)>(collect);
            for (int i = lines.Count - 1; i >= 0 && visible.Count < collect; i--)
            {
                var wrapped = StockUiChatLog.GetWrappedRows(lines[i], logFont.GetAdvance, textWidth, timestampMode);
                for (int r = wrapped.Count - 1; r >= 0 && visible.Count < collect; r--) visible.Add((wrapped[r], lines[i], r == 0));
            }

            float slide = scrolledBack ? 0 : UpdateSlide(scroll, lines, logFont, textWidth, timestampMode, rows);
            // A window being resized (reactive sizing) is drawn shorter than its rows: they keep their place against the
            // bottom edge (moved up by riseRows) and the ones above the top are clipped.
            riseRows = Math.Clamp(riseRows, 0, rows);
            bool clipped = slide > 0 || riseRows > 0;
            if (clipped) renderer.SetClip(placement.X, placement.Y + RowTop * s, frameWidth * s, Math.Max(0, rows - riseRows) * RowPitch * s);

            // A line's timestamp has its own colour whatever the line's (retail).
            int stamp = StockUiChatLog.TimestampLength(timestampMode);
            float newestEnd = 0, newestY = 0;
            for (int k = 0; k < visible.Count; k++)
            {
                float row = rows - 1 - k + slide;
                if (row >= rows || row <= -1) continue;
                float x = placement.X + TextLeft * s, y = placement.Y + (RowTop + (row - riseRows) * RowPitch) * s;
                var text = visible[k].Text.AsSpan();
                if (visible[k].FirstRow && stamp > 0 && text.Length >= stamp)
                {
                    x = logFont.Draw(renderer, text[..stamp], x, y, s, TimestampColor);
                    text = text[stamp..];
                }
                float end = logFont.Draw(renderer, text, x, y, s, LineColor(visible[k].Line, settings));
                if (k == 0)
                {
                    newestEnd = end;
                    newestY = y;
                }
            }
            if (clipped) renderer.ClearClip();

            // An event line waiting for Confirm carries the page-wait arrow right after its text (a retail recording,
            // 2026-09-28): the "kaipage" group's six-frame gold arrow, stepped like the target cursor.
            if (dialogWaiting && !scrolledBack && visible.Count > 0 && lines[^1].Channel == ChatLogChannel.Dialog
                && library.TryGetGroup("kaipage", out var wait) && wait.Images.Count > 0)
            {
                int step = (int)(Stopwatch.GetTimestamp() / (Stopwatch.Frequency * StockUiTargetWindow.CursorStepSeconds));
                int cycle = Math.Max(1, 2 * wait.Images.Count - 2);
                int frameIndex = step % cycle;
                if (frameIndex >= wait.Images.Count) frameIndex = cycle - frameIndex;
                renderer.DrawImage(wait.Images[frameIndex], newestEnd + WaitArrowGap * s, newestY + WaitArrowTop * s, s);
            }

            if (scrolledBack && library.TryGetGroup("kaipage", out var marker) && marker.Images.Count > 0)
            {
                float bottom = RowTop + (rows - riseRows) * RowPitch;
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
            logFont.Draw(renderer, text.AsSpan(start, end - start), textX, rowY, s, StockUiFontColors.FixedColor(ChatLogChannel.Say));

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
