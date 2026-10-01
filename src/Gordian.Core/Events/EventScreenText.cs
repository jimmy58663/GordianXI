// src/Gordian.Core/Events/EventScreenText.cs
using System.Collections.Generic;

namespace Gordian.Core.Events
{
    /// <summary>
    /// A line an event shows on the screen in its event message mode (opcode 0x67): the text lines and the mode's two
    /// work values, which place the text (see <c>docs/ui/stock-ui.md</c>, "Event message mode").
    /// </summary>
    public sealed record EventScreenText(IReadOnlyList<string> Lines, int X, int Y);
}
