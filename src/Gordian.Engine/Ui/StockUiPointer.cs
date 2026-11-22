// src/Gordian.Engine/Ui/StockUiPointer.cs
namespace Gordian.Engine.Ui
{
    /// <summary>
    /// Where the mouse pointer is over the viewport (screen pixels), for the stock UI's pointer: over a clickable menu
    /// entry retail hides the system arrow and draws its hover pointer itself. The viewport feeds it (UI thread)
    /// and the HUD reads it (render thread).
    /// </summary>
    public sealed class StockUiPointer
    {
        private readonly object _sync = new();
        private float _x, _y;
        private bool _inside, _hidden;

        /// <summary>
        /// Raised (on the input thread) when <see cref="HideUntilMoved"/> hides the pointer, so the viewport can take the
        /// system cursor away at once rather than at the next mouse message.
        /// </summary>
        public event System.Action? HideRequested;

        /// <summary>
        /// Whether the pointer is hidden until the mouse moves (<see cref="HideUntilMoved"/>): the viewport shows no system
        /// cursor and the HUD no hover pointer.
        /// </summary>
        public bool Hidden
        {
            get { lock (_sync) return _hidden; }
        }

        /// <summary>The pointer moved over the viewport. A real move (a new position) shows a hidden pointer again.</summary>
        public void MoveTo(float x, float y)
        {
            lock (_sync)
            {
                // Windows can repeat a WM_MOUSEMOVE at the same place (a cursor change, a window under the pointer
                // changing): only a move to a new place counts.
                if (_hidden && (!_inside || x != _x || y != _y)) _hidden = false;
                _x = x;
                _y = y;
                _inside = true;
            }
        }

        /// <summary>
        /// Hides the pointer until the mouse moves again: retail does this when Cancel (Esc or the gamepad's cancel
        /// button) is pressed with no menu open (#605).
        /// </summary>
        public void HideUntilMoved()
        {
            lock (_sync)
            {
                if (_hidden) return;
                _hidden = true;
            }
            HideRequested?.Invoke();
        }

        /// <summary>The pointer left the viewport.</summary>
        public void Leave()
        {
            lock (_sync) _inside = false;
        }

        /// <summary>The pointer's position while it is over the viewport and not hidden.</summary>
        public bool TryGetPosition(out float x, out float y)
        {
            lock (_sync)
            {
                x = _x;
                y = _y;
                return _inside && !_hidden;
            }
        }
    }
}
