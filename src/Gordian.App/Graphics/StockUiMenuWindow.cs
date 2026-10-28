// src/Gordian.App/Graphics/StockUiMenuWindow.cs
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Models;
using Gordian.Core.Resources.Ui;
using Gordian.Engine.Ui;

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
            long timestamp, (float X, float Y) companionShift = default, StockUiLogFont? logFont = null, StockUiScreen? screen = null)
        {
            if (font != null && menu.AuctionBidConfirm)
            {
                // An Auction House list under a bid's Yes / No shows only the item's info and "Current Gil" (image-27).
                StockUiAuctionWindow.DrawBidConfirm(renderer, library, font, logFont, menu, placement, timestamp);
                return;
            }
            var definition = menu.Menu;
            var frame = definition.Frame;
            float s = placement.Scale;
            // conf5w1 / conf5w2 bake an orange block (gauge texels 48,16; x 88-126 of the 192-px Maximum lines bar, y 93-111)
            // into their frame: it is retail's marker for the lowest Maximum lines value, 4 of 20 (retail capture 2026-10-18,
            // image-2). It stays drawn, under the fill, as in retail.
            renderer.DrawMenu(definition, placement, includeButtons: false,
                opaqueBody: true, frameImage: menu.FrameImage,
                // A client-sized frame stretches its images from their authored height to its own: a Status page fitted
                // to its rows, or an event query with more or fewer rows than the DAT's three, so the background meets
                // the bottom border (round 2, O7).
                frameHeight: frame.AuthoredHeight > 0 ? frame.Height : null);

            foreach (var button in definition.Buttons)
            {
                bool outsideFrame = button.X < 0 || button.X >= frame.Width;
                if (outsideFrame && menu.PageRing.Count <= 1) continue; // page arrows

                // The selected entry is tinted orange by the client (retail): the glyphs a light orange, the capsule
                // a deeper one; the darkening glyph shadows keep their colour. A button's alternate (kind 4) image is
                // the greyed look where the controller says so (Invite you cannot send, chat modes without a
                // linkshell or Unity); elsewhere (Synthesis, Party, Gamepad) its meaning is still unknown, so it is not drawn.
                bool isSelected = button.ButtonId == menu.SelectedButtonId;
                float bx = placement.X + button.X * s, by = placement.Y + button.Y * s;
                if (TryGetLabel(library, button, menu.IsGreyed(button.ButtonId), out var label))
                {
                    // (The Equipment window keeps its slot labels under the icons: retail shows "Main" through a dagger's icon.)
                    if (menu.Check is { } check && StockUiCheckWindow.HoldsItem(check, button.ButtonId))
                    {
                        // A check window slot with an item: the slot box without its label; the icon covers it.
                        foreach (var part in label.Parts)
                        {
                            if (!IsGlyph(part)) renderer.DrawPart(part, bx, by, s);
                        }
                    }
                    else if (!isSelected)
                    {
                        renderer.DrawImage(label, bx, by, s);
                    }
                    else
                    {
                        foreach (var part in label.Parts)
                        {
                            UiColor? tint = part.BlendMode != UiBlendMode.Alpha ? null : IsGlyph(part) ? SelectedGlyphTint : SelectedCapsuleTint;
                            renderer.DrawPart(part, bx, by, s, tint);
                        }
                    }
                }

                if (font != null && menu.SideTexts.TryGetValue(button.ButtonId, out var side) && side.Text.Length > 0)
                {
                    // Client text beside a label (the chat-mode list: the tell partner after Tell's red arrow, "No
                    // Linkshell" / "No Unity" beside greyed modes), centred on the row.
                    renderer.DrawText(font, side.Text, placement.X + side.X * s, by + (button.Height * s - font.LineHeight * s) * 0.5f + side.Y * s, s);
                }

                if (menu.SliderFractions.TryGetValue(button.ButtonId, out float fraction) && fraction > 0)
                {
                    // The bar is authored in the frame (a translucent "gauge" strip 192 wide, knobs at both ends); the
                    // value is the same strip's rows 1-6 drawn opaque and tinted light blue from the bar's left edge,
                    // 12 tall inside the 16-tall bar (a retail capture, 2026-09-26; "framesus" #103 is the same
                    // strip with the tint (64, 96, 127), the capture's fill reads a little more lavender).
                    var track = menu.ConfigPage?.SliderTrack ?? (0, button.Width, SliderFillTop, SliderFillHeight);
                    renderer.DrawTextureRect(SliderTexture, SliderFillSourceX, SliderFillSourceY, SliderFillSourceWidth, SliderFillSourceHeight,
                        bx + track.Left * s, by + track.FillTop * s, track.Width * fraction * s, track.FillHeight * s, SliderTint(menu, button.ButtonId));
                }

                if (menu.IsMarked(button.ButtonId) && menu.Rows.Count == 0)
                {
                    renderer.DrawTextureRect(BarTexture, BarSourceX, BarSourceY, 1, 1, bx, by + button.Height * s,
                        button.Width * s, BarThickness * s, BarColor);
                }
            }

            if (font != null && menu.IsShopList && definition.FindButton(1) is { } firstShopRow)
            {
                DrawShopList(renderer, library, font, logFont, menu, placement, firstShopRow, timestamp, companionShift);
            }
            else if (font != null && menu.IsQuantity)
            {
                DrawQuantity(renderer, library, font, menu, placement);
            }
            else if (font != null && menu.AuctionView != StockUiAuctionView.None)
            {
                StockUiAuctionWindow.Draw(renderer, library, font, logFont, menu, placement, timestamp);
            }
            else if (font != null && menu.IsBlueMagicWindow)
            {
                StockUiBlueMagicWindow.Draw(renderer, library, font, logFont, menu, placement, timestamp);
            }
            else if (font != null && menu.IsTreasureList)
            {
                StockUiTreasureWindow.Draw(renderer, library, font, logFont, menu, placement, screen ?? default, timestamp);
            }
            else if (font != null && menu.Check is { } check)
            {
                StockUiCheckWindow.Draw(renderer, library, font, logFont, menu, check, placement, screen ?? default, timestamp);
            }
            else if (font != null && menu.IsItemList)
            {
                StockUiItemWindow.DrawList(renderer, library, font, logFont, menu, placement, timestamp, screen ?? default);
            }
            else if (font != null && menu.IsSynthesisGrid)
            {
                StockUiSynthesisWindow.Draw(renderer, library, font, logFont, menu, placement, timestamp, screen ?? default);
            }
            else if (font != null && menu.IsEquipment)
            {
                StockUiItemWindow.DrawEquipment(renderer, library, font, logFont, menu, placement, timestamp, screen ?? default);
            }
            else if (font != null && menu.IsMacroWindow)
            {
                StockUiMacroWindow.Draw(renderer, library, font, logFont, menu, placement, timestamp);
            }
            else if (font != null && menu.IsQuery && definition.FindButton(1) is { } firstQueryRow)
            {
                DrawQuery(renderer, font, menu, placement, firstQueryRow, timestamp);
            }
            else if (font != null && menu.IsActionList && definition.FindButton(1) is { } firstActionRow)
            {
                DrawActionList(renderer, library, font, logFont, menu, placement, firstActionRow, timestamp);
            }
            else if (font != null && menu.StatusPage != StockUiStatusPage.None)
            {
                StockUiStatusWindow.DrawPage(renderer, library, font, logFont, menu, placement);
            }
            else if (font != null && (menu.IsKeyItemWindow || menu.IsEquipSetWindow))
            {
                StockUiKeyItemWindow.Draw(renderer, library, font, logFont, menu, placement, timestamp, screen ?? default);
            }
            else if (font != null && menu.Rows.Count > 0 && menu.VisibleRows > 0 && definition.FindButton(1) is { } firstRow)
            {
                // List pages (Chat Filters): the DAT rows are invisible hit regions; the client draws each row's
                // state ball ("framesus" #88 ON / #89 OFF at the row's origin; #83 is a "Hold" state whose meaning
                // is not decoded) and its text 34 px in, as retail captures show (2026-09-26). The list slides
                // smoothly between entries (ScrollFrom -> FirstRow over ScrollDuration), clipped to the rows' area,
                // and a scrollbar on the right edge shows the position.
                library.TryGetGroup(StateGroup, out var states);
                var secondRow = definition.FindButton(2);
                float pitch = RowPitch(firstRow, secondRow);
                float first = ScrolledFirstRow(menu, timestamp);
                float areaX = placement.X + firstRow.X * s, areaY = placement.Y + firstRow.Y * s;
                float areaH = menu.VisibleRows * pitch * s;
                // The state balls' text starts a few pixels left of the row origin, so the clip starts at the window's edge.
                renderer.SetClip(placement.X + ListClipInset * s, areaY, (frame.Width - ListClipInset) * s, areaH);
                int selectedEntry = menu.EntryIndex(menu.SelectedButtonId);
                int from = Math.Max(0, (int)Math.Floor(first) - 1), to = Math.Min(menu.Rows.Count - 1, (int)Math.Ceiling(first) + menu.VisibleRows);
                bool colorRows = menu.ListKind == StockUiListKind.FontColors;
                for (int i = from; i <= to; i++)
                {
                    var row = menu.Rows[i];
                    float ry = areaY + (i - first) * pitch * s;
                    float textY = ry + (firstRow.Height * s - font.LineHeight * s) * 0.5f;
                    if (colorRows)
                    {
                        // Font Colors rows: the message type in white in the log font, no state ball (retail
                        // screenshots, 2026-10-04: upright text like the log's).
                        if (logFont != null) logFont.Draw(renderer, row.Text, areaX + FontColorRowTextInset * s, ry, s, White);
                        else renderer.DrawText(font, row.Text, areaX + FontColorRowTextInset * s, textY, s, row.Color);
                        continue;
                    }
                    if (menu.ListKind is StockUiListKind.GamepadAssign or StockUiListKind.MapAreas)
                    {
                        // Gamepad key assignment (#21): the function from the row's origin, its buttons right-aligned
                        // before the scrollbar; no state ball (GordianXI's own list, retail has no in-game one). The Map
                        // window's Change Map lists (#23) the same way: the area or zone in white in the log font (upright,
                        // the chat's text; retail round 2 image-25), its "[4]" / "(Area)" / "(SSandOria)" at the right.
                        UiColor? tint = i == selectedEntry ? SelectedGlyphTint : null;
                        bool mapRow = menu.ListKind == StockUiListKind.MapAreas;
                        if (mapRow && i == selectedEntry)
                            DrawMapRowBar(renderer, placement.X + ListClipInset * s, ry,
                                placement.X + (frame.Width - ScrollbarInset) * s, firstRow.Height * s);
                        if (mapRow && logFont != null)
                        {
                            logFont.Draw(renderer, row.Text, areaX + MapRowTextInset * s, ry + (firstRow.Height - StockUiLogFont.CellHeight) * 0.5f * s, s, White);
                            tint = null;
                        }
                        else renderer.DrawText(font, row.Text, areaX + (mapRow ? MapRowTextInset : AssignRowTextInset) * s, textY, s, tint);
                        if (row.Value is { Length: > 0 } value)
                        {
                            float valueRight = placement.X + (frame.Width - (mapRow ? MapValueRightPad : AssignValueRightPad)) * s;
                            renderer.DrawText(font, value, valueRight - font.MeasureWidth(value) * s, textY, s, tint);
                        }
                        continue;
                    }
                    int stateImage = row.Marked ? StateOnImage : StateOffImage;
                    if (states != null && stateImage < states.Images.Count) renderer.DrawImage(states.Images[stateImage], areaX, ry, s);
                    renderer.DrawText(font, row.Text, areaX + ListRowTextInset * s, textY, s,
                        i == selectedEntry ? SelectedGlyphTint : null);
                }
                renderer.ClearClip();
                if (menu.CanScroll) DrawScrollbar(renderer, placement, frame, first, menu.Rows.Count, menu.VisibleRows);
                if (colorRows) DrawFontColorBox(renderer, library, font, logFont, menu, placement);
            }

            if (font != null && menu.SampleText is { Length: > 0 } sample)
            {
                // The colour editor: the row's sample in the colour of its sliders, where the box above the list shows
                // it, cut with ".." when it does not fit before the bars' labels.
                float sampleY = placement.Y + (frame.Height * s - font.LineHeight * s) * 0.5f;
                float sampleWidth = SampleTextRight - SampleTextX;
                Func<string, float> measure = logFont != null ? t => logFont.MeasureWidth(t) : t => font.MeasureWidth(t);
                string shown = sample;
                while (shown.Length > 1 && measure(shown) > sampleWidth) shown = shown[..^1];
                if (shown.Length < sample.Length) shown = shown.TrimEnd() + "..";
                if (logFont != null) logFont.Draw(renderer, shown, placement.X + SampleTextX * s, placement.Y + (frame.Height - StockUiLogFont.CellHeight) * 0.5f * s, s, menu.SampleColor);
                else renderer.DrawText(font, shown, placement.X + SampleTextX * s, sampleY, s, menu.SampleColor);
            }

            if (font != null && menu.TitleSuffix?.Invoke() is { Length: > 0 } suffix)
            {
                // Client text after the frame's title (the dead window's time left after "Time Left:"), on the title's
                // line: its glyphs sit at y 5-16 in the frame.
                renderer.DrawText(font, suffix, placement.X + menu.TitleSuffixX * s, placement.Y + TitleSuffixY * s, s);
            }

            if (menu.Message is { Length: > 0 } message && font != null)
            {
                // A prompt's message, centred at y 24. One wider than the window wraps at spaces onto more lines, each centred,
                // kept between the frame's top and the buttons (round 3: the equipment set Paste prompt, "Performing this
                // action will overwrite the current set with a new one. Proceed?", ran out of the 252 px comyn window).
                var lines = StockUiTextLayout.Wrap(message, t => font.MeasureWidth(t), frame.Width - 2 * MessageMargin, MessageMaxLines);
                float pitch = font.LineHeight;
                float buttonsTop = frame.Height;
                foreach (var b in definition.Buttons)
                {
                    if (StockUiMenuController.IsSelectable(b)) buttonsTop = Math.Min(buttonsTop, b.Y);
                }
                float top = MessageTop + pitch * 0.5f * (1 - lines.Count);
                top = Math.Min(top, buttonsTop - MessageButtonGap - lines.Count * pitch);
                top = Math.Max(MessageMinTop, top);
                for (int i = 0; i < lines.Count; i++)
                {
                    float textWidth = font.MeasureWidth(lines[i]) * s;
                    float x = placement.X + Math.Max(MessageMargin * s, (frame.Width * s - textWidth) * 0.5f);
                    renderer.DrawText(font, lines[i], x, placement.Y + (top + i * pitch) * s, s);
                }
            }

            var selected = menu.SelectedButton;
            // The check window's View Wares takes the plain arrow, not the grid's slot box (retail screenshot, 2026-10-07).
            string? cursorGroup = menu.IsCheck && selected?.ButtonId == StockUiCheck.ViewWaresButton ? DefaultCursorGroup : null;
            if (selected != null) DrawMenuCursor(renderer, library, frame, selected, placement, timestamp, cursorGroup);
        }

        /// <summary>
        /// Where a query's comment lines start (layout px from the window's top-left) and their pitch, and how far
        /// into a row its text starts: a retail recording (2026-09-28, 1:1) has the question 14 px in at y 8 and the
        /// options 37 px in (the cursor arrow sits in the 28 px before the row buttons).
        /// </summary>
        /// <summary>Top of the title line's text (the "Time Left:" glyphs of the dead window start at y 5).</summary>
        private const float TitleSuffixY = 5;

        /// <summary>
        /// A prompt's message: the first line's top at y 24 (one line, as before), 8 px clear of the window's sides, at most three
        /// lines, never above y 6 nor closer than 2 px to the buttons.
        /// </summary>
        private const float MessageTop = 24, MessageMargin = 8, MessageMinTop = 6, MessageButtonGap = 2;
        private const int MessageMaxLines = 3;

        private const float QueryCommentX = 14, QueryCommentY = 8, QueryCommentPitch = 16, QueryOptionTextInset = 9;

        /// <summary>
        /// An event query (Tier 2 chunk 6): the question's lines at the top, then one option per row over the
        /// invisible row buttons (the "query" DAT window authors three 20 px rows; the controller sizes the window to
        /// the rows shown). The selected option is tinted like any selected label; long lists scroll three at a time as
        /// the Chat Filters list does, with the same scrollbar (a retail recording, 2026-09-28).
        /// </summary>
        private static void DrawQuery(StockUiRenderer renderer, UiFont font, StockUiOpenMenu menu, StockUiPlacement placement,
            UiMenuButton firstRow, long timestamp)
        {
            float s = placement.Scale;
            for (int i = 0; i < menu.Comments.Count; i++)
            {
                renderer.DrawText(font, menu.Comments[i], placement.X + QueryCommentX * s, placement.Y + (QueryCommentY + i * QueryCommentPitch) * s, s);
            }
            if (menu.Rows.Count == 0 || menu.VisibleRows == 0) return;

            var definition = menu.Menu;
            var secondRow = definition.FindButton(2);
            float pitch = RowPitch(firstRow, secondRow);
            float first = ScrolledFirstRow(menu, timestamp);
            float areaX = placement.X + firstRow.X * s, areaY = placement.Y + firstRow.Y * s;
            float areaH = menu.VisibleRows * pitch * s;
            renderer.SetClip(placement.X, areaY, definition.Frame.Width * s, areaH);
            int selectedEntry = menu.EntryIndex(menu.SelectedButtonId);
            int from = Math.Max(0, (int)Math.Floor(first) - 1), to = Math.Min(menu.Rows.Count - 1, (int)Math.Ceiling(first) + menu.VisibleRows);
            for (int i = from; i <= to; i++)
            {
                float ry = areaY + (i - first) * pitch * s;
                // A greyed option (the Mog House exit's "Change floors.", image-23 of round 1, 2026-10-24) in the action
                // lists' lavender grey.
                bool greyed = i < menu.QueryOptions.Count && menu.QueryOptions[i].Greyed;
                renderer.DrawText(font, menu.Rows[i].Text, areaX + QueryOptionTextInset * s, ry + (firstRow.Height * s - font.LineHeight * s) * 0.5f, s,
                    i == selectedEntry ? SelectedGlyphTint : greyed ? ActionGreyedTint : null);
            }
            renderer.ClearClip();
            if (menu.CanScroll) DrawScrollbar(renderer, placement, definition.Frame, first, menu.Rows.Count, menu.VisibleRows);
        }


        /// <summary>
        /// The spell and ability lists' row layout, measured on the maintainer's retail captures (2026-10-11, 2560 x 1440,
        /// UI 1:1; image-14, -16, -19, -20, -21): each row's 16 x 16 icon at the row button's origin (the <c>magiconw</c>
        /// image the record names), the name in the log font <see cref="ActionRowTextInset"/> px further in, rows at the
        /// DAT's 16 px pitch, no value column. White text; greyed rows (lavender grey, sampled (159, 158, 203) on Foe Requiem
        /// VIII) and the selected row (pink, sampled (255, 132, 216) / (255, 128, 184)).
        /// </summary>
        private const float ActionRowTextInset = 19, ActionIconSize = 16;

        /// <summary>The log font's cell sits 1 px below the row's top: the capitals start 4 px into the row, as in the captures.</summary>
        private const float ActionRowTextDrop = 1;

        /// <summary>An empty list's text ("No mounts available."): log font from (11, 20), lines 20 px apart (image-22, -23).</summary>
        private const float ActionEmptyTextX = 11, ActionEmptyTextY = 20, ActionEmptyPitch = 20, ActionEmptyWidth = 150;

        internal static readonly UiColor ActionSelectedTint = new(0x80, 0x41, 0x64, 0x80);
        internal static readonly UiColor ActionGreyedTint = new(0x4F, 0x4F, 0x65, 0x80);

        /// <summary>
        /// A spell, job ability, weapon skill, trait or pet command list: one action per row over the twelve invisible row
        /// buttons; long lists slide and show the scrollbar as the other lists do; an empty list writes its text.
        /// </summary>
        private static void DrawActionList(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiLogFont? logFont,
            StockUiOpenMenu menu, StockUiPlacement placement, UiMenuButton firstRow, long timestamp)
        {
            float s = placement.Scale;
            var definition = menu.Menu;
            var frame = definition.Frame;
            var rows = menu.ActionRows;
            if (rows.Count == 0)
            {
                if (menu.EmptyText is { } empty)
                {
                    Func<string, float> measure = logFont != null ? t => logFont.MeasureWidth(t) : t => font.MeasureWidth(t);
                    var lines = StockUiTextLayout.Wrap(empty, measure, ActionEmptyWidth, 4);
                    for (int i = 0; i < lines.Count; i++)
                    {
                        float x = placement.X + ActionEmptyTextX * s, y = placement.Y + (ActionEmptyTextY + i * ActionEmptyPitch) * s;
                        if (logFont != null) logFont.Draw(renderer, lines[i], x, y, s, White);
                        else renderer.DrawText(font, lines[i], x, y, s);
                    }
                }
                return;
            }
            if (menu.VisibleRows == 0) return;
            var secondRow = definition.FindButton(2);
            float pitch = RowPitch(firstRow, secondRow);
            float first = ScrolledFirstRow(menu, timestamp);
            float areaY = placement.Y + firstRow.Y * s;
            renderer.SetClip(placement.X, areaY, frame.Width * s, menu.VisibleRows * pitch * s);
            int selectedEntry = menu.EntryIndex(menu.SelectedButtonId);
            int from = Math.Max(0, (int)Math.Floor(first) - 1), to = Math.Min(rows.Count - 1, (int)Math.Ceiling(first) + menu.VisibleRows);
            float iconX = placement.X + firstRow.X * s;
            float textX = iconX + ActionRowTextInset * s;
            library.TryGetGroup(StockUiActionMenu.IconGroup, out var icons);
            for (int i = from; i <= to; i++)
            {
                var row = rows[i];
                float ry = areaY + (i - first) * pitch * s;
                // The picture, else the small icon (pictures 677 and up are not in magiconw; their source is unknown).
                int icon = icons != null && row.Icon >= 0 && row.Icon < icons.Images.Count ? row.Icon : row.SmallIcon;
                if (icons != null && icon >= 0 && icon < icons.Images.Count) DrawIcon(renderer, icons.Images[icon], iconX, ry, ActionIconSize * s);
                UiColor tint = i == selectedEntry ? ActionSelectedTint : row.IsGreyedAt(timestamp) ? ActionGreyedTint : White;
                if (logFont != null) logFont.Draw(renderer, row.Name, textX, ry + ActionRowTextDrop * s, s, tint);
                else renderer.DrawText(font, row.Name, textX, ry + (firstRow.Height * s - font.LineHeight * s) * 0.5f, s, tint);
            }
            renderer.ClearClip();
            if (menu.CanScroll) DrawScrollbar(renderer, placement, frame, first, rows.Count, menu.VisibleRows);
        }

        /// <summary>
        /// The small window (<c>subwindo</c>, 112 x 42) the action lists show above the target window, or above the party
        /// window with no target, 2 px apart (retail image-14, -16, -20 ... -23, 2026-10-11): in <c>fontshp</c> at the party
        /// rows' 7/8 scale, labels 7 px in and values right-aligned 8 px from the right edge, glyph tops 9 and 27 px down.
        /// Spells: "MP Cost" and the cost, then "Next" and the recast ("0:00" when ready); job abilities, pet commands and
        /// mounts: "Next" on the second line only; weapon skills: "TP", then your TP right-aligned on the second line; job
        /// traits: no window.
        /// </summary>
        public static void DrawActionInfo(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiOpenMenu list,
            (float X, float Top)? anchor, float scale, long timestamp, int currentTp, (string Name, byte Hpp)? pet = null)
        {
            if (list.ActionList == StockUiActionList.JobTraits || anchor is not { } at) return;
            if (!library.TryGetMenu(StockUiActionMenu.InfoMenu, out var info)) return;
            float s = scale, ts = s * StockUiPartyWindow.TextScale;
            var placement = new StockUiPlacement(at.X, Math.Max(0, at.Top - (info.Frame.Height + 2) * s), s, false);
            renderer.DrawMenu(info, placement, includeButtons: false, opaqueBody: true);
            float left = placement.X + InfoLabelX * s, right = placement.X + (info.Frame.Width - InfoValueRightInset) * s;
            float line1 = placement.Y + InfoLine1Y * s, line2 = placement.Y + InfoLine2Y * s;
            void Value(string text, float y) => renderer.DrawText(font, text, right - font.MeasureWidth(text) * ts, y, ts);
            var row = list.SelectedActionRow;
            switch (list.ActionList)
            {
                case StockUiActionList.Spells:
                    renderer.DrawText(font, "MP Cost", left, line1, ts);
                    Value((row?.MpCost ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture), line1);
                    renderer.DrawText(font, "Next", left, line2, ts);
                    Value(StockUiActionMenu.FormatRecast(row?.RecastLeft(timestamp) ?? TimeSpan.Zero), line2);
                    break;
                case StockUiActionList.PetAbilities when list.ActionCategory is 6 or 10:
                    // Blood pacts (retail image-46): the pet's name and HP gauge on top, then "MP Cost" and the pact's cost.
                    if (pet is { } p)
                    {
                        StockUiPartyWindow.DrawPetRow(renderer, font, p.Name, p.Hpp, placement.X + InfoPetX * s, placement.Y + InfoPetY * s, s,
                            InfoPetGaugeDx, InfoPetGaugeDy);
                    }
                    renderer.DrawText(font, "MP Cost", left, line2, ts);
                    Value((row is { MpCost: >= 0 } r ? r.MpCost : 0).ToString(System.Globalization.CultureInfo.InvariantCulture), line2);
                    break;
                case StockUiActionList.PetAbilities when list.ActionCategory == StockUiActionMenu.ReadyCommandType:
                    // Ready (retail image-47): "Charge" and the charges left, then "Next".
                    var readyLeft = row?.RecastLeft(timestamp) ?? TimeSpan.Zero;
                    renderer.DrawText(font, "Charge", left, line1, ts);
                    Value(StockUiActionMenu.ReadyCharges(readyLeft).ToString(System.Globalization.CultureInfo.InvariantCulture), line1);
                    renderer.DrawText(font, "Next", left, line2, ts);
                    Value(StockUiActionMenu.FormatRecast(readyLeft), line2);
                    break;
                case StockUiActionList.WeaponSkills:
                    renderer.DrawText(font, "TP", left, line1, ts);
                    Value(currentTp.ToString(System.Globalization.CultureInfo.InvariantCulture), line2);
                    break;
                default:
                    renderer.DrawText(font, "Next", left, line2, ts);
                    Value(StockUiActionMenu.FormatRecast(row?.RecastLeft(timestamp) ?? TimeSpan.Zero), line2);
                    break;
            }
        }

        private const float InfoLabelX = 7, InfoValueRightInset = 8, InfoLine1Y = 8, InfoLine2Y = 26;

        /// <summary>The pet in a blood pact list's info window (retail image-46): name 6 px in at y 4, its gauge 23 px right and 12 px down.</summary>
        private const float InfoPetX = 6, InfoPetY = 4, InfoPetGaugeDx = 23, InfoPetGaugeDy = 12;

        /// <summary>Draws an element-group image scaled so its bounds fill a square of <paramref name="size"/> pixels.</summary>
        internal static void DrawIcon(StockUiRenderer renderer, UiImage image, float x, float y, float size)
        {
            float left = float.MaxValue, top = float.MaxValue, right = float.MinValue, bottom = float.MinValue;
            foreach (var part in image.Parts)
            {
                left = Math.Min(left, Math.Min(part.TopLeft.X, part.BottomLeft.X));
                top = Math.Min(top, Math.Min(part.TopLeft.Y, part.TopRight.Y));
                right = Math.Max(right, Math.Max(part.TopRight.X, part.BottomRight.X));
                bottom = Math.Max(bottom, Math.Max(part.BottomLeft.Y, part.BottomRight.Y));
            }
            float extent = Math.Max(right - left, bottom - top);
            if (extent <= 0) return;
            float scale = size / extent;
            renderer.DrawImage(image, x - left * scale, y - top * scale, scale);
        }

        // The shop list's rows (the "shop" frame, Tier 2 chunk 6c): the DAT authors ten invisible 132 x 16 rows at an 18 px
        // pitch with a 16 x 16 icon slot at x 3; the rows are StockUiItemRowStyle.FramedList (not captured yet: retail's exact
        // text insets and how it prints the price).

        /// <summary>Half-scale tint of a row that cannot be sold (the item's NoSale flag): a mid grey.</summary>
        internal static readonly UiColor GreyedTextTint = new(0x48, 0x48, 0x48, 0x80);

        /// <summary>
        /// The DAT authors "money", "itemctrl" and "iteminfo" all at (16,240), right under the list, and that is where
        /// retail draws them (in-game check 2026-09-28): the gil window, or the quantity prompt in its place, at the
        /// list's left and the item info window beside it, 2 px right of the gil window's 112 px.
        /// </summary>
        private const float InfoOffsetX = 114;

        /// <summary>The "Current Gil" window: the amount sits under the label band, right-aligned.</summary>
        private const float GilTextY = 30, GilRightInset = 8;

        /// <summary>Item icons decoded from the item DATs, by item id (null when the record has none).</summary>
        private static readonly ConcurrentDictionary<ushort, DecodedTexture?> ItemIcons = new();

        /// <summary>The resource generation <see cref="ItemIcons"/> was read under; a VFS reload reads the icons again (#374).</summary>
        private static int _itemIconGeneration = Gordian.Core.Resources.Tables.ZoneDatLoader.Generation;

        private static DecodedTexture? ItemIcon(Func<ushort, ItemRecord?>? itemLookup, ushort itemId)
        {
            if (itemId == 0) return null;
            int generation = Gordian.Core.Resources.Tables.ZoneDatLoader.Generation;
            if (System.Threading.Interlocked.Exchange(ref _itemIconGeneration, generation) != generation) ItemIcons.Clear();
            if (ItemIcons.TryGetValue(itemId, out var cached)) return cached;
            var record = itemLookup?.Invoke(itemId);
            var pixels = record?.IconRgbaPixels;
            var icon = pixels != null && pixels.Length >= 32 * 32 * 4 ? new DecodedTexture($"item{itemId}", 32, 32, pixels) : null;
            ItemIcons[itemId] = icon;
            return icon;
        }

        internal static void DrawItemIcon(StockUiRenderer renderer, StockUiOpenMenu menu, ushort itemId, float x, float y, float size, UiColor? tint = null) =>
            DrawItemIcon(renderer, menu.ItemLookup, itemId, x, y, size, tint);

        /// <summary>Draws an item's 32 x 32 DAT icon (cached per item) scaled to a <paramref name="size"/> px square; nothing for an item without one.</summary>
        public static void DrawItemIcon(StockUiRenderer renderer, Func<ushort, ItemRecord?>? itemLookup, ushort itemId, float x, float y, float size, UiColor? tint = null)
        {
            var icon = ItemIcon(itemLookup, itemId);
            if (icon != null) renderer.DrawTexture($"item:{itemId}", icon, x, y, size, size, tint ?? PointerColor);
        }

        /// <summary>
        /// A shop list (Buy or Sell): one item per row over the invisible row buttons, an icon in the slot, the name,
        /// and the price or count at the right; long lists scroll like the other lists, with the scrollbar. The
        /// "Current Gil" window is drawn under the list and the item info window beside it (their authored places
        /// relative to the list's; where retail puts them is not captured yet).
        /// </summary>
        private static void DrawShopList(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiLogFont? logFont, StockUiOpenMenu menu,
            StockUiPlacement placement, UiMenuButton firstRow, long timestamp, (float X, float Y) companionShift)
        {
            float s = placement.Scale;
            var definition = menu.Menu;
            var secondRow = definition.FindButton(2);
            float pitch = RowPitch(firstRow, secondRow);
            float first = ScrolledFirstRow(menu, timestamp);
            var frame = definition.Frame;
            float areaY = placement.Y + firstRow.Y * s;
            float areaH = menu.VisibleRows * pitch * s;
            if (menu.ShopRows.Count > 0 && menu.VisibleRows > 0)
            {
                renderer.SetClip(placement.X, areaY, frame.Width * s, areaH);
                int selectedEntry = menu.EntryIndex(menu.SelectedButtonId);
                int from = Math.Max(0, (int)Math.Floor(first) - 1), to = Math.Min(menu.ShopRows.Count - 1, (int)Math.Ceiling(first) + menu.VisibleRows);
                float rightEdge = placement.X + frame.Width * s;
                for (int i = from; i <= to; i++)
                {
                    var row = menu.ShopRows[i];
                    float ry = areaY + (i - first) * pitch * s;
                    UiColor? tint = i == selectedEntry ? SelectedGlyphTint : row.Greyed ? GreyedTextTint : null;
                    // The price ("29 G"); a Sell row shows it once the server has appraised the row.
                    string? right = menu.ShopSide == StockUiShopSide.Buy || row.Price > 0 ? StockUiShop.FormatPrice(row.Price) : null;
                    StockUiItemRows.Draw(renderer, font, menu.ItemLookup, StockUiItemRowStyle.FramedList, placement.X, ry, firstRow.Height, s,
                        row.ItemId, row.Name, tint, right: right, rightEdge: rightEdge);
                }
                renderer.ClearClip();
                if (menu.CanScroll) DrawScrollbar(renderer, placement, frame, first, menu.ShopRows.Count, menu.VisibleRows);
            }

            // The gil window and the item info sit right under the list (companionShift is the HUD's displacement of
            // the list, if any); an open quantity prompt stands in the gil window's place.
            if (!menu.ShowsQuantity) DrawGilWindow(renderer, library, font, menu, placement, companionShift);
            if (menu.SelectedShopRow != null) DrawInfoWindow(renderer, library, font, logFont, menu, placement, timestamp, companionShift);
        }

        /// <summary>The "Current Gil" window ("money", authored under the list) with the amount right-aligned under its label.</summary>
        private static void DrawGilWindow(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiOpenMenu menu, StockUiPlacement placement,
            (float X, float Y) shift)
        {
            if (!library.TryGetMenu(StockUiShop.GilMenu, out var gil) || !library.TryGetMenu(StockUiShop.ListMenu, out var list)) return;
            var at = StockUiWindowPlacement.Beside(placement, list.Frame, gil.Frame);
            DrawGilWindow(renderer, font, gil, at with { X = at.X + shift.X, Y = at.Y + shift.Y }, menu.Gil);
        }

        /// <summary>
        /// The "Current Gil" window (<c>money</c>) at a place, the amount right-aligned 8 px from its right edge under the label
        /// band (y 30): the shop's and the Equipment window's.
        /// </summary>
        internal static void DrawGilWindow(StockUiRenderer renderer, UiFont font, UiMenuDefinition gil, StockUiPlacement at, uint amount)
        {
            float s = at.Scale;
            renderer.DrawMenu(gil, at, includeButtons: false, opaqueBody: true);
            string text = StockUiShop.FormatPrice(amount);
            renderer.DrawText(font, text, at.X + (gil.Frame.Width - GilRightInset) * s - font.MeasureWidth(text) * s, at.Y + GilTextY * s, s);
        }

        /// <summary>
        /// The shop's item info window (<see cref="StockUiItemInfoWindow"/>, beside the gil window): the row's item described
        /// as every item info window describes it, sized to its lines and growing down from the gil window's top (#417; retail
        /// shows no price here and the window does not change while a count is chosen, in-game check 2026-09-28). A row
        /// without an item (the guild Sell list's "Select from your inventory.") shows the empty window (image-5).
        /// </summary>
        private static void DrawInfoWindow(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiLogFont? logFont,
            StockUiOpenMenu menu, StockUiPlacement placement, long timestamp, (float X, float Y) shift)
        {
            if (!library.TryGetMenu(StockUiShop.ListMenu, out var list) || !library.TryGetMenu(StockUiShop.GilMenu, out var gil)) return;
            float s = placement.Scale;
            var at = StockUiWindowPlacement.Beside(placement, list.Frame, gil.Frame);
            StockUiItemInfoWindow.Draw(renderer, library, font, logFont, menu, at with { X = at.X + InfoOffsetX * s + shift.X, Y = at.Y + shift.Y },
                timestamp, emptyWhenNone: true);
        }

        /// <summary>
        /// The quantity control's number field ("itemctrl" button 1, 24 x 16 at (34,22)) prints "1 /12": the count
        /// right-aligned inside the field, then "/" and the total running past its right edge towards the "1" arrow
        /// (the maintainer's in-game capture, 2026-09-28).
        /// </summary>
        private const float QuantityCountRight = 15, QuantityTotalX = 19;

        /// <summary>
        /// The quantity prompt: the DAT frame carries the arrows ("All" / "1" either side, + above, - below) and the
        /// number field; the chosen count and the total are drawn in the field. The item info window beside it is the
        /// list's, unchanged.
        /// </summary>
        private static void DrawQuantity(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiOpenMenu menu, StockUiPlacement placement)
        {
            float s = placement.Scale;
            if (menu.Menu.FindButton(StockUiShop.QuantityField) is not { } field) return;
            string count = menu.Quantity.ToString(System.Globalization.CultureInfo.InvariantCulture);
            string total = "/" + menu.QuantityTotal.ToString(System.Globalization.CultureInfo.InvariantCulture);
            float y = placement.Y + field.Y * s + (field.Height * s - font.LineHeight * s) * 0.5f;
            renderer.DrawText(font, count, placement.X + (field.X + QuantityCountRight) * s - font.MeasureWidth(count) * s, y, s);
            renderer.DrawText(font, total, placement.X + (field.X + QuantityTotalX) * s, y, s);
        }

        /// <summary>
        /// A button's label image: its kind-4 alternate when it is greyed and the DAT has one, else its kind-0 image.
        /// </summary>
        private static bool TryGetLabel(UiResourceLibrary library, UiMenuButton button, bool greyed, out UiImage image)
        {
            if (greyed)
            {
                foreach (var shape in button.Shapes)
                {
                    if (shape.Kind == 4 && library.TryGetImage(shape, out image)) return true;
                }
            }
            foreach (var shape in button.Shapes)
            {
                if (shape.Kind == 0 && library.TryGetImage(shape, out image)) return true;
            }
            image = null!;
            return false;
        }

        /// <summary>
        /// Half-scale tint of the selected entry's glyphs: a retail capture's highlighted label peaks at FFC05C, a
        /// white glyph at about (1.0, 0.75, 0.36).
        /// </summary>
        internal static readonly UiColor SelectedGlyphTint = new(0x80, 0x60, 0x2E, 0x80);

        /// <summary>
        /// Half-scale tint of the selected entry's capsule and any other alpha-blended part: the same capture's capsule
        /// goes from (127, 133, 186) unselected to (133, 87, 48) selected, about (1.05, 0.65, 0.26).
        /// </summary>
        internal static readonly UiColor SelectedCapsuleTint = new(0x86, 0x53, 0x21, 0x80);

        /// <summary>
        /// The glyph parts of a label: alpha-blended parts sampling a font sheet ("font", "menu2fon", "mn6font"...)
        /// or the key-cap digits ("keytop"; a retail capture shows a selected Window Type digit tinted whole).
        /// Other alpha parts (the "buttonto" capsule) take the capsule tint; the darkening glyph shadows are left alone.
        /// </summary>
        internal static bool IsGlyph(UiSpritePart part)
        {
            if (part.BlendMode != UiBlendMode.Alpha) return false;
            string texture = UiResourceLibrary.TrimResourceName(part.TextureName);
            return texture.Contains("fon", StringComparison.OrdinalIgnoreCase) || texture.StartsWith("keytop", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Slider fill: rows 1-6 of the "gauge" strip, tinted light blue (retail: (194, 194, 255) at the middle).</summary>
        private const string SliderTexture = "gauge";
        private const float SliderFillSourceX = 0, SliderFillSourceY = 1, SliderFillSourceWidth = 64, SliderFillSourceHeight = 6;
        private const float SliderFillTop = 1, SliderFillHeight = 12;
        private static readonly UiColor SliderFillTint = new(0x68, 0x60, 0x84, 0x80);

        /// <summary>
        /// The colour editor's bars fill in their channel's colour (retail screenshots, 2026-10-05: a dark red R bar,
        /// green G bar, navy B bar); every other slider takes the light blue fill.
        /// </summary>
        private static UiColor SliderTint(StockUiOpenMenu menu, int buttonId)
        {
            if (!menu.Name.Equals(StockUiConfigPages.FontColorEditPage, StringComparison.OrdinalIgnoreCase)) return SliderFillTint;
            return buttonId switch
            {
                1 => new UiColor(0x60, 0x08, 0x08, 0x80),
                2 => new UiColor(0x08, 0x60, 0x08, 0x80),
                _ => new UiColor(0x08, 0x08, 0x38, 0x80),
            };
        }

        /// <summary>
        /// Retail's list scrollbar (captures, 2026-09-26): a 6 px track straddling the window's right edge from the
        /// title band's bottom to the window's bottom, the translucent gauge strip; the thumb is pale pink-white
        /// (255, 221, 228), as long as the visible share of the list and placed at the first entry's share.
        /// </summary>
        private const float ScrollbarWidth = 6, ScrollbarInset = 4;
        private static readonly UiColor ScrollTrackTint = new(0x80, 0x80, 0x80, 0x30);
        private static readonly UiColor ScrollThumbTint = new(0x93, 0x6E, 0x73, 0x80);

        /// <summary>
        /// The list's first row as drawn: <see cref="StockUiOpenMenu.FirstRow"/>, eased from <see cref="StockUiOpenMenu.ScrollFrom"/>
        /// over <see cref="StockUiMenuController.ScrollDuration"/> while the list scrolls (one rule for every scrolling list, #418).
        /// </summary>
        internal static float ScrolledFirstRow(StockUiOpenMenu menu, long timestamp)
        {
            float first = menu.FirstRow;
            if (menu.ScrollFrom == menu.FirstRow) return first;
            double t = (timestamp - menu.ScrollStartedAt) / (double)Stopwatch.Frequency / StockUiMenuController.ScrollDuration.TotalSeconds;
            return t < 1 ? menu.ScrollFrom + (menu.FirstRow - menu.ScrollFrom) * (float)Math.Max(0, t) : first;
        }

        /// <summary>A list's row pitch: from its first row button to its second, or the first row's height without a second below it.</summary>
        internal static float RowPitch(UiMenuButton firstRow, UiMenuButton? secondRow) =>
            secondRow != null && secondRow.Y > firstRow.Y ? secondRow.Y - firstRow.Y : firstRow.Height;

        internal static void DrawScrollbar(StockUiRenderer renderer, StockUiPlacement placement, UiMenuFrame frame, float first, int total, int visible)
        {
            float s = placement.Scale;
            float x = placement.X + (frame.Width - ScrollbarInset) * s;
            float y = placement.Y + StockUiRenderer.MenuBandHeight * s;
            float trackHeight = (frame.Height - StockUiRenderer.MenuBandHeight) * s;
            if (trackHeight <= 0 || total <= 0) return;
            renderer.DrawTextureRect(SliderTexture, 30, 3, 1, 1, x, y, ScrollbarWidth * s, trackHeight, ScrollTrackTint);
            float thumbHeight = trackHeight * Math.Min(visible, total) / total;
            float thumbY = y + trackHeight * Math.Clamp(first, 0, total) / total;
            renderer.DrawTextureRect(SliderTexture, 30, 12, 1, 1, x, thumbY, ScrollbarWidth * s, thumbHeight, ScrollThumbTint);
        }

        /// <summary>The Chat Filters state balls live in the frames group ("frames" resolves to "framesus").</summary>
        private const string StateGroup = "frames";
        private const int StateOnImage = 88, StateOffImage = 89;
        private const float ListRowTextInset = 34;

        /// <summary>The gamepad key-assignment list: the function's text from the row's origin, its buttons ending this far from the window's right edge.</summary>
        private const float AssignRowTextInset = 4, AssignValueRightPad = 18;

        /// <summary>The Change Map lists' text 23 px into the row and their "[4]" ending 44 px short of the frame's right edge (round 1 image-6).</summary>
        private const float MapRowTextInset = 23, MapValueRightPad = 44;

        /// <summary>
        /// The Change Map lists' selected-row bar, top to bottom: retail round 2 image-25 shows a red bar under the
        /// selected row from the window's inner edge to the scrollbar, dark at its top and bottom and brightest just
        /// below its middle (sampled down the bar at x 120, 17 px tall at that capture's scale; colours as drawn).
        /// </summary>
        private static readonly (byte R, byte G, byte B)[] MapRowBarShades =
        [
            (31, 24, 39), (58, 40, 47), (65, 45, 49), (68, 41, 42), (76, 45, 44), (91, 55, 55), (100, 58, 57), (102, 51, 50),
            (112, 54, 51), (124, 66, 61), (116, 63, 61), (100, 51, 51), (99, 50, 52), (87, 53, 60), (77, 49, 59), (62, 33, 49),
        ];

        /// <summary>Draws <see cref="MapRowBarShades"/> as horizontal strips filling the row from <paramref name="left"/> to <paramref name="right"/>.</summary>
        private static void DrawMapRowBar(StockUiRenderer renderer, float left, float top, float right, float height)
        {
            float strip = height / MapRowBarShades.Length;
            for (int k = 0; k < MapRowBarShades.Length; k++)
            {
                var (r, g, b) = MapRowBarShades[k];
                // The solid texel is pale (about 220 per channel, measured from a render), so a half-scale tint of
                // 128/220 of the colour draws the colour itself.
                renderer.DrawTextureRect(SliderTexture, 30, 12, 1, 1, left, top + k * strip, right - left, strip + 0.5f,
                    new UiColor((byte)(r * 37 / 64), (byte)(g * 37 / 64), (byte)(b * 37 / 64), 0x80));
            }
        }

        /// <summary>The Font Colors list's text inset (its rows start at the frame's edge; retail screenshot, 2026-10-04: about 30 px).</summary>
        private const float FontColorRowTextInset = 30;

        /// <summary>
        /// Where the sample text sits in the Font Colors box (<c>textcol2</c>) and the colour editor (<c>textcol3</c>,
        /// the same 366 x 56 rectangle): 16 px in, centred on the window's height (retail screenshot, 2026-10-04); in the
        /// editor it stops before the "R G B" labels at x ~200.
        /// </summary>
        private const float SampleTextX = 16, SampleTextRight = 172;

        /// <summary>
        /// The box above a Font Colors list (<c>textcol2</c>, authored 58 px above <c>textcol1</c>): its frame, and the
        /// selected row's sample in the row's colour.
        /// </summary>
        private static void DrawFontColorBox(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiLogFont? logFont,
            StockUiOpenMenu list, StockUiPlacement placement)
        {
            if (!library.TryGetMenu(StockUiConfigPages.FontColorSampleBox, out var box)) return;
            float s = placement.Scale;
            var listFrame = list.Menu.Frame;
            var at = StockUiWindowPlacement.Beside(placement, listFrame, box.Frame);
            renderer.DrawMenu(box, at, includeButtons: false, opaqueBody: true);
            if (list.SelectedFontColorSample is not { } sample || sample.Text.Length == 0) return;
            if (logFont != null) logFont.Draw(renderer, sample.Text, at.X + SampleTextX * s, at.Y + (box.Frame.Height - StockUiLogFont.CellHeight) * 0.5f * s, s, sample.Color);
            else renderer.DrawText(font, sample.Text, at.X + SampleTextX * s, at.Y + (box.Frame.Height * s - font.LineHeight * s) * 0.5f, s, sample.Color);
        }
        private const float ListClipInset = 8;

        private const string DefaultCursorGroup = "anc_s";

        private static readonly UiColor PointerColor = new(0x80, 0x80, 0x80, 0x80);
        internal static readonly UiColor White = new(0x80, 0x80, 0x80, 0x80);

        /// <summary>
        /// The pointer over a clickable entry (retail hides the system arrow there and draws a grey ring over the
        /// arrow's tip): the arrow, then the ring, at one screen pixel per image pixel like the system cursor, the
        /// arrow's tip at the pointer (<see cref="StockUiPointerArt"/>).
        /// </summary>
        public static void DrawHoverPointer(StockUiRenderer renderer, float x, float y)
        {
            const int size = StockUiPointerArt.Size;
            float ax = MathF.Round(x) - StockUiPointerArt.HotspotX, ay = MathF.Round(y) - StockUiPointerArt.HotspotY;
            renderer.DrawTextureRegion(StockUiPointerArt.Arrow.Name, StockUiPointerArt.Arrow, 0, 0, size, size, ax, ay, size, size, PointerColor);
            float rx = MathF.Round(x + StockUiPointerArt.RingOffsetX - size / 2f), ry = MathF.Round(y + StockUiPointerArt.RingOffsetY - size / 2f);
            renderer.DrawTextureRegion(StockUiPointerArt.Ring.Name, StockUiPointerArt.Ring, 0, 0, size, size, rx, ry, size, size, PointerColor);
        }

        /// <summary>Draws a label in the selected entry's orange tint (glyphs light, capsule deeper), as the menus do.</summary>
        internal static void DrawSelectedImage(StockUiRenderer renderer, UiImage label, float x, float y, float scale)
        {
            foreach (var part in label.Parts)
            {
                UiColor? tint = part.BlendMode != UiBlendMode.Alpha ? null : IsGlyph(part) ? SelectedGlyphTint : SelectedCapsuleTint;
                renderer.DrawPart(part, x, y, scale, tint);
            }
        }

        public static void DrawMenuCursor(StockUiRenderer renderer, UiResourceLibrary library, UiMenuFrame frame, UiMenuButton button,
            StockUiPlacement placement, long timestamp, string? groupOverride = null)
        {
            string groupName = groupOverride ?? DefaultCursorGroup;
            foreach (var shape in frame.Shapes)
            {
                if (groupOverride == null && shape.Kind == 6) { groupName = shape.GroupId; break; }
            }
            if (!library.TryGetGroup(groupName, out var group) || group.Images.Count == 0) return;
            var image = StockUiTargetWindow.SelectCursorFrame(group, timestamp);
            float x = placement.X + (frame.CursorOffsetX + button.X + button.CursorOffsetX) * placement.Scale;
            float y = placement.Y + (frame.CursorOffsetY + button.Y + button.CursorOffsetY) * placement.Scale;
            renderer.DrawImage(image, x, y, placement.Scale);
        }
    }
}
