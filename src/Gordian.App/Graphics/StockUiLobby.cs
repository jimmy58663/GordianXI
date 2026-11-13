// src/Gordian.App/Graphics/StockUiLobby.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Gordian.Core.Network.LandSandBoat;
using Gordian.Core.Network.Lobby;
using Gordian.Core.Resources.Models;
using Gordian.Core.Resources.Ui;
using Gordian.Engine.Ui;
using Gordian.Engine.Ui.Lobby;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// Draws the character lobby (<see cref="LobbyController"/>) from the lobby DAT's menus. The lobby menus are
    /// authored in three layout spaces: the title (<c>loby1win</c> / <c>loby2win</c>) in 1024 x 768, the character list
    /// screen (<c>bgnamese</c>, <c>dbnamese</c>, <c>lobyhelp</c>) in 640 x 480 and the rest (creation steps, prompts) in the
    /// in-game 512 x 448 layout with their anchors; the prompts' origin is the layout's horizontal centre. Every space is
    /// fitted into the same 4:3 rectangle centred on the viewport (pillarboxed on wide screens), scaled to its height.
    /// <para>
    /// The frame is drawn in two passes around the 3D preview: <see cref="DrawBackground"/> (the screen backgrounds)
    /// before the preview model, <see cref="DrawForeground"/> (windows, text, prompts, cursor) after it.
    /// </para>
    /// <para><b>Unverified against retail:</b> the 4:3 fit, the preview's place and the character info text; see
    /// docs/design/character-lobby.md.</para>
    /// </summary>
    public static class StockUiLobby
    {
        /// <summary>The 4:3 rectangle the lobby is drawn in (screen pixels).</summary>
        public readonly record struct LobbyRect(float X, float Y, float Width, float Height)
        {
            public float Right => X + Width;
            public float CenterX => X + Width * 0.5f;
        }

        public static LobbyRect Fit(uint width, uint height)
        {
            float h = Math.Min(height, width * 3f / 4f);
            float w = h * 4f / 3f;
            return new LobbyRect((width - w) * 0.5f, (height - h) * 0.5f, w, h);
        }

        /// <summary>
        /// The title screen's layout (<c>loby1win</c>, <c>loby2win</c> and the expansion banners, authored in 1024 x 768)
        /// stretched over the whole window, as retail draws it: at 2558 x 1440 its buttons are 2.5x wide and 1.875x tall
        /// (Select Character 355 x 45 px from x 885) and the banners span x 1920-2400 from y 150 (the maintainer's capture
        /// <c>Lobby_Title_Select_Create_Delete_Retail.mp4</c>, 2026-11-13).
        /// </summary>
        public static Matrix3x2 TitleTransform(uint width, uint height) =>
            Matrix3x2.CreateScale(width / TitleSpaceWidth, height / TitleSpaceHeight);

        private const float TitleSpaceWidth = 1024f, TitleSpaceHeight = 768f;

        /// <summary>
        /// The expansion banners on the title screen, top to bottom (<c>lobbywin</c> images, each placed by its own corners
        /// at x 768-960, 46 apart from y 80): Rise of the Zilart, Chains of Promathia, Treasures of Aht Urhgan, Wings of the
        /// Goddess, A Crystalline Prophecy, A Moogle Kupo d'Etat, A Shantotto Ascension, Visions of Abyssea, Scars of Abyssea,
        /// Heroes of Abyssea, Seekers of Adoulin. Banner n is bit n + 1 of the account's expansions (LandSandBoat's
        /// <c>EXPANSION_DISPLAY</c>; bit 0 is the base game).
        /// </summary>
        public static readonly int[] ExpansionBanners = { 141, 157, 159, 172, 173, 174, 175, 177, 178, 179, 180 };

        /// <summary>A banner of an expansion the account lacks (the maintainer, 2026-11-13: "dimmed/grey").</summary>
        private static readonly UiColor MissingExpansionTint = new(0x38, 0x38, 0x38, 0x80);

        private static bool InTitleSpace(string name) =>
            name.Equals(LobbyController.TitleBackgroundMenu, StringComparison.OrdinalIgnoreCase) ||
            name.Equals(LobbyController.MainMenuName, StringComparison.OrdinalIgnoreCase);

        private static bool InListSpace(string name) => name.ToLowerInvariant() switch
        {
            "bgnamese" or "dbnamese" or "dbaccwin" or "hnbackwi" or "ptcbgwin" or "lobyhelp" => true,
            _ => false,
        };

        /// <summary>Where a lobby menu's frame origin lands on screen, and its scale.</summary>
        public static StockUiPlacement Place(UiMenuDefinition menu, LobbyRect rect)
        {
            var frame = menu.Frame;
            if (InTitleSpace(menu.Name))
            {
                float s = rect.Height / 768f;
                return new StockUiPlacement(rect.X + frame.X * s, rect.Y + frame.Y * s, s, false);
            }
            if (InListSpace(menu.Name))
            {
                float s = rect.Height / 480f;
                return new StockUiPlacement(rect.X + frame.X * s, rect.Y + frame.Y * s, s, false);
            }
            float scale = rect.Height / UiResourceLibrary.LayoutHeight;
            bool right = ((int)frame.Anchor & 1) != 0, bottom = ((int)frame.Anchor & 2) != 0;
            // The prompts (ptc*) are authored about the layout's centre column: x 256 with parts from -256.
            float x = frame.X == UiResourceLibrary.LayoutWidth / 2 && !right
                ? rect.CenterX
                : right ? rect.Right - (UiResourceLibrary.LayoutWidth - frame.X) * scale : rect.X + frame.X * scale;
            float y = bottom ? rect.Y + rect.Height - (UiResourceLibrary.LayoutHeight - frame.Y) * scale : rect.Y + frame.Y * scale;
            return new StockUiPlacement(x, y, scale, false);
        }


        /// <summary>The list screen's preview area (640-space x 0-296, feet at y 372 above the help bar): where the model stands, in screen pixels.</summary>
        public static (float CenterX, float FeetY, float TopY) PreviewArea(LobbyRect rect, bool creation = false)
        {
            float s = rect.Height / 480f;
            // Creation keeps its windows at the top left, so the model stands in the middle.
            return (rect.X + (creation ? 290 : 150) * s, rect.Y + 372 * s, rect.Y + 160 * s);
        }

        /// <summary>
        /// The backgrounds: the title art on the main menu, the list screen's backdrop elsewhere. <paramref name="overScene"/>:
        /// the title fly-through is behind (#552), so the backdrops' plain <c>newtex</c> fill is left out and only their art
        /// (the logos, the copyright line) is drawn; the licence page keeps its own backdrop either way.
        /// </summary>
        public static void DrawBackground(StockUiRenderer renderer, LobbyController lobby, uint width, uint height, bool overScene = false)
        {
            var library = lobby.Library;
            if (library == null) return;
            var rect = Fit(width, height);
            lock (lobby.SyncRoot)
            {
                string background = lobby.IsLicencePending ? LicenceBackgroundMenu
                    : lobby.Screen == LobbyScreen.MainMenu ? LobbyController.TitleBackgroundMenu : LobbyController.ListBackgroundMenu;
                // Creation keeps its windows where the backdrop's small logo is, so the logo is left out there.
                bool creating = lobby.Screen == LobbyScreen.Creation;
                if (library.TryGetMenu(background, out var menu))
                {
                    // The backdrop's tiled "newtex" fill spans the whole window (no bars on wide or tall windows); the art
                    // (logo, copyright) stays at its place in the 4:3 area. The licence page follows retail (image-8, 2026-10-05):
                    // its 640 x 480 backdrop is scaled to the window's width from the top left, the logo large at the top left.
                    var placement = lobby.IsLicencePending ? new StockUiPlacement(0, 0, width / 640f, false) : Place(menu, rect);
                    // Stretched rather than tiled further: the fill texture darkens toward its edges, so more tiles would show seams.
                    var stretch = Matrix3x2.CreateScale(width / (float)menu.Frame.Width, height / (float)menu.Frame.Height);
                    if (lobby.IsLicencePending)
                    {
                        // Retail stretches the whole 640 x 480 backdrop (lobbywin #85, though ptcbgwin's frame says 640 x 240),
                        // logo included, over the window: image-8's logo is 4x wide and 3x tall at 2559 x 1439. Its two fill
                        // halves are bright at the window's top and bottom edges and dark in the middle, the reverse of the
                        // parts' corner colours as the in-game windows read them, so the corners are drawn flipped here.
                        var licenceStretch = Matrix3x2.CreateScale(width / 640f, height / 480f);
                        foreach (var shape in menu.Frame.Shapes)
                        {
                            if (shape.Kind != 0 || !library.TryGetImage(shape, out var whole)) continue;
                            renderer.DrawImage(new UiImage { Parts = whole.Parts.Select(p => IsFill(p) ? FlipColours(p) : p).ToList() }, licenceStretch);
                        }
                        return;
                    }
                    foreach (var shape in menu.Frame.Shapes)
                    {
                        if (overScene || shape.Kind != 0 || !library.TryGetImage(shape, out var image)) continue;
                        var fills = new UiImage { Parts = image.Parts.Where(IsFill).ToList() };
                        if (fills.Parts.Count > 0) renderer.DrawImage(fills, stretch);
                    }
                    bool title = InTitleSpace(menu.Name);
                    var titleTransform = TitleTransform(width, height);
                    foreach (var shape in menu.Frame.Shapes)
                    {
                        if (shape.Kind != 0 || !library.TryGetImage(shape, out var art)) continue;
                        foreach (var part in art.Parts)
                        {
                            if (IsFill(part) || (creating && IsLogo(part))) continue;
                            if (title) renderer.DrawPart(part, titleTransform);
                            else renderer.DrawPart(part, placement.X, placement.Y, placement.Scale);
                        }
                    }
                }
            }
        }

        /// <summary>The licence page's backdrop (lobbywin #85: the 640 x 480 fill and the large logo at the top left).</summary>
        public const string LicenceBackgroundMenu = "ptcbgwin";

        /// <summary>The licence text's layout in its window, measured on retail's image-8 (2559 x 1439, 2026-10-05).</summary>
        private const float LicenceTextLeft = 28, LicenceTextTop = 13, LicenceLinePitch = 16;

        /// <summary>
        /// Where the licence page's help bar lines go in its frame: image-8's are on rows 1371-1373 and 1394-1396, one row
        /// below the top of lobyhelp's band (y 155..181) and 26 rows from first to last.
        /// </summary>
        private const float LicenceBarTop = 156, LicenceBarHeight = 26;

        /// <summary>
        /// The licence page's border lines (image-8): 3 px, the lobby DAT's <c>hfr1</c> line from row 1 (its row 0 is
        /// transparent), fading linearly over 24 px at each end (the in-game windows' fade is 16).
        /// </summary>
        private const float LicenceBorderFade = 24, LicenceBorderRow = 1;
        private static readonly UiColor LicenceTextColor = new(0x80, 0x80, 0x80, 0x80);

        private static bool IsLicence(UiMenuDefinition definition) =>
            definition.Name.Equals(LobbyController.LicencePromptMenu, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Where a lobby window goes. The licence page is drawn as retail shows it: at one screen pixel per layout pixel in
        /// a 512 x 448 box centred on the window (the in-game UI's scale; its frame at (256, 128) puts the window centred,
        /// a little above the middle). Every other window uses <see cref="Place"/>.
        /// </summary>
        public static StockUiPlacement PlaceWindow(LobbyController lobby, UiMenuDefinition definition, LobbyRect rect, uint width, uint height)
        {
            if (!IsLicence(definition)) return Place(definition, rect);
            float boxX = (width - UiResourceLibrary.LayoutWidth) * 0.5f, boxY = (height - UiResourceLibrary.LayoutHeight) * 0.5f;
            return new StockUiPlacement(MathF.Round(boxX + definition.Frame.X), MathF.Round(boxY + definition.Frame.Y), 1f, false);
        }

        /// <summary>Full opacity (0x80 on the half scale): image-8's backdrop matches the corner colours drawn opaque.</summary>
        private static UiColor Opaque(UiColor c) => c with { A = 0x80 };

        /// <summary>A part with its top and bottom corner colours swapped and drawn opaque (the licence backdrop, see DrawBackground).</summary>
        private static UiSpritePart FlipColours(UiSpritePart p) => new()
        {
            TopLeft = p.TopLeft, TopRight = p.TopRight, BottomLeft = p.BottomLeft, BottomRight = p.BottomRight,
            SourceX = p.SourceX, SourceY = p.SourceY, SourceWidth = p.SourceWidth, SourceHeight = p.SourceHeight,
            Flags = p.Flags, TextureAttributes = p.TextureAttributes, TextureName = p.TextureName,
            ColorTopLeft = Opaque(p.ColorBottomLeft), ColorTopRight = Opaque(p.ColorBottomRight), ColorBottomLeft = Opaque(p.ColorTopLeft), ColorBottomRight = Opaque(p.ColorTopRight),
        };

        private static bool IsLogo(UiSpritePart part) =>
            UiResourceLibrary.TrimResourceName(part.TextureName).Equals("titlwin", StringComparison.OrdinalIgnoreCase);

        private static bool IsFill(UiSpritePart part) =>
            UiResourceLibrary.TrimResourceName(part.TextureName).Equals("newtex", StringComparison.OrdinalIgnoreCase);

        /// <summary>The windows, their text, the prompt on top and the cursor.</summary>
        public static void DrawForeground(StockUiRenderer renderer, LobbyController lobby, UiFont? font, uint width, uint height, long timestamp,
            StockUiLogFont? logFont = null)
        {
            var library = lobby.Library;
            if (library == null) return;
            var rect = Fit(width, height);
            lock (lobby.SyncRoot)
            {
                bool promptOpen = lobby.Prompt != null;
                switch (lobby.Screen)
                {
                    case LobbyScreen.MainMenu:
                        // Behind the licence page only the title art shows.
                        if (lobby.MainMenu != null && !lobby.IsLicencePending)
                        {
                            DrawTitleMenu(renderer, library, lobby.MainMenu, width, height, timestamp, cursor: !promptOpen);
                            DrawExpansionBanners(renderer, library, lobby.AccountExpansions, width, height);
                        }
                        break;
                    case LobbyScreen.CharacterList:
                        if (lobby.CharacterList != null) DrawCharacterList(renderer, library, font, lobby, lobby.CharacterList, rect, timestamp, cursor: !promptOpen);
                        break;
                    case LobbyScreen.Creation:
                        DrawCreation(renderer, library, font, lobby, rect, timestamp, cursor: !promptOpen);
                        break;
                }

                if (library.TryGetMenu(LobbyController.HelpBarMenu, out var helpBar)) DrawHelpBar(renderer, helpBar, font, lobby.IsLicencePending ? string.Empty : lobby.HelpText, width, height);

                if (lobby.Prompt is { } prompt) DrawPrompt(renderer, library, font, logFont, lobby, prompt, rect, width, height, timestamp);
            }
        }

        /// <summary>
        /// The help bar, on every screen as retail draws it (image-8 for the licence page; the title, list and creation
        /// screens in the maintainer's capture <c>Lobby_Title_Select_Create_Delete_Retail.mp4</c>, 2026-11-13, at 2558 x
        /// 1440: the same band on rows 1371-1396): the <c>lobyhelp</c> bar at one screen pixel per layout pixel, its authored
        /// 69 px above the window's bottom, across the full width, marked by the client's border lines at its top and bottom,
        /// with the line centred on the window in the menus' font at 1:1 (the licence page's bar is empty).
        /// </summary>
        private static void DrawHelpBar(StockUiRenderer renderer, UiMenuDefinition bar, UiFont? font, string text, uint width, uint height)
        {
            var placement = new StockUiPlacement(256, height - (UiResourceLibrary.LayoutHeight - bar.Frame.Y), 1f, false);
            renderer.DrawMenu(bar, placement, includeButtons: false, border: false, frameWidth: width);
            renderer.DrawWindowBorder(0, placement.Y + LicenceBarTop, width, LicenceBarHeight, 1f, fadeLength: LicenceBorderFade, sourceRow: LicenceBorderRow);
            if (font == null || text.Length == 0) return;
            float scale = HelpTextScale, wide = font.MeasureWidth(text) * scale, room = width - 32f;
            if (wide > room) { scale *= room / wide; wide = room; }
            renderer.DrawText(font, text, MathF.Round((width - wide) * 0.5f),
                MathF.Round(placement.Y + LicenceBarTop + (LicenceBarHeight - font.LineHeight * scale) * 0.5f), scale);
        }

        /// <summary>The help line's scale: the capture's "Select a character to log on with." is 270 px wide at 2558 x 1440.</summary>
        private const float HelpTextScale = 1f;

        /// <summary>
        /// The button of the window taking input under a screen point (rendering-surface pixels), using the same
        /// placements as the drawing; null when the point is over none.
        /// </summary>
        public static (LobbyMenu Menu, int ButtonId)? HitTest(LobbyController lobby, uint width, uint height, float x, float y)
        {
            var menu = lobby.ActiveMenu;
            if (menu == null) return null;
            if (InTitleSpace(menu.Definition.Name))
            {
                float sx = width / TitleSpaceWidth, sy = height / TitleSpaceHeight;
                foreach (var button in menu.Definition.Buttons)
                {
                    float bx = button.X * sx, by = button.Y * sy;
                    if (x >= bx && x < bx + button.Width * sx && y >= by && y < by + button.Height * sy) return (menu, button.ButtonId);
                }
                return null;
            }
            var placement = PlaceWindow(lobby, menu.Definition, Fit(width, height), width, height);
            float s = placement.Scale;
            foreach (var button in menu.Definition.Buttons)
            {
                float bx = placement.X + button.X * s, by = placement.Y + button.Y * s;
                if (x >= bx && x < bx + button.Width * s && y >= by && y < by + button.Height * s) return (menu, button.ButtonId);
            }
            return null;
        }

        /// <summary>
        /// The title menu (<c>loby2win</c>), stretched over the window (<see cref="TitleTransform"/>). The selected button is
        /// its label in the menus' orange (<see cref="StockUiMenuWindow.DrawSelectedImage"/>), as retail's capture shows and as
        /// Config and Back always were; the kind-4 images Select / Create / Delete Character carry (a blue caption) are not
        /// what retail shows for the selection (the maintainer, 2026-11-13). The cursor keeps its shape (scaled by the height).
        /// </summary>
        private static void DrawTitleMenu(StockUiRenderer renderer, UiResourceLibrary library, LobbyMenu menu, uint width, uint height, long timestamp, bool cursor)
        {
            var definition = menu.Definition;
            float sx = width / TitleSpaceWidth, sy = height / TitleSpaceHeight;
            foreach (var shape in definition.Frame.Shapes)
            {
                if (shape.Kind == 0 && library.TryGetImage(shape, out var frame)) renderer.DrawImage(frame, TitleTransform(width, height));
            }
            foreach (var button in definition.Buttons)
            {
                if (!TryGetShape(library, button, 0, out var label)) continue;
                var transform = Matrix3x2.CreateTranslation(button.X, button.Y) * TitleTransform(width, height);
                bool selected = button.ButtonId == menu.SelectedButtonId;
                foreach (var part in label.Parts)
                {
                    UiColor? tint = !selected || part.BlendMode != UiBlendMode.Alpha ? null
                        : StockUiMenuWindow.IsGlyph(part) ? StockUiMenuWindow.SelectedGlyphTint : StockUiMenuWindow.SelectedCapsuleTint;
                    renderer.DrawPart(part, transform, tint);
                }
            }
            if (cursor && menu.SelectedButton is { } current)
            {
                // The cursor is placed from the button's left edge in screen pixels and drawn at the height's scale.
                var placement = new StockUiPlacement(current.X * (sx - sy), definition.Frame.Y * sy, sy, false);
                StockUiMenuWindow.DrawMenuCursor(renderer, library, definition.Frame, current, placement, timestamp);
            }
        }

        /// <summary>
        /// The expansion banners down the title screen's right side (<see cref="ExpansionBanners"/>), stretched with the
        /// title; those the account lacks are dimmed. With the account's expansions unknown every banner is lit.
        /// </summary>
        private static void DrawExpansionBanners(StockUiRenderer renderer, UiResourceLibrary library, uint? expansions, uint width, uint height)
        {
            if (!library.TryGetMenu(LobbyController.TitleBackgroundMenu, out var title)) return;
            string group = string.Empty;
            foreach (var shape in title.Frame.Shapes)
            {
                if (shape.Kind == 0) { group = shape.GroupId; break; }
            }
            if (group.Length == 0) return;
            var transform = TitleTransform(width, height);
            for (int i = 0; i < ExpansionBanners.Length; i++)
            {
                if (!library.TryGetImage(new UiShapeReference(0, (ushort)ExpansionBanners[i], group), out var banner)) continue;
                bool owned = expansions is not { } mask || (mask & (1u << (i + 1))) != 0;
                renderer.DrawImage(banner, transform, owned ? null : MissingExpansionTint);
            }
        }

        private static bool TryGetShape(UiResourceLibrary library, UiMenuButton button, int kind, out UiImage image)
        {
            foreach (var shape in button.Shapes)
            {
                if (shape.Kind == kind && library.TryGetImage(shape, out image)) return true;
            }
            image = null!;
            return false;
        }

        /// <summary>Retail draws a renamed-required character's name yellow (XiPackets ResponseChrInfo2 <c>renamef</c>).</summary>
        private static readonly UiColor RenameColor = new(0x80, 0x80, 0x30, 0x80);

        /// <summary>
        /// The 16-slot list (dbnamese): the frame's dim bars, the selected slot's light bar (lobbywin #77), each
        /// character's name in its bar, the race-change star (lobbywin #235) and the selected character's details under the
        /// preview.
        /// </summary>
        private static void DrawCharacterList(StockUiRenderer renderer, UiResourceLibrary library, UiFont? font, LobbyController lobby, LobbyMenu list,
            LobbyRect rect, long timestamp, bool cursor)
        {
            var definition = list.Definition;
            var placement = Place(definition, rect);
            renderer.DrawMenu(definition, placement, includeButtons: false, border: false);
            float s = placement.Scale;
            library.TryGetGroup("lobbywin", out var lobbyGroup);
            foreach (var button in definition.Buttons)
            {
                float bx = placement.X + button.X * s, by = placement.Y + button.Y * s;
                if (button.ButtonId == list.SelectedButtonId && TryGetShape(library, button, 0, out var bar)) renderer.DrawImage(bar, bx, by, s);
                if (font == null || lobby.CharacterInSlot(button.ButtonId) is not { } character) continue;
                float textScale = s * 0.875f;
                float ty = by + (button.Height * s - font.LineHeight * textScale) * 0.5f;
                renderer.DrawText(font, character.Name, bx + 6 * s, ty, textScale, character.RenameRequired ? RenameColor : null);
                if (character.RaceChangeAvailable && lobbyGroup != null && lobbyGroup.Images.Count > 235)
                {
                    renderer.DrawImage(lobbyGroup.Images[235], bx + (button.Width - 14) * s, by + 2 * s, s);
                }
            }
            if (cursor && list.SelectedButton is { } current) StockUiMenuWindow.DrawMenuCursor(renderer, library, definition.Frame, current, placement, timestamp);

            if (font != null && lobby.CharacterInSlot(list.SelectedButtonId) is { } selected)
            {
                // Details under the preview (client text; retail's exact layout is not captured yet).
                float ls = rect.Height / 480f;
                float x = rect.X + 40 * ls, y = rect.Y + 150 * ls, scale = ls * 0.875f;
                foreach (string line in DescribeCharacter(selected))
                {
                    renderer.DrawText(font, line, x, y, scale);
                    y += font.LineHeight * scale + 2 * ls;
                }
            }
        }

        /// <summary>
        /// Character creation: the step's window at the top left (labels from the in-game <c>windowps</c> set), the large
        /// race caption (<c>race1</c>-<c>race8</c>) and, once chosen, the nation flag (<c>nation1</c>-<c>nation3</c>) at the
        /// top right, the nation's title and description on the nation step, and the name / world fields with what is
        /// typed and chosen.
        /// </summary>
        private static void DrawCreation(StockUiRenderer renderer, UiResourceLibrary library, UiFont? font, LobbyController lobby, LobbyRect rect,
            long timestamp, bool cursor)
        {
            var creation = lobby.PreviewCreation;
            var step = lobby.CreationStep;
            bool showFlag = lobby.HasChosenNation || step == LobbyCreationStep.Nation;
            if (!lobby.IsRenaming && !showFlag && library.TryGetMenu($"race{Math.Clamp((int)creation.Race, 1, 8)}", out var raceCaption))
            {
                renderer.DrawMenu(raceCaption, Place(raceCaption, rect), includeButtons: false, border: false);
            }
            if (showFlag && !lobby.IsRenaming && library.TryGetMenu($"nation{creation.Nation + 1}", out var flag))
            {
                renderer.DrawMenu(flag, Place(flag, rect), includeButtons: false, border: false);
            }

            if (step <= LobbyCreationStep.Nation && lobby.CreationMenu is { } choices)
            {
                DrawChoiceMenu(renderer, library, choices, rect, timestamp, cursor);
                if (step == LobbyCreationStep.Nation && font != null) DrawNationText(renderer, font, lobby, creation.Nation, rect);
                return;
            }

            // Name and world: the fields stack at the top left ("Name—" at y 32, "World—" at y 60), the world list below.
            if (font != null && library.TryGetMenu(LobbyController.NameField, out var nameField))
            {
                var placement = Place(nameField, rect);
                renderer.DrawMenu(nameField, placement, includeButtons: false, border: false);
                float s = placement.Scale;
                string name = lobby.NameText;
                float tx = placement.X + 24 * s, ty = placement.Y + (26 * s - font.LineHeight * s) * 0.5f + 2 * s;
                renderer.DrawText(font, name, tx, ty, s);
                if (step == LobbyCreationStep.Name && (timestamp / (System.Diagnostics.Stopwatch.Frequency / 2)) % 2 == 0)
                {
                    // A 1-px caret after the letters typed before it.
                    float caretX = tx + font.MeasureWidth(name.AsSpan(0, Math.Min(lobby.NameCaret, name.Length))) * s;
                    renderer.DrawTextureRect("gauge", 1, 1, 1, 1, caretX, ty, Math.Max(1, s), font.LineHeight * s, new UiColor(0x80, 0x80, 0x80, 0x80));
                }
            }
            if (step == LobbyCreationStep.World && font != null && library.TryGetMenu(LobbyController.WorldField, out var worldField))
            {
                var placement = Place(worldField, rect);
                renderer.DrawMenu(worldField, placement, includeButtons: false, border: false);
                int row = (lobby.CreationMenu?.SelectedButtonId ?? 0) - 1;
                if (row >= 0 && row < lobby.Worlds.Count)
                {
                    float s = placement.Scale;
                    renderer.DrawText(font, lobby.Worlds[row].Name, placement.X + 24 * s, placement.Y + (26 * s - font.LineHeight * s) * 0.5f + 2 * s, s);
                }
                if (lobby.CreationMenu is { } worlds) DrawWorldList(renderer, library, font, lobby, worlds, rect, timestamp, cursor);
            }
        }

        /// <summary>A creation step's window: the frame, every choice's label (the selected one tinted) and the cursor; the page arrows are not drawn.</summary>
        private static void DrawChoiceMenu(StockUiRenderer renderer, UiResourceLibrary library, LobbyMenu menu, LobbyRect rect, long timestamp, bool cursor)
        {
            var definition = menu.Definition;
            var placement = Place(definition, rect);
            renderer.DrawMenu(definition, placement, includeButtons: false, opaqueBody: true);
            float s = placement.Scale;
            foreach (var button in definition.Buttons)
            {
                if (button.X < 0 || button.X >= definition.Frame.Width) continue; // the red page arrows outside the window
                if (!TryGetShape(library, button, 0, out var label)) continue;
                float bx = placement.X + button.X * s, by = placement.Y + button.Y * s;
                if (button.ButtonId == menu.SelectedButtonId) StockUiMenuWindow.DrawSelectedImage(renderer, label, bx, by, s);
                else renderer.DrawImage(label, bx, by, s);
            }
            if (cursor && menu.SelectedButton is { } current) StockUiMenuWindow.DrawMenuCursor(renderer, library, definition.Frame, current, placement, timestamp);
        }

        /// <summary>The nation step's text: the nation's name and description from ROM/165/71, in the right half of the screen.</summary>
        private static void DrawNationText(StockUiRenderer renderer, UiFont font, LobbyController lobby, int nation, LobbyRect rect)
        {
            var text = lobby.Text;
            if (text == null) return;
            int titleRow = nation switch { 1 => Gordian.Core.Resources.Tables.LobbyTextTables.BastokTitle, 2 => Gordian.Core.Resources.Tables.LobbyTextTables.WindurstTitle, _ => Gordian.Core.Resources.Tables.LobbyTextTables.SandoriaTitle };
            float s = rect.Height / 480f, scale = s * 0.7f;
            float x = rect.X + 380 * s, y = rect.Y + 96 * s;
            renderer.DrawText(font, text.Status(titleRow), x, y, s);
            y += font.LineHeight * s + 8 * s;
            foreach (string line in text.Status(titleRow + 1).Split('\n'))
            {
                renderer.DrawText(font, line, x, y, scale);
                y += font.LineHeight * scale + 2 * s;
            }
        }

        /// <summary>The world list (<c>worldsel</c>): a row per world with its name, the selected row's bar (its kind-1 image) behind it.</summary>
        private static void DrawWorldList(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, LobbyController lobby, LobbyMenu menu,
            LobbyRect rect, long timestamp, bool cursor)
        {
            var definition = menu.Definition;
            var placement = Place(definition, rect);
            renderer.DrawMenu(definition, placement, includeButtons: false, opaqueBody: true);
            float s = placement.Scale;
            var worlds = lobby.Worlds;
            foreach (var button in definition.Buttons)
            {
                int index = button.ButtonId - 1;
                if (index < 0 || index >= worlds.Count) continue;
                float bx = placement.X + button.X * s, by = placement.Y + button.Y * s;
                if (button.ButtonId == menu.SelectedButtonId && TryGetShape(library, button, 1, out var bar)) renderer.DrawImage(bar, bx, by, s);
                renderer.DrawText(font, worlds[index].Name, bx + 8 * s, by + (button.Height * s - font.LineHeight * s) * 0.5f, s);
            }
            if (cursor && menu.SelectedButton is { } current) StockUiMenuWindow.DrawMenuCursor(renderer, library, definition.Frame, current, placement, timestamp);
        }

        private static readonly string[] JobAbbreviations =
        {
            "", "WAR", "MNK", "WHM", "BLM", "RDM", "THF", "PLD", "DRK", "BST", "BRD", "RNG", "SAM", "NIN", "DRG", "SMN", "BLU",
            "COR", "PUP", "DNC", "SCH", "GEO", "RUN",
        };

        /// <summary>The selected character's lines: the name and the main job and level (sub job when set).</summary>
        public static IReadOnlyList<string> DescribeCharacter(LobbyCharacter character)
        {
            var lines = new List<string> { character.Name };
            string main = character.MainJob < JobAbbreviations.Length ? JobAbbreviations[character.MainJob] : string.Empty;
            if (main.Length > 0)
            {
                string sub = character.SubJob != 0 && character.SubJob < JobAbbreviations.Length ? "/" + JobAbbreviations[character.SubJob] : string.Empty;
                lines.Add($"{main}{character.MainJobLevel}{sub}");
            }
            return lines;
        }

        /// <summary>A message window: its frame, the lines centred near the top, its buttons and the cursor.</summary>
        private static void DrawPrompt(StockUiRenderer renderer, UiResourceLibrary library, UiFont? font, StockUiLogFont? logFont, LobbyController lobby, LobbyPrompt prompt, LobbyRect rect,
            uint width, uint height, long timestamp)
        {
            var definition = prompt.Menu.Definition;
            var placement = PlaceWindow(lobby, definition, rect, width, height);
            bool licence = IsLicence(definition);
            renderer.DrawMenu(definition, placement, includeButtons: false, border: false);
            // Retail draws the licence window with the in-game windows' light top and bottom border lines (image-8): 3 px
            // lines on its top and bottom rows across its 512 px, with a 24 px linear fade at each end. The prompt's parts
            // run from -256 (its origin is the layout's centre column), so the lines start 256 px left of the placement;
            // DrawMenu's own would start at the origin (round-3 test: a short line from the middle past the right edge).
            if (licence)
                renderer.DrawWindowBorder(placement.X - 256 * placement.Scale, placement.Y, UiResourceLibrary.LayoutWidth, definition.Frame.Height,
                    placement.Scale, fadeLength: LicenceBorderFade, sourceRow: LicenceBorderRow);
            float s = placement.Scale;
            foreach (var button in definition.Buttons)
            {
                float bx = placement.X + button.X * s, by = placement.Y + button.Y * s;
                if (!TryGetShape(library, button, 0, out var label)) continue;
                if (button.ButtonId == prompt.Menu.SelectedButtonId && prompt.HasButtons) StockUiMenuWindow.DrawSelectedImage(renderer, label, bx, by, s);
                else renderer.DrawImage(label, bx, by, s);
            }
            if (prompt.Lines.Count > 0 && licence && (logFont != null || font != null))
            {
                // Retail's licence page (image-8): upright log-font text (not the menus' italic font), the lines
                // left-aligned 28 px in from the window's left edge, from 13 px down, 16 px apart, one screen pixel per pixel.
                float ly = placement.Y + LicenceTextTop * s;
                foreach (string line in prompt.Lines)
                {
                    float lx = placement.X + (LicenceTextLeft - 256) * s;
                    if (logFont != null) logFont.Draw(renderer, line, lx, ly, s, LicenceTextColor);
                    else renderer.DrawText(font!, line, lx, ly, s);
                    ly += LicenceLinePitch * s;
                }
            }
            else if (font != null && prompt.Lines.Count > 0)
            {
                // Long notices (the six-line licence page) are set a little smaller so they clear the buttons.
                float textScale = prompt.Lines.Count > 4 ? s * 0.85f : s;
                float lineHeight = font.LineHeight * textScale + 2 * s;
                // The prompts span x -256..256 from their origin; text from 16 px down, each line centred.
                float y = placement.Y + 16 * s;
                foreach (string line in prompt.Lines)
                {
                    float w = font.MeasureWidth(line) * textScale;
                    renderer.DrawText(font, line, placement.X - w * 0.5f, y, textScale);
                    y += lineHeight;
                }
            }
            if (prompt.HasButtons && prompt.Menu.SelectedButton is { } current)
            {
                StockUiMenuWindow.DrawMenuCursor(renderer, library, definition.Frame, current, placement, timestamp);
            }
        }

        /// <summary>
        /// The preview camera: looking straight along -Z at a model standing at the origin and facing +Z, so its feet land
        /// at <paramref name="feetY"/> and 2.4 yalms above them at <paramref name="topY"/>, its centre at <paramref name="centerX"/>.
        /// </summary>
        public static (Vector3 Eye, Vector3 Target, float FieldOfView) PreviewCamera(float centerX, float feetY, float topY, uint width, uint height)
        {
            const float fov = 30f * MathF.PI / 180f, frameHeight = 2.4f;
            float tan = MathF.Tan(fov * 0.5f);
            float aspect = width / (float)Math.Max(1u, height);
            float ndcFeet = 1f - 2f * feetY / height, ndcTop = 1f - 2f * topY / height;
            float ndcX = 2f * centerX / width - 1f;
            float distance = frameHeight / (tan * Math.Max(0.05f, ndcTop - ndcFeet));
            float eyeHeight = -ndcFeet * distance * tan;
            float sideways = -ndcX * distance * tan * aspect;
            var eye = new Vector3(sideways, eyeHeight, distance);
            return (eye, new Vector3(sideways, eyeHeight, 0f), fov);
        }
    }
}
