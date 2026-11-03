// src/Gordian.Engine/Ui/StockUiMenuController.RegionInfo.cs
using System;
using Gordian.Core.Resources.Tables;

namespace Gordian.Engine.Ui
{
    public sealed partial class StockUiOpenMenu
    {
        /// <summary>A Besieged, Campaign or Colonization page's panels and help bar (#492), rebuilt when its packets change.</summary>
        public StockUiRegionInfoView? RegionInfoView { get; internal set; }
    }

    public sealed partial class StockUiMenuController
    {
        /// <summary>A Besieged, Campaign or Colonization page's view from the session's state (#492); set by the session.</summary>
        public Func<RegionMap, StockUiRegionInfoView?>? RegionInfoView { get; set; }

        /// <summary>
        /// Region Info's Besieged, Campaign or Colonization page (#492): its region map behind (panned by the movement input
        /// at the conquest map's size, never zoomed) and its panels (<see cref="StockUiRegionInfo"/>). Asks the server for the
        /// figures as it opens (C2S 0x05A: S2C 0x05E carries Besieged, and LandSandBoat answers with S2C 0x071's Colonization).
        /// </summary>
        public StockUiOpenMenu? OpenRegionInfo(RegionMap map, StockUiOpenMenu? parent)
        {
            if (ConquestRequested is { } request) _ = request();
            if (RegionInfoView?.Invoke(map) is not { Panels.Count: > 0 } view) return null;
            var definition = QuestLogDefinition(view.Panels[0].Window, 0, 0, 0, 0, 0, 0);
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
                PushOpen(page);
            }
            Changed?.Invoke();
            return page;
        }

        /// <summary>S2C 0x05E or 0x071 changed: an open Besieged, Campaign or Colonization page shows the new figures.</summary>
        public void RefreshRegionInfo()
        {
            bool changed = false;
            lock (_sync)
            {
                foreach (var menu in _open)
                {
                    if (menu.QuestLogView != StockUiQuestLogView.RegionInfo || menu.RegionMap is not { } map) continue;
                    if (RegionInfoView?.Invoke(map) is { } view) menu.RegionInfoView = view;
                    changed = true;
                }
            }
            if (changed) Changed?.Invoke();
        }
    }
}
