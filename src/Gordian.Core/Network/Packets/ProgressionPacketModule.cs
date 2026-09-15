// src/Gordian.Core/Network/Packets/ProgressionPacketModule.cs
using System;
using System.Buffers.Binary;
using System.Numerics;
using System.Threading.Tasks;
using Gordian.Core.Diagnostics;
using Gordian.Core.World;

namespace Gordian.Core.Network.Packets
{
    /// <summary>
    /// Packet domain module managing story cutscenes, missions, quests, key items,
    /// mog house menus, merits, job points, Records of Eminence, conquest, and minigames.
    /// Operates zero-allocation on the inbound pipeline.
    /// </summary>
    public sealed class ProgressionPacketModule
    {
        private readonly ProgressionState _progressionState;
        private readonly LocalPlayerState _localPlayerState;
        private readonly Func<ReadOnlyMemory<byte>, bool, Task> _sendChunkCallback;
        private readonly Action<PacketDirection, ushort, ushort, ReadOnlySpan<byte>>? _logPacketCallback;
        private ushort _sequenceNumber;

        public ProgressionState State => _progressionState;
        public bool LogOutboundOnRoute { get; set; } = true;

        public ProgressionPacketModule(
            ProgressionState progressionState,
            LocalPlayerState localPlayerState,
            Func<ReadOnlyMemory<byte>, bool, Task> sendChunkCallback,
            Action<PacketDirection, ushort, ushort, ReadOnlySpan<byte>>? logPacketCallback = null)
        {
            _progressionState = progressionState ?? throw new ArgumentNullException(nameof(progressionState));
            _localPlayerState = localPlayerState ?? throw new ArgumentNullException(nameof(localPlayerState));
            _sendChunkCallback = sendChunkCallback ?? throw new ArgumentNullException(nameof(sendChunkCallback));
            _logPacketCallback = logPacketCallback;
        }

        public void Register(IPacketDispatcher dispatcher)
        {
            ArgumentNullException.ThrowIfNull(dispatcher);

            dispatcher.Register(S2C_0x032_Event.PacketId, HandleEvent);
            dispatcher.Register(S2C_0x033_EventStr.PacketId, HandleEventStr);
            dispatcher.Register(S2C_0x034_EventNum.PacketId, HandleEventNum);
            dispatcher.Register(S2C_0x036_TalkNum.PacketId, HandleTalkNum);
            dispatcher.Register(S2C_0x052_EventUcOff.PacketId, HandleEventUcOff);
            dispatcher.Register(S2C_0x055_ScenarioItem.PacketId, HandleScenarioItem);
            dispatcher.Register(S2C_0x056_Mission.PacketId, HandleMission);
            dispatcher.Register(S2C_0x02E_OpenMogMenu.PacketId, HandleOpenMogMenu);
            dispatcher.Register(S2C_0x096_MyRoomEnter.PacketId, HandleMyRoomEnter);
            dispatcher.Register(S2C_0x0FA_MyRoomOperation.PacketId, HandleMyRoomOperation);
            dispatcher.Register(S2C_0x08C_Merit.PacketId, HandleMerit);
            dispatcher.Register(S2C_0x08D_JobPoints.PacketId, HandleJobPoints);
            dispatcher.Register(S2C_0x111_RoeActiveLog.PacketId, HandleRoeActiveLog);
            dispatcher.Register(S2C_0x112_RoeLog.PacketId, HandleRoeLog);
            dispatcher.Register(S2C_0x05E_Conquest.PacketId, HandleConquest);
            dispatcher.Register(S2C_0x115_Fish.PacketId, HandleFish);
            dispatcher.Register(S2C_0x110_Unity.PacketId, HandleUnity);
        }

        public void Unregister(IPacketDispatcher dispatcher)
        {
            ArgumentNullException.ThrowIfNull(dispatcher);

            dispatcher.Unregister(S2C_0x032_Event.PacketId);
            dispatcher.Unregister(S2C_0x033_EventStr.PacketId);
            dispatcher.Unregister(S2C_0x034_EventNum.PacketId);
            dispatcher.Unregister(S2C_0x036_TalkNum.PacketId);
            dispatcher.Unregister(S2C_0x052_EventUcOff.PacketId);
            dispatcher.Unregister(S2C_0x055_ScenarioItem.PacketId);
            dispatcher.Unregister(S2C_0x056_Mission.PacketId);
            dispatcher.Unregister(S2C_0x02E_OpenMogMenu.PacketId);
            dispatcher.Unregister(S2C_0x096_MyRoomEnter.PacketId);
            dispatcher.Unregister(S2C_0x0FA_MyRoomOperation.PacketId);
            dispatcher.Unregister(S2C_0x08C_Merit.PacketId);
            dispatcher.Unregister(S2C_0x08D_JobPoints.PacketId);
            dispatcher.Unregister(S2C_0x111_RoeActiveLog.PacketId);
            dispatcher.Unregister(S2C_0x112_RoeLog.PacketId);
            dispatcher.Unregister(S2C_0x05E_Conquest.PacketId);
            dispatcher.Unregister(S2C_0x115_Fish.PacketId);
            dispatcher.Unregister(S2C_0x110_Unity.PacketId);
        }

        #region Inbound Handlers

        private void HandleEvent(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var evt = new S2C_0x032_Event(payload);
            if (!evt.IsValid) return;

            GordianLog.Info("EVENT", $"Cutscene started: EventNum={evt.EventNum}, ActIndex={evt.ActIndex}, Mode={evt.Mode}");
            _progressionState.StartEvent(evt.UniqueNo, evt.ActIndex, evt.EventNum, evt.EventPara, evt.Mode);
        }

        private void HandleEventStr(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var evt = new S2C_0x033_EventStr(payload);
            if (!evt.IsValid) return;

            var strings = new string[4];
            for (int i = 0; i < 4; i++) strings[i] = evt.GetStringParam(i);

            var data = new uint[8];
            for (int i = 0; i < 8; i++) data[i] = evt.GetDataParam(i);

            GordianLog.Info("EVENT", $"Cutscene (Str) started: EventNum={evt.EventNum}, ActIndex={evt.ActIndex}");
            _progressionState.StartEvent(evt.UniqueNo, evt.ActIndex, evt.EventNum, evt.EventPara, evt.Mode,
                null, strings, data);
        }

        private void HandleEventNum(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var evt = new S2C_0x034_EventNum(payload);
            if (!evt.IsValid) return;

            var nums = new int[8];
            for (int i = 0; i < 8; i++) nums[i] = evt.GetNumericParam(i);

            GordianLog.Info("EVENT", $"Cutscene (Num) started: EventNum={evt.EventNum}, ActIndex={evt.ActIndex}");
            _progressionState.StartEvent(evt.UniqueNo, evt.ActIndex, evt.EventNum, evt.EventPara, evt.Mode,
                nums, null, null);
        }

        private void HandleTalkNum(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var talk = new S2C_0x036_TalkNum(payload);
            if (!talk.IsValid) return;

            GordianLog.Debug("DIALOG", $"TalkNum message received: MessageId={talk.MessageId}, ActIndex={talk.ActIndex}");
        }

        private void HandleEventUcOff(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var ucoff = new S2C_0x052_EventUcOff(payload);
            if (!ucoff.IsValid) return;

            GordianLog.Info("EVENT", $"Event user control release: Mode={ucoff.Mode}");
            if (ucoff.Mode == EventUcOffMode.CancelEvent || ucoff.Mode == EventUcOffMode.Standard)
            {
                _progressionState.EndEvent();
            }
            else if (ucoff.Mode == EventUcOffMode.Fishing)
            {
                _progressionState.ClearFishing();
            }
        }

        private void HandleScenarioItem(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var scenario = new S2C_0x055_ScenarioItem(payload);
            if (!scenario.IsValid) return;

            Span<uint> acquired = stackalloc uint[16];
            Span<uint> seen = stackalloc uint[16];

            for (int i = 0; i < 16; i++)
            {
                acquired[i] = scenario.GetAcquiredFlag(i);
                seen[i] = scenario.GetSeenFlag(i);
            }

            _progressionState.UpdateKeyItems(scenario.TableIndex, acquired, seen);
            GordianLog.Debug("PROGRESSION", $"Updated Key Items for Table {scenario.TableIndex}");
        }

        private void HandleMission(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var mission = new S2C_0x056_Mission(payload);
            if (!mission.IsValid) return;

            _progressionState.UpdateMissions(in mission);
            GordianLog.Debug("PROGRESSION", $"Updated Mission Log (Port=0x{mission.Port:X4})");
        }

        private void HandleOpenMogMenu(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            _progressionState.IsInMogHouse = true;
            _progressionState.MogMenuPending = true;
            GordianLog.Info("MOGHOUSE", "Mog House menu opened.");
        }

        private void HandleMyRoomEnter(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var enter = new S2C_0x096_MyRoomEnter(payload);
            if (!enter.IsValid) return;

            _progressionState.SetMyRoomResult(enter.Result);
            GordianLog.Info("MOGHOUSE", $"MyRoom Enter response: Result={enter.Result}");
        }

        private void HandleMyRoomOperation(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var op = new S2C_0x0FA_MyRoomOperation(payload);
            if (!op.IsValid) return;

            GordianLog.Debug("MOGHOUSE", $"MyRoom Operation: Item={op.MyroomItemNo}, Result={op.Result}");
        }

        private void HandleMerit(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var merit = new S2C_0x08C_Merit(payload);
            if (!merit.IsValid) return;

            _progressionState.UpdateMerits(in merit);
            GordianLog.Debug("PROGRESSION", $"Updated Merits: TotalPoints={merit.MeritCount}");
        }

        private void HandleJobPoints(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var jp = new S2C_0x08D_JobPoints(payload);
            if (!jp.IsValid) return;

            _progressionState.UpdateJobPoints(in jp);
            GordianLog.Debug("PROGRESSION", "Updated Job Points table.");
        }

        private void HandleRoeActiveLog(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var roe = new S2C_0x111_RoeActiveLog(payload);
            if (!roe.IsValid) return;

            _progressionState.UpdateRoeActiveLog(in roe);
            GordianLog.Debug("ROE", "Updated Records of Eminence active objectives.");
        }

        private void HandleRoeLog(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var chunk = new S2C_0x112_RoeLog(payload);
            if (!chunk.IsValid) return;

            _progressionState.UpdateRoeLogChunk(in chunk);
            GordianLog.Debug("ROE", $"Updated Records of Eminence completed log at offset {chunk.Offset}");
        }

        private void HandleConquest(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var conquest = new S2C_0x05E_Conquest(payload);
            if (!conquest.IsValid) return;

            _progressionState.UpdateConquest(in conquest);
            GordianLog.Debug("CONQUEST", $"Updated Conquest data: Balance={conquest.Balance}, CP={conquest.ConquestPoints}");
        }

        private void HandleFish(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var fish = new S2C_0x115_Fish(payload);
            if (!fish.IsValid) return;

            _progressionState.UpdateFishing(in fish);
            GordianLog.Info("FISHING", $"Fishing battle began: Stamina={fish.Stamina}, Time={fish.Time}");
        }

        private void HandleUnity(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var unity = new S2C_0x110_Unity(payload);
            if (!unity.IsValid) return;

            _progressionState.UpdateUnity(in unity);
            GordianLog.Debug("UNITY", $"Updated Unity status: Sparks={unity.Sparks}, Deeds={unity.Deeds}");
        }

        #endregion

        #region Outbound Dispatch Methods

        private ushort NextSequence() => unchecked(++_sequenceNumber);

        public Task SendEventEndAsync(uint uniqueNo, uint endPara, ushort actIndex, ushort mode, ushort eventNum, ushort eventPara)
        {
            byte[] packet = ProgressionPacketBuilder.BuildEventEnd(uniqueNo, endPara, actIndex, mode, eventNum, eventPara, NextSequence());
            LogOutbound(0x05B, packet);
            _progressionState.EndEvent();
            return _sendChunkCallback(packet, true);
        }

        public Task SendEventEndXzyAsync(Vector3 position, uint uniqueNo, uint endPara, ushort eventNum, ushort eventPara, ushort actIndex, byte mode, sbyte dir)
        {
            byte[] packet = ProgressionPacketBuilder.BuildEventEndXzy(position, uniqueNo, endPara, eventNum, eventPara, actIndex, mode, dir, NextSequence());
            LogOutbound(0x05C, packet);
            _progressionState.EndEvent();
            return _sendChunkCallback(packet, true);
        }

        public Task SendMyRoomJobChangeAsync(JobId mainJob, JobId subJob)
        {
            byte[] packet = ProgressionPacketBuilder.BuildMyRoomJob((byte)mainJob, (byte)subJob, NextSequence());
            LogOutbound(0x100, packet);
            return _sendChunkCallback(packet, true);
        }

        public Task SendMeritsAsync(MeritCommandKind kind, byte param1, ushort param2, uint param3 = 0)
        {
            byte[] packet = ProgressionPacketBuilder.BuildMerits(kind, param1, param2, param3, NextSequence());
            LogOutbound(0x0BE, packet);
            return _sendChunkCallback(packet, true);
        }

        public Task SendJobPointsSpendAsync(ushort index)
        {
            byte[] packet = ProgressionPacketBuilder.BuildJobPointsSpend(index, NextSequence());
            LogOutbound(0x0BF, packet);
            return _sendChunkCallback(packet, true);
        }

        public Task SendJobPointsReqAsync()
        {
            byte[] packet = ProgressionPacketBuilder.BuildJobPointsReq(NextSequence());
            LogOutbound(0x0C0, packet);
            return _sendChunkCallback(packet, true);
        }

        public Task SendRoeStartAsync(ushort objectiveId)
        {
            byte[] packet = ProgressionPacketBuilder.BuildRoeStart(objectiveId, NextSequence());
            LogOutbound(0x10C, packet);
            return _sendChunkCallback(packet, true);
        }

        public Task SendRoeRemoveAsync(ushort objectiveId)
        {
            byte[] packet = ProgressionPacketBuilder.BuildRoeRemove(objectiveId, NextSequence());
            LogOutbound(0x10D, packet);
            return _sendChunkCallback(packet, true);
        }

        public Task SendRoeClaimAsync(ushort objectiveId)
        {
            byte[] packet = ProgressionPacketBuilder.BuildRoeClaim(objectiveId, NextSequence());
            LogOutbound(0x10E, packet);
            return _sendChunkCallback(packet, true);
        }

        public Task SendFishingActionAsync(uint uniqueNo, int para, ushort actIndex, FishingActionMode mode, int para2 = 0)
        {
            byte[] packet = ProgressionPacketBuilder.BuildFishingAction(uniqueNo, para, actIndex, mode, para2, NextSequence());
            LogOutbound(0x110, packet);
            return _sendChunkCallback(packet, true);
        }

        public Task SendReqConquestAsync()
        {
            byte[] packet = ProgressionPacketBuilder.BuildReqConquest(NextSequence());
            LogOutbound(0x05A, packet);
            return _sendChunkCallback(packet, true);
        }

        public Task SendUnityMenuAsync(bool open)
        {
            byte[] packet = ProgressionPacketBuilder.BuildUnityMenu(open, NextSequence());
            LogOutbound(0x116, packet);
            return _sendChunkCallback(packet, true);
        }

        private void LogOutbound(ushort packetId, ReadOnlySpan<byte> packet)
        {
            if (LogOutboundOnRoute && _logPacketCallback != null)
            {
                ushort seq = packet.Length >= 4 ? BinaryPrimitives.ReadUInt16LittleEndian(packet.Slice(2, 2)) : (ushort)0;
                var payload = packet.Length >= 4 ? packet.Slice(4) : ReadOnlySpan<byte>.Empty;
                _logPacketCallback(PacketDirection.Outbound, packetId, seq, payload);
            }
        }

        #endregion
    }
}
