using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using Avalonia.Threading;
using Gordian.App.Common;
using Gordian.Core.Network;

namespace Gordian.App.ViewModels
{
    /// <summary>
    /// ViewModel for live packet stream inspection, dual-column Hex/ASCII dump rendering,
    /// directional filtering, search indexing, and ignored packet suppression.
    /// </summary>
    public sealed class PacketInspectorViewModel : ViewModelBase, IDisposable
    {
        private const int DefaultMaxPackets = 2000;
        private readonly List<PacketLogEntry> _allPackets = new();
        private readonly object _lock = new();

        private readonly HashSet<ushort> _ignoredPacketIds = new();
        private readonly List<string> _ignoredNames = new();

        private PacketLogEntry? _selectedPacket;
        private string _formattedDump = "<Select a packet from the list to view hex/ASCII dump>";
        private string _directionFilter = "All";
        private string _searchFilter = string.Empty;
        private string _ignoreFilter = string.Empty;
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

        public string IgnoreFilter
        {
            get => _ignoreFilter;
            set
            {
                if (SetProperty(ref _ignoreFilter, value))
                {
                    UpdateIgnoreList();
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

            // Ignore filter check before dispatching: dropped entirely to prevent buffer consumption
            lock (_lock)
            {
                if (IsIgnored(entry)) return;
            }

            DispatchToUi(() =>
            {
                lock (_lock)
                {
                    if (IsIgnored(entry)) return;

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
            // Ignore filter check for any existing packets in buffer
            if (IsIgnored(entry)) return false;

            // Direction filter
            if (DirectionFilter == "Inbound" && entry.Direction != PacketDirection.Inbound) return false;
            if (DirectionFilter == "Outbound" && entry.Direction != PacketDirection.Outbound) return false;

            // Search text filter
            if (!string.IsNullOrWhiteSpace(SearchFilter))
            {
                string query = SearchFilter.Trim();
                bool matchesName = entry.PacketName.Contains(query, StringComparison.OrdinalIgnoreCase);

                string hexQuery = query.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                    ? query[2..]
                    : query;

                bool matchesExactHex = ushort.TryParse(hexQuery, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort parsedHex)
                    && entry.PacketId == parsedHex;

                bool matchesExactDec = ushort.TryParse(query, NumberStyles.Integer, CultureInfo.InvariantCulture, out ushort parsedDec)
                    && entry.PacketId == parsedDec;

                string hex3 = entry.PacketId.ToString("X3");
                string hexWith0x = $"0x{hex3}";
                string hexBare = entry.PacketId.ToString("X");
                string dec = entry.PacketId.ToString(CultureInfo.InvariantCulture);

                bool matchesSubstring = hexWith0x.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                        hex3.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                        hexBare.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                        dec.Contains(query, StringComparison.OrdinalIgnoreCase);

                if (!matchesName && !matchesExactHex && !matchesExactDec && !matchesSubstring)
                {
                    return false;
                }
            }

            return true;
        }

        public bool IsIgnored(PacketLogEntry entry)
        {
            if (_ignoredPacketIds.Contains(entry.PacketId)) return true;
            for (int i = 0; i < _ignoredNames.Count; i++)
            {
                if (entry.PacketName.Contains(_ignoredNames[i], StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private void UpdateIgnoreList()
        {
            lock (_lock)
            {
                _ignoredPacketIds.Clear();
                _ignoredNames.Clear();

                if (!string.IsNullOrWhiteSpace(_ignoreFilter))
                {
                    var tokens = _ignoreFilter.Split(new[] { ',', ';', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var rawToken in tokens)
                    {
                        string token = rawToken.Trim();
                        if (string.IsNullOrEmpty(token)) continue;

                        if (token.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                        {
                            if (ushort.TryParse(token[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort id))
                            {
                                _ignoredPacketIds.Add(id);
                                continue;
                            }
                        }
                        else if (ushort.TryParse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort hexId))
                        {
                            _ignoredPacketIds.Add(hexId);
                            continue;
                        }

                        _ignoredNames.Add(token);
                    }
                }

                if (_ignoredPacketIds.Count > 0 || _ignoredNames.Count > 0)
                {
                    _allPackets.RemoveAll(IsIgnored);
                    TotalPacketCount = _allPackets.Count;
                }
            }
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
