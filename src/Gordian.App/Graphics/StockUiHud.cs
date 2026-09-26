// src/Gordian.App/Graphics/StockUiHud.cs
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Gordian.Core.Diagnostics;
using Gordian.Core.Network;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Ui;
using Gordian.Core.Ui;
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
        private int _loadStarted;

        /// <summary>The layout used for the last frame (the session's shared layout, see StockUiLayoutStore).</summary>
        public StockUiLayout Layout { get; private set; } = new();

        /// <summary>Master switch for the whole stock HUD.</summary>
        public bool Enabled { get; set; } = true;

        public UiResourceLibrary? Library => _library;

        /// <summary>
        /// Starts loading the UI resources in the background (idempotent).
        /// </summary>
        public void EnsureLoading(ResourceManager? resources)
        {
            if (resources == null || Interlocked.CompareExchange(ref _loadStarted, 1, 0) != 0) return;
            Task.Run(() =>
            {
                try
                {
                    var library = UiResourceLibrary.Load(resources);
                    if (library == null) return;
                    _font = UiFont.FromLibrary(library);
                    _statusIcons = StatusIconLibrary.Load(resources);
                    _library = library;
                }
                catch (Exception ex)
                {
                    GordianLog.Error("UI", $"Failed to load stock UI resources: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Draws the HUD for a session over the framebuffer's current contents.
        /// </summary>
        public void Render(StockUiRenderer renderer, CharacterSession? session, Framebuffer framebuffer, uint width, uint height)
        {
            var library = _library;
            if (!Enabled || library == null || session == null) return;
            Layout = session.ActionService.UiLayout;

            renderer.Begin(library);
            var party = DrawPartyWindow(renderer, library, session, width, height);
            DrawLogWindow(renderer, library, party, width, height);
            DrawTargetWindow(renderer, library, session, party, width, height);
            DrawStatusIcons(renderer, library, session, width, height);
            renderer.End(framebuffer, width, height);
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

        /// <summary>
        /// The log window keeps its authored left edge and, by default, stretches right to meet the party window
        /// (retail spans the bottom of the screen up to it; the authored 366 x 134 frame only fills the 512-wide
        /// layout). A log window the player has moved keeps its authored width.
        /// </summary>
        private void DrawLogWindow(StockUiRenderer renderer, UiResourceLibrary library, StockUiPlacement? party, uint width, uint height)
        {
            if (!library.TryGetMenu("logwindo", out var menu)) return;
            var placement = Layout.Resolve(StockUiWindowIds.Log, menu.Frame, width, height);
            if (placement.Hidden) return;

            float? frameWidth = null;
            if (!Layout.HasPositionOverride(StockUiWindowIds.Log))
            {
                // Retail leaves a 2-pixel gap between the log and the party window (366 wide at 16 vs 384).
                float rightEdge = party is { Hidden: false } p ? p.X - 2 * placement.Scale : width - 16 * placement.Scale;
                frameWidth = Math.Max(menu.Frame.Width, (rightEdge - placement.X) / placement.Scale);
            }
            renderer.DrawMenu(menu, placement, includeButtons: false, frameWidth);
        }

        /// <summary>
        /// The party window uses "ptw0" (titled Solo) outside a party and "ptw1".."ptw6" by member count in one.
        /// </summary>
        private StockUiPlacement? DrawPartyWindow(StockUiRenderer renderer, UiResourceLibrary library, CharacterSession session, uint width, uint height)
        {
            var members = session.Party.Members;
            bool inParty = session.Party.IsInParty && members.Count > 1;
            int count = inParty ? Math.Clamp(members.Count, 1, 6) : 1;
            if (!library.TryGetMenu(inParty ? $"ptw{count}" : "ptw0", out var menu)) return null;

            var placement = Layout.Resolve(StockUiWindowIds.Party, menu.Frame, width, height);
            if (placement.Hidden) return placement;
            renderer.DrawMenu(menu, placement, includeButtons: false);

            var font = _font;
            if (font == null) return placement;
            StockUiPartyWindow.Draw(renderer, font, menu, placement, GetPartyRows(session, inParty, count), Layout.ShowPartyTp);
            return placement;
        }

        private static List<PartyRowVitals> GetPartyRows(CharacterSession session, bool inParty, int count)
        {
            var rows = new List<PartyRowVitals>(count);
            var local = session.LocalPlayer;
            if (!inParty)
            {
                rows.Add(new PartyRowVitals(session.CharacterName, local.CurrentHp, Percent(local.CurrentHp, local.MaxHp),
                    local.CurrentMp, Percent(local.CurrentMp, local.MaxMp), local.CurrentTp, IsLeader: false));
                return rows;
            }

            var members = session.Party.Members;
            for (int i = 0; i < count && i < members.Count; i++)
            {
                var m = members[i];
                if (m.ServerId != 0 && m.ServerId == local.ServerId)
                {
                    // Our own row reads the local player's live vitals (always current; the party copy only
                    // updates when the server relays 0x0DF/0x0DD for us).
                    string ownName = m.Name.Length > 0 ? m.Name : session.CharacterName;
                    rows.Add(new PartyRowVitals(ownName, local.CurrentHp, Percent(local.CurrentHp, local.MaxHp),
                        local.CurrentMp, Percent(local.CurrentMp, local.MaxMp), local.CurrentTp, m.IsLeader));
                    continue;
                }
                rows.Add(new PartyRowVitals(m.Name, (int)m.Hp, m.Hpp, (int)m.Mp, m.Mpp, (int)m.Tp, m.IsLeader));
            }
            return rows;
        }

        private static int Percent(int value, int max) => max > 0 ? Math.Clamp(value * 100 / max, 0, 100) : 100;
    }
}
