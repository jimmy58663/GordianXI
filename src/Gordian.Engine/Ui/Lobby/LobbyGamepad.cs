// src/Gordian.Engine/Ui/Lobby/LobbyGamepad.cs
using System;
using System.Collections.Generic;
using Gordian.Engine.Input;

namespace Gordian.Engine.Ui.Lobby
{
    /// <summary>
    /// The gamepad in the character lobby (#553): the buttons the user's input profile assigns to the menu functions
    /// (<see cref="GamepadAssignments"/>: Confirm, Cancel and the four "Menu ..." directions, the d-pad by default) drive
    /// every lobby screen as the keyboard's Enter, Escape and arrows do (<see cref="LobbyController.HandleInput"/>). The pad is
    /// read in the menu context, so a button the profile gives a menu function and a game function (the d-pad's
    /// "Menu Up / Target Party Up") acts as the menu one. A held direction repeats at the stock menus' rate
    /// (<see cref="StockUiMenuController.RepeatDelay"/>, <see cref="StockUiMenuController.RepeatInterval"/>). Typing a name
    /// still needs the keyboard (no on-screen keyboard, maintainer 2026-11-13); the pad moves the caret, confirms and cancels
    /// there.
    /// </summary>
    public sealed class LobbyGamepad
    {
        private static readonly InputAction[] Directions = { InputAction.MenuUp, InputAction.MenuDown, InputAction.MenuLeft, InputAction.MenuRight };
        private static readonly LobbyInput[] DirectionInputs = { LobbyInput.Up, LobbyInput.Down, LobbyInput.Left, LobbyInput.Right };

        private readonly InputState _input = new() { MenuContext = true };
        private readonly TimeSpan?[] _repeat = new TimeSpan?[4];
        private readonly List<LobbyInput> _inputs = new();
        private LobbyController? _lobby;

        /// <summary>
        /// One input tick: reads <paramref name="pad"/> against <paramref name="profile"/> and hands what it pressed to
        /// <paramref name="lobby"/> (null: no lobby takes the pad). Buttons already held when a lobby takes the pad (the A that
        /// chose Log Out) stay quiet until let go. Returns the inputs handed over, in order.
        /// </summary>
        public IReadOnlyList<LobbyInput> Update(LobbyController? lobby, GamepadState pad, InputProfile profile, TimeSpan elapsed)
        {
            ArgumentNullException.ThrowIfNull(profile);
            _inputs.Clear();
            if (!ReferenceEquals(lobby, _lobby))
            {
                _lobby = lobby;
                Array.Clear(_repeat);
                _input.IgnoreHeldGamepadButtons();
            }
            _input.SetGamepadState(lobby != null ? pad : GamepadState.Disconnected);
            _input.Update(profile, elapsed);
            if (lobby == null) return _inputs;

            if (_input.WasActionTriggered(InputAction.Cancel)) _inputs.Add(LobbyInput.Cancel);
            else if (_input.WasActionTriggered(InputAction.Confirm)) _inputs.Add(LobbyInput.Confirm);
            for (int i = 0; i < Directions.Length; i++)
            {
                var action = Directions[i];
                if (!_input.IsActionHeld(action))
                {
                    _repeat[i] = null;
                    continue;
                }
                if (_input.WasActionTriggered(action) || _repeat[i] == null)
                {
                    _inputs.Add(DirectionInputs[i]);
                    _repeat[i] = StockUiMenuController.RepeatDelay;
                    continue;
                }
                var remaining = _repeat[i]!.Value - elapsed;
                if (remaining <= TimeSpan.Zero)
                {
                    _inputs.Add(DirectionInputs[i]);
                    remaining = StockUiMenuController.RepeatInterval;
                }
                _repeat[i] = remaining;
            }

            foreach (var input in _inputs) lobby.HandleInput(input);
            return _inputs;
        }
    }
}
