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
        bool Engaged = false, bool EngagedWithTarget = false, bool CanInvite = true, bool HasTreasure = false);

    /// <summary>A label sprite of the command menus: the DAT menu and button that carry it.</summary>
    /// <param name="Sprite">A <c>windowps</c> image to draw instead of a DAT button's label, for labels no menu carries (Release, #668); -1 for none.</param>
    public readonly record struct StockUiCommandLabel(string Text, string Menu, int Button, int Sprite = -1);

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
        public static readonly StockUiCommandLabel Disengage = new("Disengage", "attackmo", 5); // retail draws the arrowed sprite; "mp_pmode" 6 is the plain one

        /// <summary>
        /// Release (a Trust's command menu, retail image-25, 2026-10-12): the <c>windowps</c> #668 label, which no DAT menu uses
        /// (found by reading every label sprite's <c>menu2fon</c> glyphs; help 719 "Release an alter ego under your service.").
        /// </summary>
        public static readonly StockUiCommandLabel Release = new("Release", Template, 0, Sprite: 668);

        private static readonly StockUiMenuEntry NotAvailable = new(string.Empty, Command: StockUiMenuCommand.NotAvailable);

        private static StockUiCommandRow Row(StockUiCommandLabel label, StockUiMenuCommand command, bool greyed = false) =>
            new(label, new StockUiMenuEntry(label.Text, Command: command), greyed);

        private static StockUiCommandRow Unavailable(StockUiCommandLabel label) => Row(label, StockUiMenuCommand.NotAvailable);

        /// <summary>
        /// Treasure (help 27 "Distribute currently pooled treasure among party members."), offered while the treasure pool
        /// holds an item and placed before Check as the DAT lists ("playermo": ... Invite, Treasure, Check; "attackmo":
        /// ... Disengage, Treasure, Check). PROVISIONAL: that retail shows it only with items in the pool (#143).
        /// </summary>
        private static readonly StockUiCommandRow TreasureRow = Row(Treasure, StockUiMenuCommand.TreasurePool);

        /// <summary>
        /// Magic ▶: Confirm lists all the magic you can cast, right opens the categories beside the menu (<c>magselec</c>; retail
        /// capture 2026-10-11, maintainer). Abilities opens <c>abiselec</c> (#240, #353).
        /// </summary>
        private static readonly StockUiCommandRow MagicRow = new(Magic, new StockUiMenuEntry(Magic.Text, Opens: StockUiActionMenu.CommandMagicCategoryMenu,
            Command: StockUiMenuCommand.SpellList, Argument: 0));
        private static readonly StockUiCommandRow AbilitiesRow = new(Abilities, new StockUiMenuEntry(Abilities.Text, Opens: StockUiActionMenu.CommandAbilityMenu));

        /// <summary>Trust opens the Trust spell list at once (PROVISIONAL: retail's window for it is not captured).</summary>
        private static readonly StockUiCommandRow TrustRow = new(Trust,
            new StockUiMenuEntry(Trust.Text, Command: StockUiMenuCommand.SpellList, Argument: StockUiActionMenu.TrustKind));

        /// <summary>Items: the Items window (#351), its items used on the menu's target.</summary>
        private static readonly StockUiCommandRow ItemsRow = Row(Items, StockUiMenuCommand.Items);

        /// <summary>The rows the menu shows for a target; empty when that kind of target has no command menu.</summary>
        public static IReadOnlyList<StockUiCommandRow> Compose(in StockUiTargetContext context)
        {
            var rows = new List<StockUiCommandRow>(8);
            // Engaged = one menu (retail captures 2026-10-16/17, #63): whatever is targeted (yourself, a player, a
            // monster), the engaged list shows. Pets and trusts keep their own lists.
            if (context.Engaged && context.Kind is StockUiTargetKind.Self or StockUiTargetKind.Player or StockUiTargetKind.Monster)
            {
                rows.Add(Unavailable(SwitchTarget));
                rows.Add(MagicRow);
                rows.Add(AbilitiesRow);
                rows.Add(TrustRow);
                rows.Add(ItemsRow);
                rows.Add(Row(Disengage, StockUiMenuCommand.Disengage));
                if (context.HasTreasure) rows.Add(TreasureRow);
                rows.Add(Row(Check, StockUiMenuCommand.Check));
                return rows;
            }
            switch (context.Kind)
            {
                case StockUiTargetKind.Self:
                case StockUiTargetKind.Player:
                    // Retail (captures 2026-09-28, 2026-10-16): yourself and another player both show
                    // Chat, Magic, Abilities, Trust, Items, Trade, Check.
                    rows.Add(new StockUiCommandRow(Chat, new StockUiMenuEntry(Chat.Text, Opens: ChatMenu)));
                    rows.Add(MagicRow);
                    rows.Add(AbilitiesRow);
                    rows.Add(TrustRow);
                    rows.Add(ItemsRow);
                    rows.Add(Unavailable(Trade));
                    if (context.HasTreasure) rows.Add(TreasureRow);
                    rows.Add(Row(Check, StockUiMenuCommand.Check));
                    break;

                case StockUiTargetKind.Monster:
                    // Retail (in-game check 2026-09-28): Attack, Magic, Abilities, Trust, Items, Check.
                    rows.Add(Row(Attack, StockUiMenuCommand.Attack));
                    rows.Add(MagicRow);
                    rows.Add(AbilitiesRow);
                    rows.Add(TrustRow);
                    rows.Add(ItemsRow);
                    if (context.HasTreasure) rows.Add(TreasureRow);
                    rows.Add(Row(Check, StockUiMenuCommand.Check));
                    break;

                case StockUiTargetKind.Pet:
                    // Retail Command_Pet.png (2026-10-16): Magic, Abilities, Trust, Items; no Check.
                    rows.Add(MagicRow);
                    rows.Add(AbilitiesRow);
                    rows.Add(TrustRow);
                    rows.Add(ItemsRow);
                    if (context.HasTreasure) rows.Add(TreasureRow);
                    break;

                case StockUiTargetKind.Trust:
                    // Retail Command_Trust.png: Magic, Abilities, Trust, Items (white, usable), Release.
                    rows.Add(MagicRow);
                    rows.Add(AbilitiesRow);
                    rows.Add(TrustRow);
                    rows.Add(ItemsRow);
                    if (context.HasTreasure) rows.Add(TreasureRow);
                    rows.Add(Row(Release, StockUiMenuCommand.ReleaseTrust));
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
                    Shapes = label.Sprite >= 0 ? new[] { new UiShapeReference(0, (ushort)label.Sprite, "menu    windowps") }
                        : source?.Shapes ?? Array.Empty<UiShapeReference>(),
                    HelpTextId = label.Sprite >= 0 ? -1 : source?.HelpTextId ?? -1,
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
