// src/Gordian.Engine/Ui/StockUiTrade.cs
using System;
using System.Collections.Generic;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources.Models;
using Gordian.Core.Resources.Ui;
using Gordian.Core.World;

namespace Gordian.Engine.Ui
{
    /// <summary>One filled slot of a trade window: the item, how many, and the inventory slot it comes from.</summary>
    public readonly record struct StockUiTradeSlot(ushort ItemId, uint Count, byte InventorySlot);

    /// <summary>What a trade target is: a player is asked to trade (C2S 0x032), an NPC is handed items (C2S 0x036).</summary>
    public enum StockUiTradeTargetKind
    {
        None = 0,
        Self,
        Player,
        Npc,

        /// <summary>A monster, pet, trust, door...: "You cannot trade with that target."</summary>
        Other,
    }

    /// <summary>A trade target resolved from the world: its ids, name and kind.</summary>
    public readonly record struct StockUiTradeTarget(uint ServerId, ushort Index, string Name, StockUiTradeTargetKind Kind);

    /// <summary>
    /// The stock trade windows (#486) from the English menu DAT (39542, ROM/119/51; read 2026-10-31 with the menu dump):
    /// <list type="bullet">
    /// <item><c>trade</c> (16,48, 182 x 94, frames #14, cursor <c>anc_equi</c>): the player trade window. The 4 x 2 slots
    /// (buttons 1-8, 32 x 32 at (7,22) + 34 n, <c>keytops3</c> #51), OK (9, <c>keytops3</c> #53, its kind-3 alternate #54)
    /// and Cancel (10, #56 / #57), and the gil field (11, the red <c>numctrl</c> #0 bar at (76,7), 84 x 16, with the coin
    /// baked into the frame at its right). No help or title ids: the client supplies them.</item>
    /// <item><c>handover</c>: the same frame, buttons and art; the NPC trade window (C2S 0x036).</item>
    /// <item><c>moneyctr</c> (130,240, 366 x 56, title 88, help 352): the gil amount, shared with the Auction House's Price
    /// Set (<see cref="StockUiAuction.PriceMenu"/>).</item>
    /// </list>
    /// The client's own texts are system messages of ROM/27/76 (file id 7031; found 2026-10-31 by searching it for "trade"):
    /// 24 "{name} wishes to trade with you.", 25 "You have already sent a trade request to {name}.", 26 "You cannot trade with
    /// {name} at this time.", 27 "You cannot trade the {item} to {name}.", 28 "You cannot trade with that target.", 29
    /// "Select a target to trade with.", 30 "Trade complete.", 104 "That item is already placed in the trade window."; the
    /// menu texts ROM/165/72 #128 "Already placed in trade slot.", ROM/165/75 #327 "Trade items or money with current
    /// target." (the main menu's Trade), ROM/165/76 #10 "Trade" (a title), ROM/165/77 #45 "Cancel trade transaction?".
    /// The packet flow is LandSandBoat's (map/packets/c2s/0x032_trade_req.cpp, 0x033_trade_res.cpp, 0x034_trade_list.cpp,
    /// 0x036_item_transfer.cpp, map/items/transactions/player_trade.cpp): gil is trade slot 0 (item 0xFFFF, inventory slot
    /// 0), items slots 1-8; the target accepts a request with C2S 0x033 Start, never a second 0x032.
    /// Round 1 of the 2026-10-31 test (a retail client as the partner, image-24 / image-25): each side's name in its window's
    /// top band, orange once that side pressed OK; the partner's window has no OK / Cancel; the cursor reaches the partner's
    /// slots; the gil field takes the arrow cursor; Trade with a request waiting opens that trade whatever is targeted.
    /// PROVISIONAL: the partner window's place (a second <c>trade</c> frame under ours), whether retail asks "Cancel trade
    /// transaction?" before cancelling, the lines of the S2C 0x022 error kinds.
    /// </summary>
    public static class StockUiTrade
    {
        public const string Menu = "trade";
        public const string NpcMenu = "handover";
        public const int SlotCount = 8, OkButton = 9, CancelButton = 10, GilButton = 11;

        /// <summary>Layout x of the inventory beside the window (16 + 182), as the synthesis grid has it.</summary>
        public const int ListX = 198;

        /// <summary>Layout y of the partner's offer: under ours (48 + 94) with a 2 px gap. PROVISIONAL.</summary>
        public const int PartnerY = 144;

        /// <summary>Layout x / y of the item description under the windows (as the synthesis grid's, 16,240).</summary>
        public const int DescriptionX = 16;

        /// <summary>The gil's trade slot and item id (LandSandBoat <c>PlayerTradeTransaction::GilSlot</c>, <c>ITEMID::GIL</c>).</summary>
        public const byte GilTradeIndex = 0;
        public const ushort GilItemId = InventoryState.GilItemId;

        /// <summary>The item flag of Ex items, which cannot go to another player (LandSandBoat <c>ItemFlag::Exclusive</c>).</summary>
        public const uint ExclusiveFlag = 0x4000;

        /// <summary>The most gil a trade carries (C2S 0x034's count is a u32; the client's field is nine digits).</summary>
        public const uint MaxGil = StockUiAuction.MaxPrice;

        /// <summary>Help texts (ROM/165/75): the main menu's Trade, and "Select target to trade with.".</summary>
        public const int HelpTrade = 327, HelpSelectTarget = 45;

        /// <summary>The help bar's title (ROM/165/76 #10).</summary>
        public const string Title = "Trade";

        /// <summary>The menu message of a stack already in a slot (ROM/165/72 #128).</summary>
        public const int AlreadyPlacedMenuMessage = 128;

        // System messages (ROM/27/76).
        public const int WishesToTrade = 24, AlreadyRequested = 25, CannotTradeNow = 26, CannotTradeItem = 27,
            CannotTradeTarget = 28, SelectTarget = 29, TradeComplete = 30, AlreadyInWindow = 104;

        /// <summary>The prompt before cancelling a trade (ROM/165/77 #45).</summary>
        public const string CancelQuestion = "Cancel trade transaction?";

        /// <summary>A request stays pending this long on the server (LandSandBoat: a new invite to the target is refused within 60 s).</summary>
        public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(60);

        /// <summary>
        /// The client's buttons over the partner's slots (21-24 over 25-28, partner slot 1-8): the cursor goes down into them
        /// to describe the partner's items (round 1, 2026-10-31); they do nothing on Confirm. The partner's window has no OK
        /// or Cancel (they are not ours to press).
        /// </summary>
        public const int PartnerSlotBase = 21;

        /// <summary>Whether a button is one of the partner's slots; <paramref name="index"/> is its slot (0-7).</summary>
        public static bool IsPartnerSlot(int buttonId, out int index)
        {
            index = buttonId - PartnerSlotBase;
            return index >= 0 && index < SlotCount;
        }

        /// <summary>
        /// The player trade window's definition with the partner's slots added under ours (<see cref="PartnerSlotBase"/>): our
        /// bottom row and Cancel lead down into them, their top row leads back up.
        /// </summary>
        public static UiMenuDefinition WithPartnerSlots(UiMenuDefinition template)
        {
            int dy = PartnerY - template.Frame.Y;
            var buttons = new List<UiMenuButton>(template.Buttons.Count + SlotCount);
            foreach (var b in template.Buttons)
            {
                sbyte down = b.ButtonId is >= 5 and <= SlotCount ? (sbyte)(PartnerSlotBase + b.ButtonId - 1)
                    : b.ButtonId == CancelButton ? (sbyte)(PartnerSlotBase + 3) : b.NavDown;
                buttons.Add(Copy(b, b.ButtonId, b.X, b.Y, b.NavUp, down, b.NavLeft, b.NavRight, b.Shapes));
            }
            foreach (var slot in template.Buttons)
            {
                if (slot.ButtonId < 1 || slot.ButtonId > SlotCount) continue;
                int i = slot.ButtonId - 1, row = i / 4, col = i % 4, id = PartnerSlotBase + i;
                sbyte up = (sbyte)(row == 0 ? slot.ButtonId + 4 : id - 4);
                sbyte down = (sbyte)(row == 0 ? id + 4 : id);
                sbyte left = (sbyte)(col == 0 ? id : id - 1), right = (sbyte)(col == 3 ? id : id + 1);
                buttons.Add(Copy(slot, (short)id, slot.X, (short)(slot.Y + dy), up, down, left, right, Array.Empty<UiShapeReference>()));
            }
            return new UiMenuDefinition
            {
                DatId = template.DatId,
                Category = template.Category,
                Name = template.Name,
                MenuType = template.MenuType,
                Frame = template.Frame,
                Buttons = buttons,
            };
        }

        private static UiMenuButton Copy(UiMenuButton b, short id, short x, short y, sbyte up, sbyte down, sbyte left, sbyte right, IReadOnlyList<UiShapeReference> shapes) => new()
        {
            ButtonId = id,
            X = x,
            Y = y,
            Width = b.Width,
            Height = b.Height,
            CursorOffsetX = b.CursorOffsetX,
            CursorOffsetY = b.CursorOffsetY,
            SelectRectOffsetX = b.SelectRectOffsetX,
            SelectRectOffsetY = b.SelectRectOffsetY,
            NavUp = up,
            NavDown = down,
            NavLeft = left,
            NavRight = right,
            Shapes = shapes,
            HelpTextId = b.HelpTextId,
            TitleTextId = b.TitleTextId,
        };

        /// <summary>
        /// The English text of a trade system message, for when the table is not loaded (tests): <paramref name="name"/> is
        /// the other player, <paramref name="item"/> the item (message 27).
        /// </summary>
        public static string FallbackText(int message, string name = "", string item = "") => message switch
        {
            WishesToTrade => $"{name} wishes to trade with you.",
            AlreadyRequested => $"You have already sent a trade request to {name}.",
            CannotTradeNow => $"You cannot trade with {name} at this time.",
            CannotTradeItem => $"You cannot trade the {item} to {name}.",
            CannotTradeTarget => "You cannot trade with that target.",
            SelectTarget => "Select a target to trade with.",
            TradeComplete => "Trade complete.",
            AlreadyInWindow => "That item is already placed in the trade window.",
            _ => string.Empty,
        };

        /// <summary>
        /// The system message an S2C 0x022 result prints, or -1 for none: End "Trade complete.", ErrNoSearchYou "You cannot
        /// trade with that target.", ErrNowReq "You have already sent a trade request to ...", the other errors "You cannot
        /// trade with ... at this time.", and so does the partner's Cancel: the retail client printed "You cannot trade with
        /// Gordian at this time." when GordianXI cancelled a trade with it (round 1, 2026-10-31; LandSandBoat forwards a
        /// C2S 0x033 Cancel to the partner as S2C 0x022 Cancel, <c>PlayerTradeTransaction::abort</c>). PROVISIONAL: the
        /// lines for the error kinds are not captured.
        /// </summary>
        public static int ResultMessage(TradeResultKind kind) => kind switch
        {
            TradeResultKind.End => TradeComplete,
            TradeResultKind.ErrNoSearchYou => CannotTradeTarget,
            TradeResultKind.ErrNowReq => AlreadyRequested,
            TradeResultKind.Cancel or TradeResultKind.ErrEtc or TradeResultKind.ErrYouTrade or TradeResultKind.ErrLogout => CannotTradeNow,
            _ => -1,
        };

        /// <summary>Whether a result closes the trade window (Cancel, End, the errors).</summary>
        public static bool Closes(TradeResultKind kind) =>
            kind is TradeResultKind.Cancel or TradeResultKind.End || kind >= TradeResultKind.ErrEtc;

        /// <summary>Whether an item may go to another player (Ex items may not; any item may go to an NPC).</summary>
        public static bool IsTradeable(ItemRecord? record, bool npc) => npc || record == null || (record.Flags & ExclusiveFlag) == 0;

        /// <summary>
        /// The picker's rows: the inventory in the client's order, worn and (to a player) Ex items greyed, stacks already in
        /// a slot marked and greyed ("Already placed in trade slot.").
        /// </summary>
        public static List<StockUiItemRow> OfferRows(List<StockUiItemRow> inventoryRows, IReadOnlyList<StockUiTradeSlot?> slots, Func<ushort, ItemRecord?>? lookup,
            bool npc, string partnerName, Func<int, string?>? menuMessage)
        {
            var rows = new List<StockUiItemRow>(inventoryRows.Count);
            foreach (var row in inventoryRows)
            {
                bool placed = false;
                foreach (var slot in slots) placed |= slot is { } s && s.InventorySlot == row.Slot && s.ItemId == row.ItemId;
                var r = row with { Picked = placed };
                if (placed) r = r with { Greyed = true, GreyedReason = menuMessage?.Invoke(AlreadyPlacedMenuMessage) ?? "Already placed in trade slot." };
                else if (row.Equipped) r = r with { Greyed = true, GreyedReason = $"{row.Name} is equipped." };
                else if (!IsTradeable(lookup?.Invoke(row.ItemId), npc))
                    r = r with { Greyed = true, GreyedReason = FallbackText(CannotTradeItem, partnerName, StockUiShop.LongName(StockUiShop.Lookup(lookup, row.ItemId))) };
                rows.Add(r);
            }
            return rows;
        }

        /// <summary>
        /// A trade window's slots from the server's view of an offer (S2C 0x025 / 0x023: trade slot 1-8 = window slot 0-7,
        /// trade slot 0 = the gil).
        /// </summary>
        public static (StockUiTradeSlot?[] Slots, uint Gil) FromOffer<T>(IReadOnlyDictionary<byte, T> offer, Func<T, (ushort ItemId, uint Count, byte Slot)> read)
        {
            var slots = new StockUiTradeSlot?[SlotCount];
            uint gil = 0;
            foreach (var (index, value) in offer)
            {
                var (itemId, count, slot) = read(value);
                if (index == GilTradeIndex) gil = count;
                else if (index >= 1 && index <= SlotCount) slots[index - 1] = new StockUiTradeSlot(itemId, count, slot);
            }
            return (slots, gil);
        }

        /// <summary>
        /// C2S 0x036's entries for an NPC trade: the gil first (inventory slot 0, XiPackets <c>world/client/0x0036</c>), then
        /// each filled slot's inventory slot and count. Empty when nothing is offered.
        /// </summary>
        public static (byte Slot, uint Count)[] NpcEntries(IReadOnlyList<StockUiTradeSlot?> slots, uint gil)
        {
            var entries = new List<(byte, uint)>(SlotCount + 1);
            if (gil > 0) entries.Add((0, gil));
            foreach (var slot in slots)
            {
                if (slot is { } s && s.Count > 0) entries.Add((s.InventorySlot, s.Count));
            }
            return entries.ToArray();
        }
    }
}
