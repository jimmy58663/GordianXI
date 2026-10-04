// src/Gordian.Core/Events/EventMessageNames.cs
using Gordian.Core.Resources;
using Gordian.Core.Resources.Models;
using Gordian.Core.Ui;

namespace Gordian.Core.Events
{
    /// <summary>
    /// Resolves the names a dialog line's 0x01 tags ask for (<see cref="IEventMessageContext.ResolveName"/>) from the
    /// game's own tables: items (the item DATs' name, log name and plural log name), key items (name and plural) and zone
    /// names (d_msg ROM/165/84). The kinds come from <see cref="EventMessageFormatter"/>, which maps the other tag kinds
    /// onto these.
    /// </summary>
    public static class EventMessageNames
    {
        public static string? Resolve(ResourceManager resources, byte kind, int id)
        {
            if (id < 0) return null;
            switch (kind)
            {
                case EventMessageFormatter.ItemKind:
                    return resources.TryGetItem((uint)id, out var item) && item != null ? item.Name : null;
                case EventMessageFormatter.ItemLogNameKind:
                    return resources.TryGetItem((uint)id, out var logItem) && logItem != null ? StockUiShop.LongName(logItem) : null;
                case EventMessageFormatter.ItemPluralKind:
                    return resources.TryGetItem((uint)id, out var pluralItem) && pluralItem != null ? StockUiShop.PluralName(pluralItem) : null;
                case EventMessageFormatter.KeyItemKind:
                    return resources.TryGetKeyItemName((uint)id, out var keyItem) ? keyItem : null;
                case EventMessageFormatter.KeyItemPluralKind:
                    return resources.TryGetKeyItemPlural((uint)id, out var keyItems) ? keyItems : null;
                case EventMessageFormatter.ZoneKind:
                    return resources.TryGetString(DMsgCategory.ZoneNames, id, out var zone) ? zone : null;
                default:
                    return null;
            }
        }
    }
}
