using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Gordian.Engine.Input;

public enum KeyboardMode { Full, Compact1, Compact2 }

/// <summary>Keyboard-only provenance. Absent override inherits; [] explicitly unbinds.
/// Unknown names and fields are retained; legacy profiles migrate on load with every key explicit.</summary>
public sealed class KeyboardLayout
{
    [JsonPropertyName("revision")] public int Revision { get; set; } = 1;
    [JsonPropertyName("mode")] public KeyboardMode Mode { get; set; }
    [JsonPropertyName("overrides")] public Dictionary<string, List<string>> Overrides { get; set; } = new(StringComparer.Ordinal);
    [JsonPropertyName("legacySnapshot")] public string? LegacySnapshot { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    /// <summary>One-time load migration. All known legacy actions, even missing ones, remain explicit.
    /// A separate confirmed restore releases them to inheritance. Original JSON is retained for rollback.</summary>
    internal static void MigrateLegacy(InputProfile profile, KeyboardMode mode, string originalJson)
    {
        if (profile.KeyboardLayout != null) throw new InvalidOperationException("Profile already has keyboard provenance.");
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        var layout = new KeyboardLayout { Mode = mode, LegacySnapshot = originalJson };
        foreach (var action in Enum.GetValues<InputAction>())
            if (action != InputAction.None)
                layout.Overrides[action.ToString()] = profile.GetChords(action).Where(IsKeyboard).Select(c => c.ToString()).ToList();
        using (var json = JsonDocument.Parse(profile.SaveToJson()))
            if (json.RootElement.TryGetProperty("bindings", out var bindings))
                foreach (var entry in bindings.EnumerateObject())
                    layout.Overrides[entry.Name] = entry.Value.EnumerateArray().Select(v => v.GetString()!)
                        .Where(n => !InputChord.TryParse(n, out var c) || IsKeyboard(c)).ToList();
        profile.KeyboardLayout = layout;
    }
    [JsonIgnore] public bool Supported => Revision == 1 && Enum.IsDefined(Mode);

    public static bool IsKeyboard(InputChord c) => c.Key != GordianKey.None && !c.IsGamepadChord && !c.IsMouseChord;

    /// <summary>Materializes only a deep session/view copy. The shared definition is never flattened.</summary>
    public static InputProfile Resolve(InputProfile source, KeyboardMode mode)
    {
        var result = source.Clone();
        if (source.Version != 1 || source.KeyboardLayout is not { Supported: true } layout || !Enum.IsDefined(mode)) return result;
        foreach (var chords in result.Bindings.Values) chords.RemoveAll(IsKeyboard);
        foreach (var (action, chords) in Catalog(mode).Bindings)
            foreach (var chord in chords.Where(IsKeyboard)) result.Bind(action, chord);
        var explicitKeys = new Dictionary<InputAction, List<InputChord>>();
        foreach (var (name, names) in layout.Overrides)
        {
            if (!Enum.TryParse<InputAction>(name, out var action) || !Enum.IsDefined(action)) continue;
            // Unknown chord names still replace the action; never silently refill a future binding.
            explicitKeys[action] = names.Where(n => InputChord.TryParse(n, out var c) && IsKeyboard(c))
                .Select(n => { InputChord.TryParse(n, out var c); return c; }).Distinct().ToList();
        }
        foreach (var action in explicitKeys.Keys)
        {
            if (result.Bindings.TryGetValue(action, out var chords)) chords.RemoveAll(IsKeyboard);
        }
        foreach (var (action, chords) in result.Bindings)
        {
            if (explicitKeys.ContainsKey(action)) continue;
            chords.RemoveAll(c => IsKeyboard(c) && explicitKeys.Any(p =>
                InputProfile.IsMenuNavigationAction(p.Key) == InputProfile.IsMenuNavigationAction(action) && p.Value.Contains(c)));
        }
        // Explicit/explicit collisions retain both definitions, but resolve deterministically by enum value.
        foreach (var (action, chords) in explicitKeys.OrderBy(p => p.Key))
            foreach (var chord in chords) result.Bind(action, chord);
        return result;
    }

    /// <summary>Independently authored from Square Enix PC manual pp.2-3 and March 27 2012 post 298313.
    /// Compact 1 is a documented partial community fallback, not certified retail parity; see docs/input/console-and-input.md.</summary>
    public static InputProfile Catalog(KeyboardMode mode)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        var p = mode == KeyboardMode.Full ? InputProfile.CreateFullNumpad() : InputProfile.CreateCompact();
        p.Bind(InputAction.OpenPartyChat, new InputChord(GordianKey.P, InputModifiers.Control));
        p.Bind(InputAction.OpenPartyChat, new InputChord(GordianKey.P, InputModifiers.Alt));
        foreach (var modifier in new[] { InputModifiers.Control, InputModifiers.Alt })
        {
            p.Bind(InputAction.ToggleAttack, new InputChord(GordianKey.A, modifier));
            p.Bind(InputAction.CheckTarget, new InputChord(GordianKey.C, modifier));
            p.Bind(InputAction.OpenPetCommands, new InputChord(GordianKey.B, modifier));
            p.Bind(InputAction.OpenEquipment, new InputChord(GordianKey.E, modifier));
            p.Bind(InputAction.OpenItems, new InputChord(GordianKey.I, modifier));
            p.Bind(InputAction.OpenJobAbilities, new InputChord(GordianKey.J, modifier));
            p.Bind(InputAction.OpenMagic, new InputChord(GordianKey.M, modifier));
            p.Bind(InputAction.OpenWeaponSkills, new InputChord(GordianKey.W, modifier));
            p.Bind(InputAction.ReplyToTell, new InputChord(GordianKey.R, modifier));
            p.Bind(InputAction.OpenSayChat, new InputChord(GordianKey.S, modifier));
            p.Bind(InputAction.OpenLinkshellChat, new InputChord(GordianKey.L, modifier));
            p.Bind(InputAction.OpenTellChat, new InputChord(GordianKey.T, modifier));
        }
        void Set(InputAction action, params InputChord[] chords)
        {
            p.Bindings[action] = chords.ToList();
        }
        InputChord K(GordianKey key) => new(key);
        if (mode == KeyboardMode.Full)
        {
            Set(InputAction.StrafeLeft, new InputChord(GordianKey.NumPad4, InputModifiers.Shift));
            Set(InputAction.StrafeRight, new InputChord(GordianKey.NumPad6, InputModifiers.Shift));
            Set(InputAction.ToggleAutorun, K(GordianKey.NumPad7));
            Set(InputAction.CameraZoomIn, K(GordianKey.PageUp), K(GordianKey.NumPad9));
            Set(InputAction.CameraZoomOut, K(GordianKey.PageDown), K(GordianKey.NumPad3));
            Set(InputAction.Confirm, K(GordianKey.Enter), K(GordianKey.NumPadEnter));
            Set(InputAction.Cancel, K(GordianKey.Escape));
            Set(InputAction.ToggleCameraMode, K(GordianKey.NumPad5));
            Set(InputAction.TargetNearest, K(GordianKey.Tab), K(GordianKey.NumPad0));
            Set(InputAction.ToggleLockOn);
            Set(InputAction.LockOnOrHeal, K(GordianKey.NumPadMultiply));
            Set(InputAction.OpenMenu, K(GordianKey.NumPadSubtract));
            Set(InputAction.ResetCamera, K(GordianKey.Home));
        }
        else
        {
            Set(InputAction.StrafeLeft, new InputChord(GordianKey.A, InputModifiers.Shift));
            Set(InputAction.StrafeRight, new InputChord(GordianKey.D, InputModifiers.Shift));
            // Enter and keypad Enter are Confirm in every layout (#673); the compact factory gives only Enter.
            p.Bind(InputAction.Confirm, K(GordianKey.NumPadEnter));
            if (mode == KeyboardMode.Compact1)
            {
                Set(InputAction.CameraPanLeft);
                Set(InputAction.CameraPanRight);
                Set(InputAction.ToggleAutorun); // community source leaves this blank; do not guess.
                Set(InputAction.ToggleWalkRun, K(GordianKey.R));
                Set(InputAction.Heal);
                Set(InputAction.LockOnOrHeal, K(GordianKey.H));
                // Community punctuation zoom directions conflict with official Compact 2. Explicit safe fallback.
                Set(InputAction.CameraZoomIn, K(GordianKey.PageUp));
                Set(InputAction.CameraZoomOut, K(GordianKey.PageDown));
                Set(InputAction.OpenMenu, K(GordianKey.OemMinus));
            }
            else
            {
                Set(InputAction.CameraPitchUp, K(GordianKey.Up));
                Set(InputAction.CameraPitchDown, K(GordianKey.Down));
                Set(InputAction.CameraYawLeft, K(GordianKey.Left));
                Set(InputAction.CameraYawRight, K(GordianKey.Right));
            }
        }
        foreach (var modifier in new[] { InputModifiers.Control, InputModifiers.Alt })
        {
            p.Bind(InputAction.OpenMenu, new InputChord(GordianKey.OemMinus, modifier));
            p.Bind(InputAction.ToggleCameraMode, new InputChord(GordianKey.V, modifier));
            p.Bind(InputAction.Heal, new InputChord(GordianKey.H, modifier));
        }
        p.Bind(InputAction.ToggleWindows, K(mode == KeyboardMode.Compact2 ? GordianKey.OemCloseBracket : GordianKey.ScrollLock));
        p.Bind(InputAction.Screenshot, K(mode == KeyboardMode.Compact2 ? GordianKey.OemOpenBracket : GordianKey.PrintScreen));
        if (mode == KeyboardMode.Full) p.Bind(InputAction.Logout, K(GordianKey.Pause));
        p.Bind(InputAction.TargetSelf, K(GordianKey.F10));
        p.Bind(InputAction.TargetAllianceFirst, K(GordianKey.F11));
        p.Bind(InputAction.TargetAllianceSecond, K(GordianKey.F12));
        p.Bind(InputAction.TargetSelf, K(GordianKey.F7));
        p.Bind(InputAction.TargetNearestNpc, K(GordianKey.F8));
        p.Bind(InputAction.TargetNearestPc, K(GordianKey.F9));
        return p;
    }
}
