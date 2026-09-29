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
        uint[] DataParams
    );

    /// <summary>
    /// A zone dialog message the server asked the client to print (S2C 0x036 / 0x02A): the message id into the
    /// zone's dialog table, the entity it is about, the numbers the text substitutes, and how to show it.
    /// </summary>
    public sealed record DialogMessageInfo(
        ushort MessageId,
        uint UniqueNo,
        ushort ActIndex,
        bool HideName,
        byte Type,
        int[] Numbers,
        string Name
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

        public ushort TotalMeritPoints { get; private set; }
        private readonly Dictionary<ushort, (byte Next, byte Count)> _merits = new Dictionary<ushort, (byte, byte)>();
        private readonly Dictionary<(int JobNo, int Index), (int Next, int Level)> _jobPoints = new Dictionary<(int, int), (int, int)>();

        #endregion

        #region Records of Eminence (RoE)

        private readonly Dictionary<ushort, uint> _activeRoeObjectives = new Dictionary<ushort, uint>();
        private readonly byte[] _completedRoeBits = new byte[1024]; // 8192 records capacity

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
        private readonly (byte Owner, byte Ranking)[] _regions = new (byte, byte)[27];

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

        #endregion

        #region Events

        public event Action<CutsceneEventInfo>? EventStarted;
        public event Action? EventEnded;

        /// <summary>A zone dialog message to print (S2C 0x036 / 0x02A).</summary>
        public event Action<DialogMessageInfo>? DialogMessageReceived;

        /// <summary>The server answered a pending event update (S2C 0x052 mode 1): the event script may go on.</summary>
        public event Action? EventUpdateAcknowledged;

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

        #endregion

        #region State Mutators

        public void StartEvent(uint uniqueNo, ushort actIndex, ushort eventNum, ushort eventPara, ushort mode,
            int[]? numericParams = null, string[]? stringParams = null, uint[]? dataParams = null)
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
                    dataParams ?? new uint[8]
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
                TotalMeritPoints = merit.MeritCount;
                for (int i = 0; i < 61; i++)
                {
                    var entry = merit.GetMeritEntry(i);
                    if (entry.Index != 0 || entry.Count > 0)
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
                int offset = logChunk.Offset;
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

                for (int i = 0; i < 27; i++)
                {
                    _regions[i] = conquest.GetRegionInfo(i);
                }
            }

            ConquestUpdated?.Invoke();
        }

        public (byte Owner, byte Ranking) GetRegionConquest(int regionIndex)
        {
            lock (_lock)
            {
                if (regionIndex >= 0 && regionIndex < _regions.Length)
                {
                    return _regions[regionIndex];
                }
                return (0, 0);
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
}
