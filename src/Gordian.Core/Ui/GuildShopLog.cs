// src/Gordian.Core/Ui/GuildShopLog.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using Gordian.Core.Events;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources.Models;
using Gordian.Core.World;

namespace Gordian.Core.Ui
{
    /// <summary>
    /// The message log lines for guild shop answers (S2C 0x082 to 0x086), until a guild shop window exists.
    /// <list type="bullet">
    /// <item>A purchase or sale prints the client's own line from the system message table (<c>ROM/27/76.DAT</c>, file
    /// id 7031): 165 / 166 "You buy ... from the guild.", 167 / 168 "You sell ... to the guild." (the ids come from the
    /// same table as the synthesis lines; which of each pair retail uses was inferred from their codes: the first has a
    /// count, the second an article and one item). Failures print the texts XiPackets quotes from the client.</item>
    /// <item>The stock lists and the open / closed status are test aids, not retail lines.</item>
    /// </list>
    /// </summary>
    public static class GuildShopLog
    {
        /// <summary>System message ids: buy with a count, buy one, sell with a count, sell one.</summary>
        public const int BuyManyMessage = 165, BuyOneMessage = 166, SellManyMessage = 167, SellOneMessage = 168;

        /// <summary>
        /// Lines for a purchase or sale answer, following XiPackets (world/server/0x0082 and 0x0084): <c>ItemNo</c> 0 is a
        /// failure whose <c>Trade</c> says why (-3 too fast, -5 can hold only one for a purchase), <c>Count</c> is the guild's
        /// stock and <c>Trade</c> the amount bought or sold; a sale of 0 means the guild's stock is full, and a negative
        /// amount a partial sale. The texts of the failures are the ones XiPackets quotes from the client.
        /// </summary>
        public static IEnumerable<string> FormatTransaction(GuildTransaction t, Func<ushort, ItemRecord?>? itemLookup, ClientMessageController? messages = null)
        {
            ArgumentNullException.ThrowIfNull(t);
            if (t.ItemId == 0)
            {
                yield return t.IsPurchase && t.Trade == -3 ? "Please wait longer before making another purchase."
                    : t.IsPurchase && t.Trade == -5 ? "Transaction cancelled. You can only hold one item of that type."
                    : "You were unable to carry out that transaction.";
                yield break;
            }

            if (!t.IsPurchase && t.Trade == 0)
            {
                yield return "The guild would not buy that from you. Its stock of that item is full.";
                yield break;
            }

            int quantity = Math.Max(1, Math.Abs((int)t.Trade));
            int id = t.IsPurchase ? (quantity == 1 ? BuyOneMessage : BuyManyMessage) : (quantity == 1 ? SellOneMessage : SellManyMessage);
            var lines = messages?.FormatTableMessage(id, new[] { (int)t.ItemId, quantity });
            if (lines is { Count: > 0 })
            {
                foreach (string line in lines) yield return line;
            }
            else
            {
                string what = StockUiShop.DescribeCount(StockUiShop.Lookup(itemLookup, t.ItemId), (uint)quantity);
                yield return t.IsPurchase ? $"You buy {what} from the guild." : $"You sell {what} to the guild.";
            }

            if (!t.IsPurchase && t.Trade < 0) yield return "The guild could not purchase the full amount. Its stock of that item became full.";
        }

        /// <summary>Lines for a complete stock list: what the guild sells (<paramref name="sells"/>) or buys.</summary>
        public static IEnumerable<string> FormatList(bool sells, IReadOnlyList<GuildItemEntry> items, Func<ushort, ItemRecord?>? itemLookup)
        {
            ArgumentNullException.ThrowIfNull(items);
            yield return sells ? $"[Guild] The guild sells {items.Count} item{(items.Count == 1 ? "" : "s")}:" : $"[Guild] The guild buys {items.Count} item{(items.Count == 1 ? "" : "s")}:";
            foreach (var item in items)
            {
                string name = StockUiShop.Lookup(itemLookup, item.ItemId).Name;
                yield return string.Create(CultureInfo.InvariantCulture, $"[Guild]   {name} (item {item.ItemId}): {item.Price} gil, stock {item.Stock}/{item.Max}");
            }
        }

        /// <summary>The line for the guild's status (S2C 0x086).</summary>
        public static string FormatStatus(ShopOpenStatus status, GuildHoursInfo? hours) => status switch
        {
            ShopOpenStatus.Open => "[Guild] The guild shop is open.",
            ShopOpenStatus.Holiday => hours != null
                ? $"[Guild] The guild shop is closed for its weekly holiday (day {hours.HolidayDay})."
                : "[Guild] The guild shop is closed for its weekly holiday.",
            _ => hours != null
                ? $"[Guild] The guild shop is closed; it opens {hours.OpenHour:D2}:00 to {hours.CloseHour:D2}:00 (Vana'diel time)."
                : "[Guild] The guild shop is closed.",
        };
    }
}
