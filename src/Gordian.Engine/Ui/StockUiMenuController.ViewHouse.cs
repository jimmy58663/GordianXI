// src/Gordian.Engine/Ui/StockUiMenuController.ViewHouse.cs
using System;
using System.Collections.Generic;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources.Models;
using Gordian.Engine.Input;

namespace Gordian.Engine.Ui
{
    /// <summary>
    /// The stock View House window (#23) from the English menu DAT (39542, ROM/119/51; read 2026-10-24): main menu page 2
    /// View House opens <c>mogcont</c> (112 x 250 at 384,48, top right) listing Mog Safe, Mog Safe 2, Storage, Mog Locker, Mog
    /// Satchel, Mog Sack, Mog Case and Mog Wardrobe 1-8 (buttons 1, 8, 2-7, 9-15; help ROM/165/75 640, 935, 641, 642, 680,
    /// 682, 714, 801, 990-992, 1058-1061), as the maintainer's retail capture (<c>Main_Menu_Page2.mp4</c>, 1:15-1:30) shows.
    /// A storage opens the item list with its items and the inventory beside it (round 1 capture image-7, 2026-10-25: "M.Ward.
    /// 15/80" over the wardrobe's list at the left, the inventory's list at the right, the item description under both). Left /
    /// Right moves the cursor between the two lists, and Confirm moves the item under it to the other one ("Move an item to
    /// your inventory.", help 1069; "Move an item to your Mog Wardrobe.", 1070-1084 by storage). While a storage is open the
    /// View House list gives way to <c>itmsortw</c> ("Options", "+ : Sort": Auto, Manual, Switch; image-8): + / Y puts the
    /// cursor on it, Switch (help 1067 "Display a different storage system.") returns to the View House list, as Cancel does.
    /// The Mog Safes, Storage and Mog Locker can only be viewed outside the Mog House (help 640-642: "Items can only be removed
    /// or stored when inside your Mog House"): there they show their own list alone, without the inventory (round 2). A stack
    /// asks how many to move first (the quantity prompt), and in a Mog Wardrobe the inventory's items that are not equipment
    /// are greyed (round 2). With the cursor on the inventory the help bar reads "Items" and the inventory's count / size
    /// (round 2, retail).
    /// </summary>
    public static class StockUiViewHouse
    {
        public const string MenuName = "mogcont";
        public const int HelpMoveToInventory = 1069;

        /// <summary>The storage's options (<c>itmsortw</c>): Auto (1), Manual (2), Switch (3).</summary>
        public const string OptionsMenu = "itmsortw";
        public const int SwitchButton = 3;

        /// <summary>The inventory list's place beside a storage's list (layout x 198 = the list's 16 + its 182 width; image-7).</summary>
        public const int InventoryX = StockUiItems.RecycleInventoryX;

        /// <summary>"Move an item to your Mog Safe" ... "Move an item to your Mog Wardrobe 8." (ROM/165/75 1070-1084), by storage.</summary>
        public static int HelpMoveTo(ContainerId container) => container switch
        {
            ContainerId.MogSafe => 1070,
            ContainerId.MogSafe2 => 1071,
            ContainerId.Storage => 1072,
            ContainerId.MogLocker => 1073,
            ContainerId.MogSatchel => 1074,
            ContainerId.MogSack => 1075,
            ContainerId.MogCase => 1076,
            ContainerId.Wardrobe => 1077,
            >= ContainerId.Wardrobe2 and <= ContainerId.Wardrobe8 => 1078 + (int)container - (int)ContainerId.Wardrobe2,
            _ => -1,
        };

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
            container is ContainerId.MogSatchel or ContainerId.MogSack or ContainerId.MogCase || IsWardrobe(container);

        /// <summary>Whether a storage is one of the Mog Wardrobes, which take equipment only.</summary>
        public static bool IsWardrobe(ContainerId container) =>
            container == ContainerId.Wardrobe || container is >= ContainerId.Wardrobe2 and <= ContainerId.Wardrobe8;

        /// <summary>
        /// The inventory's rows beside a Mog Wardrobe: what is not equipment greyed, saying why on Confirm (LandSandBoat's 0x029
        /// handler refuses it; the wording is GordianXI's).
        /// </summary>
        public static List<StockUiItemRow> GreyForWardrobe(List<StockUiItemRow> rows, ContainerId wardrobe, Func<ushort, ItemRecord?>? lookup)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                if (lookup?.Invoke(rows[i].ItemId) is { EquipSlotsMask: not 0 }) continue;
                rows[i] = rows[i] with { Greyed = true, GreyedReason = $"{rows[i].Name} cannot be stored in your {StockUiItems.ContainerName(wardrobe)}." };
            }
            return rows;
        }

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

    public sealed partial class StockUiOpenMenu
    {
        /// <summary>A View House list's other side: the inventory while the storage has the cursor, the storage while the inventory has it.</summary>
        public ContainerId? StoragePartner { get; internal set; }

        /// <summary>The other side's first row and cursor row, kept while this side has the cursor.</summary>
        internal (int FirstRow, int Selected) StoragePartnerPlace { get; set; } = (0, 1);

        /// <summary>A View House list with the cursor on the inventory: it draws at the right, the storage's list beside it at the left.</summary>
        public bool StorageInventorySide => ItemListKind == StockUiItemListKind.Storage && StoragePartner != null && ItemContainer == ContainerId.Inventory;

        /// <summary>The storage a View House list shows, whichever side has the cursor.</summary>
        public ContainerId StorageContainer => StorageInventorySide && StoragePartner is { } storage ? storage : ItemContainer;
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
                // A storage that can only be viewed here shows its own list alone (round 2).
                StoragePartner = CanMoveFromStorage(container) ? ContainerId.Inventory : null,
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
        /// Confirm on a View House item: C2S 0x029 moves it to the other list's first free slot (the storage's item to the
        /// inventory, the inventory's to the storage), a stack after the quantity prompt asks how many. A full destination
        /// prints the error, a storage that can only be viewed here says so, a greyed row says why, and a worn item stays
        /// (LandSandBoat refuses to move equipped items).
        /// </summary>
        private void MoveFromStorage(StockUiOpenMenu list, StockUiItemRow row)
        {
            if (row.Greyed)
            {
                NoticePosted?.Invoke(row.GreyedReason ?? $"{row.Name} cannot be moved.");
                return;
            }
            var storage = list.StorageContainer;
            if (!CanMoveFromStorage(storage))
            {
                NoticePosted?.Invoke($"Items in your {StockUiItems.ContainerName(storage)} can only be removed or stored when inside your Mog House.");
                return;
            }
            var to = list.StorageInventorySide ? storage : ContainerId.Inventory;
            if (to == ContainerId.Inventory && StockUiItems.IsInventoryFull(Inventory))
            {
                ErrorPosted?.Invoke(StockUiItems.InventoryFullMessage);
                return;
            }
            if (to != ContainerId.Inventory && StockUiItems.Fill(Inventory, to) is var (used, total) && used >= total)
            {
                ErrorPosted?.Invoke($"Your {StockUiItems.ContainerName(to)} is full.");
                return;
            }
            if (row.Equipped)
            {
                NoticePosted?.Invoke("You cannot move an item you are wearing.");
                return;
            }
            if (ItemMove == null)
            {
                NoticePosted?.Invoke("Items cannot be moved in this session.");
                return;
            }
            if (row.Count > 1) OpenItemQuantity(list, list, row, restore: false, storageMove: true);
            else SendStorageMove(list, row, 1);
        }

        /// <summary>Sends C2S 0x029 for <paramref name="count"/> of a View House row to the other list's first free slot.</summary>
        private void SendStorageMove(StockUiOpenMenu list, StockUiItemRow row, uint count)
        {
            var move = ItemMove;
            if (move == null) return;
            var to = list.StorageInventorySide ? list.StorageContainer : ContainerId.Inventory;
            _ = SendItemAsync(() => move(count, row.Container, row.Slot, to, StockUiItems.AnyFreeSlot), "Move");
        }

        /// <summary>
        /// Left / Right in a View House list moves the cursor to the other list (Right from the storage to the inventory, Left
        /// back), each keeping its own place (image-7). False when the top menu is not one. Call under the lock.
        /// </summary>
        private bool TryMoveStorageSide(InputAction direction)
        {
            if (_open.Length == 0 || direction is not (InputAction.MenuLeft or InputAction.MenuRight)) return false;
            var list = _open[^1];
            if (list.ItemListKind != StockUiItemListKind.Storage || list.StoragePartner is not { } partner || list.IsManualSort) return false;
            bool toInventory = direction == InputAction.MenuRight;
            if (toInventory == list.StorageInventorySide) return true;
            var here = (list.FirstRow, list.SelectedButtonId);
            var there = list.StoragePartnerPlace;
            list.StoragePartner = list.ItemContainer;
            list.ItemContainer = partner;
            list.StoragePartnerPlace = here;
            FillItemRows(list);
            list.FirstRow = Math.Clamp(there.FirstRow, 0, Math.Max(0, list.ItemRows.Count - list.VisibleRows));
            list.ScrollFrom = list.FirstRow;
            list.SelectedButtonId = Math.Clamp(there.Selected, 1, Math.Max(1, Math.Min(list.VisibleRows, list.ItemRows.Count)));
            return true;
        }

        /// <summary>Switch in a storage's options: back to the View House list (the storage's list and its options close).</summary>
        private void SwitchStorage(StockUiOpenMenu options)
        {
            lock (_sync)
            {
                int index = Array.IndexOf(_open, options.Parent);
                if (index > 0) _open = _open[..index];
            }
            Changed?.Invoke();
        }
    }
}
