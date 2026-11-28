using System;
using System.Linq;
using Gordian.Core.Network;
using Gordian.Core.World;
using Gordian.Engine.Input;
using Gordian.Engine.Sessions;
using Xunit;

namespace Gordian.Engine.Tests.Input;

public class ControllerFollowupTests
{
    [Theory]
    [InlineData(GamepadButton.DPadDown)]
    [InlineData(GamepadButton.DPadUp)]
    public void VerticalDPad_NoTarget_SelectsSelf_ThenCyclesOwnParty(GamepadButton button)
    {
        var hub = new InputProfileHub(InputProfile.CreateCompact());
        using var session = Session(hub);
        session.World.UpsertEntity(new PlayerEntity(2, 2) { Name = "Second", IsSpawned = true });
        session.World.UpsertEntity(new PlayerEntity(3, 3) { Name = "Third", IsSpawned = true });
        session.World.UpsertEntity(new PlayerEntity(4, 4) { Name = "Alliance", IsSpawned = true });
        session.Party.UpsertMember(new PartyMember { ServerId = 1, MemberNumber = 0 });
        session.Party.UpsertMember(new PartyMember { ServerId = 3, MemberNumber = 2 });
        session.Party.UpsertMember(new PartyMember { ServerId = 2, MemberNumber = 1 });
        session.Party.UpsertMember(new PartyMember { ServerId = 5, MemberNumber = 3 }); // absent from zone
        session.Party.UpsertMember(new PartyMember { ServerId = 4, MemberNumber = 0, PartyNumber = 1 });
        Press(session, button);
        Assert.Equal(1u, session.ActionService.CurrentTarget?.ServerId);
        Press(session, button);
        Assert.Equal(button == GamepadButton.DPadDown ? 2u : 3u, session.ActionService.CurrentTarget?.ServerId);
        Press(session, button);
        Assert.Equal(button == GamepadButton.DPadDown ? 3u : 2u, session.ActionService.CurrentTarget?.ServerId);
        Press(session, button);
        Assert.Equal(1u, session.ActionService.CurrentTarget?.ServerId);
        session.ActionService.SetTargetByServerId(4); // unrelated target starts at self
        Press(session, button);
        Assert.Equal(1u, session.ActionService.CurrentTarget?.ServerId);
    }

    [Fact]
    public void Migration_PreservesLaterFixedSlotChoice_AndCustomChords()
    {
        var profile = InputProfile.CreateCompact();
        // Simulate pre-correction defaults without using an owner's settings.
        profile.Bindings.Remove(InputAction.TargetPartyUp);
        profile.Bindings.Remove(InputAction.TargetPartyDown);
        profile.Bind(InputAction.TargetParty1, new InputChord(GamepadButton.DPadUp));
        profile.Bind(InputAction.TargetParty2, new InputChord(GamepadButton.DPadDown));
        profile.Bind(InputAction.TargetParty1, new InputChord(GamepadButton.RightThumb, InputModifiers.Control));
        profile.GamepadSettings.InvertCameraY = true;
        // Incoming old-default D-pad profiles can also contain explicit custom shoulders.
        var autorun = Function(InputAction.ToggleAutorun);
        GamepadAssignments.Assign(profile, autorun, GamepadButton.LeftShoulder);
        profile = InputProfile.FromJson(profile.SaveToJson());
        Assert.Equal("LB", GamepadAssignments.Describe(profile, autorun));
        using (var session = Session(new InputProfileHub(profile)))
        {
            Press(session, GamepadButton.RightShoulder);
            Assert.False(session.InputState.AutorunActive);
            Press(session, GamepadButton.LeftShoulder);
            Assert.True(session.InputState.AutorunActive);
            Press(session, GamepadButton.LeftShoulder);
            Assert.False(session.InputState.AutorunActive);
            Press(session, GamepadButton.DPadDown);
            Assert.Equal(1u, session.ActionService.CurrentTarget?.ServerId);
        }
        Assert.True(profile.TryGetAction(new InputChord(GamepadButton.DPadDown), out var action));
        Assert.Equal(InputAction.TargetPartyDown, action);
        Assert.Contains(new InputChord(GordianKey.F3), profile.GetChords(InputAction.TargetParty2));
        Assert.Contains(new InputChord(GamepadButton.RightThumb, InputModifiers.Control), profile.GetChords(InputAction.TargetParty1));
        Assert.True(profile.GamepadSettings.InvertCameraY);
        // An explicit fixed-slot choice after migration must not become cycling on reload/session copy.
        profile.Unbind(InputAction.TargetPartyDown, new InputChord(GamepadButton.DPadDown));
        profile.Bind(InputAction.TargetParty2, new InputChord(GamepadButton.DPadDown));
        profile = InputProfile.FromJson(profile.SaveToJson());
        Assert.True(profile.TryGetAction(new InputChord(GamepadButton.DPadDown), out action));
        Assert.Equal(InputAction.TargetParty2, action);
    }

    [Theory]
    [InlineData(GamepadButton.DPadUp)]
    [InlineData(GamepadButton.DPadDown)]
    public void SoloAndMacroContext_KeepPartyTargetingScoped(GamepadButton button)
    {
        var profile = InputProfile.CreateCompact();
        using var session = Session(new InputProfileHub(profile));
        Press(session, button);
        Assert.Equal(1u, session.ActionService.CurrentTarget?.ServerId);
        session.ActionService.ClearTarget();
        Press(session, button | GamepadButton.LeftTrigger);
        Assert.Null(session.ActionService.CurrentTarget);
        Assert.True(profile.TryGetAction(new InputChord(button), menuContext: true, out var action));
        Assert.Equal(button == GamepadButton.DPadUp ? InputAction.MenuUp : InputAction.MenuDown, action);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShoulderLabel_MatchesEffectiveBinding_AfterSessionRefreshAndDiskReload(bool custom)
    {
        var profile = InputProfile.CreateCompact();
        var hub = new InputProfileHub(profile);
        using var session = Session(hub);
        var function = Function(InputAction.ToggleAutorun);
        if (custom) hub.Edit(p => GamepadAssignments.Assign(p, function, GamepadButton.LeftShoulder));
        var expectedButton = custom ? GamepadButton.LeftShoulder : GamepadButton.RightShoulder;
        Assert.Equal(custom ? "LB" : "RB", GamepadAssignments.Describe(hub.Profile, function));
        Press(session, expectedButton);
        Assert.True(session.InputState.AutorunActive);
        Press(session, expectedButton);
        Assert.False(session.InputState.AutorunActive);
        Press(session, custom ? GamepadButton.RightShoulder : GamepadButton.LeftShoulder);
        Assert.False(session.InputState.AutorunActive);
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "controller-" + Guid.NewGuid() + ".json");
        try
        {
            hub.Profile.SaveToFile(path);
            hub.SetProfile(InputProfile.LoadOrCreate(path));
            Assert.Equal(custom ? "LB" : "RB", GamepadAssignments.Describe(hub.Profile, function));
            Press(session, expectedButton);
            Assert.True(session.InputState.AutorunActive);
            Press(session, expectedButton); // off
            Press(session, expectedButton | GamepadButton.RightTrigger);
            Assert.False(session.InputState.AutorunActive); // macro palette consumes shoulders
        }
        finally { System.IO.File.Delete(path); }
    }

    [Fact]
    public void CustomVerticalBindingAndFixedKeyboardSlots_ArePreserved()
    {
        var profile = InputProfile.CreateCompact();
        GamepadAssignments.Assign(profile, Function(InputAction.TargetPartyDown), GamepadButton.RightShoulder);
        var hub = new InputProfileHub(profile);
        using var session = Session(hub);
        Press(session, GamepadButton.RightShoulder);
        Assert.Equal(1u, session.ActionService.CurrentTarget?.ServerId);
        Assert.False(session.InputState.AutorunActive);
        Assert.Equal("RB", GamepadAssignments.Describe(hub.Profile, Function(InputAction.TargetPartyDown)));
        Assert.True(profile.TryGetAction(new InputChord(GordianKey.F3), out var action));
        Assert.Equal(InputAction.TargetParty2, action);
        var old = InputProfile.CreateCompact();
        old.Bindings.Remove(InputAction.TargetPartyUp);
        old.Bindings.Remove(InputAction.TargetPartyDown);
        old.Bind(InputAction.ToggleWalkRun, new InputChord(GamepadButton.DPadDown));
        old.Bind(InputAction.TargetParty2, new InputChord(GamepadButton.RightThumb));
        var loaded = InputProfile.FromJson(old.SaveToJson());
        Assert.Contains(new InputChord(GamepadButton.RightThumb), loaded.GetChords(InputAction.TargetParty2));
        Assert.True(loaded.TryGetAction(new InputChord(GamepadButton.DPadDown), out action));
        Assert.Equal(InputAction.ToggleWalkRun, action);
    }

    [Fact]
    public void SubTargetCursor_UsesMenuDirection_WithoutChangingMainTarget()
    {
        using var session = Session(new InputProfileHub(InputProfile.CreateCompact()));
        session.World.UpsertEntity(new PlayerEntity(2, 2) { Name = "Second", IsSpawned = true });
        session.Party.UpsertMember(new PartyMember { ServerId = 1, MemberNumber = 0 });
        session.Party.UpsertMember(new PartyMember { ServerId = 2, MemberNumber = 1 });
        session.ActionService.SetTargetByServerId(1);
        Assert.True(session.ActionService.BeginSubTarget("Fixture", Gordian.Engine.Actions.ActionTargetFlags.Self | Gordian.Engine.Actions.ActionTargetFlags.Party,
            _ => throw new InvalidOperationException("Selection must not execute an action")).Success);
        Press(session, GamepadButton.DPadDown);
        Assert.Equal(2u, session.ActionService.SubTargeting?.Current.ServerId);
        Assert.Equal(1u, session.ActionService.CurrentTarget?.ServerId);
        Press(session, GamepadButton.DPadUp);
        Assert.Equal(1u, session.ActionService.SubTargeting?.Current.ServerId);
    }

    private static GamepadFunction Function(InputAction action) => GamepadAssignments.Functions.Single(f => f.Actions.Contains(action));

    internal static CharacterSession Session(InputProfileHub hub)
    {
        var session = new CharacterSession("ControllerFixture_" + Guid.NewGuid().ToString("N"), 1,
            "fixture", new SessionNetworkManager("127.0.0.1", 54230), hub);
        session.LocalPlayer.ServerId = 1;
        session.World.UpsertEntity(new PlayerEntity(1, 1) { Name = "Self", IsSpawned = true });
        return session;
    }

    internal static void Press(CharacterSession session, GamepadButton button)
    {
        session.InputState.SetGamepadState(new GamepadState(true, GamepadButton.None, default, default, 0, 0));
        session.Locomotion.Update(TimeSpan.FromMilliseconds(16));
        session.InputState.SetGamepadState(new GamepadState(true, button, default, default, 0, 0));
        session.Locomotion.Update(TimeSpan.FromMilliseconds(16));
    }
}
