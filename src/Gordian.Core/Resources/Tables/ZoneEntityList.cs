// src/Gordian.Core/Resources/Tables/ZoneEntityList.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace Gordian.Core.Resources.Tables
{
    /// <summary>
    /// A zone's entity list: the display names of its NPCs by server id. The retail client names the NPCs it sees
    /// from this table (the server only sends names for player characters and dynamic entities), which is why a
    /// LandSandBoat NPC arrives in S2C 0x00E without one.
    /// <para>
    /// File location referenced from XiEvents (https://github.com/atom0s/XiEvents, "Event DAT Files.md", "Zone
    /// Entities"): file id 6720 + zone for zones 0-255, 86491 + (zone - 256) for 256-299 (checked against the retail
    /// file table, 2026-09-28). Layout: 32-byte records, a 28-byte NUL-padded name then the entity's server id
    /// (uint32, little-endian), in the clear.
    /// </para>
    /// </summary>
    public sealed class ZoneEntityList
    {
        public const int RecordSize = 32;
        private const int NameSize = 28;

        private readonly Dictionary<uint, string> _names;

        private ZoneEntityList(Dictionary<uint, string> names) => _names = names;

        public int Count => _names.Count;

        /// <summary>The file id of a zone's entity list, or -1 for a zone id out of range.</summary>
        public static int GetFileId(int zoneId)
        {
            if (zoneId < 0 || zoneId > 299) return -1;
            return zoneId < 256 ? 6720 + zoneId : 86491 + (zoneId - 256);
        }

        /// <summary>Decodes an entity list DAT. Returns null when the buffer is not one (not a whole number of records, or no names).</summary>
        public static ZoneEntityList? Parse(ReadOnlySpan<byte> file)
        {
            if (file.Length < RecordSize || file.Length % RecordSize != 0) return null;
            var names = new Dictionary<uint, string>(file.Length / RecordSize);
            for (int offset = 0; offset + RecordSize <= file.Length; offset += RecordSize)
            {
                var record = file.Slice(offset, RecordSize);
                int length = record.Slice(0, NameSize).IndexOf((byte)0);
                if (length < 0) length = NameSize;
                if (length == 0) continue;
                for (int i = 0; i < length; i++)
                {
                    if (record[i] < 0x20 || record[i] > 0x7E) return null; // not a printable name: not this format
                }
                uint id = BinaryPrimitives.ReadUInt32LittleEndian(record.Slice(NameSize, 4));
                names.TryAdd(id, Encoding.ASCII.GetString(record.Slice(0, length)));
            }
            return names.Count > 0 ? new ZoneEntityList(names) : null;
        }

        /// <summary>The display name of an entity by server id, or null.</summary>
        public string? GetName(uint serverId) => _names.TryGetValue(serverId, out var name) ? name : null;

        /// <summary>Every (server id, name) pair, for tools and tests.</summary>
        public IEnumerable<KeyValuePair<uint, string>> Entries => _names;
    }

    /// <summary>
    /// Reads zone DATs by file id for the world and event code (the app's resource manager, set at startup).
    /// </summary>
    public static class ZoneDatLoader
    {
        public static Func<int, byte[]?>? Load { get; set; }
    }

    /// <summary>NPC names per zone from the zone entity lists, cached; empty when the DATs are not available.</summary>
    public static class ZoneEntityNames
    {
        private static readonly object Sync = new();
        private static int _zoneId = -1;
        private static ZoneEntityList? _list;

        /// <summary>The name of an NPC in a zone, or null when the zone's list does not carry it.</summary>
        public static string? Resolve(int zoneId, uint serverId)
        {
            var list = ListFor(zoneId);
            return list?.GetName(serverId);
        }

        private static ZoneEntityList? ListFor(int zoneId)
        {
            lock (Sync)
            {
                if (_zoneId == zoneId) return _list;
                _zoneId = zoneId;
                _list = null;
                var loader = ZoneDatLoader.Load;
                if (loader == null) return null;
                try
                {
                    var bytes = loader(ZoneEntityList.GetFileId(zoneId));
                    if (bytes != null) _list = ZoneEntityList.Parse(bytes);
                }
                catch (Exception)
                {
                    _list = null;
                }
                return _list;
            }
        }

        /// <summary>Drops the cache (tests, and a VFS reload through <see cref="ResourceManager.ClearCache"/>).</summary>
        public static void Reset()
        {
            lock (Sync)
            {
                _zoneId = -1;
                _list = null;
            }
        }
    }
}
