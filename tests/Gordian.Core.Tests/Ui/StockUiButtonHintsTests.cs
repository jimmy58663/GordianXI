// tests/Gordian.Core.Tests/Ui/StockUiButtonHintsTests.cs
using System;
using Gordian.Core.Input;
using Gordian.Core.Resources.Ui;
using Gordian.Core.Ui;
using Xunit;

namespace Gordian.Core.Tests.Ui
{
    public class StockUiButtonHintsTests
    {
        [Fact]
        public void Label_FollowsTheDeviceInUse_AndTheUsersMapping()
        {
            var profile = InputProfile.CreateGamepadDefault();
            const InputAction action = StockUiButtonHints.OptionsAction;
            Assert.Equal("+", StockUiButtonHints.Label(profile, action, gamepad: false));
            Assert.Equal("Y", StockUiButtonHints.Label(profile, action, gamepad: true));
            Assert.Equal("Triangle", StockUiButtonHints.Label(profile, action, gamepad: true, playStation: true));

            // Remapped: the pad's X, the keyboard's F5; a hint updates with the profile (it is read every frame).
            profile.Unbind(action, new InputChord(GamepadButton.Y));
            profile.Bind(action, new InputChord(GamepadButton.X));
            profile.Unbind(action, new InputChord(GordianKey.NumPadAdd));
            profile.Bind(action, new InputChord(GordianKey.F5));
            Assert.Equal("X", StockUiButtonHints.Label(profile, action, gamepad: true));
            Assert.Equal("Square", StockUiButtonHints.Label(profile, action, gamepad: true, playStation: true));
            Assert.Equal("F5", StockUiButtonHints.Label(profile, action, gamepad: false));

            // Nothing bound for the device in use falls back to the other, then to a dash.
            var keyboardOnly = InputProfile.CreateCompact();
            keyboardOnly.Unbind(action, new InputChord(GamepadButton.Y));
            Assert.Equal("+", StockUiButtonHints.Label(keyboardOnly, action, gamepad: true));
            keyboardOnly.Unbind(action, new InputChord(GordianKey.NumPadAdd));
            Assert.Equal("-", StockUiButtonHints.Label(keyboardOnly, action, gamepad: false));
        }

        [Fact]
        public void Hint_UsesTheRoundFaceButtonSpriteForAPadFaceButton()
        {
            var profile = InputProfile.CreateGamepadDefault();
            const InputAction action = StockUiButtonHints.OptionsAction;
            var xbox = StockUiButtonHints.Hint(profile, action, gamepad: true);
            Assert.True(xbox.HasXboxFace); // an Xbox controller: the lettered button
            Assert.Equal('Y', xbox.XboxFace);
            Assert.False(xbox.HasSprite);
            var pad = StockUiButtonHints.Hint(profile, action, gamepad: true, playStation: true);
            Assert.True(pad.HasSprite);
            Assert.Equal((16, 16), (pad.SpriteX, pad.SpriteY)); // a PlayStation controller: Y sits where the Triangle does
            Assert.False(StockUiButtonHints.Hint(profile, action, gamepad: false).HasSprite);
            profile.Unbind(action, new InputChord(GamepadButton.Y));
            profile.Bind(action, new InputChord(GamepadButton.LeftShoulder));
            Assert.False(StockUiButtonHints.Hint(profile, action, gamepad: true).HasSprite); // a shoulder button stays text
        }

        [Theory]
        [InlineData("Xbox 360 Controller", false)]
        [InlineData("PS5 Controller", true)]
        [InlineData("DualSense Wireless Controller", true)]
        [InlineData("Wireless Controller", true)]
        [InlineData("", false)]
        public void IsPlayStation_ReadsTheDeviceName(string name, bool expected) => Assert.Equal(expected, StockUiButtonHints.IsPlayStation(name));

        [Fact]
        public void IsHintGlyph_MatchesThePlusAndTheTriangleOnly()
        {
            UiSpritePart Part(string texture, int x, int y, ushort sx, ushort sy, ushort w, ushort h) => new()
            {
                TextureName = texture, TopLeft = new UiPoint((short)x, (short)y), SourceX = sx, SourceY = sy, SourceWidth = w, SourceHeight = h,
            };
            Assert.True(StockUiButtonHints.IsHintGlyph(Part("menu    font    ", 7, 8, 111, 1, 8, 10)));        // "+" (itmsort2, lnowin, mcrledit, ...)
            Assert.True(StockUiButtonHints.IsHintGlyph(Part("menu    marubatu", 4, 4, 16, 16, 16, 16)));        // the PS2 Triangle (mgcsort)
            Assert.False(StockUiButtonHints.IsHintGlyph(Part("menu    font    ", 28, 8, 22, 52, 9, 12)));       // the "S" of "Sort"
            Assert.False(StockUiButtonHints.IsHintGlyph(Part("menu    marubatu", 100, 40, 16, 16, 16, 16)));    // a marubatu sprite elsewhere
        }
    }
}
