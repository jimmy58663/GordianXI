// src/Gordian.Core/Events/EventMessageNames.cs
using Gordian.Core.Resources;
using Gordian.Core.Resources.Models;
using Gordian.Core.Ui;

namespace Gordian.Core.Events
{
    /// <summary>
    /// Resolves the names a dialog line's 0x01 tags ask for (<see cref="IEventMessageContext.ResolveName"/>) from the
    /// game's own tables: items (the item DATs' name, log name and plural log name), key items (name and plural), zone
    /// names (d_msg ROM/165/84), weather nouns and adjectives (d_msg ROM/165/79) and Unity leader names (d_msg ROM/165/61 rows 419-429). The kinds come from <see cref="EventMessageFormatter"/>, which maps the other tag kinds
    /// onto these.
    /// </summary>
    public static class EventMessageNames
    {
        /// <summary>The "No Unity" row of <see cref="DMsgCategory.MiscStrings"/>; Unity leader n (1-11) is the row n after it.</summary>
        public const int UnityLeaderRow = 418;

        /// <summary>The Unity leaders (Pieuje ... Sylvie).</summary>
        public const int UnityLeaderCount = 11;

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
                case EventMessageFormatter.WeatherKind:
                    return resources.TryGetWeatherName(id, adjective: false, out var weather) ? weather : null;
                case EventMessageFormatter.WeatherAdjectiveKind:
                    return resources.TryGetWeatherName(id, adjective: true, out var weatherAdjective) ? weatherAdjective : null;
                case EventMessageFormatter.UnityLeaderKind:
                    return id is > 0 and <= UnityLeaderCount
                        && resources.TryGetString(DMsgCategory.MiscStrings, UnityLeaderRow + id, out var leader) ? leader : null;
                default:
                    return null;
            }
        }
    }
}
