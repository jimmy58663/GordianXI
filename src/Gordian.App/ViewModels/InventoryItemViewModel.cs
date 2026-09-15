// src/Gordian.App/ViewModels/InventoryItemViewModel.cs
using System;
using Gordian.App.Common;
using Gordian.Core.Network.Packets;
using Gordian.Core.World;

using Gordian.Core.Resources;

namespace Gordian.App.ViewModels
{
    /// <summary>
    /// ViewModel representing a single item slot within an FFXI container.
    /// Provides reactive property change notifications and clean formatting for
    /// Item IDs, counts, lock flags, bazaar pricing, and 24-byte augment ExtData.
    /// </summary>
    public sealed class InventoryItemViewModel : ViewModelBase
    {
        private uint _count;
        private uint _price;
        private ItemLockFlag _lockFlag;
        private byte[]? _extData;
        private bool _isEquipped;
        private string? _equippedSlotName;

        public ContainerId Container { get; }
        public byte Slot { get; }
        public ushort ItemId { get; }

        public uint Count
        {
            get => _count;
            set
            {
                if (SetProperty(ref _count, value))
                {
                    OnPropertyChanged(nameof(CountDisplay));
                }
            }
        }

        public uint Price
        {
            get => _price;
            set
            {
                if (SetProperty(ref _price, value))
                {
                    OnPropertyChanged(nameof(PriceDisplay));
                    OnPropertyChanged(nameof(HasPrice));
                }
            }
        }

        public ItemLockFlag LockFlag
        {
            get => _lockFlag;
            set
            {
                if (SetProperty(ref _lockFlag, value))
                {
                    OnPropertyChanged(nameof(LockDisplay));
                    OnPropertyChanged(nameof(HasLock));
                }
            }
        }

        public byte[]? ExtData
        {
            get => _extData;
            set
            {
                if (SetProperty(ref _extData, value))
                {
                    OnPropertyChanged(nameof(ExtDataHex));
                    OnPropertyChanged(nameof(HasExtData));
                }
            }
        }

        public bool IsEquipped
        {
            get => _isEquipped;
            set
            {
                if (SetProperty(ref _isEquipped, value))
                {
                    OnPropertyChanged(nameof(EquippedDisplay));
                }
            }
        }

        public string? EquippedSlotName
        {
            get => _equippedSlotName;
            set
            {
                if (SetProperty(ref _equippedSlotName, value))
                {
                    OnPropertyChanged(nameof(EquippedDisplay));
                }
            }
        }

        public string SlotDisplay => $"#{Slot:D2}";

        public string ItemDisplay => ItemNameResolver.Resolve(ItemId);

        public string ItemIdHex => $"0x{ItemId:X4}";

        public string CountDisplay => ItemId == 65535 ? $"{Count:N0}" : (Count > 1 ? $"x{Count:N0}" : "1");

        public string LockDisplay => LockFlag != ItemLockFlag.Normal ? LockFlag.ToString() : string.Empty;

        public bool HasLock => LockFlag != ItemLockFlag.Normal;

        public string PriceDisplay => Price > 0 ? $"{Price:N0} gil" : string.Empty;

        public bool HasPrice => Price > 0;

        public string ExtDataHex => ExtData != null && ExtData.Length > 0 ? Convert.ToHexString(ExtData) : "(None)";

        public bool HasExtData => ExtData != null && ExtData.Length > 0;

        public string EquippedDisplay => IsEquipped ? $"[{EquippedSlotName}]" : string.Empty;

        public InventoryItemViewModel(
            ContainerId container,
            byte slot,
            ushort itemId,
            uint count,
            ItemLockFlag lockFlag,
            uint price = 0,
            byte[]? extData = null)
        {
            Container = container;
            Slot = slot;
            ItemId = itemId;
            _count = count;
            _lockFlag = lockFlag;
            _price = price;
            _extData = extData;
        }

        public static InventoryItemViewModel FromCore(InventoryItem item)
        {
            ArgumentNullException.ThrowIfNull(item);
            return new InventoryItemViewModel(
                item.Container,
                item.Slot,
                item.ItemId,
                item.Count,
                item.LockFlag,
                item.Price,
                item.ExtData
            );
        }
    }
}
