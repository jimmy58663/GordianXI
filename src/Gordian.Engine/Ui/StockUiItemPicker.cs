// src/Gordian.Engine/Ui/StockUiItemPicker.cs
using System;
using System.Collections.Generic;

namespace Gordian.Engine.Ui
{
    /// <summary>
    /// What an item picker (<see cref="StockUiItemListKind.Picker"/>) lists and does: a window that offers items from the
    /// inventory (the trade window's, #486; the Bazaar's later) without knowing what they are for. The picker draws in the
    /// item list (<see cref="StockUiItems.ListMenu"/>, <c>inventor</c>) at its own place; Confirm on a greyed row posts its
    /// <see cref="StockUiItemRow.GreyedReason"/>, a row of which more than one can be picked asks "No. of Items"
    /// (<c>itemctrl</c>, from 1 up to <see cref="Room"/>), and the pick goes to <see cref="Picked"/>, which closes the picker
    /// or not as it likes.
    /// </summary>
    /// <param name="Rows">The rows to list, from the inventory's rows in the client's order (grey or mark them here).</param>
    /// <param name="Room">How many of a row can be picked (0 = none: <see cref="FullText"/> is posted).</param>
    /// <param name="Picked">Called with the row and the count picked.</param>
    /// <param name="Title">The help bar's title while the picker is up ("Trade").</param>
    /// <param name="HelpTextId">The help bar's text (ROM/165/75), -1 for the list's own "Select an item.".</param>
    /// <param name="FullText">The notice when <see cref="Room"/> is 0 for a row that is not greyed.</param>
    public sealed record StockUiItemPicker(
        Func<List<StockUiItemRow>, List<StockUiItemRow>> Rows,
        Func<StockUiItemRow, uint> Room,
        Action<StockUiItemRow, uint> Picked,
        string Title,
        int HelpTextId = -1,
        string? FullText = null);
}
