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
            menus.ConquestView = () =>
            {
                int zone = World.CurrentZoneId;
                int region = ZoneRegions.RegionOf(zone);
                string name = region >= 0 ? menus.RegionName?.Invoke(region) ?? string.Empty : string.Empty;
                return StockUiConquest.Build(Progression, region, name, menus.MenuMessageText, zone, menus.RegionName, menus.ZoneShortName);
            };
            Progression.MissionsUpdated += menus.RefreshQuestLog;
            menus.ConquestRequested = () => ProgressionModule.SendReqConquestAsync();
            Progression.ConquestUpdated += menus.RefreshConquest;
        }
    }
}
