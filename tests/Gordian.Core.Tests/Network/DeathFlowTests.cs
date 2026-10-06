// tests/Gordian.Core.Tests/Network/DeathFlowTests.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Gordian.Core.Actions;
using Gordian.Core.Config;
using Gordian.Core.Input;
using Gordian.Core.Network;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources.Ui;
using Gordian.Core.Ui;
using Gordian.Core.World;
using Xunit;

namespace Gordian.Core.Tests.Network
{
    /// <summary>
    /// The death flow (#103): C2S 0x01A HomepointMenu / RaiseMenu / TractorMenu, S2C 0x0F9 (GP_SERV_COMMAND_RES), the
    /// player's death menu state, the slash commands and the dead window with its prompts.
    /// Layouts from XiPackets world/client/0x001A and world/server/0x00F9.
    /// </summary>
    public class DeathFlowTests
    {
        private const uint PlayerId = 0x01020304;
        private const ushort PlayerIndex = 10;

        #region Encoders

        private static void AssertAction(byte[] packet, ushort sequence, CliActionId action, uint statusId, uint playerId = PlayerId, ushort playerIndex = PlayerIndex)
        {
            Assert.True(PacketHeader.TryParse(packet.AsSpan(0, 4), out var hdr));
            Assert.Equal(0x01A, hdr.PacketId);
            Assert.Equal(28, hdr.TotalSize);
            Assert.Equal(sequence, hdr.SequenceId);
            Assert.Equal(playerId, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(4, 4)));
            Assert.Equal(playerIndex, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(8, 2)));
            Assert.Equal((ushort)action, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(10, 2)));
            Assert.Equal(statusId, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(12, 4)));
            Assert.All(packet.AsSpan(16, 12).ToArray(), b => Assert.Equal(0, b)); // ActionBuf[1..3]
        }

        [Fact]
        public void BuildHomepointMenuRequest_WritesActionIdAndStatusId()
        {
            byte[] buffer = new byte[32];
            buffer.AsSpan().Fill(0xCC);

            int len = CombatPacketBuilder.BuildHomepointMenuRequest(buffer, 7, PlayerId, PlayerIndex);

            Assert.Equal(28, len);
            AssertAction(buffer[..28], 7, CliActionId.HomepointMenu, 0);
            // The exact bytes: header (0x01A, 28 bytes, seq 7), UniqueNo, ActIndex, ActionID 0x0B, ActionBuf[0] = 0.
            Assert.Equal(new byte[] { 0x1A, 0x0E, 0x07, 0x00, 0x04, 0x03, 0x02, 0x01, 0x0A, 0x00, 0x0B, 0x00, 0, 0, 0, 0 }, buffer[..16]);
        }

        [Theory]
        [InlineData(HomepointMenuChoice.MonstrosityCancel, 1u)]
        [InlineData(HomepointMenuChoice.MonstrosityRetry, 2u)]
        public void BuildHomepointMenuRequest_MonstrosityChoices(HomepointMenuChoice choice, uint statusId)
        {
            byte[] buffer = new byte[28];
            CombatPacketBuilder.BuildHomepointMenuRequest(buffer, 1, PlayerId, PlayerIndex, choice);
            AssertAction(buffer, 1, CliActionId.HomepointMenu, statusId);
        }

        [Theory]
        [InlineData(ReviveMenuAnswer.Accept, 0u)]
        [InlineData(ReviveMenuAnswer.Decline, 1u)]
        public void BuildRaiseAndTractorMenuRequests_WriteTheAnswer(ReviveMenuAnswer answer, uint statusId)
        {
            byte[] raise = new byte[28];
            raise.AsSpan().Fill(0xCC);
            Assert.Equal(28, CombatPacketBuilder.BuildRaiseMenuRequest(raise, 3, PlayerId, PlayerIndex, answer));
            AssertAction(raise, 3, CliActionId.RaiseMenu, statusId);
            Assert.Equal(0x0D, raise[10]);

            byte[] tractor = new byte[28];
            tractor.AsSpan().Fill(0xCC);
            Assert.Equal(28, CombatPacketBuilder.BuildTractorMenuRequest(tractor, 4, PlayerId, PlayerIndex, answer));
            AssertAction(tractor, 4, CliActionId.TractorMenu, statusId);
            Assert.Equal(0x13, tractor[10]);
        }

        #endregion

        #region S2C 0x0F9

        private static byte[] Res(ushort type, uint id = PlayerId, ushort index = PlayerIndex)
        {
            byte[] payload = new byte[8];
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), id);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(4, 2), index);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(6, 2), type);
            return payload;
        }

        [Theory]
        [InlineData(0, DeathMenuType.HomePoint)]
        [InlineData(1, DeathMenuType.Raise)]
        [InlineData(2, DeathMenuType.Tractor)]
        [InlineData(3, DeathMenuType.HomePoint)] // XiPackets: any other value is handled as 0
        [InlineData(0xFFFF, DeathMenuType.HomePoint)]
        public void S2C_0x0F9_Res_DecodesTheXiPacketsLayout(ushort type, DeathMenuType expected)
        {
            // LandSandBoat's 0x0f9_res.cpp: UniqueNo, ActIndex, then type, as a 12-byte packet.
            var res = new S2C_0x0F9_Res(Res(type, 0x0A0B0C0D, 1792));
            Assert.True(res.IsValid);
            Assert.Equal(0x0A0B0C0Du, res.UniqueNo);
            Assert.Equal(1792, res.ActIndex);
            Assert.Equal(type, res.RawType);
            Assert.Equal(expected, res.Type);
        }

        [Fact]
        public void S2C_0x0F9_Res_RejectsAShortPayload()
        {
            Assert.False(new S2C_0x0F9_Res(new byte[7]).IsValid);
            Assert.False(new S2C_0x0F9_Res(ReadOnlySpan<byte>.Empty).IsValid);
        }

        [Fact]
        public void CombatModule_AppliesRes_ToTheLocalPlayer()
        {
            var player = new LocalPlayerState { ServerId = PlayerId };
            var module = new CombatPacketModule(new CombatState(), player, (_, _) => Task.CompletedTask);
            var dispatcher = new PacketDispatcher();
            module.Register(dispatcher);
            var seen = new List<DeathMenuType>();
            player.DeathMenuChanged += seen.Add;

            dispatcher.Dispatch(new PacketHeader(S2C_0x0F9_Res.PacketId, 12, 1), Res(1));
            Assert.Equal(DeathMenuType.Raise, player.DeathMenu);
            // The client does not read UniqueNo (XiPackets): a packet naming another id still applies.
            dispatcher.Dispatch(new PacketHeader(S2C_0x0F9_Res.PacketId, 12, 2), Res(2, 0x0999));
            Assert.Equal(DeathMenuType.Tractor, player.DeathMenu);
            dispatcher.Dispatch(new PacketHeader(S2C_0x0F9_Res.PacketId, 12, 3), Res(0));
            Assert.Equal(DeathMenuType.HomePoint, player.DeathMenu);
            Assert.Equal(new[] { DeathMenuType.Raise, DeathMenuType.Tractor, DeathMenuType.HomePoint }, seen);

            module.Unregister(dispatcher);
        }

        #endregion

        #region Player state

        private static void SendStatus(LocalPlayerState player, byte status, uint homepointSeconds = 0)
        {
            var payload = new byte[0x60];
            payload[44] = status;
            if (homepointSeconds > 0) BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(56, 4), (homepointSeconds + 360) * 60);
            player.UpdateFromCharStatus(new S2C_0x037_CharStatus(payload));
        }

        [Fact]
        public void DeathMenu_RepeatsRaiseOffers_AndClearsWhenAlive()
        {
            var player = new LocalPlayerState { ServerId = PlayerId };
            var seen = new List<DeathMenuType>();
            player.DeathMenuChanged += seen.Add;

            SendStatus(player, LocalPlayerState.StatusDead, 3600);
            Assert.True(player.IsDead);
            Assert.Equal(3600u, player.HomepointSecondsRemaining);

            player.ApplyDeathMenu(DeathMenuType.Raise);
            player.ApplyDeathMenu(DeathMenuType.Raise); // a second offer reopens the prompt
            player.ApplyDeathMenu(DeathMenuType.HomePoint);
            player.ApplyDeathMenu(DeathMenuType.HomePoint); // no news
            Assert.Equal(new[] { DeathMenuType.Raise, DeathMenuType.Raise, DeathMenuType.HomePoint }, seen);

            player.ApplyDeathMenu(DeathMenuType.Tractor);
            SendStatus(player, 0); // raised or home-pointed
            Assert.False(player.IsDead);
            Assert.Equal(DeathMenuType.HomePoint, player.DeathMenu);
        }

        #endregion

        #region Chat commands

        [Theory]
        [InlineData("/homepoint", ChatCommandResultKind.HomePoint, 0)]
        [InlineData("/acceptraise", ChatCommandResultKind.RaiseAnswer, (int)ReviveMenuAnswer.Accept)]
        [InlineData("/acceptraise yes", ChatCommandResultKind.RaiseAnswer, (int)ReviveMenuAnswer.Accept)]
        [InlineData("/acceptraise decline", ChatCommandResultKind.RaiseAnswer, (int)ReviveMenuAnswer.Decline)]
        [InlineData("/accepttractor", ChatCommandResultKind.TractorAnswer, (int)ReviveMenuAnswer.Accept)]
        [InlineData("/accepttractor no", ChatCommandResultKind.TractorAnswer, (int)ReviveMenuAnswer.Decline)]
        [InlineData("/acceptraise maybe", ChatCommandResultKind.LocalNotice, 0)]
        public void Router_ParsesTheDeathCommands(string input, ChatCommandResultKind kind, int param)
        {
            var result = ChatCommandRouter.Parse(input);
            Assert.Equal(kind, result.Kind);
            if (kind != ChatCommandResultKind.LocalNotice) Assert.Equal(param, result.ActionParam);
        }

        #endregion

        #region Action service and the dead window

        private sealed class Harness
        {
            public readonly LocalPlayerState Player = new() { ServerId = PlayerId };
            public readonly WorldState World = new();
            public readonly List<byte[]> Sent = new();
            public readonly PlayerActionService Actions;

            public Harness(bool withUi = true)
            {
                var profile = new SessionProfile();
                Task Capture(ReadOnlyMemory<byte> chunk, bool urgent)
                {
                    lock (Sent) Sent.Add(chunk.ToArray());
                    return Task.CompletedTask;
                }
                Actions = new PlayerActionService(profile, World, Player,
                    new CombatPacketModule(new CombatState(), Player, Capture),
                    new ChatPacketModule(Capture),
                    new PartyPacketModule(new PartyState(), Capture),
                    new EntityPacketModule(World, Player, Capture),
                    new LifecyclePacketModule(profile, Capture),
                    Capture);
                World.UpsertEntity(new WorldEntity(PlayerId, PlayerIndex, EntityType.Player) { Name = "Tester", Position = Vector3.Zero });
                if (withUi) Actions.Menus.Library = Library();
            }

            public byte[][] SentSnapshot()
            {
                lock (Sent) return Sent.ToArray();
            }
        }

        /// <summary>The DAT windows the flow uses, as the retail DAT authors them: "dead" (one button), "comyn" and the main menu.</summary>
        private static UiResourceLibrary Library()
        {
            static UiMenuButton Button(short id, short x, short y, sbyte left, sbyte right) => new()
            {
                ButtonId = id, X = x, Y = y, Width = 88, Height = 16, NavUp = (sbyte)id, NavDown = (sbyte)id, NavLeft = left, NavRight = right,
            };
            return UiResourceLibrary.FromDefinitions(new[]
            {
                new UiMenuDefinition
                {
                    Name = StockUiDeathMenu.MenuName,
                    Frame = new UiMenuFrame { X = 16, Y = 94, Width = 128, Height = 44, Anchor = UiAnchor.TopLeft },
                    Buttons = new List<UiMenuButton> { new() { ButtonId = 1, X = 4, Y = 23, Width = 120, Height = 16, NavUp = 1, NavDown = 1, NavLeft = 1, NavRight = 1 } },
                },
                new UiMenuDefinition
                {
                    Name = StockUiMenuEntries.MessageYesNoMenu,
                    Frame = new UiMenuFrame { X = 130, Y = 208, Width = 252, Height = 88, Anchor = UiAnchor.BottomLeft },
                    Buttons = new List<UiMenuButton> { Button(1, 58, 66, 2, 2), Button(2, 154, 66, 1, 1) },
                },
                new UiMenuDefinition
                {
                    Name = StockUiMenuEntries.MainMenu,
                    Frame = new UiMenuFrame { X = 384, Y = 48, Width = 112, Height = 60, Anchor = UiAnchor.TopRight },
                    Buttons = new List<UiMenuButton> { Button(1, 16, 6, 1, 1) },
                },
            });
        }

        private static async Task<byte[][]> WaitForSentAsync(Harness h, int count)
        {
            for (int i = 0; i < 200; i++)
            {
                var sent = h.SentSnapshot();
                if (sent.Length >= count) return sent;
                await Task.Delay(10);
            }
            return h.SentSnapshot();
        }

        private static async Task WaitUntilAsync(Func<bool> condition)
        {
            for (int i = 0; i < 200 && !condition(); i++) await Task.Delay(10);
            Assert.True(condition());
        }

        [Fact]
        public async Task HomePointAsync_SendsOnlyWhileDead()
        {
            var h = new Harness(withUi: false);
            var alive = await h.Actions.HomePointAsync();
            Assert.Equal(PlayerActionResultKind.Warning, alive.Kind);
            Assert.Empty(h.SentSnapshot());

            SendStatus(h.Player, LocalPlayerState.StatusDead);
            var result = await h.Actions.ExecuteCommandAsync("/homepoint");
            Assert.Equal(PlayerActionResultKind.Success, result.Kind);
            AssertAction(Assert.Single(h.SentSnapshot()), 1, CliActionId.HomepointMenu, 0);
        }

        [Fact]
        public async Task AnswerDeathOffer_SendsTheAnswer_AndClearsTheOffer()
        {
            var h = new Harness(withUi: false);
            SendStatus(h.Player, LocalPlayerState.StatusDead);
            h.Player.ApplyDeathMenu(DeathMenuType.Raise);

            var declined = await h.Actions.ExecuteCommandAsync("/acceptraise decline");
            Assert.Equal(PlayerActionResultKind.Success, declined.Kind);
            AssertAction(Assert.Single(h.SentSnapshot()), 1, CliActionId.RaiseMenu, 1);
            Assert.Equal(DeathMenuType.HomePoint, h.Player.DeathMenu);

            h.Player.ApplyDeathMenu(DeathMenuType.Tractor);
            await h.Actions.ExecuteCommandAsync("/accepttractor");
            AssertAction(h.SentSnapshot()[1], 2, CliActionId.TractorMenu, 0);

            // Without a recorded offer the answer still goes (the server keeps its own), with a note.
            var unoffered = await h.Actions.AnswerDeathOfferAsync(DeathMenuType.Raise, accept: true);
            Assert.Equal(PlayerActionResultKind.Notice, unoffered.Kind);
            AssertAction(h.SentSnapshot()[2], 3, CliActionId.RaiseMenu, 0);

            SendStatus(h.Player, 0);
            var alive = await h.Actions.AnswerDeathOfferAsync(DeathMenuType.Raise, accept: true);
            Assert.Equal(PlayerActionResultKind.Warning, alive.Kind);
            Assert.Equal(3, h.SentSnapshot().Length);
        }

        [Fact]
        public void DeadWindow_OpensOnDeath_StaysOnCancel_AndClosesWhenAlive()
        {
            var h = new Harness();
            var menus = h.Actions.Menus;
            Assert.False(menus.IsOpen);

            SendStatus(h.Player, LocalPlayerState.StatusDead, 3570);
            Assert.True(h.Actions.DeathMenu.IsOpen);
            var window = Assert.Single(menus.OpenMenus);
            Assert.Equal(StockUiDeathMenu.MenuName, window.Name);
            Assert.True(window.Pinned);
            Assert.Equal(StockUiDeathMenu.HomePointButton, window.SelectedButtonId);
            Assert.Equal("59:30", window.TitleSuffix!());

            menus.CloseTop();
            menus.CloseAll();
            Assert.Same(window, Assert.Single(menus.OpenMenus));

            // The main menu still opens over it, and Cancel goes back to it.
            Assert.True(menus.OpenMainMenu());
            Assert.Equal(StockUiMenuEntries.MainMenu, menus.Top!.Name);
            menus.CloseTop();
            Assert.Same(window, menus.Top);

            SendStatus(h.Player, 0);
            Assert.False(menus.IsOpen);
            Assert.False(h.Actions.DeathMenu.IsOpen);
        }

        [Fact]
        public async Task DeadWindow_BackToHomePoint_AsksFirst()
        {
            var h = new Harness();
            var menus = h.Actions.Menus;
            SendStatus(h.Player, LocalPlayerState.StatusDead);

            menus.Activate(); // Back to Home Point
            var prompt = menus.Top!;
            Assert.True(prompt.IsPrompt);
            Assert.Equal(StockUiDeathMenu.HomePointQuestion, prompt.Message);
            Assert.Equal(2, prompt.SelectedButtonId); // No under the cursor

            menus.Activate(); // No
            await Task.Delay(50);
            Assert.Empty(h.SentSnapshot());
            Assert.Equal(StockUiDeathMenu.MenuName, Assert.Single(menus.OpenMenus).Name);

            menus.Activate();
            menus.Move(InputAction.MenuLeft);
            menus.Activate(); // Yes
            var sent = await WaitForSentAsync(h, 1);
            AssertAction(Assert.Single(sent), 1, CliActionId.HomepointMenu, 0);
        }

        [Fact]
        public async Task RaiseOffer_OpensAPrompt_YesAccepts()
        {
            var h = new Harness();
            var menus = h.Actions.Menus;
            SendStatus(h.Player, LocalPlayerState.StatusDead);

            h.Player.ApplyDeathMenu(DeathMenuType.Raise);
            var prompt = menus.Top!;
            Assert.True(prompt.IsPrompt);
            Assert.Equal(StockUiDeathMenu.RaiseQuestion, prompt.Message);
            Assert.Equal(1, prompt.SelectedButtonId); // Yes under the cursor

            menus.Activate();
            var sent = await WaitForSentAsync(h, 1);
            AssertAction(Assert.Single(sent), 1, CliActionId.RaiseMenu, 0);
            await WaitUntilAsync(() => h.Player.DeathMenu == DeathMenuType.HomePoint);
            Assert.Equal(StockUiDeathMenu.MenuName, Assert.Single(menus.OpenMenus).Name);
        }

        [Fact]
        public async Task TractorOffer_NoDeclines_CancelLeavesItOpen()
        {
            var h = new Harness();
            var menus = h.Actions.Menus;
            SendStatus(h.Player, LocalPlayerState.StatusDead);

            h.Player.ApplyDeathMenu(DeathMenuType.Tractor);
            Assert.Equal(StockUiDeathMenu.TractorQuestion, menus.Top!.Message);
            menus.CloseTop(); // Cancel: no answer
            await Task.Delay(50);
            Assert.Empty(h.SentSnapshot());
            Assert.Equal(DeathMenuType.Tractor, h.Player.DeathMenu);

            h.Player.ApplyDeathMenu(DeathMenuType.Tractor); // offered again: the prompt comes back
            Assert.Equal(StockUiDeathMenu.TractorQuestion, menus.Top!.Message);
            menus.Move(InputAction.MenuRight);
            menus.Activate(); // No
            var sent = await WaitForSentAsync(h, 1);
            AssertAction(Assert.Single(sent), 1, CliActionId.TractorMenu, 1);
        }

        [Fact]
        public void OfferWithdrawn_ClosesThePrompt_AndRevivalClosesEverything()
        {
            var h = new Harness();
            var menus = h.Actions.Menus;
            SendStatus(h.Player, LocalPlayerState.StatusDead);
            h.Player.ApplyDeathMenu(DeathMenuType.Raise);
            Assert.Equal(2, menus.OpenMenus.Count);

            h.Player.ApplyDeathMenu(DeathMenuType.HomePoint);
            Assert.Equal(StockUiDeathMenu.MenuName, Assert.Single(menus.OpenMenus).Name);

            h.Player.ApplyDeathMenu(DeathMenuType.Raise);
            SendStatus(h.Player, 0);
            Assert.Empty(menus.OpenMenus);
            Assert.Empty(h.SentSnapshot());
        }

        [Fact]
        public void DeadBeforeTheUiLoaded_OpensOnTheNextStatus_WithThePendingOffer()
        {
            var h = new Harness(withUi: false);
            var menus = h.Actions.Menus;
            SendStatus(h.Player, LocalPlayerState.StatusDead);
            h.Player.ApplyDeathMenu(DeathMenuType.Raise);
            Assert.False(menus.IsOpen);

            menus.Library = Library();
            SendStatus(h.Player, LocalPlayerState.StatusDead, 1200);
            Assert.Equal(2, menus.OpenMenus.Count);
            Assert.Equal(StockUiDeathMenu.MenuName, menus.OpenMenus[0].Name);
            Assert.Equal(StockUiDeathMenu.RaiseQuestion, menus.Top!.Message);
        }

        [Fact]
        public void FormatTimeLeft_MinutesAndSeconds()
        {
            Assert.Equal("60:00", StockUiDeathMenu.FormatTimeLeft(3600));
            Assert.Equal("0:05", StockUiDeathMenu.FormatTimeLeft(5));
        }

        #endregion
    }
}
