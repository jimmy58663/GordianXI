// src/Gordian.Core/Ui/StockUiKeyItems.cs
using System;
using System.Collections.Generic;
using Gordian.Core.Resources.Tables;

namespace Gordian.Engine.Ui
{
    /// <summary>One Key Items category: its name and its key items, in the table's order.</summary>
    public sealed record StockUiKeyItemCategory(string Name, IReadOnlyList<ushort> Ids);

    /// <summary>One row of a Key Items list: the key item, its name as listed (capitalised), whether it has not been read yet, and its name as the table writes it (for "Examine %s?").</summary>
    public readonly record struct StockUiKeyItemRow(ushort Id, string Name, bool IsNew, string TableName = "");

    /// <summary>
    /// The key item table (ROM/175/35, English) as the Key Items window lists it (#91). The table is sparse and stored in
    /// category order: each record's sub-field 0 is its key item id, and a record with id 0 whose name starts with "-" is
    /// the separator that closes the category of the rows before it ("-Temporary Key Items", "-Mounts", ...). Row 0 is a
    /// filler ("in-text name") and belongs to no category.
    /// Category layout referenced from xi-tools (https://github.com/vekien/xi-tools), docs/keyitems/categories.md.
    /// </summary>
    public sealed class StockUiKeyItemCatalog
    {
        private readonly Dictionary<ushort, (string Name, string Description)> _items = new();

        private StockUiKeyItemCatalog(IReadOnlyList<StockUiKeyItemCategory> categories)
        {
            Categories = categories;
        }

        public IReadOnlyList<StockUiKeyItemCategory> Categories { get; }

        public bool TryGetName(ushort id, out string name)
        {
            if (_items.TryGetValue(id, out var entry) && entry.Name.Length > 0)
            {
                name = entry.Name;
                return true;
            }
            name = string.Empty;
            return false;
        }

        /// <summary>The key item's description (the record's sub-field 6), empty when it has none.</summary>
        public string Description(ushort id) => _items.TryGetValue(id, out var entry) ? entry.Description : string.Empty;

        /// <summary>Builds the catalog from the key item table; null without a table.</summary>
        public static StockUiKeyItemCatalog? FromTable(DMsgStringTable? table)
        {
            if (table == null) return null;
            var categories = new List<StockUiKeyItemCategory>();
            var pending = new List<ushort>();
            var seen = new HashSet<ushort>();
            var catalogItems = new Dictionary<ushort, (string, string)>();
            foreach (var record in table.Records)
            {
                record.NamedFields.TryGetValue("name", out var name);
                name ??= string.Empty;
                if (!record.TryGetNumber(0, out uint id) || id == 0)
                {
                    if (name.StartsWith('-'))
                    {
                        categories.Add(new StockUiKeyItemCategory(name[1..].Trim(), pending.ToArray()));
                        pending.Clear();
                    }
                    continue;
                }
                if (id > ushort.MaxValue || !seen.Add((ushort)id)) continue;
                record.NamedFields.TryGetValue("description", out var description);
                catalogItems[(ushort)id] = (name, description ?? string.Empty);
                pending.Add((ushort)id);
            }
            // Rows after the last separator (none in the retail table) form a category of their own.
            if (pending.Count > 0) categories.Add(new StockUiKeyItemCategory("Other Key Items", pending.ToArray()));
            var catalog = new StockUiKeyItemCatalog(categories);
            foreach (var (id, entry) in catalogItems) catalog._items[id] = entry;
            return catalog;
        }

        /// <summary>Builds a catalog from categories and names directly (tests).</summary>
        public static StockUiKeyItemCatalog FromEntries(IEnumerable<(string Category, IReadOnlyList<(ushort Id, string Name, string Description)> Items)> categories)
        {
            var list = new List<StockUiKeyItemCategory>();
            var items = new Dictionary<ushort, (string, string)>();
            foreach (var (category, entries) in categories)
            {
                var ids = new List<ushort>();
                foreach (var (id, name, description) in entries)
                {
                    ids.Add(id);
                    items[id] = (name, description);
                }
                list.Add(new StockUiKeyItemCategory(category, ids));
            }
            var catalog = new StockUiKeyItemCatalog(list);
            foreach (var (id, entry) in items) catalog._items[id] = entry;
            return catalog;
        }
    }

    /// <summary>
    /// The Key Items window (#91; main menu page 2 > Key Items), as the maintainer's retail captures 2026-10-13 show it
    /// (image-6 ... image-10, UI 1:1) on the English menu DAT's <c>evitem</c> (16,48, 366 x 248, title 59 "Key Items"):
    /// <list type="bullet">
    /// <item>The help bar: "Key Items" in <c>titlewin</c>; the category's name in <c>helpwind</c> once one is open.</item>
    /// <item>Eight rows at <c>evitem</c>'s own buttons (x 40, y 13 + 29 n, 256 x 18; their <c>evitem</c> element group is a
    /// plain <c>hfr1</c> band): a bullet, then the name in the log font, white, the row under the cursor pink. The categories
    /// carry the small bullet (<c>windowps</c> #71: the <c>colorbal</c> earth ball at 6 x 6); key items not read yet the big one
    /// (<c>keytops3</c> #104: the same ball at 14 x 14), read ones the small one. No counts, nothing greyed.</item>
    /// <item>Confirm on a key item not read yet asks "Examine %s?" (ROM/165/72 #95, the table's name) in <c>evitem01</c> (the
    /// same frame, Yes at (55,74), No at (55,112), the question above; image-9); Yes sends C2S 0x064 (marks it read) and shows
    /// its description. Confirm on a read one shows the description at once.</item>
    /// <item>The description replaces the list in the same window (image-10): the bullet and the name on the first line,
    /// the description under it, wrapped to the window.</item>
    /// <item>A category with none held says "You do not possess any key items." (ROM/165/72 #96; PROVISIONAL that retail
    /// opens the list for it).</item>
    /// </list>
    /// </summary>
    public static class StockUiKeyItems
    {
        public const string WindowMenu = "evitem", PromptMenu = "evitem01";

        /// <summary>The window title (ROM/165/76 #59).</summary>
        public const string Title = "Key Items";

        /// <summary>Main menu page 2's Key Items help (ROM/165/75 #470): "View list of key items. ..."</summary>
        public const int HelpKeyItems = 470;

        /// <summary>The rows <c>evitem</c> shows at once.</summary>
        public const int VisibleRows = 8;

        /// <summary><c>evitem</c>'s rows: x 40, y 13 + 29 n, 256 x 18.</summary>
        public const int RowX = 40, RowY = 13, RowPitch = 29, RowWidth = 256, RowHeight = 18;

        /// <summary><c>evitem01</c>'s Yes and No.</summary>
        public const int YesButton = 1, NoButton = 2;

        /// <summary>The menu message table's (ROM/165/72) key item texts.</summary>
        public const int ExamineText = 95, NoKeyItemsText = 96;

        public const string ExamineFallback = "Examine %s?", NoKeyItemsFallback = "You do not possess any key items.";

        /// <summary>The Examine question for a key item ("Examine job gesture: warrior?").</summary>
        public static string ExaminePrompt(string format, string name) => format.Replace("%s", name, StringComparison.Ordinal);

        /// <summary>A category's held key items, new ones flagged; names fall back to "Key item #id".</summary>
        public static List<StockUiKeyItemRow> Rows(StockUiKeyItemCatalog catalog, StockUiKeyItemCategory category, Func<ushort, bool> held, Func<ushort, bool> seen)
        {
            var rows = new List<StockUiKeyItemRow>();
            foreach (ushort id in category.Ids)
            {
                if (!held(id)) continue;
                string name = catalog.TryGetName(id, out var n) ? n : "Key item #" + id.ToString(System.Globalization.CultureInfo.InvariantCulture);
                rows.Add(new StockUiKeyItemRow(id, Capitalize(name), !seen(id), name));
            }
            return rows;
        }

        /// <summary>The table's names start lower case ("airship pass"); the list shows them as titles start ("Airship pass").</summary>
        public static string Capitalize(string name) =>
            name.Length > 0 && char.IsLower(name[0]) ? char.ToUpperInvariant(name[0]) + name[1..] : name;

        /// <summary>
        /// Text wrapped to <paramref name="width"/> by <paramref name="measure"/>, at spaces, keeping the text's own line
        /// breaks; at most <paramref name="maxLines"/> lines.
        /// </summary>
        public static List<string> Wrap(string text, Func<string, float> measure, float width, int maxLines = 12) =>
            StockUiTextLayout.Wrap(text, measure, width, maxLines, keepBlankLines: true);
    }
}