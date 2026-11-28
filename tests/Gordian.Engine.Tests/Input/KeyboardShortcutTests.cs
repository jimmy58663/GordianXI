using System;
using System.Threading.Tasks;
using Gordian.Engine.Actions;
using Gordian.Engine.Input;
using Gordian.Engine.Ui;
using Gordian.Core.Network.Packets;
using Xunit;
using System.Numerics;
using System.Linq;
using Gordian.Core.World;
using Gordian.Core.Config;
using Gordian.Core.Resources.Ui;

namespace Gordian.Engine.Tests.Input;

public class KeyboardShortcutTests
{
    [Theory]
    [InlineData(KeyboardMode.Full)]
    [InlineData(KeyboardMode.Compact1)]
    [InlineData(KeyboardMode.Compact2)]
    public async Task Reply_UsesReceivedSenderNotOutgoingPartner_AndNeverSendsOnOpen(KeyboardMode mode)
    {
        foreach (var modifier in new[] { InputModifiers.Control, InputModifiers.Alt })
        {
            int sends = 0;
            var chat = new StockUiChat { Execute = (_, _) => { sends++; return Task.FromResult(PlayerActionResult.Ok("sent")); } };
            chat.OnChatMessage(new ChatMessage(ChatMessageType.Tell, "Incoming", "hello", 0, false, 0, 0, DateTime.UtcNow, false));
            await chat.SubmitAsync("/tell Outgoing hello", ChatInputMode.Say);
            Assert.Equal("Outgoing", chat.Input.TellTarget);
            Assert.True(chat.TryOpen(GordianKey.R, modifier, KeyboardLayout.Resolve(InputProfile.CreateDefault(), mode), false));
            Assert.Equal("/tell Incoming ", chat.Input.Text);
            Assert.Equal(1, sends);
            Assert.False(chat.TryOpen(GordianKey.R, modifier, KeyboardLayout.Resolve(InputProfile.CreateDefault(), mode), false));
            Assert.Equal("/tell Incoming ", chat.Input.Text);
        }
    }

    private static (PlayerLocomotionController Controller, InputState Input, PlayerActionService Service, WorldState World) Harness(KeyboardMode mode, PartyState? party = null, System.Collections.Concurrent.ConcurrentQueue<byte[]>? sent = null)
    {
        var world = new WorldState();
        var player = new LocalPlayerState { ServerId = 100 };
        world.UpsertEntity(new PlayerEntity(100, 1) { Name = "Self", IsSpawned = true, Hpp = 100 });
        Task Send(ReadOnlyMemory<byte> bytes, bool _) { if (sent == null) throw new InvalidOperationException("Unexpected network send"); sent.Enqueue(bytes.ToArray()); return Task.CompletedTask; }
        var service = new PlayerActionService(new SessionProfile(), world, player, new CombatPacketModule(new CombatState(), player, Send),
            new ChatPacketModule(Send), new PartyPacketModule(party ?? new PartyState(), Send), new EntityPacketModule(world, player, Send),
            new LifecyclePacketModule(new SessionProfile(), Send), Send);
        service.CommandModule = new PlayerCommandPacketModule(new PlayerCommandState(), world, Send);
        var input = new InputState();
        var controller = new PlayerLocomotionController(input, KeyboardLayout.Resolve(InputProfile.CreateDefault(), mode), world, player, service);
        return (controller, input, service, world);
    }

    [Theory]
    [InlineData(KeyboardMode.Full)] [InlineData(KeyboardMode.Compact1)] [InlineData(KeyboardMode.Compact2)]
    public void FunctionKeys_SelectSelfNearestNpcAndPc_WithoutTabCycling(KeyboardMode mode)
    {
        var (controller, input, service, world) = Harness(mode);
        world.UpsertEntity(new PlayerEntity(101, 2) { Name = "Other", IsSpawned = true, Hpp = 100, Position = new Vector3(0, 0, 2) });
        world.UpsertEntity(new WorldEntity(102, 3, EntityType.Npc) { Name = "Npc", IsSpawned = true, Position = new Vector3(0, 0, 4) });
        world.UpsertEntity(new WorldEntity(103, 4, EntityType.Monster) { Name = "Monster", IsSpawned = true, Hpp = 100, Position = new Vector3(0, 0, 3) });
        foreach (var (key, expected) in new[] { (GordianKey.F7, 100u), (GordianKey.F8, 103u), (GordianKey.F9, 101u), (GordianKey.F8, 103u) })
        {
            input.SetKeyDown(key); controller.Update(TimeSpan.FromMilliseconds(16));
            Assert.Equal(expected, service.CurrentTarget?.ServerId);
            input.SetKeyUp(key); controller.Update(TimeSpan.FromMilliseconds(16));
        }
        controller.Chat = new StockUiChat(); controller.Chat.Input.Open();
        input.SetKeyDown(GordianKey.F7); controller.Update(TimeSpan.FromMilliseconds(16));
        Assert.Equal(103u, service.CurrentTarget?.ServerId);
    }

    [Theory]
    [InlineData(KeyboardMode.Full)] [InlineData(KeyboardMode.Compact1)] [InlineData(KeyboardMode.Compact2)]
    public void MenuShortcuts_RouteThroughRealInputAndMenus_RespectTyping(KeyboardMode mode)
    {
        foreach (var modifier in new[] { GordianKey.LeftCtrl, GordianKey.LeftAlt })
        foreach (var (key, name, list) in new[] {
            (GordianKey.B, StockUiActionMenu.AbilityListMenu, StockUiActionList.PetCommands),
            (GordianKey.J, StockUiActionMenu.AbilityListMenu, StockUiActionList.JobAbilities),
            (GordianKey.W, StockUiActionMenu.AbilityListMenu, StockUiActionList.WeaponSkills),
            (GordianKey.M, StockUiActionMenu.SpellListMenu, StockUiActionList.Spells),
            (GordianKey.E, StockUiItems.EquipmentMenu, StockUiActionList.None),
            (GordianKey.I, StockUiItems.ListMenu, StockUiActionList.None),
            (GordianKey.OemMinus, StockUiMenuEntries.MainMenu, StockUiActionList.None) })
        {
            var (controller, input, service, _) = Harness(mode);
            service.Menus.Library = UiResourceLibrary.FromDefinitions(new[] { new UiMenuDefinition {
                Name = name, Frame = new UiMenuFrame { Width = 200, Height = 200 },
                Buttons = new[] { new UiMenuButton { ButtonId = 1, Width = 80, Height = 16 } } } });
            service.Menus.ActionRowsProvider = (_, _, _) => Array.Empty<StockUiActionRow>();
            input.SetKeyDown(modifier); input.SetKeyDown(key); controller.Update(TimeSpan.FromMilliseconds(16));
            Assert.Equal(name, service.Menus.Top?.Name);
            Assert.Equal(list, service.Menus.Top!.ActionList);
            service.Menus.CloseAll(); input.SetKeyUp(key); input.SetKeyUp(modifier); controller.Update(TimeSpan.FromMilliseconds(16));
            controller.Chat = new StockUiChat(); controller.Chat.Input.Open();
            input.SetKeyDown(modifier); input.SetKeyDown(key); controller.Update(TimeSpan.FromMilliseconds(16));
            Assert.False(service.Menus.IsOpen);
        }
    }

    [Theory]
    [InlineData(KeyboardMode.Full)] [InlineData(KeyboardMode.Compact1)] [InlineData(KeyboardMode.Compact2)]
    public void PartyFunctionKeys_SelectDisplayedFirstMembers_NotLeaderFlags(KeyboardMode mode)
    {
        var party = new PartyState();
        party.UpsertMember(new PartyMember { ServerId = 100, Name = "Self", PartyNumber = 1, MemberNumber = 3 });
        party.UpsertMember(new PartyMember { ServerId = 201, Name = "First", PartyNumber = 0, MemberNumber = 0 });
        party.UpsertMember(new PartyMember { ServerId = 202, Name = "Leader", PartyNumber = 0, MemberNumber = 1, IsLeader = true });
        party.UpsertMember(new PartyMember { ServerId = 203, Name = "Second", PartyNumber = 2, MemberNumber = 0 });
        var (controller, input, service, world) = Harness(mode, party);
        foreach (uint id in new[] { 201u, 202u, 203u })
            world.UpsertEntity(new PlayerEntity(id, (ushort)id) { Name = "Member" + id, IsSpawned = true, Hpp = 100 });
        foreach (var (key, expected) in new[] { (GordianKey.F10, 100u), (GordianKey.F11, 201u), (GordianKey.F12, 203u) })
        {
            input.SetKeyDown(key); controller.Update(TimeSpan.FromMilliseconds(16));
            Assert.Equal(expected, service.CurrentTarget?.ServerId);
            input.SetKeyUp(key); controller.Update(TimeSpan.FromMilliseconds(16));
        }
    }

    [Theory]
    [InlineData(KeyboardMode.Full)] [InlineData(KeyboardMode.Compact1)] [InlineData(KeyboardMode.Compact2)]
    public void ViewModifierAlias_TogglesActualCamera(KeyboardMode mode)
    {
        foreach (var modifier in new[] { GordianKey.LeftCtrl, GordianKey.LeftAlt })
        {
            var (controller, input, _, _) = Harness(mode);
            var before = controller.Camera.Mode;
            input.SetKeyDown(modifier); input.SetKeyDown(GordianKey.V); controller.Update(TimeSpan.FromMilliseconds(16));
            Assert.NotEqual(before, controller.Camera.Mode);
        }
    }

    [Theory]
    [InlineData(KeyboardMode.Full)] [InlineData(KeyboardMode.Compact1)] [InlineData(KeyboardMode.Compact2)]
    public void CombatShortcuts_UseExistingActionService_AndDoNotRepeatWhileHeld(KeyboardMode mode)
    {
        foreach (var modifier in new[] { GordianKey.LeftCtrl, GordianKey.LeftAlt })
        foreach (var key in new[] { GordianKey.A, GordianKey.C })
        {
            var sent = new System.Collections.Concurrent.ConcurrentQueue<byte[]>();
            var (controller, input, service, world) = Harness(mode, sent: sent);
            world.UpsertEntity(new WorldEntity(102, 3, EntityType.Monster) { Name = "Target", IsSpawned = true, Hpp = 100 });
            service.SetTargetByServerId(102);
            input.SetKeyDown(modifier); input.SetKeyDown(key); controller.Update(TimeSpan.FromMilliseconds(16));
            Assert.Single(sent);
            var packet = sent.Single();
            Assert.Equal(key == GordianKey.A ? 0x01A : 0x0DD, System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(packet) % 512);
            controller.Update(TimeSpan.FromMilliseconds(16)); Assert.Single(sent);
            input.SetKeyUp(key); controller.Update(TimeSpan.FromMilliseconds(16));
            if (key == GordianKey.A)
            {
                service.Menus.Library = UiResourceLibrary.FromDefinitions(new[] { new UiMenuDefinition { Name = StockUiCommandMenu.Template, Frame = new UiMenuFrame { Width = 112, Height = 200 } } });
                Assert.True(service.OpenTargetCommandMenu());
                input.SetKeyDown(key); controller.Update(TimeSpan.FromMilliseconds(16));
                Assert.Equal(2, sent.Count);
                Assert.False(service.Combat!.IsEngaged);
            }
        }
    }

    [Theory]
    [InlineData(KeyboardMode.Full)] [InlineData(KeyboardMode.Compact1)] [InlineData(KeyboardMode.Compact2)]
    public void DisplayShortcuts_PreserveVisibilityOwners_UseExistingScreenshotHandler(KeyboardMode mode)
    {
        var (controller, input, service, _) = Harness(mode);
        var hide = mode == KeyboardMode.Compact2 ? GordianKey.OemCloseBracket : GordianKey.ScrollLock;
        Assert.True(Enum.TryParse<GordianKey>(mode == KeyboardMode.Compact2 ? "OemOpenBracket" : "PrintScreen", out var shot));
        service.UiVisibility.Hide("addon", StockUiElement.Party);
        service.UiVisibility.Hide(StockUiVisibilityState.UserOwner, StockUiElement.Buffs);
        var before = service.UiVisibility.Hidden;
        int shots = 0;
        service.ScreenshotHandler = request => { Assert.False(request.HideUi); shots++; return true; };
        input.SetKeyDown(hide); controller.Update(TimeSpan.FromMilliseconds(16));
        Assert.Equal(StockUiElement.All, service.UiVisibility.Hidden);
        input.SetKeyUp(hide); controller.Update(TimeSpan.FromMilliseconds(16));
        input.SetKeyDown(shot); controller.Update(TimeSpan.FromMilliseconds(16));
        Assert.Equal(1, shots);
        input.SetKeyUp(shot); input.SetKeyDown(hide); controller.Update(TimeSpan.FromMilliseconds(16));
        Assert.Equal(before, service.UiVisibility.Hidden);
        input.SetKeyUp(hide); controller.Update(TimeSpan.FromMilliseconds(16));
        controller.Chat = new StockUiChat(); controller.Chat.Input.Open();
        input.SetKeyDown(hide); input.SetKeyDown(shot); controller.Update(TimeSpan.FromMilliseconds(16));
        Assert.Equal(before, service.UiVisibility.Hidden); Assert.Equal(1, shots);
    }

    [Theory]
    [InlineData(KeyboardMode.Full)] [InlineData(KeyboardMode.Compact1)] [InlineData(KeyboardMode.Compact2)]
    public async Task HealAlias_AndFullPause_RouteExistingRequests(KeyboardMode mode)
    {
        foreach (var modifier in new[] { GordianKey.LeftCtrl, GordianKey.LeftAlt })
        {
            var sent = new System.Collections.Concurrent.ConcurrentQueue<byte[]>();
            var (controller, input, _, _) = Harness(mode, sent: sent);
            Assert.True(controller.Profile.TryGetAction(new InputChord(GordianKey.H,
                modifier == GordianKey.LeftCtrl ? InputModifiers.Control : InputModifiers.Alt), out var action));
            Assert.Equal(InputAction.Heal, action);
            input.SetKeyDown(modifier); input.SetKeyDown(GordianKey.H); controller.Update(TimeSpan.FromMilliseconds(16));
            for (int i = 0; i < 100 && sent.IsEmpty; i++) await Task.Delay(10, TestContext.Current.CancellationToken);
            Assert.Equal(0x0E8, System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(Assert.Single(sent)) % 512);
        }
        if (mode != KeyboardMode.Full) return; // Pause is verified by the Full manual only.
        {
            var sent = new System.Collections.Concurrent.ConcurrentQueue<byte[]>();
            var (controller, input, _, _) = Harness(mode, sent: sent);
            input.SetKeyDown(GordianKey.Pause); controller.Update(TimeSpan.FromMilliseconds(16));
            for (int i = 0; i < 100 && sent.IsEmpty; i++) await Task.Delay(10, TestContext.Current.CancellationToken);
            Assert.Equal(0x0E7, System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(Assert.Single(sent)) % 512);
        }
    }
}
