// src/Gordian.Engine/Ui/StockUiMenuController.ViewHouse.cs
using System;
using System.Collections.Generic;
using Gordian.Core.Network.Packets;

namespace Gordian.Engine.Ui
{
    /// <summary>
    /// The stock View House window (#23) from the English menu DAT (39542, ROM/119/51; read 2026-10-24): main menu page 2
    /// View House opens <c>mogcont</c> (112 x 250 at 384,48, top right) listing Mog Safe, Mog Safe 2, Storage, Mog Locker, Mog
    /// Satchel, Mog Sack, Mog Case and Mog Wardrobe 1-8 (buttons 1, 8, 2-7, 9-15; help ROM/165/75 640, 935, 641, 642, 680,
    /// 682, 714, 801, 990-992, 1058-1061), as the maintainer's retail capture (<c>Main_Menu_Page2.mp4</c>, 1:15-1:30) shows.
    /// A storage opens the item list with its items; Confirm moves the item under the cursor to the inventory ("Move an item
    /// to your inventory.", help 1069; the capture's Mog Wardrobe). The Mog Safes, Storage and Mog Locker can only be viewed
    /// outside the Mog House (help 640-642: "Items can only be removed or stored when inside your Mog House").
    /// </summary>
    public static class StockUiViewHouse
    {
        public const string MenuName = "mogcont";
        public const int HelpMoveToInventory = 1069;

        /// <summary><c>mogcont</c>'s buttons by storage.</summary>
        private static readonly (int Button, ContainerId Container)[] Buttons =
        {
            (1, ContainerId.MogSafe), (8, ContainerId.MogSafe2), (2, ContainerId.Storage), (3, ContainerId.MogLocker),
            (4, ContainerId.MogSatchel), (5, ContainerId.MogSack), (6, ContainerId.MogCase), (7, ContainerId.Wardrobe),
            (9, ContainerId.Wardrobe2), (10, ContainerId.Wardrobe3), (11, ContainerId.Wardrobe4), (12, ContainerId.Wardrobe5),
            (13, ContainerId.Wardrobe6), (14, ContainerId.Wardrobe7), (15, ContainerId.Wardrobe8),
        };

        public static IReadOnlyList<(int Button, ContainerId Container)> Storages => Buttons;

        public static bool TryGetStorage(int button, out ContainerId container)
        {
            foreach (var (b, c) in Buttons)
            {
                if (b != button) continue;
                container = c;
                return true;
            }
            container = default;
            return false;
        }

        /// <summary>Whether a storage's items can be moved out away from the Mog House: the Satchel, Sack, Case and Wardrobes (LandSandBoat's 0x029 handler).</summary>
        public static bool MovableAnywhere(ContainerId container) =>
            container is ContainerId.MogSatchel or ContainerId.MogSack or ContainerId.MogCase || container == ContainerId.Wardrobe
            || container is >= ContainerId.Wardrobe2 and <= ContainerId.Wardrobe8;

        /// <summary>
        /// The help bar's title over a storage's list. The capture shows "M.Ward." for the Mog Wardrobe (the title box leaves room
        /// before the "15/80" fraction); the other short names are GordianXI's (not in the capture).
        /// </summary>
        public static string Title(ContainerId container) => container switch
        {
            ContainerId.MogLocker => "Locker",
            ContainerId.MogSatchel => "Satchel",
            ContainerId.MogSack => "Sack",
            ContainerId.MogCase => "Case",
            ContainerId.Wardrobe => "M.Ward.",
            >= ContainerId.Wardrobe2 and <= ContainerId.Wardrobe8 => $"M.Ward.{(int)container - (int)ContainerId.Wardrobe2 + 2}",
            _ => StockUiItems.ContainerName(container),
        };
    }

    public sealed partial class StockUiMenuController
    {
        /// <summary>Whether the character is in its Mog House (S2C 0x00A); set by the session.</summary>
        public Func<bool>? InMogHouse { get; set; }

        /// <summary>Whether a storage's items can be moved to the inventory here.</summary>
        public bool CanMoveFromStorage(ContainerId container) => StockUiViewHouse.MovableAnywhere(container) || InMogHouse?.Invoke() == true;

        /// <summary>Opens the View House list over the main menu, the storages the server has not given greyed. Call under the lock.</summary>
        private StockUiOpenMenu? OpenViewHouse(StockUiOpenMenu from)
        {
            var menu = Push(StockUiViewHouse.MenuName, from, Array.Empty<string>(), null, null);
            if (menu == null) return null;
            var greyed = new HashSet<int>();
            foreach (var (button, container) in StockUiViewHouse.Storages)
            {
                if (!StockUiItems.IsAvailable(Inventory, container)) greyed.Add(button);
            }
            menu.SetGreyed(greyed.Count > 0 ? greyed : null);
            return menu;
        }

        /// <summary>Opens a storage's item list (View House) over the storage list.</summary>
        private StockUiOpenMenu? OpenStorage(StockUiOpenMenu from, ContainerId container)
        {
            var library = _library;
            if (library == null || !library.TryGetMenu(StockUiItems.ListMenu, out var definition)) return null;
            var menu = new StockUiOpenMenu(definition, from, Array.Empty<string>(), null, VisibleRowsOf(definition))
            {
                ItemListKind = StockUiItemListKind.Storage,
                ItemContainer = container,
                ItemLookup = ItemLookup,
                ItemOwner = this,
            };
            lock (_sync)
            {
                FillItemRows(menu);
                menu.SelectedButtonId = 1;
                PushOpen(menu);
            }
            Changed?.Invoke();
            return menu;
        }

        /// <summary>
        /// Confirm on a storage's item: C2S 0x029 moves the whole stack to the inventory's first free slot; a full inventory prints
        /// the error, and a storage that can only be viewed here says so.
        /// </summary>
        private void MoveFromStorage(StockUiOpenMenu list, StockUiItemRow row)
        {
            if (!CanMoveFromStorage(list.ItemContainer))
            {
                NoticePosted?.Invoke($"Items in your {StockUiItems.ContainerName(list.ItemContainer)} can only be removed or stored when inside your Mog House.");
                return;
            }
            if (StockUiItems.IsInventoryFull(Inventory))
            {
                ErrorPosted?.Invoke(StockUiItems.InventoryFullMessage);
                return;
            }
            var move = ItemMove;
            if (move == null)
            {
                NoticePosted?.Invoke("Items cannot be moved in this session.");
                return;
            }
            _ = SendItemAsync(() => move(row.Count, list.ItemContainer, row.Slot, ContainerId.Inventory, StockUiItems.AnyFreeSlot), "Move");
        }
    }
}
