// src/Gordian.Core/Ui/StockUiPointer.cs
namespace Gordian.Core.Ui
{
    /// <summary>
    /// Where the mouse pointer is over the viewport (screen pixels), for the stock UI's own pointer: retail hides the
    /// system cursor over the game and draws the "yubi" hand sprite at the pointer. The viewport feeds it (UI thread)
    /// and the HUD reads it (render thread).
    /// </summary>
    public sealed class StockUiPointer
    {
        private readonly object _sync = new();
        private float _x, _y;
        private bool _inside;

        /// <summary>The pointer moved over the viewport.</summary>
        public void MoveTo(float x, float y)
        {
            lock (_sync)
            {
                _x = x;
                _y = y;
                _inside = true;
            }
        }

        /// <summary>The pointer left the viewport.</summary>
        public void Leave()
        {
            lock (_sync) _inside = false;
        }

        /// <summary>The pointer's position while it is over the viewport.</summary>
        public bool TryGetPosition(out float x, out float y)
        {
            lock (_sync)
            {
                x = _x;
                y = _y;
                return _inside;
            }
        }
    }
}
