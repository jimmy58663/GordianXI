using Gordian.App.Services;
using Gordian.Core.Network;
using Xunit;

namespace Gordian.App.Tests.Services
{
    /// <summary>The gamepad follows the focused viewport window, main or popped out (#150).</summary>
    public class InputFocusTrackerTests
    {
        private static CharacterSession NewSession(string name, uint id) =>
            new(name, id, $"user_{name}", new SessionNetworkManager("127.0.0.1", 54230));

        [Fact]
        public void NothingTracked_FallsBackToThePrimaryCharacter()
        {
            var tracker = new InputFocusTracker();
            var primary = NewSession("Gordian", 3);

            Assert.False(tracker.IsViewportFocused);
            Assert.Same(primary, tracker.ResolveGamepadTarget(primary));
        }

        [Fact]
        public void GamepadFollowsTheFocusedWindow_NotTheLastOpened()
        {
            var tracker = new InputFocusTracker();
            var gordian = NewSession("Gordian", 3);
            var knot = NewSession("Knot", 1);
            var claude = NewSession("Claude", 4);
            object main = new(), popOutKnot = new(), popOutClaude = new();

            tracker.SetWindowSession(main, gordian);
            tracker.SetWindowSession(popOutKnot, knot);
            tracker.SetWindowSession(popOutClaude, claude); // opened last

            tracker.Activated(main);
            Assert.Same(gordian, tracker.ResolveGamepadTarget(fallback: null));

            tracker.Deactivated(main);
            tracker.Activated(popOutKnot);
            Assert.Same(knot, tracker.ResolveGamepadTarget(fallback: null));

            tracker.Deactivated(popOutKnot);
            tracker.Activated(main);
            Assert.Same(gordian, tracker.ResolveGamepadTarget(fallback: claude));
        }

        [Fact]
        public void NoWindowFocused_KeepsTheLastFocusedCharacter()
        {
            var tracker = new InputFocusTracker();
            var gordian = NewSession("Gordian", 3);
            var knot = NewSession("Knot", 1);
            object main = new(), popOut = new();
            tracker.SetWindowSession(main, gordian);
            tracker.SetWindowSession(popOut, knot);

            tracker.Activated(popOut);
            tracker.Deactivated(popOut); // e.g. the control panel or another program took the focus

            Assert.False(tracker.IsViewportFocused);
            Assert.Null(tracker.FocusedSession);
            Assert.Same(knot, tracker.LastFocusedSession);
            Assert.Same(knot, tracker.ResolveGamepadTarget(fallback: gordian));
        }

        [Fact]
        public void SwitchingCharacterInTheFocusedWindow_MovesTheGamepad()
        {
            var tracker = new InputFocusTracker();
            var gordian = NewSession("Gordian", 3);
            var knot = NewSession("Knot", 1);
            object main = new();
            tracker.SetWindowSession(main, gordian);
            tracker.Activated(main);

            tracker.SetWindowSession(main, knot); // Ctrl+Tab

            Assert.Same(knot, tracker.FocusedSession);
            Assert.Same(knot, tracker.ResolveGamepadTarget(fallback: gordian));
        }

        [Fact]
        public void FocusedWindowWithoutACharacter_DrivesNobody()
        {
            var tracker = new InputFocusTracker();
            var gordian = NewSession("Gordian", 3);
            object main = new();
            tracker.SetWindowSession(main, null); // the lobby is on show
            tracker.Activated(main);

            Assert.True(tracker.IsViewportFocused);
            Assert.Null(tracker.ResolveGamepadTarget(fallback: gordian));
        }

        [Fact]
        public void ClosedWindow_IsForgotten()
        {
            var tracker = new InputFocusTracker();
            var gordian = NewSession("Gordian", 3);
            var knot = NewSession("Knot", 1);
            object main = new(), popOut = new();
            tracker.SetWindowSession(main, gordian);
            tracker.SetWindowSession(popOut, knot);
            tracker.Activated(popOut);

            tracker.Remove(popOut);

            Assert.False(tracker.IsViewportFocused);
            Assert.Null(tracker.LastFocusedSession);
            Assert.Same(gordian, tracker.ResolveGamepadTarget(fallback: gordian));
        }

        [Fact]
        public void Changed_IsRaisedOnFocusAndCharacterChanges()
        {
            var tracker = new InputFocusTracker();
            int changes = 0;
            tracker.Changed += (_, _) => changes++;
            var gordian = NewSession("Gordian", 3);
            object main = new();

            tracker.SetWindowSession(main, gordian);
            tracker.SetWindowSession(main, gordian); // unchanged: no event
            tracker.Activated(main);
            tracker.Activated(main); // already focused: no event
            tracker.Deactivated(main);
            tracker.Deactivated(main); // already unfocused: no event

            Assert.Equal(3, changes);
        }
    }
}
