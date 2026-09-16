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
        private string _packetsInRateText = "0.0 chunk/s";
        private string _packetsOutRateText = "0.0 chunk/s";
        private string _bandwidthInText = "0.0 KB/s";
        private string _bandwidthOutText = "0.0 KB/s";
        private string _totalPacketsText = "0 in / 0 out";
        private string _heapMemoryText = "0.0 MB";
        private string _allocationVelocityText = "+0.0 MB/s";
        private string _gcCollectionsText = "0 / 0 / 0";
        private string _gcPressureText = "0.0% pause";
        private string _sequenceDropsText = "0";
        private string _dispatchLatencyText = "-- µs";
        private string _spatialQueryText = "-- µs";
        private string _deadReckoningText = "-- µs";
        private string _benchmarkResultText = "Click 'Run Benchmark' to profile simulation cycles.";
        private bool _isBenchmarking;

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

        #region Entity Table User-Resizable Column Widths
        private Avalonia.Controls.GridLength _colWidthType = new Avalonia.Controls.GridLength(55);
        private Avalonia.Controls.GridLength _colWidthIndex = new Avalonia.Controls.GridLength(65);
        private Avalonia.Controls.GridLength _colWidthServerId = new Avalonia.Controls.GridLength(95);
        private Avalonia.Controls.GridLength _colWidthName = new Avalonia.Controls.GridLength(160);
        private Avalonia.Controls.GridLength _colWidthDist = new Avalonia.Controls.GridLength(65);
        private Avalonia.Controls.GridLength _colWidthCoords = new Avalonia.Controls.GridLength(140);
        private Avalonia.Controls.GridLength _colWidthHpp = new Avalonia.Controls.GridLength(50);

        public Avalonia.Controls.GridLength ColWidthType
        {
            get => _colWidthType;
            set => SetProperty(ref _colWidthType, value);
        }

        public Avalonia.Controls.GridLength ColWidthIndex
        {
            get => _colWidthIndex;
            set => SetProperty(ref _colWidthIndex, value);
        }

        public Avalonia.Controls.GridLength ColWidthServerId
        {
            get => _colWidthServerId;
            set => SetProperty(ref _colWidthServerId, value);
        }

        public Avalonia.Controls.GridLength ColWidthName
        {
            get => _colWidthName;
            set => SetProperty(ref _colWidthName, value);
        }

        public Avalonia.Controls.GridLength ColWidthDist
        {
            get => _colWidthDist;
            set => SetProperty(ref _colWidthDist, value);
        }

        public Avalonia.Controls.GridLength ColWidthCoords
        {
            get => _colWidthCoords;
            set => SetProperty(ref _colWidthCoords, value);
        }

        public Avalonia.Controls.GridLength ColWidthHpp
        {
            get => _colWidthHpp;
            set => SetProperty(ref _colWidthHpp, value);
        }

        /// <summary>
        /// Adjusts the width of a specific entity table column by a horizontal delta from a header thumb drag.
        /// Constrains width within minimum and maximum limits without affecting other columns.
        /// </summary>
        public void AdjustColumnWidth(string columnName, double deltaX)
        {
            switch (columnName)
            {
                case "Type":
                    ColWidthType = new Avalonia.Controls.GridLength(Math.Clamp(ColWidthType.Value + deltaX, 35, 160));
                    break;
                case "Index":
                    ColWidthIndex = new Avalonia.Controls.GridLength(Math.Clamp(ColWidthIndex.Value + deltaX, 45, 160));
                    break;
                case "ServerId":
                    ColWidthServerId = new Avalonia.Controls.GridLength(Math.Clamp(ColWidthServerId.Value + deltaX, 60, 200));
                    break;
                case "Name":
                    ColWidthName = new Avalonia.Controls.GridLength(Math.Clamp(ColWidthName.Value + deltaX, 70, 500));
                    break;
                case "Dist":
                    ColWidthDist = new Avalonia.Controls.GridLength(Math.Clamp(ColWidthDist.Value + deltaX, 45, 160));
                    break;
                case "Coords":
                    ColWidthCoords = new Avalonia.Controls.GridLength(Math.Clamp(ColWidthCoords.Value + deltaX, 80, 350));
                    break;
                case "Hpp":
                    ColWidthHpp = new Avalonia.Controls.GridLength(Math.Clamp(ColWidthHpp.Value + deltaX, 40, 160));
                    break;
            }
        }
        #endregion

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

        public string DispatchLatencyText
        {
            get => _dispatchLatencyText;
            private set => SetProperty(ref _dispatchLatencyText, value);
        }

        public string AllocationVelocityText
        {
            get => _allocationVelocityText;
            private set => SetProperty(ref _allocationVelocityText, value);
        }

        public string GcPressureText
        {
            get => _gcPressureText;
            private set => SetProperty(ref _gcPressureText, value);
        }

        public string SpatialQueryText
        {
            get => _spatialQueryText;
            private set => SetProperty(ref _spatialQueryText, value);
        }

        public string DeadReckoningText
        {
            get => _deadReckoningText;
            private set => SetProperty(ref _deadReckoningText, value);
        }

        public string BenchmarkResultText
        {
            get => _benchmarkResultText;
            private set => SetProperty(ref _benchmarkResultText, value);
        }

        public bool IsBenchmarking
        {
            get => _isBenchmarking;
            private set
            {
                if (SetProperty(ref _isBenchmarking, value))
                {
                    RunDeadReckoningBenchmarkCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public RelayCommand RunDeadReckoningBenchmarkCommand { get; }
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

            RunDeadReckoningBenchmarkCommand = new RelayCommand(RunDeadReckoningBenchmark, () => !IsBenchmarking);
        }

        public async void RunDeadReckoningBenchmark()
        {
            if (IsBenchmarking) return;
            IsBenchmarking = true;
            BenchmarkResultText = "Benchmarking 50 dead-reckoning & grid iterations...";

            try
            {
                var session = SelectedSession;
                DeadReckoningBenchmarkResult result;

                if (session != null)
                {
                    if (session.World.Count == 0)
                    {
                        session.World.PopulateSyntheticEntities(50);
                    }
                    result = await System.Threading.Tasks.Task.Run(() =>
                        session.World.BenchmarkDeadReckoning(50, TimeSpan.FromMilliseconds(250), updateSpatialGrid: true));

                    var worldPerf = session.World.Performance.GetSnapshot(session.World.Count);
                    UpdateWorldTelemetryTexts(worldPerf);
                }
                else
                {
                    var testWorld = new WorldState();
                    testWorld.PopulateSyntheticEntities(50);
                    result = await System.Threading.Tasks.Task.Run(() =>
                        testWorld.BenchmarkDeadReckoning(50, TimeSpan.FromMilliseconds(250), updateSpatialGrid: true));

                    var worldPerf = testWorld.Performance.GetSnapshot(testWorld.Count);
                    UpdateWorldTelemetryTexts(worldPerf);
                }

                BenchmarkResultText = $"{result.EntityCount} ents × {result.Iterations} iter: avg {result.AvgCycleMicroseconds:F1} µs (min {result.MinCycleMicroseconds:F1} µs, max {result.MaxCycleMicroseconds:F1} µs, p95 {result.P95CycleMicroseconds:F1} µs) — {result.ThroughputEntitiesPerSecond:N0} ents/s";
            }
            catch (Exception ex)
            {
                BenchmarkResultText = $"Benchmark error: {ex.Message}";
            }
            finally
            {
                IsBenchmarking = false;
            }
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
                if (session.CharacterId != 0 && session.LocalPlayer.ServerId == 0)
                {
                    session.LocalPlayer.ServerId = session.CharacterId;
                }

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
            uint localCharId = SelectedSession?.CharacterId ?? 0;

            EntityItemViewModel item;
            lock (_entityLock)
            {
                if (_entityMap.TryGetValue(entity.ServerId, out var existing))
                {
                    existing.Update(entity, playerPos, localCharId);
                    item = existing;
                }
                else
                {
                    item = new EntityItemViewModel(entity, playerPos, localCharId);
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
            uint localCharId = SelectedSession?.CharacterId ?? 0;

            lock (_entityLock)
            {
                if (_entityMap.TryGetValue(entity.ServerId, out var item))
                {
                    item.Update(entity, playerPos, localCharId);
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

            // Update tracked entity distances relative to live player coordinates
            Vector3 playerPos = new Vector3(PositionX, PositionY, PositionZ);
            uint localCharId = SelectedSession.CharacterId;
            lock (_entityLock)
            {
                foreach (var item in _entityMap.Values)
                {
                    if (localCharId != 0 && item.ServerId == localCharId)
                    {
                        item.Distance = 0f;
                    }
                    else if (playerPos != Vector3.Zero)
                    {
                        item.Distance = Vector3.Distance(playerPos, item.Position);
                    }
                }
            }

            // Query atomic performance snapshot
            var snapshot = SelectedSession.Performance.GetSnapshot();

            PacketsInRateText = $"{snapshot.PacketsReceivedPerSecond:F1} (avg {snapshot.PacketsReceived30SecAverage:F1}) c/s";
            PacketsOutRateText = $"{snapshot.PacketsSentPerSecond:F1} (avg {snapshot.PacketsSent30SecAverage:F1}) c/s";
            BandwidthInText = $"{snapshot.KilobytesReceivedPerSecond:F1} KB/s";
            BandwidthOutText = $"{snapshot.KilobytesSentPerSecond:F1} KB/s";
            TotalPacketsText = $"{snapshot.PacketsReceivedTotal:N0} in / {snapshot.PacketsSentTotal:N0} out";
            HeapMemoryText = $"{snapshot.ManagedHeapMegaBytes:F1} MB (+{snapshot.AllocationVelocityMegaBytesPerSecond:F1} MB/s)";
            AllocationVelocityText = $"+{snapshot.AllocationVelocityMegaBytesPerSecond:F1} MB/s";
            GcCollectionsText = $"{snapshot.Gen0Collections} / {snapshot.Gen1Collections} / {snapshot.Gen2Collections}";
            GcPressureText = $"{snapshot.PauseDurationPercentage:F1}% pause";
            SequenceDropsText = $"{snapshot.SequenceDiscrepancies}";

            string lastStr = snapshot.LastDispatchLatencyMicroseconds <= 0 ? "--" :
                snapshot.LastDispatchLatencyMicroseconds < 1.0 ? "< 1" : $"{snapshot.LastDispatchLatencyMicroseconds:F0}";
            string avgStr = snapshot.AverageDispatchLatencyMicroseconds <= 0 ? "--" :
                snapshot.AverageDispatchLatencyMicroseconds < 1.0 ? "< 1" : $"{snapshot.AverageDispatchLatencyMicroseconds:F0}";
            DispatchLatencyText = $"{lastStr} µs (avg {avgStr})";

            // Query world spatial & dead-reckoning telemetry
            var worldPerf = SelectedSession.World.Performance.GetSnapshot(SelectedSession.World.Count);
            UpdateWorldTelemetryTexts(worldPerf);
        }

        private void UpdateWorldTelemetryTexts(WorldPerformanceSnapshot worldPerf)
        {
            if (worldPerf.Spatial.TotalQueries > 0)
            {
                string spatialAvgStr = worldPerf.Spatial.OverallAvgQueryMicroseconds < 1.0 ? "< 1" : $"{worldPerf.Spatial.OverallAvgQueryMicroseconds:F0}";
                string spatialPeakStr = worldPerf.Spatial.OverallPeakQueryMicroseconds < 1.0 ? "< 1" : $"{worldPerf.Spatial.OverallPeakQueryMicroseconds:F0}";
                SpatialQueryText = $"{spatialAvgStr} µs (peak {spatialPeakStr})";
            }
            else if (worldPerf.Spatial.TotalUpdates > 0)
            {
                string updateAvgStr = worldPerf.Spatial.UpdateAvgMicroseconds < 1.0 ? "< 1" : $"{worldPerf.Spatial.UpdateAvgMicroseconds:F0}";
                SpatialQueryText = $"{updateAvgStr} µs ({worldPerf.Spatial.TotalUpdates} upd)";
            }
            else
            {
                SpatialQueryText = "-- µs";
            }

            if (worldPerf.DeadReckoning.TotalCycles > 0)
            {
                string drAvgStr = worldPerf.DeadReckoning.AverageCycleMicroseconds < 1.0 ? "< 1" : $"{worldPerf.DeadReckoning.AverageCycleMicroseconds:F0}";
                DeadReckoningText = $"{drAvgStr} µs ({worldPerf.DeadReckoning.TotalCycles} cyc)";
            }
            else
            {
                DeadReckoningText = "-- µs";
            }
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
