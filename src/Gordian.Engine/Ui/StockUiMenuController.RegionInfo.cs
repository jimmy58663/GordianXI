// src/Gordian.Engine/Ui/StockUiMenuController.RegionInfo.cs
using System;
using Gordian.Core.Resources.Tables;

namespace Gordian.Engine.Ui
{
    public sealed partial class StockUiOpenMenu
    {
        /// <summary>A Besieged, Campaign or Colonization page's panels, markers and help bar (#492), rebuilt when its cursor or packets change.</summary>
        public StockUiRegionInfoView? RegionInfoView { get; internal set; }
    }

    public sealed partial class StockUiMenuController
    {
        /// <summary>
        /// A Besieged, Campaign or Colonization page's view for the point under its cursor (-1 for none) from the session's
        /// state (#492); set by the session.
        /// </summary>
        public Func<RegionMap, int, StockUiRegionInfoView?>? RegionInfoView { get; set; }

        /// <summary>Asks the server for the currencies the pages' boxes show (C2S 0x10F, 0x115); set by the session.</summary>
        public Action? RegionInfoFiguresRequested { get; set; }

        /// <summary>
        /// Region Info's Besieged, Campaign or Colonization page (#492): its region map behind at the conquest map's size, with
        /// the conquest map's cursor, and its panels (<see cref="StockUiRegionInfo"/>). Asks the server for the figures as it
        /// opens (C2S 0x05A: S2C 0x05E carries Besieged, and LandSandBoat answers with S2C 0x071's Colonization).
        /// </summary>
        public StockUiOpenMenu? OpenRegionInfo(RegionMap map, StockUiOpenMenu? parent)
        {
            if (ConquestRequested is { } request) _ = request();
            RegionInfoFiguresRequested?.Invoke();
            var (u, v, point) = StockUiRegionInfo.OpeningPoint(map, CurrentZone?.Invoke() ?? 0);
            if (RegionInfoView?.Invoke(map, point) is not { } view) return null;
            var definition = QuestLogDefinition(view.Panels.Count > 0 ? view.Panels[0].Window : StockUiRegionInfo.CampaignWindow, 0, 0, 0, 0, 0, 0);
            if (definition == null) return null;
            var page = new StockUiOpenMenu(definition, parent, Array.Empty<string>(), null)
            {
                QuestLogView = StockUiQuestLogView.RegionInfo,
                RegionMap = map,
                ItemOwner = this,
            };
            lock (_sync)
            {
                page.RegionInfoView = view;
                page.MapZoom = StockUiConquest.MapZoom;
                page.SetMapCenter(u, v);
                page.SetMapCursor(u, v);
                PushOpen(page);
            }
            Changed?.Invoke();
            return page;
        }

        /// <summary>Moves a Region Info page's cursor by (<paramref name="dx"/>, <paramref name="dy"/>) map px; the point it comes to selects the page's windows.</summary>
        private void MoveRegionInfoCursor(StockUiOpenMenu page, float dx, float dy)
        {
            page.SetMapCursor(page.MapCursorX + dx / StockUiConquest.MapSize, page.MapCursorY + dy / StockUiConquest.MapSize);
            if (page.RegionMap is not { } map) return;
            int point = StockUiRegionInfo.PointAt(map, page.MapCursorX * StockUiConquest.MapSize, page.MapCursorY * StockUiConquest.MapSize);
            if (page.RegionInfoView is { } shown && shown.Selected == point) return;
            if (RegionInfoView?.Invoke(map, point) is { } view) page.RegionInfoView = view;
        }

        /// <summary>S2C 0x05E, 0x071 or the currencies changed: an open Besieged, Campaign or Colonization page shows the new figures.</summary>
        public void RefreshRegionInfo()
        {
            bool changed = false;
            lock (_sync)
            {
                foreach (var menu in _open)
                {
                    if (menu.QuestLogView != StockUiQuestLogView.RegionInfo || menu.RegionMap is not { } map) continue;
                    if (RegionInfoView?.Invoke(map, menu.RegionInfoView?.Selected ?? -1) is { } view) menu.RegionInfoView = view;
                    changed = true;
                }
            }
            if (changed) Changed?.Invoke();
        }
    }
}
