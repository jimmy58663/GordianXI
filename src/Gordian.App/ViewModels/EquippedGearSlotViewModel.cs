// src/Gordian.App/ViewModels/EquippedGearSlotViewModel.cs
using System;
using Gordian.App.Common;
using Gordian.Core.Network.Packets;

namespace Gordian.App.ViewModels
{
    /// <summary>
    /// ViewModel representing one of the 18 active equipment slots (Main, Sub, Head, Body, etc.).
    /// Tracks which container and slot the gear resides in, and provides formatted displays.
    /// </summary>
    public sealed class EquippedGearSlotViewModel : ViewModelBase
    {
        private ContainerId _container;
        private byte _containerSlot = 0xFF;
        private ushort? _itemId;

        public EquipSlotId SlotId { get; }
        public string SlotName => SlotId.ToString();

        public ContainerId Container
        {
            get => _container;
            set
            {
                if (SetProperty(ref _container, value))
                {
                    OnPropertyChanged(nameof(LocationDisplay));
                }
            }
        }

        public byte ContainerSlot
        {
            get => _containerSlot;
            set
            {
                if (SetProperty(ref _containerSlot, value))
                {
                    OnPropertyChanged(nameof(IsEmpty));
                    OnPropertyChanged(nameof(LocationDisplay));
                    OnPropertyChanged(nameof(ItemDisplay));
                }
            }
        }

        public ushort? ItemId
        {
            get => _itemId;
            set
            {
                if (SetProperty(ref _itemId, value))
                {
                    OnPropertyChanged(nameof(ItemDisplay));
                }
            }
        }

        public bool IsEmpty => ContainerSlot == 0xFF;

        public string ItemDisplay
        {
            get
            {
                if (IsEmpty) return "(Empty)";
                if (ItemId.HasValue)
                {
                    return Gordian.Core.Resources.ItemNameResolver.Resolve(ItemId.Value);
                }
                return $"Item in Slot {ContainerSlot}";
            }
        }

        public string LocationDisplay => IsEmpty ? "--" : $"{Container} #{ContainerSlot:D2}";

        public EquippedGearSlotViewModel(EquipSlotId slotId)
        {
            SlotId = slotId;
            _container = ContainerId.Inventory;
            _containerSlot = 0xFF;
            _itemId = null;
        }

        public void SetEquipped(ContainerId container, byte slot, ushort? itemId)
        {
            Container = container;
            ContainerSlot = slot;
            ItemId = itemId;
        }

        public void Clear()
        {
            ContainerSlot = 0xFF;
            ItemId = null;
        }
    }
}
