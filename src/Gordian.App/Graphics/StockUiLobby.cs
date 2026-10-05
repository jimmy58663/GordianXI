// src/Gordian.App/Graphics/StockUiLobby.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Gordian.Core.Network.LandSandBoat;
using Gordian.Core.Resources.Models;
using Gordian.Core.Resources.Ui;
using Gordian.Core.Ui;
using Gordian.Core.Ui.Lobby;

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

        /// <summary>
        /// The help bar (lobyhelp: frame (256, 224), bottom-left anchored, its one image spanning x -256..384, y 155..181,
        /// 640 wide): drawn in the 640 space across the 4:3 area and kept at its authored distance from the bottom in the
        /// 448-high layout, below the title's copyright line.
        /// </summary>
        public static StockUiPlacement PlaceHelpBar(UiMenuDefinition menu, LobbyRect rect)
        {
            float s = rect.Height / 480f, s448 = rect.Height / UiResourceLibrary.LayoutHeight;
            float barTop = rect.Y + rect.Height - (UiResourceLibrary.LayoutHeight - menu.Frame.Y - 155) * s448;
            return new StockUiPlacement(rect.X + menu.Frame.X * s, barTop - 155 * s, s, false);
        }

        /// <summary>The list screen's preview area (640-space x 0-296, feet at y 372 above the help bar): where the model stands, in screen pixels.</summary>
        public static (float CenterX, float FeetY, float TopY) PreviewArea(LobbyRect rect)
        {
            float s = rect.Height / 480f;
            return (rect.X + 150 * s, rect.Y + 372 * s, rect.Y + 160 * s);
        }

        /// <summary>The backgrounds: the title art on the main menu, the list screen's backdrop elsewhere.</summary>
        public static void DrawBackground(StockUiRenderer renderer, LobbyController lobby, uint width, uint height)
        {
            var library = lobby.Library;
            if (library == null) return;
            var rect = Fit(width, height);
            lock (lobby.SyncRoot)
            {
                string background = lobby.IsLicencePending ? LicenceBackgroundMenu
                    : lobby.Screen == LobbyScreen.MainMenu ? LobbyController.TitleBackgroundMenu : LobbyController.ListBackgroundMenu;
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
                        if (shape.Kind != 0 || !library.TryGetImage(shape, out var image)) continue;
                        var fills = new UiImage { Parts = image.Parts.Where(IsFill).ToList() };
                        if (fills.Parts.Count > 0) renderer.DrawImage(fills, stretch);
                    }
                    foreach (var shape in menu.Frame.Shapes)
                    {
                        if (shape.Kind != 0 || !library.TryGetImage(shape, out var art)) continue;
                        foreach (var part in art.Parts)
                        {
                            if (!IsFill(part)) renderer.DrawPart(part, placement.X, placement.Y, placement.Scale);
                        }
                    }
                }
            }
        }

        /// <summary>The licence page's backdrop (lobbywin #85: the 640 x 480 fill and the large logo at the top left).</summary>
        public const string LicenceBackgroundMenu = "ptcbgwin";

        /// <summary>The licence text's layout in its window, measured on retail's image-8 (2559 x 1439, 2026-10-05).</summary>
        private const float LicenceTextLeft = 28, LicenceTextTop = 13, LicenceLinePitch = 16;

        /// <summary>The licence page's help bar band in its frame (lobyhelp's image, y 155..181), where its border lines go.</summary>
        private const float LicenceBarTop = 155, LicenceBarHeight = 26;
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
                        if (lobby.MainMenu != null && !lobby.IsLicencePending) DrawButtonMenu(renderer, library, lobby.MainMenu, rect, timestamp, cursor: !promptOpen);
                        break;
                    case LobbyScreen.CharacterList:
                        if (lobby.CharacterList != null) DrawCharacterList(renderer, library, font, lobby, lobby.CharacterList, rect, timestamp, cursor: !promptOpen);
                        break;
                }

                if (lobby.IsLicencePending && library.TryGetMenu(LobbyController.HelpBarMenu, out var licenceBar))
                {
                    // Retail's licence page: the empty help bar at one screen pixel per layout pixel, its authored 69 px
                    // above the window's bottom, across the full width. Its image is a backdrop-coloured band, so the
                    // client's border lines mark it (image-8: 3 px lines at its top and bottom, 26 px apart, fading at
                    // the window's ends).
                    var bar = new StockUiPlacement(256, height - (UiResourceLibrary.LayoutHeight - licenceBar.Frame.Y), 1f, false);
                    renderer.DrawMenu(licenceBar, bar, includeButtons: false, border: false, frameWidth: width);
                    renderer.DrawWindowBorder(0, bar.Y + LicenceBarTop, width, LicenceBarHeight, 1f);
                }
                else if (font != null && lobby.HelpText.Length > 0 && library.TryGetMenu(LobbyController.HelpBarMenu, out var help))
                {
                    var placement = PlaceHelpBar(help, rect);
                    // The bar runs the window's full width; its text starts at the 4:3 area's left edge.
                    var bar = placement with { X = 256 * placement.Scale };
                    renderer.DrawMenu(help, bar, includeButtons: false, border: false, frameWidth: width / placement.Scale);
                    // The bar (lobbywin #2) spans x -256..384, y 155..181 from the frame origin; text inset 16, centred on the bar.
                    float textScale = placement.Scale * 0.875f;
                    renderer.DrawText(font, lobby.HelpText, placement.X + (-256 + 16) * placement.Scale,
                        placement.Y + 168 * placement.Scale - font.LineHeight * textScale * 0.5f, textScale);
                }

                if (lobby.Prompt is { } prompt) DrawPrompt(renderer, library, font, logFont, lobby, prompt, rect, width, height, timestamp);
            }
        }

        /// <summary>
        /// The button of the window taking input under a screen point (rendering-surface pixels), using the same
        /// placements as the drawing; null when the point is over none.
        /// </summary>
        public static (LobbyMenu Menu, int ButtonId)? HitTest(LobbyController lobby, uint width, uint height, float x, float y)
        {
            var menu = lobby.ActiveMenu;
            if (menu == null) return null;
            var placement = PlaceWindow(lobby, menu.Definition, Fit(width, height), width, height);
            float s = placement.Scale;
            foreach (var button in menu.Definition.Buttons)
            {
                float bx = placement.X + button.X * s, by = placement.Y + button.Y * s;
                if (x >= bx && x < bx + button.Width * s && y >= by && y < by + button.Height * s) return (menu, button.ButtonId);
            }
            return null;
        }

        /// <summary>A window whose buttons are DAT label sprites; the selected one shows its kind-4 (highlighted) image when it has one.</summary>
        private static void DrawButtonMenu(StockUiRenderer renderer, UiResourceLibrary library, LobbyMenu menu, LobbyRect rect, long timestamp, bool cursor)
        {
            var definition = menu.Definition;
            var placement = Place(definition, rect);
            renderer.DrawMenu(definition, placement, includeButtons: false, border: false);
            float s = placement.Scale;
            foreach (var button in definition.Buttons)
            {
                bool selected = button.ButtonId == menu.SelectedButtonId;
                float bx = placement.X + button.X * s, by = placement.Y + button.Y * s;
                if (selected && TryGetShape(library, button, 4, out var highlighted))
                {
                    renderer.DrawImage(highlighted, bx, by, s);
                }
                else if (TryGetShape(library, button, 0, out var label))
                {
                    if (selected) StockUiMenuWindow.DrawSelectedImage(renderer, label, bx, by, s);
                    else renderer.DrawImage(label, bx, by, s);
                }
            }
            if (cursor && menu.SelectedButton is { } current) StockUiMenuWindow.DrawMenuCursor(renderer, library, definition.Frame, current, placement, timestamp);
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
            // Retail draws the licence window with the in-game windows' light top and bottom border lines (image-8).
            renderer.DrawMenu(definition, placement, includeButtons: false, border: licence);
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
