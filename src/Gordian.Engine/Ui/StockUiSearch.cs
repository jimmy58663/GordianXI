// src/Gordian.Engine/Ui/StockUiSearch.cs
using System;
using System.Collections.Generic;
using Gordian.Core.Resources.Ui;

namespace Gordian.Engine.Ui
{
    /// <summary>What a Search, Linkshell or Communication entry does (#490); a menu entry's Argument with <see cref="StockUiSearch.Action"/>.</summary>
    public enum StockUiSearchAction
    {
        None,

        /// <summary>Search by Area: the area list over the chat (<c>arealist</c>).</summary>
        Area,

        /// <summary>Search by Name: the name field (<c>scname</c> button 1), Begin (2) and Cancel (3).</summary>
        NameField,
        NameBegin,
        NameCancel,

        /// <summary>A job of <c>searchjo</c> (the value: the job id).</summary>
        Job,

        /// <summary>An allegiance of <c>searchna</c> (the value: 0 San d'Oria, 1 Bastok, 2 Windurst).</summary>
        Nation,

        /// <summary>A race of <c>searchra</c> (the value: 0 Hume, 1 Elvaan, 2 Tarutaru, 3 Mithra, 4 Galka).</summary>
        Race,

        /// <summary>Search by Level (<c>scchar</c>): a level field (the value: 1 the minimum, 2 the maximum), Begin and Cancel.</summary>
        LevelField,
        LevelBegin,
        LevelCancel,

        /// <summary>A mission rank of <c>searchle</c> (the value: 1-10).</summary>
        Rank,

        /// <summary>Search by Unity: the Unity list over the chat.</summary>
        Unity,

        /// <summary>The players on your friend list (every area).</summary>
        Friends,

        /// <summary>The members of the linkshell worn in slot 1 or 2 (the value).</summary>
        LinkshellMembers,

        /// <summary>Search by comment: the comment type list (<c>comgenre</c>), then the search.</summary>
        CommentSearch,

        /// <summary>Edit your search comment: the comment type list, then the text (C2S 0x0E0).</summary>
        CommentEdit,

        /// <summary>A comment type of <c>comgenre</c> (the value: 1-7).</summary>
        CommentGenre,

        /// <summary>A row of the refine lists (<c>sccompar</c>, <c>sccomite</c>, <c>sccomls</c>; the value: 1-3).</summary>
        CommentRefine,

        /// <summary>Main menu Linkshell: the linkshell items you hold (<c>link5</c>).</summary>
        LinkshellItems,

        /// <summary>A worn linkshell item's Link List (<c>link2</c> / <c>link3</c> / <c>link4</c> button 1).</summary>
        ItemMembers,

        /// <summary>A worn linkshell item's Unequip (button 2).</summary>
        ItemEquip,

        /// <summary>A linkshell or pearlsack's Create Pearl (button 3).</summary>
        ItemMakePearl,

        /// <summary>An unworn linkshell item's Set as No. 1 / No. 2 (<c>link13</c>; the value: the slot).</summary>
        ItemSetSlot,

        /// <summary>An unworn linkshell item's Drop (<c>link13</c> button 2).</summary>
        ItemDrop,

        /// <summary>A linkshell member's To Sack, To Pearl and Kick (<c>link7</c> ... <c>link9</c>).</summary>
        MemberToSack,
        MemberToPearl,
        MemberKick,

        /// <summary>Communication's Friend List (<c>flistmai</c>).</summary>
        FriendList,

        /// <summary>Friend List's To List, Messages and Online Stat. (<c>flistmai</c> buttons 1-3).</summary>
        FriendTo,
        FriendMessages,
        FriendStatus,

        /// <summary>An online status of <c>olstat</c> (the value: 1 Online, 2 Away, 3 Invisible).</summary>
        OnlineStatus,

        /// <summary>Communication's Emote List (<c>emote</c>).</summary>
        EmoteList,
    }

    /// <summary>The linkshell items the Linkshell window lists (Linkshell, Pearlsack, Linkpearl; item ids 513-515 in LandSandBoat's item_basic).</summary>
    public enum StockUiLinkshellKind
    {
        Linkshell = 513,
        Pearlsack = 514,
        Linkpearl = 515,
    }

    /// <summary>One linkshell item held: where it is, its linkshell and whether it is worn.</summary>
    /// <param name="Name">The linkshell's name (the item's extra data), else the item's name.</param>
    /// <param name="Kind">Linkshell, pearlsack or linkpearl.</param>
    /// <param name="Container">The container it is in.</param>
    /// <param name="Index">Its index there.</param>
    /// <param name="GroupId">The linkshell's id (the first word of the extra data); 0 when unknown.</param>
    /// <param name="WornSlot">The linkshell slot (1 or 2) it is worn in; 0 when not worn.</param>
    /// <param name="Color">The linkshell's colour: the extra data's u16 at 6, 4 bits each of R, G, B from the low end; 0 when unknown.</param>
    public readonly record struct StockUiLinkshellItem(string Name, StockUiLinkshellKind Kind, byte Container, byte Index, uint GroupId, int WornSlot,
        ushort Color = 0)
    {
        /// <summary>The linkshell rank the item gives in its member list (1 holder, 2 pearlsack, 3 pearl).</summary>
        public byte Rank => Kind switch { StockUiLinkshellKind.Linkshell => 1, StockUiLinkshellKind.Pearlsack => 2, _ => 3 };
    }

    /// <summary>A client list over the chat that searches (<c>arealist</c> / <c>maplist</c> frames): areas, a region's zones, the Unities.</summary>
    public enum StockUiSearchList
    {
        None,
        Areas,
        Zones,
        Unity,
    }

    /// <summary>
    /// The main menu's Search, Linkshell and Communication windows (#490), from the English menu DAT (39542, ROM/119/51; the
    /// 2026-10-31 dump with titles ROM/165/76 and help ROM/165/75) and the maintainer's round 1 retail captures (2026-11-01,
    /// <c>TESTING-2026-10-31.md</c> E1, image-30 to image-47).
    /// <para>Search opens <c>searchma</c> (384,48, 112 x 282, title "Search"): Area 1, Name 2, Job 3, Allegiance 4, Race 5,
    /// Level 6, Master Level 17, Rank 7, Requesting 16, Unity 14, Friend 8, Linkshell 9, Linkshell 2 15, Ballista 10 (help 602
    /// "... Ballista."), Monipulators 13, Comment 11, Edit Comment 12. Area opens the area list over the chat (<c>arealist</c>,
    /// 16,208, 366 x 88, title 38 "Search by Area"; image-45: "Current Area (Area)", "Current Region (Region)", "All Areas
    /// (World)", then the regions with their zone counts "San d'Oria [4]") and a region its zones in <c>maplist</c>; Level opens
    /// <c>scchar</c> (title "Search by Level": the minimum 1 and maximum 2 fields, 24 x 14 at 13,39 / 73,39, OK 3, Cancel 4;
    /// image-43); Unity lists the Unities over the chat (image-47). Its other lists: <c>searchjo</c> (the 22 jobs, buttons 1-22
    /// = job ids), <c>searchna</c>, <c>searchra</c>, <c>searchle</c> (ranks 1-10), <c>scname</c> (a 96 x 14 name field at
    /// 13,23, Begin, Cancel), <c>comgenre</c> (title 44 "Comment Type": seven types) and its refine lists <c>sccompar</c>,
    /// <c>sccomite</c>, <c>sccomls</c> (title 105), the comment text box <c>comment</c> (16,240, 366 x 56; image-46: a dot in
    /// the comment type's colour, then the text). The results go to <c>scresult</c> (<see cref="StockUiPeople"/>). The filter
    /// values are LandSandBoat's search server's (<c>src/search/search_handler.cpp</c>, <c>data_loader.cpp</c>: race 0-4
    /// grouping both genders, nation 0-2, the comment type matched exactly; it has no Unity filter;
    /// https://github.com/LandSandBoat/server).</para>
    /// <para>Linkshell opens <c>link5</c> (16,106, 366 x 190, ten rows, help 294) under <c>link6</c> (16,48, 366 x 56: the
    /// selected item's icon and name; image-30). An unworn item opens <c>link13</c> (Set as No. 1 1, Set as No. 2 3, Drop 2;
    /// image-31), a worn linkshell <c>link2</c>, a worn pearlsack <c>link3</c> (Link List 1, Unequip 2, Create Pearl 3; image-32)
    /// and a worn linkpearl <c>link4</c> (Link List, Unequip). Link List shows the members in <c>link12</c> (16,106, ten rows)
    /// under <c>link6</c>, each with its rank's icon (image-33); a member's options depend on your rank and theirs (image-33 to
    /// image-37): a holder over a pearl <c>link7</c> (Tell, To Sack, Kick), over a sack <c>link8</c> (Tell, To Pearl, Kick); a
    /// sack holder over anyone <c>link9</c> (Tell, Kick); otherwise <c>link10</c> (Tell).</para>
    /// <para>Communication opens <c>commenu</c> (title 179: Friend List 1, Emote List 2). Friend List opens <c>flistmai</c>
    /// (title "Friend List": To List 1, Messages 2, Online Stat. 3; image-38), which open <c>friend</c> (the friends; image-39
    /// "No friends registered."), <c>msglist</c> (PlayOnline messages: the "From To Type Date" bar; image-40) and <c>olstat</c>
    /// (16,48, 366 x 64: Online 1, Away 2, Invisible 3; image-41). Emote List opens <c>emote</c> (16,96, 182 x 200, twelve rows,
    /// title 180; image-42), the emotes in alphabetical order.</para>
    /// </summary>
    public static class StockUiSearch
    {
        public const string SearchMenu = "searchma", JobMenu = "searchjo", NationMenu = "searchna", RaceMenu = "searchra",
            LevelMenu = "scchar", RankMenu = "searchle", NameMenu = "scname", CommentTypeMenu = "comgenre",
            PartyCommentMenu = "sccompar", ItemCommentMenu = "sccomite", LinkshellCommentMenu = "sccomls", CommentWindow = "comment",
            AreaListMenu = "arealist", ZoneListMenu = "maplist";

        public const string LinkshellItemMenu = "link5", LinkshellHeader = "link6", LinkshellMenu = "link2", PearlsackMenu = "link3",
            LinkpearlMenu = "link4", UnwornMenu = "link13", MemberListMenu = "link12",
            HolderOverPearlMenu = "link7", HolderOverSackMenu = "link8", SackMenu = "link9", LinkshellMemberMenu = "link10";

        public const string CommunicationMenu = "commenu", FriendMenu = "flistmai", FriendListMenu = "friend", MessagesMenu = "msglist",
            OnlineStatusMenu = "olstat", EmoteMenu = "emote";

        public const int NameFieldButton = 1, NameBeginButton = 2, NameCancelButton = 3;
        public const int LevelMinButton = 1, LevelMaxButton = 2, LevelBeginButton = 3, LevelCancelButton = 4;
        public const int BallistaButton = 10, UnequipButton = 2;

        /// <summary>"Unequip" (menu windowps #122), drawn over a worn item's equip entry (image-32; the DAT's own label is "Equip").</summary>
        public const int UnequipImage = 122;

        /// <summary>The longest name the search takes (LandSandBoat reads up to 15 characters).</summary>
        public const int MaxNameLength = 15;

        /// <summary>The longest search comment C2S 0x0E0 carries (128 bytes, the last a terminator).</summary>
        public const int MaxCommentLength = 123;

        /// <summary>The help bar's titles over the lists Search opens over the chat.</summary>
        public const string AreaTitle = "Search by Area", UnityTitle = "Unity";

        /// <summary>What the friend list shows with nobody on it (image-39), and its help bar's title.</summary>
        public const string NoFriendsText = "No friends registered.";

        /// <summary>The PlayOnline message list's column bar (image-40).</summary>
        public static readonly IReadOnlyList<(string Text, float X)> MessageColumns = new[] { ("From", 9f), ("To", 89f), ("Type", 161f), ("Date", 225f) };

        /// <summary>
        /// The Unities in their Unity numbers' order (1-11), each with the short name the list shows at the right (image-47:
        /// "Pieuje (Pieuje)", "Ayame (Ayame)", "Invincible Shield (IShield)", "Apururu (Apururu)"; the short names after those
        /// four are PROVISIONAL). The list starts with "Unity (Unity)".
        /// </summary>
        public static readonly IReadOnlyList<(string Name, string Short)> Unities = new[]
        {
            ("Pieuje", "Pieuje"), ("Ayame", "Ayame"), ("Invincible Shield", "IShield"), ("Apururu", "Apururu"), ("Maat", "Maat"),
            ("Aldo", "Aldo"), ("Jakoh Wahcondalo", "Jakoh"), ("Naja Salaheem", "Naja"), ("Flaviria", "Flaviria"),
            ("Yoran-Oran", "Yoran-Oran"), ("Sylvie", "Sylvie"),
        };

        /// <summary>
        /// The emotes the Emote List offers: the ones <c>ChatCommandRouter</c> sends (C2S 0x05D), in alphabetical order as retail
        /// lists them (image-42). Retail also lists the emotes that need an item or an unlock (/aim, /bell, /dance1-4, the job
        /// emote ...), which the client does not send yet.
        /// </summary>
        public static readonly IReadOnlyList<string> Emotes = new[]
        {
            "amazed", "angry", "blush", "bow", "cheer", "clap", "comfort", "cry", "dance", "disgusted", "doubt", "doze", "farewell",
            "fume", "goodbye", "grin", "huh", "hurray", "joy", "kneel", "laugh", "muted", "no", "panic", "point", "poke", "praise",
            "psych", "salute", "shocked", "sigh", "slap", "smile", "stagger", "stare", "sulk", "surprised", "think", "toss", "wave",
            "welcome", "yes",
        };

        /// <summary>Packs an entry's action and value into a menu entry's Argument.</summary>
        public static int Action(StockUiSearchAction action, int value = 0) => (int)action | (value << 8);

        /// <summary>Unpacks <see cref="Action"/>.</summary>
        public static (StockUiSearchAction Action, int Value) ActionOf(int argument) => ((StockUiSearchAction)(argument & 0xFF), argument >> 8);

        /// <summary>The refine list a comment type opens (null for Friends and Miscellaneous, which have none).</summary>
        public static string? RefineMenuOf(int genre) => genre switch
        {
            1 or 2 or 3 => PartyCommentMenu,
            4 => ItemCommentMenu,
            5 => LinkshellCommentMenu,
            _ => null,
        };

        /// <summary>
        /// The search comment type (C2S 0x0E0 <c>msgType</c>, LandSandBoat <c>GP_CLI_COMMAND_SET_USERMSG_MSGTYPE</c>): the
        /// type's high digit and the refine row's low one (0x11 seek party for job levels ... 0x53), 0x61 friends, 0x73 the rest.
        /// </summary>
        public static uint CommentType(int genre, int refine) => genre switch
        {
            >= 1 and <= 5 => (uint)(genre * 0x10 + Math.Clamp(refine, 1, 3)),
            6 => 0x61,
            _ => 0x73,
        };

        /// <summary>
        /// The zone whose compact name (ROM/165/85, <c>StockUiMenuController.ZoneCompactName</c>: "LowJeuno", "BibikiBay") is
        /// <paramref name="name"/>, ignoring case; null for none. The same table names the zones in the people rows' area
        /// column and the Map screen's zone list, and is what <c>/sea</c> takes as a zone (#495).
        /// </summary>
        public static int? ZoneByCompactName(Func<int, string?>? compactName, string name, int zoneCount = 300)
        {
            if (compactName == null || string.IsNullOrWhiteSpace(name)) return null;
            for (int zone = 1; zone < zoneCount; zone++)
            {
                if (compactName(zone) is { Length: > 0 } compact && string.Equals(compact, name.Trim(), StringComparison.OrdinalIgnoreCase)) return zone;
            }
            return null;
        }

        /// <summary>A comment type's genre (its high digit: 1 Job Levels ... 7 Miscellaneous); 0 for none.</summary>
        public static int CommentGenreOf(uint commentType) => commentType is >= 0x10 and <= 0x7F ? (int)(commentType >> 4) : 0;

        /// <summary>
        /// The comment's text colour in the log (half scale) by genre, the colour of its dot (<c>colorbal</c> by the genre's
        /// number). Genre 1's green is sampled from round 2 image-54 (about 23FF39); the rest are the balls' colours
        /// brightened (PROVISIONAL: not captured).
        /// </summary>
        public static UiColor CommentColor(int genre) => genre switch
        {
            1 => new UiColor(0x11, 0x80, 0x1C, 0x80),
            2 => new UiColor(0x80, 0x30, 0x24, 0x80),
            3 => new UiColor(0x50, 0x6C, 0x80, 0x80),
            4 => new UiColor(0x80, 0x80, 0x74, 0x80),
            5 => new UiColor(0x80, 0x80, 0x28, 0x80),
            7 => new UiColor(0x74, 0x50, 0x78, 0x80),
            _ => new UiColor(0x80, 0x80, 0x80, 0x80),
        };

        /// <summary>
        /// The window a linkshell item's options are in: <c>link13</c> while not worn (image-31), else by kind (image-32).
        /// </summary>
        public static string MenuOf(StockUiLinkshellItem item) => item.WornSlot == 0 ? UnwornMenu : item.Kind switch
        {
            StockUiLinkshellKind.Linkshell => LinkshellMenu,
            StockUiLinkshellKind.Pearlsack => PearlsackMenu,
            _ => LinkpearlMenu,
        };

        /// <summary>
        /// A linkshell member's options from your rank and theirs (1 holder, 2 pearlsack, 3 pearl), as round 1's captures show
        /// them (image-33 to image-37): a holder can make a pearl a sack holder and back and remove either, a sack holder can
        /// remove (LandSandBoat checks the ranks again: C2S 0x071 needs a pearlsack, 0x077 change kinds 2 / 3 a linkshell).
        /// </summary>
        /// <summary><c>link9</c>'s Kick (button 2).</summary>
        public const int SackKickButton = 2;

        /// <summary>
        /// Whether a pearlsack holder may remove a member of this rank: only a linkpearl's (round 2: a sack's Kick is greyed
        /// over another sack or the shell, the maintainer's retail check).
        /// </summary>
        public static bool SackCanKick(byte theirRank) => theirRank == 3;

        public static string MemberMenuOf(byte yourRank, byte theirRank, bool yourself) => yourself ? LinkshellMemberMenu : yourRank switch
        {
            1 when theirRank == 3 => HolderOverPearlMenu,
            1 when theirRank == 2 => HolderOverSackMenu,
            2 => SackMenu,
            _ => LinkshellMemberMenu,
        };

        /// <summary>The help bar's title over the Search, Linkshell and Communication windows; null for other menus.</summary>
        public static string? TitleOf(string menuName) => menuName.ToLowerInvariant() switch
        {
            SearchMenu or NameMenu => "Search",
            JobMenu => "Search by Job",
            NationMenu => "Search by Allegiance",
            RaceMenu => "Search by Race",
            LevelMenu => "Search by Level",
            RankMenu => "Search by Rank",
            CommentTypeMenu => "Comment Type",
            PartyCommentMenu or ItemCommentMenu or LinkshellCommentMenu => "Refine by Type",
            CommentWindow => "Search Comment",
            LinkshellItemMenu or LinkshellMenu or PearlsackMenu or LinkpearlMenu or UnwornMenu or MemberListMenu
                or HolderOverPearlMenu or HolderOverSackMenu or SackMenu or LinkshellMemberMenu => "Linkshell",
            CommunicationMenu => "Communication",
            FriendMenu or FriendListMenu => "Friend List",
            MessagesMenu => "Messages",
            OnlineStatusMenu => "Status",
            EmoteMenu => "Emote List",
            _ => null,
        };
    }
}
