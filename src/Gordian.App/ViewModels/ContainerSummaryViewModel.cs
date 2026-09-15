// src/Gordian.App/ViewModels/ContainerSummaryViewModel.cs
using System;
using Gordian.App.Common;
using Gordian.Core.Network.Packets;

namespace Gordian.App.ViewModels
{
    /// <summary>
    /// ViewModel representing a summary entry for an FFXI item container
    /// (e.g. Inventory, Mog Safe, Wardrobes 1-8). Tracks current capacity and used slots.
    /// </summary>
    public sealed class ContainerSummaryViewModel : ViewModelBase
    {
        private byte _maxSize;
        private ushort _usableSize;
        private int _itemCount;

        public ContainerId Id { get; }
        public string Name { get; }

        public byte MaxSize
        {
            get => _maxSize;
            set
            {
                if (SetProperty(ref _maxSize, value))
                {
                    OnPropertyChanged(nameof(CapacityText));
                    OnPropertyChanged(nameof(DisplayName));
                    OnPropertyChanged(nameof(IsFull));
                }
            }
        }

        public ushort UsableSize
        {
            get => _usableSize;
            set
            {
                if (SetProperty(ref _usableSize, value))
                {
                    OnPropertyChanged(nameof(CapacityText));
                    OnPropertyChanged(nameof(DisplayName));
                    OnPropertyChanged(nameof(IsFull));
                }
            }
        }

        public int ItemCount
        {
            get => _itemCount;
            set
            {
                if (SetProperty(ref _itemCount, value))
                {
                    OnPropertyChanged(nameof(CapacityText));
                    OnPropertyChanged(nameof(DisplayName));
                    OnPropertyChanged(nameof(IsFull));
                    OnPropertyChanged(nameof(HasItems));
                }
            }
        }

        public int EffectiveCapacity => UsableSize > 0 ? UsableSize : (MaxSize > 0 ? MaxSize : 0);

        public string CapacityText => $"{ItemCount}/{EffectiveCapacity}";

        public string DisplayName => $"{Name} ({CapacityText})";

        public bool IsFull => EffectiveCapacity > 0 && ItemCount >= EffectiveCapacity;

        public bool HasItems => ItemCount > 0;

        public ContainerSummaryViewModel(ContainerId id)
        {
            Id = id;
            Name = GetFriendlyName(id);
        }

        public static string GetFriendlyName(ContainerId id) => id switch
        {
            ContainerId.Inventory  => "Inventory",
            ContainerId.MogSafe    => "Mog Safe",
            ContainerId.Storage    => "Storage",
            ContainerId.TempItems  => "Temp Items",
            ContainerId.MogLocker  => "Mog Locker",
            ContainerId.MogSatchel => "Mog Satchel",
            ContainerId.MogSack    => "Mog Sack",
            ContainerId.MogCase    => "Mog Case",
            ContainerId.Wardrobe   => "Wardrobe 1",
            ContainerId.MogSafe2   => "Mog Safe 2",
            ContainerId.Wardrobe2  => "Wardrobe 2",
            ContainerId.Wardrobe3  => "Wardrobe 3",
            ContainerId.Wardrobe4  => "Wardrobe 4",
            ContainerId.Wardrobe5  => "Wardrobe 5",
            ContainerId.Wardrobe6  => "Wardrobe 6",
            ContainerId.Wardrobe7  => "Wardrobe 7",
            ContainerId.Wardrobe8  => "Wardrobe 8",
            ContainerId.RecycleBin => "Recycle Bin",
            _                      => id.ToString()
        };
    }
}
