// src/Gordian.Engine/Ui/StockUiMenuController.Synthesis.cs
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Gordian.Core.Diagnostics;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources.Ui;
using Gordian.Core.World;

namespace Gordian.Engine.Ui
{
    public sealed partial class StockUiOpenMenu
    {
        /// <summary>The synthesis grid (<see cref="StockUiSynthesis.GridMenu"/>, #330).</summary>
        public bool IsSynthesisGrid { get; init; }

        /// <summary>The History list's options ("+ : Synthesis Options": Favorite / Unfavorite).</summary>
        public bool IsSynthesisOptions { get; init; }

        /// <summary>Whether the grid shows History recipes (its slots are the recipe's, not chosen one by one).</summary>
        public bool IsSynthesisHistory { get; init; }

        /// <summary>The grid's crystal.</summary>
        public StockUiSynthesisCrystal SynthesisCrystal { get; internal set; }

        /// <summary>The grid's eight slots (null = empty).</summary>
        public StockUiSynthesisSlot?[] SynthesisSlots { get; internal set; } = new StockUiSynthesisSlot?[StockUiSynthesis.SlotCount];

        /// <summary>The History recipe the grid shows (History only).</summary>
        public SynthesisHistoryEntry? SynthesisRecipe { get; internal set; }

        /// <summary>The grid slot (0-7) a material list or its count prompt fills.</summary>
        public int SynthesisTargetSlot { get; init; } = -1;

        /// <summary>The material row a synthesis count prompt is for; null for other prompts.</summary>
        public StockUiItemRow? SynthesisPickRow { get; init; }

        /// <summary>The grid's slot item under the cursor, or (History, on OK / Cancel) the recipe's result.</summary>
        public ushort SynthesisGridItem =>
            SelectedButtonId is >= 1 and <= StockUiSynthesis.SlotCount && SynthesisSlots[SelectedButtonId - 1] is { } s ? s.ItemId
            : IsSynthesisHistory && SynthesisRecipe is { } r ? r.ResultItemId : (ushort)0;
    }

    public sealed partial class StockUiMenuController
    {
        /// <summary>Sends C2S 0x096: the crystal's item id and inventory slot and the ingredient items (one entry per item); true when sent.</summary>
        public Func<ushort, byte, (ushort ItemId, byte Slot)[], Task<bool>>? SynthesisRequested { get; set; }

        /// <summary>Whether a synthesis is running (<see cref="CraftingState.IsSynthesizing"/>): new ones are refused.</summary>
        public Func<bool>? IsSynthesizing { get; set; }

        /// <summary>The character's synthesis history (the History window); null when the session has none.</summary>
        public Func<SynthesisHistory?>? SynthesisHistory { get; set; }

        private bool RefuseWhileSynthesizing()
        {
            if (IsSynthesizing?.Invoke() != true) return false;
            ErrorPosted?.Invoke(StockUiSynthesis.LockedMessage);
            return true;
        }

        /// <summary>Synthesis > Crystal Synthesis: the inventory's crystals in the item list ("Items 3/30", "Select an item.").</summary>
        public StockUiOpenMenu? OpenCrystalList(StockUiOpenMenu? parent)
        {
            var library = _library;
            if (library == null || !library.TryGetMenu(StockUiItems.ListMenu, out var definition)) return null;
            var list = new StockUiOpenMenu(definition, parent, Array.Empty<string>(), null, VisibleRowsOf(definition))
            {
                ItemListKind = StockUiItemListKind.Crystals,
                ItemContainer = ContainerId.Inventory,
                ItemLookup = ItemLookup,
                ItemOwner = this,
            };
            lock (_sync)
            {
                FillItemRows(list);
                list.SelectedButtonId = 1;
                PushOpen(list);
            }
            Changed?.Invoke();
            return list;
        }

        /// <summary>
        /// Opens the synthesis grid for a crystal (from Crystal Synthesis or the Items window's Use) over
        /// <paramref name="parent"/>, the cursor on the first slot and the inventory beside it. Refused while a synthesis runs
        /// or with a full inventory (retail's red line, ROM/165/70 #140). Returns the grid.
        /// </summary>
        public StockUiOpenMenu? OpenSynthesisGrid(StockUiOpenMenu? parent, StockUiItemRow crystal)
        {
            if (RefuseWhileSynthesizing()) return null;
            if (StockUiItems.IsInventoryFull(Inventory))
            {
                ErrorPosted?.Invoke(StockUiItems.InventoryFullMessage);
                return null;
            }
            var library = _library;
            if (library == null || !library.TryGetMenu(StockUiSynthesis.GridMenu, out var definition)) return null;
            var grid = new StockUiOpenMenu(definition, parent, Array.Empty<string>(), null)
            {
                IsSynthesisGrid = true,
                Inventory = Inventory,
                ItemLookup = ItemLookup,
                ItemOwner = this,
            };
            grid.SynthesisCrystal = new StockUiSynthesisCrystal(crystal.ItemId, crystal.Slot);
            grid.SelectedButtonId = 1;
            lock (_sync)
            {
                grid.CompanionRows = StockUiSynthesis.MaterialRows(OrderedRows(ContainerId.Inventory), grid.SynthesisCrystal, grid.SynthesisSlots);
                PushOpen(grid);
            }
            GordianLog.Debug("CRAFT", $"Synthesis grid opened with crystal {crystal.ItemId} (inventory slot {crystal.Slot}).");
            Changed?.Invoke();
            return grid;
        }

        /// <summary>
        /// Synthesis > History (capture <c>Synthesis_History.png</c>): the grid with the History list beside it taking the
        /// cursor; the grid shows the recipe under the cursor.
        /// </summary>
        public StockUiOpenMenu? OpenSynthesisHistory(StockUiOpenMenu? parent)
        {
            if (RefuseWhileSynthesizing()) return null;
            var library = _library;
            if (library == null || !library.TryGetMenu(StockUiSynthesis.GridMenu, out var gridDefinition)) return null;
            var grid = new StockUiOpenMenu(gridDefinition, parent, Array.Empty<string>(), null)
            {
                IsSynthesisGrid = true,
                IsSynthesisHistory = true,
                Inventory = Inventory,
                ItemLookup = ItemLookup,
                ItemOwner = this,
            };
            grid.SelectedButtonId = StockUiSynthesis.OkButton;
            lock (_sync) PushOpen(grid);
            OpenHistoryList(grid, 0);
            return grid;
        }

        /// <summary>The History list (<c>cmbhlst</c> at x 198) over the grid, the cursor on entry <paramref name="index"/>.</summary>
        private StockUiOpenMenu? OpenHistoryList(StockUiOpenMenu grid, int index)
        {
            var library = _library;
            if (library == null || !library.TryGetMenu(StockUiSynthesis.HistoryListMenu, out var template)) return null;
            var definition = Placed(template, StockUiSynthesis.ListX, template.Frame.Y);
            var list = new StockUiOpenMenu(definition, grid, Array.Empty<string>(), null, VisibleRowsOf(definition))
            {
                ItemListKind = StockUiItemListKind.SynthesisHistory,
                ItemContainer = ContainerId.Inventory,
                ItemLookup = ItemLookup,
                ItemOwner = this,
            };
            lock (_sync)
            {
                FillItemRows(list);
                SelectEntry(list, index);
                PushOpen(list);
                PreviewHistory(list);
            }
            Changed?.Invoke();
            return list;
        }

        private static void SelectEntry(StockUiOpenMenu list, int index)
        {
            int count = list.Rows.Count;
            index = Math.Clamp(index, 0, Math.Max(0, count - 1));
            list.FirstRow = Math.Clamp(index - list.VisibleRows + 1, 0, Math.Max(0, count - list.VisibleRows));
            list.ScrollFrom = list.FirstRow;
            list.SelectedButtonId = index - list.FirstRow + 1;
        }

        private IReadOnlyList<SynthesisHistoryEntry> HistoryEntries() => SynthesisHistory?.Invoke()?.Entries ?? Array.Empty<SynthesisHistoryEntry>();

        /// <summary>The grid under a History list shows the recipe under the list's cursor (under the lock).</summary>
        private void PreviewHistory(StockUiOpenMenu list)
        {
            if (list.ItemListKind != StockUiItemListKind.SynthesisHistory || list.Parent is not { IsSynthesisGrid: true } grid) return;
            var entries = HistoryEntries();
            int index = list.EntryIndex(list.SelectedButtonId);
            if (index < 0 || index >= entries.Count)
            {
                grid.SynthesisRecipe = null;
                grid.SynthesisCrystal = default;
                grid.SynthesisSlots = new StockUiSynthesisSlot?[StockUiSynthesis.SlotCount];
                return;
            }
            var recipe = entries[index];
            var (crystal, slots) = StockUiSynthesis.Resolve(Inventory, recipe);
            grid.SynthesisRecipe = recipe;
            grid.SynthesisCrystal = crystal;
            grid.SynthesisSlots = slots;
        }

        /// <summary>The cursor moved in the top window: a History list's grid follows it.</summary>
        private void OnSynthesisCursorMoved(StockUiOpenMenu top)
        {
            if (top.ItemListKind == StockUiItemListKind.SynthesisHistory) PreviewHistory(top);
        }

        /// <summary>Confirm in a synthesis window; false when the top menu is none of them.</summary>
        private bool TryActivateSynthesis(StockUiOpenMenu top, UiMenuButton button)
        {
            if (top.IsSynthesisGrid)
            {
                ActivateSynthesisGrid(top, button);
                return true;
            }
            if (top.IsSynthesisOptions)
            {
                ActivateSynthesisOptions(top, button);
                return true;
            }
            return false;
        }

        /// <summary>Confirm on the grid: a slot opens the material list (a filled one empties, PROVISIONAL), OK synthesizes, Cancel closes.</summary>
        private void ActivateSynthesisGrid(StockUiOpenMenu grid, UiMenuButton button)
        {
            int id = button.ButtonId;
            if (id == StockUiSynthesis.OkButton)
            {
                Synthesize(grid);
                return;
            }
            if (id == StockUiSynthesis.CancelButton)
            {
                CloseMenu(grid);
                return;
            }
            if (id < 1 || id > StockUiSynthesis.SlotCount || grid.IsSynthesisHistory) return;
            int index = id - 1;
            if (grid.SynthesisSlots[index] != null)
            {
                lock (_sync)
                {
                    grid.SynthesisSlots[index] = null;
                    RefreshSynthesisRows(grid);
                }
                Changed?.Invoke();
                return;
            }
            var library = _library;
            if (library == null || !library.TryGetMenu(StockUiItems.ListMenu, out var template)) return;
            var definition = Placed(template, StockUiSynthesis.ListX, template.Frame.Y);
            var list = new StockUiOpenMenu(definition, grid, Array.Empty<string>(), null, VisibleRowsOf(definition))
            {
                ItemListKind = StockUiItemListKind.SynthesisMaterials,
                ItemContainer = ContainerId.Inventory,
                ItemLookup = ItemLookup,
                ItemOwner = this,
                SynthesisTargetSlot = index,
            };
            lock (_sync)
            {
                FillItemRows(list);
                list.SelectedButtonId = 1;
                PushOpen(list);
            }
            Changed?.Invoke();
        }

        /// <summary>
        /// Confirm on a material: a greyed row says why; a stack of which more than one can go in asks the count ("No. of
        /// Items", from 1, at most what is held and not yet in the grid and what the eight-item limit leaves); otherwise one
        /// goes in at once.
        /// </summary>
        private void PickMaterial(StockUiOpenMenu list, StockUiItemRow row)
        {
            if (list.Parent is not { IsSynthesisGrid: true } grid || list.SynthesisTargetSlot < 0) return;
            if (row.Greyed)
            {
                NoticePosted?.Invoke(row.GreyedReason ?? $"{row.Name} cannot be used for synthesis.");
                return;
            }
            uint max = MaterialRoom(grid, list.SynthesisTargetSlot, row.ItemId);
            if (max == 0)
            {
                NoticePosted?.Invoke(StockUiSynthesis.TotalPlaced(grid.SynthesisSlots, list.SynthesisTargetSlot) >= StockUiSynthesis.MaxIngredients
                    ? "No more materials can be added."
                    : $"You have no more {row.Name} to add.");
                return;
            }
            if (max == 1)
            {
                PlaceMaterial(grid, list.SynthesisTargetSlot, row, 1);
                return;
            }
            var library = _library;
            if (library == null || !library.TryGetMenu(StockUiShop.QuantityMenu, out var definition)) return;
            var prompt = new StockUiOpenMenu(definition, list, Array.Empty<string>(), null)
            {
                IsQuantity = true,
                ItemDropRow = row,
                SynthesisPickRow = row,
                SynthesisTargetSlot = list.SynthesisTargetSlot,
                QuantityMax = max,
                QuantityTotal = Math.Max(1, row.Count),
                ItemLookup = ItemLookup,
            };
            prompt.Quantity = 1;
            prompt.SelectedButtonId = StockUiShop.QuantityField;
            lock (_sync)
            {
                list.ShowsQuantity = true;
                PushOpen(prompt);
            }
            Changed?.Invoke();
        }

        /// <summary>How many of an item can still go into a slot: held, less what the other slots (and the crystal) take, within the eight items.</summary>
        private uint MaterialRoom(StockUiOpenMenu grid, int slot, ushort itemId)
        {
            uint held = StockUiSynthesis.HeldCount(Inventory, itemId);
            if (itemId == grid.SynthesisCrystal.ItemId && held > 0) held--;
            uint placed = StockUiSynthesis.PlacedCount(grid.SynthesisSlots, itemId, slot);
            uint room = (uint)StockUiSynthesis.MaxIngredients - Math.Min((uint)StockUiSynthesis.MaxIngredients, StockUiSynthesis.TotalPlaced(grid.SynthesisSlots, slot));
            return Math.Min(held > placed ? held - placed : 0, room);
        }

        /// <summary>Confirm on the material count: the items go in.</summary>
        private void RunSynthesisQuantity(StockUiOpenMenu prompt, StockUiItemRow row)
        {
            if (prompt.Parent is not { } list || list.Parent is not { IsSynthesisGrid: true } grid) return;
            PlaceMaterial(grid, prompt.SynthesisTargetSlot, row, prompt.Quantity);
        }

        /// <summary>Puts <paramref name="count"/> of a material in a slot; the list (and count prompt) close and the cursor moves to the next slot.</summary>
        private void PlaceMaterial(StockUiOpenMenu grid, int slot, StockUiItemRow row, uint count)
        {
            if (slot < 0 || slot >= StockUiSynthesis.SlotCount || count == 0) return;
            foreach (var menu in _open)
            {
                if (ReferenceEquals(menu.Parent, grid))
                {
                    CloseMenu(menu);
                    break;
                }
            }
            lock (_sync)
            {
                grid.SynthesisSlots[slot] = new StockUiSynthesisSlot(row.ItemId, count, row.Slot);
                // The video (2026-10-16): after the seashells went into the first slot the cursor stood on the second.
                grid.SelectedButtonId = slot + 2 <= StockUiSynthesis.SlotCount ? slot + 2 : StockUiSynthesis.OkButton;
                RefreshSynthesisRows(grid);
            }
            GordianLog.Debug("CRAFT", $"Synthesis slot {slot + 1}: {count} x {row.Name} (item {row.ItemId}, inventory slot {row.Slot}).");
            Changed?.Invoke();
        }

        /// <summary>The grid's companion rows after a change (under the lock).</summary>
        private void RefreshSynthesisRows(StockUiOpenMenu grid)
        {
            if (grid.IsSynthesisHistory)
            {
                grid.CompanionRows = StockUiSynthesis.HistoryRows(HistoryEntries(), ItemLookup);
                return;
            }
            grid.CompanionRows = StockUiSynthesis.MaterialRows(OrderedRows(ContainerId.Inventory), grid.SynthesisCrystal, grid.SynthesisSlots);
        }

        /// <summary>
        /// OK: checks the grid against the inventory and sends C2S 0x096, then the synthesis windows close back to the menu
        /// they came from (the video, 2026-10-16: the main menu with Items under the cursor stayed).
        /// </summary>
        private void Synthesize(StockUiOpenMenu grid)
        {
            if (RefuseWhileSynthesizing()) return;
            var crystal = grid.SynthesisCrystal;
            var record = StockUiShop.Lookup(ItemLookup, crystal.ItemId);
            if (crystal.ItemId == 0 || crystal.InventorySlot == 0 || Inventory?.GetContainer(ContainerId.Inventory).TryGetItem(crystal.InventorySlot, out var held) != true || held.ItemId != crystal.ItemId)
            {
                NoticePosted?.Invoke(crystal.ItemId == 0 ? "Select a recipe." : $"You do not have {StockUiShop.DescribeCount(record, 1)}.");
                return;
            }
            var ingredients = StockUiSynthesis.Expand(Inventory, crystal, grid.SynthesisSlots, out ushort shortItem);
            if (ingredients == null)
            {
                NoticePosted?.Invoke(shortItem != 0
                    ? $"You do not have enough {StockUiShop.PluralName(StockUiShop.Lookup(ItemLookup, shortItem))}."
                    : MenuHelpText?.Invoke(StockUiSynthesis.HelpSelectMaterials) ?? "Select materials for synthesis.");
                return;
            }
            var send = SynthesisRequested;
            if (send == null)
            {
                NoticePosted?.Invoke("Synthesis is not available in this session.");
                return;
            }
            GordianLog.Info("CRAFT", $"Synthesis: {record.Name} (slot {crystal.InventorySlot}) with {ingredients.Length} items.");
            CloseSynthesisFlow(grid);
            _ = SendItemAsync(async () =>
            {
                if (!await send(crystal.ItemId, crystal.InventorySlot, ingredients).ConfigureAwait(false)) NoticePosted?.Invoke("The synthesis could not be sent.");
            }, "Synthesis");
        }

        /// <summary>Closes the synthesis windows and what led to them, down to the main menu or command menu they were opened from.</summary>
        private void CloseSynthesisFlow(StockUiOpenMenu grid)
        {
            var first = grid;
            while (first.Parent is { } parent && parent.PageRing.Count == 0 && !parent.IsCommandMenu && !parent.Pinned) first = parent;
            CloseMenu(first);
        }

        /// <summary>
        /// Cancel on the History grid (called under the lock): the History list comes back over it on the recipe it showed,
        /// rather than the grid closing. False when the top window is not the History grid.
        /// </summary>
        private bool CancelSynthesisHistoryGrid(StockUiOpenMenu top)
        {
            if (!top.IsSynthesisGrid || !top.IsSynthesisHistory) return false;
            var library = _library;
            if (library == null || !library.TryGetMenu(StockUiSynthesis.HistoryListMenu, out var template)) return false;
            int index = 0;
            if (top.SynthesisRecipe is { } recipe)
            {
                var entries = HistoryEntries();
                for (int i = 0; i < entries.Count; i++) if (entries[i].SameRecipe(recipe)) index = i;
            }
            var definition = Placed(template, StockUiSynthesis.ListX, template.Frame.Y);
            var list = new StockUiOpenMenu(definition, top, Array.Empty<string>(), null, VisibleRowsOf(definition))
            {
                ItemListKind = StockUiItemListKind.SynthesisHistory,
                ItemContainer = ContainerId.Inventory,
                ItemLookup = ItemLookup,
                ItemOwner = this,
            };
            FillItemRows(list);
            SelectEntry(list, index);
            PushOpen(list);
            PreviewHistory(list);
            return true;
        }

        /// <summary>Confirm on a History recipe: the cursor goes to the grid's OK (PROVISIONAL: retail's step not captured).</summary>
        private void ChooseHistoryRecipe(StockUiOpenMenu list)
        {
            if (list.Parent is not { IsSynthesisGrid: true } grid) return;
            if (grid.SynthesisRecipe == null)
            {
                NoticePosted?.Invoke("You have no synthesis history.");
                return;
            }
            CloseMenu(list);
            lock (_sync)
            {
                grid.SelectedButtonId = StockUiSynthesis.OkButton;
                RefreshSynthesisRows(grid);
            }
            Changed?.Invoke();
        }

        /// <summary>
        /// + on the keyboard or Y on the gamepad in the History list: the cursor goes to "Synthesis Options" (Favorite /
        /// Unfavorite) at the top right, and back. Returns whether it applied.
        /// </summary>
        public bool ToggleSynthesisOptions()
        {
            var top = Top;
            if (top is { IsSynthesisOptions: true })
            {
                CloseMenu(top);
                return true;
            }
            var library = _library;
            if (top is not { ItemListKind: StockUiItemListKind.SynthesisHistory } || library == null
                || !library.TryGetMenu(StockUiSynthesis.OptionsMenu, out var definition)) return false;
            var options = new StockUiOpenMenu(definition, top, Array.Empty<string>(), null) { IsSynthesisOptions = true };
            options.SelectedButtonId = StockUiSynthesis.FavoriteButton;
            lock (_sync) PushOpen(options);
            Changed?.Invoke();
            return true;
        }

        /// <summary>Favorite / Unfavorite: marks the recipe under the History list's cursor; the list re-sorts with the cursor on it.</summary>
        private void ActivateSynthesisOptions(StockUiOpenMenu options, UiMenuButton button)
        {
            var list = options.Parent;
            CloseMenu(options);
            if (list is not { ItemListKind: StockUiItemListKind.SynthesisHistory } || list.Parent is not { SynthesisRecipe: { } recipe }) return;
            var history = SynthesisHistory?.Invoke();
            if (history == null) return;
            bool favorite = button.ButtonId == StockUiSynthesis.FavoriteButton;
            if (button.ButtonId is not (StockUiSynthesis.FavoriteButton or StockUiSynthesis.UnfavoriteButton)) return;
            history.SetFavorite(recipe, favorite);
            lock (_sync)
            {
                FillItemRows(list);
                var entries = HistoryEntries();
                for (int i = 0; i < entries.Count; i++)
                {
                    if (entries[i].SameRecipe(recipe)) SelectEntry(list, i);
                }
                PreviewHistory(list);
            }
            Changed?.Invoke();
        }
    }
}
