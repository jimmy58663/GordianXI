// src/Gordian.App/ViewModels/StateInspectorViewModel.cs
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Numerics;
using Avalonia.Threading;
using Gordian.App.Common;
using Gordian.Core.Diagnostics;
using Gordian.Core.Network;
using Gordian.Core.World;

namespace Gordian.App.ViewModels
{
    /// <summary>
    /// ViewModel driving the Character and World State Inspector tab, exposing real-time
    /// player vitals, 3D world coordinates, combat stats, tracked entities, and datagram telemetry.
    /// </summary>
    public sealed class StateInspectorViewModel : ViewModelBase, IDisposable
    {
        private readonly SessionRegistry _sessionRegistry;
        private readonly DispatcherTimer _telemetryTimer;
        private readonly object _entityLock = new object();
        private readonly Dictionary<uint, EntityItemViewModel> _entityMap = new Dictionary<uint, EntityItemViewModel>();

        private CharacterSession? _selectedSession;
        private string _entitySearchFilter = string.Empty;
        private string _entityTypeFilter = "All";

        // Performance telemetry displays
        private string _packetsInRateText = "0.0 pkt/s";
        private string _packetsOutRateText = "0.0 pkt/s";
        private string _bandwidthInText = "0.0 KB/s";
        private string _bandwidthOutText = "0.0 KB/s";
        private string _totalPacketsText = "0 in / 0 out";
        private string _heapMemoryText = "0.0 MB";
        private string _gcCollectionsText = "0 / 0 / 0";
        private string _sequenceDropsText = "0";

        public ObservableCollection<CharacterSession> ActiveSessions { get; } = new();
        public ObservableCollection<EntityItemViewModel> FilteredEntities { get; } = new();

        public IReadOnlyList<string> EntityTypeOptions { get; } = new[]
        {
            "All",
            "Player",
            "Monster",
            "Npc",
            "Other"
        };

        public CharacterSession? SelectedSession
        {
            get => _selectedSession;
            set
            {
                if (SetProperty(ref _selectedSession, value))
                {
                    OnSelectedSessionChanged(value);
                }
            }
        }

        public bool HasActiveSession => SelectedSession != null;

        public string EntitySearchFilter
        {
            get => _entitySearchFilter;
            set
            {
                if (SetProperty(ref _entitySearchFilter, value))
                {
                    ApplyEntityFilter();
                }
            }
        }

        public string EntityTypeFilter
        {
            get => _entityTypeFilter;
            set
            {
                if (SetProperty(ref _entityTypeFilter, value))
                {
                    ApplyEntityFilter();
                }
            }
        }

        #region Performance Telemetry Properties
        public string PacketsInRateText
        {
            get => _packetsInRateText;
            private set => SetProperty(ref _packetsInRateText, value);
        }

        public string PacketsOutRateText
        {
            get => _packetsOutRateText;
            private set => SetProperty(ref _packetsOutRateText, value);
        }

        public string BandwidthInText
        {
            get => _bandwidthInText;
            private set => SetProperty(ref _bandwidthInText, value);
        }

        public string BandwidthOutText
        {
            get => _bandwidthOutText;
            private set => SetProperty(ref _bandwidthOutText, value);
        }

        public string TotalPacketsText
        {
            get => _totalPacketsText;
            private set => SetProperty(ref _totalPacketsText, value);
        }

        public string HeapMemoryText
        {
            get => _heapMemoryText;
            private set => SetProperty(ref _heapMemoryText, value);
        }

        public string GcCollectionsText
        {
            get => _gcCollectionsText;
            private set => SetProperty(ref _gcCollectionsText, value);
        }

        public string SequenceDropsText
        {
            get => _sequenceDropsText;
            private set => SetProperty(ref _sequenceDropsText, value);
        }
        #endregion

        #region Player State Properties
        public string CharacterName => SelectedSession?.CharacterName ?? "No Session";
        public string CharacterIdDisplay => SelectedSession != null ? $"ID: {SelectedSession.CharacterId} (0x{SelectedSession.CharacterId:X6})" : "ID: --";
        public string SessionStateDisplay => SelectedSession?.State.ToString() ?? "Disconnected";
        public string ServerAddressDisplay => SelectedSession != null ? $"{SelectedSession.NetworkManager.ServerAddress}" : "--";

        public string JobDisplay
        {
            get
            {
                if (SelectedSession == null) return "None";
                var player = SelectedSession.LocalPlayer;
                if (player.MainJob == Gordian.Core.Network.Packets.JobId.None) return "Awaiting Status...";
                string main = $"{player.MainJob} {player.MainJobLevel}";
                if (player.SubJob != Gordian.Core.Network.Packets.JobId.None && player.SubJobLevel > 0)
                {
                    return $"{main} / {player.SubJob} {player.SubJobLevel}";
                }
                return main;
            }
        }

        public int CurrentHp => SelectedSession?.LocalPlayer.CurrentHp ?? 0;
        public int MaxHp => SelectedSession?.LocalPlayer.MaxHp ?? 0;
        public int CurrentMp => SelectedSession?.LocalPlayer.CurrentMp ?? 0;
        public int MaxMp => SelectedSession?.LocalPlayer.MaxMp ?? 0;
        public short CurrentTp => SelectedSession?.LocalPlayer.CurrentTp ?? 0;
        public byte Hpp => SelectedSession?.LocalPlayer.Hpp ?? 0;

        public double HpPercent => MaxHp > 0 ? Math.Clamp((double)CurrentHp / MaxHp, 0.0, 1.0) : 0.0;
        public double MpPercent => MaxMp > 0 ? Math.Clamp((double)CurrentMp / MaxMp, 0.0, 1.0) : 0.0;
        public double TpPercent => Math.Clamp((double)CurrentTp / 3000.0, 0.0, 1.0);

        public string HpText => MaxHp > 0 ? $"{CurrentHp} / {MaxHp} ({Hpp}%)" : $"{CurrentHp}";
        public string MpText => MaxMp > 0 ? $"{CurrentMp} / {MaxMp}" : $"{CurrentMp}";
        public string TpText => $"{CurrentTp} / 3000";

        public float PositionX => SelectedSession?.NetworkManager.PositionX ?? 0f;
        public float PositionY => SelectedSession?.NetworkManager.PositionY ?? 0f;
        public float PositionZ => SelectedSession?.NetworkManager.PositionZ ?? 0f;
        public byte Direction => SelectedSession?.NetworkManager.Direction ?? 0;
        public string CoordinatesDisplay => $"X: {PositionX:F2}  Y: {PositionY:F2}  Z: {PositionZ:F2}";
        public string HeadingDisplay => $"Dir: {Direction} ({(Direction / 256.0f * 360f):F0}°)";

        public string StatStr => FormatStat(0);
        public string StatDex => FormatStat(1);
        public string StatVit => FormatStat(2);
        public string StatAgi => FormatStat(3);
        public string StatInt => FormatStat(4);
        public string StatMnd => FormatStat(5);
        public string StatChr => FormatStat(6);
        public string AttackDisplay => SelectedSession != null ? $"{SelectedSession.LocalPlayer.Attack}" : "--";
        public string DefenseDisplay => SelectedSession != null ? $"{SelectedSession.LocalPlayer.Defense}" : "--";

        public int TotalEntityCount
        {
            get
            {
                lock (_entityLock) return _entityMap.Count;
            }
        }

        private string FormatStat(int index)
        {
            if (SelectedSession == null) return "--";
            var p = SelectedSession.LocalPlayer;
            ushort b = p.BaseStats[index];
            short m = p.StatModifiers[index];
            if (b == 0 && m == 0) return "--";
            return m != 0 ? $"{b + m} ({m:+0;-0})" : $"{b}";
        }
        #endregion

        public StateInspectorViewModel(SessionRegistry? registry = null)
        {
            _sessionRegistry = registry ?? SessionRegistry.Default;
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

            _telemetryTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _telemetryTimer.Tick += OnTelemetryTimerTick;
            _telemetryTimer.Start();
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
            });
        }

        private void OnSelectedSessionChanged(CharacterSession? session)
        {
            OnPropertyChanged(nameof(HasActiveSession));
            OnPropertyChanged(nameof(CharacterName));
            OnPropertyChanged(nameof(CharacterIdDisplay));
            OnPropertyChanged(nameof(SessionStateDisplay));
            OnPropertyChanged(nameof(ServerAddressDisplay));

            ClearEntities();

            if (session != null)
            {
                session.LocalPlayer.VitalsUpdated += RefreshVitalsOnUi;
                session.LocalPlayer.StatsUpdated += RefreshStatsOnUi;
                session.World.EntitySpawned += OnEntitySpawned;
                session.World.EntityUpdated += OnEntityUpdated;
                session.World.EntityDespawned += OnEntityDespawned;
                session.World.WorldCleared += ClearEntities;

                // Populate initial entities snapshot
                foreach (var entity in session.World.GetAllEntities())
                {
                    OnEntitySpawned(entity);
                }
            }

            RefreshAllPlayerProperties();
        }

        private void RefreshVitalsOnUi()
        {
            DispatchToUi(() =>
            {
                OnPropertyChanged(nameof(CurrentHp));
                OnPropertyChanged(nameof(MaxHp));
                OnPropertyChanged(nameof(CurrentMp));
                OnPropertyChanged(nameof(MaxMp));
                OnPropertyChanged(nameof(CurrentTp));
                OnPropertyChanged(nameof(Hpp));
                OnPropertyChanged(nameof(HpPercent));
                OnPropertyChanged(nameof(MpPercent));
                OnPropertyChanged(nameof(TpPercent));
                OnPropertyChanged(nameof(HpText));
                OnPropertyChanged(nameof(MpText));
                OnPropertyChanged(nameof(TpText));
            });
        }

        private void RefreshStatsOnUi()
        {
            DispatchToUi(() =>
            {
                OnPropertyChanged(nameof(JobDisplay));
                OnPropertyChanged(nameof(StatStr));
                OnPropertyChanged(nameof(StatDex));
                OnPropertyChanged(nameof(StatVit));
                OnPropertyChanged(nameof(StatAgi));
                OnPropertyChanged(nameof(StatInt));
                OnPropertyChanged(nameof(StatMnd));
                OnPropertyChanged(nameof(StatChr));
                OnPropertyChanged(nameof(AttackDisplay));
                OnPropertyChanged(nameof(DefenseDisplay));
            });
        }

        private void RefreshAllPlayerProperties()
        {
            RefreshVitalsOnUi();
            RefreshStatsOnUi();
            OnPropertyChanged(nameof(PositionX));
            OnPropertyChanged(nameof(PositionY));
            OnPropertyChanged(nameof(PositionZ));
            OnPropertyChanged(nameof(Direction));
            OnPropertyChanged(nameof(CoordinatesDisplay));
            OnPropertyChanged(nameof(HeadingDisplay));
        }

        private void OnEntitySpawned(WorldEntity entity)
        {
            Vector3 playerPos = SelectedSession != null
                ? new Vector3(SelectedSession.NetworkManager.PositionX, SelectedSession.NetworkManager.PositionY, SelectedSession.NetworkManager.PositionZ)
                : Vector3.Zero;

            EntityItemViewModel item;
            lock (_entityLock)
            {
                if (_entityMap.TryGetValue(entity.ServerId, out var existing))
                {
                    existing.Update(entity, playerPos);
                    item = existing;
                }
                else
                {
                    item = new EntityItemViewModel(entity, playerPos);
                    _entityMap[entity.ServerId] = item;
                }
            }

            DispatchToUi(() =>
            {
                OnPropertyChanged(nameof(TotalEntityCount));
                if (MatchesEntityFilter(item) && !FilteredEntities.Contains(item))
                {
                    FilteredEntities.Add(item);
                }
            });
        }

        private void OnEntityUpdated(WorldEntity entity)
        {
            Vector3 playerPos = SelectedSession != null
                ? new Vector3(SelectedSession.NetworkManager.PositionX, SelectedSession.NetworkManager.PositionY, SelectedSession.NetworkManager.PositionZ)
                : Vector3.Zero;

            lock (_entityLock)
            {
                if (_entityMap.TryGetValue(entity.ServerId, out var item))
                {
                    item.Update(entity, playerPos);
                }
            }
        }

        private void OnEntityDespawned(WorldEntity entity)
        {
            EntityItemViewModel? removed = null;
            lock (_entityLock)
            {
                if (_entityMap.Remove(entity.ServerId, out removed))
                {
                }
            }

            if (removed != null)
            {
                DispatchToUi(() =>
                {
                    FilteredEntities.Remove(removed);
                    OnPropertyChanged(nameof(TotalEntityCount));
                });
            }
        }

        private void ClearEntities()
        {
            lock (_entityLock)
            {
                _entityMap.Clear();
            }
            DispatchToUi(() =>
            {
                FilteredEntities.Clear();
                OnPropertyChanged(nameof(TotalEntityCount));
            });
        }

        private void ApplyEntityFilter()
        {
            lock (_entityLock)
            {
                var matching = _entityMap.Values
                    .Where(MatchesEntityFilter)
                    .OrderBy(e => e.Distance)
                    .ToList();

                DispatchToUi(() =>
                {
                    FilteredEntities.Clear();
                    foreach (var item in matching)
                    {
                        FilteredEntities.Add(item);
                    }
                });
            }
        }

        private bool MatchesEntityFilter(EntityItemViewModel item)
        {
            if (item == null) return false;

            // Type filter
            if (!string.Equals(_entityTypeFilter, "All", StringComparison.OrdinalIgnoreCase))
            {
                bool matchesType = _entityTypeFilter.ToLowerInvariant() switch
                {
                    "player" => item.Type == EntityType.Player,
                    "monster" => item.Type == EntityType.Monster,
                    "npc" => item.Type == EntityType.Npc,
                    "other" => item.Type != EntityType.Player && item.Type != EntityType.Monster && item.Type != EntityType.Npc,
                    _ => true
                };
                if (!matchesType) return false;
            }

            // Text search filter
            if (!string.IsNullOrWhiteSpace(_entitySearchFilter))
            {
                string search = _entitySearchFilter.Trim();
                return item.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                       item.TargetIndexHex.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                       item.ServerIdHex.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                       item.TargetIndex.ToString().Contains(search, StringComparison.OrdinalIgnoreCase);
            }

            return true;
        }

        private void OnTelemetryTimerTick(object? sender, EventArgs e)
        {
            if (SelectedSession == null) return;

            // Update player coordinates and heading
            OnPropertyChanged(nameof(PositionX));
            OnPropertyChanged(nameof(PositionY));
            OnPropertyChanged(nameof(PositionZ));
            OnPropertyChanged(nameof(Direction));
            OnPropertyChanged(nameof(CoordinatesDisplay));
            OnPropertyChanged(nameof(HeadingDisplay));
            OnPropertyChanged(nameof(SessionStateDisplay));

            // Query atomic performance snapshot
            var snapshot = SelectedSession.Performance.GetSnapshot();

            PacketsInRateText = $"{snapshot.PacketsReceivedPerSecond:F1} pkt/s";
            PacketsOutRateText = $"{snapshot.PacketsSentPerSecond:F1} pkt/s";
            BandwidthInText = $"{snapshot.KilobytesReceivedPerSecond:F1} KB/s";
            BandwidthOutText = $"{snapshot.KilobytesSentPerSecond:F1} KB/s";
            TotalPacketsText = $"{snapshot.PacketsReceivedTotal:N0} in / {snapshot.PacketsSentTotal:N0} out";
            HeapMemoryText = $"{snapshot.ManagedHeapMegaBytes:F1} MB";
            GcCollectionsText = $"{snapshot.Gen0Collections} / {snapshot.Gen1Collections} / {snapshot.Gen2Collections}";
            SequenceDropsText = $"{snapshot.SequenceDiscrepancies}";
        }

        public void Dispose()
        {
            _telemetryTimer.Stop();
            _telemetryTimer.Tick -= OnTelemetryTimerTick;

            if (SelectedSession != null)
            {
                SelectedSession.LocalPlayer.VitalsUpdated -= RefreshVitalsOnUi;
                SelectedSession.LocalPlayer.StatsUpdated -= RefreshStatsOnUi;
                SelectedSession.World.EntitySpawned -= OnEntitySpawned;
                SelectedSession.World.EntityUpdated -= OnEntityUpdated;
                SelectedSession.World.EntityDespawned -= OnEntityDespawned;
                SelectedSession.World.WorldCleared -= ClearEntities;
            }

            _sessionRegistry.SessionRegistered -= OnSessionRegistered;
            _sessionRegistry.SessionUnregistered -= OnSessionUnregistered;
        }

        /// <summary>
        /// Optional delegate to route UI thread dispatches (e.g. for synchronous execution in test suites).
        /// Defaults to Avalonia's <see cref="Dispatcher.UIThread"/> when null.
        /// </summary>
        public static Action<Action>? UiDispatcher { get; set; }

        private static void DispatchToUi(Action action)
        {
            if (UiDispatcher != null)
            {
                UiDispatcher(action);
                return;
            }

            if (Dispatcher.UIThread.CheckAccess())
            {
                action();
            }
            else
            {
                Dispatcher.UIThread.Post(action);
            }
        }
    }
}
