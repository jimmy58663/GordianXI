// src/Gordian.Engine/Ui/StockUiMenuController.Status.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Gordian.Core.Resources.Ui;
using Gordian.Engine.Input;

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
        /// Opens a Status page (#23) over the list: its DAT window, with no entries and no cursor (retail's pages only show; the
        /// D-pad keeps moving the list, <see cref="TryMoveStatusPage"/>), as tall as its rows (<see cref="StockUiStatus.PageHeight"/>:
        /// the round 1 captures of Job Levels' 11 rows and a 2-row Magic Skills, 2026-10-25). Craft Skills is <c>mgcskill</c>
        /// with the "Craft Skills" frame image (<c>frames</c> #101) in place of "Magic Skills" (#100). Call under the lock.
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
            definition = PageDefinition(definition, page, StatusInfo?.Invoke());
            var menu = new StockUiOpenMenu(definition, from, Array.Empty<string>(), null)
            {
                StatusPage = page,
                StatusOwner = this,
            };
            PushOpen(menu);
            return menu;
        }

        /// <summary>
        /// The D-pad's Up / Down on a Status page moves the cursor on the list under it while the page stays shown; Confirm then
        /// shows the entry under the cursor (<see cref="StatusPageConfirm"/>; round 2 of the 2026-10-24 test: retail keeps the
        /// list live while a page shows, and the page changes only on Confirm). False when the top menu is not a Status page.
        /// Call under the lock.
        /// </summary>
        private bool TryMoveStatusPage(InputAction direction)
        {
            if (_open.Length < 2 || direction is not (InputAction.MenuUp or InputAction.MenuDown)) return false;
            var page = _open[^1];
            if (page.StatusPage == StockUiStatusPage.None || page.Parent is not { IsStatusList: true } list || !ReferenceEquals(_open[^2], list)) return false;
            if (list.SelectedButton is not { } button) return true;
            int link = direction == InputAction.MenuUp ? button.NavUp : button.NavDown;
            if (link < 0 || link == button.ButtonId || list.Menu.FindButton(link) == null) return true;
            list.SelectedButtonId = link;
            return true;
        }

        /// <summary>
        /// Confirm on a Status page: the page closes and the list's entry under the cursor runs as if confirmed on the list (its
        /// page shows). Returns the list, or null when the top menu is not a Status page. Call under the lock.
        /// </summary>
        private StockUiOpenMenu? StatusPageConfirm()
        {
            if (_open.Length < 2) return null;
            var page = _open[^1];
            if (page.StatusPage == StockUiStatusPage.None || page.Parent is not { IsStatusList: true } list || !ReferenceEquals(_open[^2], list)) return null;
            _open = _open[..^1];
            return list;
        }

        /// <summary>A page's DAT window without its buttons, as tall as its rows, its frame image stretched to fit (its authored height kept).</summary>
        private static UiMenuDefinition PageDefinition(UiMenuDefinition definition, StockUiStatusPage page, StockUiStatusInfo? info)
        {
            var f = definition.Frame;
            var shapes = new List<UiShapeReference>(f.Shapes.Count);
            foreach (var shape in f.Shapes)
            {
                bool craftFrame = page == StockUiStatusPage.CraftSkills && shape.Kind == 0 && shape.GroupName == StockUiStatus.CraftFrameGroup;
                shapes.Add(craftFrame ? shape with { ImageIndex = StockUiStatus.CraftFrameImage } : shape);
            }
            short imageHeight = page switch
            {
                StockUiStatusPage.CraftSkills => StockUiStatus.CraftFrameHeight,
                StockUiStatusPage.JobLevels => StockUiStatus.JobLevelImageHeight,
                _ => f.Height,
            };
            short height = StockUiStatus.PageHeight(page, info) ?? imageHeight;
            return new UiMenuDefinition
            {
                DatId = definition.DatId, Category = definition.Category, Name = definition.Name, MenuType = definition.MenuType,
                Frame = new UiMenuFrame
                {
                    X = f.X, Y = f.Y, CursorOffsetX = f.CursorOffsetX, CursorOffsetY = f.CursorOffsetY,
                    Width = f.Width, Height = height, AuthoredHeight = height != imageHeight ? imageHeight : (short)0,
                    DrawOffsetX = f.DrawOffsetX, DrawOffsetY = f.DrawOffsetY,
                    Anchor = f.Anchor, Shapes = shapes, HelpTextId = f.HelpTextId, TitleTextId = f.TitleTextId,
                },
            };
        }
    }
}
