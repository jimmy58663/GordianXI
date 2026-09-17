// tests/Gordian.App.Tests/Services/AvaloniaInputMapperTests.cs
using Avalonia.Input;
using Gordian.App.Services;
using Gordian.Core.Input;
using Xunit;

namespace Gordian.App.Tests.Services
{
    public sealed class AvaloniaInputMapperTests
    {
        [Fact]
        public void ToGordianKey_MapsAlphabetAndDigitsCorrectly()
        {
            Assert.Equal(GordianKey.W, AvaloniaInputMapper.ToGordianKey(Key.W));
            Assert.Equal(GordianKey.A, AvaloniaInputMapper.ToGordianKey(Key.A));
            Assert.Equal(GordianKey.S, AvaloniaInputMapper.ToGordianKey(Key.S));
            Assert.Equal(GordianKey.D, AvaloniaInputMapper.ToGordianKey(Key.D));
            Assert.Equal(GordianKey.D1, AvaloniaInputMapper.ToGordianKey(Key.D1));
            Assert.Equal(GordianKey.D0, AvaloniaInputMapper.ToGordianKey(Key.D0));
        }

        [Fact]
        public void ToGordianKey_MapsNumpadKeysCorrectly()
        {
            Assert.Equal(GordianKey.NumPad8, AvaloniaInputMapper.ToGordianKey(Key.NumPad8));
            Assert.Equal(GordianKey.NumPad2, AvaloniaInputMapper.ToGordianKey(Key.NumPad2));
            Assert.Equal(GordianKey.NumPad4, AvaloniaInputMapper.ToGordianKey(Key.NumPad4));
            Assert.Equal(GordianKey.NumPad6, AvaloniaInputMapper.ToGordianKey(Key.NumPad6));
            Assert.Equal(GordianKey.NumPad5, AvaloniaInputMapper.ToGordianKey(Key.NumPad5));
            Assert.Equal(GordianKey.NumPad0, AvaloniaInputMapper.ToGordianKey(Key.NumPad0));
            Assert.Equal(GordianKey.NumPadAdd, AvaloniaInputMapper.ToGordianKey(Key.Add));
            Assert.Equal(GordianKey.NumPadSubtract, AvaloniaInputMapper.ToGordianKey(Key.Subtract));
            Assert.Equal(GordianKey.NumPadDivide, AvaloniaInputMapper.ToGordianKey(Key.Divide));
        }

        [Fact]
        public void ToGordianKey_MapsNavigationAndFunctionsCorrectly()
        {
            Assert.Equal(GordianKey.Tab, AvaloniaInputMapper.ToGordianKey(Key.Tab));
            Assert.Equal(GordianKey.Escape, AvaloniaInputMapper.ToGordianKey(Key.Escape));
            Assert.Equal(GordianKey.Enter, AvaloniaInputMapper.ToGordianKey(Key.Enter));
            Assert.Equal(GordianKey.Space, AvaloniaInputMapper.ToGordianKey(Key.Space));
            Assert.Equal(GordianKey.F1, AvaloniaInputMapper.ToGordianKey(Key.F1));
            Assert.Equal(GordianKey.F6, AvaloniaInputMapper.ToGordianKey(Key.F6));
            Assert.Equal(GordianKey.PageUp, AvaloniaInputMapper.ToGordianKey(Key.PageUp));
            Assert.Equal(GordianKey.PageDown, AvaloniaInputMapper.ToGordianKey(Key.PageDown));
            Assert.Equal(GordianKey.End, AvaloniaInputMapper.ToGordianKey(Key.End));
        }

        [Fact]
        public void ToInputModifiers_CombinesFlagsCorrectly()
        {
            var none = AvaloniaInputMapper.ToInputModifiers(KeyModifiers.None);
            Assert.Equal(InputModifiers.None, none);

            var shift = AvaloniaInputMapper.ToInputModifiers(KeyModifiers.Shift);
            Assert.Equal(InputModifiers.Shift, shift);

            var ctrlShift = AvaloniaInputMapper.ToInputModifiers(KeyModifiers.Control | KeyModifiers.Shift);
            Assert.Equal(InputModifiers.Control | InputModifiers.Shift, ctrlShift);

            var alt = AvaloniaInputMapper.ToInputModifiers(KeyModifiers.Alt);
            Assert.Equal(InputModifiers.Alt, alt);
        }

        [Fact]
        public void ToMouseButton_MapsButtonsCorrectly()
        {
            Assert.Equal(Gordian.Core.Input.MouseButton.Left, AvaloniaInputMapper.ToMouseButton(Avalonia.Input.MouseButton.Left));
            Assert.Equal(Gordian.Core.Input.MouseButton.Right, AvaloniaInputMapper.ToMouseButton(Avalonia.Input.MouseButton.Right));
            Assert.Equal(Gordian.Core.Input.MouseButton.Middle, AvaloniaInputMapper.ToMouseButton(Avalonia.Input.MouseButton.Middle));
        }
    }
}
