// src/Gordian.Core/Ui/StockUiKeyItems.cs
using System;
using System.Collections.Generic;
using Gordian.Core.Resources.Tables;

namespace Gordian.Core.Ui
{
    /// <summary>One Key Items category: its name and its key items, in the table's order.</summary>
    public sealed record StockUiKeyItemCategory(string Name, IReadOnlyList<ushort> Ids);

    /// <summary>One row of a Key Items list: the key item, its name, and whether it has not been read yet.</summary>
    public readonly record struct StockUiKeyItemRow(ushort Id, string Name, bool IsNew);

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
    /// The Key Items window (#91; main menu page 2 > Key Items). PROVISIONAL layout, no retail capture yet: the English menu
    /// DAT's only windows titled "Key Items" (title 59, ROM/165/76) are <c>evitem</c> / <c>evitem01</c> (16,48, 366 x 248),
    /// whose rows reference an <c>evitem</c> element group the DATs do not hold, so the window keeps <c>evitem</c>'s frame and
    /// takes the quest log's list rows (<c>quset</c>: fifteen 356 x 16 rows from (0,4)). First the categories with the
    /// number of key items held in each, then a category's held key items in the table's order, new (unread) ones marked;
    /// the description of the key item under the cursor shows at the bottom left. The first time the cursor rests on a new
    /// key item it is marked read (C2S 0x064, as retail does when a yellow-bubble key item is first viewed).
    /// </summary>
    public static class StockUiKeyItems
    {
        public const string WindowMenu = "evitem", RowsMenu = "quset";

        /// <summary>The window title (ROM/165/76 #59).</summary>
        public const string Title = "Key Items";

        /// <summary>Main menu page 2's Key Items help (ROM/165/75 #470): "View list of key items. ..."</summary>
        public const int HelpKeyItems = 470;

        /// <summary>The rows the list shows at once (<c>quset</c>'s row buttons).</summary>
        public const int VisibleRows = 15;

        /// <summary>The marker drawn before a new key item's name (PROVISIONAL: retail's yellow speech bubble is not identified in the DATs).</summary>
        public const string NewMarker = "!";

        /// <summary>A category's held key items, new ones flagged; names fall back to "Key item #id".</summary>
        public static List<StockUiKeyItemRow> Rows(StockUiKeyItemCatalog catalog, StockUiKeyItemCategory category, Func<ushort, bool> held, Func<ushort, bool> seen)
        {
            var rows = new List<StockUiKeyItemRow>();
            foreach (ushort id in category.Ids)
            {
                if (!held(id)) continue;
                string name = catalog.TryGetName(id, out var n) ? n : "Key item #" + id.ToString(System.Globalization.CultureInfo.InvariantCulture);
                rows.Add(new StockUiKeyItemRow(id, Capitalize(name), !seen(id)));
            }
            return rows;
        }

        /// <summary>How many key items of a category are held, and how many of them are new.</summary>
        public static (int Held, int New) Count(StockUiKeyItemCategory category, Func<ushort, bool> held, Func<ushort, bool> seen)
        {
            int count = 0, fresh = 0;
            foreach (ushort id in category.Ids)
            {
                if (!held(id)) continue;
                count++;
                if (!seen(id)) fresh++;
            }
            return (count, fresh);
        }

        /// <summary>The table's names start lower case ("airship pass"); the list shows them as titles start ("Airship pass").</summary>
        public static string Capitalize(string name) =>
            name.Length > 0 && char.IsLower(name[0]) ? char.ToUpperInvariant(name[0]) + name[1..] : name;

        /// <summary>The description wrapped into lines of at most <paramref name="columns"/> characters (at most <paramref name="maxLines"/>).</summary>
        public static List<string> Wrap(string text, int columns = 42, int maxLines = StockUiItemDescription.MaxLines)
        {
            var lines = new List<string>();
            foreach (string paragraph in text.Replace("\r", string.Empty).Split('\n'))
            {
                string rest = paragraph.Trim();
                while (rest.Length > columns && lines.Count < maxLines)
                {
                    int cut = rest.LastIndexOf(' ', columns);
                    if (cut <= 0) cut = columns;
                    lines.Add(rest[..cut].TrimEnd());
                    rest = rest[cut..].TrimStart();
                }
                if (rest.Length > 0 && lines.Count < maxLines) lines.Add(rest);
            }
            return lines;
        }
    }
}
