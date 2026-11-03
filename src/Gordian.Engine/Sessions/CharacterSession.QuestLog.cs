// src/Gordian.Engine/Sessions/CharacterSession.QuestLog.cs
using Gordian.Core.Resources.Tables;
using Gordian.Engine.Ui;

namespace Gordian.Engine.Sessions
{
    public sealed partial class CharacterSession
    {
        /// <summary>
        /// The Missions, Quests and Region Info windows (#488): the quest log from S2C 0x056 and the conquest figures from
        /// S2C 0x05E feed them, and the open lists and the Conquest page follow both packets.
        /// </summary>
        private void AttachQuestLog()
        {
            var menus = ActionService.Menus;
            menus.QuestLogState = () => StockUiQuestLogState.From(Progression);
            // The page shows the region under the map's cursor; the character's zone and region mark its own zone and decide
            // whose influence shares are known.
            StockUiConquestView Build(int shown)
            {
                int zone = World.CurrentZoneId;
                string name = shown >= 0 ? menus.RegionName?.Invoke(shown) ?? string.Empty : string.Empty;
                return StockUiConquest.Build(Progression, shown, name, menus.MenuMessageText, zone, menus.RegionName, menus.ZoneShortName,
                    ZoneRegions.RegionOf(zone));
            }
            menus.ConquestView = () => Build(ZoneRegions.RegionOf(World.CurrentZoneId));
            menus.ConquestRegionView = Build;
            Progression.MissionsUpdated += menus.RefreshQuestLog;
            menus.ConquestRequested = () => ProgressionModule.SendReqConquestAsync();
            Progression.ConquestUpdated += menus.RefreshConquest;
            // Besieged (S2C 0x05E), Campaign and Colonization (S2C 0x071) pages (#492).
            menus.RegionInfoView = map => StockUiRegionInfo.Build(map, Progression, World.CurrentZoneId, menus.MenuMessageText);
            Progression.ConquestUpdated += menus.RefreshRegionInfo;
            Progression.InfluenceUpdated += menus.RefreshRegionInfo;
        }
    }
}
