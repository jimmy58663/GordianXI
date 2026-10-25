// src/Gordian.Engine/Ui/MogHouseExitMenu.cs
using System;
using System.Collections.Generic;
using Gordian.Core.Network.Packets;
using Gordian.Core.World;

namespace Gordian.Engine.Ui
{
    /// <summary>
    /// The Mog House exit (#354): Confirm on the room's door asks <see cref="MogHouseRooms.ExitQuestion"/>. With no city
    /// exit unlocked (S2C 0x00A <c>MyRoomExitBit</c> 0) it is a Yes / No prompt and Yes leaves to the area entered from
    /// (<c>Retail Captures/Mog_House.mp4</c>, Aht Urhgan Whitegate, 2026-10-17). With one unlocked the same question
    /// lists the city's areas (<see cref="MogHouseRooms.ExitAreas"/>) and the chosen row is sent as its exit mode; the
    /// layout of that list is not confirmed against retail yet (no capture of a character with the exit quest done), so
    /// it uses the event query window with the area names from the zone name table.
    /// </summary>
    public static class MogHouseExitMenu
    {
        /// <summary>
        /// The rows of the area list for <paramref name="exitBit"/>: one per area, numbered by its exit mode (Option1 =
        /// 1, ...), named by <paramref name="zoneName"/> (or "Zone N" when the table has no name). The cursor starts on
        /// <paramref name="currentZone"/>, the area the player entered from, else on the first row. Empty when the bit
        /// has no areas: the exit is then the Yes / No prompt.
        /// </summary>
        public static IReadOnlyList<StockUiQueryOption> Options(MogHouseExitBit exitBit, ushort currentZone, Func<int, string?>? zoneName, out int cursorIndex)
        {
            cursorIndex = 0;
            var areas = MogHouseRooms.ExitAreas(exitBit);
            if (areas.Count == 0) return Array.Empty<StockUiQueryOption>();
            var options = new StockUiQueryOption[areas.Count];
            for (int i = 0; i < areas.Count; i++)
            {
                string? name = zoneName?.Invoke(areas[i]);
                options[i] = new StockUiQueryOption(i + 1, string.IsNullOrEmpty(name) ? $"Zone {areas[i]}" : name);
                if (areas[i] == currentZone) cursorIndex = i;
            }
            return options;
        }

        /// <summary>
        /// The exit mode for a query answer: the row's Option mode, or null for Cancel (255) or a number outside the list.
        /// </summary>
        public static MogHouseExitMode? ModeOf(int answer, int optionCount) =>
            answer >= 1 && answer <= optionCount && answer <= (int)MogHouseExitMode.Option4 ? (MogHouseExitMode)answer : null;
    }
}
