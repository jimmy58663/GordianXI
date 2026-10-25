// src/Gordian.Engine/Ui/StockUiMenuController.Status.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Gordian.Core.Resources.Ui;

namespace Gordian.Engine.Ui
{
    public sealed partial class StockUiOpenMenu
    {
        /// <summary>The Status page this window shows (<see cref="StockUiStatus"/>); None for other menus.</summary>
        public StockUiStatusPage StatusPage { get; init; }

        /// <summary>The Status window's list (<c>statcom2</c>).</summary>
        public bool IsStatusList => Name.Equals(StockUiStatus.CommandMenu, StringComparison.OrdinalIgnoreCase);

        /// <summary>The controller a Status page reads its numbers from.</summary>
        internal StockUiMenuController? StatusOwner { get; init; }

        /// <summary>What a Status page shows, read now; null outside the Status window or without a character.</summary>
        public StockUiStatusInfo? StatusInfoNow => StatusOwner?.StatusInfo?.Invoke();
    }

    public sealed partial class StockUiMenuController
    {
        /// <summary>What the Status window shows; null without a character. Set by the session.</summary>
        public Func<StockUiStatusInfo?>? StatusInfo { get; set; }

        /// <summary>A title's name by title id (ROM/180/78); set by the HUD.</summary>
        public Func<int, string?>? TitleName { get; set; }

        /// <summary>A zone's compact name by zone id ("SSandOria", ROM/165/85), as the Profile's Home Point shows it; set by the HUD.</summary>
        public Func<int, string?>? ZoneCompactName { get; set; }

        /// <summary>Whether the Status window is up (its list, with or without a page): the stats side and level box draw.</summary>
        public bool IsStatusOpen => _open.Any(m => m.IsStatusList);

        /// <summary>
        /// Opens a Status page (#23) over the list: its DAT window with no entries (Job Levels keeps its 22 rows for the
        /// cursor); Craft Skills is <c>mgcskill</c> with the "Craft Skills" frame image (<c>frames</c> #101, 152 tall) in place
        /// of "Magic Skills" (#100). Call under the lock.
        /// </summary>
        private StockUiOpenMenu? OpenStatusPage(StockUiStatusPage page, StockUiOpenMenu from)
        {
            var library = _library;
            string name = StockUiStatus.MenuOf(page);
            if (library == null || name.Length == 0 || !library.TryGetMenu(name, out var definition))
            {
                NoticePosted?.Invoke("Status: this page is not available in this session.");
                return null;
            }
            if (page == StockUiStatusPage.CraftSkills) definition = CraftSkillsDefinition(definition);
            var menu = new StockUiOpenMenu(definition, from, Array.Empty<string>(), null)
            {
                StatusPage = page,
                StatusOwner = this,
            };
            menu.SelectedButtonId = FirstSelectable(definition);
            PushOpen(menu);
            return menu;
        }

        private static UiMenuDefinition CraftSkillsDefinition(UiMenuDefinition magic)
        {
            var f = magic.Frame;
            var shapes = new List<UiShapeReference>(f.Shapes.Count);
            foreach (var shape in f.Shapes)
            {
                shapes.Add(shape.Kind == 0 && shape.GroupName == StockUiStatus.CraftFrameGroup ? shape with { ImageIndex = StockUiStatus.CraftFrameImage } : shape);
            }
            return new UiMenuDefinition
            {
                DatId = magic.DatId, Category = magic.Category, Name = magic.Name, MenuType = magic.MenuType, Buttons = magic.Buttons,
                Frame = new UiMenuFrame
                {
                    X = f.X, Y = f.Y, CursorOffsetX = f.CursorOffsetX, CursorOffsetY = f.CursorOffsetY,
                    Width = f.Width, Height = StockUiStatus.CraftFrameHeight, DrawOffsetX = f.DrawOffsetX, DrawOffsetY = f.DrawOffsetY,
                    Anchor = f.Anchor, Shapes = shapes, HelpTextId = f.HelpTextId, TitleTextId = f.TitleTextId,
                },
            };
        }
    }
}
