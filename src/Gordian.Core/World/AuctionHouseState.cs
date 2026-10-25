// src/Gordian.Core/World/AuctionHouseState.cs
using System;
using Gordian.Core.Network.Packets;

namespace Gordian.Core.World
{
    /// <summary>
    /// One of the character's Auction House listings (a sale slot of the Sales Status window): the item, whether it is a
    /// stack, the asking price and the parcel's state.
    /// </summary>
    public sealed record AuctionSale(sbyte Slot, ushort ItemId, bool Stack, uint Price, AuctionParcelStat Stat, string SellerName);

    /// <summary>
    /// The listing fee the server quoted for a sale (the AskCommit reply): the item, its inventory slot, whether the stack is
    /// sold, the asking price the client sent and the fee.
    /// </summary>
    public sealed record AuctionListingQuote(ushort ItemId, byte InventorySlot, bool Stack, uint Fee);

    /// <summary>
    /// The Auction House as the server last described it (S2C 0x04C): whether the window was opened, the character's seven
    /// sale slots and the last listing fee quoted. The stock Auction House window reads it; <see cref="InventoryState"/> feeds
    /// it every 0x04C answer.
    /// <para>
    /// How LandSandBoat answers (<c>utils/auctionutils.cpp</c>, <c>packets/s2c/0x04c_auc.cpp</c>): <c>sendMenu(AUCTION)</c> sends
    /// command 0x02 (MOPEN) with result 1; C2S WorkCheck is answered with one sale-slot reply per listing, C2S Info with an Info
    /// reply (result 1, or 0xF6 within 5 s of the last) and then the slot replies. A slot reply is command 0x0C (LotCancel) or
    /// 0x0D (LotCheck) with the slot in <c>AucWorkIndex</c> and the listing in the parcel (<c>ItemQuantity</c> 0 for a stack,
    /// 1 for a single item); a successful LotCancel carries no parcel, which empties the slot. AskCommit is answered with the
    /// fee in <c>Param.AskCommit.Commission</c>, LotIn with result 1 (placed) or a failure code, Bid with result 1 (bought) or
    /// 0xC5 / 0xE5. Commands and result codes referenced from XiPackets (https://github.com/atom0s/XiPackets),
    /// <c>world/server/0x004C</c>.
    /// </para>
    /// </summary>
    public sealed class AuctionHouseState
    {
        /// <summary>The number of sale slots (the most items a character can have on auction at once).</summary>
        public const int SlotCount = 7;

        private readonly object _lock = new();
        private readonly AuctionSale?[] _slots = new AuctionSale?[SlotCount];

        /// <summary>Raised when the server opened the Auction House window (MOPEN, result 1).</summary>
        public event Action? Opened;

        /// <summary>Raised when a sale slot changed or the list was reset.</summary>
        public event Action? SalesChanged;

        /// <summary>Raised for every answer, after the state took it in (the window prints the messages and moves on).</summary>
        public event Action<AuctionResponse>? ResponseReceived;

        /// <summary>Whether the server has opened the Auction House since the last <see cref="Close"/>.</summary>
        public bool IsOpen { get; private set; }

        /// <summary>The listing fee of the last successful AskCommit, or null.</summary>
        public AuctionListingQuote? LastQuote { get; private set; }

        /// <summary>The sale slots, by work index (null = empty), copied under the lock.</summary>
        public AuctionSale?[] SnapshotSales()
        {
            lock (_lock) return (AuctionSale?[])_slots.Clone();
        }

        /// <summary>The first empty sale slot, or -1 when all seven are used.</summary>
        public sbyte NextFreeSlot
        {
            get
            {
                lock (_lock)
                {
                    for (int i = 0; i < SlotCount; i++) if (_slots[i] == null) return (sbyte)i;
                    return -1;
                }
            }
        }

        /// <summary>Takes in one S2C 0x04C answer.</summary>
        public void Apply(AuctionResponse response)
        {
            bool opened = false, salesChanged = false;
            lock (_lock)
            {
                switch (response.Command)
                {
                    case AuctionCommand.Open:
                        if (response.ResultCode == AuctionResultCode.Success)
                        {
                            IsOpen = true;
                            opened = true;
                        }
                        break;
                    case AuctionCommand.Info when response.ResultCode == AuctionResultCode.Success && response.WorkIndex < 0:
                        // The slot replies follow and describe every listing again.
                        Array.Clear(_slots);
                        salesChanged = true;
                        break;
                    case AuctionCommand.LotCancel or AuctionCommand.LotCheck when response.WorkIndex is >= 0 and < SlotCount:
                        _slots[response.WorkIndex] = response.ItemId != 0 && response.Stat != AuctionParcelStat.None
                            ? new AuctionSale(response.WorkIndex, response.ItemId, response.Count == 0, response.Price, response.Stat, response.SellerName)
                            : null;
                        salesChanged = true;
                        break;
                    case AuctionCommand.AskCommit:
                        LastQuote = response.ResultCode == AuctionResultCode.Success
                            ? new AuctionListingQuote(response.ItemId, (byte)response.ParamWorkIndex, response.ParamStacks == 0, response.Price)
                            : null;
                        break;
                }
            }
            if (opened) Opened?.Invoke();
            if (salesChanged) SalesChanged?.Invoke();
            ResponseReceived?.Invoke(response);
        }

        /// <summary>The window closed or the zone changed: the next MOPEN opens it again. The sale slots are kept.</summary>
        public void Close()
        {
            lock (_lock)
            {
                IsOpen = false;
                LastQuote = null;
            }
        }
    }
}
