// src/Gordian.Core/Input/InputChord.cs
// Clean-room input chord abstraction for GordianXI.

using System;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Gordian.Core.Input
{
    /// <summary>
    /// Represents a specific physical input trigger: a keyboard key or mouse button combined with modifier keys.
    /// Immutable and suitable as a dictionary key or serialized mapping definition.
    /// </summary>
    [JsonConverter(typeof(InputChordJsonConverter))]
    public readonly struct InputChord : IEquatable<InputChord>
    {
        public GordianKey Key { get; }
        public MouseButton MouseButton { get; }
        public GamepadButton GamepadButton { get; }
        public InputModifiers Modifiers { get; }

        public bool IsMouseChord => MouseButton != MouseButton.None;
        public bool IsGamepadChord => GamepadButton != GamepadButton.None;
        public bool IsEmpty => Key == GordianKey.None && MouseButton == MouseButton.None && GamepadButton == GamepadButton.None;

        public InputChord(GordianKey key, InputModifiers modifiers = InputModifiers.None)
        {
            Key = key;
            MouseButton = MouseButton.None;
            GamepadButton = GamepadButton.None;
            Modifiers = modifiers;
        }

        public InputChord(MouseButton mouseButton, InputModifiers modifiers = InputModifiers.None)
        {
            Key = GordianKey.None;
            MouseButton = mouseButton;
            GamepadButton = GamepadButton.None;
            Modifiers = modifiers;
        }

        public InputChord(GamepadButton gamepadButton, InputModifiers modifiers = InputModifiers.None)
        {
            Key = GordianKey.None;
            MouseButton = MouseButton.None;
            GamepadButton = gamepadButton;
            Modifiers = modifiers;
        }

        public bool Equals(InputChord other)
        {
            return Key == other.Key &&
                   MouseButton == other.MouseButton &&
                   GamepadButton == other.GamepadButton &&
                   Modifiers == other.Modifiers;
        }

        public override bool Equals(object? obj) => obj is InputChord other && Equals(other);

        public override int GetHashCode() => HashCode.Combine((ushort)Key, (byte)MouseButton, (uint)GamepadButton, (byte)Modifiers);

        public static bool operator ==(InputChord left, InputChord right) => left.Equals(right);
        public static bool operator !=(InputChord left, InputChord right) => !left.Equals(right);

        public override string ToString()
        {
            if (IsEmpty) return "None";

            var sb = new StringBuilder();

            if (Modifiers.HasFlag(InputModifiers.Control)) sb.Append("Ctrl+");
            if (Modifiers.HasFlag(InputModifiers.Alt)) sb.Append("Alt+");
            if (Modifiers.HasFlag(InputModifiers.Shift)) sb.Append("Shift+");
            if (Modifiers.HasFlag(InputModifiers.Super)) sb.Append("Super+");

            if (IsGamepadChord)
            {
                sb.Append("Pad:").Append(GamepadButton);
            }
            else if (IsMouseChord)
            {
                sb.Append("Mouse").Append(MouseButton);
            }
            else
            {
                sb.Append(Key);
            }

            return sb.ToString();
        }

        public static InputChord Parse(string text)
        {
            if (TryParse(text, out var chord))
            {
                return chord;
            }
            throw new FormatException($"Invalid input chord string: '{text}'");
        }

        public static bool TryParse(string? text, out InputChord chord)
        {
            chord = default;
            if (string.IsNullOrWhiteSpace(text) || text.Equals("None", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0) return false;

            InputModifiers modifiers = InputModifiers.None;
            string mainPart = parts[^1];

            for (int i = 0; i < parts.Length - 1; i++)
            {
                string mod = parts[i];
                if (mod.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || mod.Equals("Control", StringComparison.OrdinalIgnoreCase))
                    modifiers |= InputModifiers.Control;
                else if (mod.Equals("Alt", StringComparison.OrdinalIgnoreCase))
                    modifiers |= InputModifiers.Alt;
                else if (mod.Equals("Shift", StringComparison.OrdinalIgnoreCase))
                    modifiers |= InputModifiers.Shift;
                else if (mod.Equals("Super", StringComparison.OrdinalIgnoreCase) || mod.Equals("Win", StringComparison.OrdinalIgnoreCase) || mod.Equals("Cmd", StringComparison.OrdinalIgnoreCase))
                    modifiers |= InputModifiers.Super;
                else
                    return false;
            }

            // Check if gamepad chord (e.g. "Pad:A", "Pad:LeftShoulder", "Gamepad:X", "Pad:LB")
            if (mainPart.StartsWith("Pad:", StringComparison.OrdinalIgnoreCase) || mainPart.StartsWith("Gamepad:", StringComparison.OrdinalIgnoreCase))
            {
                int colonIdx = mainPart.IndexOf(':');
                string padBtnStr = mainPart.Substring(colonIdx + 1).Trim();

                // Match friendly gamepad aliases
                padBtnStr = padBtnStr.ToUpperInvariant() switch
                {
                    "LB" => nameof(GamepadButton.LeftShoulder),
                    "RB" => nameof(GamepadButton.RightShoulder),
                    "LT" => nameof(GamepadButton.LeftTrigger),
                    "RT" => nameof(GamepadButton.RightTrigger),
                    "L3" => nameof(GamepadButton.LeftThumb),
                    "R3" => nameof(GamepadButton.RightThumb),
                    "SELECT" or "VIEW" or "SHARE" => nameof(GamepadButton.Back),
                    "CROSS" => nameof(GamepadButton.A),
                    "CIRCLE" => nameof(GamepadButton.B),
                    "SQUARE" => nameof(GamepadButton.X),
                    "TRIANGLE" => nameof(GamepadButton.Y),
                    _ => padBtnStr
                };

                if (Enum.TryParse<GamepadButton>(padBtnStr, true, out var padBtn) && padBtn != GamepadButton.None)
                {
                    chord = new InputChord(padBtn, modifiers);
                    return true;
                }
            }

            // Check if mouse chord
            if (mainPart.StartsWith("Mouse", StringComparison.OrdinalIgnoreCase))
            {
                string btnStr = mainPart.Substring(5);
                if (Enum.TryParse<MouseButton>(btnStr, true, out var btn) && btn != MouseButton.None)
                {
                    chord = new InputChord(btn, modifiers);
                    return true;
                }
            }

            // Otherwise keyboard key
            if (Enum.TryParse<GordianKey>(mainPart, true, out var key) && key != GordianKey.None)
            {
                chord = new InputChord(key, modifiers);
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// JSON string converter for <see cref="InputChord"/>.
    /// </summary>
    public sealed class InputChordJsonConverter : JsonConverter<InputChord>
    {
        public override InputChord Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            string? str = reader.GetString();
            if (str != null && InputChord.TryParse(str, out var chord))
            {
                return chord;
            }
            return default;
        }

        public override void Write(Utf8JsonWriter writer, InputChord value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString());
        }
    }
}
