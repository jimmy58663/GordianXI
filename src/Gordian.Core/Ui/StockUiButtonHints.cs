// src/Gordian.Core/Ui/StockUiButtonHints.cs
using System;
using Gordian.Core.Input;
using Gordian.Core.Resources.Ui;

namespace Gordian.Core.Ui
{
    /// <summary>
    /// The one source of the key / button hints the stock menus show (#360). The menu DAT bakes a hint into the frame art
    /// of the windows that use the select-window key ("+ : Sort", "+ : Line Edit", "+ : Spoils Options"): a "+" glyph
    /// (the keyboard's keypad plus), or on the PS2 variants (the spell list's <c>mgcsort</c>) a Triangle sprite. Retail
    /// draws whatever the device in use has there; here the glyph is left out of the frame
    /// (<see cref="IsHintGlyph"/>) and <see cref="Label"/> is drawn in its place: the bound keyboard key while the
    /// keyboard is in use, the bound gamepad button (Xbox names, PlayStation names for a PlayStation controller) while a
    /// gamepad is, following the user's mapping and updating live.
    /// </summary>
    /// <summary>
    /// A hint to draw in the key glyph's place: text, or (when <see cref="SpriteX"/> is not negative) the round button sprite
    /// at that cell of the <c>marubatu</c> texture (the PS2 symbols the DAT's own Triangle hint comes from).
    /// </summary>
    public readonly record struct StockUiHint(string Text, int SpriteX = -1, int SpriteY = -1)
    {
        public bool HasSprite => SpriteX >= 0;
    }

    public static class StockUiButtonHints
    {
        /// <summary>
        /// The 16 x 16 cells of <c>marubatu</c> (32 x 32, measured from the texture): Circle (0, 0), Cross (16, 0), Square (0, 16),
        /// Triangle (16, 16).
        /// </summary>
        public static (int X, int Y)? FaceSprite(GamepadButton button) => button switch
        {
            GamepadButton.A => (16, 0),
            GamepadButton.B => (0, 0),
            GamepadButton.X => (0, 16),
            GamepadButton.Y => (16, 16),
            _ => null,
        };

        /// <summary>
        /// The hint for <paramref name="action"/>: while a gamepad is in use and its bound button is a face button (A / B / X /
        /// Y, which sit where the PlayStation's Cross / Circle / Square / Triangle do), that button's round sprite as retail's
        /// PS2 hint draws it (#360); otherwise <see cref="Label"/> as text.
        /// </summary>
        public static StockUiHint Hint(InputProfile profile, InputAction action, bool gamepad, bool playStation = false)
        {
            ArgumentNullException.ThrowIfNull(profile);
            string text = Label(profile, action, gamepad, playStation);
            if (!gamepad) return new StockUiHint(text);
            foreach (var chord in profile.GetChords(action))
            {
                if (chord.IsGamepadChord && chord.Modifiers == InputModifiers.None && FaceSprite(chord.GamepadButton) is { } cell)
                {
                    return new StockUiHint(text, cell.X, cell.Y);
                }
            }
            return new StockUiHint(text);
        }

        /// <summary>The action every baked hint stands for: select log window / the menus' "options" key (keypad +, gamepad Y).</summary>
        public const InputAction OptionsAction = InputAction.CycleLogWindow;

        /// <summary>The room the hint glyph has in the frame: from x 4 to x 22 (the ":" follows), y 4 to y 20.</summary>
        public const int GlyphLeft = 4, GlyphRight = 22, GlyphTop = 4, GlyphBottom = 20;

        /// <summary>
        /// Whether a frame image part is the hint's key glyph: the font "+" at (7, 8) 8 x 10, or the PS2 Triangle
        /// (<c>marubatu</c> 16 x 16 at (4, 4)).
        /// </summary>
        public static bool IsHintGlyph(UiSpritePart part)
        {
            ArgumentNullException.ThrowIfNull(part);
            int x = part.TopLeft.X, y = part.TopLeft.Y;
            string texture = part.TextureName;
            if (texture.Contains("marubatu", StringComparison.OrdinalIgnoreCase))
            {
                return x == 4 && y == 4 && part.SourceWidth == 16 && part.SourceHeight == 16;
            }
            return texture.TrimEnd().EndsWith("font", StringComparison.OrdinalIgnoreCase)
                && x == 7 && y == 8 && part.SourceX == 111 && part.SourceY == 1 && part.SourceWidth == 8 && part.SourceHeight == 10;
        }

        /// <summary>Whether a frame image carries a baked hint glyph.</summary>
        public static bool HasHintGlyph(UiImage? image)
        {
            if (image == null) return false;
            foreach (var part in image.Parts)
            {
                if (IsHintGlyph(part)) return true;
            }
            return false;
        }

        /// <summary>
        /// The text for <paramref name="action"/>'s hint: the first plain gamepad button bound to it when
        /// <paramref name="gamepad"/> is in use (falling back to the keyboard key when none is), else the first keyboard
        /// key (falling back to the gamepad button, then "-"). Short, as the glyph's room is small.
        /// </summary>
        public static string Label(InputProfile profile, InputAction action, bool gamepad, bool playStation = false)
        {
            ArgumentNullException.ThrowIfNull(profile);
            string? key = null, pad = null;
            foreach (var chord in profile.GetChords(action))
            {
                if (chord.IsGamepadChord)
                {
                    if (pad == null && chord.Modifiers == InputModifiers.None) pad = PadName(chord.GamepadButton, playStation);
                }
                else if (!chord.IsMouseChord && key == null)
                {
                    key = KeyName(chord);
                }
            }
            return (gamepad ? pad ?? key : key ?? pad) ?? "-";
        }

        /// <summary>Whether a controller's reported name is a PlayStation one (DualShock, DualSense, "Wireless Controller").</summary>
        public static bool IsPlayStation(string? deviceName) =>
            !string.IsNullOrEmpty(deviceName)
            && (deviceName.Contains("DualSense", StringComparison.OrdinalIgnoreCase)
                || deviceName.Contains("DualShock", StringComparison.OrdinalIgnoreCase)
                || deviceName.Contains("PS4", StringComparison.OrdinalIgnoreCase)
                || deviceName.Contains("PS5", StringComparison.OrdinalIgnoreCase)
                || deviceName.Contains("PlayStation", StringComparison.OrdinalIgnoreCase)
                || deviceName.Equals("Wireless Controller", StringComparison.OrdinalIgnoreCase));

        /// <summary>A gamepad button's hint name: the Xbox letters, or the PlayStation symbols' short names.</summary>
        public static string PadName(GamepadButton button, bool playStation)
        {
            if (!playStation) return GamepadAssignments.ButtonName(button);
            return button switch
            {
                GamepadButton.A => "Cross",
                GamepadButton.B => "Circle",
                GamepadButton.X => "Square",
                GamepadButton.Y => "Triangle",
                GamepadButton.LeftShoulder => "L1",
                GamepadButton.RightShoulder => "R1",
                GamepadButton.LeftTrigger => "L2",
                GamepadButton.RightTrigger => "R2",
                _ => GamepadAssignments.ButtonName(button),
            };
        }

        /// <summary>A keyboard chord's hint name: "+" for the keypad's plus, "Ctrl+X" style with modifiers.</summary>
        public static string KeyName(InputChord chord)
        {
            string name = chord.Key switch
            {
                GordianKey.NumPadAdd => "+",
                GordianKey.NumPadSubtract => "-",
                GordianKey.NumPadMultiply => "*",
                GordianKey.NumPadDivide => "/",
                _ => chord.Key.ToString(),
            };
            if (name.StartsWith("NumPad", StringComparison.Ordinal) && name.Length > 6) name = "Num" + name[6..];
            string prefix = (chord.Modifiers.HasFlag(InputModifiers.Control) ? "Ctrl+" : string.Empty)
                + (chord.Modifiers.HasFlag(InputModifiers.Alt) ? "Alt+" : string.Empty)
                + (chord.Modifiers.HasFlag(InputModifiers.Shift) ? "Shift+" : string.Empty);
            return prefix + name;
        }
    }
}
