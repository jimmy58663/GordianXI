// src/Gordian.Engine/Ui/StockUiMenuHelpBar.cs
using System;

namespace Gordian.Engine.Ui
{
    /// <summary>
    /// What the help bar at the top of the screen (<c>titlewin</c> + <c>helpwind</c>) shows over an open menu that draws no
    /// bar of its own (#313), from the maintainer's retail captures of 2026-10-16/17 (<c>Config.png</c>, <c>Gameplay.png</c>,
    /// <c>Gameplay_Controls.png</c>, <c>Long_Help.png</c>, <c>Synthesis.png</c>):
    /// <list type="bullet">
    /// <item>The title is the open window's: its frame's <c>TitleTextId</c> (ROM/165/76), which stays while the cursor moves
    /// ("Config" with the cursor on Gameplay: <c>configwi</c>'s frame carries title 54). A frame without one takes the
    /// selected button's: the main menu (<c>menuwind</c>, no frame title) names the window the entry opens ("Config" on page
    /// 2's Config). A window with neither takes its parent's, the same way.</item>
    /// <item>The help line is the selected button's <c>HelpTextId</c> (ROM/165/75), else the frame's: every capture shows the
    /// entry's help, and frame help ("Select an item.") only where the entries carry none.</item>
    /// </list>
    /// </summary>
    public static class StockUiMenuHelpBar
    {
        /// <summary>The bar's title and help line over <paramref name="top"/>; both null when it names neither.</summary>
        public static (string? Title, string? Help) Of(StockUiOpenMenu top, Func<int, string?>? titleText, Func<int, string?>? helpText)
        {
            string? title = null;
            for (var menu = top; menu != null && title == null; menu = menu.Parent)
            {
                title = menu.HelpTitle;
                if (title == null && menu.Menu.Frame.TitleTextId >= 0) title = titleText?.Invoke(menu.Menu.Frame.TitleTextId);
                if (title == null && menu.SelectedButton is { TitleTextId: >= 0 } button) title = titleText?.Invoke(button.TitleTextId);
            }
            int helpId = top.SelectedButton is { HelpTextId: >= 0 } selected ? selected.HelpTextId : top.Menu.Frame.HelpTextId;
            string? help = helpId >= 0 ? helpText?.Invoke(helpId) : null;
            return (title, help);
        }
    }
}
