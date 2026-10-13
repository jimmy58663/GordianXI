using System;
using System.Collections.Generic;
using System.Numerics;
using Gordian.Core.Resources.Graphics;

namespace Gordian.Core.World.Collision
{
    /// <summary>
    /// Zone lines (#350): the Section 0x36 <c>z</c> volumes of a zone DAT. The legacy client asks for the zone change
    /// itself when the player walks into one, sending C2S 0x05E (MAPRECT) with the volume's four-character id and the
    /// player's position; the server looks the id up in its own zone-line table and answers with the zone change
    /// (S2C 0x00B) or a refusal. Volume ids and the request referenced from xi-tools (https://github.com/vekien/xi-tools,
    /// docs/zone/subareas.md: the <c>z</c> kind of <c>KO_RectData</c>) and LandSandBoat
    /// (https://github.com/LandSandBoat/server, packets/c2s/0x05e_maprect.cpp and data/zones/*/zone.yaml <c>zonelines</c>).
    /// </summary>
    public static class ZoneLines
    {
        /// <summary>The Mog House exit line, the same id in every Mog House; it carries the exit bit and mode instead of a table row.</summary>
        public const string MogHouseExitId = "zmrq";

        /// <summary>
        /// Whether a zone-line id is a line to cross (one the server has a row for) rather than its landing. The DAT's
        /// <c>z</c> records come in pairs, numbered by the id's last character in base 36: the even one is the line, the
        /// odd one after it the small box on the far side of the next zone's line where the player arrives (LandSandBoat's
        /// <c>at</c> positions are those boxes' centres, e.g. East Ronfaure's <c>z2t2</c> lands at Southern San d'Oria's
        /// <c>z6e1</c>). All 844 LandSandBoat zone lines end in an even character and none in an odd one; asking for an
        /// odd one gets the refusal (unknown id). The pairing is an inference from the data (#350).
        /// </summary>
        public static bool IsCrossing(string id)
        {
            if (id.Length != 4 || id[0] != 'z') return false;
            int digit = Base36(id[3]);
            return digit >= 0 && digit % 2 == 0;
        }

        /// <summary>
        /// A Mog House entrance (<c>zmr?</c> in the starting cities, <c>zms?</c> in the [S] cities and Adoulin, per
        /// LandSandBoat's 0x05e_maprect.cpp), not the exit <see cref="MogHouseExitId"/>.
        /// </summary>
        public static bool IsMogHouseEntrance(string id) =>
            id.Length == 4 && id != MogHouseExitId && (id.StartsWith("zmr", StringComparison.Ordinal) || id.StartsWith("zms", StringComparison.Ordinal));

        /// <summary>
        /// Whether walking into the line sends the request: a crossing (<see cref="IsCrossing"/>) that is not a Mog House
        /// entrance. GordianXI does not draw the Mog House room yet, and its way out (the <c>zmrq</c> line or the exit
        /// menu of the room) comes with it, so entering one would leave the player stuck in the city with no exit.
        /// </summary>
        public static bool IsRequestable(string id) => IsCrossing(id) && !IsMogHouseEntrance(id);

        private static int Base36(char c) => c switch
        {
            >= '0' and <= '9' => c - '0',
            >= 'a' and <= 'z' => c - 'a' + 10,
            >= 'A' and <= 'Z' => c - 'A' + 10,
            _ => -1,
        };

        /// <summary>The zone-line records of a zone's interaction tables, one per id (the first one wins).</summary>
        public static IReadOnlyList<ZoneInteraction> Select(IEnumerable<ZoneInteraction> interactions)
        {
            var lines = new List<ZoneInteraction>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var record in interactions)
            {
                if (!record.IsZoneLine || record.Size.X <= 0.0f || record.Size.Y <= 0.0f || record.Size.Z <= 0.0f || !seen.Add(record.Id)) continue;
                lines.Add(record);
            }
            return lines.Count == 0 ? Array.Empty<ZoneInteraction>() : lines;
        }

        /// <summary>The first requestable zone line (<see cref="IsRequestable"/>) whose box holds <paramref name="position"/>, or null.</summary>
        public static ZoneInteraction? Find(IReadOnlyList<ZoneInteraction> lines, Vector3 position)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                if (IsRequestable(lines[i].Id) && lines[i].Contains(position)) return lines[i];
            }
            return null;
        }
    }

    /// <summary>
    /// Decides when to send the zone-line request (#350), once per crossing:
    /// <list type="bullet">
    /// <item>It fires when the player is inside a zone line and armed. It is armed only after the player has been seen
    /// outside every requestable zone line of the zone (<see cref="ZoneLines.IsRequestable"/>; landing boxes and Mog House
    /// entrances do not count), so arriving inside one (a spawn point on the line) never sends the player
    /// straight back, and a refused request (LandSandBoat turns the player round where they stand with S2C 0x065 and
    /// prints "could not enter", <c>denyZone</c> in 0x05e_maprect.cpp) is not repeated until the player walks out and
    /// back in.</item>
    /// <item>While a request is pending nothing more is sent; it stops pending when the zone changes or after
    /// <see cref="PendingTimeoutSeconds"/>.</item>
    /// </list>
    /// </summary>
    public sealed class ZoneLineMonitor
    {
        /// <summary>
        /// How long a request stays pending without an answer (a safety limit: the screen is black while it is pending,
        /// and the retail recording waited 2.2 s and 5.3 s in the black for the zone change).
        /// </summary>
        public const double PendingTimeoutSeconds = 15.0;

        /// <summary>The server refused the request (it placed the player instead): stop waiting. The player must still leave the line.</summary>
        public void CancelPending()
        {
            PendingId = null;
            _pendingSince = double.NaN;
        }

        private ushort _zoneId;
        private bool _armed;
        private double _pendingSince = double.NaN;

        /// <summary>The id of the request waiting for the server's answer, or null.</summary>
        public string? PendingId { get; private set; }

        /// <summary>Forgets the zone: the next update starts disarmed.</summary>
        public void Reset()
        {
            _zoneId = 0;
            _armed = false;
            PendingId = null;
            _pendingSince = double.NaN;
        }

        /// <summary>
        /// Updates the monitor with the player's position in <paramref name="zoneId"/> and returns the zone line to
        /// request now, or null. <paramref name="canSend"/> is false while the player may not cross (not in the world, a
        /// zone change already under way, an event running); the player is then neither armed nor sent.
        /// </summary>
        public ZoneInteraction? Update(ushort zoneId, IReadOnlyList<ZoneInteraction>? lines, Vector3 position, bool canSend, double nowSeconds)
        {
            if (zoneId != _zoneId)
            {
                Reset();
                _zoneId = zoneId;
            }
            if (PendingId != null && nowSeconds - _pendingSince >= PendingTimeoutSeconds)
            {
                PendingId = null;
                _pendingSince = double.NaN;
            }
            if (lines == null || lines.Count == 0 || zoneId == 0) return null;

            var line = ZoneLines.Find(lines, position);
            if (line == null)
            {
                if (canSend) _armed = true;
                return null;
            }
            if (!canSend || !_armed || PendingId != null) return null;

            _armed = false;
            PendingId = line.Value.Id;
            _pendingSince = nowSeconds;
            return line;
        }
    }
}
