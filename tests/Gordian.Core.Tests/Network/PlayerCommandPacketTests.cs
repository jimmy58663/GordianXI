// tests/Gordian.Core.Tests/Network/PlayerCommandPacketTests.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Gordian.Core.Actions;
using Gordian.Core.Config;
using Gordian.Core.Network;
using Gordian.Core.Network.Packets;
using Gordian.Core.Ui;
using Gordian.Core.World;
using Xunit;

namespace Gordian.Core.Tests.Network
{
    /// <summary>
    /// The everyday command packets: byte-exact builders, decoders laid out as LandSandBoat sends them, the module's
    /// state updates and the slash commands that send them.
    /// </summary>
    public class PlayerCommandPacketTests
    {
        private const uint LocalId = 0x01020304;
        private const ushort LocalIndex = 10;

        private static void AssertHeader(byte[] packet, ushort opcode, int sequence = 0)
        {
            Assert.Equal(0, packet.Length % 4);
            Assert.True(PacketHeader.TryParse(packet, out var header));
            Assert.Equal(opcode, header.PacketId);
            Assert.Equal(packet.Length, header.TotalSize);
            Assert.Equal(sequence, header.SequenceId);
        }

        // ---- builders ----

        [Theory]
        [InlineData(RestMode.Toggle, new byte[] { 0xE8, 0x04, 0x07, 0x00, 0, 0, 0, 0 })]
        [InlineData(RestMode.On, new byte[] { 0xE8, 0x04, 0x07, 0x00, 1, 0, 0, 0 })]
        [InlineData(RestMode.Off, new byte[] { 0xE8, 0x04, 0x07, 0x00, 2, 0, 0, 0 })]
        public void BuildCamp_Is8Bytes(RestMode mode, byte[] expected)
        {
            Assert.Equal(expected, PlayerCommandPacketBuilder.BuildCamp(mode, 7));
        }

        [Theory]
        [InlineData(RestMode.Toggle, new byte[] { 0xEA, 0x04, 0x09, 0x00, 0, 0, 0, 0 })]
        [InlineData(RestMode.On, new byte[] { 0xEA, 0x04, 0x09, 0x00, 1, 0, 0, 0 })]
        [InlineData(RestMode.Off, new byte[] { 0xEA, 0x04, 0x09, 0x00, 2, 0, 0, 0 })]
        public void BuildSit_Is8Bytes(RestMode mode, byte[] expected)
        {
            Assert.Equal(expected, PlayerCommandPacketBuilder.BuildSit(mode, 9));
        }

        [Fact]
        public void BuildSitChair_Is12BytesWithModeAndChair()
        {
            // 0x113 | (3 << 9) = 0x0713
            Assert.Equal(new byte[] { 0x13, 0x07, 0x02, 0x00, 1, 0, 0, 0, 3, 0, 0, 0 },
                PlayerCommandPacketBuilder.BuildSitChair(RestMode.On, 3, 2));
        }

        [Fact]
        public void BuildDice_Is8BytesWithTheTypedNumber()
        {
            // 0x0A2 | (2 << 9) = 0x04A2
            Assert.Equal(new byte[] { 0xA2, 0x04, 0x01, 0x00, 0, 0, 0, 0 }, PlayerCommandPacketBuilder.BuildDice(0, 1));
            Assert.Equal(new byte[] { 0xA2, 0x04, 0x00, 0x00, 0x64, 0, 0, 0 }, PlayerCommandPacketBuilder.BuildDice(100));
        }

        [Fact]
        public void BuildTrackingList_SendsFlagOne()
        {
            // 0x0F4 | (2 << 9) = 0x04F4; LSB drops the packet unless SendFlg is 1.
            Assert.Equal(new byte[] { 0xF4, 0x04, 0x05, 0x00, 1, 0, 0, 0 }, PlayerCommandPacketBuilder.BuildTrackingList(5));
        }

        [Fact]
        public void BuildTrackingStart_CarriesTheIndexAsAWord()
        {
            Assert.Equal(new byte[] { 0xF5, 0x04, 0x00, 0x00, 0x34, 0x12, 0, 0 }, PlayerCommandPacketBuilder.BuildTrackingStart(0x1234));
        }

        [Fact]
        public void BuildTrackingEnd_IsZeroPadded()
        {
            Assert.Equal(new byte[] { 0xF6, 0x04, 0x00, 0x00, 0, 0, 0, 0 }, PlayerCommandPacketBuilder.BuildTrackingEnd());
        }

        [Fact]
        public void BuildEmoteListRequest_IsAHeaderOnly()
        {
            // 0x119 | (1 << 9) = 0x0319
            Assert.Equal(new byte[] { 0x19, 0x03, 0x00, 0x00 }, PlayerCommandPacketBuilder.BuildEmoteListRequest());
        }

        [Fact]
        public void BuildEffectEnd_Is16Bytes()
        {
            // 0x059 | (4 << 9) = 0x0859
            Assert.Equal(new byte[] { 0x59, 0x08, 0x00, 0x00, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 },
                PlayerCommandPacketBuilder.BuildEffectEnd(1));
        }

        [Fact]
        public void BuildProposal_RoundsTheTextUpTo4Bytes()
        {
            // "Pizza?" is 6 bytes: 4 header + 1 kind + 6 + NUL = 12.
            byte[] packet = PlayerCommandPacketBuilder.BuildProposal(ProposalKind.Party, "Pizza?", 3);
            Assert.Equal(12, packet.Length);
            AssertHeader(packet, 0x0A0, 3);
            Assert.Equal(1, packet[4]);
            Assert.Equal("Pizza?", Encoding.ASCII.GetString(packet, 5, 6));
            Assert.Equal(0, packet[11]);
        }

        [Fact]
        public void BuildProposal_WithNoText_IsA8ByteCancel()
        {
            byte[] packet = PlayerCommandPacketBuilder.BuildProposal(ProposalKind.Say, string.Empty);
            Assert.Equal(8, packet.Length);
            Assert.Equal(5, packet[4]);
            Assert.Equal(0, packet[5]);
        }

        [Fact]
        public void BuildProposal_CapsTheTextAtTheServersBuffer()
        {
            byte[] packet = PlayerCommandPacketBuilder.BuildProposal(ProposalKind.Shout, new string('x', 400));
            // 4 + 1 + 127 + NUL = 133, rounded up to 136; LSB reads at most Str[128].
            Assert.Equal(136, packet.Length);
            Assert.Equal(0, packet[132]);
        }

        [Fact]
        public void BuildVote_CarriesTheOptionAndProposerName()
        {
            byte[] packet = PlayerCommandPacketBuilder.BuildVote(2, "Ayame");
            // 4 + 1 + 5 + NUL = 11, rounded up to 12.
            Assert.Equal(12, packet.Length);
            AssertHeader(packet, 0x0A1);
            Assert.Equal(2, packet[4]);
            Assert.Equal("Ayame", Encoding.ASCII.GetString(packet, 5, 5));
            Assert.Equal(0, packet[10]);
        }

        [Fact]
        public void BuildVote_TruncatesALongNameToTheServersField()
        {
            byte[] packet = PlayerCommandPacketBuilder.BuildVote(1, new string('n', 40));
            Assert.Equal(20, packet.Length); // 4 + 1 + 14 + NUL
            Assert.Equal(0, packet[19]);
        }

        // ---- decoders ----

        [Fact]
        public void SwitchStart_DecodesLandSandBoatsLayout()
        {
            const string text = "[Pizza?]\n1:yes\n2:no";
            var p = new byte[26 + text.Length + 4];
            BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(0, 4), 0x01000011);
            BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(4, 4), 18);
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(8, 2), 0x55);
            Encoding.ASCII.GetBytes("Ayame").CopyTo(p.AsSpan(10));
            p[25] = (byte)ProposalKind.Party;
            Encoding.ASCII.GetBytes(text).CopyTo(p.AsSpan(26));

            var start = new S2C_0x078_SwitchStart(p);

            Assert.True(start.IsValid);
            Assert.Equal(0x01000011u, start.ProposerId);
            Assert.Equal(18u, start.AllNum);
            Assert.Equal(0x55, start.ProposerIndex);
            Assert.Equal("Ayame", start.ProposerName);
            Assert.Equal(ProposalKind.Party, start.Kind);
            Assert.Equal(text, start.Text);
        }

        private static byte[] SwitchProc(bool closed, string name, string text, params ushort[] votes)
        {
            var p = new byte[44 + (closed ? text.Length + 4 : 0)];
            BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(0, 4), 18);
            for (int i = 0; i < votes.Length; i++) BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(4 + (i + 1) * 2, 2), votes[i]);
            p[22] = (byte)ProposalKind.Party;
            p[23] = (byte)(closed ? 2 : 0);
            p[24] = (byte)(votes.Length + 1);
            Encoding.ASCII.GetBytes(name).CopyTo(p.AsSpan(25));
            if (closed) Encoding.ASCII.GetBytes(text).CopyTo(p.AsSpan(40));
            return p;
        }

        [Fact]
        public void SwitchProc_LiveUpdateHasVotesAndNoText()
        {
            var proc = new S2C_0x079_SwitchProc(SwitchProc(false, "Ayame", "", 3, 1));

            Assert.True(proc.IsValid);
            Assert.False(proc.Closed);
            Assert.Equal(3, proc.QuestionNum);
            Assert.Equal("Ayame", proc.ProposerName);
            Assert.Equal(3, proc.GetVotes(1));
            Assert.Equal(1, proc.GetVotes(2));
            Assert.Equal(0, proc.GetVotes(3));
            Assert.Equal(0, proc.GetVotes(0));
            Assert.Equal(0, proc.GetVotes(9));
            Assert.Equal(string.Empty, proc.Text);
        }

        [Fact]
        public void SwitchProc_FinalResultCarriesTheText()
        {
            const string text = "[Pizza?]\n1[3]:yes\n2[1]:no";
            var proc = new S2C_0x079_SwitchProc(SwitchProc(true, "Ayame", text, 3, 1));
            Assert.True(proc.Closed);
            Assert.Equal(text, proc.Text);
        }

        [Fact]
        public void TrackingList_DecodesTheBitfieldAndOffsets()
        {
            var p = new byte[24];
            // ActIndex 0x0321, Level 42, Type 2 (monster).
            BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(0, 4), 0x0321u | (42u << 16) | (2u << 24));
            BinaryPrimitives.WriteInt16LittleEndian(p.AsSpan(4, 2), -15);
            BinaryPrimitives.WriteInt16LittleEndian(p.AsSpan(6, 2), 120);
            Encoding.ASCII.GetBytes("Rarab").CopyTo(p.AsSpan(8));

            var entry = new S2C_0x0F4_TrackingList(p);

            Assert.True(entry.IsValid);
            Assert.Equal(0x0321, entry.ActIndex);
            Assert.Equal(42, entry.Level);
            Assert.Equal(2, entry.Type);
            Assert.Equal(-15, entry.DeltaX);
            Assert.Equal(120, entry.DeltaZ);
            Assert.Equal("Rarab", entry.Name);
        }

        [Fact]
        public void TrackingPos_DecodesPositionAndState()
        {
            var p = new byte[20];
            BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(0, 4), 12.5f);
            BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(4, 4), -3.25f);
            BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(8, 4), 99f);
            p[12] = 1;
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(14, 2), 0x321);
            BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(16, 4), 2);

            var pos = new S2C_0x0F5_TrackingPos(p);

            Assert.True(pos.IsValid);
            Assert.Equal(12.5f, pos.X);
            Assert.Equal(-3.25f, pos.Y);
            Assert.Equal(99f, pos.Z);
            Assert.Equal(1, pos.Level);
            Assert.Equal(0x321, pos.ActIndex);
            Assert.Equal(TrackingPosState.Lose, pos.State);
        }

        [Fact]
        public void TrackingState_DecodesTheState()
        {
            Assert.Equal(TrackingListState.ListStart, new S2C_0x0F6_TrackingState(new byte[] { 1, 0, 0, 0 }).State);
            Assert.Equal(TrackingListState.ListEnd, new S2C_0x0F6_TrackingState(new byte[] { 2, 0, 0, 0 }).State);
            Assert.Equal(TrackingListState.Error, new S2C_0x0F6_TrackingState(new byte[] { 0x0A, 0, 0, 0 }).State);
            Assert.False(new S2C_0x0F6_TrackingState(new byte[3]).IsValid);
        }

        [Fact]
        public void EmoteList_DecodesJobEmoteAndChairBits()
        {
            var p = new byte[8];
            BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(0, 4), (1u << 0) | (1u << 14) | (1u << 21)); // WAR, SMN, RUN
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(4, 2), (1 << 0) | (1 << 10)); // chairs 1 and 11

            var list = new S2C_0x11A_EmoteList(p);

            Assert.True(list.IsValid);
            var state = new EmoteListState();
            state.Apply(list.JobEmotes, list.Chairs);
            Assert.True(state.Received);
            Assert.True(state.HasJobEmote(JobId.Warrior));
            Assert.True(state.HasJobEmote(JobId.Summoner));
            Assert.True(state.HasJobEmote(JobId.RuneFencer));
            Assert.False(state.HasJobEmote(JobId.Monk));
            Assert.False(state.HasJobEmote(JobId.None));
            Assert.True(state.HasChair(1));
            Assert.True(state.HasChair(11));
            Assert.False(state.HasChair(2));
            Assert.False(state.HasChair(0));
            Assert.False(state.HasChair(12));
        }

        [Fact]
        public void Jump_DecodesTheJumpersIndex()
        {
            var jump = new S2C_0x11E_Jump(new byte[] { 0x34, 0x02, 0, 0 });
            Assert.True(jump.IsValid);
            Assert.Equal(0x234, jump.ActIndex);
            Assert.False(new S2C_0x11E_Jump(new byte[1]).IsValid);
        }

        // ---- module ----

        private sealed class Fixture
        {
            public PlayerCommandState State { get; } = new();
            public WorldState World { get; } = new();
            public List<byte[]> Sent { get; } = new();
            public PlayerCommandPacketModule Module { get; }
            public PacketDispatcher Dispatcher { get; } = new();

            public Fixture()
            {
                Module = new PlayerCommandPacketModule(State, World, (data, _) =>
                {
                    Sent.Add(data.ToArray());
                    return Task.CompletedTask;
                });
                Module.Register(Dispatcher);
            }

            public void Receive(ushort id, byte[] payload) => Assert.True(Dispatcher.Dispatch(new PacketHeader(id, payload.Length + 4, 1), payload));
        }

        [Fact]
        public void Module_JumpOfAnotherPlayer_CountsAJumpOnTheEntity()
        {
            var f = new Fixture();
            f.World.UpsertEntity(new PlayerEntity(0x01000099, 0x234) { Name = "Other" });

            f.Receive(0x11E, new byte[] { 0x34, 0x02, 0, 0 });
            f.Receive(0x11E, new byte[] { 0x34, 0x02, 0, 0 });
            f.Receive(0x11E, new byte[] { 0x35, 0x02, 0, 0 }); // unknown entity: ignored

            Assert.True(f.World.TryGetByServerId(0x01000099, out var other));
            Assert.Equal(2, other!.JumpCount);
        }

        [Fact]
        public void Module_WideScanList_CollectsEntriesBetweenStartAndEnd()
        {
            var f = new Fixture();
            IReadOnlyList<WideScanEntry>? completed = null;
            f.State.WideScan.ListCompleted += list => completed = list;

            f.Receive(0x0F6, new byte[] { 1, 0, 0, 0 });
            var entry = new byte[24];
            BinaryPrimitives.WriteUInt32LittleEndian(entry.AsSpan(0, 4), 0x0100u | (2u << 24));
            BinaryPrimitives.WriteInt16LittleEndian(entry.AsSpan(4, 2), 30);
            BinaryPrimitives.WriteInt16LittleEndian(entry.AsSpan(6, 2), -40);
            f.Receive(0x0F4, entry);
            Assert.Null(completed);
            f.Receive(0x0F6, new byte[] { 2, 0, 0, 0 });

            Assert.NotNull(completed);
            var only = Assert.Single(completed!);
            Assert.Equal(new WideScanEntry(0x0100, 0, 2, 30, -40, string.Empty), only);
            Assert.Equal(TrackingListState.ListEnd, f.State.WideScan.ListState);
        }

        [Fact]
        public void Module_WideScanStart_ClearsTheOldList()
        {
            var f = new Fixture();
            f.State.WideScan.AddEntry(new WideScanEntry(1, 0, 1, 0, 0, ""));
            f.Receive(0x0F6, new byte[] { 1, 0, 0, 0 });
            Assert.Empty(f.State.WideScan.Snapshot());
        }

        [Fact]
        public void Module_WideScanError_RaisesListFailed()
        {
            var f = new Fixture();
            TrackingListState? failed = null;
            f.State.WideScan.ListFailed += s => failed = s;
            f.Receive(0x0F6, new byte[] { 0x0A, 0, 0, 0 });
            Assert.Equal(TrackingListState.Error, failed);
        }

        [Fact]
        public void Module_TrackingPos_TracksUntilLost()
        {
            var f = new Fixture();
            byte[] Pos(uint state)
            {
                var p = new byte[20];
                BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(0, 4), 1f);
                BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(4, 4), 2f);
                BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(8, 4), 3f);
                BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(14, 2), 0x321);
                BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(16, 4), state);
                return p;
            }

            f.Receive(0x0F5, Pos(1));
            Assert.Equal(new Vector3(1, 2, 3), f.State.WideScan.Track!.Value.Position);

            f.Receive(0x0F5, Pos(2));
            Assert.Null(f.State.WideScan.Track);
        }

        [Fact]
        public void Module_EmoteList_FillsTheState()
        {
            var f = new Fixture();
            var p = new byte[8];
            BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(0, 4), 1u << 8);
            f.Receive(0x11A, p);
            Assert.True(f.State.Emotes.HasJobEmote(JobId.Beastmaster));
        }

        [Fact]
        public void Module_Proposal_StartsAndTallies()
        {
            var f = new Fixture();
            var changes = new List<VoteProposal>();
            f.State.Votes.Changed += changes.Add;

            var start = new byte[26 + 24];
            Encoding.ASCII.GetBytes("Ayame").CopyTo(start.AsSpan(10));
            start[25] = (byte)ProposalKind.Party;
            Encoding.ASCII.GetBytes("[Pizza?]\n1:yes\n2:no").CopyTo(start.AsSpan(26));
            f.Receive(0x078, start);

            Assert.Equal("Ayame", f.State.Votes.LastProposer);
            var proposal = f.State.Votes.Latest!;
            Assert.Equal("Pizza?", proposal.Question);
            Assert.Equal(new[] { "yes", "no" }, proposal.Options);
            Assert.Equal(new[] { 0, 0 }, proposal.Votes);
            Assert.False(proposal.Closed);

            f.Receive(0x079, SwitchProc(false, "Ayame", "", 2, 1));
            proposal = f.State.Votes.Get("ayame")!;
            Assert.Equal(new[] { 2, 1 }, proposal.Votes);
            Assert.Equal("Pizza?", proposal.Question);
            Assert.False(proposal.Closed);

            f.Receive(0x079, SwitchProc(true, "Ayame", "[Pizza?]\n1[3]:yes\n2[1]:no", 3, 1));
            proposal = f.State.Votes.Get("Ayame")!;
            Assert.True(proposal.Closed);
            Assert.Equal(new[] { 3, 1 }, proposal.Votes);
            Assert.Equal(3, changes.Count);
        }

        [Fact]
        public async Task Module_Senders_WriteTheBuilderPackets()
        {
            var f = new Fixture();

            await f.Module.SendHealAsync(RestMode.On);
            await f.Module.SendSitAsync();
            await f.Module.SendSitChairAsync(2);
            await f.Module.SendRandomAsync();
            await f.Module.SendProposalAsync(ProposalKind.Say, "\"Q\" a b");
            await f.Module.SendVoteAsync(1, "Ayame");
            await f.Module.SendWideScanAsync();
            await f.Module.SendTrackingStartAsync(0x321);
            await f.Module.SendTrackingEndAsync();
            await f.Module.SendEmoteListRequestAsync();
            await f.Module.SendEffectEndAsync();

            ushort[] expected = { 0x0E8, 0x0EA, 0x113, 0x0A2, 0x0A0, 0x0A1, 0x0F4, 0x0F5, 0x0F6, 0x119, 0x059 };
            Assert.Equal(expected, f.Sent.Select(p => (ushort)(BinaryPrimitives.ReadUInt16LittleEndian(p) & 0x1FF)).ToArray());
            foreach (var packet in f.Sent) Assert.Equal(packet.Length, ((packet[1] & 0xFE) * 2));
        }

        // ---- slash commands ----

        [Theory]
        [InlineData("/heal", ChatCommandResultKind.Heal, RestMode.Toggle)]
        [InlineData("/heal on", ChatCommandResultKind.Heal, RestMode.On)]
        [InlineData("/HEAL off", ChatCommandResultKind.Heal, RestMode.Off)]
        [InlineData("/sit", ChatCommandResultKind.Sit, RestMode.Toggle)]
        [InlineData("/sit off", ChatCommandResultKind.Sit, RestMode.Off)]
        public void Router_ParsesRestCommands(string input, ChatCommandResultKind kind, RestMode mode)
        {
            var result = ChatCommandRouter.Parse(input);
            Assert.Equal(kind, result.Kind);
            Assert.Equal(mode, result.Rest);
        }

        [Theory]
        [InlineData("/heal maybe")]
        [InlineData("/sit sideways")]
        [InlineData("/sitchair 99")]
        [InlineData("/sitchair x")]
        [InlineData("/vote")]
        [InlineData("/vote 9")]
        [InlineData("/vote x")]
        public void Router_RejectsBadArguments(string input)
        {
            Assert.Equal(ChatCommandResultKind.LocalNotice, ChatCommandRouter.Parse(input).Kind);
        }

        [Theory]
        [InlineData("/sitchair", 0, RestMode.Toggle)]
        [InlineData("/sitchair 3", 3, RestMode.Toggle)]
        [InlineData("/sitchair 3 on", 3, RestMode.On)]
        [InlineData("/sitchair off", 0, RestMode.Off)]
        public void Router_ParsesSitChair(string input, int chair, RestMode mode)
        {
            var result = ChatCommandRouter.Parse(input);
            Assert.Equal(ChatCommandResultKind.SitChair, result.Kind);
            Assert.Equal(chair, result.ActionParam);
            Assert.Equal(mode, result.Rest);
        }

        [Fact]
        public void Router_ParsesRandomWideScanAndTrack()
        {
            Assert.Equal(ChatCommandResultKind.Random, ChatCommandRouter.Parse("/random").Kind);
            Assert.Equal(ChatCommandResultKind.WideScan, ChatCommandRouter.Parse("/widescan").Kind);
            var track = ChatCommandRouter.Parse("/track Rarab");
            Assert.Equal(ChatCommandResultKind.TrackTarget, track.Kind);
            Assert.Equal("Rarab", track.Message);
            Assert.Equal("off", ChatCommandRouter.Parse("/untrack").Message);
        }

        [Fact]
        public void Router_KeepsServerBangHealForGms()
        {
            Assert.Equal(ChatCommandResultKind.ServerCommand, ChatCommandRouter.Parse("!heal").Kind);
        }

        [Theory]
        [InlineData("/nominate \"Pizza?\" yes no", ProposalKind.Say, "\"Pizza?\" yes no")]
        [InlineData("/propose party \"Pizza?\" yes no", ProposalKind.Party, "\"Pizza?\" yes no")]
        [InlineData("/nominate shout Q a", ProposalKind.Shout, "Q a")]
        [InlineData("/nominate linkshell2 Q a", ProposalKind.Linkshell2, "Q a")]
        [InlineData("/nominate", ProposalKind.Say, "")]
        [InlineData("/nominate party", ProposalKind.Party, "")]
        public void Router_ParsesProposals(string input, ProposalKind kind, string text)
        {
            var result = ChatCommandRouter.Parse(input);
            Assert.Equal(ChatCommandResultKind.Propose, result.Kind);
            Assert.Equal((ushort)kind, result.ActionParam);
            Assert.Equal(text, result.Message);
        }

        [Fact]
        public void Router_ProposalDefaultsToTheCurrentChatMode()
        {
            var result = ChatCommandRouter.Parse("/nominate Q a", ChatSendKind.Party);
            Assert.Equal((ushort)ProposalKind.Party, result.ActionParam);
        }

        [Fact]
        public void Router_ParsesVotes()
        {
            var plain = ChatCommandRouter.Parse("/vote 2");
            Assert.Equal(ChatCommandResultKind.Vote, plain.Kind);
            Assert.Equal(2, plain.ActionParam);
            Assert.Equal(string.Empty, plain.TargetName);

            var named = ChatCommandRouter.Parse("/vote 3 Ayame");
            Assert.Equal(3, named.ActionParam);
            Assert.Equal("Ayame", named.TargetName);
        }

        // ---- action service ----

        private sealed class ServiceFixture
        {
            public List<byte[]> Sent { get; } = new();
            public WorldState World { get; } = new();
            public LocalPlayerState Local { get; } = new() { ServerId = LocalId };
            public PlayerCommandState State { get; } = new();
            public PlayerActionService Service { get; }
            public CombatPacketModule Combat { get; }

            public ServiceFixture()
            {
                Task Capture(ReadOnlyMemory<byte> mem, bool urgent)
                {
                    Sent.Add(mem.ToArray());
                    return Task.CompletedTask;
                }

                var profile = new SessionProfile();
                Combat = new CombatPacketModule(new CombatState(), Local, Capture);
                Service = new PlayerActionService(
                    profile, World, Local, Combat,
                    new ChatPacketModule(Capture), new PartyPacketModule(new PartyState(), Capture),
                    new EntityPacketModule(World, Local, Capture), new LifecyclePacketModule(profile, Capture), Capture)
                {
                    CommandModule = new PlayerCommandPacketModule(State, World, Capture)
                };
                World.UpsertEntity(new WorldEntity(LocalId, LocalIndex, EntityType.Player) { Name = "Me" });
                World.UpsertEntity(new WorldEntity(0x02020202, 25, EntityType.Monster) { Name = "Forest Hare", Hpp = 100 });
            }

            public ushort LastOpcode => (ushort)(BinaryPrimitives.ReadUInt16LittleEndian(Sent[^1]) & 0x1FF);
        }

        [Theory]
        [InlineData("/heal", 0x0E8, 0)]
        [InlineData("/heal on", 0x0E8, 1)]
        [InlineData("/sit", 0x0EA, 0)]
        [InlineData("/sit off", 0x0EA, 2)]
        public async Task Command_RestSendsItsPacket(string input, int opcode, byte mode)
        {
            var f = new ServiceFixture();
            var result = await f.Service.ExecuteCommandAsync(input);

            Assert.True(result.Success);
            byte[] packet = Assert.Single(f.Sent);
            AssertHeader(packet, (ushort)opcode, 1);
            Assert.Equal(mode, packet[4]);
        }

        [Fact]
        public async Task Command_SitChairSendsTheChair()
        {
            var f = new ServiceFixture();
            await f.Service.ExecuteCommandAsync("/sitchair 4 on");
            byte[] packet = Assert.Single(f.Sent);
            AssertHeader(packet, 0x113, 1);
            Assert.Equal(1, packet[4]);
            Assert.Equal(4, packet[8]);
        }

        [Fact]
        public async Task Command_RandomSendsDice()
        {
            var f = new ServiceFixture();
            await f.Service.ExecuteCommandAsync("/random");
            AssertHeader(Assert.Single(f.Sent), 0x0A2, 1);
        }

        [Fact]
        public async Task Command_WidescanAndTrack()
        {
            var f = new ServiceFixture();
            await f.Service.ExecuteCommandAsync("/widescan");
            AssertHeader(f.Sent[0], 0x0F4, 1);
            Assert.Equal(1, f.Sent[0][4]);

            var track = await f.Service.ExecuteCommandAsync("/track Forest Hare");
            Assert.True(track.Success);
            AssertHeader(f.Sent[1], 0x0F5, 2);
            Assert.Equal(25, f.Sent[1][4]);

            await f.Service.ExecuteCommandAsync("/track 25");
            Assert.Equal(25, f.Sent[2][4]);

            await f.Service.ExecuteCommandAsync("/untrack");
            AssertHeader(f.Sent[3], 0x0F6, 4);
        }

        [Fact]
        public async Task Command_TrackWithNoTargetWarns()
        {
            var f = new ServiceFixture();
            var result = await f.Service.ExecuteCommandAsync("/track");
            Assert.Equal(PlayerActionResultKind.Warning, result.Kind);
            Assert.Empty(f.Sent);

            f.Service.SetTargetByServerId(0x02020202);
            var targeted = await f.Service.ExecuteCommandAsync("/track");
            Assert.True(targeted.Success);
            Assert.Equal(25, f.Sent[0][4]);
        }

        [Fact]
        public async Task Command_ProposeAndVote()
        {
            var f = new ServiceFixture();

            var empty = await f.Service.ExecuteCommandAsync("/vote 1");
            Assert.Equal(PlayerActionResultKind.Warning, empty.Kind);
            Assert.Empty(f.Sent);

            await f.Service.ExecuteCommandAsync("/propose party \"Pizza?\" yes no");
            byte[] proposal = Assert.Single(f.Sent);
            AssertHeader(proposal, 0x0A0, 1);
            Assert.Equal((byte)ProposalKind.Party, proposal[4]);
            Assert.Equal("\"Pizza?\" yes no", Encoding.ASCII.GetString(proposal, 5, 15));

            f.State.Votes.ApplyStart("Ayame", ProposalKind.Party, "[Pizza?]\n1:yes\n2:no");
            await f.Service.ExecuteCommandAsync("/vote 2");
            byte[] vote = f.Sent[1];
            AssertHeader(vote, 0x0A1, 2);
            Assert.Equal(2, vote[4]);
            Assert.Equal("Ayame", Encoding.ASCII.GetString(vote, 5, 5));

            await f.Service.ExecuteCommandAsync("/vote 1 Bob");
            Assert.Equal("Bob", Encoding.ASCII.GetString(f.Sent[2], 5, 3));
        }

        [Fact]
        public async Task Command_JumpSendsTheLocalPlayersOwnIndex()
        {
            // LandSandBoat drops 0x11D unless both the id and the index are the character's own.
            var f = new ServiceFixture();
            await f.Service.ExecuteCommandAsync("/jump");

            byte[] packet = Assert.Single(f.Sent);
            AssertHeader(packet, 0x11D, 1);
            Assert.Equal(LocalId, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(4, 4)));
            Assert.Equal(LocalIndex, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(8, 2)));
        }

        [Fact]
        public void Router_ParsesConquest()
        {
            Assert.Equal(ChatCommandResultKind.ConquestRequest, ChatCommandRouter.Parse("/conquest").Kind);
            Assert.Equal(ChatCommandResultKind.ConquestRequest, ChatCommandRouter.Parse("/cq").Kind);
        }

        [Fact]
        public async Task Command_ConquestSends0x05AAndReportsTheAnswer()
        {
            var f = new ServiceFixture();
            var progression = new ProgressionState();
            f.Service.ProgressionModule = new ProgressionPacketModule(progression, f.Local, (mem, _) =>
            {
                f.Sent.Add(mem.ToArray());
                progression.UpdateConquest(new S2C_0x05E_Conquest(new byte[S2C_0x05E_Conquest.PayloadSize]));
                return Task.CompletedTask;
            });

            var result = await f.Service.ExecuteCommandAsync("/cq");

            Assert.True(result.Success);
            Assert.StartsWith("Conquest points: 0, Imperial Standing: 0", result.Message);
            AssertHeader(Assert.Single(f.Sent), 0x05A, 1);
        }

        [Fact]
        public async Task Command_ConquestWithoutAnAnswerWarns()
        {
            var f = new ServiceFixture();
            f.Service.ConquestReplyTimeout = TimeSpan.FromMilliseconds(30);
            f.Service.ProgressionModule = new ProgressionPacketModule(new ProgressionState(), f.Local, (mem, _) =>
            {
                f.Sent.Add(mem.ToArray());
                return Task.CompletedTask;
            });

            var result = await f.Service.ExecuteCommandAsync("/conquest");

            Assert.Equal(PlayerActionResultKind.Warning, result.Kind);
            Assert.Single(f.Sent);
        }

        [Fact]
        public async Task Command_WithoutAModuleFailsCleanly()
        {
            var f = new ServiceFixture();
            f.Service.CommandModule = null;
            var result = await f.Service.ExecuteCommandAsync("/heal");
            Assert.Equal(PlayerActionResultKind.Error, result.Kind);
            Assert.Empty(f.Sent);
        }

        [Fact]
        public void Help_ListsTheNewCommands()
        {
            var f = new ServiceFixture();
            string summary = f.Service.GetStandardCommandsSummary();
            foreach (string name in new[] { "/heal", "/sit ", "/sitchair", "/random", "/nominate", "/widescan" })
            {
                Assert.Contains(name, summary);
            }
            Assert.Contains("/heal", f.Service.GetStandardCommandsSummary("heal"));
        }

        // ---- message log text ----

        [Fact]
        public void DiceRoll_FormatsLandSandBoatsMessage88()
        {
            var msg = new SystemMessage(0, 0, StandardMessages.DiceRollMessage, 0, "string2 Ayame string3 523", DateTime.UtcNow);
            Assert.Equal("Ayame rolls 523.", StandardMessages.FormatMessage(msg));
        }

        [Fact]
        public void DiceRoll_WithUnexpectedDataFallsBackToTheGenericText()
        {
            var msg = new SystemMessage(0, 0, StandardMessages.DiceRollMessage, 0, "Para0 5", DateTime.UtcNow);
            Assert.Equal("Msg#88: Para0 5", StandardMessages.FormatMessage(msg));
        }

        [Fact]
        public void WideScanLog_ListsTheNearestFirstWithResolvedNames()
        {
            var entries = new[]
            {
                new WideScanEntry(0x20, 0, 1, 100, 0, string.Empty),
                new WideScanEntry(0x10, 12, 2, 3, 4, string.Empty),
                new WideScanEntry(0x30, 0, 0, -30, 0, "Pal"),
            };

            var lines = StockUiPlayerCommands.FormatWideScan(entries, i => i == 0x10 ? "Rarab" : null).ToList();

            Assert.Equal("Wide Scan: 3 found.", lines[0]);
            Assert.Equal("  Monster Lv.12 Rarab: 5 yalms (+3, +4)", lines[1]);
            Assert.Equal("  Player Pal: 30 yalms (-30, 0)", lines[2]);
            Assert.Equal("  NPC #32: 100 yalms (+100, 0)", lines[3]);
        }

        [Fact]
        public void WideScanLog_EmptyAndCapped()
        {
            Assert.Equal("Wide Scan: nothing found.", Assert.Single(StockUiPlayerCommands.FormatWideScan(Array.Empty<WideScanEntry>(), _ => null)));

            var many = Enumerable.Range(1, 50).Select(i => new WideScanEntry((ushort)i, 0, 2, (short)i, 0, string.Empty)).ToArray();
            var lines = StockUiPlayerCommands.FormatWideScan(many, _ => null).ToList();
            Assert.Equal(1 + StockUiPlayerCommands.MaxWideScanLines + 1, lines.Count);
            Assert.Equal("  ...and 10 more.", lines[^1]);
        }

        [Fact]
        public void ProposalLog_StartAndResult()
        {
            var proposal = new VoteProposal("Ayame", ProposalKind.Party, "Pizza?", new[] { "yes", "no" }, new[] { 3, 1 }, true);

            Assert.Equal(new[]
            {
                "Ayame proposes: Pizza?", "  1: yes", "  2: no", "Vote with /vote <number> Ayame"
            }, StockUiPlayerCommands.FormatProposalStart(proposal).ToArray());
            Assert.Equal(new[]
            {
                "Results of Ayame's proposal: Pizza?", "  1: yes (3 votes)", "  2: no (1 vote)"
            }, StockUiPlayerCommands.FormatProposalResult(proposal).ToArray());
        }

        [Fact]
        public void VoteText_ParsesTalliesAndPlainOptions()
        {
            VoteState.ParseText("[Q]\n1[3]:yes\n2[0]:no: maybe", out string q, out var options, out var votes);
            Assert.Equal("Q", q);
            Assert.Equal(new[] { "yes", "no: maybe" }, options);
            Assert.Equal(new[] { 3, 0 }, votes);

            VoteState.ParseText("just text", out q, out options, out _);
            Assert.Equal("just text", q);
            Assert.Empty(options);
        }
    }
}
