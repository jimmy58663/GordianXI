// src/Gordian.Engine/Ui/StockUiSearch.cs
using System;

namespace Gordian.Engine.Ui
{
    /// <summary>What a Search, Linkshell or Communication entry does (#490); a menu entry's Argument with <see cref="StockUiSearch.Action"/>.</summary>
    public enum StockUiSearchAction
    {
        None,

        /// <summary>Search: the players in this area.</summary>
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

        /// <summary>A level bracket of <c>chsclev</c> (the value: 1 for 1-10 ... 10 for 91-99).</summary>
        Level,

        /// <summary>A mission rank of <c>searchle</c> (the value: 1-10).</summary>
        Rank,

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

        /// <summary>A linkshell item's Members (<c>link2</c> / <c>link3</c> / <c>link4</c> button 1).</summary>
        ItemMembers,

        /// <summary>A linkshell item's equip / unequip (button 2).</summary>
        ItemEquip,

        /// <summary>A linkshell or pearlsack's Create Linkpearl (button 3).</summary>
        ItemMakePearl,

        /// <summary>Communication's Friend List.</summary>
        FriendList,

        /// <summary>The blacklist window's Delete (<c>blkmain</c>).</summary>
        BlacklistDelete,
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
    public readonly record struct StockUiLinkshellItem(string Name, StockUiLinkshellKind Kind, byte Container, byte Index, uint GroupId, int WornSlot);

    /// <summary>
    /// The main menu's Search, Linkshell and Communication windows (#490), from the English menu DAT (39542, ROM/119/51; the
    /// 2026-10-31 dump with titles ROM/165/76 and help ROM/165/75).
    /// <para>Search opens <c>searchma</c> (384,48, 112 x 282, title "Search"): Area 1, Name 2, Job 3, Allegiance 4, Race 5,
    /// Level 6, Master Level 17, Rank 7, Requesting 16, Unity 14, Friend 8, Linkshell 9, Linkshell 2 15, Besieged 10,
    /// Monipulators 13, Comment 11, Edit Comment 12. Its lists: <c>searchjo</c> (the 22 jobs, buttons 1-22 = job ids),
    /// <c>searchna</c> (San d'Oria, Bastok, Windurst), <c>searchra</c> (Hume, Elvaan, Tarutaru, Mithra, Galka), <c>chsclev</c>
    /// (270,48; level brackets 1-10 ... 91-99), <c>searchle</c> (ranks 1-10), <c>scname</c> (a 96 x 14 name field at 13,23,
    /// Begin, Cancel), <c>comgenre</c> (title 44 "Comment Type": seven types) and its refine lists <c>sccompar</c>,
    /// <c>sccomite</c>, <c>sccomls</c> (title 105), the comment text box <c>comment</c> (16,240, 366 x 56). The results go to
    /// <c>scresult</c> (<see cref="StockUiPeople"/>). The filter values are LandSandBoat's search server's
    /// (<c>src/search/search_handler.cpp</c>, <c>data_loader.cpp</c>: race 0-4 grouping both genders, nation 0-2, the comment
    /// type matched exactly; https://github.com/LandSandBoat/server).</para>
    /// <para>Linkshell opens <c>link5</c> (16,106, 366 x 190, ten rows, help 294 "Select a linkshell item."), then for the item
    /// <c>link2</c> (a linkshell: Members 1, equip 2, Create Linkpearl 3), <c>link3</c> (a pearlsack, the same) or <c>link4</c>
    /// (a linkpearl: Members, equip). Members list the linkshell's members from the search server; a member's options are
    /// <c>link10</c> (Tell).</para>
    /// <para>Communication opens <c>commenu</c> (title 179: Friend List 1, Emote List 2). Friend List shows the friends online
    /// (the search server's friend search) in <c>friend</c>; left / right switch to the blacklist (<c>blklist</c>, title 126),
    /// whose rows open <c>blkmain</c> (Delete 1). <b>PROVISIONAL:</b> retail's Friend List is the PlayOnline viewer's and the
    /// DAT has no Communication entry for the blacklist; the switch is GordianXI's.</para>
    /// </summary>
    public static class StockUiSearch
    {
        public const string SearchMenu = "searchma", JobMenu = "searchjo", NationMenu = "searchna", RaceMenu = "searchra",
            LevelMenu = "chsclev", RankMenu = "searchle", NameMenu = "scname", CommentTypeMenu = "comgenre",
            PartyCommentMenu = "sccompar", ItemCommentMenu = "sccomite", LinkshellCommentMenu = "sccomls", CommentWindow = "comment";

        public const string LinkshellItemMenu = "link5", LinkshellMenu = "link2", PearlsackMenu = "link3", LinkpearlMenu = "link4",
            LinkshellMemberMenu = "link10";

        public const string CommunicationMenu = "commenu", FriendListMenu = "friend", BlacklistMenu = "blklist", BlacklistOptionsMenu = "blkmain";

        public const int NameFieldButton = 1, NameBeginButton = 2, NameCancelButton = 3;

        /// <summary>The longest name the search takes (LandSandBoat reads up to 15 characters).</summary>
        public const int MaxNameLength = 15;

        /// <summary>The longest search comment C2S 0x0E0 carries (128 bytes, the last a terminator).</summary>
        public const int MaxCommentLength = 123;

        /// <summary>Packs an entry's action and value into a menu entry's Argument.</summary>
        public static int Action(StockUiSearchAction action, int value = 0) => (int)action | (value << 8);

        /// <summary>Unpacks <see cref="Action"/>.</summary>
        public static (StockUiSearchAction Action, int Value) ActionOf(int argument) => ((StockUiSearchAction)(argument & 0xFF), argument >> 8);

        /// <summary>A <c>chsclev</c> bracket's level range: 1 is 1-10, 9 is 81-90, 10 is 91-99.</summary>
        public static (byte Min, byte Max) LevelBracket(int bracket)
        {
            int b = Math.Clamp(bracket, 1, 10);
            return ((byte)(10 * b - 9), (byte)(b == 10 ? 99 : 10 * b));
        }

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

        /// <summary>The window a linkshell item's options are in.</summary>
        public static string MenuOf(StockUiLinkshellKind kind) => kind switch
        {
            StockUiLinkshellKind.Linkshell => LinkshellMenu,
            StockUiLinkshellKind.Pearlsack => PearlsackMenu,
            _ => LinkpearlMenu,
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
            LinkshellItemMenu or LinkshellMenu or PearlsackMenu or LinkpearlMenu or LinkshellMemberMenu => "Linkshell",
            CommunicationMenu => "Communication",
            BlacklistOptionsMenu => "Blacklist",
            _ => null,
        };
    }
}
