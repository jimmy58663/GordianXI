// tests/Gordian.App.Tests/Services/SdlGamepadDriverTests.cs
using System;
using Gordian.App.Services;
using Gordian.Core.Input;
using Xunit;

namespace Gordian.App.Tests.Services
{
    public sealed class SdlGamepadDriverTests : IDisposable
    {
        private readonly SdlGamepadDriver _driver;

        public SdlGamepadDriverTests()
        {
            _driver = new SdlGamepadDriver();
        }

        public void Dispose()
        {
            _driver.Dispose();
        }

        [Fact]
        public void Driver_InitializesWithSilkSdl()
        {
            // On Windows (and desktop platforms with bundled SDL runtime), driver should be available
            Assert.True(_driver.IsAvailable);
            Assert.True(_driver.RumbleEnabled);
        }

        [Fact]
        public void Poll_DisconnectedOrEmptySlot_ReturnsDisconnected()
        {
            // Slot 0 (if no physical controller attached) or out-of-range slot returns Disconnected
            var state = _driver.Poll(3);
            Assert.False(state.IsConnected);
            Assert.Equal(GamepadButton.None, state.Buttons);

            var invalidNegative = _driver.Poll(-1);
            Assert.False(invalidNegative.IsConnected);

            var invalidTooHigh = _driver.Poll(4);
            Assert.False(invalidTooHigh.IsConnected);
        }

        [Fact]
        public void RumbleEnabled_ToggleAndSetVibration_SucceedsWithoutThrowing()
        {
            // Setting vibration on slot 0
            _driver.SetVibration(0, 0.5f, 0.5f);

            // Disabling rumble should silence motors and not throw
            _driver.RumbleEnabled = false;
            Assert.False(_driver.RumbleEnabled);

            _driver.SetVibration(0, 0.8f, 0.8f);

            // Re-enabling
            _driver.RumbleEnabled = true;
            Assert.True(_driver.RumbleEnabled);
        }
    }
}
