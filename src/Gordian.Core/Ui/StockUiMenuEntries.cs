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

        /// <summary>One choice of a config page's option row (<see cref="StockUiConfigPages"/>): confirm selects it.</summary>
        ConfigOption,

        /// <summary>A config page's slider (<see cref="StockUiConfigPages"/>): left/right move it.</summary>
        ConfigSlider,

        /// <summary>A row of the Chat Filters list: confirm toggles the filter.</summary>
        ChatFilter,
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
            // Font Colors ("conftxtc" -> "textcol1"/"textcol3"), Log ("conf11l" -> "conf11s"/"conf11m") and Effects
            // ("fxfilter") are list pages whose row text comes from the menu string table, still unresearched;
            // Gamepad is the key-assignment editor ("keypad"/"k1assign").
            [(ConfigMenu, 1)] = new("Gameplay", Opens: StockUiConfigPages.GameplayPage),
            [(ConfigMenu, 2)] = new("Chat Filters", Opens: StockUiConfigPages.ChatFiltersPage),
            [(ConfigMenu, 3)] = new("Font Colors", Command: StockUiMenuCommand.NotAvailable),
            [(ConfigMenu, 4)] = new("Windows", Opens: WindowsMenu),
            [(ConfigMenu, 10)] = new("Log", Command: StockUiMenuCommand.NotAvailable),
            [(ConfigMenu, 5)] = new("Misc.", Opens: StockUiConfigPages.MiscPage),
            [(ConfigMenu, 6)] = new("Misc. 2", Opens: StockUiConfigPages.Misc2Page),
            [(ConfigMenu, 11)] = new("Misc. 3", Opens: StockUiConfigPages.Misc3Page),
            [(ConfigMenu, 12)] = new("Misc. 4", Opens: StockUiConfigPages.Misc4Page),
            [(ConfigMenu, 7)] = new("Effects", Command: StockUiMenuCommand.NotAvailable),
            [(ConfigMenu, 8)] = new("Mouse/Cam.", Opens: StockUiConfigPages.MouseCameraPage),
            [(ConfigMenu, 9)] = new("Global", Opens: StockUiConfigPages.GlobalPage),
            [(ConfigMenu, 13)] = new("Gamepad", Command: StockUiMenuCommand.NotAvailable),

            // Windows: which window's settings to edit.
            [(WindowsMenu, 1)] = new("Shared", Opens: StockUiConfigPages.WindowSettingsPage),
            [(WindowsMenu, 2)] = new("Window 1", Opens: StockUiConfigPages.Window1SettingsPage),
            [(WindowsMenu, 3)] = new("Window 2", Opens: StockUiConfigPages.Window2SettingsPage),
        };

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
