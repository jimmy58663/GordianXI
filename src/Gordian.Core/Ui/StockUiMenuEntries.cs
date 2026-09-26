// src/Gordian.Core/Ui/StockUiMenuEntries.cs
using System;
using System.Collections.Generic;

namespace Gordian.Core.Ui
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

        /// <summary>Selects window skin 1-8 (the "Window Type" dots of the Windows config page).</summary>
        SelectWindowSkin,
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
    /// "menuwind" (page 1) and "socialme" (page 2, reached with left/right), the config list is "configwi" and the
    /// Windows entry opens the "conf5m" list (Shared / Window 1 / Window 2) whose Shared entry opens the
    /// "Window Settings (Shared)" page "conf5win" (flow confirmed against a retail capture, 2026-09-26).
    /// </summary>
    public static class StockUiMenuEntries
    {
        public const string MainMenu = "menuwind";
        public const string MainMenuPage2 = "socialme";
        public const string ConfigMenu = "configwi";
        public const string WindowsMenu = "conf5m";
        public const string WindowSettingsPage = "conf5win";
        public const string YesNoMenu = "yesno";
        public const string MessageYesNoMenu = "comyn";

        /// <summary>The main menu's pages, flipped with left/right (the DAT's page arrows, buttons 13 and 14).</summary>
        public static readonly IReadOnlyList<string> MainMenuPages = new[] { MainMenu, MainMenuPage2 };

        /// <summary>The first "Window Type" dot of the Windows config page (dots are buttons 7..14 = skins 1..8).</summary>
        public const int WindowSkinFirstButton = 7;

        private static readonly Dictionary<(string Menu, int Button), StockUiMenuEntry> Entries = new(new MenuKeyComparer())
        {
            // Commands, page 1 (buttons in their authored top-to-bottom order).
            [(MainMenu, 1)] = new("Status", Command: StockUiMenuCommand.NotAvailable),
            [(MainMenu, 2)] = new("Equipment", Command: StockUiMenuCommand.NotAvailable),
            [(MainMenu, 3)] = new("Magic", Command: StockUiMenuCommand.NotAvailable),
            [(MainMenu, 4)] = new("Items", Command: StockUiMenuCommand.NotAvailable),
            [(MainMenu, 10)] = new("Synthesis", Command: StockUiMenuCommand.NotAvailable),
            [(MainMenu, 5)] = new("Abilities", Command: StockUiMenuCommand.NotAvailable),
            [(MainMenu, 6)] = new("Party", Command: StockUiMenuCommand.NotAvailable),
            [(MainMenu, 7)] = new("Trade", Command: StockUiMenuCommand.NotAvailable),
            [(MainMenu, 8)] = new("Search", Command: StockUiMenuCommand.NotAvailable),
            [(MainMenu, 9)] = new("Linkshell", Command: StockUiMenuCommand.NotAvailable),
            [(MainMenu, 11)] = new("Region Info", Command: StockUiMenuCommand.NotAvailable),
            [(MainMenu, 12)] = new("Map", Command: StockUiMenuCommand.NotAvailable),

            // Commands, page 2.
            [(MainMenuPage2, 1)] = new("Missions", Command: StockUiMenuCommand.NotAvailable),
            [(MainMenuPage2, 2)] = new("Quests", Command: StockUiMenuCommand.NotAvailable),
            [(MainMenuPage2, 3)] = new("Key Items", Command: StockUiMenuCommand.NotAvailable),
            [(MainMenuPage2, 4)] = new("View House", Command: StockUiMenuCommand.NotAvailable),
            [(MainMenuPage2, 5)] = new("Bazaar", Command: StockUiMenuCommand.NotAvailable),
            [(MainMenuPage2, 6)] = new("Macros", Command: StockUiMenuCommand.NotAvailable),
            [(MainMenuPage2, 7)] = new("Config", Opens: ConfigMenu),
            [(MainMenuPage2, 8)] = new("Help Desk", Command: StockUiMenuCommand.NotAvailable),
            [(MainMenuPage2, 10)] = new("Current Time", Command: StockUiMenuCommand.CurrentTime),
            [(MainMenuPage2, 9)] = new("Communication", Command: StockUiMenuCommand.NotAvailable),
            [(MainMenuPage2, 11)] = new("Shut Down", Command: StockUiMenuCommand.ShutDown),
            [(MainMenuPage2, 12)] = new("Log Out", Command: StockUiMenuCommand.LogOut),

            // Config list (label sprites: 338 Gameplay, 364 Chat Filters, 365 Font Colors, 366 Windows, 728 Log,
            // 367 Misc., 470 Misc. 2, 752 Misc. 3, 792 Misc. 4, 539 Effects, 527 Mouse/Cam., 359 Global, 840 Gamepad).
            [(ConfigMenu, 1)] = new("Gameplay", Command: StockUiMenuCommand.NotAvailable),
            [(ConfigMenu, 2)] = new("Chat Filters", Command: StockUiMenuCommand.NotAvailable),
            [(ConfigMenu, 3)] = new("Font Colors", Command: StockUiMenuCommand.NotAvailable),
            [(ConfigMenu, 4)] = new("Windows", Opens: WindowsMenu),
            [(ConfigMenu, 10)] = new("Log", Command: StockUiMenuCommand.NotAvailable),
            [(ConfigMenu, 5)] = new("Misc.", Command: StockUiMenuCommand.NotAvailable),
            [(ConfigMenu, 6)] = new("Misc. 2", Command: StockUiMenuCommand.NotAvailable),
            [(ConfigMenu, 11)] = new("Misc. 3", Command: StockUiMenuCommand.NotAvailable),
            [(ConfigMenu, 12)] = new("Misc. 4", Command: StockUiMenuCommand.NotAvailable),
            [(ConfigMenu, 7)] = new("Effects", Command: StockUiMenuCommand.NotAvailable),
            [(ConfigMenu, 8)] = new("Mouse/Cam.", Command: StockUiMenuCommand.NotAvailable),
            [(ConfigMenu, 9)] = new("Global", Command: StockUiMenuCommand.NotAvailable),
            [(ConfigMenu, 13)] = new("Gamepad", Command: StockUiMenuCommand.NotAvailable),

            // Windows: which window's settings to edit.
            [(WindowsMenu, 1)] = new("Shared", Opens: WindowSettingsPage),
            [(WindowsMenu, 2)] = new("Window 1", Command: StockUiMenuCommand.NotAvailable),
            [(WindowsMenu, 3)] = new("Window 2", Command: StockUiMenuCommand.NotAvailable),

            // Window Settings (Shared): Log Window Multi-window (1-3), Timestamp (4-6), Window Type (7-14), Window Effect (15-16).
            [(WindowSettingsPage, 1)] = new("Multi-window", Command: StockUiMenuCommand.NotAvailable),
            [(WindowSettingsPage, 2)] = new("Multi-window", Command: StockUiMenuCommand.NotAvailable),
            [(WindowSettingsPage, 3)] = new("Multi-window", Command: StockUiMenuCommand.NotAvailable),
            [(WindowSettingsPage, 4)] = new("Timestamp", Command: StockUiMenuCommand.NotAvailable),
            [(WindowSettingsPage, 5)] = new("Timestamp", Command: StockUiMenuCommand.NotAvailable),
            [(WindowSettingsPage, 6)] = new("Timestamp", Command: StockUiMenuCommand.NotAvailable),
            [(WindowSettingsPage, 7)] = new("Window Type 1", Command: StockUiMenuCommand.SelectWindowSkin, Argument: 1),
            [(WindowSettingsPage, 8)] = new("Window Type 2", Command: StockUiMenuCommand.SelectWindowSkin, Argument: 2),
            [(WindowSettingsPage, 9)] = new("Window Type 3", Command: StockUiMenuCommand.SelectWindowSkin, Argument: 3),
            [(WindowSettingsPage, 10)] = new("Window Type 4", Command: StockUiMenuCommand.SelectWindowSkin, Argument: 4),
            [(WindowSettingsPage, 11)] = new("Window Type 5", Command: StockUiMenuCommand.SelectWindowSkin, Argument: 5),
            [(WindowSettingsPage, 12)] = new("Window Type 6", Command: StockUiMenuCommand.SelectWindowSkin, Argument: 6),
            [(WindowSettingsPage, 13)] = new("Window Type 7", Command: StockUiMenuCommand.SelectWindowSkin, Argument: 7),
            [(WindowSettingsPage, 14)] = new("Window Type 8", Command: StockUiMenuCommand.SelectWindowSkin, Argument: 8),
            [(WindowSettingsPage, 15)] = new("Window Effect", Command: StockUiMenuCommand.NotAvailable),
            [(WindowSettingsPage, 16)] = new("Window Effect", Command: StockUiMenuCommand.NotAvailable),
        };

        /// <summary>Looks up what a menu's button does; false for buttons without a client meaning (titles, arrows).</summary>
        public static bool TryGet(string menuName, int buttonId, out StockUiMenuEntry entry) =>
            Entries.TryGetValue((menuName, buttonId), out entry);

        private sealed class MenuKeyComparer : IEqualityComparer<(string Menu, int Button)>
        {
            public bool Equals((string Menu, int Button) x, (string Menu, int Button) y) =>
                x.Button == y.Button && string.Equals(x.Menu, y.Menu, StringComparison.OrdinalIgnoreCase);

            public int GetHashCode((string Menu, int Button) key) =>
                HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(key.Menu), key.Button);
        }
    }
}
