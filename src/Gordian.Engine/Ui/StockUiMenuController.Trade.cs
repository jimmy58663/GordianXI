// src/Gordian.Engine/Ui/StockUiMenuController.Trade.cs
using System;
using System.Threading.Tasks;
using Gordian.Core.Diagnostics;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources.Ui;
using Gordian.Core.World;

namespace Gordian.Engine.Ui
{
    public sealed partial class StockUiOpenMenu
    {
        /// <summary>A trade window (<see cref="StockUiTrade.Menu"/> with a player, <see cref="StockUiTrade.NpcMenu"/> with an NPC; #486).</summary>
        public bool IsTradeWindow { get; init; }

        /// <summary>Whether the trade window hands items to an NPC (C2S 0x036) rather than trading with a player.</summary>
        public bool IsNpcTrade { get; init; }

        /// <summary>Whom the trade window is with.</summary>
        public StockUiTradeTarget TradePartner { get; init; }

        /// <summary>Our offer's eight slots (null = empty): S2C 0x025's view with a player, chosen here with an NPC.</summary>
        public StockUiTradeSlot?[] TradeSlots { get; internal set; } = new StockUiTradeSlot?[StockUiTrade.SlotCount];

        /// <summary>The gil we offer.</summary>
        public uint TradeGil { get; internal set; }

        /// <summary>The partner's offer (S2C 0x023); empty with an NPC.</summary>
        public StockUiTradeSlot?[] PartnerSlots { get; internal set; } = new StockUiTradeSlot?[StockUiTrade.SlotCount];

        /// <summary>The gil the partner offers.</summary>
        public uint PartnerGil { get; internal set; }

        /// <summary>Whether we pressed OK (C2S 0x033 Make) since the trade last changed.</summary>
        public bool TradeAccepted { get; internal set; }

        /// <summary>Whether the partner pressed OK (S2C 0x022 Make) since the trade last changed.</summary>
        public bool PartnerAccepted { get; internal set; }

        /// <summary>Our own name, drawn in our window's top band.</summary>
        public string TradeOwnName { get; init; } = string.Empty;

        /// <summary>The item in our slot, or the partner's, under the cursor (0 when empty or on OK / Cancel / the gil).</summary>
        public ushort TradeGridItem =>
            SelectedButtonId is >= 1 and <= StockUiTrade.SlotCount && TradeSlots[SelectedButtonId - 1] is { } s ? s.ItemId
            : StockUiTrade.IsPartnerSlot(SelectedButtonId, out int i) && PartnerSlots[i] is { } p ? p.ItemId
            : (ushort)0;
    }

    public sealed partial class StockUiMenuController
    {
        /// <summary>Resolves a trade target: the given server id, or the current target when null; null when there is none.</summary>
        public Func<uint?, StockUiTradeTarget?>? TradeTargetOf { get; set; }

        /// <summary>Sends C2S 0x032 (the target's server id and index).</summary>
        public Func<uint, ushort, Task>? TradeRequest { get; set; }

        /// <summary>Sends C2S 0x033 (Start accepts a request, Cancel leaves, Make is OK; the trade counter).</summary>
        public Func<TradeClientKind, ushort, Task>? TradeRespond { get; set; }

        /// <summary>Sends C2S 0x034 (count, item id, inventory slot, trade slot; count 0 empties the slot).</summary>
        public Func<uint, ushort, byte, byte, Task>? TradeOffer { get; set; }

        /// <summary>Sends C2S 0x036 to an NPC (server id, index, the inventory slots and counts, the gil first).</summary>
        public Func<uint, ushort, (byte Slot, uint Count)[], Task>? NpcTrade { get; set; }

        /// <summary>The character's own name (shown on our side of the trade window).</summary>
        public Func<string>? TradeOwnName { get; set; }

        /// <summary>A system message of the client's table (ROM/27/76) with a player's name as string 0; null when not loaded.</summary>
        public Func<int, string, string?>? SystemMessageText { get; set; }

        /// <summary>The distance in yalms from the character to an entity (server id); null when either is not known.</summary>
        public Func<uint, float?>? TradeDistanceTo { get; set; }

        private uint _tradeRequestedTo;
        private DateTime _tradeRequestedAt;
        private StockUiOpenMenu? _rangeCancelled;

        /// <summary>The clock outgoing requests time out on (tests replace it).</summary>
        public Func<DateTime> TradeClock { get; set; } = () => DateTime.UtcNow;

        /// <summary>The open trade window, if any.</summary>
        public StockUiOpenMenu? TradeWindow => Array.Find(_open, m => m.IsTradeWindow);

        private string TradeText(int message, string name = "") => SystemMessageText?.Invoke(message, name) ?? StockUiTrade.FallbackText(message, name);

        private bool HasPendingRequest(out uint serverId)
        {
            serverId = _tradeRequestedTo;
            return serverId != 0 && TradeClock() - _tradeRequestedAt < StockUiTrade.RequestTimeout;
        }

        /// <summary>
        /// The main menu's Trade ("Trade items or money with current target.") and the command menu's: a player is asked to
        /// trade (or, when they asked first, accepted), an NPC opens the hand-over window; anything else is refused.
        /// </summary>
        private void RunTradeCommand(StockUiOpenMenu from)
        {
            // A request waiting for us: Trade opens it whatever is targeted (round 1, 2026-10-31).
            if (TradeWindow == null && Inventory is { TradeRequesterServerId: > 0 } inventory)
            {
                uint requester = inventory.TradeRequesterServerId;
                var asker = TradeTargetOf?.Invoke(requester) ?? new StockUiTradeTarget(requester, inventory.TradeRequesterIndex, string.Empty, StockUiTradeTargetKind.Player);
                _ = RequestTradeAsync(asker with { ServerId = requester });
                return;
            }
            uint? serverId = from.IsCommandMenu && CommandMenuTarget is { } context ? context.TargetServerId : null;
            var target = TradeTargetOf?.Invoke(serverId);
            switch (target?.Kind ?? StockUiTradeTargetKind.None)
            {
                case StockUiTradeTargetKind.None:
                    NoticePosted?.Invoke(TradeText(StockUiTrade.SelectTarget));
                    return;
                case StockUiTradeTargetKind.Npc:
                    if (from.IsCommandMenu) CloseAll();
                    OpenNpcTrade(from.IsCommandMenu ? null : from, target!.Value);
                    return;
                case StockUiTradeTargetKind.Player:
                    _ = RequestTradeAsync(target!.Value);
                    return;
                default:
                    ErrorPosted?.Invoke(TradeText(StockUiTrade.CannotTradeTarget));
                    return;
            }
        }

        /// <summary>
        /// Asks a player to trade (C2S 0x032), or accepts their request (C2S 0x033 Start) when they asked first. A request
        /// to someone else still pending asks "Cancel trade transaction?" and is withdrawn (C2S 0x033 Cancel) first, as
        /// LandSandBoat expects of the client; asking the same player again posts "You have already sent a trade request".
        /// </summary>
        public async Task RequestTradeAsync(StockUiTradeTarget target)
        {
            if (TradeWindow != null) return;
            var inventory = Inventory;
            if (inventory != null && inventory.TradeRequesterServerId == target.ServerId)
            {
                CloseAll();
                GordianLog.Info("TRADE", $"Accepting the trade request of {target.Name} ({target.ServerId}).");
                await SendTradeAsync(() => TradeRespond?.Invoke(TradeClientKind.Start, 0)).ConfigureAwait(false);
                return;
            }
            bool pending = HasPendingRequest(out uint pendingTo);
            if (pending && pendingTo == target.ServerId)
            {
                ErrorPosted?.Invoke(TradeText(StockUiTrade.AlreadyRequested, target.Name));
                return;
            }
            if (pending || inventory?.TradeRequesterServerId is > 0)
            {
                if (!await PromptYesNoAsync(StockUiTrade.CancelQuestion, defaultYes: false).ConfigureAwait(false)) return;
                _tradeRequestedTo = 0;
                inventory?.ClearTradeRequest();
                GordianLog.Info("TRADE", "Withdrawing the pending trade request (C2S 0x033 Cancel).");
                await SendTradeAsync(() => TradeRespond?.Invoke(TradeClientKind.Cancel, 0)).ConfigureAwait(false);
            }
            CloseAll();
            _tradeRequestedTo = target.ServerId;
            _tradeRequestedAt = TradeClock();
            GordianLog.Info("TRADE", $"Asking {target.Name} ({target.ServerId}, index {target.Index}) to trade.");
            await SendTradeAsync(() => TradeRequest?.Invoke(target.ServerId, target.Index)).ConfigureAwait(false);
        }

        /// <summary>S2C 0x021: "{name} wishes to trade with you."; Trade on that player accepts.</summary>
        public void OnTradeRequested(uint serverId, ushort index)
        {
            string name = TradeTargetOf?.Invoke(serverId)?.Name ?? string.Empty;
            NoticePosted?.Invoke(TradeText(StockUiTrade.WishesToTrade, name));
        }

        /// <summary>
        /// S2C 0x022: Start opens the trade window as the root (closing other menus, as the shop does); Make / MakeCancel
        /// light or clear the partner's OK; Cancel, End and the errors close it with their line.
        /// </summary>
        public void OnTradeResult(TradeResultKind kind, uint serverId, ushort index)
        {
            if (kind == TradeResultKind.Start)
            {
                _tradeRequestedTo = 0;
                var partner = TradeTargetOf?.Invoke(serverId) ?? new StockUiTradeTarget(serverId, index, string.Empty, StockUiTradeTargetKind.Player);
                CloseAll();
                OpenTradeWindow(null, partner with { ServerId = serverId, Index = index }, npc: false);
                return;
            }
            if (!StockUiTrade.Closes(kind))
            {
                OnTradeChanged();
                return;
            }
            if (serverId == _tradeRequestedTo) _tradeRequestedTo = 0;
            var window = TradeWindow;
            string name = window?.TradePartner.Name is { Length: > 0 } open ? open : TradeTargetOf?.Invoke(serverId)?.Name ?? string.Empty;
            if (window != null && !window.IsNpcTrade) CloseMenu(window);
            int message = StockUiTrade.ResultMessage(kind);
            // The partner's client cancelled for the distance before ours saw it: the same line as ours (round 3, B3).
            if (kind == TradeResultKind.Cancel && OutOfTradeRange(serverId))
            {
                if (window == null) return; // ours already cancelled for it and printed the line
                message = StockUiTrade.OutOfRange;
            }
            if (kind == TradeResultKind.End) NoticePosted?.Invoke(TradeText(message, name));
            else if (message >= 0) ErrorPosted?.Invoke(TradeText(message, name));
        }

        /// <summary>The offers changed (S2C 0x023 / 0x025 / 0x022): the open player trade window takes them.</summary>
        public void OnTradeChanged()
        {
            var window = TradeWindow;
            if (window == null || window.IsNpcTrade) return;
            lock (_sync) RefreshTrade(window);
            Changed?.Invoke();
        }

        /// <summary>
        /// The character or the partner moved: past <see cref="StockUiTrade.Range"/> the player trade cancels with "Target out
        /// of range." and C2S 0x033 Cancel, no prompt. The server never cancels for the distance (it only refuses to start or
        /// commit), so the client does (retail, round 3, 2026-11-01). PROVISIONAL: that retail sends 0x033 Cancel here.
        /// </summary>
        public void CheckTradeRange()
        {
            if (TradeWindow is not { IsNpcTrade: false } window || !OutOfTradeRange(window.TradePartner.ServerId)) return;
            // Our movement and the partner's updates arrive on different threads: one cancel per window.
            if (System.Threading.Interlocked.Exchange(ref _rangeCancelled, window) == window) return;
            CloseMenu(window);
            GordianLog.Info("TRADE", $"{window.TradePartner.Name} is out of range; cancelling the trade (C2S 0x033 Cancel).");
            ErrorPosted?.Invoke(TradeText(StockUiTrade.OutOfRange));
            _ = SendTradeAsync(() => TradeRespond?.Invoke(TradeClientKind.Cancel, 0));
        }

        private bool OutOfTradeRange(uint serverId) => TradeDistanceTo?.Invoke(serverId) is { } distance && distance > StockUiTrade.Range;

        /// <summary>A zone change: the trade window closes (the server ends the trade) and requests are forgotten.</summary>
        public void CloseTrade()
        {
            _tradeRequestedTo = 0;
            if (TradeWindow is { } window) CloseMenu(window);
        }

        /// <summary>Opens a trade window over <paramref name="parent"/> (null = as the root), the cursor on the first slot.</summary>
        private StockUiOpenMenu? OpenTradeWindow(StockUiOpenMenu? parent, StockUiTradeTarget partner, bool npc)
        {
            var library = _library;
            if (library == null || !library.TryGetMenu(npc ? StockUiTrade.NpcMenu : StockUiTrade.Menu, out var definition)) return null;
            var window = new StockUiOpenMenu(npc ? definition : StockUiTrade.WithPartnerSlots(definition), parent, Array.Empty<string>(), null)
            {
                IsTradeWindow = true,
                IsNpcTrade = npc,
                TradePartner = partner,
                TradeOwnName = TradeOwnName?.Invoke() ?? string.Empty,
                Inventory = Inventory,
                ItemLookup = ItemLookup,
                ItemOwner = this,
            };
            window.SelectedButtonId = 1;
            lock (_sync)
            {
                RefreshTrade(window);
                PushOpen(window);
            }
            GordianLog.Info("TRADE", $"Trade window opened with {partner.Name} ({partner.ServerId}){(npc ? " (NPC)" : string.Empty)}.");
            Changed?.Invoke();
            return window;
        }

        /// <summary>The main menu's Trade on an NPC: the hand-over window (<c>handover</c>), sent as C2S 0x036 on OK.</summary>
        public StockUiOpenMenu? OpenNpcTrade(StockUiOpenMenu? parent, StockUiTradeTarget npc) => OpenTradeWindow(parent, npc, npc: true);

        /// <summary>A trade window's offers and the inventory beside it (under the lock).</summary>
        private void RefreshTrade(StockUiOpenMenu window)
        {
            if (!window.IsNpcTrade && Inventory is { } inventory)
            {
                var (mine, partner) = inventory.SnapshotTrade();
                var (slots, gil) = StockUiTrade.FromOffer(mine, v => v);
                var (partnerSlots, partnerGil) = StockUiTrade.FromOffer(partner, v => (v.ItemId, v.Count, (byte)0));
                // Any change to either offer withdraws both OKs (LandSandBoat's setSlot); the partner's comes from Core.
                if (!SameSlots(slots, window.TradeSlots) || gil != window.TradeGil || !SameSlots(partnerSlots, window.PartnerSlots) || partnerGil != window.PartnerGil)
                {
                    window.TradeAccepted = false;
                }
                window.TradeSlots = slots;
                window.TradeGil = gil;
                window.PartnerSlots = partnerSlots;
                window.PartnerGil = partnerGil;
                window.PartnerAccepted = inventory.TradePartnerAccepted;
            }
            window.CompanionRows = StockUiTrade.OfferRows(OrderedRows(ContainerId.Inventory), window.TradeSlots, ItemLookup, window.IsNpcTrade,
                window.TradePartner.Name, MenuMessageText);
        }

        private static bool SameSlots(StockUiTradeSlot?[] a, StockUiTradeSlot?[] b)
        {
            for (int i = 0; i < a.Length && i < b.Length; i++)
            {
                if (a[i] != b[i]) return false;
            }
            return a.Length == b.Length;
        }

        /// <summary>Confirm in a trade window; false when the top menu is not one.</summary>
        private bool TryActivateTrade(StockUiOpenMenu top, UiMenuButton button)
        {
            if (!top.IsTradeWindow) return false;
            int id = button.ButtonId;
            if (id == StockUiTrade.OkButton) AcceptTrade(top);
            else if (id == StockUiTrade.CancelButton) _ = CancelTradeAsync(top);
            else if (id == StockUiTrade.GilButton) OpenPriceSet(top, default, StockUiAuctionPriceFor.TradeGil, top.TradeGil);
            else if (id >= 1 && id <= StockUiTrade.SlotCount) ActivateTradeSlot(top, id - 1);
            return true;
        }

        /// <summary>
        /// Confirm on a slot: an empty one opens the item picker beside the window, a filled one is emptied (C2S 0x034 with
        /// count 0 to a player; PROVISIONAL, as the synthesis grid does it).
        /// </summary>
        private void ActivateTradeSlot(StockUiOpenMenu window, int index)
        {
            if (window.TradeSlots[index] is { } filled)
            {
                if (window.IsNpcTrade)
                {
                    lock (_sync)
                    {
                        window.TradeSlots[index] = null;
                        RefreshTrade(window);
                    }
                    Changed?.Invoke();
                }
                else
                {
                    _ = SendTradeAsync(() => TradeOffer?.Invoke(0, filled.ItemId, filled.InventorySlot, (byte)(index + 1)));
                }
                return;
            }
            var picker = new StockUiItemPicker(
                rows => StockUiTrade.OfferRows(rows, window.TradeSlots, ItemLookup, window.IsNpcTrade, window.TradePartner.Name, MenuMessageText),
                row => row.Count,
                (row, count) => PlaceTradeItem(window, index, row, count),
                StockUiTrade.Title);
            OpenItemPicker(window, StockUiTrade.ListX, picker);
        }

        /// <summary>
        /// An item picked for a slot: the picker closes and the cursor moves to the next slot. To a player it goes out as
        /// C2S 0x034 and shows once the server's S2C 0x025 has it; with an NPC it fills the slot at once.
        /// </summary>
        private void PlaceTradeItem(StockUiOpenMenu window, int index, StockUiItemRow row, uint count)
        {
            foreach (var menu in _open)
            {
                if (ReferenceEquals(menu.Parent, window))
                {
                    CloseMenu(menu);
                    break;
                }
            }
            lock (_sync)
            {
                if (window.IsNpcTrade)
                {
                    window.TradeSlots[index] = new StockUiTradeSlot(row.ItemId, count, row.Slot);
                    RefreshTrade(window);
                }
                window.SelectedButtonId = index + 2 <= StockUiTrade.SlotCount ? index + 2 : StockUiTrade.OkButton;
            }
            GordianLog.Debug("TRADE", $"Trade slot {index + 1}: {count} x {row.Name} (item {row.ItemId}, inventory slot {row.Slot}).");
            if (!window.IsNpcTrade) _ = SendTradeAsync(() => TradeOffer?.Invoke(count, row.ItemId, row.Slot, (byte)(index + 1)));
            Changed?.Invoke();
        }

        /// <summary>The gil amount chosen in the gil window: to a player as C2S 0x034 on trade slot 0, with an NPC kept for OK.</summary>
        private void SetTradeGil(StockUiOpenMenu? window, uint gil)
        {
            if (window is not { IsTradeWindow: true }) return;
            gil = Math.Min(gil, Gil);
            if (window.IsNpcTrade)
            {
                lock (_sync) window.TradeGil = gil;
                Changed?.Invoke();
                return;
            }
            _ = SendTradeAsync(() => TradeOffer?.Invoke(gil, StockUiTrade.GilItemId, 0, StockUiTrade.GilTradeIndex));
        }

        /// <summary>
        /// OK: with a player, C2S 0x033 Make with the trade counter (once until the trade changes); the trade completes when
        /// both have pressed it. With an NPC, C2S 0x036 with the offer, and every menu closes (main menu included).
        /// </summary>
        private void AcceptTrade(StockUiOpenMenu window)
        {
            if (window.IsNpcTrade)
            {
                var entries = StockUiTrade.NpcEntries(window.TradeSlots, window.TradeGil);
                if (entries.Length == 0) return;
                var npc = window.TradePartner;
                GordianLog.Info("TRADE", $"Trading {entries.Length} entries to {npc.Name} ({npc.ServerId}).");
                CloseAll(); // the main menu too, so the next Confirm goes to the NPC's event (round 3, B3)
                _ = SendTradeAsync(() => NpcTrade?.Invoke(npc.ServerId, npc.Index, entries));
                return;
            }
            if (window.TradeAccepted) return;
            ushort counter = Inventory?.TradeCounter ?? 0;
            lock (_sync) window.TradeAccepted = true;
            Changed?.Invoke();
            _ = SendTradeAsync(() => TradeRespond?.Invoke(TradeClientKind.Make, counter));
        }

        /// <summary>
        /// Cancel (the button, or the Cancel key on the window): with a player it asks "Cancel trade transaction?" and on Yes
        /// sends C2S 0x033 Cancel and closes; with an NPC it just closes.
        /// </summary>
        private async Task CancelTradeAsync(StockUiOpenMenu window)
        {
            if (window.IsNpcTrade)
            {
                CloseMenu(window);
                return;
            }
            if (!await PromptYesNoAsync(StockUiTrade.CancelQuestion, defaultYes: false).ConfigureAwait(false)) return;
            if (Array.IndexOf(_open, window) < 0) return;
            CloseMenu(window);
            GordianLog.Info("TRADE", $"Cancelling the trade with {window.TradePartner.Name} (C2S 0x033 Cancel).");
            await SendTradeAsync(() => TradeRespond?.Invoke(TradeClientKind.Cancel, 0)).ConfigureAwait(false);
        }

        /// <summary>Cancel with a trade window on top goes through <see cref="CancelTradeAsync"/>; false when the top is not one.</summary>
        private bool TryCancelTrade()
        {
            if (Top is not { IsTradeWindow: true } window) return false;
            _ = CancelTradeAsync(window);
            return true;
        }

        private async Task SendTradeAsync(Func<Task?> send)
        {
            try
            {
                var task = send();
                if (task == null)
                {
                    NoticePosted?.Invoke("Trade is not available in this session.");
                    return;
                }
                await task.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                GordianLog.Warning("TRADE", $"Trade packet failed: {ex.Message}");
                NoticePosted?.Invoke($"Trade failed: {ex.Message}");
            }
        }
    }
}
