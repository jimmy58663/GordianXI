// src/Gordian.Engine/Ui/StockUiMenuEntries.cs
using System;
using System.Collections.Generic;

namespace Gordian.Engine.Ui
{
    /// <summary>
    /// What a menu entry does when confirmed. Entries whose window is not implemented yet post a notice.
    /// </summary>
    public enum StockUiMenuCommand
    {
        None = 0,

        /// <summary>The entry's window is not implemented yet: a notice names it.</summary>
        NotAvailable,

        /// <summary>Asks "Log out?" and requests a logout.</summary>
        LogOut,

        /// <summary>Asks "Shut down?" and requests a logout that closes the client.</summary>
        ShutDown,

        /// <summary>Posts the Vana'diel and Earth time.</summary>
        CurrentTime,

        /// <summary>A Status list entry: opens its page (the Argument is a <see cref="StockUiStatusPage"/>).</summary>
        StatusPage,

        /// <summary>Main menu Map: opens the Map window on the zone's map (<see cref="StockUiMap"/>).</summary>
        Map,

        /// <summary>The Map window's Wide Scan.</summary>
        MapWideScan,

        /// <summary>The Map window's Change Map: the zone's next map.</summary>
        MapChange,

        /// <summary>Main menu View House: the storage list (<see cref="StockUiViewHouse"/>).</summary>
        ViewHouse,

        /// <summary>A View House storage: opens its item list (the Argument is a <c>ContainerId</c>).</summary>
        ViewStorage,

        /// <summary>One choice of a config page's option row (<see cref="StockUiConfigPages"/>): confirm selects it.</summary>
        ConfigOption,

        /// <summary>A config page's slider (<see cref="StockUiConfigPages"/>): left/right move it.</summary>
        ConfigSlider,

        /// <summary>A row of the Chat Filters list: confirm toggles the filter.</summary>
        ChatFilter,

        /// <summary>A chat mode of the "chatctrl" list: sets the default chat mode (the Argument is a <see cref="ChatInputMode"/>).</summary>
        ChatMode,

        /// <summary>The command menu's Attack: engages the target.</summary>
        Attack,

        /// <summary>The command menu's Disengage.</summary>
        Disengage,

        /// <summary>The engaged command menu's Call for Help (Disengage's alternate on Right).</summary>
        CallForHelp,

        /// <summary>The command menu's Invite: a party invite to the targeted player.</summary>
        Invite,

        /// <summary>The command menu's Check: examines the target (C2S 0x0DD).</summary>
        Check,

        /// <summary>The shop window's Buy: opens the shop's item list (<see cref="StockUiShop"/>).</summary>
        ShopBuy,

        /// <summary>The shop window's Sell: opens the inventory list for appraisal and sale.</summary>
        ShopSell,

        /// <summary>A Font Colors category (<c>conftxtc</c>): opens its list; the Argument is a <see cref="StockUiFontColorCategory"/>.</summary>
        FontColorList,

        /// <summary>The Font Colors page's Default: every colour back to the retail default (after a yes/no prompt).</summary>
        FontColorDefault,

        /// <summary>A row of a Font Colors list: confirm opens the R/G/B editor on it.</summary>
        FontColorRow,

        /// <summary>The colour editor's OK: the edited colour becomes the row's.</summary>
        FontColorApply,

        /// <summary>The colour editor's Cancel.</summary>
        FontColorCancel,

        /// <summary>The Log page's Window 1 / Window 2 (<c>conf11m</c>): opens the categories for that window (the Argument).</summary>
        LogWindowSelect,

        /// <summary>A Log page category (<c>conf11l</c>): opens its list; the Argument is a <see cref="StockUiFontColorCategory"/>.</summary>
        LogCategory,

        /// <summary>The Log page's Default: every message type back to its default window (after a yes/no prompt).</summary>
        LogDefault,

        /// <summary>A row of the Log page's list: confirm moves the type into or out of the chosen window.</summary>
        LogRow,

        /// <summary>A row of the Effects page: confirm toggles the filter.</summary>
        EffectFilter,

        /// <summary>The dead window's "Back to Home Point" (<see cref="StockUiDeathMenu"/>).</summary>
        HomePoint,

        /// <summary>The command menu's Treasure: opens the Treasure Pool window (<see cref="StockUiTreasurePool"/>).</summary>
        TreasurePool,

        /// <summary>The config list's Gamepad: opens the gamepad key-assignment list (#21).</summary>
        GamepadAssign,

        /// <summary>A Magic category (<see cref="StockUiActionMenu"/>): opens its spell list; the Argument is the spell kind.</summary>
        SpellList,

        /// <summary>Abilities' Job Abil.: opens the job ability list.</summary>
        JobAbilityList,

        /// <summary>Abilities' Weapon Skl.: opens the weapon skill list.</summary>
        WeaponSkillList,

        /// <summary>Abilities' Ranged Atk.: a ranged attack on the target.</summary>
        RangedAttack,

        /// <summary>Main menu or command menu Items: opens the Items window (<see cref="StockUiItems"/>, #351).</summary>
        Items,

        /// <summary>Main menu Equipment: opens the Equipment window (<see cref="StockUiItems"/>, #352).</summary>
        Equipment,

        /// <summary>Abilities' Job Traits: the job trait list.</summary>
        JobTraitList,

        /// <summary>Abilities' Mount: the mount list.</summary>
        MountList,

        /// <summary>Abilities' Pet Command: the pet command list.</summary>
        PetCommandList,

        /// <summary>A Trust's command menu Release: releases that Trust (as <c>/refa</c>).</summary>
        ReleaseTrust,

        /// <summary>The mounted command menu's Dig (#87): chocobo digging (as <c>/dig</c>).</summary>
        ChocoboDig,

        /// <summary>The mounted command menu's Dismount (#87): asks "Dismount?", then gets off (as <c>/dismount</c>).</summary>
        Dismount,

        /// <summary>Main menu Key Items: opens the Key Items window (<see cref="StockUiKeyItems"/>, #91).</summary>
        KeyItems,

        /// <summary>Macros' Edit Equip. Set: opens the Equipment Sets list (<see cref="StockUiEquipSets"/>, #92).</summary>
        EquipmentSets,

        /// <summary>The Macros menu's Edit Macro: opens the macro book list (<see cref="StockUiMacroMenus"/>, #339).</summary>
        MacroBooks,

        /// <summary>Synthesis's Crystal Synthesis: the inventory's crystals, then the synthesis grid (<see cref="StockUiSynthesis"/>, #330).</summary>
        SynthesisCrystals,

        /// <summary>Synthesis's History: the recipes done, shown in the synthesis grid (<see cref="StockUiSynthesis"/>, #330).</summary>
        SynthesisHistory,

        /// <summary>The guild shop window's Buy: the guild's stock list (<see cref="StockUiGuildShop"/>, #330).</summary>
        GuildBuy,

        /// <summary>The guild shop window's Sell: the inventory items the guild buys (<see cref="StockUiGuildShop"/>, #330).</summary>
        GuildSell,

        /// <summary>The main menu's Party: opens the Party list that fits the character's state (<see cref="StockUiParty"/>, #397).</summary>
        PartyMenu,

        /// <summary>The Party menu's Disband: breaks the party up (leader, C2S 0x070) or, for a member, leaves it (C2S 0x06F), after a prompt.</summary>
        PartyDisband,

        /// <summary>
        /// A Party menu entry that runs through the party commands (#489, <see cref="StockUiParty"/>); the Argument is a
        /// <see cref="StockUiPartyAction"/>, with the value it sets in its upper bits (<see cref="StockUiParty.Action"/>).
        /// </summary>
        PartyAction,

        /// <summary>An entry of the options for a person in a people list (<c>scoption</c>); the Argument is a <see cref="StockUiPartyAction"/>.</summary>
        PersonAction,

        /// <summary>A Search, Linkshell or Communication entry (#490, <see cref="StockUiSearch"/>); the Argument packs a <see cref="StockUiSearchAction"/>.</summary>
        SearchAction,
    }

    /// <summary>
    /// The meaning of one menu button: the label retail pre-renders for it (kept here so notices and logs can name
    /// it), the menu it opens, or the command it runs.
    /// </summary>
    public readonly record struct StockUiMenuEntry(string Label, string? Opens = null, StockUiMenuCommand Command = StockUiMenuCommand.None, int Argument = 0);

    /// <summary>
    /// The client-side meaning of the stock menu buttons. The DAT menus (Section 0x30) only carry the buttons'
    /// pre-rendered label sprites and navigation links; which window an entry opens is client behaviour. Labels were
    /// read from the "windowps" sprites each button references (ROM/119/51, 2026-09-26): the main "Commands" menu is
    /// "menuwind" (page 1) and "socialme" (page 2, reached with left/right), the config list is "configwi" and its
    /// entries open the pages described by <see cref="StockUiConfigPages"/>; the Windows entry opens the "conf5m"
    /// list (Shared / Window 1 / Window 2), whose entries open the three "Window Settings" pages (flow confirmed
    /// against a retail capture, 2026-09-26).
    /// </summary>
    public static class StockUiMenuEntries
    {
        public const string MainMenu = "menuwind";
        public const string MainMenuPage2 = "socialme";
        public const string ConfigMenu = "configwi";
        public const string WindowsMenu = "conf5m";
        public const string WindowSettingsPage = StockUiConfigPages.WindowSettingsPage;
        public const string YesNoMenu = "yesno";
        public const string MessageYesNoMenu = "comyn";

        /// <summary>
        /// The chat-mode list the command menu's Chat entry opens (252 x 120 at (130, 192), beside the command menu):
        /// Say, Tell (a red arrow, "frames" #26 on its own button 8, then the last tell partner's name in client
        /// text), Party, Linkshell, Linkshell 2, Unity, Shout. Linkshell, Linkshell 2 and Unity carry kind-4 greyed
        /// alternates, shown with "No Linkshell" / "No Unity" in client text when there is none.
        /// </summary>
        public const string ChatModeMenu = "chatctrl";

        /// <summary>The "chatctrl" button that draws the red arrow after Tell; its X is where the client text starts.</summary>
        public const int ChatModeArrowButton = 8;
        public const int ChatModeTellButton = 2, ChatModeLinkshellButton = 4, ChatModeLinkshell2Button = 5, ChatModeUnityButton = 6;

        /// <summary>The main menu's pages, flipped with left/right (the DAT's page arrows, buttons 13 and 14).</summary>
        public static readonly IReadOnlyList<string> MainMenuPages = new[] { MainMenu, MainMenuPage2 };

        /// <summary>The first "Window Type" dot of the Windows config page (dots are buttons 7..14 = skins 1..8).</summary>
        public const int WindowSkinFirstButton = 7;

        private static readonly Dictionary<(string Menu, int Button), StockUiMenuEntry> Entries = new(new MenuKeyComparer())
        {
            // Commands, page 1 (buttons in their authored top-to-bottom order).
            [(MainMenu, 1)] = new("Status", Opens: StockUiStatus.CommandMenu),
            [(MainMenu, 2)] = new("Equipment", Command: StockUiMenuCommand.Equipment),
            [(MainMenu, 3)] = new("Magic", Opens: StockUiActionMenu.MagicCategoryMenu),
            [(MainMenu, 4)] = new("Items", Command: StockUiMenuCommand.Items),
            [(MainMenu, 10)] = new("Synthesis", Opens: StockUiSynthesis.Menu),
            [(MainMenu, 5)] = new("Abilities", Opens: StockUiActionMenu.AbilityMenu),
            [(MainMenu, 6)] = new("Party", Command: StockUiMenuCommand.PartyMenu),
            [(MainMenu, 7)] = new("Trade", Command: StockUiMenuCommand.NotAvailable),
            [(MainMenu, 8)] = new("Search", Opens: StockUiSearch.SearchMenu),
            [(MainMenu, 9)] = Search("Linkshell", StockUiSearchAction.LinkshellItems),
            [(MainMenu, 11)] = new("Region Info", Command: StockUiMenuCommand.NotAvailable),
            [(MainMenu, 12)] = new("Map", Command: StockUiMenuCommand.Map),

            // Commands, page 2.
            [(MainMenuPage2, 1)] = new("Missions", Command: StockUiMenuCommand.NotAvailable),
            [(MainMenuPage2, 2)] = new("Quests", Command: StockUiMenuCommand.NotAvailable),
            [(MainMenuPage2, 3)] = new("Key Items", Command: StockUiMenuCommand.KeyItems),
            [(MainMenuPage2, 4)] = new("View House", Command: StockUiMenuCommand.ViewHouse),
            [(MainMenuPage2, 5)] = new("Bazaar", Command: StockUiMenuCommand.NotAvailable),
            [(MainMenuPage2, 6)] = new("Macros", Opens: StockUiEquipSets.MacroMenu),
            [(MainMenuPage2, 7)] = new("Config", Opens: ConfigMenu),
            [(MainMenuPage2, 8)] = new("Help Desk", Command: StockUiMenuCommand.NotAvailable),
            [(MainMenuPage2, 10)] = new("Current Time", Command: StockUiMenuCommand.CurrentTime),
            [(MainMenuPage2, 9)] = new("Communication", Opens: StockUiSearch.CommunicationMenu),
            [(MainMenuPage2, 11)] = new("Shut Down", Command: StockUiMenuCommand.ShutDown),
            [(MainMenuPage2, 12)] = new("Log Out", Command: StockUiMenuCommand.LogOut),

            // Macros ("mcrmenu", "Macro"): Edit Macro (help 829, #339) and Edit Equip. Set (help 830, #92).
            // View House (#23): mogcont's storages.
            [(StockUiViewHouse.MenuName, 1)] = new("Mog Safe", Command: StockUiMenuCommand.ViewStorage, Argument: (int)Gordian.Core.Network.Packets.ContainerId.MogSafe),
            [(StockUiViewHouse.MenuName, 8)] = new("Mog Safe 2", Command: StockUiMenuCommand.ViewStorage, Argument: (int)Gordian.Core.Network.Packets.ContainerId.MogSafe2),
            [(StockUiViewHouse.MenuName, 2)] = new("Storage", Command: StockUiMenuCommand.ViewStorage, Argument: (int)Gordian.Core.Network.Packets.ContainerId.Storage),
            [(StockUiViewHouse.MenuName, 3)] = new("Mog Locker", Command: StockUiMenuCommand.ViewStorage, Argument: (int)Gordian.Core.Network.Packets.ContainerId.MogLocker),
            [(StockUiViewHouse.MenuName, 4)] = new("Mog Satchel", Command: StockUiMenuCommand.ViewStorage, Argument: (int)Gordian.Core.Network.Packets.ContainerId.MogSatchel),
            [(StockUiViewHouse.MenuName, 5)] = new("Mog Sack", Command: StockUiMenuCommand.ViewStorage, Argument: (int)Gordian.Core.Network.Packets.ContainerId.MogSack),
            [(StockUiViewHouse.MenuName, 6)] = new("Mog Case", Command: StockUiMenuCommand.ViewStorage, Argument: (int)Gordian.Core.Network.Packets.ContainerId.MogCase),
            [(StockUiViewHouse.MenuName, 7)] = new("Mog Wardrobe", Command: StockUiMenuCommand.ViewStorage, Argument: (int)Gordian.Core.Network.Packets.ContainerId.Wardrobe),
            [(StockUiViewHouse.MenuName, 9)] = new("Mog Wardrobe 2", Command: StockUiMenuCommand.ViewStorage, Argument: (int)Gordian.Core.Network.Packets.ContainerId.Wardrobe2),
            [(StockUiViewHouse.MenuName, 10)] = new("Mog Wardrobe 3", Command: StockUiMenuCommand.ViewStorage, Argument: (int)Gordian.Core.Network.Packets.ContainerId.Wardrobe3),
            [(StockUiViewHouse.MenuName, 11)] = new("Mog Wardrobe 4", Command: StockUiMenuCommand.ViewStorage, Argument: (int)Gordian.Core.Network.Packets.ContainerId.Wardrobe4),
            [(StockUiViewHouse.MenuName, 12)] = new("Mog Wardrobe 5", Command: StockUiMenuCommand.ViewStorage, Argument: (int)Gordian.Core.Network.Packets.ContainerId.Wardrobe5),
            [(StockUiViewHouse.MenuName, 13)] = new("Mog Wardrobe 6", Command: StockUiMenuCommand.ViewStorage, Argument: (int)Gordian.Core.Network.Packets.ContainerId.Wardrobe6),
            [(StockUiViewHouse.MenuName, 14)] = new("Mog Wardrobe 7", Command: StockUiMenuCommand.ViewStorage, Argument: (int)Gordian.Core.Network.Packets.ContainerId.Wardrobe7),
            [(StockUiViewHouse.MenuName, 15)] = new("Mog Wardrobe 8", Command: StockUiMenuCommand.ViewStorage, Argument: (int)Gordian.Core.Network.Packets.ContainerId.Wardrobe8),

            // Map (#23).
            [(StockUiMap.MenuName, StockUiMap.MarkersButton)] = new("Markers", Command: StockUiMenuCommand.NotAvailable),
            [(StockUiMap.MenuName, StockUiMap.WideScanButton)] = new("Wide Scan", Command: StockUiMenuCommand.MapWideScan),
            [(StockUiMap.MenuName, StockUiMap.ChangeMapButton)] = new("Change Map", Command: StockUiMenuCommand.MapChange),

            // Status (#23): statcom2's entries in their top-to-bottom order.
            [(StockUiStatus.CommandMenu, StockUiStatus.ProfileButton)] = new("Profile", Command: StockUiMenuCommand.StatusPage, Argument: (int)StockUiStatusPage.Profile),
            [(StockUiStatus.CommandMenu, StockUiStatus.JobLevelsButton)] = new("Job Levels", Command: StockUiMenuCommand.StatusPage, Argument: (int)StockUiStatusPage.JobLevels),
            [(StockUiStatus.CommandMenu, StockUiStatus.MasterLevelsButton)] = new("Master Levels", Command: StockUiMenuCommand.NotAvailable),
            [(StockUiStatus.CommandMenu, StockUiStatus.CombatSkillsButton)] = new("Combat Skl.", Command: StockUiMenuCommand.StatusPage, Argument: (int)StockUiStatusPage.CombatSkills),
            [(StockUiStatus.CommandMenu, StockUiStatus.MagicSkillsButton)] = new("Magic Skl.", Command: StockUiMenuCommand.StatusPage, Argument: (int)StockUiStatusPage.MagicSkills),
            [(StockUiStatus.CommandMenu, StockUiStatus.CraftSkillsButton)] = new("Craft Skl.", Command: StockUiMenuCommand.StatusPage, Argument: (int)StockUiStatusPage.CraftSkills),
            [(StockUiStatus.CommandMenu, StockUiStatus.CurrenciesButton)] = new("Currencies", Command: StockUiMenuCommand.NotAvailable),
            [(StockUiStatus.CommandMenu, StockUiStatus.Currencies2Button)] = new("Currencies 2", Command: StockUiMenuCommand.NotAvailable),
            [(StockUiStatus.CommandMenu, StockUiStatus.UnityButton)] = new("Unity", Command: StockUiMenuCommand.NotAvailable),
            [(StockUiStatus.CommandMenu, StockUiStatus.PlayTimeButton)] = new("Play Time", Command: StockUiMenuCommand.NotAvailable),
            [(StockUiStatus.CommandMenu, StockUiStatus.MeritPointsButton)] = new("Merit Points", Command: StockUiMenuCommand.NotAvailable),
            [(StockUiStatus.CommandMenu, StockUiStatus.JobPointsButton)] = new("Job Points", Command: StockUiMenuCommand.NotAvailable),
            [(StockUiStatus.CommandMenu, StockUiStatus.AlterEgoButton)] = new("Alter Ego Points", Command: StockUiMenuCommand.NotAvailable),

            [(StockUiEquipSets.MacroMenu, StockUiEquipSets.EditMacroButton)] = new("Edit Macro", Command: StockUiMenuCommand.MacroBooks),
            [(StockUiEquipSets.MacroMenu, StockUiEquipSets.EditEquipSetButton)] = new("Edit Equip. Set", Command: StockUiMenuCommand.EquipmentSets),

            // Synthesis ("cmbmenu", title 170): Crystal Synthesis (help 819) and History (help 820), #330.
            [(StockUiSynthesis.Menu, StockUiSynthesis.CrystalSynthesisButton)] = new("Crystal Synthesis", Command: StockUiMenuCommand.SynthesisCrystals),
            [(StockUiSynthesis.Menu, StockUiSynthesis.HistoryButton)] = new("History", Command: StockUiMenuCommand.SynthesisHistory),

            // The guild shop window ("guildsho", opened by S2C 0x086; Buy windowps #149, Sell #150), #330.
            [(StockUiGuildShop.MenuName, StockUiGuildShop.BuyButton)] = new("Buy", Command: StockUiMenuCommand.GuildBuy),
            [(StockUiGuildShop.MenuName, StockUiGuildShop.SellButton)] = new("Sell", Command: StockUiMenuCommand.GuildSell),

            // Party (#397, #489): "prty1" not in a party, "prty6" the same with the party flag on, "prty2" a member, "prty3" the
            // leader (label sprites read against the retail captures). Rearrange still posts "not available yet".
            [(StockUiParty.NotInPartyMenu, StockUiParty.SeekPartyButton)] = Party("Seek Party", StockUiPartyAction.SeekParty),
            [(StockUiParty.NotInPartyMenu, StockUiParty.UngroupedFindMemberButton)] = Party("Find Member", StockUiPartyAction.FindMember),
            [(StockUiParty.NotInPartyMenu, StockUiParty.UngroupedRequestButton)] = Party("Request", StockUiPartyAction.Request),
            [(StockUiParty.NotInPartyMenu, StockUiParty.UngroupedViewRequestButton)] = Party("View Request", StockUiPartyAction.ViewRequest),
            [(StockUiParty.NotInPartyMenu, StockUiParty.AutogroupButton)] = Party("Autogroup", StockUiPartyAction.Autogroup),
            [(StockUiParty.NotInPartyMenu, StockUiParty.LanguagesButton)] = new("Languages", Opens: StockUiParty.LanguagesMenu),
            [(StockUiParty.NotInPartyMenu, StockUiParty.AlarmTypeButton)] = new("Alarm Type", Opens: StockUiParty.AlarmMenu),
            [(StockUiParty.SeekingMenu, StockUiParty.SeekingOffButton)] = Party("Seek Party", StockUiPartyAction.SeekParty),
            [(StockUiParty.SeekingMenu, StockUiParty.SeekingFindMemberButton)] = Party("Find Member", StockUiPartyAction.FindMember),
            [(StockUiParty.SeekingMenu, StockUiParty.SeekingRequestButton)] = Party("Request", StockUiPartyAction.Request),
            [(StockUiParty.SeekingMenu, StockUiParty.SeekingViewRequestButton)] = Party("View Request", StockUiPartyAction.ViewRequest),
            [(StockUiParty.SeekingMenu, StockUiParty.SeekingAutogroupButton)] = Party("Autogroup", StockUiPartyAction.Autogroup),
            [(StockUiParty.SeekingMenu, StockUiParty.SeekingLanguagesButton)] = new("Languages", Opens: StockUiParty.LanguagesMenu),
            [(StockUiParty.MemberMenu, StockUiParty.MemberListButton)] = Party("Member List", StockUiPartyAction.MemberList),
            [(StockUiParty.MemberMenu, StockUiParty.MemberRequestButton)] = Party("Request", StockUiPartyAction.Request),
            [(StockUiParty.MemberMenu, StockUiParty.MemberViewRequestButton)] = Party("View Request", StockUiPartyAction.ViewRequest),
            [(StockUiParty.MemberMenu, StockUiParty.MemberRearrangeButton)] = new("Rearrange", Command: StockUiMenuCommand.NotAvailable),
            [(StockUiParty.MemberMenu, StockUiParty.MemberFocusButton)] = Party("Focus", StockUiPartyAction.Focus),
            [(StockUiParty.MemberMenu, StockUiParty.MemberDistributionButton)] = Party("Distribution", StockUiPartyAction.ShowDistribution),
            [(StockUiParty.MemberMenu, StockUiParty.MemberDisbandButton)] = new("Disband", Command: StockUiMenuCommand.PartyDisband),
            [(StockUiParty.LeaderMenu, StockUiParty.LeaderFindMemberButton)] = Party("Find Member", StockUiPartyAction.FindMember),
            [(StockUiParty.LeaderMenu, StockUiParty.LeaderMemberListButton)] = Party("Member List", StockUiPartyAction.MemberList),
            [(StockUiParty.LeaderMenu, StockUiParty.LeaderRequestButton)] = Party("Request", StockUiPartyAction.Request),
            [(StockUiParty.LeaderMenu, StockUiParty.LeaderViewRequestButton)] = Party("View Request", StockUiPartyAction.ViewRequest),
            [(StockUiParty.LeaderMenu, StockUiParty.LeaderDistributionButton)] = new("Distribution", Opens: StockUiParty.DistributionMenu),
            [(StockUiParty.LeaderMenu, StockUiParty.ChangeLeaderButton)] = Party("Chg. Leader", StockUiPartyAction.ChangeLeader),
            [(StockUiParty.LeaderMenu, StockUiParty.LeaderRearrangeButton)] = new("Rearrange", Command: StockUiMenuCommand.NotAvailable),
            [(StockUiParty.LeaderMenu, StockUiParty.LeaderFocusButton)] = Party("Focus", StockUiPartyAction.Focus),
            [(StockUiParty.LeaderMenu, StockUiParty.RemoveMemberButton)] = Party("Rmv. Member", StockUiPartyAction.RemoveMember),
            [(StockUiParty.LeaderMenu, StockUiParty.LeaderDisbandButton)] = new("Disband", Command: StockUiMenuCommand.PartyDisband),
            [(StockUiParty.LeaderMenu, StockUiParty.AllianceButton)] = new("Alliance", Opens: StockUiParty.AllianceMenu),
            [(StockUiParty.LeaderMenu, StockUiParty.LevelSyncButton)] = Party("Level Sync", StockUiPartyAction.LevelSync),
            [(StockUiParty.AllianceMenu, StockUiParty.AllianceFormButton)] = Party("Form", StockUiPartyAction.AllianceForm),
            [(StockUiParty.AllianceMenu, StockUiParty.AllianceDissolveButton)] = Party("Dissolve", StockUiPartyAction.AllianceDissolve),
            [(StockUiParty.DistributionMenu, StockUiParty.LotteryButton)] = Party("Lottery", StockUiPartyAction.Lottery),
            [(StockUiParty.DistributionMenu, StockUiParty.QuartermasterButton)] = Party("Leader", StockUiPartyAction.Quartermaster),
            [(StockUiParty.LanguagesMenu, 1)] = Party("English", StockUiPartyAction.Language, 0x02),
            [(StockUiParty.LanguagesMenu, 2)] = Party("French", StockUiPartyAction.Language, 0x08),
            [(StockUiParty.LanguagesMenu, 3)] = Party("German", StockUiPartyAction.Language, 0x04),
            [(StockUiParty.LanguagesMenu, 4)] = Party("Japanese", StockUiPartyAction.Language, 0x01),
            [(StockUiParty.LanguagesMenu, 5)] = Party("Other", StockUiPartyAction.Language, 0x10),
            [(StockUiParty.AlarmMenu, 1)] = Party("Off", StockUiPartyAction.Alarm, 0),
            [(StockUiParty.AlarmMenu, 2)] = Party("Sound 1", StockUiPartyAction.Alarm, 1),
            [(StockUiParty.AlarmMenu, 3)] = Party("Sound 2", StockUiPartyAction.Alarm, 2),
            [(StockUiParty.AlarmMenu, 4)] = Party("Sound 3", StockUiPartyAction.Alarm, 3),
            [(StockUiParty.AlarmMenu, 5)] = Party("Sound 4", StockUiPartyAction.Alarm, 4),
            [(StockUiParty.AlarmMenu, 6)] = Party("Sound 5", StockUiPartyAction.Alarm, 5),
            [(StockUiParty.AlarmMenu, 7)] = Party("Sound 6", StockUiPartyAction.Alarm, 6),
            [(StockUiParty.AlarmMenu, 8)] = Party("Sound 7", StockUiPartyAction.Alarm, 7),

            // The options for a person in a people list ("scoption", #489 / #490). Send Message is PlayOnline's.
            [(StockUiPeople.OptionsMenu, StockUiPeople.TellButton)] = Person("Tell", StockUiPartyAction.Tell),
            [(StockUiPeople.OptionsMenu, StockUiPeople.RequestButton)] = Person("Request", StockUiPartyAction.RequestJoin),
            [(StockUiPeople.OptionsMenu, StockUiPeople.CancelRequestButton)] = Person("Cancel Request", StockUiPartyAction.CancelRequest),
            [(StockUiPeople.OptionsMenu, StockUiPeople.MessageButton)] = new("Send Message", Command: StockUiMenuCommand.NotAvailable),
            [(StockUiPeople.OptionsMenu, StockUiPeople.InviteButton)] = Person("Invite", StockUiPartyAction.Invite),

            // Search (#490): "searchma" and its lists. Master Level, Requesting, Unity, Besieged and Monipulators are not done.
            [(StockUiSearch.SearchMenu, 1)] = Search("Area", StockUiSearchAction.Area),
            [(StockUiSearch.SearchMenu, 2)] = new("Name", Opens: StockUiSearch.NameMenu),
            [(StockUiSearch.SearchMenu, 3)] = new("Job", Opens: StockUiSearch.JobMenu),
            [(StockUiSearch.SearchMenu, 4)] = new("Allegiance", Opens: StockUiSearch.NationMenu),
            [(StockUiSearch.SearchMenu, 5)] = new("Race", Opens: StockUiSearch.RaceMenu),
            [(StockUiSearch.SearchMenu, 6)] = new("Level", Opens: StockUiSearch.LevelMenu),
            [(StockUiSearch.SearchMenu, 17)] = new("Master Level", Command: StockUiMenuCommand.NotAvailable),
            [(StockUiSearch.SearchMenu, 7)] = new("Rank", Opens: StockUiSearch.RankMenu),
            [(StockUiSearch.SearchMenu, 16)] = new("Requesting", Command: StockUiMenuCommand.NotAvailable),
            [(StockUiSearch.SearchMenu, 14)] = new("Unity", Command: StockUiMenuCommand.NotAvailable),
            [(StockUiSearch.SearchMenu, 8)] = Search("Friend", StockUiSearchAction.Friends),
            [(StockUiSearch.SearchMenu, 9)] = Search("Linkshell", StockUiSearchAction.LinkshellMembers, 1),
            [(StockUiSearch.SearchMenu, 15)] = Search("Linkshell 2", StockUiSearchAction.LinkshellMembers, 2),
            [(StockUiSearch.SearchMenu, 10)] = new("Besieged", Command: StockUiMenuCommand.NotAvailable),
            [(StockUiSearch.SearchMenu, 13)] = new("Monipulators", Command: StockUiMenuCommand.NotAvailable),
            [(StockUiSearch.SearchMenu, 11)] = Search("Comment", StockUiSearchAction.CommentSearch),
            [(StockUiSearch.SearchMenu, 12)] = Search("Edit Comment", StockUiSearchAction.CommentEdit),
            [(StockUiSearch.NameMenu, StockUiSearch.NameFieldButton)] = Search("Name", StockUiSearchAction.NameField),
            [(StockUiSearch.NameMenu, StockUiSearch.NameBeginButton)] = Search("Begin", StockUiSearchAction.NameBegin),
            [(StockUiSearch.NameMenu, StockUiSearch.NameCancelButton)] = Search("Cancel", StockUiSearchAction.NameCancel),
            [(StockUiSearch.NationMenu, 1)] = Search("San d'Oria", StockUiSearchAction.Nation, 0),
            [(StockUiSearch.NationMenu, 2)] = Search("Bastok", StockUiSearchAction.Nation, 1),
            [(StockUiSearch.NationMenu, 3)] = Search("Windurst", StockUiSearchAction.Nation, 2),
            [(StockUiSearch.RaceMenu, 1)] = Search("Hume", StockUiSearchAction.Race, 0),
            [(StockUiSearch.RaceMenu, 2)] = Search("Elvaan", StockUiSearchAction.Race, 1),
            [(StockUiSearch.RaceMenu, 3)] = Search("Tarutaru", StockUiSearchAction.Race, 2),
            [(StockUiSearch.RaceMenu, 4)] = Search("Mithra", StockUiSearchAction.Race, 3),
            [(StockUiSearch.RaceMenu, 5)] = Search("Galka", StockUiSearchAction.Race, 4),
            [(StockUiSearch.JobMenu, 1)] = Search("WAR", StockUiSearchAction.Job, 1),
            [(StockUiSearch.JobMenu, 2)] = Search("MNK", StockUiSearchAction.Job, 2),
            [(StockUiSearch.JobMenu, 3)] = Search("WHM", StockUiSearchAction.Job, 3),
            [(StockUiSearch.JobMenu, 4)] = Search("BLM", StockUiSearchAction.Job, 4),
            [(StockUiSearch.JobMenu, 5)] = Search("RDM", StockUiSearchAction.Job, 5),
            [(StockUiSearch.JobMenu, 6)] = Search("THF", StockUiSearchAction.Job, 6),
            [(StockUiSearch.JobMenu, 7)] = Search("PLD", StockUiSearchAction.Job, 7),
            [(StockUiSearch.JobMenu, 8)] = Search("DRK", StockUiSearchAction.Job, 8),
            [(StockUiSearch.JobMenu, 9)] = Search("BST", StockUiSearchAction.Job, 9),
            [(StockUiSearch.JobMenu, 10)] = Search("BRD", StockUiSearchAction.Job, 10),
            [(StockUiSearch.JobMenu, 11)] = Search("RNG", StockUiSearchAction.Job, 11),
            [(StockUiSearch.JobMenu, 12)] = Search("SAM", StockUiSearchAction.Job, 12),
            [(StockUiSearch.JobMenu, 13)] = Search("NIN", StockUiSearchAction.Job, 13),
            [(StockUiSearch.JobMenu, 14)] = Search("DRG", StockUiSearchAction.Job, 14),
            [(StockUiSearch.JobMenu, 15)] = Search("SMN", StockUiSearchAction.Job, 15),
            [(StockUiSearch.JobMenu, 16)] = Search("BLU", StockUiSearchAction.Job, 16),
            [(StockUiSearch.JobMenu, 17)] = Search("COR", StockUiSearchAction.Job, 17),
            [(StockUiSearch.JobMenu, 18)] = Search("PUP", StockUiSearchAction.Job, 18),
            [(StockUiSearch.JobMenu, 19)] = Search("DNC", StockUiSearchAction.Job, 19),
            [(StockUiSearch.JobMenu, 20)] = Search("SCH", StockUiSearchAction.Job, 20),
            [(StockUiSearch.JobMenu, 21)] = Search("GEO", StockUiSearchAction.Job, 21),
            [(StockUiSearch.JobMenu, 22)] = Search("RUN", StockUiSearchAction.Job, 22),
            [(StockUiSearch.LevelMenu, 1)] = Search("Lv. 1-10", StockUiSearchAction.Level, 1),
            [(StockUiSearch.LevelMenu, 2)] = Search("Lv. 11-20", StockUiSearchAction.Level, 2),
            [(StockUiSearch.LevelMenu, 3)] = Search("Lv. 21-30", StockUiSearchAction.Level, 3),
            [(StockUiSearch.LevelMenu, 4)] = Search("Lv. 31-40", StockUiSearchAction.Level, 4),
            [(StockUiSearch.LevelMenu, 5)] = Search("Lv. 41-50", StockUiSearchAction.Level, 5),
            [(StockUiSearch.LevelMenu, 6)] = Search("Lv. 51-60", StockUiSearchAction.Level, 6),
            [(StockUiSearch.LevelMenu, 7)] = Search("Lv. 61-70", StockUiSearchAction.Level, 7),
            [(StockUiSearch.LevelMenu, 8)] = Search("Lv. 71-80", StockUiSearchAction.Level, 8),
            [(StockUiSearch.LevelMenu, 9)] = Search("Lv. 81-90", StockUiSearchAction.Level, 9),
            [(StockUiSearch.LevelMenu, 10)] = Search("Lv. 91-99", StockUiSearchAction.Level, 10),
            [(StockUiSearch.RankMenu, 1)] = Search("Rank 1", StockUiSearchAction.Rank, 1),
            [(StockUiSearch.RankMenu, 2)] = Search("Rank 2", StockUiSearchAction.Rank, 2),
            [(StockUiSearch.RankMenu, 3)] = Search("Rank 3", StockUiSearchAction.Rank, 3),
            [(StockUiSearch.RankMenu, 4)] = Search("Rank 4", StockUiSearchAction.Rank, 4),
            [(StockUiSearch.RankMenu, 5)] = Search("Rank 5", StockUiSearchAction.Rank, 5),
            [(StockUiSearch.RankMenu, 6)] = Search("Rank 6", StockUiSearchAction.Rank, 6),
            [(StockUiSearch.RankMenu, 7)] = Search("Rank 7", StockUiSearchAction.Rank, 7),
            [(StockUiSearch.RankMenu, 8)] = Search("Rank 8", StockUiSearchAction.Rank, 8),
            [(StockUiSearch.RankMenu, 9)] = Search("Rank 9", StockUiSearchAction.Rank, 9),
            [(StockUiSearch.RankMenu, 10)] = Search("Rank 10", StockUiSearchAction.Rank, 10),
            [(StockUiSearch.PartyCommentMenu, 1)] = Search("Seek Party", StockUiSearchAction.CommentRefine, 1),
            [(StockUiSearch.PartyCommentMenu, 2)] = Search("Find Member", StockUiSearchAction.CommentRefine, 2),
            [(StockUiSearch.PartyCommentMenu, 3)] = Search("Other", StockUiSearchAction.CommentRefine, 3),
            [(StockUiSearch.ItemCommentMenu, 1)] = Search("Sell", StockUiSearchAction.CommentRefine, 1),
            [(StockUiSearch.ItemCommentMenu, 2)] = Search("Buy", StockUiSearchAction.CommentRefine, 2),
            [(StockUiSearch.ItemCommentMenu, 3)] = Search("Other", StockUiSearchAction.CommentRefine, 3),
            [(StockUiSearch.LinkshellCommentMenu, 1)] = Search("Seek Linkshell", StockUiSearchAction.CommentRefine, 1),
            [(StockUiSearch.LinkshellCommentMenu, 2)] = Search("Recruit", StockUiSearchAction.CommentRefine, 2),
            [(StockUiSearch.LinkshellCommentMenu, 3)] = Search("Other", StockUiSearchAction.CommentRefine, 3),
            [(StockUiSearch.CommentTypeMenu, 1)] = Search("Job Levels", StockUiSearchAction.CommentGenre, 1),
            [(StockUiSearch.CommentTypeMenu, 2)] = Search("Battle Content", StockUiSearchAction.CommentGenre, 2),
            [(StockUiSearch.CommentTypeMenu, 3)] = Search("Missions / Quests", StockUiSearchAction.CommentGenre, 3),
            [(StockUiSearch.CommentTypeMenu, 4)] = Search("Items", StockUiSearchAction.CommentGenre, 4),
            [(StockUiSearch.CommentTypeMenu, 5)] = Search("Linkshells", StockUiSearchAction.CommentGenre, 5),
            [(StockUiSearch.CommentTypeMenu, 6)] = Search("Friends", StockUiSearchAction.CommentGenre, 6),
            [(StockUiSearch.CommentTypeMenu, 7)] = Search("Others", StockUiSearchAction.CommentGenre, 7),

            // Linkshell (#490): a linkshell item's options, a member's options.
            [(StockUiSearch.LinkshellMenu, 1)] = Search("Members", StockUiSearchAction.ItemMembers),
            [(StockUiSearch.LinkshellMenu, 2)] = Search("Equip", StockUiSearchAction.ItemEquip),
            [(StockUiSearch.LinkshellMenu, 3)] = Search("Create Linkpearl", StockUiSearchAction.ItemMakePearl),
            [(StockUiSearch.PearlsackMenu, 1)] = Search("Members", StockUiSearchAction.ItemMembers),
            [(StockUiSearch.PearlsackMenu, 2)] = Search("Equip", StockUiSearchAction.ItemEquip),
            [(StockUiSearch.PearlsackMenu, 3)] = Search("Create Linkpearl", StockUiSearchAction.ItemMakePearl),
            [(StockUiSearch.LinkpearlMenu, 1)] = Search("Members", StockUiSearchAction.ItemMembers),
            [(StockUiSearch.LinkpearlMenu, 2)] = Search("Equip", StockUiSearchAction.ItemEquip),
            [(StockUiSearch.LinkshellMemberMenu, 1)] = Person("Tell", StockUiPartyAction.Tell),

            // Communication (#490): "commenu", and the blacklist's Delete.
            [(StockUiSearch.CommunicationMenu, 1)] = Search("Friend List", StockUiSearchAction.FriendList),
            [(StockUiSearch.CommunicationMenu, 2)] = new("Emote List", Command: StockUiMenuCommand.NotAvailable),
            [(StockUiSearch.BlacklistOptionsMenu, 1)] = Search("Delete", StockUiSearchAction.BlacklistDelete),

            // Config list (label sprites: 338 Gameplay, 364 Chat Filters, 365 Font Colors, 366 Windows, 728 Log,
            // 367 Misc., 470 Misc. 2, 752 Misc. 3, 792 Misc. 4, 539 Effects, 527 Mouse/Cam., 359 Global, 840 Gamepad).
            // Font Colors ("conftxtc" -> "textcol1"/"textcol3"), Log ("conf11l" -> "conf11s"/"conf11m") and Effects
            // ("fxfilter") are list pages whose row text comes from the menu string table.
            [(ConfigMenu, 1)] = new("Gameplay", Opens: StockUiConfigPages.GameplayPage),
            [(ConfigMenu, 2)] = new("Chat Filters", Opens: StockUiConfigPages.ChatFiltersPage),
            [(ConfigMenu, 3)] = new("Font Colors", Opens: StockUiConfigPages.FontColorCategoryMenu),
            [(ConfigMenu, 4)] = new("Windows", Opens: WindowsMenu),
            [(ConfigMenu, 10)] = new("Log", Opens: StockUiConfigPages.LogWindowMenu),
            [(ConfigMenu, 5)] = new("Misc.", Opens: StockUiConfigPages.MiscPage),
            [(ConfigMenu, 6)] = new("Misc. 2", Opens: StockUiConfigPages.Misc2Page),
            [(ConfigMenu, 11)] = new("Misc. 3", Opens: StockUiConfigPages.Misc3Page),
            [(ConfigMenu, 12)] = new("Misc. 4", Opens: StockUiConfigPages.Misc4Page),
            [(ConfigMenu, 7)] = new("Effects", Opens: StockUiConfigPages.EffectsPage),
            [(ConfigMenu, 8)] = new("Mouse/Cam.", Opens: StockUiConfigPages.MouseCameraPage),
            [(ConfigMenu, 9)] = new("Global", Opens: StockUiConfigPages.GlobalPage),
            // Retail's Gamepad boots the external Gamepad Config program (help 1092); GordianXI opens its own
            // key-assignment list in its place (#21).
            [(ConfigMenu, 13)] = new("Gamepad", Command: StockUiMenuCommand.GamepadAssign),

            // Font Colors: the category lists (label sprites keytops3 #272 Chat, #273 For Self, windowps #432 For
            // Others, keytops3 #277 System, #278 Default), then the colour editor's OK and Cancel.
            [(StockUiConfigPages.FontColorCategoryMenu, 1)] = new("Chat", Command: StockUiMenuCommand.FontColorList, Argument: (int)StockUiFontColorCategory.Chat),
            [(StockUiConfigPages.FontColorCategoryMenu, 2)] = new("For Self", Command: StockUiMenuCommand.FontColorList, Argument: (int)StockUiFontColorCategory.ForSelf),
            [(StockUiConfigPages.FontColorCategoryMenu, 3)] = new("For Others", Command: StockUiMenuCommand.FontColorList, Argument: (int)StockUiFontColorCategory.ForOthers),
            [(StockUiConfigPages.FontColorCategoryMenu, 4)] = new("System", Command: StockUiMenuCommand.FontColorList, Argument: (int)StockUiFontColorCategory.System),
            [(StockUiConfigPages.FontColorCategoryMenu, StockUiConfigPages.FontColorDefaultButton)] = new("Default", Command: StockUiMenuCommand.FontColorDefault),
            [(StockUiConfigPages.FontColorEditPage, StockUiConfigPages.FontColorOkButton)] = new("OK", Command: StockUiMenuCommand.FontColorApply),
            [(StockUiConfigPages.FontColorEditPage, StockUiConfigPages.FontColorCancelButton)] = new("Cancel", Command: StockUiMenuCommand.FontColorCancel),

            // Log: the window (windowps #730 Window 1, #731 Window 2, keytops3 #278 Default), then the categories
            // (keytops3 #272 Chat, #273 For Self, windowps #432 For Others, keytops3 #277 System).
            [(StockUiConfigPages.LogWindowMenu, 1)] = new("Window 1", Command: StockUiMenuCommand.LogWindowSelect, Argument: 1),
            [(StockUiConfigPages.LogWindowMenu, 2)] = new("Window 2", Command: StockUiMenuCommand.LogWindowSelect, Argument: 2),
            [(StockUiConfigPages.LogWindowMenu, StockUiConfigPages.LogDefaultButton)] = new("Default", Command: StockUiMenuCommand.LogDefault),
            [(StockUiConfigPages.LogCategoryMenu, 1)] = new("Chat", Command: StockUiMenuCommand.LogCategory, Argument: (int)StockUiFontColorCategory.Chat),
            [(StockUiConfigPages.LogCategoryMenu, 2)] = new("For Self", Command: StockUiMenuCommand.LogCategory, Argument: (int)StockUiFontColorCategory.ForSelf),
            [(StockUiConfigPages.LogCategoryMenu, 3)] = new("For Others", Command: StockUiMenuCommand.LogCategory, Argument: (int)StockUiFontColorCategory.ForOthers),
            [(StockUiConfigPages.LogCategoryMenu, 4)] = new("System", Command: StockUiMenuCommand.LogCategory, Argument: (int)StockUiFontColorCategory.System),

            // Windows: which window's settings to edit.
            [(WindowsMenu, 1)] = new("Shared", Opens: StockUiConfigPages.WindowSettingsPage),
            [(WindowsMenu, 2)] = new("Window 1", Opens: StockUiConfigPages.Window1SettingsPage),
            [(WindowsMenu, 3)] = new("Window 2", Opens: StockUiConfigPages.Window2SettingsPage),

            // The NPC shop's Buy / Sell window ("shopmain", opened by S2C 0x03E; Tier 2 chunk 6c).
            [(StockUiShop.MenuName, StockUiShop.BuyButton)] = new("Buy", Command: StockUiMenuCommand.ShopBuy),
            [(StockUiShop.MenuName, StockUiShop.SellButton)] = new("Sell", Command: StockUiMenuCommand.ShopSell),

            // The dead character's window ("dead": "Time Left:" title, one button, label sprite windowps #254).
            [(StockUiDeathMenu.MenuName, StockUiDeathMenu.HomePointButton)] = new("Back to Home Point", Command: StockUiMenuCommand.HomePoint),

            // Chat modes (the command menu's Chat entry): each sets the default chat mode as /chatmode does.
            [(ChatModeMenu, 1)] = new("Say", Command: StockUiMenuCommand.ChatMode, Argument: (int)ChatInputMode.Say),
            [(ChatModeMenu, ChatModeTellButton)] = new("Tell", Command: StockUiMenuCommand.ChatMode, Argument: (int)ChatInputMode.Tell),
            [(ChatModeMenu, 3)] = new("Party", Command: StockUiMenuCommand.ChatMode, Argument: (int)ChatInputMode.Party),
            [(ChatModeMenu, ChatModeLinkshellButton)] = new("Linkshell", Command: StockUiMenuCommand.ChatMode, Argument: (int)ChatInputMode.Linkshell),
            [(ChatModeMenu, ChatModeLinkshell2Button)] = new("Linkshell 2", Command: StockUiMenuCommand.ChatMode, Argument: (int)ChatInputMode.Linkshell2),
            [(ChatModeMenu, ChatModeUnityButton)] = new("Unity", Command: StockUiMenuCommand.ChatMode, Argument: (int)ChatInputMode.Unity),
            [(ChatModeMenu, 7)] = new("Shout", Command: StockUiMenuCommand.ChatMode, Argument: (int)ChatInputMode.Shout),

            // Magic (#240): the categories of "mgcmenu" (main menu) and "magselec" (command menu, no Trust), each opening
            // its spell list; the Argument is the spell kind (StockUiActionMenu.CategoryKinds).
            [(StockUiActionMenu.MagicCategoryMenu, 1)] = Spells("White Magic", 1),
            [(StockUiActionMenu.MagicCategoryMenu, 2)] = Spells("Black Magic", 2),
            [(StockUiActionMenu.MagicCategoryMenu, 3)] = Spells("Songs", 3),
            [(StockUiActionMenu.MagicCategoryMenu, 4)] = Spells("Ninjutsu", 4),
            [(StockUiActionMenu.MagicCategoryMenu, 5)] = Spells("Summoning", 5),
            [(StockUiActionMenu.MagicCategoryMenu, 6)] = Spells("Blue Magic", 6),
            [(StockUiActionMenu.MagicCategoryMenu, 7)] = Spells("Geomancy", 7),
            [(StockUiActionMenu.MagicCategoryMenu, 8)] = Spells("Trust", 8),
            [(StockUiActionMenu.CommandMagicCategoryMenu, 1)] = Spells("White Magic", 1),
            [(StockUiActionMenu.CommandMagicCategoryMenu, 2)] = Spells("Black Magic", 2),
            [(StockUiActionMenu.CommandMagicCategoryMenu, 3)] = Spells("Songs", 3),
            [(StockUiActionMenu.CommandMagicCategoryMenu, 4)] = Spells("Ninjutsu", 4),
            [(StockUiActionMenu.CommandMagicCategoryMenu, 5)] = Spells("Summoning", 5),
            [(StockUiActionMenu.CommandMagicCategoryMenu, 6)] = Spells("Blue Magic", 6),
            [(StockUiActionMenu.CommandMagicCategoryMenu, 7)] = Spells("Geomancy", 7),

            // Abilities (#353): "abimenu" (main menu) and "abiselec" (command menu, no Job Traits).
            [(StockUiActionMenu.AbilityMenu, 1)] = new("Job Traits", Command: StockUiMenuCommand.JobTraitList),
            [(StockUiActionMenu.AbilityMenu, 2)] = new("Job Abil.", Command: StockUiMenuCommand.JobAbilityList),
            [(StockUiActionMenu.AbilityMenu, 3)] = new("Weapon Skl.", Command: StockUiMenuCommand.WeaponSkillList),
            [(StockUiActionMenu.AbilityMenu, 4)] = new("Ranged Atk.", Command: StockUiMenuCommand.RangedAttack),
            [(StockUiActionMenu.AbilityMenu, 6)] = new("Mount", Command: StockUiMenuCommand.MountList),
            [(StockUiActionMenu.AbilityMenu, 5)] = new("Pet Command", Command: StockUiMenuCommand.PetCommandList),
            [(StockUiActionMenu.CommandAbilityMenu, 1)] = new("Job Abil.", Command: StockUiMenuCommand.JobAbilityList),
            [(StockUiActionMenu.CommandAbilityMenu, 2)] = new("Weapon Skl.", Command: StockUiMenuCommand.WeaponSkillList),
            [(StockUiActionMenu.CommandAbilityMenu, 3)] = new("Ranged Atk.", Command: StockUiMenuCommand.RangedAttack),
            [(StockUiActionMenu.CommandAbilityMenu, 5)] = new("Mount", Command: StockUiMenuCommand.MountList),
            [(StockUiActionMenu.CommandAbilityMenu, 4)] = new("Pet Command", Command: StockUiMenuCommand.PetCommandList),
        };

        private static StockUiMenuEntry Party(string label, StockUiPartyAction action, int value = 0) =>
            new(label, Command: StockUiMenuCommand.PartyAction, Argument: StockUiParty.Action(action, value));

        private static StockUiMenuEntry Person(string label, StockUiPartyAction action) =>
            new(label, Command: StockUiMenuCommand.PersonAction, Argument: (int)action);

        private static StockUiMenuEntry Search(string label, StockUiSearchAction action, int value = 0) =>
            new(label, Command: StockUiMenuCommand.SearchAction, Argument: StockUiSearch.Action(action, value));

        private static StockUiMenuEntry Spells(string label, int button) =>
            new(label, Command: StockUiMenuCommand.SpellList, Argument: StockUiActionMenu.CategoryKinds[button]);

        /// <summary>
        /// Looks up what a menu's button does: the table above, then the config pages (an option choice, a slider,
        /// a chat-filter row); false for buttons without a client meaning (titles, arrows).
        /// </summary>
        public static bool TryGet(string menuName, int buttonId, out StockUiMenuEntry entry)
        {
            if (Entries.TryGetValue((menuName, buttonId), out entry)) return true;
            if (StockUiConfigPages.TryGet(menuName, out var page))
            {
                if (page.TryGetOption(buttonId, out var option, out var choice))
                {
                    entry = new StockUiMenuEntry($"{option.Label}: {choice.Name}", Command: StockUiMenuCommand.ConfigOption, Argument: choice.Value);
                    return true;
                }
                if (page.TryGetSlider(buttonId, out var slider))
                {
                    entry = new StockUiMenuEntry(slider.Label, Command: StockUiMenuCommand.ConfigSlider);
                    return true;
                }
            }
            if (menuName.Equals(StockUiConfigPages.ChatFiltersPage, StringComparison.OrdinalIgnoreCase)
                && buttonId >= 1 && buttonId <= StockUiConfigPages.ChatFilterRowsPerPage)
            {
                entry = new StockUiMenuEntry("Chat filter", Command: StockUiMenuCommand.ChatFilter, Argument: buttonId);
                return true;
            }
            if (menuName.Equals(StockUiConfigPages.FontColorListMenu, StringComparison.OrdinalIgnoreCase)
                && buttonId >= 1 && buttonId <= StockUiConfigPages.FontColorListRows)
            {
                entry = new StockUiMenuEntry("Font color", Command: StockUiMenuCommand.FontColorRow, Argument: buttonId);
                return true;
            }
            if (menuName.Equals(StockUiConfigPages.LogListMenu, StringComparison.OrdinalIgnoreCase)
                && buttonId >= 1 && buttonId <= StockUiConfigPages.LogListRows)
            {
                entry = new StockUiMenuEntry("Log message type", Command: StockUiMenuCommand.LogRow, Argument: buttonId);
                return true;
            }
            if (menuName.Equals(StockUiConfigPages.EffectsPage, StringComparison.OrdinalIgnoreCase)
                && buttonId >= 1 && buttonId <= StockUiConfigPages.EffectRows)
            {
                entry = new StockUiMenuEntry("Effect filter", Command: StockUiMenuCommand.EffectFilter, Argument: buttonId);
                return true;
            }
            return false;
        }

        private sealed class MenuKeyComparer : IEqualityComparer<(string Menu, int Button)>
        {
            public bool Equals((string Menu, int Button) x, (string Menu, int Button) y) =>
                x.Button == y.Button && string.Equals(x.Menu, y.Menu, StringComparison.OrdinalIgnoreCase);

            public int GetHashCode((string Menu, int Button) key) =>
                HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(key.Menu), key.Button);
        }
    }
}
