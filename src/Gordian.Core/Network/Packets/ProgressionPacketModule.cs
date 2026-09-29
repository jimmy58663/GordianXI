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
            dispatcher.Register(S2C_0x02A_TalkNumWork.PacketId, HandleTalkNumWork);
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
            dispatcher.Register(S2C_0x073_ChocoboToteboard.PacketId, HandleChocoboToteboard);
            dispatcher.Register(S2C_0x110_Unity.PacketId, HandleUnity);
        }

        public void Unregister(IPacketDispatcher dispatcher)
        {
            ArgumentNullException.ThrowIfNull(dispatcher);

            dispatcher.Unregister(S2C_0x032_Event.PacketId);
            dispatcher.Unregister(S2C_0x033_EventStr.PacketId);
            dispatcher.Unregister(S2C_0x034_EventNum.PacketId);
            dispatcher.Unregister(S2C_0x036_TalkNum.PacketId);
            dispatcher.Unregister(S2C_0x02A_TalkNumWork.PacketId);
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
            dispatcher.Unregister(S2C_0x073_ChocoboToteboard.PacketId);
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

            GordianLog.Debug("DIALOG", $"TalkNum message received: MessageId={talk.MessageId}, ActIndex={talk.ActIndex}, HideName={talk.HideName}, Type={talk.Type}");
            _progressionState.PostDialogMessage(new DialogMessageInfo(talk.MessageId, talk.UniqueNo, talk.ActIndex, talk.HideName, talk.Type,
                Array.Empty<int>(), string.Empty));
        }

        private void HandleTalkNumWork(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var talk = new S2C_0x02A_TalkNumWork(payload);
            if (!talk.IsValid) return;

            var numbers = new int[4];
            for (int i = 0; i < 4; i++) numbers[i] = talk.GetNumber(i);
            string name = talk.GetName();
            GordianLog.Debug("DIALOG", $"TalkNumWork message received: MessageId={talk.MessageId}, ActIndex={talk.ActIndex}, HideName={talk.HideName}, Type={talk.Type}, Numbers={string.Join(",", numbers)}, Name='{name}'");
            _progressionState.PostDialogMessage(new DialogMessageInfo(talk.MessageId, talk.UniqueNo, talk.ActIndex, talk.HideName, talk.Type, numbers, name));
        }

        /// <summary>
        /// The release modes (XiPackets 0x0052): 0 releases character control after a cutscene, 1 answers a pending
        /// event update (the script goes on), 2 cancels the event (the event id is in the high bits), 3 cancels a
        /// text input, 4 releases the fishing lock. Only mode 2 ends the event; modes 0 and 1 arrive after every
        /// event end and update, so they must not.
        /// </summary>
        private void HandleEventUcOff(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var ucoff = new S2C_0x052_EventUcOff(payload);
            if (!ucoff.IsValid) return;

            var mode = (EventUcOffMode)((uint)ucoff.Mode & 0xFF);
            GordianLog.Info("EVENT", $"Event user control release: Mode={mode} (raw 0x{(uint)ucoff.Mode:X})");
            switch (mode)
            {
                case EventUcOffMode.EventRecvPending:
                    _progressionState.AcknowledgeEventUpdate();
                    break;
                case EventUcOffMode.CancelEvent:
                    _progressionState.CancelEventByServer();
                    _progressionState.EndEvent();
                    break;
                case EventUcOffMode.Fishing:
                    _progressionState.ClearFishing();
                    break;
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
            _progressionState.SetMyRoomOperation(new MyRoomOperationInfo(op.MyroomItemNo, op.Result, op.MyroomItemIndex, (ContainerId)op.MyroomCategory));
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

        private void HandleChocoboToteboard(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var toteboard = new S2C_0x073_ChocoboToteboard(payload);
            if (!toteboard.IsValid) return;

            _progressionState.UpdateToteboard(in toteboard);
            GordianLog.Debug("CHOCOBO", $"Updated chocobo race toteboard: Slot={toteboard.SlotIndex}, Ident=0x{toteboard.Ident:X}");
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

        /// <summary>Sends 0x05B mode 1: an event update the script waits on (answered by 0x052 mode 1).</summary>
        public Task SendEventUpdateAsync(uint uniqueNo, uint endPara, ushort actIndex, ushort eventNum, ushort eventPara)
        {
            byte[] packet = ProgressionPacketBuilder.BuildEventEnd(uniqueNo, endPara, actIndex, 1, eventNum, eventPara, NextSequence());
            LogOutbound(0x05B, packet);
            return _sendChunkCallback(packet, true);
        }

        /// <summary>Sends 0x05C mode 1: a position update the script waits on (a same-zone warp), answered by 0x052 mode 1.</summary>
        public Task SendEventUpdateXzyAsync(Vector3 position, uint uniqueNo, uint endPara, ushort eventNum, ushort eventPara, ushort actIndex, sbyte dir)
        {
            byte[] packet = ProgressionPacketBuilder.BuildEventEndXzy(position, uniqueNo, endPara, eventNum, eventPara, actIndex, 1, dir, NextSequence());
            LogOutbound(0x05C, packet);
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

        /// <summary>
        /// Marks a held key item as read, as retail does the first time the key item is viewed: sets its seen bit
        /// locally and sends C2S 0x064 with the whole table's seen flags (the server stores every set bit and
        /// does not answer). Does nothing when the key item is not held or was already seen.
        /// </summary>
        /// <param name="actIndex">The local player's actor (target) index; the server rejects any other.</param>
        public Task MarkKeyItemSeenAsync(ushort keyItemId, ushort actIndex)
        {
            Span<uint> flags = stackalloc uint[16];
            if (!_progressionState.TryMarkKeyItemSeen(keyItemId, flags, out ushort tableIndex))
            {
                return Task.CompletedTask;
            }

            byte[] packet = ProgressionPacketBuilder.BuildScenarioItemRead(_localPlayerState.ServerId, actIndex, tableIndex, flags, NextSequence());
            LogOutbound(0x064, packet);
            return _sendChunkCallback(packet, true);
        }

        /// <summary>
        /// Sends C2S 0x0CB: a Mog House operation. <paramref name="param2"/> is 0 when opening, 1 when closing,
        /// or the remodel style (615 San d'Oria, 616 Bastok, 617 Windurst, 618 Mog Patio).
        /// </summary>
        public Task SendMyRoomIsAsync(MyRoomIsKind kind, byte param1 = 0, ushort param2 = 0)
        {
            byte[] packet = ProgressionPacketBuilder.BuildMyRoomIs(kind, param1, param2, NextSequence());
            LogOutbound(0x0CB, packet);
            return _sendChunkCallback(packet, true);
        }

        /// <summary>
        /// Sends C2S 0x0FA: places a furnishing in the Mog House layout. The server answers with S2C 0x0FA.
        /// </summary>
        public Task SendMyRoomLayoutAsync(ushort itemNo, byte itemIndex, byte category, byte floor, byte x, byte y, byte z, byte rotation)
        {
            byte[] packet = ProgressionPacketBuilder.BuildMyRoomLayout(itemNo, itemIndex, category, floor, x, y, z, rotation, NextSequence());
            LogOutbound(0x0FA, packet);
            return _sendChunkCallback(packet, true);
        }

        /// <summary>
        /// Sends C2S 0x09B: a Chocobo Circuit request. <see cref="ChocoboRaceReqKind.Toteboard"/> is answered with
        /// S2C 0x073 (stored in <see cref="ProgressionState"/>), <see cref="ChocoboRaceReqKind.ChocoboList"/> with 0x074.
        /// </summary>
        public Task SendChocoboRaceReqAsync(ChocoboRaceReqParam param, ChocoboRaceReqKind kind)
        {
            byte[] packet = ProgressionPacketBuilder.BuildChocoboRaceReq(param, kind, NextSequence());
            LogOutbound(0x09B, packet);
            return _sendChunkCallback(packet, true);
        }

        /// <summary>Sends C2S 0x117: requests the Unity quest information (answered with S2C 0x110).</summary>
        public Task SendUnityQuestAsync()
        {
            byte[] packet = ProgressionPacketBuilder.BuildUnityQuest(NextSequence());
            LogOutbound(0x117, packet);
            return _sendChunkCallback(packet, true);
        }

        /// <summary>Sends C2S 0x118: turns Unity chat on or off.</summary>
        public Task SendUnityToggleAsync(bool active)
        {
            byte[] packet = ProgressionPacketBuilder.BuildUnityToggle(active, NextSequence());
            LogOutbound(0x118, packet);
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
