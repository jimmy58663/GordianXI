// src/Gordian.Core/World/ProgressionState.cs
using System;
using System.Collections.Generic;
using Gordian.Core.Network.Packets;

namespace Gordian.Core.World
{
    /// <summary>
    /// Snapshot data structure for an active cutscene or NPC dialogue event.
    /// </summary>
    public sealed record CutsceneEventInfo(
        uint UniqueNo,
        ushort ActIndex,
        ushort EventNum,
        ushort EventPara,
        ushort Mode,
        int[] NumericParams,
        string[] StringParams,
        uint[] DataParams,
        bool FromZoneIn = false
    );

    /// <summary>
    /// A zone dialog message the server asked the client to print (S2C 0x036 / 0x02A / 0x027 / 0x043 / 0x03B): the
    /// message id into the zone's dialog table, the entity it is about, the numbers the text substitutes, and how to show
    /// it. <see cref="Strings"/> holds the strings the text's 0x1C n codes read (0x027's String1 / String2, 0x043's name),
    /// null for the packets without any.
    /// </summary>
    public sealed record DialogMessageInfo(
        ushort MessageId,
        uint UniqueNo,
        ushort ActIndex,
        bool HideName,
        byte Type,
        int[] Numbers,
        string Name,
        string[]? Strings = null
    );

    /// <summary>
    /// A Mog House furniture or plant operation result (S2C 0x0FA): the furnishing's item id, what happened, and
    /// where the item is held.
    /// </summary>
    public sealed record MyRoomOperationInfo(ushort ItemId, MyRoomOperationResult Result, byte ItemIndex, ContainerId Container);

    /// <summary>
    /// Thread-safe active state container for character story progression, key items,
    /// missions, merits, job points, Records of Eminence, mog house, conquest, and minigames.
    /// </summary>
    public sealed class ProgressionState
    {
        private readonly object _lock = new object();

        #region Cutscene & Dialog Event State

        public CutsceneEventInfo? ActiveEvent { get; private set; }
        public bool IsInEvent => ActiveEvent != null;

        #endregion

        #region Key Items (Scenario Items)

        // 64 DWORDs per table = 2048 key items per table
        private readonly Dictionary<ushort, uint[]> _acquiredKeyItems = new Dictionary<ushort, uint[]>();
        private readonly Dictionary<ushort, uint[]> _seenKeyItems = new Dictionary<ushort, uint[]>();

        #endregion

        #region Story & Missions

        public uint NationId { get; private set; }
        public uint NationMissionId { get; private set; }
        public uint ExpansionRotZ { get; private set; }
        public uint ExpansionCoP { get; private set; }
        public uint ExpansionCoP2 { get; private set; }
        public ushort ExpansionAddons { get; private set; }
        public ushort TalesBeginning { get; private set; }
        public uint ExpansionSoA { get; private set; }
        public uint ExpansionRoV { get; private set; }

        #endregion

        #region Merits & Job Points

        private readonly Dictionary<ushort, (byte Next, byte Count)> _merits = new Dictionary<ushort, (byte, byte)>();
        private readonly Dictionary<(int JobNo, int Index), (int Next, int Level)> _jobPoints = new Dictionary<(int, int), (int, int)>();

        #endregion

        #region Records of Eminence (RoE)

        private readonly Dictionary<ushort, uint> _activeRoeObjectives = new Dictionary<ushort, uint>();
        private readonly byte[] _completedRoeBits = new byte[S2C_0x112_RoeLog.ChunkSize * S2C_0x112_RoeLog.ChunkCount]; // 4096 records

        #endregion

        #region Mog House

        public bool IsInMogHouse { get; internal set; }
        public bool MogMenuPending { get; internal set; }
        public byte LastMyRoomResult { get; private set; }

        /// <summary>The last furniture or plant operation result in the Mog House (S2C 0x0FA), or null.</summary>
        public MyRoomOperationInfo? LastMyRoomOperation { get; private set; }

        #endregion

        #region Conquest & Besieged

        public byte ConquestBalance { get; private set; }
        public byte ConquestAlliance { get; private set; }
        public uint ConquestPoints { get; private set; }
        public uint ImperialStanding { get; private set; }
        public byte ConquestNextTally { get; private set; }
        public byte AstralCandescence { get; private set; }
        public byte AlZahbiOrders { get; private set; }
        public BesiegedStronghold MamookStronghold { get; private set; }
        public BesiegedStronghold HalvungStronghold { get; private set; }
        public BesiegedStronghold ArrapagoStronghold { get; private set; }
        private readonly ConquestRegion[] _regions = new ConquestRegion[S2C_0x05E_Conquest.RegionCount];

        #endregion

        #region Fishing Mini-Game

        public bool IsFishingActive { get; private set; }
        public ushort FishStamina { get; private set; }
        public ushort FishTimeRemaining { get; private set; }
        public ushort FishRegen { get; private set; }
        public ushort FishArrowDamage { get; private set; }
        public byte FishAnglerSense { get; private set; }
        public uint FishIntuition { get; private set; }

        #endregion

        #region Chocobo Racing

        /// <summary>Quinella pairs on a toteboard: 8 entrants choose 2.</summary>
        public const int ToteboardPairCount = 28;

        /// <summary>The last toteboard's slot index (usually mirrors the C2S 0x09B param).</summary>
        public uint ToteboardSlotIndex { get; private set; }

        /// <summary>The last toteboard's race ident: <c>(grade &lt;&lt; 18) | raceNumber</c>.</summary>
        public uint ToteboardIdent { get; private set; }

        private readonly ushort[] _toteboardOdds = new ushort[ToteboardPairCount];

        #endregion

        #region Unity Concord

        public uint UnitySparks { get; private set; }
        public ushort UnityDeeds { get; private set; }
        public ushort UnityPlaudits { get; private set; }
        public byte RoEUnityShared { get; private set; }
        public byte RoEUnityLeader { get; private set; }

        /// <summary>Unity rankings for the previous (index 0) and current (index 1) week from S2C 0x063 type 0x07.</summary>
        public UnityWeekInfo[] UnityWeeks { get; } = { new UnityWeekInfo(), new UnityWeekInfo() };

        #endregion

        #region Misc Data (S2C 0x063)

        public ushort LimitPoints { get; private set; }
        public int MeritPoints { get; private set; }
        public byte MaxMeritPoints { get; private set; }
        public int BluSpellPointBonus { get; private set; }
        public bool CanUseMeritMode { get; private set; }
        public bool XpCappedOrMeritMode { get; private set; }
        public bool MeritModeEnabled { get; private set; }

        /// <summary>Whether job points are unlocked (S2C 0x063 type 0x05).</summary>
        public bool JobPointsUnlocked { get; private set; }

        private readonly (ushort Capacity, ushort Points, ushort Spent)[] _jobPointTotals =
            new (ushort, ushort, ushort)[S2C_0x063_MiscData.JobPointJobCount];

        private readonly uint[] _teleportMasks = new uint[S2C_0x063_MiscData.TeleportMaskWordCount];

        #endregion

        #region Events

        public event Action<CutsceneEventInfo>? EventStarted;
        public event Action? EventEnded;

        /// <summary>A zone dialog message to print (S2C 0x036 / 0x02A).</summary>
        public event Action<DialogMessageInfo>? DialogMessageReceived;

        /// <summary>The server answered a pending event update (S2C 0x052 mode 1): the event script may go on.</summary>
        public event Action? EventUpdateAcknowledged;

        /// <summary>
        /// New numbers for the running event (S2C 0x05C): the eight values the client copies into the event work zone from
        /// index 2, where the event scripts read their parameters.
        /// </summary>
        public event Action<int[]>? EventNumbersUpdated;

        /// <summary>New strings for the running event (S2C 0x05D): the four strings the 0x1C n dialog codes read.</summary>
        public event Action<string[]>? EventStringsUpdated;

        /// <summary>The server cancelled the running event (S2C 0x052 mode 2).</summary>
        public event Action? EventCancelledByServer;
        public event Action? KeyItemsUpdated;
        public event Action? MissionsUpdated;
        public event Action? MeritsUpdated;
        public event Action? JobPointsUpdated;
        public event Action? RecordsOfEminenceUpdated;
        public event Action? MogHouseUpdated;
        public event Action? ConquestUpdated;
        public event Action? FishingUpdated;
        public event Action? ToteboardUpdated;
        public event Action? UnityUpdated;
        public event Action? TeleportMasksUpdated;

        /// <summary>The unlocked mounts changed (S2C 0x0AE).</summary>
        public event Action? MountsUpdated;

        /// <summary>The Moblin Maze Mongers vouchers or runes changed (S2C 0x0AD).</summary>
        public event Action? MazeUnlocksUpdated;

        /// <summary>The Alter Ego (Trust) points or upgrades changed (S2C 0x08E).</summary>
        public event Action? AlterEgoPointsUpdated;

        #endregion

        #region Mounts, Moblin Maze Mongers, Alter Ego points (S2C 0x0AE / 0x0AD / 0x08E)

        private readonly byte[] _mountTable = new byte[S2C_0x0AE_MountData.TableLength];
        private readonly byte[] _mazeVouchers = new byte[S2C_0x0AD_Dungeon.VoucherBytes];
        private readonly byte[] _mazeRunes = new byte[S2C_0x0AD_Dungeon.RuneBytes];
        private readonly byte[] _alterEgoUpgrades = new byte[S2C_0x08E_AlterEgoPoints.CategorySlots];
        private readonly ushort[] _alterEgoCosts = new ushort[S2C_0x08E_AlterEgoPoints.CategorySlots];

        /// <summary>True once an S2C 0x0AE arrived.</summary>
        public bool HasMountData { get; private set; }

        /// <summary>The character's Alter Ego (Trust) points (S2C 0x08E).</summary>
        public ushort AlterEgoPoints { get; private set; }

        /// <summary>Stores the unlocked mount table of S2C 0x0AE.</summary>
        public void UpdateMounts(in S2C_0x0AE_MountData mounts)
        {
            if (!mounts.IsValid) return;
            lock (_lock)
            {
                mounts.MountTable.CopyTo(_mountTable);
                HasMountData = true;
            }
            MountsUpdated?.Invoke();
        }

        /// <summary>True when mount <paramref name="mountIndex"/> (0 = Chocobo, the mount names DAT order) is unlocked.</summary>
        public bool HasMount(int mountIndex)
        {
            lock (_lock) return LoginDataBits.Test(_mountTable, mountIndex);
        }

        /// <summary>The unlocked mount indices, lowest first.</summary>
        public IReadOnlyList<int> GetUnlockedMounts() => CollectBits(_mountTable);

        /// <summary>Stores the Moblin Maze Mongers vouchers and runes of S2C 0x0AD.</summary>
        public void UpdateMazeUnlocks(in S2C_0x0AD_Dungeon dungeon)
        {
            if (!dungeon.IsValid) return;
            lock (_lock)
            {
                dungeon.Vouchers.CopyTo(_mazeVouchers);
                dungeon.Runes.CopyTo(_mazeRunes);
            }
            MazeUnlocksUpdated?.Invoke();
        }

        /// <summary>True when Maze Voucher <paramref name="index"/> (item 28736 + index) is unlocked.</summary>
        public bool HasMazeVoucher(int index)
        {
            lock (_lock) return LoginDataBits.Test(_mazeVouchers, index);
        }

        /// <summary>True when Maze Rune <paramref name="index"/> (item 28800 + index) is unlocked.</summary>
        public bool HasMazeRune(int index)
        {
            lock (_lock) return LoginDataBits.Test(_mazeRunes, index);
        }

        /// <summary>The unlocked Maze Voucher item ids.</summary>
        public IReadOnlyList<ushort> GetMazeVoucherItemIds() => ToItemIds(CollectBits(_mazeVouchers), S2C_0x0AD_Dungeon.FirstVoucherItemId);

        /// <summary>The unlocked Maze Rune item ids.</summary>
        public IReadOnlyList<ushort> GetMazeRuneItemIds() => ToItemIds(CollectBits(_mazeRunes), S2C_0x0AD_Dungeon.FirstRuneItemId);

        /// <summary>Stores the Alter Ego points and per-category upgrades of S2C 0x08E.</summary>
        public void UpdateAlterEgoPoints(in S2C_0x08E_AlterEgoPoints points)
        {
            if (!points.IsValid) return;
            lock (_lock)
            {
                AlterEgoPoints = points.Points;
                for (int i = 0; i < S2C_0x08E_AlterEgoPoints.CategorySlots; i++)
                {
                    _alterEgoUpgrades[i] = points.GetUpgrade(i);
                    _alterEgoCosts[i] = points.GetNextCost(i);
                }
            }
            AlterEgoPointsUpdated?.Invoke();
        }

        /// <summary>The upgrade level of an Alter Ego category.</summary>
        public byte GetAlterEgoUpgrade(AlterEgoCategory category)
        {
            int i = (int)category;
            lock (_lock) return (uint)i < (uint)_alterEgoUpgrades.Length ? _alterEgoUpgrades[i] : (byte)0;
        }

        /// <summary>The points needed for the next upgrade of an Alter Ego category.</summary>
        public ushort GetAlterEgoNextCost(AlterEgoCategory category)
        {
            int i = (int)category;
            lock (_lock) return (uint)i < (uint)_alterEgoCosts.Length ? _alterEgoCosts[i] : (ushort)0;
        }

        private List<int> CollectBits(byte[] table)
        {
            var result = new List<int>();
            lock (_lock)
            {
                for (int i = 0; i < table.Length * 8; i++)
                {
                    if (LoginDataBits.Test(table, i)) result.Add(i);
                }
            }
            return result;
        }

        private static List<ushort> ToItemIds(List<int> bits, ushort firstItemId)
        {
            var ids = new List<ushort>(bits.Count);
            foreach (int bit in bits) ids.Add((ushort)(firstItemId + bit));
            return ids;
        }

        #endregion

        #region State Mutators

        public void StartEvent(uint uniqueNo, ushort actIndex, ushort eventNum, ushort eventPara, ushort mode,
            int[]? numericParams = null, string[]? stringParams = null, uint[]? dataParams = null, bool fromZoneIn = false)
        {
            CutsceneEventInfo info;
            lock (_lock)
            {
                info = new CutsceneEventInfo(
                    uniqueNo,
                    actIndex,
                    eventNum,
                    eventPara,
                    mode,
                    numericParams ?? new int[8],
                    stringParams ?? new string[4],
                    dataParams ?? new uint[8],
                    fromZoneIn
                );
                ActiveEvent = info;
            }

            EventStarted?.Invoke(info);
        }

        /// <summary>The last zone dialog message the server sent (S2C 0x036 / 0x02A), or null.</summary>
        public DialogMessageInfo? LastDialogMessage { get; private set; }

        public void PostDialogMessage(DialogMessageInfo message)
        {
            lock (_lock) LastDialogMessage = message;
            DialogMessageReceived?.Invoke(message);
        }

        public void AcknowledgeEventUpdate() => EventUpdateAcknowledged?.Invoke();

        /// <summary>
        /// Replaces the running event's numbers (S2C 0x05C) in <see cref="ActiveEvent"/> and raises
        /// <see cref="EventNumbersUpdated"/>. Raised also when no event runs: the client copies them into the work zone
        /// regardless (XiPackets 0x005C).
        /// </summary>
        public void UpdateEventNumbers(ReadOnlySpan<int> numbers)
        {
            var copy = numbers.ToArray();
            lock (_lock)
            {
                if (ActiveEvent != null) ActiveEvent = ActiveEvent with { NumericParams = (int[])copy.Clone() };
            }
            EventNumbersUpdated?.Invoke(copy);
        }

        /// <summary>Replaces the running event's strings (S2C 0x05D) in <see cref="ActiveEvent"/> and raises <see cref="EventStringsUpdated"/>.</summary>
        public void UpdateEventStrings(string[] strings)
        {
            ArgumentNullException.ThrowIfNull(strings);
            var copy = (string[])strings.Clone();
            lock (_lock)
            {
                if (ActiveEvent != null) ActiveEvent = ActiveEvent with { StringParams = (string[])copy.Clone() };
            }
            EventStringsUpdated?.Invoke(copy);
        }

        public void CancelEventByServer() => EventCancelledByServer?.Invoke();

        public void EndEvent()
        {
            bool wasActive;
            lock (_lock)
            {
                wasActive = ActiveEvent != null;
                ActiveEvent = null;
            }

            if (wasActive)
            {
                EventEnded?.Invoke();
            }
        }

        public void UpdateKeyItems(ushort tableIndex, ReadOnlySpan<uint> acquiredFlags, ReadOnlySpan<uint> seenFlags)
        {
            lock (_lock)
            {
                if (!_acquiredKeyItems.TryGetValue(tableIndex, out var acq))
                {
                    acq = new uint[16];
                    _acquiredKeyItems[tableIndex] = acq;
                }
                if (!_seenKeyItems.TryGetValue(tableIndex, out var seen))
                {
                    seen = new uint[16];
                    _seenKeyItems[tableIndex] = seen;
                }

                int len = Math.Min(16, acquiredFlags.Length);
                for (int i = 0; i < len; i++)
                {
                    acq[i] = acquiredFlags[i];
                }

                int seenLen = Math.Min(16, seenFlags.Length);
                for (int i = 0; i < seenLen; i++)
                {
                    seen[i] = seenFlags[i];
                }
            }

            KeyItemsUpdated?.Invoke();
        }

        public bool HasKeyItem(ushort keyItemId)
        {
            ushort tableIndex = (ushort)(keyItemId / 512);
            int bitIndex = keyItemId % 512;
            int dwordIndex = bitIndex / 32;
            int bit = bitIndex % 32;

            lock (_lock)
            {
                if (_acquiredKeyItems.TryGetValue(tableIndex, out var acq))
                {
                    if (dwordIndex < acq.Length)
                    {
                        return (acq[dwordIndex] & (1u << bit)) != 0;
                    }
                }
                return false;
            }
        }

        public bool IsKeyItemSeen(ushort keyItemId)
        {
            ushort tableIndex = (ushort)(keyItemId / 512);
            int bitIndex = keyItemId % 512;
            int dwordIndex = bitIndex / 32;
            int bit = bitIndex % 32;

            lock (_lock)
            {
                if (_seenKeyItems.TryGetValue(tableIndex, out var seen))
                {
                    if (dwordIndex < seen.Length)
                    {
                        return (seen[dwordIndex] & (1u << bit)) != 0;
                    }
                }
                return false;
            }
        }

        /// <summary>
        /// Marks a held key item as seen and copies its 512-item table's seen flags into
        /// <paramref name="tableSeenFlags"/> (16 words), ready for C2S 0x064. Returns false when the key item
        /// is not held or was already seen, in which case the client sends nothing.
        /// </summary>
        public bool TryMarkKeyItemSeen(ushort keyItemId, Span<uint> tableSeenFlags, out ushort tableIndex)
        {
            tableIndex = (ushort)(keyItemId / 512);
            int bitIndex = keyItemId % 512;
            int dwordIndex = bitIndex / 32;
            uint mask = 1u << (bitIndex % 32);

            lock (_lock)
            {
                if (!_acquiredKeyItems.TryGetValue(tableIndex, out var acq) || (acq[dwordIndex] & mask) == 0)
                {
                    return false;
                }
                if (!_seenKeyItems.TryGetValue(tableIndex, out var seen))
                {
                    seen = new uint[16];
                    _seenKeyItems[tableIndex] = seen;
                }
                if ((seen[dwordIndex] & mask) != 0)
                {
                    return false;
                }

                seen[dwordIndex] |= mask;
                seen.AsSpan(0, Math.Min(seen.Length, tableSeenFlags.Length)).CopyTo(tableSeenFlags);
            }

            KeyItemsUpdated?.Invoke();
            return true;
        }

        public void UpdateMissions(in S2C_0x056_Mission mission)
        {
            lock (_lock)
            {
                if (mission.IsMainPort)
                {
                    NationId = mission.Nation;
                    NationMissionId = mission.NationMission;
                    ExpansionRotZ = mission.ExpansionRotZ;
                    ExpansionCoP = mission.ExpansionCoP;
                    ExpansionCoP2 = mission.ExpansionCoP2;
                    ExpansionAddons = mission.ExpansionAddons;
                    TalesBeginning = mission.TalesBeginning;
                    ExpansionSoA = mission.ExpansionSoA;
                    ExpansionRoV = mission.ExpansionRoV;
                }
            }

            MissionsUpdated?.Invoke();
        }

        public void UpdateMerits(in S2C_0x08C_Merit merit)
        {
            lock (_lock)
            {
                for (int i = 0; i < merit.EntryCount; i++)
                {
                    var entry = merit.GetMeritEntry(i);
                    if (S2C_0x08C_Merit.IsRemoval(entry.Index))
                    {
                        _merits.Remove((ushort)(entry.Index - 1));
                    }
                    else
                    {
                        _merits[entry.Index] = (entry.Next, entry.Count);
                    }
                }
            }

            MeritsUpdated?.Invoke();
        }

        public byte GetMeritLevel(ushort meritIndex)
        {
            lock (_lock)
            {
                return _merits.TryGetValue(meritIndex, out var val) ? val.Count : (byte)0;
            }
        }

        public void UpdateJobPoints(in S2C_0x08D_JobPoints jp)
        {
            lock (_lock)
            {
                for (int i = 0; i < S2C_0x08D_JobPoints.MaxEntries; i++)
                {
                    var entry = jp.GetJobPoint(i);
                    if (entry.JobNo != 0 || entry.Level > 0)
                    {
                        _jobPoints[(entry.JobNo, entry.Index)] = (entry.Next, entry.Level);
                    }
                }
            }

            JobPointsUpdated?.Invoke();
        }

        public int GetJobPointLevel(int jobNo, int index)
        {
            lock (_lock)
            {
                return _jobPoints.TryGetValue((jobNo, index), out var val) ? val.Level : 0;
            }
        }

        public void UpdateRoeActiveLog(in S2C_0x111_RoeActiveLog roe)
        {
            lock (_lock)
            {
                _activeRoeObjectives.Clear();
                for (int i = 0; i < S2C_0x111_RoeActiveLog.MaxEntries; i++)
                {
                    var obj = roe.GetActiveObjective(i);
                    if (obj.ObjectiveId != 0)
                    {
                        _activeRoeObjectives[obj.ObjectiveId] = obj.Progress;
                    }
                }
            }

            RecordsOfEminenceUpdated?.Invoke();
        }

        public void UpdateRoeLogChunk(in S2C_0x112_RoeLog logChunk)
        {
            lock (_lock)
            {
                int offset = logChunk.ByteOffset;
                var data = logChunk.GetData();
                if (offset + data.Length <= _completedRoeBits.Length)
                {
                    data.CopyTo(_completedRoeBits.AsSpan(offset, data.Length));
                }
            }

            RecordsOfEminenceUpdated?.Invoke();
        }

        public bool IsRoeObjectiveCompleted(ushort objectiveId)
        {
            int byteIndex = objectiveId / 8;
            int bit = objectiveId % 8;

            lock (_lock)
            {
                if (byteIndex < _completedRoeBits.Length)
                {
                    return (_completedRoeBits[byteIndex] & (1 << bit)) != 0;
                }
                return false;
            }
        }

        public uint GetRoeObjectiveProgress(ushort objectiveId)
        {
            lock (_lock)
            {
                return _activeRoeObjectives.TryGetValue(objectiveId, out uint prog) ? prog : 0;
            }
        }

        public IReadOnlyDictionary<ushort, uint> GetActiveRoeObjectives()
        {
            lock (_lock)
            {
                return new Dictionary<ushort, uint>(_activeRoeObjectives);
            }
        }

        public void UpdateConquest(in S2C_0x05E_Conquest conquest)
        {
            lock (_lock)
            {
                ConquestBalance = conquest.Balance;
                ConquestAlliance = conquest.Alliance;
                ConquestPoints = conquest.ConquestPoints;
                ImperialStanding = conquest.ImperialStanding;
                ConquestNextTally = conquest.NextTally;
                AstralCandescence = conquest.AstralCandescence;
                AlZahbiOrders = conquest.AlZahbiOrders;
                MamookStronghold = conquest.Mamook;
                HalvungStronghold = conquest.Halvung;
                ArrapagoStronghold = conquest.Arrapago;

                for (int i = 0; i < _regions.Length; i++)
                {
                    _regions[i] = conquest.GetRegion(i);
                }
            }

            ConquestUpdated?.Invoke();
        }

        public ConquestRegion GetRegionConquest(int regionIndex)
        {
            lock (_lock)
            {
                if (regionIndex >= 0 && regionIndex < _regions.Length)
                {
                    return _regions[regionIndex];
                }
                return default;
            }
        }

        public void UpdateFishing(in S2C_0x115_Fish fish)
        {
            lock (_lock)
            {
                IsFishingActive = true;
                FishStamina = fish.Stamina;
                FishTimeRemaining = fish.Time;
                FishRegen = fish.Regen;
                FishArrowDamage = fish.ArrowDamage;
                FishAnglerSense = fish.AnglerSense;
                FishIntuition = fish.Intuition;
            }

            FishingUpdated?.Invoke();
        }

        public void ClearFishing()
        {
            lock (_lock)
            {
                IsFishingActive = false;
                FishStamina = 0;
                FishTimeRemaining = 0;
            }

            FishingUpdated?.Invoke();
        }

        public void UpdateToteboard(in S2C_0x073_ChocoboToteboard toteboard)
        {
            lock (_lock)
            {
                ToteboardSlotIndex = toteboard.SlotIndex;
                ToteboardIdent = toteboard.Ident;
                for (int i = 0; i < ToteboardPairCount; i++)
                    _toteboardOdds[i] = toteboard.GetOdds(i);
            }

            ToteboardUpdated?.Invoke();
        }

        /// <summary>The odds for quinella pair <paramref name="pairIndex"/> (0-27) from the last toteboard, or 0.</summary>
        public ushort GetToteboardOdds(int pairIndex)
        {
            if (pairIndex < 0 || pairIndex >= ToteboardPairCount) return 0;
            lock (_lock)
            {
                return _toteboardOdds[pairIndex];
            }
        }

        public void UpdateUnity(in S2C_0x110_Unity unity)
        {
            lock (_lock)
            {
                UnitySparks = unity.Sparks;
                UnityDeeds = unity.Deeds;
                UnityPlaudits = unity.Plaudits;
                RoEUnityShared = unity.RoEUnityShared;
                RoEUnityLeader = unity.RoEUnityLeader;
            }

            UnityUpdated?.Invoke();
        }

        /// <summary>Applies S2C 0x063 type 0x02: limit points, merit points and the merit mode flags.</summary>
        public void UpdateMiscMerits(in S2C_0x063_MiscData misc)
        {
            lock (_lock)
            {
                LimitPoints = misc.LimitPoints;
                MeritPoints = misc.MeritPoints;
                MaxMeritPoints = misc.MaxMeritPoints;
                BluSpellPointBonus = misc.BluBonus;
                CanUseMeritMode = misc.CanUseMeritMode;
                XpCappedOrMeritMode = misc.XpCappedOrMeritMode;
                MeritModeEnabled = misc.MeritModeEnabled;
            }

            MeritsUpdated?.Invoke();
        }

        /// <summary>Applies S2C 0x063 type 0x05: per-job capacity points, job points and points spent.</summary>
        public void UpdateMiscJobPoints(in S2C_0x063_MiscData misc)
        {
            lock (_lock)
            {
                JobPointsUnlocked = misc.JobPointsUnlocked;
                for (int job = 0; job < _jobPointTotals.Length; job++)
                {
                    _jobPointTotals[job] = misc.GetJobPointsEntry(job);
                }
            }

            JobPointsUpdated?.Invoke();
        }

        /// <summary>Capacity points, unspent job points and points spent for a job id (1-23).</summary>
        public (ushort Capacity, ushort Points, ushort Spent) GetJobPointTotals(int jobNo)
        {
            if ((uint)jobNo >= _jobPointTotals.Length) return default;
            lock (_lock) return _jobPointTotals[jobNo];
        }

        /// <summary>Applies S2C 0x063 type 0x06: the home point, survival guide, waypoint and other unlock masks.</summary>
        public void UpdateTeleportMasks(in S2C_0x063_MiscData misc)
        {
            lock (_lock)
            {
                for (int i = 0; i < _teleportMasks.Length; i++) _teleportMasks[i] = misc.GetTeleportMaskWord(i);
            }

            TeleportMasksUpdated?.Invoke();
        }

        /// <summary>Whether a home point is unlocked (bit index across mask words 0-3).</summary>
        public bool HasHomePoint(int bit) => HasTeleportBit(0, 4, bit);

        /// <summary>Whether a survival guide is unlocked (words 4-7).</summary>
        public bool HasSurvivalGuide(int bit) => HasTeleportBit(4, 4, bit);

        /// <summary>Whether a waypoint is unlocked (words 8-11).</summary>
        public bool HasWaypoint(int bit) => HasTeleportBit(8, 4, bit);

        /// <summary>Whether a telepoint is unlocked (word 12).</summary>
        public bool HasTelepoint(int bit) => HasTeleportBit(12, 1, bit);

        /// <summary>Whether an atmacite teleport is unlocked (word 13).</summary>
        public bool HasAtmos(int bit) => HasTeleportBit(13, 1, bit);

        /// <summary>Whether an eschan portal is unlocked (word 14).</summary>
        public bool HasEschanPortal(int bit) => HasTeleportBit(14, 1, bit);

        private bool HasTeleportBit(int firstWord, int wordCount, int bit)
        {
            if (bit < 0 || bit >= wordCount * 32) return false;
            lock (_lock) return (_teleportMasks[firstWord + bit / 32] & (1u << (bit % 32))) != 0;
        }

        /// <summary>Applies one S2C 0x063 type 0x07 Unity block (base, members, points or personal) to its week.</summary>
        public void UpdateMiscUnity(in S2C_0x063_MiscData misc)
        {
            var week = UnityWeeks[misc.UnityCurrentWeek ? 1 : 0];
            lock (_lock)
            {
                switch (misc.UnityDataType)
                {
                    case 0x00:
                        week.FinalizedTimestamp = misc.UnityBaseTimestamp;
                        break;
                    case 0x01:
                        for (int i = 0; i < S2C_0x063_MiscData.UnityCount; i++) week.Members[i] = misc.GetUnityValue(i);
                        break;
                    case 0x02:
                        for (int i = 0; i < S2C_0x063_MiscData.UnityCount; i++) week.Points[i] = misc.GetUnityValue(i);
                        break;
                    case 0x14:
                        week.PersonalRankingPoints = misc.UnityPersonalPoints;
                        break;
                }
            }

            UnityUpdated?.Invoke();
        }

        public void SetMyRoomOperation(MyRoomOperationInfo operation)
        {
            lock (_lock) LastMyRoomOperation = operation;
            MogHouseUpdated?.Invoke();
        }

        public void SetMyRoomResult(byte result)
        {
            lock (_lock)
            {
                LastMyRoomResult = result;
            }

            MogHouseUpdated?.Invoke();
        }

        #endregion
    }

    /// <summary>One week of Unity rankings (S2C 0x063 type 0x07). The arrays are indexed by Unity 0-10.</summary>
    public sealed class UnityWeekInfo
    {
        /// <summary>Earth seconds since the Vana'diel epoch when the rankings were finalized; 0 for the running week.</summary>
        public uint FinalizedTimestamp { get; internal set; }
        public uint[] Members { get; } = new uint[S2C_0x063_MiscData.UnityCount];
        public uint[] Points { get; } = new uint[S2C_0x063_MiscData.UnityCount];
        public ushort PersonalRankingPoints { get; internal set; }
    }
}
