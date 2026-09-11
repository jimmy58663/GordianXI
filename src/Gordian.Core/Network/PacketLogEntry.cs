// src/Gordian.Core/Network/PacketLogEntry.cs
using System;
using System.Buffers.Binary;
using System.Text;

namespace Gordian.Core.Network
{
    /// <summary>
    /// Indicates whether a packet was received from the server or dispatched by the client.
    /// </summary>
    public enum PacketDirection
    {
        Inbound,
        Outbound
    }

    /// <summary>
    /// Represents an immutable log entry for an inspected network packet or sub-packet,
    /// formatted for low-overhead logging and UI diagnostics.
    /// </summary>
    public sealed class PacketLogEntry
    {
        public DateTime Timestamp { get; init; } = DateTime.UtcNow;
        public PacketDirection Direction { get; init; }
        public ushort PacketId { get; init; }
        public string PacketName { get; init; } = string.Empty;
        public ushort SequenceId { get; init; }
        public int Size { get; init; }
        public byte[] RawBytes { get; init; } = Array.Empty<byte>();

        public string HexPreview => FormatHexPreview(RawBytes);
        public string HexAsciiDump => FormatHexAscii(RawBytes);

        /// <summary>
        /// Resolves a human-readable protocol command name from a packet ID and direction
        /// using the complete LandSandBoat / XiPackets game protocol specification.
        /// </summary>
        public static string ResolvePacketName(ushort packetId, PacketDirection direction)
        {
            if (direction == PacketDirection.Inbound)
            {
                return packetId switch
                {
                    0x005 => "GP_SERV_PACKETCONTROL",
                    0x006 => "GP_SERV_NARAKU",
                    0x008 => "GP_SERV_ENTERZONE",
                    0x009 => "GP_SERV_MESSAGE",
                    0x00A => "GP_SERV_LOGIN",
                    0x00B => "GP_SERV_LOGOUT",
                    0x00D => "GP_SERV_CHAR_PC",
                    0x00E => "GP_SERV_CHAR_NPC",
                    0x012 => "GP_SERV_GM",
                    0x013 => "GP_SERV_GMCOMMAND",
                    0x015 => "GP_SERV_PING",
                    0x017 => "GP_SERV_CHAT_STD",
                    0x01B => "GP_SERV_JOB_INFO",
                    0x01C => "GP_SERV_ITEM_MAX",
                    0x01D => "GP_SERV_ITEM_SAME",
                    0x01E => "GP_SERV_ITEM_NUM",
                    0x01F => "GP_SERV_ITEM_LIST",
                    0x020 => "GP_SERV_ITEM_ATTR",
                    0x021 => "GP_SERV_ITEM_TRADE_REQ",
                    0x022 => "GP_SERV_ITEM_TRADE_RES",
                    0x023 => "GP_SERV_ITEM_TRADE_LIST",
                    0x025 => "GP_SERV_ITEM_TRADE_MYLIST",
                    0x026 => "GP_SERV_ITEM_SUBCONTAINER",
                    0x027 => "GP_SERV_TALKNUMWORK2",
                    0x028 => "GP_SERV_BATTLE2",
                    0x029 => "GP_SERV_BATTLE_MESSAGE",
                    0x02A => "GP_SERV_TALKNUMWORK",
                    0x02D => "GP_SERV_BATTLE_MESSAGE2",
                    0x02E => "GP_SERV_OPENMOGMENU",
                    0x02F => "GP_SERV_DIG",
                    0x030 => "GP_SERV_EFFECT",
                    0x031 => "GP_SERV_RECIPE",
                    0x032 => "GP_SERV_EVENT",
                    0x033 => "GP_SERV_EVENTSTR",
                    0x034 => "GP_SERV_EVENTNUM",
                    0x036 => "GP_SERV_TALKNUM",
                    0x037 => "GP_SERV_SERVERSTATUS",
                    0x038 => "GP_SERV_SCHEDULOR",
                    0x039 => "GP_SERV_MAPSCHEDULOR",
                    0x03A => "GP_SERV_MAGICSCHEDULOR",
                    0x03B => "GP_SERV_EVENTMES",
                    0x03C => "GP_SERV_SHOP_LIST",
                    0x03D => "GP_SERV_SHOP_SELL",
                    0x03E => "GP_SERV_SHOP_OPEN",
                    0x03F => "GP_SERV_SHOP_BUY",
                    0x041 => "GP_SERV_BLACK_LIST",
                    0x042 => "GP_SERV_BLACK_EDIT",
                    0x043 => "GP_SERV_TALKNUMNAME",
                    0x044 => "GP_SERV_EXTENDED_JOB",
                    0x047 => "GP_SERV_TRANSLATE",
                    0x048 => "GP_SERV_LINK_CONCIERGE",
                    0x049 => "GP_SERV_ITEMSEARCH",
                    0x04B => "GP_SERV_PBX_RESULT",
                    0x04C => "GP_SERV_AUC",
                    0x04D => "GP_SERV_FRAGMENTS",
                    0x04F => "GP_SERV_EQUIP_CLEAR",
                    0x050 => "GP_SERV_EQUIP_LIST",
                    0x051 => "GP_SERV_GRAP_LIST",
                    0x052 => "GP_SERV_EVENTUCOFF",
                    0x053 => "GP_SERV_SYSTEMMES",
                    0x055 => "GP_SERV_SCENARIOITEM",
                    0x056 => "GP_SERV_MISSION",
                    0x057 => "GP_SERV_WEATHER",
                    0x058 => "GP_SERV_ASSIST",
                    0x059 => "GP_SERV_FRIENDPASS",
                    0x05A => "GP_SERV_MOTIONMES",
                    0x05B => "GP_SERV_WPOS",
                    0x05C => "GP_SERV_PENDINGNUM",
                    0x05D => "GP_SERV_PENDINGSTR",
                    0x05E => "GP_SERV_CONQUEST",
                    0x05F => "GP_SERV_MUSIC",
                    0x060 => "GP_SERV_MUSICVOLUME",
                    0x061 => "GP_SERV_CLISTATUS",
                    0x062 => "GP_SERV_CLISTATUS2",
                    0x063 => "GP_SERV_MISCDATA",
                    0x065 => "GP_SERV_WPOS2",
                    0x067 => "GP_SERV_ENTITY_UPDATE1",
                    0x068 => "GP_SERV_ENTITY_UPDATE2",
                    0x069 => "GP_SERV_CHOCOBO_RACING",
                    0x06F => "GP_SERV_COMBINE_ANS",
                    0x070 => "GP_SERV_COMBINE_INF",
                    0x071 => "GP_SERV_INFLUENCE",
                    0x072 => "GP_SERV_UNKNOWN_072",
                    0x073 => "GP_SERV_CHOCOBO_TOTEBOARD",
                    0x074 => "GP_SERV_CHOCOBO_LIST",
                    0x075 => "GP_SERV_BATTLEFIELD",
                    0x076 => "GP_SERV_GROUP_EFFECTS",
                    0x077 => "GP_SERV_ENTITY_VIS",
                    0x078 => "GP_SERV_SWITCH_START",
                    0x079 => "GP_SERV_SWITCH_PROC",
                    0x081 => "GP_SERV_UNKNOWN_081",
                    0x082 => "GP_SERV_GUILD_BUY",
                    0x083 => "GP_SERV_GUILD_BUYLIST",
                    0x084 => "GP_SERV_GUILD_SELL",
                    0x085 => "GP_SERV_GUILD_SELLLIST",
                    0x086 => "GP_SERV_GUILD_OPEN",
                    0x08C => "GP_SERV_MERIT",
                    0x08D => "GP_SERV_JOB_POINTS",
                    0x08E => "GP_SERV_ALTER_EGO_POINTS",
                    0x096 => "GP_SERV_MYROOM_ENTER",
                    0x097 => "GP_SERV_MYROOM_EXIT",
                    0x0A0 => "GP_SERV_MAP_GROUP",
                    0x0AA => "GP_SERV_MAGIC_DATA",
                    0x0AB => "GP_SERV_FEAT_DATA",
                    0x0AC => "GP_SERV_COMMAND_DATA",
                    0x0AD => "GP_SERV_DUNGEON",
                    0x0AE => "GP_SERV_MOUNT_DATA",
                    0x0B4 => "GP_SERV_CONFIG",
                    0x0B5 => "GP_SERV_FAQ_GMPARAM",
                    0x0B6 => "GP_SERV_SET_GMMSG",
                    0x0B7 => "GP_SERV_GMSCITEM",
                    0x0BF => "GP_SERV_REGISTRATION",
                    0x0C8 => "GP_SERV_GROUP_TBL",
                    0x0C9 => "GP_SERV_EQUIP_INSPECT",
                    0x0CA => "GP_SERV_INSPECT_MESSAGE",
                    0x0CC => "GP_SERV_LINKSHELL_MESSAGE",
                    0x0D2 => "GP_SERV_TROPHY_LIST",
                    0x0D3 => "GP_SERV_TROPHY_SOLUTION",
                    0x0DC => "GP_SERV_GROUP_SOLICIT_REQ",
                    0x0DD => "GP_SERV_GROUP_LIST",
                    0x0DE => "GP_SERV_GROUP_SOLICIT_NO",
                    0x0DF => "GP_SERV_GROUP_ATTR",
                    0x0E0 => "GP_SERV_GROUP_COMLINK",
                    0x0E1 => "GP_SERV_GROUP_CHECKID",
                    0x0E2 => "GP_SERV_GROUP_LIST2",
                    0x0E6 => "GP_SERV_BALLISTA",
                    0x0EE => "GP_SERV_AUTOMATION_POLICY",
                    0x0F4 => "GP_SERV_TRACKING_LIST",
                    0x0F5 => "GP_SERV_TRACKING_POS",
                    0x0F6 => "GP_SERV_TRACKING_STATE",
                    0x0F9 => "GP_SERV_RES",
                    0x0FA => "GP_SERV_MYROOM_OPERATION",
                    0x105 => "GP_SERV_BAZAAR_LIST",
                    0x106 => "GP_SERV_BAZAAR_BUY",
                    0x107 => "GP_SERV_BAZAAR_CLOSE",
                    0x108 => "GP_SERV_BAZAAR_SHOPPING",
                    0x109 => "GP_SERV_BAZAAR_SELL",
                    0x10A => "GP_SERV_BAZAAR_SALE",
                    0x10E => "GP_SERV_REQSUBMAPNUM",
                    0x110 => "GP_SERV_UNITY",
                    0x111 => "GP_SERV_ROE_ACTIVELOG",
                    0x112 => "GP_SERV_ROE_LOG",
                    0x113 => "GP_SERV_CURRENCIES_1",
                    0x115 => "GP_SERV_FISH",
                    0x116 => "GP_SERV_EQUIPSET_VALID",
                    0x117 => "GP_SERV_EQUIPSET_RES",
                    0x118 => "GP_SERV_CURRENCIES_2",
                    0x119 => "GP_SERV_ABIL_RECAST",
                    0x11A => "GP_SERV_EMOTE_LIST",
                    0x11C => "GP_SERV_LOCKSTYLE_ERROR",
                    0x11D => "GP_SERV_PARTYREQ",
                    0x11E => "GP_SERV_JUMP",
                    _ => $"GP_SERV_0x{packetId:X3}"
                };
            }
            else
            {
                return packetId switch
                {
                    0x00A => "GP_CLI_LOGIN",
                    0x00C => "GP_CLI_GAMEOK",
                    0x00D => "GP_CLI_NETEND",
                    0x00F => "GP_CLI_CLSTAT",
                    0x011 => "GP_CLI_ZONE_TRANSITION",
                    0x015 => "GP_CLI_POS",
                    0x016 => "GP_CLI_CHARREQ",
                    0x017 => "GP_CLI_CHARREQ2",
                    0x01A => "GP_CLI_ACTION",
                    0x01B => "GP_CLI_FRIENDPASS",
                    0x01C => "GP_CLI_UNKNOWN",
                    0x01E => "GP_CLI_GM",
                    0x01F => "GP_CLI_GMCOMMAND",
                    0x028 => "GP_CLI_ITEM_DUMP",
                    0x029 => "GP_CLI_ITEM_MOVE",
                    0x02B => "GP_CLI_TRANSLATE",
                    0x02C => "GP_CLI_ITEMSEARCH",
                    0x032 => "GP_CLI_TRADE_REQ",
                    0x033 => "GP_CLI_TRADE_RES",
                    0x034 => "GP_CLI_TRADE_LIST",
                    0x036 => "GP_CLI_ITEM_TRANSFER",
                    0x037 => "GP_CLI_ITEM_USE",
                    0x03A => "GP_CLI_ITEM_STACK",
                    0x03B => "GP_CLI_SUBCONTAINER",
                    0x03C => "GP_CLI_BLACK_LIST",
                    0x03D => "GP_CLI_BLACK_EDIT",
                    0x041 => "GP_CLI_TROPHY_ENTRY",
                    0x042 => "GP_CLI_TROPHY_ABSENCE",
                    0x04B => "GP_CLI_FRAGMENTS",
                    0x04D => "GP_CLI_PBX",
                    0x04E => "GP_CLI_AUC",
                    0x050 => "GP_CLI_EQUIP_SET",
                    0x051 => "GP_CLI_EQUIPSET_SET",
                    0x052 => "GP_CLI_EQUIPSET_CHECK",
                    0x053 => "GP_CLI_LOCKSTYLE",
                    0x058 => "GP_CLI_RECIPE",
                    0x059 => "GP_CLI_EFFECTEND",
                    0x05A => "GP_CLI_REQCONQUEST",
                    0x05B => "GP_CLI_EVENTEND",
                    0x05C => "GP_CLI_EVENTENDXZY",
                    0x05D => "GP_CLI_MOTION",
                    0x05E => "GP_CLI_MAPRECT",
                    0x060 => "GP_CLI_PASSWARDS",
                    0x061 => "GP_CLI_CLISTATUS",
                    0x063 => "GP_CLI_DIG",
                    0x064 => "GP_CLI_SCENARIOITEM",
                    0x066 => "GP_CLI_FISHING",
                    0x06E => "GP_CLI_GROUP_SOLICIT_REQ",
                    0x06F => "GP_CLI_GROUP_LEAVE",
                    0x070 => "GP_CLI_GROUP_BREAKUP",
                    0x071 => "GP_CLI_GROUP_STRIKE",
                    0x074 => "GP_CLI_GROUP_SOLICIT_RES",
                    0x076 => "GP_CLI_GROUP_LIST_REQ",
                    0x077 => "GP_CLI_GROUP_CHANGE2",
                    0x078 => "GP_CLI_GROUP_CHECKID",
                    0x083 => "GP_CLI_SHOP_BUY",
                    0x084 => "GP_CLI_SHOP_SELL_REQ",
                    0x085 => "GP_CLI_SHOP_SELL_SET",
                    0x096 => "GP_CLI_COMBINE_ASK",
                    0x09B => "GP_CLI_CHOCOBO_RACE_REQ",
                    0x0A0 => "GP_CLI_SWITCH_PROPOSAL",
                    0x0A1 => "GP_CLI_SWITCH_VOTE",
                    0x0A2 => "GP_CLI_DICE",
                    0x0AA => "GP_CLI_GUILD_BUY",
                    0x0AB => "GP_CLI_GUILD_BUYLIST",
                    0x0AC => "GP_CLI_GUILD_SELL",
                    0x0AD => "GP_CLI_GUILD_SELLLIST",
                    0x0B5 => "GP_CLI_CHAT_STD",
                    0x0B6 => "GP_CLI_CHAT_NAME",
                    0x0B7 => "GP_CLI_ASSIST_CHANNEL",
                    0x0BE => "GP_CLI_MERITS",
                    0x0BF => "GP_CLI_JOB_POINTS_SPEND",
                    0x0C0 => "GP_CLI_JOB_POINTS_REQ",
                    0x0C1 => "GP_CLI_ALTER_EGO_POINTS",
                    0x0C3 => "GP_CLI_GROUP_COMLINK_MAKE",
                    0x0C4 => "GP_CLI_GROUP_COMLINK_ACTIVE",
                    0x0CB => "GP_CLI_MYROOM_IS",
                    0x0D2 => "GP_CLI_MAP_GROUP",
                    0x0D3 => "GP_CLI_FAQ_GMCALL",
                    0x0D4 => "GP_CLI_FAQ_GMPARAM",
                    0x0D5 => "GP_CLI_ACK_GMMSG",
                    0x0D8 => "GP_CLI_DUNGEON_PARAM",
                    0x0DB => "GP_CLI_CONFIG_LANGUAGE",
                    0x0DC => "GP_CLI_CONFIG",
                    0x0DD => "GP_CLI_EQUIP_INSPECT",
                    0x0DE => "GP_CLI_INSPECT_MESSAGE",
                    0x0E0 => "GP_CLI_SET_USERMSG",
                    0x0E1 => "GP_CLI_GET_LSMSG",
                    0x0E2 => "GP_CLI_SET_LSMSG",
                    0x0E4 => "GP_CLI_GET_LSPRIV",
                    0x0E7 => "GP_CLI_REQLOGOUT",
                    0x0E8 => "GP_CLI_CAMP",
                    0x0EA => "GP_CLI_SIT",
                    0x0EB => "GP_CLI_REQSUBMAPNUM",
                    0x0F0 => "GP_CLI_RESCUE",
                    0x0F1 => "GP_CLI_BUFFCANCEL",
                    0x0F2 => "GP_CLI_SUBMAPCHANGE",
                    0x0F4 => "GP_CLI_TRACKING_LIST",
                    0x0F5 => "GP_CLI_TRACKING_START",
                    0x0F6 => "GP_CLI_TRACKING_END",
                    0x0FA => "GP_CLI_MYROOM_LAYOUT",
                    0x0FB => "GP_CLI_MYROOM_BANKIN",
                    0x0FC => "GP_CLI_MYROOM_PLANT_ADD",
                    0x0FD => "GP_CLI_MYROOM_PLANT_CHECK",
                    0x0FE => "GP_CLI_MYROOM_PLANT_CROP",
                    0x0FF => "GP_CLI_MYROOM_PLANT_STOP",
                    0x100 => "GP_CLI_MYROOM_JOB",
                    0x102 => "GP_CLI_EXTENDED_JOB",
                    0x104 => "GP_CLI_BAZAAR_EXIT",
                    0x105 => "GP_CLI_BAZAAR_LIST",
                    0x106 => "GP_CLI_BAZAAR_BUY",
                    0x109 => "GP_CLI_BAZAAR_OPEN",
                    0x10A => "GP_CLI_BAZAAR_ITEMSET",
                    0x10B => "GP_CLI_BAZAAR_CLOSE",
                    0x10C => "GP_CLI_ROE_START",
                    0x10D => "GP_CLI_ROE_REMOVE",
                    0x10E => "GP_CLI_ROE_CLAIM",
                    0x10F => "GP_CLI_CURRENCIES_1",
                    0x110 => "GP_CLI_FISHING_2",
                    0x112 => "GP_CLI_BATTLEFIELD_REQ",
                    0x113 => "GP_CLI_SITCHAIR",
                    0x114 => "GP_CLI_MAP_MARKERS",
                    0x115 => "GP_CLI_CURRENCIES_2",
                    0x116 => "GP_CLI_UNITY_MENU",
                    0x117 => "GP_CLI_UNITY_QUEST",
                    0x118 => "GP_CLI_UNITY_TOGGLE",
                    0x119 => "GP_CLI_EMOTE_LIST",
                    0x11B => "GP_CLI_MASTERY_DISPLAY",
                    0x11C => "GP_CLI_PARTY_REQUEST",
                    0x11D => "GP_CLI_JUMP",
                    _ => $"GP_CLI_0x{packetId:X3}"
                };
            }
        }

        /// <summary>
        /// Produces a compact hex string preview of up to <paramref name="maxBytes"/>.
        /// </summary>
        public static string FormatHexPreview(ReadOnlySpan<byte> bytes, int maxBytes = 16)
        {
            if (bytes.IsEmpty) return string.Empty;

            int count = Math.Min(bytes.Length, maxBytes);
            var sb = new StringBuilder(count * 3);
            for (int i = 0; i < count; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(bytes[i].ToString("X2"));
            }
            if (bytes.Length > maxBytes)
            {
                sb.Append(" ...");
            }
            return sb.ToString();
        }

        /// <summary>
        /// Formats a complete, dual-column Hex and ASCII dump matching Wireshark / hex editor presentation:
        /// 0000  0A 2E 00 00 00 00 00 00  00 00 00 00 12 34 56 78  |.............4Vx|
        /// </summary>
        public static string FormatHexAscii(ReadOnlySpan<byte> bytes)
        {
            if (bytes.IsEmpty) return "<Empty Payload>";

            var sb = new StringBuilder();
            const int bytesPerLine = 16;

            for (int offset = 0; offset < bytes.Length; offset += bytesPerLine)
            {
                sb.Append(offset.ToString("X4"));
                sb.Append("  ");

                int chunk = Math.Min(bytesPerLine, bytes.Length - offset);

                // Hex column
                for (int i = 0; i < bytesPerLine; i++)
                {
                    if (i < chunk)
                    {
                        sb.Append(bytes[offset + i].ToString("X2"));
                        sb.Append(' ');
                    }
                    else
                    {
                        sb.Append("   ");
                    }

                    if (i == 7) sb.Append(' ');
                }

                sb.Append(" |");

                // ASCII column
                for (int i = 0; i < chunk; i++)
                {
                    byte b = bytes[offset + i];
                    char c = (b >= 32 && b <= 126) ? (char)b : '.';
                    sb.Append(c);
                }

                sb.Append('|');
                sb.AppendLine();
            }

            return sb.ToString();
        }
    }
}
