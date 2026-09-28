// src/Gordian.Core/Ui/StockUiCommandMenu.cs
using System;
using System.Collections.Generic;
using Gordian.Core.Resources.Ui;

namespace Gordian.Core.Ui
{
    /// <summary>What the command menu was opened on; decides which entries it shows.</summary>
    public enum StockUiTargetKind
    {
        None = 0,
        Self,
        Player,
        Monster,
        Pet,
        Trust,
    }

    /// <summary>
    /// The target the command menu is composed for: its kind and id, whether you are engaged (with it), and whether
    /// an Invite would be allowed (you lead your party, or have none).
    /// </summary>
    public readonly record struct StockUiTargetContext(StockUiTargetKind Kind, uint TargetServerId, string TargetName,
        bool Engaged = false, bool EngagedWithTarget = false, bool CanInvite = true);

    /// <summary>A label sprite of the command menus: the DAT menu and button that carry it.</summary>
    public readonly record struct StockUiCommandLabel(string Text, string Menu, int Button);

    /// <summary>One row of a composed command menu: its label sprite, what it does, and whether it is greyed.</summary>
    public readonly record struct StockUiCommandRow(StockUiCommandLabel Label, StockUiMenuEntry Entry, bool Greyed = false);

    /// <summary>
    /// The target command menu retail opens at the bottom left when Confirm is pressed on a target (yourself, another
    /// player, a monster, a pet or trust). The English menu DAT (ROM/119/51) authors several fixed lists for it:
    /// "playermo" (Chat, Abilities, Magic, Items, Trade, Invite, Treasure, Check, Trust, Request), "normalmo",
    /// "battlemo" (Attack...), "attackmo" (Switch Target... Disengage), "mp_pmode", "chocopc", "myroom"; retail does not
    /// draw those as-is but composes a subset in its own order from their label sprites (the maintainer's retail
    /// check, 2026-09-27: yourself shows Chat, Magic, Abilities, Trust, Items, Trade, Check). The lists here do the
    /// same: each row takes its sprite from the DAT button that carries that label, the rows are laid out at the
    /// DAT's 16 px pitch from (21, 5), and the frame is rebuilt from "playermo"'s (a "newtex" background plus one
    /// "buttonto" capsule pair per row, every variant's frame image has the same make-up) at the height the rows
    /// need, its bottom at layout y 296 where every variant's frame ends (2 px above the eight-line log window).
    /// Only the self list is from a retail capture; the others are the DAT's authored lists with Magic before
    /// Abilities as the self list has, and want retail captures ([#50](https://github.com/jimmy58663/GordianXI/issues/50)).
    /// </summary>
    public static class StockUiCommandMenu
    {
        /// <summary>The composed menu's name (not a DAT menu); the controller remembers its cursor under it.</summary>
        public const string Name = "cmdmenu";

        /// <summary>The DAT menu whose frame art and label sprites the composition is built from.</summary>
        public const string Template = "playermo";

        public const string ChatMenu = StockUiMenuEntries.ChatModeMenu;

        // Row geometry shared by every variant: rows of 88 x 16 at x 21 from y 5, 8 px of frame padding.
        public const int FrameX = 16, FrameWidth = 112, FrameBottom = 296, FramePadding = 8;
        public const int RowX = 21, FirstRowY = 5, RowPitch = 16, RowWidth = 88, RowHeight = 16;

        // Labels and the DAT buttons that carry their sprites (read from the composited menus, 2026-09-28).
        public static readonly StockUiCommandLabel Chat = new("Chat", Template, 1);
        public static readonly StockUiCommandLabel Abilities = new("Abilities", Template, 2);
        public static readonly StockUiCommandLabel Magic = new("Magic", Template, 3);
        public static readonly StockUiCommandLabel Items = new("Items", Template, 4);
        public static readonly StockUiCommandLabel Trade = new("Trade", Template, 5);
        public static readonly StockUiCommandLabel Invite = new("Invite", Template, 6); // kind-4 alternate: greyed
        public static readonly StockUiCommandLabel Treasure = new("Treasure", Template, 7);
        public static readonly StockUiCommandLabel Check = new("Check", Template, 8);
        public static readonly StockUiCommandLabel Trust = new("Trust", Template, 9);
        public static readonly StockUiCommandLabel Request = new("Request", Template, 10);
        public static readonly StockUiCommandLabel Attack = new("Attack", "battlemo", 1);
        public static readonly StockUiCommandLabel SwitchTarget = new("Switch Target", "attackmo", 1);
        public static readonly StockUiCommandLabel Disengage = new("Disengage", "mp_pmode", 6); // "attackmo" 5 is the arrowed variant

        private static readonly StockUiMenuEntry NotAvailable = new(string.Empty, Command: StockUiMenuCommand.NotAvailable);

        private static StockUiCommandRow Row(StockUiCommandLabel label, StockUiMenuCommand command, bool greyed = false) =>
            new(label, new StockUiMenuEntry(label.Text, Command: command), greyed);

        private static StockUiCommandRow Unavailable(StockUiCommandLabel label) => Row(label, StockUiMenuCommand.NotAvailable);

        /// <summary>The rows the menu shows for a target; empty when that kind of target has no command menu.</summary>
        public static IReadOnlyList<StockUiCommandRow> Compose(in StockUiTargetContext context)
        {
            var rows = new List<StockUiCommandRow>(8);
            switch (context.Kind)
            {
                case StockUiTargetKind.Self:
                    // Retail (maintainer's check): Chat, Magic, Abilities, Trust, Items, Trade, Check. Disengage's place
                    // while engaged is a guess.
                    rows.Add(new StockUiCommandRow(Chat, new StockUiMenuEntry(Chat.Text, Opens: ChatMenu)));
                    rows.Add(Unavailable(Magic));
                    rows.Add(Unavailable(Abilities));
                    if (context.Engaged) rows.Add(Row(Disengage, StockUiMenuCommand.Disengage));
                    rows.Add(Unavailable(Trust));
                    rows.Add(Unavailable(Items));
                    rows.Add(Unavailable(Trade));
                    rows.Add(Row(Check, StockUiMenuCommand.Check));
                    break;

                case StockUiTargetKind.Player:
                    rows.Add(Row(Check, StockUiMenuCommand.Check));
                    rows.Add(Row(Invite, StockUiMenuCommand.Invite, greyed: !context.CanInvite));
                    rows.Add(Unavailable(Trade));
                    rows.Add(Unavailable(Magic));
                    rows.Add(Unavailable(Abilities));
                    rows.Add(Unavailable(Items));
                    break;

                case StockUiTargetKind.Monster:
                    if (context.EngagedWithTarget)
                    {
                        // "attackmo": Switch Target, Abilities, Magic, Items, Disengage, Treasure, Check.
                        rows.Add(Unavailable(SwitchTarget));
                        rows.Add(Unavailable(Magic));
                        rows.Add(Unavailable(Abilities));
                        rows.Add(Unavailable(Items));
                        rows.Add(Row(Disengage, StockUiMenuCommand.Disengage));
                        rows.Add(Row(Check, StockUiMenuCommand.Check));
                    }
                    else
                    {
                        // "battlemo": Attack, Abilities, Magic, Items, Treasure, Check.
                        rows.Add(Row(Attack, StockUiMenuCommand.Attack));
                        rows.Add(Unavailable(Magic));
                        rows.Add(Unavailable(Abilities));
                        rows.Add(Unavailable(Items));
                        rows.Add(Row(Check, StockUiMenuCommand.Check));
                    }
                    break;

                case StockUiTargetKind.Pet:
                case StockUiTargetKind.Trust:
                    rows.Add(Unavailable(Magic));
                    rows.Add(Unavailable(Abilities));
                    rows.Add(Unavailable(Items));
                    rows.Add(Row(Check, StockUiMenuCommand.Check));
                    break;
            }
            return rows;
        }

        /// <summary>
        /// Builds the composed menu: one 88 x 16 button per row, each carrying the label sprite of the DAT button it
        /// is taken from (its kind-0 image, and the kind-4 greyed alternate where the DAT has one), linked up/down in
        /// a ring and left/right to itself as the DAT lists are; the frame keeps the template's anchor, cursor group
        /// and cursor offsets, sized to the rows. <paramref name="frameImage"/> is the template frame image rebuilt
        /// for that height (null when the template's image is missing, in which case the window has no background).
        /// Returns null when the template menu is not in the library.
        /// </summary>
        public static UiMenuDefinition? Build(UiResourceLibrary library, IReadOnlyList<StockUiCommandRow> rows, out UiImage? frameImage)
        {
            frameImage = null;
            if (rows.Count == 0 || !library.TryGetMenu(Template, out var template)) return null;

            var buttons = new List<UiMenuButton>(rows.Count);
            for (int i = 0; i < rows.Count; i++)
            {
                var label = rows[i].Label;
                UiMenuButton? source = library.TryGetMenu(label.Menu, out var menu) ? menu.FindButton(label.Button) : null;
                buttons.Add(new UiMenuButton
                {
                    ButtonId = (short)(i + 1),
                    X = RowX,
                    Y = (short)(FirstRowY + i * RowPitch),
                    Width = RowWidth,
                    Height = RowHeight,
                    CursorOffsetX = source?.CursorOffsetX ?? 0,
                    CursorOffsetY = source?.CursorOffsetY ?? 0,
                    NavUp = (sbyte)(i == 0 ? rows.Count : i),
                    NavDown = (sbyte)(i == rows.Count - 1 ? 1 : i + 2),
                    NavLeft = (sbyte)(i + 1),
                    NavRight = (sbyte)(i + 1),
                    Shapes = source?.Shapes ?? Array.Empty<UiShapeReference>(),
                    HelpTextId = source?.HelpTextId ?? -1,
                });
            }

            short height = (short)(FramePadding + rows.Count * RowPitch);
            var shapes = new List<UiShapeReference>();
            foreach (var shape in template.Frame.Shapes)
            {
                if (shape.Kind == 0)
                {
                    if (frameImage == null && library.TryGetImage(shape, out var image)) frameImage = ResizeFrame(image, template.Frame.Height, height, rows.Count);
                }
                else
                {
                    shapes.Add(shape);
                }
            }
            var frame = new UiMenuFrame
            {
                X = FrameX,
                Y = (short)(FrameBottom - height),
                Width = FrameWidth,
                Height = height,
                Anchor = template.Frame.Anchor,
                CursorOffsetX = template.Frame.CursorOffsetX,
                CursorOffsetY = template.Frame.CursorOffsetY,
                DrawOffsetX = template.Frame.DrawOffsetX,
                DrawOffsetY = template.Frame.DrawOffsetY,
                Shapes = shapes,
                HelpTextId = template.Frame.HelpTextId,
                TitleTextId = template.Frame.TitleTextId,
            };
            return new UiMenuDefinition
            {
                DatId = template.DatId,
                Category = template.Category,
                Name = Name,
                MenuType = template.MenuType,
                Frame = frame,
                Buttons = buttons,
            };
        }

        /// <summary>
        /// Rebuilds a command-menu frame image for another row count: parts as tall as the frame (the "newtex"
        /// background) are resized to the new height, parts inside the first row (the capsule pair) are repeated per
        /// row, and any other part above the first row is kept.
        /// </summary>
        public static UiImage ResizeFrame(UiImage template, int templateHeight, int height, int rows)
        {
            var parts = new List<UiSpritePart>(1 + rows * 2);
            var rowParts = new List<UiSpritePart>(2);
            foreach (var part in template.Parts)
            {
                int top = part.TopLeft.Y, bottom = part.BottomLeft.Y;
                if (top <= 0 && bottom >= templateHeight)
                {
                    parts.Add(Clone(part, 0, 0, bottom: height, sourceHeight: (ushort)Math.Max(1, part.SourceHeight * height / Math.Max(1, bottom - top))));
                }
                else if (top >= FirstRowY && top < FirstRowY + RowPitch)
                {
                    rowParts.Add(part);
                }
                else if (top < FirstRowY)
                {
                    parts.Add(part);
                }
            }
            for (int i = 0; i < rows; i++)
            {
                foreach (var part in rowParts) parts.Add(Clone(part, 0, i * RowPitch));
            }
            return new UiImage { Parts = parts };
        }

        private static UiSpritePart Clone(UiSpritePart part, int dx, int dy, int? bottom = null, ushort? sourceHeight = null)
        {
            short by = (short)(bottom ?? part.BottomLeft.Y + dy);
            return new UiSpritePart
            {
                TopLeft = new UiPoint((short)(part.TopLeft.X + dx), (short)(part.TopLeft.Y + dy)),
                TopRight = new UiPoint((short)(part.TopRight.X + dx), (short)(part.TopRight.Y + dy)),
                BottomLeft = new UiPoint((short)(part.BottomLeft.X + dx), by),
                BottomRight = new UiPoint((short)(part.BottomRight.X + dx), by),
                SourceX = part.SourceX,
                SourceY = part.SourceY,
                SourceWidth = part.SourceWidth,
                SourceHeight = sourceHeight ?? part.SourceHeight,
                Flags = part.Flags,
                ColorTopLeft = part.ColorTopLeft,
                ColorTopRight = part.ColorTopRight,
                ColorBottomLeft = part.ColorBottomLeft,
                ColorBottomRight = part.ColorBottomRight,
                TextureAttributes = part.TextureAttributes,
                TextureName = part.TextureName,
            };
        }
    }
}
