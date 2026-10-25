// src/Gordian.Engine/Ui/MogHouseExitMenu.cs
using System;
using System.Collections.Generic;
using Gordian.Core.Network.Packets;
using Gordian.Core.World;

namespace Gordian.Engine.Ui
{
    /// <summary>
    /// The Mog House exit (#354). Confirm on the room's door with no city exit unlocked (S2C 0x00A <c>MyRoomExitBit</c> 0)
    /// asks <see cref="MogHouseRooms.ExitQuestion"/>, Yes / No, and Yes leaves to the area entered from
    /// (<c>Retail Captures/Mog_House.mp4</c>, Aht Urhgan Whitegate, 2026-10-17). With one unlocked it is retail's exit
    /// menu (image-23 of round 1, 2026-10-24): "Which area do you want to exit to?" over "Stay in your room.", "Area you
    /// entered from.", "Change floors." (greyed unless <see cref="MogHouseRooms.CanChangeFloors"/>) and "Select an area to
    /// exit to.", the cursor on the first row. The last opens the area list: the city's areas
    /// (<see cref="MogHouseRooms.ExitAreas"/>, named from the zone name table) and, once it is open to the character, the Mog Garden. The texts are the menus'
    /// own messages (ROM/165/72, <c>DMsgCategory.MenuMessages</c>).
    /// </summary>
    public static class MogHouseExitMenu
    {
        /// <summary>The menu message ids (ROM/165/72) of the question and the rows, read 2026-10-25.</summary>
        public const int QuestionText = 69, AreaEnteredFromText = 70, StayText = 184, SelectAreaText = 185, ChangeFloorsText = 293;

        /// <summary>The texts as the table has them, for when it is not loaded.</summary>
        public const string QuestionFallback = "Which area do you want to exit to?", AreaEnteredFromFallback = "Area you entered from.",
            StayFallback = "Stay in your room.", SelectAreaFallback = "Select an area to exit to.", ChangeFloorsFallback = "Change floors.";

        /// <summary>The main menu's rows, by the number its query answers with.</summary>
        public const int Stay = 1, AreaEnteredFrom = 2, ChangeFloors = 3, SelectArea = 4;

        /// <summary>Rows shown at once: image-23 shows the four rows under the question without a scrollbar.</summary>
        public const int VisibleRows = 4;

        /// <summary>Whether the exit bit opens the exit menu (it has areas) rather than the Yes / No prompt.</summary>
        public static bool HasMenu(MogHouseExitBit exitBit) => MogHouseRooms.ExitAreas(exitBit).Count > 0;

        /// <summary>The question, from the menu message table or its fallback.</summary>
        public static string Question(Func<int, string?>? menuText) => Text(menuText, QuestionText, QuestionFallback);

        /// <summary>The main menu's four rows in retail's order; "Change floors." greyed unless <paramref name="canChangeFloors"/>.</summary>
        public static IReadOnlyList<StockUiQueryOption> MainOptions(Func<int, string?>? menuText, bool canChangeFloors) => new[]
        {
            new StockUiQueryOption(Stay, Text(menuText, StayText, StayFallback)),
            new StockUiQueryOption(AreaEnteredFrom, Text(menuText, AreaEnteredFromText, AreaEnteredFromFallback)),
            new StockUiQueryOption(ChangeFloors, Text(menuText, ChangeFloorsText, ChangeFloorsFallback), Greyed: !canChangeFloors),
            new StockUiQueryOption(SelectArea, Text(menuText, SelectAreaText, SelectAreaFallback)),
        };

        /// <summary>
        /// The area list for <paramref name="exitBit"/>: one row per city area, numbered by its exit mode (Option1 = 1, ...),
        /// then the Mog Garden (mode 127) when <paramref name="mogGarden"/> (<see cref="MogHouseRooms.OffersMogGarden"/>), each named by <paramref name="zoneName"/> (or "Zone N" when the table has no
        /// name). The cursor starts on <paramref name="currentZone"/>, the area the player entered from, else on the first
        /// row. Empty when the bit has no areas.
        /// </summary>
        public static IReadOnlyList<StockUiQueryOption> AreaOptions(MogHouseExitBit exitBit, ushort currentZone, Func<int, string?>? zoneName, bool mogGarden, out int cursorIndex)
        {
            cursorIndex = 0;
            var areas = MogHouseRooms.ExitAreas(exitBit);
            if (areas.Count == 0) return Array.Empty<StockUiQueryOption>();
            var options = new StockUiQueryOption[areas.Count + (mogGarden ? 1 : 0)];
            for (int i = 0; i < areas.Count; i++)
            {
                options[i] = new StockUiQueryOption(i + 1, ZoneText(zoneName, areas[i]));
                if (areas[i] == currentZone) cursorIndex = i;
            }
            if (mogGarden) options[^1] = new StockUiQueryOption((int)MogHouseExitMode.MogGarden, ZoneText(zoneName, MogHouseRooms.MogGardenZone));
            return options;
        }

        /// <summary>The exit mode for an area list answer: a city area's Option mode or the Mog Garden, null for Cancel (255) or anything else.</summary>
        public static MogHouseExitMode? AreaModeOf(int answer, int areaCount) =>
            answer == (int)MogHouseExitMode.MogGarden ? MogHouseExitMode.MogGarden
            : answer >= 1 && answer <= areaCount && answer <= (int)MogHouseExitMode.Option4 ? (MogHouseExitMode)answer : null;

        private static string Text(Func<int, string?>? menuText, int id, string fallback) =>
            menuText?.Invoke(id) is { Length: > 0 } text ? text : fallback;

        private static string ZoneText(Func<int, string?>? zoneName, ushort zone) =>
            zoneName?.Invoke(zone) is { Length: > 0 } name ? name : $"Zone {zone}";
    }
}
