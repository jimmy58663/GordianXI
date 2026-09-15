// src/Gordian.App/ViewModels/InventoryViewModel.cs
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using Gordian.App.Common;
using Gordian.Core.Diagnostics;
using Gordian.Core.Network;
using Gordian.Core.Network.Packets;
using Gordian.Core.World;

namespace Gordian.App.ViewModels
{
    /// <summary>
    /// ViewModel driving the primary Inventory &amp; Equipment inspection page and tab.
    /// Manages multi-container item tracking, real-time container capacity, active equipment loadout,
    /// currencies/points display, and outbound inventory action dispatching.
    /// </summary>
    public sealed class InventoryViewModel : ViewModelBase, IDisposable
    {
        public static Action<Action>? UiDispatcher { get; set; }

        private readonly SessionRegistry _sessionRegistry;
        private CharacterSession? _selectedSession;
        private CharacterSession? _hookedSession;

        private ContainerSummaryViewModel? _selectedContainer;
        private InventoryItemViewModel? _selectedItem;
        private string _itemSearchFilter = string.Empty;
        private string _statusText = "Ready";

        public ObservableCollection<CharacterSession> ActiveSessions { get; } = new();
        public ObservableCollection<ContainerSummaryViewModel> Containers { get; } = new();
        public ObservableCollection<InventoryItemViewModel> ContainerItems { get; } = new();
        public ObservableCollection<InventoryItemViewModel> FilteredItems { get; } = new();
        public ObservableCollection<EquippedGearSlotViewModel> EquippedSlots { get; } = new();

        public CharacterSession? SelectedSession
        {
            get => _selectedSession;
            set
            {
                if (SetProperty(ref _selectedSession, value))
                {
                    OnSelectedSessionChanged(value);
                    OnPropertyChanged(nameof(HasActiveSession));
                    OnPropertyChanged(nameof(SessionHeaderTitle));
                }
            }
        }

        public bool HasActiveSession => SelectedSession != null;

        public string SessionHeaderTitle => SelectedSession != null
            ? $"Active Session: {SelectedSession.CharacterName} ({SelectedSession.State})"
            : "No Active Session";

        public ContainerSummaryViewModel? SelectedContainer
        {
            get => _selectedContainer;
            set
            {
                if (SetProperty(ref _selectedContainer, value))
                {
                    LoadItemsForSelectedContainer();
                    RaiseCommandsCanExecute();
                }
            }
        }

        public InventoryItemViewModel? SelectedItem
        {
            get => _selectedItem;
            set => SetProperty(ref _selectedItem, value);
        }

        public string ItemSearchFilter
        {
            get => _itemSearchFilter;
            set
            {
                if (SetProperty(ref _itemSearchFilter, value))
                {
                    ApplyItemFilter();
                }
            }
        }

        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        #region Currencies Display Properties

        public int SparksOfEminence => SelectedSession?.Inventory.SparksOfEminence ?? 0;
        public int UnityAccolades => SelectedSession?.Inventory.UnityAccolades ?? 0;
        public int ConquestSandoria => SelectedSession?.Inventory.ConquestSandoria ?? 0;
        public int ConquestBastok => SelectedSession?.Inventory.ConquestBastok ?? 0;
        public int ConquestWindurst => SelectedSession?.Inventory.ConquestWindurst ?? 0;
        public int ImperialStanding => SelectedSession?.Inventory.ImperialStanding ?? 0;
        public int AlliedNotes => SelectedSession?.Inventory.AlliedNotes ?? 0;
        public ushort LoginPoints => SelectedSession?.Inventory.LoginPoints ?? 0;
        public int Cruor => SelectedSession?.Inventory.Cruor ?? 0;
        public ushort Deeds => SelectedSession?.Inventory.Deeds ?? 0;
        public ushort AncientBeastcoins => SelectedSession?.Inventory.AncientBeastcoins ?? 0;
        public ushort BeastmansSeals => SelectedSession?.Inventory.BeastmansSeals ?? 0;
        public ushort KindredsSeals => SelectedSession?.Inventory.KindredsSeals ?? 0;
        public int Bayld => SelectedSession?.Inventory.Bayld ?? 0;
        public ushort KineticUnits => SelectedSession?.Inventory.KineticUnits ?? 0;
        public byte CoalitionImprimaturs => SelectedSession?.Inventory.CoalitionImprimaturs ?? 0;
        public int MweyaPlasm => SelectedSession?.Inventory.MweyaPlasm ?? 0;
        public ushort EschaBeads => SelectedSession?.Inventory.EschaBeads ?? 0;
        public int EschaSilt => SelectedSession?.Inventory.EschaSilt ?? 0;
        public int Hallmarks => SelectedSession?.Inventory.Hallmarks ?? 0;
        public int BadgesOfGallantry => SelectedSession?.Inventory.BadgesOfGallantry ?? 0;
        public int DomainPoints => SelectedSession?.Inventory.DomainPoints ?? 0;
        public int MogSegments => SelectedSession?.Inventory.MogSegments ?? 0;
        public int Gallimaufry => SelectedSession?.Inventory.Gallimaufry ?? 0;

        #endregion

        #region Commands

        public ICommand RequestCurrencies1Command { get; }
        public ICommand RequestCurrencies2Command { get; }
        public ICommand SortContainerCommand { get; }
        public ICommand RefreshCommand { get; }

        #endregion

        public InventoryViewModel(SessionRegistry? registry = null)
        {
            _sessionRegistry = registry ?? SessionRegistry.Default;

            // Initialize clean-room DAT item name resolver from detected game folder
            Gordian.Core.Resources.ItemNameResolver.Initialize(Services.GameDirectoryDetector.DetectGameDirectory());

            // Initialize all 18 container summaries
            for (int i = 0; i < (int)ContainerId.Count; i++)
            {
                Containers.Add(new ContainerSummaryViewModel((ContainerId)i));
            }
            _selectedContainer = Containers.FirstOrDefault();

            // Initialize all 18 equipment slot view models
            for (int i = 0; i < (int)EquipSlotId.Count; i++)
            {
                EquippedSlots.Add(new EquippedGearSlotViewModel((EquipSlotId)i));
            }

            RequestCurrencies1Command = new RelayCommand(async () => await ExecuteRequestCurrencies1Async(), () => HasActiveSession);
            RequestCurrencies2Command = new RelayCommand(async () => await ExecuteRequestCurrencies2Async(), () => HasActiveSession);
            SortContainerCommand = new RelayCommand(async () => await ExecuteSortContainerAsync(), () => HasActiveSession && SelectedContainer != null);
            RefreshCommand = new RelayCommand(ExecuteRefresh, () => HasActiveSession);

            _sessionRegistry.SessionRegistered += OnSessionRegistered;
            _sessionRegistry.SessionUnregistered += OnSessionUnregistered;

            foreach (var s in _sessionRegistry.ActiveSessions)
            {
                ActiveSessions.Add(s);
            }

            if (ActiveSessions.Count > 0)
            {
                SelectedSession = ActiveSessions[0];
            }

            RaiseCommandsCanExecute();
        }

        public void RaiseCommandsCanExecute()
        {
            (RequestCurrencies1Command as RelayCommand)?.RaiseCanExecuteChanged();
            (RequestCurrencies2Command as RelayCommand)?.RaiseCanExecuteChanged();
            (SortContainerCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (RefreshCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        private void OnSessionRegistered(object? sender, CharacterSession session)
        {
            DispatchToUi(() =>
            {
                if (!ActiveSessions.Contains(session))
                {
                    ActiveSessions.Add(session);
                }
                if (SelectedSession == null)
                {
                    SelectedSession = session;
                }
                RaiseCommandsCanExecute();
            });
        }

        private void OnSessionUnregistered(object? sender, CharacterSession session)
        {
            DispatchToUi(() =>
            {
                ActiveSessions.Remove(session);
                if (SelectedSession == session)
                {
                    SelectedSession = ActiveSessions.FirstOrDefault();
                }
                RaiseCommandsCanExecute();
            });
        }

        private void OnSelectedSessionChanged(CharacterSession? session)
        {
            if (_hookedSession != null)
            {
                _hookedSession.Inventory.ItemChanged -= OnCoreItemChanged;
                _hookedSession.Inventory.ContainerSizesChanged -= OnCoreContainerSizesChanged;
                _hookedSession.Inventory.EquipChanged -= OnCoreEquipChanged;
                _hookedSession.Inventory.CurrenciesChanged -= OnCoreCurrenciesChanged;
                _hookedSession = null;
            }

            if (session != null)
            {
                _hookedSession = session;
                session.Inventory.ItemChanged += OnCoreItemChanged;
                session.Inventory.ContainerSizesChanged += OnCoreContainerSizesChanged;
                session.Inventory.EquipChanged += OnCoreEquipChanged;
                session.Inventory.CurrenciesChanged += OnCoreCurrenciesChanged;

                RefreshAllContainersAndEquip();
            }
            else
            {
                ClearAllDisplays();
            }

            NotifyCurrenciesChanged();
            RaiseCommandsCanExecute();
        }

        public void RefreshAllContainersAndEquip()
        {
            if (SelectedSession == null) return;

            var inv = SelectedSession.Inventory;

            // 1. Refresh container sizes and counts
            foreach (var contSummary in Containers)
            {
                var coreCont = inv.GetContainer(contSummary.Id);
                contSummary.MaxSize = coreCont.MaxSize;
                contSummary.UsableSize = coreCont.UsableSize;
                contSummary.ItemCount = coreCont.Items.Count;
            }

            // 2. Refresh equipped slots
            for (int i = 0; i < (int)EquipSlotId.Count; i++)
            {
                var slotId = (EquipSlotId)i;
                var eq = inv.GetEquipped(slotId);
                var eqVm = EquippedSlots[i];

                if (eq.Slot == 0xFF)
                {
                    eqVm.Clear();
                }
                else
                {
                    ushort? itemId = null;
                    if (inv.GetContainer(eq.Container).TryGetItem(eq.Slot, out var coreItem))
                    {
                        itemId = coreItem.ItemId;
                    }
                    eqVm.SetEquipped(eq.Container, eq.Slot, itemId);
                }
            }

            // 3. Reload current selected container items
            LoadItemsForSelectedContainer();
            NotifyCurrenciesChanged();
            StatusText = $"Loaded inventory for {SelectedSession.CharacterName}.";
        }

        private void ClearAllDisplays()
        {
            foreach (var contSummary in Containers)
            {
                contSummary.MaxSize = 0;
                contSummary.UsableSize = 0;
                contSummary.ItemCount = 0;
            }

            foreach (var eqVm in EquippedSlots)
            {
                eqVm.Clear();
            }

            ContainerItems.Clear();
            FilteredItems.Clear();
            SelectedItem = null;
            StatusText = "No active session.";
        }

        private void LoadItemsForSelectedContainer()
        {
            ContainerItems.Clear();
            SelectedItem = null;

            if (SelectedSession == null || SelectedContainer == null)
            {
                FilteredItems.Clear();
                return;
            }

            var coreCont = SelectedSession.Inventory.GetContainer(SelectedContainer.Id);
            var sortedSlots = coreCont.Items.Keys.OrderBy(k => k);

            foreach (var slot in sortedSlots)
            {
                var coreItem = coreCont.Items[slot];
                var vm = InventoryItemViewModel.FromCore(coreItem);
                CheckAndSetEquippedStatus(vm);
                ContainerItems.Add(vm);
            }

            SelectedContainer.ItemCount = ContainerItems.Count;
            ApplyItemFilter();
        }

        private void CheckAndSetEquippedStatus(InventoryItemViewModel vm)
        {
            if (SelectedSession == null) return;

            for (int i = 0; i < (int)EquipSlotId.Count; i++)
            {
                var slotId = (EquipSlotId)i;
                var eq = SelectedSession.Inventory.GetEquipped(slotId);
                if (eq.Container == vm.Container && eq.Slot == vm.Slot)
                {
                    vm.IsEquipped = true;
                    vm.EquippedSlotName = slotId.ToString();
                    return;
                }
            }

            vm.IsEquipped = false;
            vm.EquippedSlotName = null;
        }

        private void ApplyItemFilter()
        {
            FilteredItems.Clear();
            string filter = ItemSearchFilter.Trim();

            foreach (var item in ContainerItems)
            {
                if (string.IsNullOrEmpty(filter) ||
                    item.ItemDisplay.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                    item.Slot.ToString().Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                    item.ItemIdHex.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                    item.ItemId.ToString().Contains(filter, StringComparison.OrdinalIgnoreCase))
                {
                    FilteredItems.Add(item);
                }
            }

            if (SelectedItem != null && !FilteredItems.Contains(SelectedItem))
            {
                SelectedItem = FilteredItems.FirstOrDefault();
            }
        }

        #region Inbound Event Handlers

        private void OnCoreItemChanged(ContainerId container, byte slot, InventoryItem? item)
        {
            DispatchToUi(() =>
            {
                // Update container summary item count
                var summary = Containers.FirstOrDefault(c => c.Id == container);
                if (summary != null && SelectedSession != null)
                {
                    summary.ItemCount = SelectedSession.Inventory.GetContainer(container).Items.Count;
                }

                // If currently viewing this container, update ContainerItems
                if (SelectedContainer != null && SelectedContainer.Id == container)
                {
                    var existing = ContainerItems.FirstOrDefault(i => i.Slot == slot);

                    if (item == null)
                    {
                        if (existing != null)
                        {
                            ContainerItems.Remove(existing);
                            if (SelectedItem == existing)
                            {
                                SelectedItem = null;
                            }
                        }
                    }
                    else
                    {
                        if (existing != null)
                        {
                            existing.Count = item.Count;
                            existing.Price = item.Price;
                            existing.LockFlag = item.LockFlag;
                            existing.ExtData = item.ExtData;
                            CheckAndSetEquippedStatus(existing);
                        }
                        else
                        {
                            var newVm = InventoryItemViewModel.FromCore(item);
                            CheckAndSetEquippedStatus(newVm);

                            // Insert maintaining slot order
                            int insertIdx = 0;
                            while (insertIdx < ContainerItems.Count && ContainerItems[insertIdx].Slot < slot)
                            {
                                insertIdx++;
                            }
                            ContainerItems.Insert(insertIdx, newVm);
                        }
                    }

                    ApplyItemFilter();
                }

                // Update equipped gear slot if this item is currently equipped
                for (int i = 0; i < (int)EquipSlotId.Count; i++)
                {
                    var eqVm = EquippedSlots[i];
                    if (eqVm.Container == container && eqVm.ContainerSlot == slot)
                    {
                        eqVm.ItemId = item?.ItemId;
                        break;
                    }
                }
            });
        }

        private void OnCoreContainerSizesChanged(ContainerId container, byte maxSize, ushort usableSize)
        {
            DispatchToUi(() =>
            {
                var summary = Containers.FirstOrDefault(c => c.Id == container);
                if (summary != null)
                {
                    summary.MaxSize = maxSize;
                    summary.UsableSize = usableSize;
                }
            });
        }

        private void OnCoreEquipChanged(EquipSlotId slot, ContainerId container, byte slotIndex)
        {
            DispatchToUi(() =>
            {
                int idx = (int)slot;
                if (idx >= 0 && idx < EquippedSlots.Count)
                {
                    var eqVm = EquippedSlots[idx];
                    if (slotIndex == 0xFF)
                    {
                        eqVm.Clear();
                    }
                    else
                    {
                        ushort? itemId = null;
                        if (SelectedSession != null && SelectedSession.Inventory.GetContainer(container).TryGetItem(slotIndex, out var it))
                        {
                            itemId = it.ItemId;
                        }
                        eqVm.SetEquipped(container, slotIndex, itemId);
                    }
                }

                // Recheck equipped status for visible container items
                foreach (var item in ContainerItems)
                {
                    CheckAndSetEquippedStatus(item);
                }
            });
        }

        private void OnCoreCurrenciesChanged()
        {
            DispatchToUi(NotifyCurrenciesChanged);
        }

        private void NotifyCurrenciesChanged()
        {
            OnPropertyChanged(nameof(SparksOfEminence));
            OnPropertyChanged(nameof(UnityAccolades));
            OnPropertyChanged(nameof(ConquestSandoria));
            OnPropertyChanged(nameof(ConquestBastok));
            OnPropertyChanged(nameof(ConquestWindurst));
            OnPropertyChanged(nameof(ImperialStanding));
            OnPropertyChanged(nameof(AlliedNotes));
            OnPropertyChanged(nameof(LoginPoints));
            OnPropertyChanged(nameof(Cruor));
            OnPropertyChanged(nameof(Deeds));
            OnPropertyChanged(nameof(AncientBeastcoins));
            OnPropertyChanged(nameof(BeastmansSeals));
            OnPropertyChanged(nameof(KindredsSeals));
            OnPropertyChanged(nameof(Bayld));
            OnPropertyChanged(nameof(KineticUnits));
            OnPropertyChanged(nameof(CoalitionImprimaturs));
            OnPropertyChanged(nameof(MweyaPlasm));
            OnPropertyChanged(nameof(EschaBeads));
            OnPropertyChanged(nameof(EschaSilt));
            OnPropertyChanged(nameof(Hallmarks));
            OnPropertyChanged(nameof(BadgesOfGallantry));
            OnPropertyChanged(nameof(DomainPoints));
            OnPropertyChanged(nameof(MogSegments));
            OnPropertyChanged(nameof(Gallimaufry));
        }

        #endregion

        #region Outbound Execution Methods

        public async Task ExecuteRequestCurrencies1Async()
        {
            if (SelectedSession == null) return;
            try
            {
                await SelectedSession.InventoryModule.RequestCurrencies1Async().ConfigureAwait(false);
                DispatchToUi(() => StatusText = "Requested Currencies 1 (0x10F)...");
            }
            catch (Exception ex)
            {
                GordianLog.Error("INVENTORY_UI", "Failed requesting currencies 1", ex);
                DispatchToUi(() => StatusText = $"Currency request error: {ex.Message}");
            }
        }

        public async Task ExecuteRequestCurrencies2Async()
        {
            if (SelectedSession == null) return;
            try
            {
                await SelectedSession.InventoryModule.RequestCurrencies2Async().ConfigureAwait(false);
                DispatchToUi(() => StatusText = "Requested Currencies 2 (0x115)...");
            }
            catch (Exception ex)
            {
                GordianLog.Error("INVENTORY_UI", "Failed requesting currencies 2", ex);
                DispatchToUi(() => StatusText = $"Currency request error: {ex.Message}");
            }
        }

        public async Task ExecuteSortContainerAsync()
        {
            if (SelectedSession == null || SelectedContainer == null) return;
            try
            {
                await SelectedSession.InventoryModule.SortContainerAsync(SelectedContainer.Id).ConfigureAwait(false);
                DispatchToUi(() => StatusText = $"Sent sort request (0x03A) for {SelectedContainer.Name}...");
            }
            catch (Exception ex)
            {
                GordianLog.Error("INVENTORY_UI", "Failed sending sort request", ex);
                DispatchToUi(() => StatusText = $"Sort request error: {ex.Message}");
            }
        }

        public void ExecuteRefresh()
        {
            RefreshAllContainersAndEquip();
            RaiseCommandsCanExecute();
        }

        #endregion

        private static void DispatchToUi(Action action)
        {
            if (UiDispatcher != null)
            {
                UiDispatcher(action);
            }
            else
            {
                Dispatcher.UIThread.Post(action);
            }
        }

        public void Dispose()
        {
            _sessionRegistry.SessionRegistered -= OnSessionRegistered;
            _sessionRegistry.SessionUnregistered -= OnSessionUnregistered;

            if (_hookedSession != null)
            {
                _hookedSession.Inventory.ItemChanged -= OnCoreItemChanged;
                _hookedSession.Inventory.ContainerSizesChanged -= OnCoreContainerSizesChanged;
                _hookedSession.Inventory.EquipChanged -= OnCoreEquipChanged;
                _hookedSession.Inventory.CurrenciesChanged -= OnCoreCurrenciesChanged;
                _hookedSession = null;
            }
        }
    }
}
