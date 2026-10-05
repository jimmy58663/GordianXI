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
using Gordian.Core.Ui;
using Gordian.Core.World;
using Veldrid;

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

        /// <summary>The zoning overlay's opacity (0 clear, 1 black), drawn over the scene and the interface (<see cref="Gordian.Core.Ui.ZoneLoadingScreen"/>).</summary>
        public float LoadingOpacity { get; set; }

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
            Vector2? targetCursor = null, IReadOnlyList<NamePlateAnchor>? namePlates = null)
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
            if (menus.ItemLookup == null && _resources is { } items)
            {
                // The shop windows' names, stack sizes, icons and descriptions come from the item DATs.
                menus.ItemLookup = id => items.TryGetItem(id, out var record) ? record : null;
                Gordian.Core.Network.Packets.StandardMessages.ItemLookup ??= menus.ItemLookup;
            }

            renderer.Begin(library);
            // An event's screen fades (#165): the 3D scene's under the interface (and its 0x72 flashes, #192), then the interface's own.
            var presentation = session.Events.Presentation;
            renderer.DrawScreenTint(width, height, presentation.SceneColor);
            renderer.DrawScreenFlash(width, height, presentation.SceneFlash);
            renderer.Opacity = presentation.InterfaceOpacity;
            var groups = GroupParty(session);
            // An event's message mode (opcode 0x67) hides the HUD windows and shows the event's lines on the screen instead; name plates stay (the retail recording shows them through the aerial shots).
            bool cutscene = session.Events.IsCutsceneHud;
            NamePlateBounds? targetPlate = null;
            if (namePlates != null && _font is { } plateFont)
            {
                var ownParty = new List<uint>();
                foreach (var member in groups.Own) ownParty.Add(member.ServerId);
                // The plates belong to the scene: they fade to black with it (the fdo? / fdi? fades) and come back with it.
                var scene = presentation.SceneColor;
                renderer.Opacity = presentation.InterfaceOpacity * Math.Clamp((scene.X + scene.Y + scene.Z) / 3f, 0f, 1f);
                targetPlate = StockUiNamePlates.Draw(renderer, library, plateFont, session, namePlates, ownParty, width, height, Layout.NamePlateScale);
                renderer.Opacity = presentation.InterfaceOpacity;
            }
            if (targetCursor is { } cursor && session.ActionService.CurrentTarget != null && !cutscene)
            {
                if (targetPlate is { } plate)
                {
                    float tip = Math.Min(plate.Center.Y - plate.GlyphHeight * StockUiNamePlates.CursorGapShare, plate.Top);
                    cursor = new Vector2(plate.Center.X, tip);
                }
                StockUiTargetWindow.DrawCursor(renderer, library, cursor, Layout.Scale, Stopwatch.GetTimestamp());
            }
            if (!cutscene)
            {
                var party = DrawPartyWindow(renderer, library, session, groups, width, height);
                DrawAllianceWindows(renderer, library, session, groups, width, height);
                DrawLogWindows(renderer, library, session, party, width, height);
                DrawTargetWindow(renderer, library, session, party?.Placement, width, height);
                DrawStatusIcons(renderer, library, session, width, height);
            }
            else if (session.Events.EventText is { } eventText)
            {
                DrawEventText(renderer, eventText, width, height);
            }
            DrawMenus(renderer, library, session, menus, width, height);
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
            renderer.End(framebuffer, width, height);
        }

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
        private void DrawEventText(StockUiRenderer renderer, Gordian.Core.Events.EventScreenText text, uint width, uint height)
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
            if (open.Count == 0)
            {
                menus.SetScreenPlacements(_menuPlacements);
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
                    // The shop's list and quantity prompt keep their authored places; the other windows follow the root.
                    bool shopWindow = menu.ShopSide != null;
                    float x = shopWindow ? authored.X : Math.Clamp(authored.X + dx, 0, Math.Max(0, width - frame.Width * root.Scale));
                    float y = shopWindow ? authored.Y : Math.Clamp(authored.Y + dy, 0, Math.Max(0, height - frame.Height * root.Scale));
                    placement = new StockUiPlacement(x, y, root.Scale, false);
                }
                StockUiMenuWindow.Draw(renderer, library, _font, menu, placement, timestamp);
                _menuPlacements.Add(new StockUiMenuPlacement(menu, placement.X, placement.Y, placement.Scale));
            }
            menus.SetScreenPlacements(_menuPlacements);
        }

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
            if (members.Count <= 1) return new PartyGroups(false, Array.Empty<PartyMember>(), Array.Empty<IReadOnlyList<PartyMember>>());

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
            var target = session.ActionService.CurrentTarget;
            var font = _font;
            if (!library.TryGetMenu("targetwi", out var menu)) return;

            var placement = ResolveWindow(StockUiWindowIds.Target, menu.Frame, width, height, out bool moved);
            if (placement.Hidden) return;
            if (!moved && party is { Hidden: false } p)
            {
                placement = placement with { Y = Math.Max(0, p.Y - (menu.Frame.Height + 2) * placement.Scale) };
            }
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
            StockUiTargetWindow.Draw(renderer, font, menu, placement, target.Name, target.Hpp, kind);

            if (session.ActionService.IsLockedOn) StockUiTargetWindow.DrawLockOverlay(renderer, library, placement);
        }

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
            StockUiTargetWindow.DrawStatusIcons(renderer, icons, grid, placement, ids);
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
            if (!TryGetLogFrame(library, StockUiSettingKey.Window1MaxLines, out var menu1, out int maxRows1)) return;
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

            var input = chat.Input;
            string modeLabel = StockUiChatInput.Label(input.Mode);
            float height1 = menu1.Frame.Height + StockUiChatWindow.TitleBand;
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
            float textBottom1 = inputOnWindow1 ? height1 - inline.Frame.Height - 1 : height1 - StockUiChatWindow.BottomPadding;
            DrawLog(renderer, library, chat.Log, 1, menu1, window1, width1, height1,
                StockUiChatWindow.RowsThatFit(textBottom1, maxRows1), multi != 0 ? $"Window 1:{modeLabel}" : modeLabel,
                chat.SelectedLogWindow == 1, logFont);
            Drag.Register(StockUiWindowIds.Log, menu1.Frame, placement, window1.X, window1.Y, width1 * s, height1 * s);

            if (multi != 0 && TryGetLogFrame(library, StockUiSettingKey.Window2MaxLines, out var menu2, out int maxRows2))
            {
                if (!horizontal) width2 = fullWidth;
                width2 = ApplyWidthSetting(width2, StockUiSettingKey.Window2Width);
                float height2 = menu2.Frame.Height + StockUiChatWindow.TitleBand;
                var window2 = horizontal
                    ? new StockUiPlacement(placement.X + (fullWidth - width2) * s, logBottom - height2 * s, s, false)
                    : new StockUiPlacement(placement.X, window1.Y - (height2 + 2) * s, s, false);
                DrawLog(renderer, library, chat.Log, 2, menu2, window2, width2, height2,
                    StockUiChatWindow.RowsThatFit(height2 - StockUiChatWindow.BottomPadding, maxRows2), "Window 2",
                    chat.SelectedLogWindow == 2, logFont);
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
            StockUiLogFont? logFont)
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
                _logScroll[Math.Clamp(window - 1, 0, 1)]);
        }

        /// <summary>Each log window's slide of newly arrived rows (see StockUiChatWindow.LogScrollState).</summary>
        private readonly StockUiChatWindow.LogScrollState[] _logScroll = { new(), new() };

        /// <summary>Whether the session's event dialog waits for Confirm this frame (the log then shows the wait arrow).</summary>
        private bool _dialogWaiting;

        /// <summary>
        /// The frame for a log window's "Maximum lines displayed" ("log1".."log8"; "logwindo" is the same frame as
        /// "log8") and its row count.
        /// </summary>
        private bool TryGetLogFrame(UiResourceLibrary library, StockUiSettingKey maxLinesKey, out UiMenuDefinition menu, out int rows)
        {
            rows = Math.Clamp(Settings.GetValue(maxLinesKey), 1, 8);
            if (library.TryGetMenu($"log{rows}", out menu)) return true;
            rows = 8;
            return library.TryGetMenu("logwindo", out menu);
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

            var font = _font;
            if (font == null) return result;
            var rows = GetPartyRows(session, groups, count, _resources);
            StockUiPartyWindow.Draw(renderer, font, menu, placement, rows, Layout.ShowPartyTp);
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
            }
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
