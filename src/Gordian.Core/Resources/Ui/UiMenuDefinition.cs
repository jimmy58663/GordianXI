// src/Gordian.Core/Resources/Ui/UiMenuDefinition.cs
using System;
using System.Collections.Generic;

namespace Gordian.Core.Resources.Ui
{
    /// <summary>
    /// The screen corner a menu window is laid out from (FrameDefinitionHeader AnchorType): bit 0 = right, bit 1 = bottom.
    /// Positions are authored for the 512 x 448 layout; an anchored window keeps its distance to its corner.
    /// </summary>
    public enum UiAnchor : byte
    {
        TopLeft = 0,
        TopRight = 1,
        BottomLeft = 2,
        BottomRight = 3,
    }

    /// <summary>
    /// A menu's reference to one image of a UI element group (Section 0x31).
    /// </summary>
    /// <param name="Kind">
    /// Reference kind (u16): 0 on plain images; 4 on a button's second (alternate-state) image; 6 on a frame's
    /// animation reference. Only partially understood.
    /// </param>
    /// <param name="ImageIndex">Index into the group's images.</param>
    /// <param name="GroupId">The group's 16-character resource id (e.g. "menu    windowps").</param>
    public readonly record struct UiShapeReference(ushort Kind, ushort ImageIndex, string GroupId)
    {
        /// <summary>The group's trimmed name (e.g. "windowps").</summary>
        public string GroupName => GroupId.Length > 8 ? GroupId.Substring(8).Trim() : GroupId.Trim();
    }

    /// <summary>
    /// A menu window's frame (FrameDefinitionHeader). Coordinates are in 512 x 448 layout pixels.
    /// </summary>
    public sealed class UiMenuFrame
    {
        public short X { get; init; }
        public short Y { get; init; }
        public short CursorOffsetX { get; init; }
        public short CursorOffsetY { get; init; }
        public short Width { get; init; }
        public short Height { get; init; }
        public short DrawOffsetX { get; init; }
        public short DrawOffsetY { get; init; }
        public UiAnchor Anchor { get; init; }
        public IReadOnlyList<UiShapeReference> Shapes { get; init; } = Array.Empty<UiShapeReference>();

        /// <summary>Help text id, or -1 when none.</summary>
        public int HelpTextId { get; init; } = -1;

        /// <summary>Title text id, or -1 when none.</summary>
        public int TitleTextId { get; init; } = -1;
    }

    /// <summary>
    /// A selectable element of a menu (ButtonDefinitionHeader). Coordinates are relative to the frame's origin.
    /// Navigation links hold ButtonIds; -1 means no link.
    /// </summary>
    public sealed class UiMenuButton
    {
        public short X { get; init; }
        public short Y { get; init; }
        public short CursorOffsetX { get; init; }
        public short CursorOffsetY { get; init; }
        public short Width { get; init; }
        public short Height { get; init; }
        public short SelectRectOffsetX { get; init; }
        public short SelectRectOffsetY { get; init; }
        public short ButtonId { get; init; }
        public sbyte NavUp { get; init; }
        public sbyte NavDown { get; init; }
        public sbyte NavLeft { get; init; }
        public sbyte NavRight { get; init; }
        public IReadOnlyList<UiShapeReference> Shapes { get; init; } = Array.Empty<UiShapeReference>();
        public int HelpTextId { get; init; } = -1;
        public int TitleTextId { get; init; } = -1;
    }

    /// <summary>
    /// Decoded FFXI DAT Section 0x30 UiMenu: one menu window's frame and its buttons.
    /// </summary>
    public sealed class UiMenuDefinition
    {
        /// <summary>Section FourCC (e.g. "logw").</summary>
        public string DatId { get; init; } = string.Empty;

        /// <summary>Resource id category, trimmed (e.g. "menu").</summary>
        public string Category { get; init; } = string.Empty;

        /// <summary>Resource id name, trimmed (e.g. "logwindo", "partywin").</summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>Byte +0x10 of the definition (1 on every in-game menu); meaning not yet decoded.</summary>
        public byte MenuType { get; init; }

        public UiMenuFrame Frame { get; init; } = new();
        public IReadOnlyList<UiMenuButton> Buttons { get; init; } = Array.Empty<UiMenuButton>();

        /// <summary>
        /// Finds the button with the given ButtonId.
        /// </summary>
        public UiMenuButton? FindButton(int buttonId)
        {
            foreach (var button in Buttons)
            {
                if (button.ButtonId == buttonId) return button;
            }
            return null;
        }

        public override string ToString() => $"UiMenu [{Category}/{Name}] ({Buttons.Count} buttons)";
    }
}
