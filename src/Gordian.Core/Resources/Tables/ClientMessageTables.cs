// src/Gordian.Core/Resources/Tables/ClientMessageTables.cs
using System;

namespace Gordian.Core.Resources.Tables
{
    /// <summary>
    /// The client's own message tables, in the zone dialog table format (<see cref="ZoneDialogTable"/>: the 0x10 header
    /// dword, the body XOR 0x80, the offset table) and the same control codes (<see cref="EventMessageDecoder"/>):
    /// <list type="bullet">
    /// <item>System messages, English file id 7031 (<c>ROM/27/76</c>), Japanese 7030 (<c>ROM/27/75</c>): what S2C 0x053
    /// (and, by the same ids, S2C 0x009) prints. 326 messages; message 2 is "You could not enter the next area.", 88 the
    /// <c>/random</c> roll. File ids referenced from XiPackets (https://github.com/atom0s/XiPackets,
    /// <c>world/server/0x0053</c>).</item>
    /// <item>Emote messages, English file id 7025 (<c>ROM/27/70</c>), Japanese 7024 (<c>ROM/27/69</c>): the log lines of
    /// S2C 0x05A, two per emote id, <c>2 * id</c> with a target ("{caster} waves to {target}.") and <c>2 * id + 1</c>
    /// without ("{caster} waves."). <b>Beyond XiPackets:</b> it does not name the file; found by searching the retail
    /// DATs for the emote text and checked against LandSandBoat's emote ids (<c>enums/emote.h</c>: 43 Hurray is message
    /// 86 / 87 "gives a triumphant cry", 65 Dance1 is 130 / 131 "samba", 73 Bell 146 / 147) on 2026-10-03.</item>
    /// </list>
    /// The file ids resolve through the retail file table to those paths (checked 2026-10-03).
    /// </summary>
    public sealed class ClientMessageTables
    {
        public const int SystemMessagesFileId = 7031;
        public const int SystemMessagesJapaneseFileId = 7030;
        public const int EmoteMessagesFileId = 7025;
        public const int EmoteMessagesJapaneseFileId = 7024;

        private readonly Func<int, byte[]?>? _loader;
        private readonly object _sync = new();
        private ZoneDialogTable? _system;
        private ZoneDialogTable? _emotes;
        private bool _systemTried;
        private bool _emotesTried;

        /// <summary>Tables read through <paramref name="loader"/>, or through <see cref="ZoneDatLoader.Load"/> when null.</summary>
        public ClientMessageTables(Func<int, byte[]?>? loader = null)
        {
            _loader = loader;
        }

        /// <summary>The system message table (S2C 0x053), or null when the DAT is not available.</summary>
        public ZoneDialogTable? SystemMessages => Get(SystemMessagesFileId, ref _system, ref _systemTried);

        /// <summary>The emote message table (S2C 0x05A), or null when the DAT is not available.</summary>
        public ZoneDialogTable? EmoteMessages => Get(EmoteMessagesFileId, ref _emotes, ref _emotesTried);

        /// <summary>The emote table message of an emote id: <c>2 * id</c> with a target, <c>2 * id + 1</c> without.</summary>
        public static int EmoteMessageId(int emoteId, bool targeted) => (emoteId * 2) + (targeted ? 0 : 1);

        /// <summary>Drops the loaded tables (a VFS reload), so the next read loads them again.</summary>
        public void Reset()
        {
            lock (_sync)
            {
                _system = null;
                _emotes = null;
                _systemTried = false;
                _emotesTried = false;
            }
        }

        private ZoneDialogTable? Get(int fileId, ref ZoneDialogTable? table, ref bool tried)
        {
            lock (_sync)
            {
                if (tried) return table;
                var loader = _loader ?? ZoneDatLoader.Load;
                // Not marked as tried without a loader: the app sets it after the sessions start.
                if (loader == null) return null;
                tried = true;
                try
                {
                    if (loader(fileId) is { } bytes) table = ZoneDialogTable.Parse(bytes);
                }
                catch (Exception)
                {
                    table = null;
                }
                return table;
            }
        }
    }
}
