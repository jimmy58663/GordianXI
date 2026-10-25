// src/Gordian.App/Graphics/StockUiHud.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Gordian.Core.Diagnostics;
using Gordian.Core.Network;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Models;
using Gordian.Core.Resources.Ui;
using Gordian.Engine.Sessions;
using Gordian.Engine.Ui;
using Gordian.Core.World;
using NeoVeldrid;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// Composes the stock 2D HUD each frame: decides which persistent windows show, places them through the
    /// session's <see cref="StockUiLayout"/> (shared with <c>/uilayout</c>), and draws them with a
    /// <see cref="StockUiRenderer"/>.
    /// UI resources load once in the background; the HUD draws nothing until they are ready.
    /// </summary>
    public sealed class StockUiHud
    {
        private volatile UiResourceLibrary? _library;
        private volatile UiFont? _font;
        private volatile StatusIconLibrary? _statusIcons;
        private volatile StockUiLogFont? _logFont;
        private int _loadStarted;
        private ResourceManager? _resources;
        private int _skinReloading;
        // ResourceManager.CacheGeneration the current library was read under; a change (a VFS reload) reloads it.
        private int _libraryGeneration;

        /// <summary>The layout used for the last frame (the session's shared layout, see StockUiLayoutStore).</summary>
        public StockUiLayout Layout { get; private set; } = new();

        /// <summary>The session's config-menu settings (log window lines, party icons...).</summary>
        public StockUiSettings Settings { get; private set; } = new();

        /// <summary>Master switch for the whole stock HUD.</summary>
        public bool Enabled { get; set; } = true;

        public UiResourceLibrary? Library => _library;

        /// <summary>The unlocked-UI drag state used for the last frame (the session's, see PlayerActionService.UiDrag).</summary>
        public StockUiDragController Drag { get; private set; } = new();

        /// <summary>
        /// Whether the last frame drew the hover pointer (the pointer over a clickable menu entry), in which case the
        /// viewport hides the system cursor; otherwise the system cursor is the arrow.
        /// </summary>
        public bool PointerDrawn { get; private set; }

        /// <summary>The zoning overlay's opacity (0 clear, 1 black), drawn over the scene and the interface (<see cref="Gordian.Engine.Ui.ZoneLoadingScreen"/>).</summary>
        public float LoadingOpacity { get; set; }

        /// <summary>Whether the view counts as loading this frame, which shows retail's "Downloading data ..." over the black (<see cref="ZoneLoadingIndicator"/>).</summary>
        public bool LoadingIndicatorVisible { get; set; }

        /// <summary>Whether the view is zoning (fading out or black): the interface and name plates are not drawn.</summary>
        public bool InterfaceHidden { get; set; }

        private long _loadingIndicatorSince;

        /// <summary>The stock elements hidden at run time this frame (<see cref="StockUiVisibilityState"/>, #29).</summary>
        private StockUiElement _hiddenElements;

        /// <summary>Whether the character is in a party this frame: the party window then counts as Party, else as Vitals.</summary>
        private bool _inParty;

        /// <summary>
        /// Starts loading the UI resources in the background (idempotent).
        /// </summary>
        public void EnsureLoading(ResourceManager? resources)
        {
            if (resources == null || Interlocked.CompareExchange(ref _loadStarted, 1, 0) != 0) return;
            _resources = resources;
            int generation = resources.CacheGeneration;
            Task.Run(() =>
            {
                try
                {
                    var library = UiResourceLibrary.Load(resources, Layout.WindowSkin);
                    if (library == null) return;
                    _font = UiFont.FromLibrary(library);
                    _statusIcons = StatusIconLibrary.Load(resources);
                    _logFont = StockUiLogFont.FromLibrary(library);
                    // Item descriptions wrap with the log font they are drawn in (StockUiTextLayout).
                    if (_logFont is { } wrapFont) StockUiItemDescription.FontMeasure = StockUiTextBoxes.Measure(_font!, wrapFont);
                    _libraryGeneration = generation;
                    _library = library;
                }
                catch (Exception ex)
                {
                    GordianLog.Error("UI", $"Failed to load stock UI resources: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Reloads the UI resources in the background when the layout's window skin (1-8) changed (the config menu's
        /// "Window Type", or <c>/uilayout skin</c>) or the resource caches were cleared by a VFS reload; the current
        /// library draws until the new one is ready. A new library makes the renderer drop its uploaded textures.
        /// </summary>
        private void EnsureWindowSkin(UiResourceLibrary current, int skin)
        {
            var resources = _resources;
            if (resources == null) return;
            int generation = resources.CacheGeneration;
            bool reloaded = generation != _libraryGeneration;
            if ((current.WindowSkin == skin && !reloaded) || Interlocked.CompareExchange(ref _skinReloading, 1, 0) != 0) return;
            Task.Run(() =>
            {
                try
                {
                    var library = UiResourceLibrary.Load(resources, skin);
                    if (library == null) return;
                    _font = UiFont.FromLibrary(library);
                    _logFont = StockUiLogFont.FromLibrary(library);
                    // Item descriptions wrap with the log font they are drawn in (StockUiTextLayout).
                    if (_logFont is { } wrapFont) StockUiItemDescription.FontMeasure = StockUiTextBoxes.Measure(_font!, wrapFont);
                    if (reloaded) _statusIcons = StatusIconLibrary.Load(resources);
                    _library = library;
                }
                catch (Exception ex)
                {
                    GordianLog.Error("UI", $"Failed to load window skin {skin}: {ex.Message}");
                }
                finally
                {
                    // Also on failure: a reload that cannot read the UI DATs keeps the current library, not retry each frame.
                    _libraryGeneration = generation;
                    Interlocked.Exchange(ref _skinReloading, 0);
                }
            });
        }

        /// <summary>
        /// Draws the HUD for a session over the framebuffer's current contents. <paramref name="targetCursor"/> is the
        /// screen point of the target's overhead point, when the target is on screen; <paramref name="namePlates"/> are
        /// the overhead points of the entities drawn this frame, where their names go. The target cursor sits above the
        /// target's name, or on the overhead point when it has none.
        /// </summary>
        public void Render(StockUiRenderer renderer, CharacterSession? session, Framebuffer framebuffer, uint width, uint height,
            Vector2? targetCursor = null, IReadOnlyList<NamePlateAnchor>? namePlates = null, Vector2? subTargetCursor = null)
        {
            var library = _library;
            if (!Enabled || library == null || session == null)
            {
                PointerDrawn = false;
                return;
            }
            _dialogWaiting = session.Events.IsWaitingForConfirm;
            Layout = session.ActionService.UiLayout;
            Settings = session.ActionService.UiSettings;
            Drag = session.ActionService.UiDrag;
            EnsureWindowSkin(library, Layout.WindowSkin);
            Drag.BeginFrame(Layout, width, height);

            var menus = session.ActionService.Menus;
            if (!ReferenceEquals(menus.Library, library)) menus.Library = library;
            renderer.HintLabel = menus.HintLabel;
            renderer.HintFont = _font;
            if (menus.ConfigRowText == null && _resources is { } strings)
            {
                // The list pages' row text comes from the config row table (ROM/165/74).
                menus.ConfigRowText = i => strings.TryGetString(DMsgCategory.MenuConfigRows, i, out var text) ? text : null;
                menus.MenuHelpText = i => strings.TryGetString(DMsgCategory.MenuHelp, i, out var help) ? help : null;
                // The menus' own texts (ROM/165/72): "Examine %s?", "Remove piece.", the equipment set prompts (#91, #92).
                menus.MenuMessageText = i => strings.TryGetString(DMsgCategory.MenuMessages, i, out var text) ? text : null;
            }
            if (session.ActionService.MountName == null && _resources is { } mountStrings)
            {
                // The mount list and /mount (#87): names and help lines by mount id (ROM/351/84, 85).
                session.ActionService.MountName = i => mountStrings.TryGetString(DMsgCategory.MountNames, i, out var name) ? name : null;
                session.ActionService.MountHelp = i => mountStrings.TryGetString(DMsgCategory.MountHelp, i, out var help) ? help : null;
            }
            if (_resources is { } game && session.Chat.GameDirectory() == null)
            {
                // /importretail reads the install's USER folder (read-only).
                string directory = game.GameDirectory;
                session.Chat.GameDirectory = () => directory;
            }
            if (menus.ItemLookup == null && _resources is { } items)
            {
                // The shop windows' names, stack sizes, icons and descriptions come from the item DATs.
                menus.ItemLookup = id => items.TryGetItem(id, out var record) ? record : null;
                Gordian.Engine.Ui.StandardMessages.ItemLookup ??= menus.ItemLookup;
            }
            if (menus.KeyItemCatalog == null && _resources is { } keyItems)
            {
                // The Key Items window's categories, names and descriptions (ROM/175/35, #91), read once when first opened.
                var catalog = new Lazy<StockUiKeyItemCatalog?>(() => StockUiKeyItemCatalog.FromTable(keyItems.GetDMsgTable(DMsgCategory.KeyItems)));
                menus.KeyItemCatalog = () => catalog.Value;
            }

            renderer.Begin(library);
            // An event's screen fades (#165): the 3D scene's under the interface (and its 0x72 flashes, #192), then the interface's own.
            var presentation = session.Events.Presentation;
            renderer.DrawScreenTint(width, height, presentation.SceneColor);
            renderer.DrawScreenFlash(width, height, presentation.SceneFlash);
            renderer.Opacity = presentation.InterfaceOpacity;
            var groups = GroupParty(session);
            // Elements an addon, an ImGui replacement or /stockui hides this frame (#29); hidden like a /uilayout hide.
            _hiddenElements = session.ActionService.UiVisibility.Hidden;
            _inParty = groups.InGroup;
            // An event's message mode (opcode 0x67) hides the HUD windows and shows the event's lines on the screen instead; name plates stay (the retail recording shows them through the aerial shots).
            bool cutscene = session.Events.IsCutsceneHud;
            NamePlateBounds? targetPlate = null, subTargetPlate = null;
            // Zoning (#350): retail closes the interface and the name plates the moment the player crosses, before the scene
            // fades, and opens them again as the next zone starts to fade in (the 2026-10-11 retail recording).
            bool zoning = InterfaceHidden;
            if (namePlates != null && _font is { } plateFont && !zoning && (_hiddenElements & StockUiElement.NamePlates) == 0)
            {
                var ownParty = new List<uint>();
                foreach (var member in groups.Own) ownParty.Add(member.ServerId);
                // The plates belong to the scene: they fade to black with it (the fdo? / fdi? fades) and come back with it.
                var scene = presentation.SceneColor;
                renderer.Opacity = presentation.InterfaceOpacity * Math.Clamp((scene.X + scene.Y + scene.Z) / 3f, 0f, 1f);
                targetPlate = StockUiNamePlates.Draw(renderer, library, plateFont, session, namePlates, ownParty, width, height, out subTargetPlate, Layout.NamePlateScale);
                renderer.Opacity = presentation.InterfaceOpacity;
            }
            long now = Stopwatch.GetTimestamp();
            Vector2? targetTip = null;
            bool cursorsHidden = (_hiddenElements & StockUiElement.TargetCursor) != 0;
            if (targetCursor is { } cursor && session.ActionService.CurrentTarget != null && !cutscene && !zoning && !cursorsHidden)
            {
                targetTip = CursorTip(cursor, targetPlate);
                StockUiTargetWindow.DrawCursor(renderer, library, targetTip.Value, Layout.Scale, now);
            }
            if (subTargetCursor is { } subCursor && session.ActionService.SubTargeting is { } picking && !cutscene && !cursorsHidden)
            {
                // The sub-target cursor (#240): the target cursor in violet; on your own target it sits 12 px above
                // that target's cursor (retail capture 2026-10-11, maintainer: video frames at 2556 x 1434, 1:1).
                var tip = CursorTip(subCursor, subTargetPlate);
                if (picking.Current.ServerId == session.ActionService.CurrentTarget?.ServerId && targetTip is { } shared)
                {
                    tip = shared - new Vector2(0, SubTargetCursorRise * Layout.Scale);
                }
                StockUiTargetWindow.DrawCursor(renderer, library, tip, Layout.Scale, now, SubTargetCursorTint);
            }
            if (zoning)
            {
                // Nothing but the black and the loading sprite below.
            }
            else if (!cutscene)
            {
                var party = DrawPartyWindow(renderer, library, session, groups, width, height);
                DrawAllianceWindows(renderer, library, session, groups, width, height);
                DrawLogWindows(renderer, library, session, party, width, height);
                DrawTargetWindow(renderer, library, session, party?.Placement, width, height);
                DrawStatusIcons(renderer, library, session, width, height);
            }
            else
            {
                // The log stays up while an event line waits for Confirm, even in the event message mode (#369).
                if (session.Events.IsWaitingForConfirm) DrawLogWindows(renderer, library, session, null, width, height);
                if (session.Events.EventText is { } eventText) DrawEventText(renderer, eventText, width, height);
            }
            if (!zoning)
            {
                DrawMenus(renderer, library, session, menus, width, height);
                if (!cutscene && (_hiddenElements & StockUiElement.Menus) == 0) DrawActionHelpBar(renderer, library, session, width, height);
                if (!cutscene) DrawMacros(renderer, library, session, width, height);
                if (!cutscene && (_hiddenElements & StockUiElement.Menus) == 0)
                {
                    DrawPartyHelpBar(renderer, library, session, width, height);
                    DrawAuctionHelpBar(renderer, library, session, width, height);
                    DrawBlueMagicHelpBar(renderer, library, session, width, height);
                    DrawVitals(renderer, library, session, width, height);
                }
            }
            if (session.ActionService.ButtonPreview.IsActive) _buttonPreview.Draw(renderer, _font, session.ActionService.ButtonPreview, Layout.Scale, width, height);
            bool unlocked = Drag.Unlocked;
            if (unlocked)
            {
                var button = StockUiDragOverlay.ResetButtonRect(_font, width, Layout.Scale);
                Drag.RegisterButton(StockUiDragController.ResetPositionsButton, button.X, button.Y, button.Width, button.Height, Layout.Scale);
            }
            Drag.EndFrame();
            if (unlocked) StockUiDragOverlay.Draw(renderer, _font, Drag.Regions, Drag.HoveredWindow, Drag.DraggingWindow);
            PointerDrawn = DrawPointer(renderer, session);
            if (LoadingOpacity > 0f)
            {
                // Zoning (#36): the scene and the interface go black together.
                renderer.Opacity = 1f;
                renderer.DrawScreenTint(width, height, new Vector3(1f - Math.Clamp(LoadingOpacity, 0f, 1f)));
            }
            DrawLoadingIndicator(renderer, library, width, height);
            renderer.End(framebuffer, width, height);
        }

        /// <summary>
        /// Retail's loading indicator (#350) in the bottom-right corner while the view is loading: the menu DAT's own
        /// "Downloading data ..." sprite (<see cref="ZoneLoadingIndicator"/>), its dimmed dot pair cycling, appearing with
        /// the black (<see cref="Gordian.Engine.Ui.ZoneLoadingScreen.IndicatorVisible"/>) and gone as soon as loading ends.
        /// </summary>
        private void DrawLoadingIndicator(StockUiRenderer renderer, UiResourceLibrary library, uint width, uint height)
        {
            if (!LoadingIndicatorVisible)
            {
                _loadingIndicatorSince = 0;
                return;
            }
            long now = Stopwatch.GetTimestamp();
            if (_loadingIndicatorSince == 0) _loadingIndicatorSince = now;
            int index = ZoneLoadingIndicator.ImageAt(Stopwatch.GetElapsedTime(_loadingIndicatorSince, now).TotalSeconds);
            if (!library.TryGetGroup(ZoneLoadingIndicator.GroupName, out var group) || index >= group.Images.Count) return;
            float s = Layout.Scale;
            var (x, y) = ZoneLoadingIndicator.Position(width, height, s);
            renderer.Opacity = 1f;
            renderer.DrawImage(group.Images[index], x, y, s);
            renderer.Opacity = 1f;
        }

        /// <summary>
        /// The help bar over the Magic and Abilities lists (the list's title and the selected action's help line) and over the
        /// sub-target cursor (the action's name and "Select target of spell."; the retail video, 2026-10-11).
        /// </summary>
        private void DrawActionHelpBar(StockUiRenderer renderer, UiResourceLibrary library, CharacterSession session, uint width, uint height)
        {
            if (_font is not { } font) return;
            var actions = session.ActionService;
            if (actions.SubTargeting is { } picking)
            {
                // All three lines confirmed as retail's (the maintainer's round 2 check, 2026-10-12).
                string line = picking.Kind switch
                {
                    Gordian.Engine.Actions.ActionKind.Spell => "Select target of spell.",
                    Gordian.Engine.Actions.ActionKind.Item => "Select target of item.",
                    _ => "Select target of ability.",
                };
                StockUiHelpBar.DrawOnScreen(renderer, library, font, _logFont, picking.Name, line, Layout.Scale, width, height);
                return;
            }
            if (actions.Menus.Suspended || actions.Menus.OpenMenus.LastOrDefault(m => m.IsActionList) is not { } list) return;
            // Set Spells over the Magic list draws its own help bar; only one is ever drawn (round 1, N11, image-19).
            if (actions.Menus.OpenMenus.Any(m => m.IsBlueMagicWindow)) return;
            string title = list.ActionList == StockUiActionList.Spells ? "Magic List" : "Abilities";
            StockUiHelpBar.DrawOnScreen(renderer, library, font, _logFont, title, list.SelectedActionRow?.Help, Layout.Scale, width, height);
        }

        /// <summary>
        /// The help bar over the Party menu (#397, the maintainer's captures 2026-10-16): "Party" or "Party Leader" and the
        /// selected entry's help line.
        /// </summary>
        private void DrawPartyHelpBar(StockUiRenderer renderer, UiResourceLibrary library, CharacterSession session, uint width, uint height)
        {
            var menus = session.ActionService.Menus;
            if (_font is not { } font || menus.Suspended || menus.OpenMenus.LastOrDefault() is not { } top) return;
            if (StockUiParty.TitleOf(top.Name) is not { } title) return;
            string? help = top.SelectedButton is { HelpTextId: >= 0 } button ? menus.MenuHelpText?.Invoke(button.HelpTextId) : null;
            StockUiHelpBar.DrawOnScreen(renderer, library, font, _logFont, title, help, Layout.Scale, width, height);
        }

        /// <summary>
        /// The help bar over the Auction House windows (#90; capture Aucion_House.mp4): the window's title ("Auction", "Bid" or the
        /// category, "Price Set", "Price History") and the selected entry's help line. The Sell list's item list draws its own.
        /// </summary>
        private void DrawAuctionHelpBar(StockUiRenderer renderer, UiResourceLibrary library, CharacterSession session, uint width, uint height)
        {
            var menus = session.ActionService.Menus;
            if (_font is not { } font || menus.Suspended || !menus.IsAuctionOpen || menus.OpenMenus.LastOrDefault() is not { } top) return;
            if (menus.OpenMenus.Any(m => m.ItemListKind == StockUiItemListKind.AuctionSell)) return;
            var prompt = top.IsPrompt && menus.OpenMenus.Count > 1 ? top : null;
            if (StockUiAuctionWindow.HelpBar(prompt != null && !prompt.IsAuctionWindow ? menus.OpenMenus[^2] : top, menus.MenuHelpText) is not { } bar) return;
            StockUiHelpBar.DrawOnScreen(renderer, library, font, _logFont, bar.Title, bar.Help, Layout.Scale, width, height);
        }

        /// <summary>The help bar over Blue Magic's Set Spells windows (#363): "Blue Magic" and the window's help line.</summary>
        private void DrawBlueMagicHelpBar(StockUiRenderer renderer, UiResourceLibrary library, CharacterSession session, uint width, uint height)
        {
            var menus = session.ActionService.Menus;
            if (_font is not { } font || menus.Suspended || menus.OpenMenus.LastOrDefault() is not { } top) return;
            if (StockUiBlueMagicWindow.HelpBar(top, menus.MenuHelpText) is not { } bar) return;
            StockUiHelpBar.DrawOnScreen(renderer, library, font, _logFont, bar.Title, bar.Help, Layout.Scale, width, height);
        }

        /// <summary>
        /// Your own HP / MP / TP block at the top left while engaged (#391). It gives way to any window opened over that corner
        /// (the menus' left-hand lists, the macro editor) and to the macro palette's own room: the palette sits to its right.
        /// </summary>
        private void DrawVitals(StockUiRenderer renderer, UiResourceLibrary library, CharacterSession session, uint width, uint height)
        {
            if (_font is not { } font || session.ActionService.Combat?.IsEngaged != true) return;
            var menus = session.ActionService.Menus;
            if (!menus.Suspended && menus.OpenMenus.Any(m => !m.IsCommandMenu && m.Menu.Frame.Anchor == UiAnchor.TopLeft && m.Menu.Frame.X < 144 && m.Menu.Frame.Y < 92)) return;
            var local = session.LocalPlayer;
            StockUiVitalsWindow.Draw(renderer, library, font, local.CurrentHp, local.MaxHp, local.CurrentMp, local.MaxMp, local.CurrentTp,
                Layout.Scale, width, height, Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency);
        }

        /// <summary>
        /// The macro palette in play while Ctrl / Alt (LT / RT) is held, and the help bar over it and over the macro windows
        /// (#339): "Edit Macros" (or "MacroPalette" in play) and "Macro Set &lt;book&gt; (#&lt;set&gt;)", as the maintainer's retail
        /// captures (2026-10-13) show; over a window whose selected entry has a help line (the book options, Line / Button
        /// Edit), that line.
        /// </summary>
        private void DrawMacros(StockUiRenderer renderer, UiResourceLibrary library, CharacterSession session, uint width, uint height)
        {
            if (_font is not { } font) return;
            var menus = session.ActionService.Menus;
            if (menus.Suspended) return;
            var top = menus.OpenMenus.LastOrDefault(m => m.IsMacroWindow);
            if (top != null)
            {
                string? help = top.SelectedButton is { HelpTextId: >= 0 } button ? menus.MenuHelpText?.Invoke(button.HelpTextId) : null;
                string title = top.MacroView is StockUiMacroView.BookList or StockUiMacroView.BookOptions ? StockUiMacroMenus.PaletteTitle : StockUiMacroMenus.EditTitle;
                if (help == null && top.MacroView is not (StockUiMacroView.BookList or StockUiMacroView.BookOptions)) help = top.MacroSetCaption;
                StockUiHelpBar.DrawOnScreen(renderer, library, font, _logFont, title, help, Layout.Scale, width, height);
                return;
            }
            var macros = session.Macros;
            // The same rule as the input tick: the target command menu alone, which stays up while engaged, does not hide the
            // palette (round 2, A22: this check hid it whenever any menu was open, so never while engaged).
            if (macros.ShownPalette == Gordian.Engine.Macros.MacroPalette.None
                || Gordian.Engine.Macros.MacroController.IsBlockedBy(menus, session.Chat, session.ActionService.SubTargeting != null)) return;
            var settings = session.ActionService.UiSettings;
            bool compact = settings.GetValue(StockUiSettingKey.MacroPaletteSize) == 1;
            // Static: always shifted right; Dynamic: while engaged, when retail draws your own HP / MP / TP bars at its left.
            bool shifted = settings.GetValue(StockUiSettingKey.MacroPalettePosition) == 1 || session.ActionService.Combat?.IsEngaged == true;
            StockUiMacroWindow.DrawPlayPalette(renderer, library, font, macros, compact, shifted, Layout.Scale, width, height);
            var library2 = macros.Library;
            StockUiHelpBar.DrawOnScreen(renderer, library, font, _logFont, StockUiMacroMenus.PaletteTitle,
                $"Macro Set {library2.GetBookTitle(library2.CurrentBook)} (#{library2.CurrentSet + 1})", Layout.Scale, width, height);
        }

        /// <summary>The cursor's tip above the name plate (when the entity has one), else on the overhead point.</summary>
        private static Vector2 CursorTip(Vector2 cursor, NamePlateBounds? plate)
        {
            if (plate is not { } p) return cursor;
            float tip = Math.Min(p.Center.Y - p.GlyphHeight * StockUiNamePlates.CursorGapShare, p.Top);
            return new Vector2(p.Center.X, tip);
        }

        /// <summary>
        /// The sub-target cursor's colour: the gold target arrow turned violet (peak (132, 102, 213) in the maintainer's
        /// retail video, 2026-10-11, against the gold's ~(230, 170, 110)), and how far above a shared target's cursor it sits.
        /// </summary>
        private static readonly UiColor SubTargetCursorTint = new(0x49, 0x4D, 0xF8, 0x80);
        private const float SubTargetCursorRise = 12;

        /// <summary>The screen height the event text positions were measured at (the maintainer's 1440p retail client area).</summary>
        public const float EventTextReferenceHeight = 1416f;

        /// <summary>
        /// An event line in the event message mode (opcode 0x67): white log-font text with a dark shadow, its lines
        /// <see cref="StockUiLogFont.CellHeight"/> apart. Its position is the line's own 0x02 code (else the mode's two
        /// values): the left edge at x, the first line's middle at y, in pixels of a client area
        /// <see cref="EventTextReferenceHeight"/> high, scaled to the screen's height so the text keeps its place near the
        /// top left at any resolution. Measured on the maintainer's retail recording (Southern San d'Oria intro,
        /// 2026-09-30: lines coded x 80, y 340 drawn at x 78, top 332 of a 2544 x 1416 client area at UI scale 1).
        /// </summary>
        private void DrawEventText(StockUiRenderer renderer, Gordian.Engine.Events.EventScreenText text, uint width, uint height)
        {
            var font = _logFont;
            if (font == null || text.Lines.Count == 0) return;
            float s = Layout.Scale;
            float k = height / EventTextReferenceHeight;
            float x = text.X * k;
            float y = text.Y * k - StockUiLogFont.CellHeight * s / 2f;
            var shadow = new UiColor(0, 0, 0, 0x80);
            var white = new UiColor(0x80, 0x80, 0x80, 0x80);
            for (int i = 0; i < text.Lines.Count; i++)
            {
                float lineY = y + i * StockUiLogFont.CellHeight * s;
                font.Draw(renderer, text.Lines[i], x + s, lineY + s, s, shadow);
                font.Draw(renderer, text.Lines[i], x, lineY, s, white);
            }
        }

        /// <summary>
        /// Resolves a persistent window's placement, substituting the transient position while the player drags it
        /// (the layout is only written when the drag ends). <paramref name="moved"/> is true when the window has left
        /// its retail place, by override or by the drag in progress, so the default stacking (target on party, log
        /// stretched to the party window, input line on Window 1) no longer applies.
        /// </summary>
        private StockUiPlacement ResolveWindow(string windowId, UiMenuFrame frame, uint width, uint height, out bool moved)
        {
            var placement = Layout.Resolve(windowId, frame, width, height);
            if (StockUiVisibilityState.HidesWindow(_hiddenElements, windowId, _inParty)) placement = placement with { Hidden = true };
            moved = Layout.HasPositionOverride(windowId);
            if (Drag.TryGetDragPosition(windowId, out float x, out float y))
            {
                placement = placement with { X = x, Y = y };
                moved = true;
            }
            return placement;
        }

        /// <summary>
        /// While unlocked, a window with nothing to show (no target, no status effects, no alliance, closed menu or
        /// input line) is drawn as an empty frame so it can still be placed.
        /// </summary>
        private void DrawPlaceholder(StockUiRenderer renderer, string windowId, UiMenuDefinition menu, StockUiPlacement placement)
        {
            if (!Drag.Unlocked || placement.Hidden) return;
            renderer.DrawMenu(menu, placement, includeButtons: false);
            Drag.Register(windowId, menu.Frame, placement, placeholder: true);
        }

        /// <summary>
        /// Draws the open stock menus, root first. The root (the main menu, or a prompt opened on its own) takes the
        /// layout's placement for the main menu; sub-menus keep their authored place relative to it (they follow a
        /// moved main menu rather than taking overrides of their own). A window whose authored rectangle a later
        /// window covers is not drawn: a sub-menu opened in the same corner replaces its parent, as in retail.
        /// </summary>
        private void DrawMenus(StockUiRenderer renderer, UiResourceLibrary library, CharacterSession session,
            StockUiMenuController menus, uint width, uint height)
        {
            var open = menus.OpenMenus;
            _menuPlacements.Clear();
            // While the sub-target cursor picks the target of an action chosen in the menus they step aside (#240); an exclusive
            // view (the full-screen log) hides them too, and with no placements published the mouse cannot reach them (#339 round 5).
            if (open.Count == 0 || menus.Suspended || session.Chat.HidesMenus)
            {
                menus.SetScreenPlacements(_menuPlacements);
                if (open.Count > 0) return;
                if (Drag.Unlocked && library.TryGetMenu("menuwind", out var closedMenu))
                {
                    DrawPlaceholder(renderer, StockUiWindowIds.MainMenu, closedMenu, ResolveWindow(StockUiWindowIds.MainMenu, closedMenu.Frame, width, height, out _));
                }
                return;
            }
            long timestamp = Stopwatch.GetTimestamp();

            var rootFrame = open[0].Menu.Frame;
            string rootId = open[0].IsQuery ? StockUiWindowIds.Query
                : open[0].IsCommandMenu ? StockUiWindowIds.CommandMenu
                : open[0].IsShopMenu ? StockUiWindowIds.Shop
                : open[0].IsCheck ? StockUiWindowIds.Check
                : open[0].Pinned && open[0].Name.Equals(StockUiDeathMenu.MenuName, StringComparison.OrdinalIgnoreCase) ? StockUiWindowIds.Death
                : StockUiWindowIds.MainMenu;
            var root = ResolveWindow(rootId, rootFrame, width, height, out bool rootMoved);
            if ((open[0].IsCommandMenu || open[0].IsShopMenu) && !rootMoved && _window1Top is { } logTop)
            {
                // Retail keeps the command menu's bottom on Window 1's top edge whatever the log's line count
                // (in-game check 2026-09-28); the DAT's authored place only fits the eight-line window. The shop's
                // Buy / Sell window sits there too, while its item list and the gil, item info and quantity windows
                // under the list keep their authored places at the top (the maintainer's in-game check 2026-09-28).
                float h = rootFrame.Height * root.Scale;
                root = root with { Y = Math.Clamp(logTop - h, 0, Math.Max(0, height - h)) };
            }
            if (root.Hidden)
            {
                menus.SetScreenPlacements(_menuPlacements);
                return;
            }
            var authoredRoot = StockUiLayout.Place(rootFrame.Anchor, rootFrame.X, rootFrame.Y, rootFrame.Width, rootFrame.Height, root.Scale, width, height);
            float dx = root.X - authoredRoot.X, dy = root.Y - authoredRoot.Y;

            for (int i = 0; i < open.Count; i++)
            {
                var menu = open[i];
                bool covered = false;
                for (int j = i + 1; j < open.Count && !covered; j++) covered = open[j].OverlapsAuthored(menu);
                // A main-menu spell list puts its Options window where the Magic categories were (retail image-16).
                for (int j = i + 1; j < open.Count && !covered; j++) covered = open[j].ShowsActionOptions && menu.Name.Equals(StockUiActionMenu.MagicCategoryMenu, StringComparison.OrdinalIgnoreCase);
                if (covered) continue;

                StockUiPlacement placement;
                if (i == 0)
                {
                    placement = root;
                    Drag.Register(rootId, rootFrame, root);
                }
                else
                {
                    var frame = menu.Menu.Frame;
                    var authored = StockUiLayout.Place(frame.Anchor, frame.X, frame.Y, frame.Width, frame.Height, root.Scale, width, height);
                    // The shop's list and quantity prompt, the Treasure Pool windows and the Items / Equipment windows keep their
                    // own places; the other windows follow the root.
                    bool shopWindow = menu.ShopSide != null || menu.IsTreasureWindow || menu.IsItemWindow || menu.IsAuctionWindow || menu.IsBlueMagicWindow
                        || menu.MacroView is StockUiMacroView.PaletteEditor or StockUiMacroView.Editor or StockUiMacroView.LineOptions or StockUiMacroView.ButtonOptions;
                    float x = shopWindow ? authored.X : Math.Clamp(authored.X + dx, 0, Math.Max(0, width - frame.Width * root.Scale));
                    float y = shopWindow ? authored.Y : Math.Clamp(authored.Y + dy, 0, Math.Max(0, height - frame.Height * root.Scale));
                    if (menu.ItemDropRow != null)
                    {
                        // A Drop / Recycling Bin count box sits at the bottom left, its description beside it (retail capture 2026-10-11).
                        y = Math.Max(0, StockUiWindowPlacement.BottomOf(new StockUiScreen(width, height, _window1Top), root.Scale) - frame.Height * root.Scale);
                    }
                    else if (menu.PageRing.Count > 0)
                    {
                        // The main menu opened over the target command menu takes its own place, not the command menu's offset.
                        var own = ResolveWindow(StockUiWindowIds.MainMenu, frame, width, height, out _);
                        x = own.X;
                        y = own.Y;
                    }
                    else if (menu.MacroView == StockUiMacroView.BookOptions && menu.Parent is { } bookList
                        && _menuPlacements.FirstOrDefault(p => ReferenceEquals(p.Menu, bookList)) is { Menu: not null } listAt
                        && bookList.Menu.FindButton(bookList.SelectedButtonId) is { } bookRow)
                    {
                        // A book's options open beside its row, left of the book list (PROVISIONAL: authored at 0,0; retail's place not captured).
                        x = Math.Max(0, listAt.X - (frame.Width + 4) * root.Scale);
                        y = Math.Clamp(listAt.Y + (bookRow.Y - 6) * root.Scale, 0, Math.Max(0, height - frame.Height * root.Scale));
                    }
                    else if (menu.IsActionList && _window1Top is { } listTop)
                    {
                        // The spell and ability lists sit on Window 1's top edge as the command menu does (retail image-14, the video).
                        x = authored.X;
                        y = Math.Clamp(listTop - frame.Height * root.Scale, 0, Math.Max(0, height - frame.Height * root.Scale));
                    }
                    placement = new StockUiPlacement(x, y, root.Scale, false);
                }
                StockUiMenuWindow.Draw(renderer, library, _font, menu, placement, timestamp, logFont: _logFont,
                    screen: new StockUiScreen(width, height, _window1Top));
                if (menu.IsTreasureList && !open.Any(m => m.IsTreasureDone) && library.TryGetMenu(StockUiTreasurePool.DoneMenu, out var spoils))
                {
                    // The Spoils Options window shows beside the Treasure Pool list (retail screenshots); + / Y puts the cursor on its Done.
                    var f = spoils.Frame;
                    renderer.DrawMenu(spoils, StockUiLayout.Place(f.Anchor, f.X, f.Y, f.Width, f.Height, placement.Scale, width, height));
                }
                if (menu.ItemListKind == StockUiItemListKind.Items && i == open.Count - 1 && library.TryGetMenu(StockUiItems.OptionsMenu, out var itemOptions))
                {
                    // The Items window's options ("+ : Sort") stand at the top right while the list takes input; + / Y puts the cursor on them.
                    var f = itemOptions.Frame;
                    StockUiItemWindow.DrawOptions(renderer, library, StockUiLayout.Place(f.Anchor, f.X, f.Y, f.Width, f.Height, placement.Scale, width, height));
                }
                if (menu.ItemListKind == StockUiItemListKind.SynthesisHistory && i == open.Count - 1 && library.TryGetMenu(StockUiSynthesis.OptionsMenu, out var synthesisOptions))
                {
                    // "+ : Synthesis Options" (Favorite / Unfavorite) stands at the top right while the History list takes input
                    // (capture Synthesis_History.png); + / Y puts the cursor on it.
                    var f = synthesisOptions.Frame;
                    renderer.DrawMenu(synthesisOptions, StockUiLayout.Place(f.Anchor, f.X, f.Y, f.Width, f.Height, placement.Scale, width, height), includeButtons: true, opaqueBody: true);
                }
                if (i == open.Count - 1 && menu.MacroView is StockUiMacroView.PaletteEditor or StockUiMacroView.Editor
                    && library.TryGetMenu(menu.MacroView == StockUiMacroView.PaletteEditor ? StockUiMacroMenus.ButtonOptions : StockUiMacroMenus.LineOptions, out var macroOptions))
                {
                    // Button Edit beside the palettes and Line Edit beside the editor stand at their authored places at the top right
                    // whenever those windows have the cursor (maintainer retail capture 2026-10-13); + / Y puts the cursor on them.
                    var f = macroOptions.Frame;
                    StockUiMacroWindow.DrawOptions(renderer, library, macroOptions, StockUiLayout.Place(f.Anchor, f.X, f.Y, f.Width, f.Height, placement.Scale, width, height));
                }
                if (menu.ShowsActionOptions && !open.Any(m => m.IsActionOptions)
                    && library.TryGetMenu(menus.OptionsMenuFor(menu.ActionCategory), out var options))
                {
                    var f = options.Frame;
                    renderer.DrawMenu(options, StockUiLayout.Place(f.Anchor, f.X, f.Y, f.Width, f.Height, placement.Scale, width, height));
                }
                _menuPlacements.Add(new StockUiMenuPlacement(menu, placement.X, placement.Y, placement.Scale));
            }
            if (_font is { } infoFont && open.LastOrDefault(m => m.IsActionList) is { } actionList)
            {
                var petNow = session.Pet.Current;
                StockUiMenuWindow.DrawActionInfo(renderer, library, infoFont, actionList, _actionInfoAnchor, root.Scale, timestamp,
                    session.LocalPlayer.CurrentTp, petNow != null ? (petNow.Name, petNow.Hpp) : null);
            }
            menus.SetScreenPlacements(_menuPlacements);
        }

        /// <summary>The target window's (or, with no target, the party window's) left and top edge: the action info window sits on it.</summary>
        private (float X, float Top)? _actionInfoAnchor;

        private readonly List<StockUiMenuPlacement> _menuPlacements = new();

        /// <summary>Window 1's top edge (screen px) as drawn this frame, null while the log is hidden: the command menu sits on it.</summary>
        private float? _window1Top;

        /// <summary>
        /// Over a clickable menu entry the system cursor is hidden and the hover pointer (arrow and ring) is drawn here,
        /// over everything; elsewhere the system cursor is the arrow itself. Returns whether it was drawn.
        /// </summary>
        private static bool DrawPointer(StockUiRenderer renderer, CharacterSession session)
        {
            if (!session.ActionService.UiPointer.TryGetPosition(out float x, out float y)) return false;
            if (!session.ActionService.Menus.IsOverEntry(x, y)) return false;
            StockUiMenuWindow.DrawHoverPointer(renderer, x, y);
            return true;
        }

        /// <summary>
        /// The party list split for the windows: your own party (bottom "Party" window) and the alliance's other
        /// parties in party-number order (the "raid1" window above, then "raid2").
        /// </summary>
        private readonly record struct PartyGroups(bool InGroup, IReadOnlyList<PartyMember> Own, IReadOnlyList<IReadOnlyList<PartyMember>> Others);

        private static PartyGroups GroupParty(CharacterSession session)
        {
            var members = session.Party.Members;
            if (members.Count == 0) return new PartyGroups(false, Array.Empty<PartyMember>(), Array.Empty<IReadOnlyList<PartyMember>>());

            uint localId = session.LocalPlayer.ServerId;
            byte ownParty = members.FirstOrDefault(m => m.ServerId == localId)?.PartyNumber ?? 0;
            var byParty = members.GroupBy(m => m.PartyNumber)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<PartyMember>)g.OrderBy(m => m.MemberNumber).ToList());
            var own = byParty.TryGetValue(ownParty, out var list) ? list : Array.Empty<PartyMember>();
            var others = byParty.Where(kv => kv.Key != ownParty).OrderBy(kv => kv.Key).Select(kv => kv.Value).ToList();
            return new PartyGroups(true, own, others);
        }


        /// <summary>
        /// The target window shows while something is targeted. By default it sits on top of the party window with a
        /// 2-pixel gap, as retail does (its authored y of 252 only fits a six-member party); a moved target window
        /// keeps its own placement.
        /// </summary>
        private void DrawTargetWindow(StockUiRenderer renderer, UiResourceLibrary library, CharacterSession session,
            StockUiPlacement? party, uint width, uint height)
        {
            // The target window follows the sub-target cursor while it is up (retail note C3 and image-26, 2026-10-12).
            var picking = session.ActionService.SubTargeting;
            var target = picking?.Current ?? session.ActionService.CurrentTarget;
            var font = _font;
            if (!library.TryGetMenu("targetwi", out var menu)) return;

            var placement = ResolveWindow(StockUiWindowIds.Target, menu.Frame, width, height, out bool moved);
            _actionInfoAnchor = party is { Hidden: false } anchorParty ? (placement.X, anchorParty.Y) : null;
            if (placement.Hidden) return;
            if (!moved && party is { Hidden: false } p)
            {
                placement = placement with { Y = Math.Max(0, p.Y - (menu.Frame.Height + 2) * placement.Scale) };
            }
            if (target != null && font != null) _actionInfoAnchor = (placement.X, placement.Y);
            if (target == null || font == null)
            {
                DrawPlaceholder(renderer, StockUiWindowIds.Target, menu, placement);
                return;
            }
            renderer.DrawMenu(menu, placement, includeButtons: false);
            Drag.Register(StockUiWindowIds.Target, menu.Frame, placement);

            var partyIds = new List<uint>();
            foreach (var member in session.Party.Members) partyIds.Add(member.ServerId);
            var kind = StockUiTargetWindow.Classify(target, session.LocalPlayer.ServerId, partyIds);
            // No HP gauge for an NPC without a name plate (#259); its name then has room for a third line (#258).
            bool showGauge = NamePlateStyle.ShowsTargetHealthBar(target, target.NamePlate);
            StockUiTargetWindow.Draw(renderer, font, menu, placement, target.Name, target.Hpp, kind, showGauge);

            if (session.ActionService.IsLockedOn && picking == null) StockUiTargetWindow.DrawLockOverlay(renderer, library, placement);
        }

        private readonly List<float> _statusOpacities = new();
        private readonly List<string?> _statusDurations = new();

        private void DrawStatusIcons(StockUiRenderer renderer, UiResourceLibrary library, CharacterSession session, uint width, uint height)
        {
            var icons = _statusIcons;
            if (icons == null || !library.TryGetMenu("buff", out var grid)) return;
            var ids = session.LocalPlayer.GetStatusEffectIds();
            var placement = ResolveWindow(StockUiWindowIds.StatusIcons, grid.Frame, width, height, out _);
            if (placement.Hidden) return;
            // The grid's frame is only a strip; the draggable extent is the icon slots in use (all of them when empty).
            var extent = StockUiTargetWindow.StatusGridExtent(grid, placement, ids.Count);
            if (ids.Count == 0)
            {
                if (Drag.Unlocked) Drag.Register(StockUiWindowIds.StatusIcons, grid.Frame, placement, extent.X, extent.Y, extent.Width, extent.Height, placeholder: true);
                return;
            }
            // Icons blink before they expire (#17): the end times come from S2C 0x063 type 0x09, on the server's clock.
            double now = session.World.Clock.GetEarthSecondsSinceEpoch(DateTime.UtcNow);
            var remaining = StockUiStatusBlink.MatchRemaining(ids, session.LocalPlayer.GetStatusIconTimers(now));
            double clock = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
            _statusOpacities.Clear();
            _statusDurations.Clear();
            foreach (var left in remaining)
            {
                _statusOpacities.Add(StockUiStatusBlink.Opacity(left, clock));
                _statusDurations.Add(StockUiStatusBlink.DurationText(left));
            }
            StockUiTargetWindow.DrawStatusIcons(renderer, icons, grid, placement, ids, _statusOpacities, _statusDurations, _font);
            int selected = session.Chat.SelectedStatusIcon;
            if (selected >= 0) StockUiTargetWindow.DrawStatusCursor(renderer, library, grid, placement, selected, Stopwatch.GetTimestamp());
            Drag.Register(StockUiWindowIds.StatusIcons, grid.Frame, placement, extent.X, extent.Y, extent.Width, extent.Height);
        }

        private readonly List<ChatLogLine> _logLines = new();

        /// <summary>
        /// The log window(s) and the chat input line. Window 1 keeps its authored left edge and stretches right to
        /// meet the party window (retail spans the bottom of the screen up to it; the authored 366 x 134 frame only
        /// fills the 512-wide layout). That width is the retail arrangement's and stays when either window is moved
        /// (a moved party window counts at its retail place), so a dragged log keeps its size. The Window 1/2 settings pick each window's frame ("log1".."log8" by "Maximum lines displayed", plus
        /// the title band) and its share of that width ("Window Width"); "Log Window Multi-window" adds Window 2 above
        /// Window 1 (Vertical) or beside it (Horizontal). The input line takes the bottom of Window 1, whose rows that
        /// no longer fit above it are not shown (retail capture, 2026-09-27). Each window is titled with the chat
        /// mode ("Say"; "Window 1:Say" and "Window 2" when split).
        /// </summary>
        private void DrawLogWindows(StockUiRenderer renderer, UiResourceLibrary library, CharacterSession session,
            PartyWindow? party, uint width, uint height)
        {
            var chat = session.Chat;
            int multi = Settings.GetValue(StockUiSettingKey.LogMultiWindow);
            chat.SetMultiWindow(multi != 0);
            var logFont = _logFont;
            _window1Top = null;
            UseLogSession(session);
            // Reactive sizing picks each window's line count (and so its frame) from the lines arriving; it shows the
            // maximum while the player reads the window (selected, scrolled back, typing, or placing the UI).
            double now = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
            UpdateLogSizing(_logSizing, chat, Settings, Drag.Unlocked, multi, now, DialogFloors(session, chat, _logFont, _logTextWidth));
            // The drawn height follows the line count smoothly: the frame is the next whole line count up, drawn
            // shorter, its rows held against the bottom edge (StockUiChatWindow.DrawLog's riseRows).
            float display1 = _logSizing[0].DisplayLines;
            if (!TryGetLogFrame(library, (int)Math.Ceiling(display1), out var menu1, out int maxRows1)) return;
            var placement = ResolveWindow(StockUiWindowIds.Log, menu1.Frame, width, height, out _);
            if (placement.Hidden) return;
            float s = placement.Scale;

            // Retail leaves a 2-pixel gap between the log and the party window (366 wide at 16 vs 384), measured in
            // the retail arrangement: the log at its authored left edge, the party window at its authored place.
            var retailLog = StockUiLayout.Place(menu1.Frame.Anchor, menu1.Frame.X, menu1.Frame.Y, menu1.Frame.Width, menu1.Frame.Height, s, width, height);
            float rightEdge = width - 16 * s;
            if (party is { Placement.Hidden: false } p)
            {
                var pf = p.Frame;
                float partyLeft = p.Moved ? StockUiLayout.Place(pf.Anchor, pf.X, pf.Y, pf.Width, pf.Height, p.Placement.Scale, width, height).X : p.Placement.X;
                rightEdge = partyLeft - 2 * s;
            }
            float fullWidth = Math.Max(menu1.Frame.Width, (rightEdge - retailLog.X) / s);
            // A moved log keeps that width, so keep it on screen.
            placement = placement with { X = Math.Clamp(placement.X, 0, Math.Max(0, width - fullWidth * s)) };
            float logBottom = placement.Y + menu1.Frame.Height * s;

            bool horizontal = multi == 2;
            float width1 = fullWidth, width2 = 0;
            if (horizontal)
            {
                width1 = (fullWidth - 2) * 0.5f;
                width2 = width1;
            }
            width1 = ApplyWidthSetting(width1, StockUiSettingKey.Window1Width);
            _logTextWidth[0] = width1 - StockUiChatWindow.TextLeft - StockUiChatWindow.TextRight;

            // The full-screen log (Confirm with the log selected) takes the log's place: Window 1's left edge and width,
            // from near the top of the screen down to the log's bottom edge.
            if (chat.FullLogOpen && logFont != null)
            {
                int pageRows = StockUiChatWindow.DrawFullLog(renderer, library, logFont, chat, placement.X, logBottom, width1, s,
                    Settings.GetValue(StockUiSettingKey.LogTimestamp), Settings);
                if (pageRows > 0) chat.FullLogPageLines = pageRows;
                _window1Top = StockUiChatWindow.FullLogTop * s;
                return;
            }

            var input = chat.Input;
            string modeLabel = StockUiChatInput.Label(input.Mode);
            float fullHeight1 = menu1.Frame.Height + StockUiChatWindow.TitleBand;
            float height1 = Math.Min(fullHeight1, StockUiChatWindow.WindowHeight(display1));
            float rise1 = (fullHeight1 - height1) / StockUiChatWindow.RowPitch;
            var window1 = new StockUiPlacement(placement.X, logBottom - height1 * s, s, false);
            _window1Top = window1.Y;
            bool hasInline = library.TryGetMenu("inline", out var inline);
            bool inputOpen = input.IsOpen && hasInline;

            // The input line sits over the bottom of Window 1 (retail) unless the player has placed it ("chat")
            // elsewhere, in which case it keeps its authored width and Window 1 shows all its rows.
            StockUiPlacement inputPlacement = default;
            bool inputMoved = false;
            if (hasInline)
            {
                var chatPlacement = ResolveWindow(StockUiWindowIds.ChatInput, inline.Frame, width, height, out inputMoved);
                inputPlacement = inputMoved ? chatPlacement : new StockUiPlacement(placement.X, logBottom - inline.Frame.Height * s, s, chatPlacement.Hidden);
            }
            bool inputOnWindow1 = inputOpen && !inputMoved && !inputPlacement.Hidden;
            float textBottom1 = inputOnWindow1 ? fullHeight1 - inline.Frame.Height - 1 : fullHeight1 - StockUiChatWindow.BottomPadding;
            // Minimum lines 0: a quiet log closes entirely (the command menu then sits on its bottom edge).
            if (display1 > 0.01f)
            {
                DrawLog(renderer, library, chat.Log, 1, menu1, window1, width1, height1,
                    StockUiChatWindow.RowsThatFit(textBottom1, maxRows1), multi != 0 ? $"Window 1:{modeLabel}" : modeLabel,
                    chat.IsLogSelected, logFont, rise1);
                Drag.Register(StockUiWindowIds.Log, menu1.Frame, placement, window1.X, window1.Y, width1 * s, height1 * s);
            }

            float display2 = _logSizing[1].DisplayLines;
            if (multi != 0 && display2 > 0.01f && TryGetLogFrame(library, (int)Math.Ceiling(display2), out var menu2, out int maxRows2))
            {
                if (!horizontal) width2 = fullWidth;
                width2 = ApplyWidthSetting(width2, StockUiSettingKey.Window2Width);
                _logTextWidth[1] = width2 - StockUiChatWindow.TextLeft - StockUiChatWindow.TextRight;
                float fullHeight2 = menu2.Frame.Height + StockUiChatWindow.TitleBand;
                float height2 = Math.Min(fullHeight2, StockUiChatWindow.WindowHeight(display2));
                float rise2 = (fullHeight2 - height2) / StockUiChatWindow.RowPitch;
                var window2 = horizontal
                    ? new StockUiPlacement(placement.X + (fullWidth - width2) * s, logBottom - height2 * s, s, false)
                    : new StockUiPlacement(placement.X, window1.Y - (height2 + 2) * s, s, false);
                DrawLog(renderer, library, chat.Log, 2, menu2, window2, width2, height2,
                    StockUiChatWindow.RowsThatFit(fullHeight2 - StockUiChatWindow.BottomPadding, maxRows2), "Window 2",
                    chat.IsLogSelected, logFont, rise2);
                Drag.Register(StockUiWindowIds.Log, menu1.Frame, placement, window2.X, window2.Y, width2 * s, height2 * s);
            }

            if (!hasInline || inputPlacement.Hidden) return;
            float inputWidth = width1;
            if (inputMoved) inputPlacement = inputPlacement with { X = Math.Clamp(inputPlacement.X, 0, Math.Max(0, width - inputWidth * s)) };
            if (!inputOpen || logFont == null)
            {
                if (Drag.Unlocked)
                {
                    renderer.DrawMenu(inline, inputPlacement, includeButtons: false, inputWidth);
                    Drag.Register(StockUiWindowIds.ChatInput, inline.Frame, inputPlacement, hitWidth: inputWidth * s, placeholder: true);
                }
                return;
            }
            StockUiChatWindow.DrawInput(renderer, library, logFont, inline, inputPlacement, inputWidth, input, Stopwatch.GetTimestamp());
            Drag.Register(StockUiWindowIds.ChatInput, inline.Frame, inputPlacement, hitWidth: inputWidth * s);
        }

        private void DrawLog(StockUiRenderer renderer, UiResourceLibrary library, StockUiChatLog log, int window,
            UiMenuDefinition menu, StockUiPlacement placement, float frameWidth, float frameHeight, int rows, string title, bool selected,
            StockUiLogFont? logFont, float riseRows = 0)
        {
            if (logFont == null)
            {
                renderer.DrawMenu(menu, placement, includeButtons: false, frameWidth, frameHeight: frameHeight);
                return;
            }
            // A window shows at most as many lines as rows (each line wraps to one row or more); two more are copied
            // for the rows that slide out of the top while new ones slide in.
            log.CopyVisible(window, rows + 2, _logLines);
            StockUiChatWindow.DrawLog(renderer, library, menu, logFont, _font, placement, frameWidth, frameHeight, rows, _logLines,
                Settings.GetValue(StockUiSettingKey.LogTimestamp), log.ScrollOffset(window) > 0, title, selected, _dialogWaiting,
                _logScroll[Math.Clamp(window - 1, 0, 1)], Settings, riseRows);
        }

        /// <summary>Each log window's slide of newly arrived rows (see StockUiChatWindow.LogScrollState).</summary>
        private readonly StockUiChatWindow.LogScrollState[] _logScroll = { new(), new() };

        /// <summary>Whether the session's event dialog waits for Confirm this frame (the log then shows the wait arrow).</summary>
        private bool _dialogWaiting;

        /// <summary>The /buttonpreview picker (#360).</summary>
        private readonly ButtonGlyphPreview _buttonPreview = new();

        /// <summary>The drawn session's log windows' reactive sizing (Window 1, Window 2): one of <see cref="_logSizings"/>.</summary>
        private StockUiLogSizing[] _logSizing = { new(), new() };

        /// <summary>
        /// Each character's own log window sizing (#377). One HUD draws whichever character the viewport shows; with a shared
        /// sizing a switch read the other log's arrived-line counter as that many new lines and grew the window line by line,
        /// as if the log replayed. The characters not shown keep theirs up to date (<see cref="TickHiddenLog"/>).
        /// </summary>
        private readonly System.Runtime.CompilerServices.ConditionalWeakTable<CharacterSession, StockUiLogSizing[]> _logSizings = new();

        /// <summary>The session whose log was drawn last (render thread).</summary>
        private CharacterSession? _logSession;

        private StockUiLogSizing[] LogSizingFor(CharacterSession session) =>
            _logSizings.GetValue(session, _ => new StockUiLogSizing[] { new(), new() });

        /// <summary>A character's reactive sizing of log window 1 or 2 (its own, #377).</summary>
        public StockUiLogSizing LogSizing(CharacterSession session, int window) => LogSizingFor(session)[window == 2 ? 1 : 0];

        /// <summary>
        /// Takes the drawn session's log state. On a switch the rows' slide starts over from the log as it stands, so the
        /// other character's lines do not slide in as new ones (#377).
        /// </summary>
        private void UseLogSession(CharacterSession session)
        {
            if (ReferenceEquals(session, _logSession)) return;
            _logSession = session;
            _logSizing = LogSizingFor(session);
            foreach (var scroll in _logScroll) scroll.Reset();
        }

        /// <summary>
        /// Advances a log's reactive sizing (Window 2's only while the log is split); it shows the maximum while the player
        /// reads the window (selected, scrolled back, typing, or placing the UI).
        /// </summary>
        private static void UpdateLogSizing(StockUiLogSizing[] sizing, StockUiChat chat, StockUiSettings settings, bool unlocked, int multi, double now, (int Window1, int Window2) dialogFloor)
        {
            // Window 2 is a copy of Window 1 with its own settings: one routine, configured per window (#369: an NPC line
            // waiting for Confirm keeps either window up, whichever the dialog channel is routed to).
            UpdateWindowSizing(sizing[0], 1, chat, settings, unlocked || chat.Input.IsOpen, now, dialogFloor.Window1,
                StockUiSettingKey.Window1ReactiveSizing, StockUiSettingKey.Window1MinLines, StockUiSettingKey.Window1MaxLines, StockUiSettingKey.Window1ResizeTime);
            if (multi != 0)
            {
                UpdateWindowSizing(sizing[1], 2, chat, settings, unlocked, now, dialogFloor.Window2,
                    StockUiSettingKey.Window2ReactiveSizing, StockUiSettingKey.Window2MinLines, StockUiSettingKey.Window2MaxLines, StockUiSettingKey.Window2ResizeTime);
            }
        }

        private static void UpdateWindowSizing(StockUiLogSizing sizing, int window, StockUiChat chat, StockUiSettings settings, bool expanded, double now,
            int dialogFloor, StockUiSettingKey reactive, StockUiSettingKey minLines, StockUiSettingKey maxLines, StockUiSettingKey resizeTime) =>
            sizing.Update(now, chat.Log.AddedCount(window), settings.IsOn(reactive), settings.GetValue(minLines), settings.GetValue(maxLines),
                StockUiLogSizing.ResizeSeconds(settings.GetValue(resizeTime)),
                expanded: expanded || chat.IsLogSelected || chat.Log.ScrollOffset(window) > 0, floor: dialogFloor,
                shrinkRequests: chat.LogShrinkRequests);

        /// <summary>Each log window's text width as last drawn (authored pixels), for working out how many rows a dialog line takes.</summary>
        private readonly float[] _logTextWidth = { 366 - StockUiChatWindow.TextLeft - StockUiChatWindow.TextRight, 366 - StockUiChatWindow.TextLeft - StockUiChatWindow.TextRight };

        /// <summary>
        /// The rows each log window must keep while an event line waits for Confirm (#369): the wrapped rows of the lines that dialog
        /// added to that window (<see cref="Gordian.Engine.Events.EventDialogController.DialogLogMark"/>), the window still growing and
        /// shrinking above that. Zero when nothing waits.
        /// </summary>
        private static (int Window1, int Window2) DialogFloors(CharacterSession session, StockUiChat chat, StockUiLogFont? logFont, float[] textWidth)
        {
            if (!session.Events.IsWaitingForConfirm) return (0, 0);
            var mark = session.Events.DialogLogMark;
            int timestamp = session.ActionService.UiSettings.GetValue(StockUiSettingKey.LogTimestamp);
            int Rows(int window, long marked)
            {
                long added = chat.Log.AddedCount(window) - marked;
                if (added <= 0) return 0;
                var lines = new List<ChatLogLine>();
                chat.Log.CopyVisible(window, (int)Math.Min(added, 40), lines);
                if (logFont == null) return lines.Count;
                int rows = 0;
                foreach (var line in lines) rows += StockUiChatLog.GetWrappedRows(line, logFont.GetAdvance, textWidth[window - 1], timestamp).Count;
                return Math.Max(1, rows);
            }
            return (Rows(1, mark.Window1), chat.Log.MultiWindow ? Rows(2, mark.Window2) : 0);
        }

        /// <summary>
        /// Keeps the log windows of a character not on screen sized as its lines arrive (#377), so a switch to it shows its
        /// log at once, as it already was. Render thread; the session being drawn is skipped.
        /// </summary>
        public void TickHiddenLog(CharacterSession session)
        {
            ArgumentNullException.ThrowIfNull(session);
            if (ReferenceEquals(session, _logSession)) return;
            var settings = session.ActionService.UiSettings;
            int multi = settings.GetValue(StockUiSettingKey.LogMultiWindow);
            session.Chat.SetMultiWindow(multi != 0);
            UpdateLogSizing(LogSizingFor(session), session.Chat, settings, session.ActionService.UiDrag.Unlocked, multi,
                Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency, DialogFloors(session, session.Chat, _logFont, _logTextWidth));
        }

        /// <summary>
        /// The frame for a log window showing <paramref name="lines"/> lines ("log1".."log8"; "logwindo" is the same
        /// frame as "log8") and its row count.
        /// </summary>
        private static bool TryGetLogFrame(UiResourceLibrary library, int lines, out UiMenuDefinition menu, out int rows)
        {
            rows = Math.Clamp(lines, 1, 20);
            if (rows > 8)
            {
                // The UI DAT authors "log1".."log8" only (checked 2026-10-18); retail's way past 8 lines is not known,
                // so the log8 frame grows 16 px per extra line (the pitch of log1..log8), bottom edge fixed.
                if (!library.TryGetMenu("log8", out var log8)) { menu = null!; return false; }
                var cache = TallLogFrames.GetValue(library, _ => new Dictionary<int, UiMenuDefinition>());
                if (!cache.TryGetValue(rows, out menu!)) cache[rows] = menu = StretchedLogFrame(log8, rows);
                return true;
            }
            if (library.TryGetMenu($"log{rows}", out menu)) return true;
            rows = 8;
            return library.TryGetMenu("logwindo", out menu);
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<UiResourceLibrary, Dictionary<int, UiMenuDefinition>> TallLogFrames = new();

        private static UiMenuDefinition StretchedLogFrame(UiMenuDefinition log8, int rows)
        {
            short extra = (short)((rows - 8) * 16);
            var f = log8.Frame;
            return new UiMenuDefinition
            {
                DatId = log8.DatId, Category = log8.Category, Name = $"log{rows}", MenuType = log8.MenuType, Buttons = log8.Buttons,
                Frame = new UiMenuFrame
                {
                    X = f.X, Y = (short)(f.Y - extra), CursorOffsetX = f.CursorOffsetX, CursorOffsetY = f.CursorOffsetY,
                    Width = f.Width, Height = (short)(f.Height + extra), AuthoredHeight = f.Height, DrawOffsetX = f.DrawOffsetX, DrawOffsetY = f.DrawOffsetY,
                    Anchor = f.Anchor, Shapes = f.Shapes, HelpTextId = f.HelpTextId, TitleTextId = f.TitleTextId,
                },
            };
        }

        /// <summary>A window's "Window Width" setting (percent of its full width; never narrower than 128 px).</summary>
        private float ApplyWidthSetting(float fullWidth, StockUiSettingKey key)
        {
            int percent = Math.Clamp(Settings.GetValue(key), 0, 100);
            return Math.Max(Math.Min(128, fullWidth), fullWidth * percent / 100f);
        }

        /// <summary>
        /// The party window shows your own party: "ptw0" (titled Solo) outside a party and "ptw1".."ptw6" by member
        /// count in one (or in an alliance).
        /// </summary>
        /// <summary>The party window as drawn this frame: its placement, its frame and whether the player moved it.</summary>
        private readonly record struct PartyWindow(StockUiPlacement Placement, UiMenuFrame Frame, bool Moved);

        private PartyWindow? DrawPartyWindow(StockUiRenderer renderer, UiResourceLibrary library, CharacterSession session,
            PartyGroups groups, uint width, uint height)
        {
            int count = groups.InGroup ? Math.Clamp(groups.Own.Count, 1, 6) : 1;
            if (!library.TryGetMenu(groups.InGroup ? $"ptw{count}" : "ptw0", out var menu)) return null;

            var placement = ResolveWindow(StockUiWindowIds.Party, menu.Frame, width, height, out bool moved);
            var result = new PartyWindow(placement, menu.Frame, moved);
            if (placement.Hidden) return result;
            renderer.DrawMenu(menu, placement, includeButtons: false);
            Drag.Register(StockUiWindowIds.Party, menu.Frame, placement);
            if (session.Party.HasPendingInvite && _font is { } inviteFont) StockUiPartyWindow.DrawInviteIcon(renderer, inviteFont, placement);

            var font = _font;
            if (font == null) return result;
            var rows = GetPartyRows(session, groups, count, _resources);
            StockUiPartyWindow.Draw(renderer, font, menu, placement, rows, Layout.ShowPartyTp);
            var memberIds = groups.InGroup ? groups.Own.Take(count).Select(m => m.ServerId).ToList() : new List<uint> { session.LocalPlayer.ServerId };
            DrawRowSelector(renderer, library, session, menu, placement, memberIds);
            // The pet beside your own row (retail image-45 / -48), unless a blood pact list shows it in its info window (image-46).
            if (session.Pet.Current is { } pet && !ShowsPetInInfo(session.ActionService.Menus))
            {
                string petName = string.IsNullOrEmpty(pet.Name) && session.World.TryGetByServerId(pet.ServerId, out var petEntity) && petEntity != null ? petEntity.Name : pet.Name;
                StockUiPartyWindow.DrawPetRow(renderer, font, menu, placement, Math.Max(0, memberIds.IndexOf(session.LocalPlayer.ServerId)), petName, pet.Hpp);
            }
            if (RollTexts(session, groups.InGroup ? groups.Own.Take(count).Select(m => m.ServerId) : new[] { session.LocalPlayer.ServerId }) is { } rolls)
            {
                StockUiTreasureWindow.DrawRollColumn(renderer, font, menu, placement, rolls, label: true);
            }
            if (Layout.ShowPartyStatusIcons && _statusIcons is { } icons)
            {
                StockUiPartyWindow.DrawStatusIcons(renderer, icons, menu, placement, rows, Layout.PartyStatusIconSide);
            }
            return result;
        }

        /// <summary>
        /// The alliance's other parties, in the "raid1" (upper) and "raid2" (lower) windows: authored above the target
        /// window's slot, so they stay put whether or not something is targeted (as in retail captures).
        /// </summary>
        private void DrawAllianceWindows(StockUiRenderer renderer, UiResourceLibrary library, CharacterSession session, PartyGroups groups, uint width, uint height)
        {
            var font = _font;
            int windows = Drag.Unlocked ? 2 : Math.Min(groups.Others.Count, 2);
            for (int i = 0; i < windows; i++)
            {
                if (!library.TryGetMenu($"raid{i + 1}", out var menu)) continue;
                string id = i == 0 ? StockUiWindowIds.Alliance1 : StockUiWindowIds.Alliance2;
                var placement = ResolveWindow(id, menu.Frame, width, height, out _);
                if (placement.Hidden) continue;
                if (i >= groups.Others.Count)
                {
                    DrawPlaceholder(renderer, id, menu, placement);
                    continue;
                }
                renderer.DrawMenu(menu, placement, includeButtons: false);
                Drag.Register(id, menu.Frame, placement);
                if (font == null) continue;

                var rows = new List<PartyRowVitals>(6);
                foreach (var m in groups.Others[i].Take(6)) rows.Add(ToRow(m, session.World.CurrentZoneId, _resources));
                StockUiPartyWindow.DrawAllianceRows(renderer, font, menu, placement, rows);
                DrawRowSelector(renderer, library, session, menu, placement, groups.Others[i].Take(6).Select(m => m.ServerId).ToList());
                if (RollTexts(session, groups.Others[i].Take(6).Select(m => m.ServerId)) is { } rolls)
                {
                    StockUiTreasureWindow.DrawRollColumn(renderer, font, menu, placement, rolls, label: false);
                }
            }
        }

        /// <summary>
        /// The sub-target cursor's selector beside its member's row in the party or an alliance window (retail image-26,
        /// 2026-10-12): the half-size menu cursor, its tip at the row's left edge, level with the name (6 px down).
        /// </summary>
        private static void DrawRowSelector(StockUiRenderer renderer, UiResourceLibrary library, CharacterSession session, UiMenuDefinition menu,
            StockUiPlacement placement, IReadOnlyList<uint> memberIds)
        {
            if (session.ActionService.SubTargeting is not { } picking) return;
            int index = -1;
            for (int i = 0; i < memberIds.Count && i < menu.Buttons.Count; i++)
            {
                if (memberIds[i] == picking.Current.ServerId) index = i;
            }
            if (index < 0) return;
            var button = menu.Buttons[index];
            float s = placement.Scale;
            var tip = new Vector2(placement.X + (button.X - 1) * s, placement.Y + (button.Y + RowSelectorDrop) * s);
            StockUiTargetWindow.DrawRowSelector(renderer, library, tip, s, Stopwatch.GetTimestamp());
        }

        private const float RowSelectorDrop = 6;

        /// <summary>Whether an open blood pact list draws the pet in its info window (then the floating pet row is left out).</summary>
        private static bool ShowsPetInInfo(StockUiMenuController menus) =>
            !menus.Suspended && menus.OpenMenus.LastOrDefault(m => m.IsActionList) is { ActionList: StockUiActionList.PetAbilities, ActionCategory: 6 or 10 };

        /// <summary>
        /// The "roll" column's texts for some members while the Treasure Pool list is open: each one's lot or pass on the
        /// selected item (#143); null when the list is not open.
        /// </summary>
        private static List<string>? RollTexts(CharacterSession session, IEnumerable<uint> memberIds)
        {
            StockUiOpenMenu? list = null;
            foreach (var menu in session.ActionService.Menus.OpenMenus)
            {
                if (menu.IsTreasureList) list = menu;
            }
            if (list?.SelectedTreasureRow is not { } row) return null;
            uint localId = session.LocalPlayer.ServerId;
            var texts = new List<string>(6);
            foreach (uint id in memberIds) texts.Add(StockUiTreasurePool.RollText(session.Treasure.GetMemberEntry(row.Slot, id, localId)));
            return texts;
        }

        private static List<PartyRowVitals> GetPartyRows(CharacterSession session, PartyGroups groups, int count, ResourceManager? resources)
        {
            var rows = new List<PartyRowVitals>(count);
            var local = session.LocalPlayer;
            if (!groups.InGroup)
            {
                rows.Add(new PartyRowVitals(session.CharacterName, local.CurrentHp, Percent(local.CurrentHp, local.MaxHp),
                    local.CurrentMp, Percent(local.CurrentMp, local.MaxMp), local.CurrentTp, IsLeader: false,
                    StatusIds: local.GetStatusEffectIds()));
                return rows;
            }

            for (int i = 0; i < count && i < groups.Own.Count; i++)
            {
                var m = groups.Own[i];
                if (m.ServerId != 0 && m.ServerId == local.ServerId)
                {
                    // Our own row reads the local player's live vitals and status (always current; the party copy
                    // only updates when the server relays 0x0DF/0x0DD for us, and 0x076 never lists us).
                    string ownName = m.Name.Length > 0 ? m.Name : session.CharacterName;
                    rows.Add(new PartyRowVitals(ownName, local.CurrentHp, Percent(local.CurrentHp, local.MaxHp),
                        local.CurrentMp, Percent(local.CurrentMp, local.MaxMp), local.CurrentTp, m.IsLeader, m.IsAllianceLeader,
                        local.GetStatusEffectIds()));
                    continue;
                }
                rows.Add(ToRow(m, session.World.CurrentZoneId, resources));
            }
            return rows;
        }

        private static PartyRowVitals ToRow(PartyMember m, ushort currentZoneId, ResourceManager? resources) =>
            new(m.Name, (int)m.Hp, m.Hpp, (int)m.Mp, m.Mpp, (int)m.Tp, m.IsLeader, m.IsAllianceLeader, m.StatusEffectIds,
                m.IsInOtherZone(currentZoneId) ? ZoneRowName(m.ZoneId, resources) : null);

        /// <summary>The parenthesised zone text for a member in zone <paramref name="zoneId"/>: the compact name from ROM/165/85.</summary>
        internal static string ZoneRowName(ushort zoneId, ResourceManager? resources)
        {
            if (resources != null && resources.TryGetString(DMsgCategory.ZoneNamesCompact, zoneId, out var name))
            {
                return StockUiPartyWindow.ZoneRowText(name);
            }
            return StockUiPartyWindow.ZoneRowText("Zone " + zoneId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        private static int Percent(int value, int max) => max > 0 ? Math.Clamp(value * 100 / max, 0, 100) : 100;
    }
}
