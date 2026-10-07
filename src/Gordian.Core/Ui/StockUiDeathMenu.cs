// src/Gordian.Core/Ui/StockUiDeathMenu.cs
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Gordian.Core.Actions;
using Gordian.Core.Diagnostics;
using Gordian.Core.Network.Packets;
using Gordian.Core.World;

namespace Gordian.Core.Ui
{
    /// <summary>
    /// The dead character's windows (#103). On death (S2C 0x037 status 3) retail shows the English menu DAT's "dead"
    /// window: top-left (16, 94), a 128 x 44 frame (its art 144 wide), titled "Time Left:" with one button, "Back to Home Point" (label sprite
    /// windowps #254, help text 241, title 57; read from ROM/119/51 with <c>GORDIAN_UI_DUMP_MENUS=dead</c>,
    /// 2026-10-05). It stays up while the character is dead: Cancel does not close it (the main menu can still open
    /// over it), and it closes when the character is raised, home-pointed or otherwise alive again.
    /// <para>
    /// Confirming "Back to Home Point" asks "Return to home point?" (yes/no, No under the cursor) and on Yes sends C2S
    /// 0x01A HomepointMenu with ActionBuf[0] = 0. A Raise or Tractor offer (S2C 0x0F9 type 1 or 2) opens a yes/no
    /// prompt over it ("Accept Raise?" / "Accept Tractor?", Yes under the cursor); Yes and No send C2S 0x01A RaiseMenu or
    /// TractorMenu with 0 (accept) or 1 (decline). Cancel closes the prompt without answering, so the offer stays
    /// open on the server; <c>/acceptraise</c> and <c>/accepttractor</c> answer it from the chat line. PROVISIONAL: the
    /// home point confirmation, both prompts' wording and windows (the stock "comyn" prompt), and what the time
    /// after "Time Left:" looks like are not checked against retail; XiPackets calls the Raise and Tractor windows
    /// sub-menus of the home point menu, which no DAT window found so far matches. The Monstrosity variant is
    /// "mp_dead" ("Time Limit:", Retry / Cancel), not driven.
    /// </para>
    /// Protocol referenced from XiPackets (https://github.com/atom0s/XiPackets/tree/main/world/server/0x00F9,
    /// world/client/0x001A) and LandSandBoat (https://github.com/LandSandBoat/server, c2s/0x01a_action.cpp).
    /// </summary>
    public sealed class StockUiDeathMenu
    {
        /// <summary>The DAT window shown while dead.</summary>
        public const string MenuName = "dead";

        /// <summary>Its only button, "Back to Home Point".</summary>
        public const int HomePointButton = 1;

        /// <summary>Where the time left starts: just after the "Time Left:" title glyphs (they end at x 66).</summary>
        public const float TimeLeftX = 70;

        public const string HomePointQuestion = "Return to home point?";
        public const string RaiseQuestion = "Accept Raise?";
        public const string TractorQuestion = "Accept Tractor?";

        private readonly object _sync = new();
        private readonly StockUiMenuController _menus;
        private readonly LocalPlayerState _player;
        private readonly Func<Task<PlayerActionResult>> _homePoint;
        private readonly Func<DeathMenuType, bool, Task<PlayerActionResult>> _answerOffer;

        private StockUiOpenMenu? _window;
        private StockUiOpenMenu? _homePointPrompt;
        private StockUiOpenMenu? _offerPrompt;
        private bool _offerDismissed;
        private uint _lastSeconds;
        private long _deadlineTimestamp;

        /// <param name="homePoint">Sends the home point answer (C2S 0x01A HomepointMenu 0).</param>
        /// <param name="answerOffer">Answers a Raise or Tractor offer: true accepts, false declines.</param>
        public StockUiDeathMenu(StockUiMenuController menus, LocalPlayerState player, Func<Task<PlayerActionResult>> homePoint,
            Func<DeathMenuType, bool, Task<PlayerActionResult>> answerOffer)
        {
            _menus = menus ?? throw new ArgumentNullException(nameof(menus));
            _player = player ?? throw new ArgumentNullException(nameof(player));
            _homePoint = homePoint ?? throw new ArgumentNullException(nameof(homePoint));
            _answerOffer = answerOffer ?? throw new ArgumentNullException(nameof(answerOffer));
            _menus.HomePointSelected = AskHomePoint;
            _player.ServerStatusChanged += OnServerStatusChanged;
            _player.DeathMenuChanged += OnDeathMenuChanged;
            _player.VitalsUpdated += Refresh;
        }

        /// <summary>Whether the dead window is open.</summary>
        public bool IsOpen
        {
            get
            {
                var window = _window;
                return window != null && IsStillOpen(window);
            }
        }

        /// <summary>The open Raise / Tractor prompt, if any.</summary>
        public StockUiOpenMenu? OfferPrompt => _offerPrompt;

        /// <summary>
        /// The text drawn after "Time Left:": minutes and seconds until the server returns the character to its home
        /// point, counted down from the last S2C 0x037 <c>dead_counter1</c>; null while unknown.
        /// </summary>
        public string? TimeLeftText()
        {
            long deadline = _deadlineTimestamp;
            if (deadline == 0) return null;
            double seconds = Math.Max(0, (deadline - Stopwatch.GetTimestamp()) / (double)Stopwatch.Frequency);
            return FormatTimeLeft((uint)Math.Ceiling(seconds));
        }

        /// <summary>"59:30" for 3570 s; minutes are not wrapped into hours (the server's limit is an hour).</summary>
        public static string FormatTimeLeft(uint seconds) => $"{seconds / 60}:{seconds % 60:00}";

        private void OnServerStatusChanged(byte previous, byte current)
        {
            if (current == LocalPlayerState.StatusDead) Refresh();
            else if (previous == LocalPlayerState.StatusDead) Close();
        }

        private void OnDeathMenuChanged(DeathMenuType type)
        {
            if (type == DeathMenuType.HomePoint)
            {
                StockUiOpenMenu? prompt;
                lock (_sync)
                {
                    prompt = _offerPrompt;
                    _offerPrompt = null;
                    _offerDismissed = false;
                }
                if (prompt != null) _menus.CloseMenu(prompt);
                return;
            }
            lock (_sync) _offerDismissed = false;
            OpenOfferPrompt(type);
        }

        /// <summary>
        /// Brings the windows in line with the state: opens the dead window while the character is dead (also after a
        /// zone change or once the UI has loaded), its pending offer prompt, and keeps the time left current.
        /// </summary>
        public void Refresh()
        {
            if (!_player.IsDead)
            {
                if (_window != null) Close();
                return;
            }
            uint seconds = _player.HomepointSecondsRemaining;
            if (seconds != _lastSeconds)
            {
                _lastSeconds = seconds;
                _deadlineTimestamp = seconds == 0 ? 0 : Stopwatch.GetTimestamp() + seconds * Stopwatch.Frequency;
            }
            if (IsOpen || _menus.Library == null) return;

            var window = _menus.OpenPinned(MenuName, TimeLeftText, TimeLeftX);
            if (window == null) return;
            bool reopenOffer;
            lock (_sync)
            {
                _window = window;
                _homePointPrompt = null;
                _offerPrompt = null;
                reopenOffer = !_offerDismissed;
            }
            GordianLog.Info("UI", "Dead: home point window opened.");
            if (reopenOffer && _player.DeathMenu != DeathMenuType.HomePoint) OpenOfferPrompt(_player.DeathMenu);
        }

        private void OpenOfferPrompt(DeathMenuType type)
        {
            if (!_player.IsDead) return;
            StockUiOpenMenu? previous;
            lock (_sync)
            {
                previous = _offerPrompt;
                _offerPrompt = null;
            }
            if (previous != null) _menus.CloseMenu(previous);
            if (_window == null || !IsStillOpen(_window)) return; // Refresh opens it with the window.

            string question = type == DeathMenuType.Tractor ? TractorQuestion : RaiseQuestion;
            StockUiOpenMenu? prompt = null;
            prompt = _menus.OpenYesNo(question, defaultYes: true, answer => OnOfferAnswered(type, prompt, answer));
            lock (_sync) _offerPrompt = prompt;
        }

        private void OnOfferAnswered(DeathMenuType type, StockUiOpenMenu? prompt, bool? answer)
        {
            lock (_sync)
            {
                // A prompt this class closed itself (replaced, withdrawn, the window closing) was let go first.
                if (prompt == null || !ReferenceEquals(_offerPrompt, prompt)) return;
                _offerPrompt = null;
                if (answer == null)
                {
                    // Closed without an answer (Cancel, or the window went away): the offer stays open on the server.
                    _offerDismissed = true;
                    return;
                }
            }
            if (!_player.IsDead || _player.DeathMenu != type) return;
            _ = SendAsync(() => _answerOffer(type, answer!.Value));
        }

        private void AskHomePoint()
        {
            lock (_sync)
            {
                if (_homePointPrompt != null && IsStillOpen(_homePointPrompt)) return;
            }
            StockUiOpenMenu? prompt = null;
            prompt = _menus.OpenYesNo(HomePointQuestion, defaultYes: false, answer =>
            {
                lock (_sync)
                {
                    if (ReferenceEquals(_homePointPrompt, prompt)) _homePointPrompt = null;
                }
                if (answer == true && _player.IsDead) _ = SendAsync(_homePoint);
            });
            lock (_sync) _homePointPrompt = prompt;
        }

        private async Task SendAsync(Func<Task<PlayerActionResult>> send)
        {
            try
            {
                var result = await send().ConfigureAwait(false);
                if (result.Kind is PlayerActionResultKind.Warning or PlayerActionResultKind.Error && !string.IsNullOrEmpty(result.Message))
                {
                    _menus.PostNotice(result.Message);
                }
            }
            catch (Exception ex)
            {
                GordianLog.Warning("UI", $"Death menu answer failed: {ex.Message}");
                _menus.PostNotice($"Death menu answer failed: {ex.Message}");
            }
        }

        /// <summary>Closes the dead window and its prompts (the character is alive again).</summary>
        public void Close()
        {
            StockUiOpenMenu? window;
            lock (_sync)
            {
                window = _window;
                _window = null;
                _homePointPrompt = null;
                _offerPrompt = null;
                _offerDismissed = false;
                _lastSeconds = 0;
                _deadlineTimestamp = 0;
            }
            if (window != null)
            {
                _menus.CloseMenu(window);
                GordianLog.Info("UI", "Alive again: home point window closed.");
            }
        }

        /// <summary>Stops following the player (the session ends).</summary>
        public void Detach()
        {
            _player.ServerStatusChanged -= OnServerStatusChanged;
            _player.DeathMenuChanged -= OnDeathMenuChanged;
            _player.VitalsUpdated -= Refresh;
            if (_menus.HomePointSelected == (Action)AskHomePoint) _menus.HomePointSelected = null;
            Close();
        }

        private bool IsStillOpen(StockUiOpenMenu menu)
        {
            foreach (var open in _menus.OpenMenus)
            {
                if (ReferenceEquals(open, menu)) return true;
            }
            return false;
        }
    }
}
