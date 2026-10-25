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
            [(MainMenu, 8)] = new("Search", Command: StockUiMenuCommand.NotAvailable),
            [(MainMenu, 9)] = new("Linkshell", Command: StockUiMenuCommand.NotAvailable),
            [(MainMenu, 11)] = new("Region Info", Command: StockUiMenuCommand.NotAvailable),
            [(MainMenu, 12)] = new("Map", Command: StockUiMenuCommand.Map),

            // Commands, page 2.
            [(MainMenuPage2, 1)] = new("Missions", Command: StockUiMenuCommand.NotAvailable),
            [(MainMenuPage2, 2)] = new("Quests", Command: StockUiMenuCommand.NotAvailable),
            [(MainMenuPage2, 3)] = new("Key Items", Command: StockUiMenuCommand.KeyItems),
            [(MainMenuPage2, 4)] = new("View House", Command: StockUiMenuCommand.NotAvailable),
            [(MainMenuPage2, 5)] = new("Bazaar", Command: StockUiMenuCommand.NotAvailable),
            [(MainMenuPage2, 6)] = new("Macros", Opens: StockUiEquipSets.MacroMenu),
            [(MainMenuPage2, 7)] = new("Config", Opens: ConfigMenu),
            [(MainMenuPage2, 8)] = new("Help Desk", Command: StockUiMenuCommand.NotAvailable),
            [(MainMenuPage2, 10)] = new("Current Time", Command: StockUiMenuCommand.CurrentTime),
            [(MainMenuPage2, 9)] = new("Communication", Command: StockUiMenuCommand.NotAvailable),
            [(MainMenuPage2, 11)] = new("Shut Down", Command: StockUiMenuCommand.ShutDown),
            [(MainMenuPage2, 12)] = new("Log Out", Command: StockUiMenuCommand.LogOut),

            // Macros ("mcrmenu", "Macro"): Edit Macro (help 829, #339) and Edit Equip. Set (help 830, #92).
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

            // Party (#397): "prty1" not in a party, "prty2" a member, "prty3" the leader (label sprites read against the retail
            // captures). Only Disband works so far; the rest post "not available yet".
            [(StockUiParty.NotInPartyMenu, StockUiParty.SeekPartyButton)] = new("Seek Party", Command: StockUiMenuCommand.NotAvailable),
            [(StockUiParty.NotInPartyMenu, StockUiParty.UngroupedFindMemberButton)] = new("Find Member", Command: StockUiMenuCommand.NotAvailable),
            [(StockUiParty.NotInPartyMenu, StockUiParty.UngroupedRequestButton)] = new("Request", Command: StockUiMenuCommand.NotAvailable),
            [(StockUiParty.NotInPartyMenu, StockUiParty.UngroupedViewRequestButton)] = new("View Request", Command: StockUiMenuCommand.NotAvailable),
            [(StockUiParty.NotInPartyMenu, StockUiParty.AutogroupButton)] = new("Autogroup", Command: StockUiMenuCommand.NotAvailable),
            [(StockUiParty.NotInPartyMenu, StockUiParty.LanguagesButton)] = new("Languages", Command: StockUiMenuCommand.NotAvailable),
            [(StockUiParty.NotInPartyMenu, StockUiParty.AlarmTypeButton)] = new("Alarm Type", Command: StockUiMenuCommand.NotAvailable),
            [(StockUiParty.MemberMenu, StockUiParty.MemberListButton)] = new("Member List", Command: StockUiMenuCommand.NotAvailable),
            [(StockUiParty.MemberMenu, StockUiParty.MemberRequestButton)] = new("Request", Command: StockUiMenuCommand.NotAvailable),
            [(StockUiParty.MemberMenu, StockUiParty.MemberViewRequestButton)] = new("View Request", Command: StockUiMenuCommand.NotAvailable),
            [(StockUiParty.MemberMenu, StockUiParty.MemberRearrangeButton)] = new("Rearrange", Command: StockUiMenuCommand.NotAvailable),
            [(StockUiParty.MemberMenu, StockUiParty.MemberFocusButton)] = new("Focus", Command: StockUiMenuCommand.NotAvailable),
            [(StockUiParty.MemberMenu, StockUiParty.MemberDistributionButton)] = new("Distribution", Command: StockUiMenuCommand.NotAvailable),
            [(StockUiParty.MemberMenu, StockUiParty.MemberDisbandButton)] = new("Disband", Command: StockUiMenuCommand.PartyDisband),
            [(StockUiParty.LeaderMenu, StockUiParty.LeaderFindMemberButton)] = new("Find Member", Command: StockUiMenuCommand.NotAvailable),
            [(StockUiParty.LeaderMenu, StockUiParty.LeaderMemberListButton)] = new("Member List", Command: StockUiMenuCommand.NotAvailable),
            [(StockUiParty.LeaderMenu, StockUiParty.LeaderRequestButton)] = new("Request", Command: StockUiMenuCommand.NotAvailable),
            [(StockUiParty.LeaderMenu, StockUiParty.LeaderViewRequestButton)] = new("View Request", Command: StockUiMenuCommand.NotAvailable),
            [(StockUiParty.LeaderMenu, StockUiParty.LeaderDistributionButton)] = new("Distribution", Command: StockUiMenuCommand.NotAvailable),
            [(StockUiParty.LeaderMenu, StockUiParty.ChangeLeaderButton)] = new("Chg. Leader", Command: StockUiMenuCommand.NotAvailable),
            [(StockUiParty.LeaderMenu, StockUiParty.LeaderRearrangeButton)] = new("Rearrange", Command: StockUiMenuCommand.NotAvailable),
            [(StockUiParty.LeaderMenu, StockUiParty.LeaderFocusButton)] = new("Focus", Command: StockUiMenuCommand.NotAvailable),
            [(StockUiParty.LeaderMenu, StockUiParty.RemoveMemberButton)] = new("Rmv. Member", Command: StockUiMenuCommand.NotAvailable),
            [(StockUiParty.LeaderMenu, StockUiParty.LeaderDisbandButton)] = new("Disband", Command: StockUiMenuCommand.PartyDisband),
            [(StockUiParty.LeaderMenu, StockUiParty.AllianceButton)] = new("Alliance", Command: StockUiMenuCommand.NotAvailable),
            [(StockUiParty.LeaderMenu, StockUiParty.LevelSyncButton)] = new("Level Sync", Command: StockUiMenuCommand.NotAvailable),

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
