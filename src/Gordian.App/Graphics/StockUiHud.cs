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

        /// <summary>The layout used for the last frame (the session's shared layout, see StockUiLayoutStore).</summary>
        public StockUiLayout Layout { get; private set; } = new();

        /// <summary>The session's config-menu settings (log window lines, party icons...).</summary>
        public StockUiSettings Settings { get; private set; } = new();

        /// <summary>Master switch for the whole stock HUD.</summary>
        public bool Enabled { get; set; } = true;

        public UiResourceLibrary? Library => _library;

        /// <summary>
        /// Starts loading the UI resources in the background (idempotent).
        /// </summary>
        public void EnsureLoading(ResourceManager? resources)
        {
            if (resources == null || Interlocked.CompareExchange(ref _loadStarted, 1, 0) != 0) return;
            _resources = resources;
            Task.Run(() =>
            {
                try
                {
                    var library = UiResourceLibrary.Load(resources, Layout.WindowSkin);
                    if (library == null) return;
                    _font = UiFont.FromLibrary(library);
                    _statusIcons = StatusIconLibrary.Load(resources);
                    _logFont = StockUiLogFont.FromLibrary(library);
                    _library = library;
                }
                catch (Exception ex)
                {
                    GordianLog.Error("UI", $"Failed to load stock UI resources: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Reloads the UI resources with another window skin (1-8) in the background when the layout's skin changed
        /// (the config menu's "Window Type", or <c>/uilayout skin</c>); the current library draws until it is ready.
        /// </summary>
        private void EnsureWindowSkin(UiResourceLibrary current, int skin)
        {
            var resources = _resources;
            if (resources == null || current.WindowSkin == skin || Interlocked.CompareExchange(ref _skinReloading, 1, 0) != 0) return;
            Task.Run(() =>
            {
                try
                {
                    var library = UiResourceLibrary.Load(resources, skin);
                    if (library == null) return;
                    _font = UiFont.FromLibrary(library);
                    _logFont = StockUiLogFont.FromLibrary(library);
                    _library = library;
                }
                catch (Exception ex)
                {
                    GordianLog.Error("UI", $"Failed to load window skin {skin}: {ex.Message}");
                }
                finally
                {
                    Interlocked.Exchange(ref _skinReloading, 0);
                }
            });
        }

        /// <summary>
        /// Draws the HUD for a session over the framebuffer's current contents. <paramref name="targetCursor"/> is the
        /// screen point the target cursor points at (just above the target's head), when the target is on screen.
        /// </summary>
        public void Render(StockUiRenderer renderer, CharacterSession? session, Framebuffer framebuffer, uint width, uint height,
            Vector2? targetCursor = null)
        {
            var library = _library;
            if (!Enabled || library == null || session == null) return;
            Layout = session.ActionService.UiLayout;
            Settings = session.ActionService.UiSettings;
            EnsureWindowSkin(library, Layout.WindowSkin);

            var menus = session.ActionService.Menus;
            if (!ReferenceEquals(menus.Library, library)) menus.Library = library;

            renderer.Begin(library);
            if (targetCursor is { } cursor && session.ActionService.CurrentTarget != null)
            {
                StockUiTargetWindow.DrawCursor(renderer, library, cursor, Layout.Scale, Stopwatch.GetTimestamp());
            }
            var groups = GroupParty(session);
            var party = DrawPartyWindow(renderer, library, session, groups, width, height);
            DrawAllianceWindows(renderer, library, groups, width, height);
            DrawLogWindows(renderer, library, session, party, width, height);
            DrawTargetWindow(renderer, library, session, party, width, height);
            DrawStatusIcons(renderer, library, session, width, height);
            DrawMenus(renderer, library, session, menus, width, height);
            renderer.End(framebuffer, width, height);
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
            if (open.Count == 0) return;
            long timestamp = Stopwatch.GetTimestamp();

            var rootFrame = open[0].Menu.Frame;
            var root = Layout.Resolve(StockUiWindowIds.MainMenu, rootFrame, width, height);
            if (root.Hidden) return;
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
                }
                else
                {
                    var frame = menu.Menu.Frame;
                    var authored = StockUiLayout.Place(frame.Anchor, frame.X, frame.Y, frame.Width, frame.Height, root.Scale, width, height);
                    float x = Math.Clamp(authored.X + dx, 0, Math.Max(0, width - frame.Width * root.Scale));
                    float y = Math.Clamp(authored.Y + dy, 0, Math.Max(0, height - frame.Height * root.Scale));
                    placement = new StockUiPlacement(x, y, root.Scale, false);
                }
                StockUiMenuWindow.Draw(renderer, library, _font, menu, placement, timestamp);
            }
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
            if (target == null || font == null || !library.TryGetMenu("targetwi", out var menu)) return;

            var placement = Layout.Resolve(StockUiWindowIds.Target, menu.Frame, width, height);
            if (placement.Hidden) return;
            if (!Layout.HasPositionOverride(StockUiWindowIds.Target) && party is { Hidden: false } p)
            {
                placement = placement with { Y = Math.Max(0, p.Y - (menu.Frame.Height + 2) * placement.Scale) };
            }
            renderer.DrawMenu(menu, placement, includeButtons: false);

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
            if (ids.Count == 0) return;

            var placement = Layout.Resolve(StockUiWindowIds.StatusIcons, grid.Frame, width, height);
            if (placement.Hidden) return;
            StockUiTargetWindow.DrawStatusIcons(renderer, icons, grid, placement, ids);
        }

        private readonly List<ChatLogLine> _logLines = new();

        /// <summary>
        /// The log window(s) and the chat input line. Window 1 keeps its authored left edge and, by default,
        /// stretches right to meet the party window (retail spans the bottom of the screen up to it; the authored
        /// 366 x 134 frame only fills the 512-wide layout); a log window the player has moved keeps its authored
        /// width. The Window 1/2 settings pick each window's frame ("log1".."log8" by "Maximum lines displayed", plus
        /// the title band) and its share of that width ("Window Width"); "Log Window Multi-window" adds Window 2 above
        /// Window 1 (Vertical) or beside it (Horizontal). The input line takes the bottom of Window 1, whose rows that
        /// no longer fit above it are not shown (retail capture, 2026-09-27). Each window is titled with the chat
        /// mode ("Say"; "Window 1:Say" and "Window 2" when split).
        /// </summary>
        private void DrawLogWindows(StockUiRenderer renderer, UiResourceLibrary library, CharacterSession session,
            StockUiPlacement? party, uint width, uint height)
        {
            var chat = session.Chat;
            int multi = Settings.GetValue(StockUiSettingKey.LogMultiWindow);
            chat.SetMultiWindow(multi != 0);
            var logFont = _logFont;
            if (!TryGetLogFrame(library, StockUiSettingKey.Window1MaxLines, out var menu1, out int maxRows1)) return;
            var placement = Layout.Resolve(StockUiWindowIds.Log, menu1.Frame, width, height);
            if (placement.Hidden) return;
            float s = placement.Scale;

            float fullWidth = menu1.Frame.Width;
            if (!Layout.HasPositionOverride(StockUiWindowIds.Log))
            {
                // Retail leaves a 2-pixel gap between the log and the party window (366 wide at 16 vs 384).
                float rightEdge = party is { Hidden: false } p ? p.X - 2 * s : width - 16 * s;
                fullWidth = Math.Max(menu1.Frame.Width, (rightEdge - placement.X) / s);
            }
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
            bool inputOpen = input.IsOpen && library.TryGetMenu("inline", out _);
            library.TryGetMenu("inline", out var inline);
            float textBottom1 = inputOpen ? height1 - inline.Frame.Height - 1 : height1 - StockUiChatWindow.BottomPadding;
            DrawLog(renderer, library, chat.Log, 1, menu1, window1, width1, height1,
                StockUiChatWindow.RowsThatFit(textBottom1, maxRows1), multi != 0 ? $"Window 1:{modeLabel}" : modeLabel,
                input.SelectedLogWindow == 1, logFont);

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
                    input.SelectedLogWindow == 2, logFont);
            }

            if (!inputOpen || logFont == null) return;
            float inputY = logBottom - inline.Frame.Height * s;
            StockUiChatWindow.DrawInput(renderer, library, logFont, inline, new StockUiPlacement(placement.X, inputY, s, false), width1,
                input, Stopwatch.GetTimestamp());

            if (input.IsModeListOpen && library.TryGetMenu("fep", out var fep))
            {
                // Above the input line's left end (and its tab), over the log.
                float listY = inputY - (StockUiChatWindow.TabHeight + fep.Frame.Height) * s;
                StockUiChatWindow.DrawModeList(renderer, library, fep, new StockUiPlacement(placement.X, Math.Max(0, listY), s, false), input);
            }
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
            // A window shows at most as many lines as rows (each line wraps to one row or more).
            log.CopyVisible(window, rows, _logLines);
            StockUiChatWindow.DrawLog(renderer, library, menu, logFont, _font, placement, frameWidth, frameHeight, rows, _logLines,
                Settings.GetValue(StockUiSettingKey.LogTimestamp), log.ScrollOffset(window) > 0, title, selected);
        }

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
        private StockUiPlacement? DrawPartyWindow(StockUiRenderer renderer, UiResourceLibrary library, CharacterSession session,
            PartyGroups groups, uint width, uint height)
        {
            int count = groups.InGroup ? Math.Clamp(groups.Own.Count, 1, 6) : 1;
            if (!library.TryGetMenu(groups.InGroup ? $"ptw{count}" : "ptw0", out var menu)) return null;

            var placement = Layout.Resolve(StockUiWindowIds.Party, menu.Frame, width, height);
            if (placement.Hidden) return placement;
            renderer.DrawMenu(menu, placement, includeButtons: false);

            var font = _font;
            if (font == null) return placement;
            var rows = GetPartyRows(session, groups, count);
            StockUiPartyWindow.Draw(renderer, font, menu, placement, rows, Layout.ShowPartyTp);
            if (Layout.ShowPartyStatusIcons && _statusIcons is { } icons)
            {
                StockUiPartyWindow.DrawStatusIcons(renderer, icons, menu, placement, rows, Layout.PartyStatusIconSide);
            }
            return placement;
        }

        /// <summary>
        /// The alliance's other parties, in the "raid1" (upper) and "raid2" (lower) windows: authored above the target
        /// window's slot, so they stay put whether or not something is targeted (as in retail captures).
        /// </summary>
        private void DrawAllianceWindows(StockUiRenderer renderer, UiResourceLibrary library, PartyGroups groups, uint width, uint height)
        {
            var font = _font;
            for (int i = 0; i < groups.Others.Count && i < 2; i++)
            {
                if (!library.TryGetMenu($"raid{i + 1}", out var menu)) continue;
                var placement = Layout.Resolve(i == 0 ? StockUiWindowIds.Alliance1 : StockUiWindowIds.Alliance2, menu.Frame, width, height);
                if (placement.Hidden) continue;
                renderer.DrawMenu(menu, placement, includeButtons: false);
                if (font == null) continue;

                var rows = new List<PartyRowVitals>(6);
                foreach (var m in groups.Others[i].Take(6)) rows.Add(ToRow(m));
                StockUiPartyWindow.DrawAllianceRows(renderer, font, menu, placement, rows);
            }
        }

        private static List<PartyRowVitals> GetPartyRows(CharacterSession session, PartyGroups groups, int count)
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
                rows.Add(ToRow(m));
            }
            return rows;
        }

        private static PartyRowVitals ToRow(PartyMember m) =>
            new(m.Name, (int)m.Hp, m.Hpp, (int)m.Mp, m.Mpp, (int)m.Tp, m.IsLeader, m.IsAllianceLeader, m.StatusEffectIds);

        private static int Percent(int value, int max) => max > 0 ? Math.Clamp(value * 100 / max, 0, 100) : 100;
    }
}
