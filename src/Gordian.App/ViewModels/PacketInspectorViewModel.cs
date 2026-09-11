// src/Gordian.App/ViewModels/PacketInspectorViewModel.cs
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using Avalonia.Threading;
using Gordian.App.Common;
using Gordian.Core.Network;

namespace Gordian.App.ViewModels
{
    /// <summary>
    /// ViewModel for live packet stream inspection, dual-column Hex/ASCII dump rendering,
    /// directional filtering, and search indexing.
    /// </summary>
    public sealed class PacketInspectorViewModel : ViewModelBase, IDisposable
    {
        private const int DefaultMaxPackets = 500;
        private readonly List<PacketLogEntry> _allPackets = new();
        private readonly object _lock = new();

        private PacketLogEntry? _selectedPacket;
        private string _formattedDump = "<Select a packet from the list to view hex/ASCII dump>";
        private string _directionFilter = "All";
        private string _searchFilter = string.Empty;
        private bool _isPaused;
        private bool _autoScroll = true;
        private int _totalPacketCount;

        public int MaxPackets { get; set; } = DefaultMaxPackets;

        /// <summary>
        /// Filtered packet collection bound directly to the UI DataGrid / ListBox.
        /// </summary>
        public ObservableCollection<PacketLogEntry> FilteredPackets { get; } = new();

        public PacketLogEntry? SelectedPacket
        {
            get => _selectedPacket;
            set
            {
                if (SetProperty(ref _selectedPacket, value))
                {
                    FormattedDump = value?.HexAsciiDump ?? "<Select a packet from the list to view hex/ASCII dump>";
                }
            }
        }

        public string FormattedDump
        {
            get => _formattedDump;
            private set => SetProperty(ref _formattedDump, value);
        }

        public string DirectionFilter
        {
            get => _directionFilter;
            set
            {
                if (SetProperty(ref _directionFilter, value))
                {
                    ApplyFilter();
                }
            }
        }

        public string SearchFilter
        {
            get => _searchFilter;
            set
            {
                if (SetProperty(ref _searchFilter, value))
                {
                    ApplyFilter();
                }
            }
        }

        public bool IsPaused
        {
            get => _isPaused;
            set => SetProperty(ref _isPaused, value);
        }

        public bool AutoScroll
        {
            get => _autoScroll;
            set => SetProperty(ref _autoScroll, value);
        }

        public int TotalPacketCount
        {
            get => _totalPacketCount;
            private set => SetProperty(ref _totalPacketCount, value);
        }

        public IReadOnlyList<string> DirectionOptions { get; } = new[] { "All", "Inbound", "Outbound" };

        public ICommand ClearCommand { get; }
        public ICommand TogglePauseCommand { get; }

        public PacketInspectorViewModel()
        {
            ClearCommand = new RelayCommand(Clear);
            TogglePauseCommand = new RelayCommand(() => IsPaused = !IsPaused);

            // Connect to active session registry
            SessionRegistry.Default.SessionRegistered += OnSessionRegistered;
            foreach (var session in SessionRegistry.Default.ActiveSessions)
            {
                HookSession(session);
            }
        }

        private void OnSessionRegistered(object? sender, CharacterSession session)
        {
            HookSession(session);
        }

        private void HookSession(CharacterSession session)
        {
            session.NetworkManager.PacketInspected += OnPacketInspected;
        }

        public void OnPacketInspected(object? sender, PacketLogEntry entry)
        {
            if (entry == null) return;

            // When paused, newly arriving packets are not appended to the inspection view
            if (IsPaused) return;

            DispatchToUi(() =>
            {
                lock (_lock)
                {
                    _allPackets.Add(entry);
                    if (_allPackets.Count > MaxPackets)
                    {
                        _allPackets.RemoveAt(0);
                    }
                    TotalPacketCount = _allPackets.Count;

                    if (MatchesFilter(entry))
                    {
                        FilteredPackets.Add(entry);
                        if (FilteredPackets.Count > MaxPackets)
                        {
                            FilteredPackets.RemoveAt(0);
                        }
                    }
                }
            });
        }

        public void Clear()
        {
            lock (_lock)
            {
                _allPackets.Clear();
                FilteredPackets.Clear();
                SelectedPacket = null;
                TotalPacketCount = 0;
            }
        }

        public void ApplyFilter()
        {
            lock (_lock)
            {
                FilteredPackets.Clear();
                foreach (var packet in _allPackets.Where(MatchesFilter))
                {
                    FilteredPackets.Add(packet);
                }
            }
        }

        private bool MatchesFilter(PacketLogEntry entry)
        {
            // Direction filter
            if (DirectionFilter == "Inbound" && entry.Direction != PacketDirection.Inbound) return false;
            if (DirectionFilter == "Outbound" && entry.Direction != PacketDirection.Outbound) return false;

            // Search text filter
            if (!string.IsNullOrWhiteSpace(SearchFilter))
            {
                string query = SearchFilter.Trim();
                bool matchesName = entry.PacketName.Contains(query, StringComparison.OrdinalIgnoreCase);
                bool matchesId = entry.PacketId.ToString("X").Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                 entry.PacketId.ToString().Contains(query, StringComparison.OrdinalIgnoreCase);
                if (!matchesName && !matchesId) return false;
            }

            return true;
        }

        private static void DispatchToUi(Action action)
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                action();
            }
            else
            {
                Dispatcher.UIThread.Post(action);
            }
        }

        public void Dispose()
        {
            SessionRegistry.Default.SessionRegistered -= OnSessionRegistered;
            foreach (var session in SessionRegistry.Default.ActiveSessions)
            {
                session.NetworkManager.PacketInspected -= OnPacketInspected;
            }
        }
    }
}
