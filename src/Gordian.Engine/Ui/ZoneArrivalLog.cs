// src/Gordian.Engine/Ui/ZoneArrivalLog.cs
using System;

namespace Gordian.Engine.Ui
{
    /// <summary>
    /// The zone-name line the client prints in the chat log on every zone-in (#579). Retail (local LSB capture 2026-11-15,
    /// <c>579-zone-arrival.png</c>): <c>=== Area: La Theine Plateau ===</c>, white like the other system text, printed with the
    /// zone-in itself (within 4 s of a <c>!zone</c>, while "Downloading data..." is still up and before any NPC spawns); a
    /// <c>!zone</c> into the zone the character is already in prints it again. The server sends no such line (LandSandBoat has
    /// none), so it is the client's, from the zone name table (<c>ROM/165/84</c>).
    /// <para>One per S2C 0x00A (<c>WorldState.ZoneEntered</c>), first login included. Each session has its own log, so a
    /// Ctrl+Tab session switch prints nothing. The zone name table is the app's: a zone-in before the app has attached it (the
    /// first login) is held and printed once it is.</para>
    /// </summary>
    public sealed class ZoneArrivalLog
    {
        private readonly object _lock = new();
        private readonly Action<string> _print;
        private int _pendingZone = -1;

        /// <param name="print">Adds a line to the session's chat log (system channel).</param>
        public ZoneArrivalLog(Action<string> print) => _print = print ?? throw new ArgumentNullException(nameof(print));

        /// <summary>The line for a zone name.</summary>
        public static string Format(string zoneName) => $"=== Area: {zoneName} ===";

        /// <summary>A zone-in: prints the line, or holds it until <paramref name="zoneName"/> can be read.</summary>
        public void OnZoneEntered(ushort zoneId, Func<int, string?>? zoneName)
        {
            lock (_lock)
            {
                if (zoneName == null)
                {
                    _pendingZone = zoneId;
                    return;
                }
                _pendingZone = -1;
            }
            Print(zoneId, zoneName);
        }

        /// <summary>The zone name table is now readable: prints a held zone-in, if any.</summary>
        public void OnZoneNamesAttached(Func<int, string?>? zoneName)
        {
            if (zoneName == null) return;
            int zone;
            lock (_lock)
            {
                zone = _pendingZone;
                _pendingZone = -1;
            }
            if (zone >= 0) Print(zone, zoneName);
        }

        private void Print(int zoneId, Func<int, string?> zoneName)
        {
            string? name = zoneName(zoneId);
            if (!string.IsNullOrWhiteSpace(name)) _print(Format(name.Trim()));
        }
    }
}
