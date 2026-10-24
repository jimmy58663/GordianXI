// tests/Gordian.Core.Tests/Ui/KeyItemsAndEquipmentSetsTests.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Gordian.Engine.Actions;
using Gordian.Core.Config;
using Gordian.Engine.Input;
using Gordian.Core.Network;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Models;
using Gordian.Core.Resources.Ui;
using Gordian.Engine.Ui;
using Gordian.Core.World;
using Xunit;

namespace Gordian.Core.Tests.Ui
{
    /// <summary>The Key Items window (#91) and the equipment sets with /equipset, /lockstyleset and the style lock (#92).</summary>
    public class KeyItemsAndEquipmentSetsTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";

        private const ushort BronzeCap = 12448, BronzeSword = 16535, HomeRing = 28540, Shield = 12289;

        private static readonly Dictionary<ushort, ItemRecord> Records = new()
        {
            [BronzeCap] = new ItemRecord { ItemId = BronzeCap, Name = "Bronze Cap", LogName = "bronze cap", Level = 7, JobsMask = 0x7FFFFE, EquipSlotsMask = 1 << (int)EquipSlotId.Head },
            [BronzeSword] = new ItemRecord { ItemId = BronzeSword, Name = "Bronze Sword", Level = 1, JobsMask = 0x7FFFFE, Skill = 3, EquipSlotsMask = 1 << (int)EquipSlotId.Main },
            [HomeRing] = new ItemRecord { ItemId = HomeRing, Name = "Home Ring", Level = 1, JobsMask = 0x7FFFFE, EquipSlotsMask = (1 << (int)EquipSlotId.Ring1) | (1 << (int)EquipSlotId.Ring2) },
            [Shield] = new ItemRecord { ItemId = Shield, Name = "Lauan Shield", Level = 1, JobsMask = 0x7FFFFE, EquipSlotsMask = 1 << (int)EquipSlotId.Sub },
        };

        #region Test windows

        private static UiMenuDefinition List(string name, short x, short y, UiAnchor anchor, int count, short width = 112)
        {
            var buttons = new List<UiMenuButton>();
            for (int i = 1; i <= count; i++)
            {
                buttons.Add(new UiMenuButton
                {
                    ButtonId = (short)i, X = 16, Y = (short)(6 + 16 * (i - 1)), Width = 88, Height = 16,
                    NavUp = (sbyte)(i == 1 ? count : i - 1), NavDown = (sbyte)(i == count ? 1 : i + 1), NavLeft = (sbyte)i, NavRight = (sbyte)i,
                });
            }
            return new UiMenuDefinition { Name = name, Frame = new UiMenuFrame { X = x, Y = y, Width = width, Height = (short)(10 + 16 * count), Anchor = anchor }, Buttons = buttons };
        }

        private static UiMenuDefinition ItemList()
        {
            var buttons = new List<UiMenuButton>();
            for (int i = 1; i <= StockUiItems.ListRows; i++)
            {
                buttons.Add(new UiMenuButton
                {
                    ButtonId = (short)i, X = 3, Y = (short)(6 + 18 * (i - 1)), Width = 132, Height = 16,
                    NavUp = (sbyte)(i == 1 ? 1 : i - 1), NavDown = (sbyte)(i == StockUiItems.ListRows ? i : i + 1), NavLeft = (sbyte)i, NavRight = (sbyte)i,
                });
            }
            return new UiMenuDefinition { Name = StockUiItems.ListMenu, Frame = new UiMenuFrame { X = 16, Y = 48, Width = 182, Height = 190, Anchor = UiAnchor.TopLeft }, Buttons = buttons };
        }

        private static UiMenuDefinition Grid(string name, short x)
        {
            var buttons = new List<UiMenuButton>();
            for (int c = 0; c < 4; c++)
            {
                for (int r = 0; r < 4; r++)
                {
                    int id = c * 4 + r + 1;
                    buttons.Add(new UiMenuButton
                    {
                        ButtonId = (short)id, X = (short)(24 + 34 * c), Y = (short)(11 + 34 * r), Width = 32, Height = 32,
                        NavUp = (sbyte)(r == 0 ? id : id - 1), NavDown = (sbyte)(r == 3 ? id : id + 1),
                        NavLeft = (sbyte)(c == 0 ? id : id - 4), NavRight = (sbyte)(c == 3 ? id : id + 4),
                    });
                }
            }
            return new UiMenuDefinition { Name = name, Frame = new UiMenuFrame { X = x, Y = 48, Width = 182, Height = 190, Anchor = UiAnchor.TopLeft }, Buttons = buttons };
        }

        private static UiResourceLibrary Library() => UiResourceLibrary.FromDefinitions(new[]
        {
            List(StockUiMenuEntries.MainMenuPage2, 384, 48, UiAnchor.TopRight, 12),
            List(StockUiKeyItems.WindowMenu, 16, 48, UiAnchor.TopLeft, 8, 366),
            List(StockUiKeyItems.PromptMenu, 16, 48, UiAnchor.TopLeft, 2, 366),
            List(StockUiEquipSets.StorageMenu, 384, 48, UiAnchor.TopRight, 16),
            List(StockUiEquipSets.MacroMenu, 384, 48, UiAnchor.TopRight, 2),
            List(StockUiEquipSets.ListMenu, 384, 48, UiAnchor.TopRight, 20),
            List(StockUiEquipSets.OptionsMenu, 0, 0, UiAnchor.TopLeft, 8),
            Grid(StockUiEquipSets.GridMenu, 16),
            ItemList(),
            List(StockUiItems.EquipStorageMenu, 384, 48, UiAnchor.TopRight, 9),
            List(StockUiMenuEntries.MessageYesNoMenu, 130, 208, UiAnchor.BottomLeft, 2),
        });

        #endregion

        #region Key Items window

        private static StockUiKeyItemCatalog Catalog() => StockUiKeyItemCatalog.FromEntries(new (string, IReadOnlyList<(ushort, string, string)>)[]
        {
            ("Temporary Key Items", new (ushort, string, string)[] { (1, "Zeruhn report", "A report on the Zeruhn Mines."), (5, "blue acidity tester", "Tests acidity.") }),
            ("Permanent Key Items", new (ushort, string, string)[] { (8, "airship pass", "Lets you ride the airships.") }),
            ("Mounts", new (ushort, string, string)[] { (3072, "Chocobo companion", "Summon a chocobo.") }),
        });

        private sealed class KeyItemHarness
        {
            public readonly StockUiMenuController Menus = new() { Library = Library() };
            public readonly HashSet<ushort> Held = new() { 1, 5, 3072 };
            public readonly HashSet<ushort> Seen = new() { 1, 3072 };
            public readonly List<ushort> Viewed = new();
            public readonly List<string> Notices = new();

            public KeyItemHarness()
            {
                Menus.KeyItemCatalog = Catalog;
                Menus.KeyItemHeld = Held.Contains;
                Menus.KeyItemSeen = Seen.Contains;
                Menus.KeyItemViewed = id =>
                {
                    Viewed.Add(id);
                    Seen.Add(id);
                    Menus.RefreshKeyItems();
                };
                Menus.NoticePosted += Notices.Add;
            }

            public StockUiOpenMenu Open()
            {
                Assert.True(Menus.Open(StockUiMenuEntries.MainMenuPage2));
                Menus.Top!.SelectedButtonId = 3; // Key Items
                Menus.Activate();
                return Menus.Top!;
            }
        }

        [Fact]
        public void KeyItems_MainMenuOpensTheCategories_EvenEmptyOnesOpen()
        {
            var h = new KeyItemHarness();
            var categories = h.Open();
            Assert.True(categories.IsKeyItemCategories);
            // image-6: the category names only, no counts, nothing greyed.
            Assert.Equal(new[] { "Temporary Key Items", "Permanent Key Items", "Mounts" }, categories.Rows.Select(r => r.Text));
            Assert.All(categories.Rows, r => Assert.Null(r.Value));
            // evitem's frame and its own eight rows at a 29 px pitch from (40,13).
            Assert.Equal((16, 48, 366), (categories.Menu.Frame.X, categories.Menu.Frame.Y, categories.Menu.Frame.Width));
            Assert.Equal(StockUiKeyItems.VisibleRows, categories.Menu.Buttons.Count);
            Assert.Equal((40, 13 + 29), (categories.Menu.FindButton(2)!.X, categories.Menu.FindButton(2)!.Y));

            // A category with none held opens with "You do not possess any key items." (ROM/165/72 #96).
            categories.SelectedButtonId = 2;
            h.Menus.Activate();
            var empty = h.Menus.Top!;
            Assert.True(empty.IsKeyItemList);
            Assert.Equal(StockUiKeyItems.NoKeyItemsFallback, empty.EmptyText);
        }

        [Fact]
        public void KeyItems_ANewOneAsksExamine_YesMarksItReadAndShowsTheDescription()
        {
            var h = new KeyItemHarness();
            var categories = h.Open();
            categories.SelectedButtonId = 1;
            h.Menus.Activate();
            var list = h.Menus.Top!;
            Assert.True(list.IsKeyItemList);
            Assert.Equal(new[] { "Zeruhn report", "Blue acidity tester" }, list.Rows.Select(r => r.Text));
            Assert.Equal(new[] { false, true }, list.Rows.Select(r => r.Marked)); // big bullet for the new one

            // Moving the cursor marks nothing (round 1: retail asks first).
            h.Menus.Move(InputAction.MenuDown);
            Assert.Empty(h.Viewed);

            // Confirm on the new one: "Examine blue acidity tester?" (the table's name), Yes under the cursor (image-9).
            h.Menus.Activate();
            var prompt = h.Menus.Top!;
            Assert.True(prompt.IsKeyItemExamine);
            Assert.Equal("Examine blue acidity tester?", prompt.KeyItemQuestion);
            Assert.Equal(StockUiKeyItems.YesButton, prompt.SelectedButtonId);

            // No returns to the list, still new.
            prompt.SelectedButtonId = StockUiKeyItems.NoButton;
            h.Menus.Activate();
            Assert.Same(list, h.Menus.Top);
            Assert.Empty(h.Viewed);

            // Yes: C2S 0x064 for it, then its description in the same window (image-10); Cancel returns to the list, now read.
            h.Menus.Activate();
            h.Menus.Activate();
            Assert.Equal(new ushort[] { 5 }, h.Viewed);
            var page = h.Menus.Top!;
            Assert.True(page.IsKeyItemDescription);
            Assert.Equal("Blue acidity tester", page.KeyItemShown!.Value.Name);
            Assert.Equal("Tests acidity.", page.KeyItemDescription);
            Assert.Equal(page.Menu.Frame.X, list.Menu.Frame.X);
            h.Menus.CloseTop();
            Assert.Same(list, h.Menus.Top);
            Assert.False(list.KeyItemRows[1].IsNew);

            // A read key item shows its description at once.
            list.SelectedButtonId = 1;
            h.Menus.Activate();
            Assert.True(h.Menus.Top!.IsKeyItemDescription);
            Assert.Equal(new ushort[] { 5 }, h.Viewed);
        }

        [Fact]
        public void KeyItems_Wrap_BreaksAtSpacesToTheWidth()
        {
            var lines = StockUiKeyItems.Wrap("Strike a pose befitting a mighty warrior.", t => t.Length * 9f, 300);
            Assert.Equal(new[] { "Strike a pose befitting a mighty", "warrior." }, lines);
            Assert.Equal(new[] { "One", "Two" }, StockUiKeyItems.Wrap("One\nTwo", t => t.Length, 300));
        }
        [Fact]
        public void KeyItems_WithoutTheTable_PostsANotice()
        {
            var h = new KeyItemHarness();
            h.Menus.KeyItemCatalog = () => null;
            var top = h.Open();
            Assert.False(top.IsKeyItemWindow);
            Assert.Contains(h.Notices, n => n.Contains("key item table", StringComparison.Ordinal));
        }

        [Fact]
        public void KeyItemCatalog_RetailTable_CategoriesAndIds()
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var catalog = StockUiKeyItemCatalog.FromTable(rm.GetDMsgTable(DMsgCategory.KeyItems));
            if (catalog == null) return;
            var names = catalog.Categories.Select(c => c.Name).ToList();
            Assert.Contains("Temporary Key Items", names);
            Assert.Contains("Mounts", names);
            Assert.Contains("Claim Slips", names);
            // #89's ids: the names by id, not by row.
            Assert.True(catalog.TryGetName(8, out var airship));
            Assert.Equal("airship pass", airship);
            Assert.True(catalog.TryGetName(3072, out var chocobo));
            Assert.EndsWith("Chocobo companion", chocobo, StringComparison.Ordinal); // the mounts start with a note glyph
            Assert.Contains((ushort)3072, catalog.Categories.Single(c => c.Name == "Mounts").Ids);
            Assert.False(string.IsNullOrEmpty(catalog.Description(8)));
        }

        #endregion

        #region Equipment set model and retail files

        [Fact]
        public void EquipmentSetBook_RoundTripsThroughJson_KeepingOnlyChangedSets()
        {
            var book = new EquipmentSetBook();
            Assert.Equal("Set004", book.Get(4).Name);
            Assert.True(book.Get(4).IsEmpty);
            book.Set(4, new EquipmentSet("BrdLock").WithPiece(EquipSlotId.Main, EquipmentSetPiece.Item(ContainerId.Wardrobe2, 27, 17652))
                .WithPiece(EquipSlotId.Sub, EquipmentSetPiece.Remove));
            book.Set(200, EquipmentSet.Blank(200));
            string json = book.ToJson();
            var back = EquipmentSetBook.FromJson(json);
            var set = back.Get(4);
            Assert.Equal("BrdLock", set.Name);
            Assert.Equal(EquipmentSetPiece.Item(ContainerId.Wardrobe2, 27, 17652), set[EquipSlotId.Main]);
            Assert.Equal(EquipmentSetPieceKind.Remove, set[EquipSlotId.Sub].Kind);
            Assert.True(set[EquipSlotId.Head].Kind == EquipmentSetPieceKind.Empty);
            Assert.DoesNotContain("\"Number\": 200", json);
            back.Clear(4);
            Assert.Equal("Set004", back.Get(4).Name);
            Assert.Throws<ArgumentOutOfRangeException>(() => back.Get(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => back.Get(201));
            Assert.Equal(EquipmentSet.MaxNameLength, new EquipmentSet("A name far longer than fifteen").Name.Length);
        }

        /// <summary>An es&lt;n&gt;.dat as the retail client writes one (the layout <see cref="RetailEquipSets"/> documents).</summary>
        private static byte[] RetailFile(int firstNumber, params (int Index, string Name, (EquipSlotId Slot, byte Flags, byte Index, ushort Item)[] Pieces)[] sets)
        {
            var data = new byte[RetailEquipSets.FileSize];
            data[0] = 1;
            for (int i = 0; i < RetailEquipSets.SetsPerFile; i++)
            {
                Encoding.ASCII.GetBytes(EquipmentSet.DefaultName(firstNumber + i)).CopyTo(data, RetailEquipSets.HeaderSize + i * RetailEquipSets.SetSize);
            }
            foreach (var (index, name, pieces) in sets)
            {
                int at = RetailEquipSets.HeaderSize + index * RetailEquipSets.SetSize;
                Array.Clear(data, at, RetailEquipSets.NameSize);
                Encoding.ASCII.GetBytes(name).CopyTo(data, at);
                foreach (var (slot, flags, slotIndex, item) in pieces)
                {
                    int p = at + RetailEquipSets.NameSize + (int)slot * 4;
                    data[p] = flags;
                    data[p + 1] = slotIndex;
                    BitConverter.GetBytes(item).CopyTo(data, p + 2);
                }
            }
            MD5.HashData(data.AsSpan(RetailEquipSets.HeaderSize)).CopyTo(data, 8);
            return data;
        }

        [Fact]
        public void RetailEquipSets_ParsesNamesSlotsAndFlags()
        {
            // The maintainer's BrdLock set: main Joyeuse from Wardrobe 2 (0x29 = container 10, has item), PLD's "02" remove marks.
            byte[] file = RetailFile(21, (0, "BrdLock", new[] { (EquipSlotId.Main, (byte)0x29, (byte)0x1b, (ushort)17652), (EquipSlotId.Head, (byte)0x21, (byte)0x1f, (ushort)13870) }),
                (1, "PLD", new[] { (EquipSlotId.Ranged, (byte)0x02, (byte)0, (ushort)0), (EquipSlotId.Main, (byte)0x01, (byte)0x05, (ushort)19066) }));
            Assert.True(RetailEquipSets.HasValidChecksum(file));
            var sets = RetailEquipSets.Parse(file, 21)!;
            Assert.Equal(20, sets.Count);
            Assert.Equal(21, sets[0].Number);
            Assert.Equal("BrdLock", sets[0].Set.Name);
            Assert.Equal(EquipmentSetPiece.Item(ContainerId.Wardrobe2, 0x1b, 17652), sets[0].Set[EquipSlotId.Main]);
            Assert.Equal(EquipmentSetPiece.Item(ContainerId.Wardrobe, 0x1f, 13870), sets[0].Set[EquipSlotId.Head]);
            Assert.Equal(EquipmentSetPieceKind.Remove, sets[1].Set[EquipSlotId.Ranged].Kind);
            Assert.Equal(EquipmentSetPiece.Item(ContainerId.Inventory, 5, 19066), sets[1].Set[EquipSlotId.Main]);
            Assert.True(sets[2].Set.IsEmpty);
            Assert.Equal("Set023", sets[2].Set.Name);
            Assert.Null(RetailEquipSets.Parse(file.AsSpan(0, 100), 1));
            file[100] ^= 0xFF;
            Assert.False(RetailEquipSets.HasValidChecksum(file));
        }

        [Fact]
        public void RetailEquipSets_ImportReplacesTheFilesSets()
        {
            string dir = Path.Combine(Path.GetTempPath(), "gordian_es_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllBytes(Path.Combine(dir, RetailEquipSets.FileName(1)),
                    RetailFile(21, (3, "Melee", new[] { (EquipSlotId.Main, (byte)0x01, (byte)4, BronzeSword) })));
                var book = new EquipmentSetBook();
                book.Set(1, new EquipmentSet("Kept"));
                var imported = RetailEquipSets.Import(dir, book);
                Assert.Equal(new[] { "1 equipment sets" }, imported);
                Assert.Equal("Melee", book.Get(24).Name); // es1.dat holds sets 21-40
                Assert.Equal("Kept", book.Get(1).Name);
                Assert.Empty(RetailEquipSets.Import(Path.Combine(dir, "none"), book));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void RetailEquipSets_MaintainersFiles_EveryItemFitsItsSlot()
        {
            string user = Path.Combine(GameDirectory, "USER");
            if (!Directory.Exists(user)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            int checkedItems = 0, files = 0;
            foreach (string folder in Directory.EnumerateDirectories(user))
            {
                string path = Path.Combine(folder, RetailEquipSets.FileName(0));
                if (!File.Exists(path)) continue;
                files++;
                byte[] data = File.ReadAllBytes(path);
                Assert.Equal(RetailEquipSets.FileSize, data.Length);
                Assert.True(RetailEquipSets.HasValidChecksum(data), path);
                foreach (var (_, set) in RetailEquipSets.Parse(data, 1)!)
                {
                    for (int slot = 0; slot < EquipmentSet.SlotCount; slot++)
                    {
                        var piece = set.Pieces[slot];
                        if (!piece.HasItem || !rm.TryGetItem(piece.ItemId, out var record) || record == null) continue;
                        Assert.True(record.CanEquipSlot(slot), $"{record.Name} ({piece.ItemId}) in slot {(EquipSlotId)slot} of {set.Name}");
                        checkedItems++;
                    }
                }
            }
            if (files == 0) return;
            Assert.True(checkedItems > 20, $"{checkedItems} items checked in {files} files");
        }

        #endregion

        #region Equipment Sets windows

        private sealed class SetHarness
        {
            public readonly InventoryState Inventory = new();
            public readonly EquipmentSetBook Book = new();
            public readonly StockUiMenuController Menus = new() { Library = Library() };
            public readonly List<string> Notices = new();
            public readonly List<(EquipSlotId Slot, EquipsetRequestItem Change, EquipsetRequestItem[] Others)> Checks = new();
            public readonly List<int> Equipped = new(), Locked = new();
            public EquipsetSlotEntry[] Validation = Array.Empty<EquipsetSlotEntry>();

            public SetHarness()
            {
                Menus.Inventory = Inventory;
                Menus.ItemLookup = id => Records.TryGetValue(id, out var r) ? r : null;
                Menus.EquipCharacter = () => new StockUiEquipCharacter(1, 30);
                Menus.NoticePosted += Notices.Add;
                Menus.EquipSets = () => Book;
                Menus.EquipSetEquip = n => { Equipped.Add(n); return Task.FromResult(PlayerActionResult.Ok(string.Empty)); };
                Menus.EquipSetStyleLock = n => { Locked.Add(n); return Task.FromResult(PlayerActionResult.Ok(string.Empty)); };
                Menus.EquipSetCheck = (slot, change, rest) => { Checks.Add((slot, change, rest)); return Task.CompletedTask; };
                Menus.EquipSetValidation = () => Validation;
                Inventory.SetContainerSizes(ContainerId.Inventory, 80, 80);
                Inventory.SetContainerSizes(ContainerId.Wardrobe, 80, 80);
                Inventory.SetItem(ContainerId.Inventory, 4, BronzeCap, 1, ItemLockFlag.Normal);
                Inventory.SetItem(ContainerId.Inventory, 5, Shield, 1, ItemLockFlag.Normal);
                Inventory.SetItem(ContainerId.Wardrobe, 1, BronzeSword, 1, ItemLockFlag.Normal);
                Inventory.SetItem(ContainerId.Wardrobe, 2, HomeRing, 1, ItemLockFlag.Normal);
            }

            public StockUiOpenMenu OpenList()
            {
                Assert.True(Menus.Open(StockUiMenuEntries.MainMenuPage2));
                Menus.Top!.SelectedButtonId = 6; // Macros
                Menus.Activate();
                Assert.Equal(StockUiEquipSets.MacroMenu, Menus.Top!.Name);
                Menus.Top!.SelectedButtonId = StockUiEquipSets.EditEquipSetButton;
                Menus.Activate();
                return Menus.Top!;
            }

            public StockUiOpenMenu OpenOptions(int row)
            {
                var list = OpenList();
                list.SelectedButtonId = row;
                Menus.Activate();
                return Menus.Top!;
            }

            public void Choose(int button)
            {
                Menus.Top!.SelectedButtonId = button;
                Menus.Activate();
            }
        }

        [Fact]
        public void EquipSets_MacrosOpensTheListOfTwentyAPage_AndThePagesWrap()
        {
            var h = new SetHarness();
            h.Book.Set(22, new EquipmentSet("Page2"));
            var list = h.OpenList();
            Assert.True(list.IsEquipSetList);
            Assert.Equal(20, list.VisibleRows);
            Assert.Equal(EquipmentSetBook.SetCount, list.Rows.Count);
            Assert.Equal(1, list.SelectedEquipSet);
            h.Menus.Move(InputAction.MenuRight);
            Assert.Equal(20, list.FirstRow);
            Assert.Equal(21, list.SelectedEquipSet);
            Assert.Equal("Page2", list.EquipSetBook!.Get(22).Name);
            // Up from the first row wraps within the page (the DAT links 1 to 20).
            h.Menus.Move(InputAction.MenuUp);
            Assert.Equal(40, list.SelectedEquipSet);
            // Left twice from page 2: page 1, then the last page (round 1: the pages wrap both ways).
            h.Menus.Move(InputAction.MenuLeft);
            h.Menus.Move(InputAction.MenuLeft);
            Assert.Equal(EquipmentSetBook.SetCount - StockUiEquipSets.VisibleRows, list.FirstRow);
            Assert.Equal(200, list.SelectedEquipSet);
            h.Menus.Move(InputAction.MenuRight);
            Assert.Equal(0, list.FirstRow);
            Assert.Equal(20, list.SelectedEquipSet);
        }

        /// <summary>Answers the yes / no prompt on top of the menus with Yes.</summary>
        private static async Task AnswerYesAsync(SetHarness h, Func<bool> done)
        {
            Assert.True(h.Menus.Top!.IsPrompt);
            h.Choose(1);
            for (int i = 0; i < 50 && !done(); i++) await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        [Fact]
        public async Task EquipSets_OptionsBesideTheList_UseCurrentEquipAndStyleLock()
        {
            var h = new SetHarness();
            h.Inventory.SetEquip(EquipSlotId.Main, ContainerId.Wardrobe, 1);
            h.Inventory.SetEquip(EquipSlotId.Head, ContainerId.Inventory, 4);
            var options = h.OpenOptions(3);
            Assert.True(options.IsEquipSetOptions);
            Assert.Equal(3, options.EquipSetNumber);
            Assert.Equal(StockUiEquipSets.OptionsX, options.Menu.Frame.X);
            // An empty set greys Equip and Style Lock; nothing is copied yet, so Paste is greyed.
            Assert.True(options.IsGreyed(StockUiEquipSets.EquipButton));
            Assert.True(options.IsGreyed(StockUiEquipSets.PasteButton));

            // Use Current asks first (ROM/165/72 #279).
            h.Choose(StockUiEquipSets.UseCurrentButton);
            Assert.Equal(StockUiEquipSets.FallbackText(StockUiEquipSets.UseCurrentPromptText), h.Menus.Top!.Message);
            await AnswerYesAsync(h, () => !h.Book.Get(3).IsEmpty);
            var set = h.Book.Get(3);
            Assert.Equal(EquipmentSetPiece.Item(ContainerId.Wardrobe, 1, BronzeSword), set[EquipSlotId.Main]);
            Assert.Equal(EquipmentSetPiece.Item(ContainerId.Inventory, 4, BronzeCap), set[EquipSlotId.Head]);
            Assert.False(options.IsGreyed(StockUiEquipSets.EquipButton));

            h.Choose(StockUiEquipSets.EquipButton);
            h.Choose(StockUiEquipSets.StyleLockButton);
            Assert.Equal(new[] { 3 }, h.Equipped);
            Assert.Equal(new[] { 3 }, h.Locked);

            // Copy, then Paste into another set (it asks, #277).
            h.Choose(StockUiEquipSets.CopyButton);
            Assert.False(options.IsGreyed(StockUiEquipSets.PasteButton));
            h.Menus.CloseTop();
            var list = h.Menus.Top!;
            list.SelectedButtonId = 5;
            h.Menus.Activate();
            h.Choose(StockUiEquipSets.PasteButton);
            Assert.Equal(StockUiEquipSets.FallbackText(StockUiEquipSets.PastePromptText), h.Menus.Top!.Message);
            await AnswerYesAsync(h, () => !h.Book.Get(5).IsEmpty);
            Assert.Equal(set[EquipSlotId.Main], h.Book.Get(5)[EquipSlotId.Main]);
        }

        [Fact]
        public async Task EquipSets_DeleteAsksFirst()
        {
            var h = new SetHarness();
            h.Book.Set(2, new EquipmentSet("Doomed").WithPiece(EquipSlotId.Head, EquipmentSetPiece.Item(ContainerId.Inventory, 4, BronzeCap)));
            h.OpenOptions(2);
            h.Choose(StockUiEquipSets.DeleteButton);
            var prompt = h.Menus.Top!;
            Assert.True(prompt.IsPrompt);
            Assert.Equal("Truly delete this set?", prompt.Message);
            Assert.Equal(2, prompt.SelectedButtonId); // No under the cursor
            await AnswerYesAsync(h, () => h.Book.Get(2).Name == "Set002");
            Assert.True(h.Book.Get(2).IsEmpty);
            Assert.Equal("Set002", h.Book.Get(2).Name);
        }

        [Fact]
        public void EquipSets_EditShowsTheStoragesEquipment_PutsAnItemInASlot_ChecksIt_AndTakesTheServersAnswer()
        {
            var h = new SetHarness();
            h.Book.Set(1, new EquipmentSet("Tank").WithPiece(EquipSlotId.Sub, EquipmentSetPiece.Item(ContainerId.Inventory, 5, Shield)));
            h.OpenOptions(1);
            h.Choose(StockUiEquipSets.EditButton);
            var grid = h.Menus.Top!;
            Assert.True(grid.IsEquipSetGrid);
            Assert.Equal(Shield, grid.EquipSetItemIn(StockUiCheck.ButtonOf(EquipSlotId.Sub)));
            // image-12: the storage's equipment beside the grid (the set's first item is in the inventory): Storage Options,
            // Remove piece., Keep same piece., then the items; the grid's help bar is "Equipment Sets" with the slot's help.
            Assert.Equal(new[] { StockUiItems.StorageOptionsText, "Remove piece.", "Keep same piece.", "Bronze Cap", "Lauan Shield" },
                grid.CompanionRows.Select(r => r.Name));
            Assert.Equal(StockUiEquipSets.Title, grid.ItemHelpBar!.Value.Title);
            grid.SelectedButtonId = StockUiCheck.ButtonOf(EquipSlotId.Sub);
            Assert.Equal(Shield, grid.DescribedItem);
            // The options and the set list give way to the grid and its list (image-12).
            Assert.True(grid.OverlapsAuthored(h.Menus.OpenMenus.First(m => m.IsEquipSetOptions)));
            Assert.True(grid.OverlapsAuthored(h.Menus.OpenMenus.First(m => m.IsEquipSetList)));

            // Main: the list right of the grid (x 198) takes the cursor; Storage Options opens mcrmogc (image-14), the wardrobe there.
            h.Choose(StockUiCheck.ButtonOf(EquipSlotId.Main));
            var slotList = h.Menus.Top!;
            Assert.True(slotList.IsItemList);
            Assert.Equal(StockUiEquipSets.SlotListX, slotList.Menu.Frame.X);
            Assert.Equal(StockUiEquipSets.Title, slotList.ItemHelpBar!.Value.Title);
            slotList.SelectedButtonId = 1; // Storage Options
            h.Menus.Activate();
            Assert.True(h.Menus.Top!.IsEquipSetStorage);
            Assert.Equal(StockUiEquipSets.StorageMenu, h.Menus.Top!.Name);
            h.Choose(StockUiEquipSets.StorageButtonOf(ContainerId.Wardrobe));
            Assert.Same(slotList, h.Menus.Top);
            Assert.Equal(ContainerId.Wardrobe, grid.EquipContainer);
            int sword = slotList.ItemRows.ToList().FindIndex(r => r.ItemId == BronzeSword);
            Assert.Equal(3, sword);
            h.Choose(sword + 1);

            Assert.Same(grid, h.Menus.Top);
            Assert.Equal(EquipmentSetPiece.Item(ContainerId.Wardrobe, 1, BronzeSword), h.Book.Get(1)[EquipSlotId.Main]);
            var check = Assert.Single(h.Checks);
            Assert.Equal(EquipSlotId.Main, check.Slot);
            Assert.Equal(new EquipsetRequestItem(true, false, ContainerId.Wardrobe, 1, BronzeSword), check.Change);
            Assert.Equal(new EquipsetRequestItem(true, false, ContainerId.Inventory, 5, Shield), check.Others[(int)EquipSlotId.Sub]);
            Assert.Equal(default, check.Others[(int)EquipSlotId.Main]);

            // The old answer (held before the check) is ignored; the new one replaces the set's slots (LandSandBoat: by slot).
            h.Menus.OnEquipSetValidated(h.Validation);
            var answer = new EquipsetSlotEntry[17];
            answer[(int)EquipSlotId.Main] = new EquipsetSlotEntry(true, false, ContainerId.Wardrobe, 1, BronzeSword);
            answer[(int)EquipSlotId.Sub] = new EquipsetSlotEntry(false, true, ContainerId.Inventory, 0, 0);
            h.Menus.OnEquipSetValidated(answer);
            Assert.Equal(EquipmentSetPieceKind.Remove, h.Book.Get(1)[EquipSlotId.Sub].Kind);
            Assert.Equal("Tank", h.Book.Get(1).Name);
            // Answered once: a later 0x116 / 0x117 does not touch it again.
            answer[(int)EquipSlotId.Sub] = default;
            h.Menus.OnEquipSetValidated(answer);
            Assert.Equal(EquipmentSetPieceKind.Remove, h.Book.Get(1)[EquipSlotId.Sub].Kind);

            // The list opens on the set's item; "Remove piece." marks the slot and asks the server (RemoveItemFlg).
            h.Choose(StockUiCheck.ButtonOf(EquipSlotId.Main));
            var again = h.Menus.Top!;
            Assert.Equal(sword + 1, again.SelectedButtonId);
            h.Choose(2);
            Assert.Equal(EquipmentSetPieceKind.Remove, h.Book.Get(1)[EquipSlotId.Main].Kind);
            Assert.True(grid.IsEquipSetRemoved(StockUiCheck.ButtonOf(EquipSlotId.Main)));
            Assert.Equal(2, h.Checks.Count);
            Assert.Equal(new EquipsetRequestItem(false, true, ContainerId.Inventory, 0, 0), h.Checks[1].Change);

            // "Keep same piece." empties it and sends nothing.
            h.Choose(StockUiCheck.ButtonOf(EquipSlotId.Main));
            h.Choose(3);
            Assert.Equal(EquipmentSetPieceKind.Empty, h.Book.Get(1)[EquipSlotId.Main].Kind);
            Assert.Equal(2, h.Checks.Count);
        }
        [Fact]
        public void EquipSets_ChangeNameTypesInTheSetsCapsule_EnterKeepsIt_EscapeDropsIt()
        {
            var h = new SetHarness();
            h.Book.Set(4, new EquipmentSet("Old"));
            h.OpenOptions(4);
            h.Choose(StockUiEquipSets.ChangeNameButton);
            // The options close; the list's capsule for set 4 is the field, starting with the name (image-16).
            var list = h.Menus.Top!;
            Assert.True(list.IsEquipSetList);
            Assert.True(h.Menus.IsEditingName);
            Assert.Equal(4, list.EquipSetNameEdit!.Number);
            Assert.Equal("Old", list.EquipSetNameEdit.Text);
            // The menus take no input meanwhile (the shared text field has the keyboard).
            var input = new InputState();
            Assert.True(h.Menus.ProcessInput(input, TimeSpan.FromMilliseconds(16)));
            var field = h.Menus.MacroField;
            void Key(GordianKey key) => field.HandleKey(key, InputModifiers.None);

            for (int i = 0; i < 4; i++) Key(GordianKey.Backspace); // the fourth has nothing left to delete
            field.InsertText("BehavResearch\u00e9\t!!!"); // non-ASCII and control characters are dropped; 15 at most
            Assert.Equal("BehavResearch!!", h.Menus.NameEdit!.Text);
            Key(GordianKey.Up); // no neighbouring field: Up / Down do nothing
            Key(GordianKey.Down);
            Assert.True(h.Menus.IsEditingName);
            Key(GordianKey.Enter);
            Assert.False(h.Menus.IsEditingName);
            Assert.Equal("BehavResearch!!", h.Book.Get(4).Name);

            // Escape leaves the name (the field's EscapeKeepsText is off here, #400); an empty name gives the set its default.
            h.OpenOptions(4);
            h.Choose(StockUiEquipSets.ChangeNameButton);
            Assert.False(field.EscapeKeepsText);
            field.InsertText("x");
            Key(GordianKey.Escape);
            Assert.False(h.Menus.IsEditingName);
            Assert.Equal("BehavResearch!!", h.Book.Get(4).Name);
            h.Choose(4); // the list's row 4 again
            h.Choose(StockUiEquipSets.ChangeNameButton);
            for (int i = 0; i < 20; i++) Key(GordianKey.Backspace);
            Key(GordianKey.Enter);
            Assert.Equal("Set004", h.Book.Get(4).Name);

            // The caret edits in place, as the macro editor's field does (#400).
            h.Choose(4);
            h.Choose(StockUiEquipSets.ChangeNameButton);
            Key(GordianKey.Home);
            field.InsertText("Z");
            Key(GordianKey.Enter);
            Assert.Equal("ZSet004", h.Book.Get(4).Name);

            // The gamepad's Cancel drops the edit, its Confirm keeps it; a macro line's field still keeps its text on Escape.
            h.Choose(4);
            h.Choose(StockUiEquipSets.ChangeNameButton);
            field.InsertText("!");
            var profile = InputProfile.CreateCompact();
            input.SetKeyDown(GordianKey.Escape);
            input.MenuContext = true;
            input.Update(profile, TimeSpan.FromMilliseconds(16));
            Assert.True(h.Menus.ProcessInput(input, TimeSpan.FromMilliseconds(16)));
            Assert.False(h.Menus.IsEditingName);
            Assert.Equal("ZSet004", h.Book.Get(4).Name);
            // The gamepad's d-pad moves the caret (left / right) in the field, and up / down do nothing in Change Name (#400).
            h.Choose(4);
            h.Choose(StockUiEquipSets.ChangeNameButton);
            var pad = InputProfile.CreateGamepadDefault();
            var padInput = new InputState { MenuContext = true };
            void Press(GamepadButton button)
            {
                padInput.SetGamepadState(new GamepadState(true, button, System.Numerics.Vector2.Zero, System.Numerics.Vector2.Zero, 0, 0));
                padInput.Update(pad, TimeSpan.FromMilliseconds(16));
                h.Menus.ProcessInput(padInput, TimeSpan.FromMilliseconds(16));
                padInput.SetGamepadState(new GamepadState(true, GamepadButton.None, System.Numerics.Vector2.Zero, System.Numerics.Vector2.Zero, 0, 0));
                padInput.Update(pad, TimeSpan.FromMilliseconds(16));
            }
            Press(GamepadButton.DPadLeft);
            Press(GamepadButton.DPadDown);
            Assert.True(h.Menus.IsEditingName);
            Assert.Equal(h.Book.Get(4).Name.Length - 1, h.Menus.MacroField.Caret);
            h.Menus.MacroField.Escape();

            var macroLine = new StockUiTextField();
            string? kept = null;
            macroLine.Open("abc", 10, (text, _) => kept = text);
            macroLine.HandleKey(GordianKey.Escape, InputModifiers.None);
            Assert.Equal("abc", kept);
        }
        [Fact]
        public void RetailMenus_KeyItemAndEquipSetWindowsResolve()
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var library = UiResourceLibrary.Load(rm);
            if (library == null) return;
            Assert.True(library.TryGetMenu(StockUiKeyItems.WindowMenu, out var evitem));
            Assert.Equal((16, 48, 366, 248, 59), (evitem.Frame.X, evitem.Frame.Y, evitem.Frame.Width, evitem.Frame.Height, evitem.Frame.TitleTextId));
            Assert.True(library.TryGetMenu(StockUiEquipSets.MacroMenu, out var macro));
            Assert.Equal(830, macro.FindButton(StockUiEquipSets.EditEquipSetButton)!.HelpTextId);
            Assert.True(library.TryGetMenu(StockUiEquipSets.ListMenu, out var list));
            Assert.Equal(20, Enumerable.Range(1, 20).Count(i => list.FindButton(i) != null));
            Assert.True(library.TryGetMenu(StockUiEquipSets.OptionsMenu, out var options));
            Assert.Equal(new[] { 831, 832, 833, 834, 835, 836, 837, 921 }, Enumerable.Range(1, 8).Select(i => options.FindButton(i)!.HelpTextId));
            Assert.True(library.TryGetMenu(StockUiEquipSets.GridMenu, out var grid));
            Assert.Equal((16, 48, 172), (grid.Frame.X, grid.Frame.Y, grid.Frame.TitleTextId));
            // The grid's buttons are the check window's order (help 243 Main ... 258 Feet).
            Assert.Equal(244, grid.FindButton(StockUiCheck.ButtonOf(EquipSlotId.Head))!.HelpTextId);
            Assert.Equal(258, grid.FindButton(StockUiCheck.ButtonOf(EquipSlotId.Feet))!.HelpTextId);

            Assert.True(library.TryGetMenu(StockUiKeyItems.PromptMenu, out var examine));
            Assert.Equal((55, 74, 55, 112), (examine.FindButton(1)!.X, examine.FindButton(1)!.Y, examine.FindButton(2)!.X, examine.FindButton(2)!.Y));
            Assert.True(library.TryGetMenu(StockUiEquipSets.StorageMenu, out var storage));
            Assert.Equal((384, 48, 802, 16), (storage.Frame.X, storage.Frame.Y, storage.Frame.HelpTextId, storage.Buttons.Count));
            // The menus' own texts (ROM/165/72).
            Assert.True(rm.TryGetString(DMsgCategory.MenuMessages, StockUiKeyItems.ExamineText, out var examineText));
            Assert.Equal(StockUiKeyItems.ExamineFallback, examineText);
            foreach (int id in new[] { StockUiKeyItems.NoKeyItemsText })
            {
                Assert.True(rm.TryGetString(DMsgCategory.MenuMessages, id, out var text));
                Assert.Equal(StockUiKeyItems.NoKeyItemsFallback, text);
            }
            foreach (int id in new[] { StockUiEquipSets.RemovePieceText, StockUiEquipSets.KeepSamePieceText, StockUiEquipSets.PastePromptText, StockUiEquipSets.DeletePromptText, StockUiEquipSets.UseCurrentPromptText })
            {
                Assert.True(rm.TryGetString(DMsgCategory.MenuMessages, id, out var text));
                Assert.Equal(StockUiEquipSets.FallbackText(id), text);
            }

            // The flows run on the retail frames.
            var h = new SetHarness();
            h.Menus.Library = library;
            var options1 = h.OpenOptions(1);
            Assert.True(options1.IsEquipSetOptions);
            var k = new KeyItemHarness();
            k.Menus.Library = library;
            Assert.True(k.Open().IsKeyItemCategories);
        }

        #endregion

        #region /equipset, /lockstyleset and the style lock

        private sealed class ServiceHarness
        {
            public readonly List<byte[]> Sent = new();
            public readonly InventoryState Inventory = new();
            public readonly LocalPlayerState LocalPlayer = new() { ServerId = 0x01020304 };
            public readonly PlayerActionService Service;

            public ServiceHarness()
            {
                var profile = new SessionProfile();
                var world = new WorldState();
                Task Capture(ReadOnlyMemory<byte> mem, bool urgent)
                {
                    Sent.Add(mem.ToArray());
                    return Task.CompletedTask;
                }
                Service = new PlayerActionService(profile, world, LocalPlayer,
                    new CombatPacketModule(new CombatState(), LocalPlayer, Capture), new ChatPacketModule(Capture), new PartyPacketModule(new PartyState(), Capture),
                    new EntityPacketModule(world, LocalPlayer, Capture), new LifecyclePacketModule(profile, Capture), Capture);
                Service.InventoryModule = new InventoryPacketModule(Inventory, LocalPlayer, Capture);
                Service.Menus.ItemLookup = id => Records.TryGetValue(id, out var r) ? r : null;
                Inventory.SetItem(ContainerId.Inventory, 4, BronzeCap, 1, ItemLockFlag.Normal);
                Inventory.SetItem(ContainerId.Wardrobe, 7, BronzeSword, 1, ItemLockFlag.Normal);
                Inventory.SetItem(ContainerId.Wardrobe, 8, HomeRing, 1, ItemLockFlag.Normal);
                Inventory.SetItem(ContainerId.Wardrobe, 9, HomeRing, 1, ItemLockFlag.Normal);
            }

            public byte[] Single(ushort id)
            {
                var packet = Assert.Single(Sent);
                Assert.True(PacketHeader.TryParse(packet, out var header));
                Assert.Equal(id, header.PacketId);
                return packet;
            }
        }

        [Theory]
        [InlineData("/equipset 12", ChatCommandResultKind.Equipset, "12")]
        [InlineData("/lockstyleset 3 echo", ChatCommandResultKind.LockstyleSet, "3 echo")]
        public void Router_ParsesTheSetCommands(string input, ChatCommandResultKind kind, string args)
        {
            var parsed = ChatCommandRouter.Parse(input);
            Assert.Equal(kind, parsed.Kind);
            Assert.Equal(args, parsed.Message);
        }

        [Fact]
        public async Task Equipset_Sends0x051_FindingMovedItems_AndRemovePieces()
        {
            var h = new ServiceHarness();
            // The sword was put in the set from the inventory and has since moved to the wardrobe; both rings are the same id.
            h.Service.EquipmentSets.Set(5, new EquipmentSet("Melee")
                .WithPiece(EquipSlotId.Main, EquipmentSetPiece.Item(ContainerId.Inventory, 2, BronzeSword))
                .WithPiece(EquipSlotId.Head, EquipmentSetPiece.Item(ContainerId.Inventory, 4, BronzeCap))
                .WithPiece(EquipSlotId.Ring1, EquipmentSetPiece.Item(ContainerId.Wardrobe, 8, HomeRing))
                .WithPiece(EquipSlotId.Ring2, EquipmentSetPiece.Item(ContainerId.Wardrobe, 8, HomeRing))
                .WithPiece(EquipSlotId.Sub, EquipmentSetPiece.Remove));

            var result = await h.Service.ExecuteCommandAsync("/equipset 5");

            Assert.True(result.Success);
            Assert.Equal(string.Empty, result.Message);
            byte[] p = h.Single(0x051);
            Assert.Equal(5, p[4]); // Count
            var entries = Enumerable.Range(0, 5).Select(i => (p[8 + i * 4], (EquipSlotId)p[9 + i * 4], (ContainerId)p[10 + i * 4])).ToList();
            Assert.Equal(new[]
            {
                ((byte)7, EquipSlotId.Main, ContainerId.Wardrobe),
                ((byte)0, EquipSlotId.Sub, ContainerId.Inventory),
                ((byte)4, EquipSlotId.Head, ContainerId.Inventory),
                ((byte)8, EquipSlotId.Ring1, ContainerId.Wardrobe),
                ((byte)9, EquipSlotId.Ring2, ContainerId.Wardrobe),
            }, entries);
        }

        [Fact]
        public async Task Equipset_NamesItemsNotFound_EmptyAndBadNumbersSendNothing()
        {
            var h = new ServiceHarness();
            h.Service.EquipmentSets.Set(1, new EquipmentSet("Gone").WithPiece(EquipSlotId.Sub, EquipmentSetPiece.Item(ContainerId.Inventory, 9, Shield)));
            var missing = await h.Service.ExecuteCommandAsync("/equipset 1");
            Assert.Equal(PlayerActionResultKind.Warning, missing.Kind);
            Assert.Contains("Lauan Shield", missing.Message);
            Assert.Empty(h.Sent);
            foreach (string bad in new[] { "/equipset", "/equipset 0", "/equipset 201", "/equipset x", "/equipset 2" })
            {
                var result = await h.Service.ExecuteCommandAsync(bad);
                Assert.Equal(PlayerActionResultKind.Warning, result.Kind);
            }
            Assert.Empty(h.Sent);
        }

        [Fact]
        public async Task LockstyleSet_Sends0x053SetWithAllSixteenSlots_AndTheEchoFlag()
        {
            var h = new ServiceHarness();
            h.Service.EquipmentSets.Set(7, new EquipmentSet("Look")
                .WithPiece(EquipSlotId.Main, EquipmentSetPiece.Item(ContainerId.Wardrobe, 7, BronzeSword))
                .WithPiece(EquipSlotId.Head, EquipmentSetPiece.Item(ContainerId.Inventory, 30, BronzeCap)));

            var result = await h.Service.ExecuteCommandAsync("/lockstyleset 7 echo");

            Assert.True(result.Success);
            byte[] p = h.Single(0x053);
            Assert.Equal(16, p[4]); // Count
            Assert.Equal((byte)LockstyleMode.Set, p[5]);
            Assert.Equal(1, p[6]); // echo
            // Items[i] at 8 + 8 i: ItemIndex, EquipKind, Category, pad, ItemNo.
            Assert.Equal((7, (int)EquipSlotId.Main, (int)ContainerId.Wardrobe, BronzeSword), (p[8], p[9], p[10], BitConverter.ToUInt16(p, 12)));
            int head = 8 + 8 * (int)EquipSlotId.Head;
            Assert.Equal((4, (int)EquipSlotId.Head, (int)ContainerId.Inventory, BronzeCap), (p[head], p[head + 1], p[head + 2], BitConverter.ToUInt16(p, head + 4)));
            int feet = 8 + 8 * (int)EquipSlotId.Feet;
            Assert.Equal(((int)EquipSlotId.Feet, 0), (p[feet + 1], BitConverter.ToUInt16(p, feet + 4)));
            Assert.Equal(StyleLockStatus.On, h.Service.StyleLock);

            h.Sent.Clear();
            Assert.Equal(PlayerActionResultKind.Warning, (await h.Service.ExecuteCommandAsync("/lockstyleset 7 loud")).Kind);
            Assert.Equal(PlayerActionResultKind.Warning, (await h.Service.ExecuteCommandAsync("/lockstyleset 8")).Kind); // empty
            Assert.Empty(h.Sent);
        }

        [Fact]
        public async Task StyleLock_ZoneInSendsContinueOrDisable_JobChangeSendsDisable()
        {
            var h = new ServiceHarness();
            // After login the lock is unknown: nothing goes out on zone-in.
            await h.Service.OnZoneInAsync();
            Assert.Empty(h.Sent);

            // "Style lock mode enabled." (MsgStd 267): Continue on the next zone-in.
            h.Service.NoteSystemMessage(PlayerActionService.StyleLockOnMessage);
            await h.Service.OnZoneInAsync();
            Assert.Equal((byte)LockstyleMode.Continue, h.Single(0x053)[5]);

            // "Style lock mode is disabled." (270): Disable.
            h.Sent.Clear();
            h.Service.NoteSystemMessage(PlayerActionService.StyleLockIsOffMessage);
            await h.Service.OnZoneInAsync();
            Assert.Equal((byte)LockstyleMode.Disable, h.Single(0x053)[5]);

            // /lockstyle on turns it on.
            h.Sent.Clear();
            await h.Service.ExecuteCommandAsync("/lockstyle on");
            Assert.Equal(StyleLockStatus.On, h.Service.StyleLock);

            // Jobs: the first report records them; a change of main job sends Disable; the sub job arriving from none does not.
            h.Sent.Clear();
            await h.Service.OnJobsReportedAsync(JobId.Warrior, JobId.None);
            await h.Service.OnJobsReportedAsync(JobId.Warrior, JobId.Monk);
            Assert.Empty(h.Sent);
            await h.Service.OnJobsReportedAsync(JobId.Bard, JobId.Monk);
            Assert.Equal((byte)LockstyleMode.Disable, h.Single(0x053)[5]);
            Assert.Equal(StyleLockStatus.Off, h.Service.StyleLock);
            h.Sent.Clear();
            await h.Service.OnJobsReportedAsync(JobId.Bard, JobId.Monk);
            Assert.Empty(h.Sent);
        }

        [Fact]
        public void StyleLockMessages_PrintFromTheSystemMessageTable()
        {
            var previous = StandardMessages.TableText;
            try
            {
                StandardMessages.TableText = (id, _) => id == 267 ? "Style lock mode enabled." : null;
                Assert.Equal("Style lock mode enabled.", StandardMessages.FormatMessage(new SystemMessage(0, 0, 267, 0, string.Empty, DateTime.UtcNow)));
                Assert.Equal("Msg#268", StandardMessages.FormatMessage(new SystemMessage(0, 0, 268, 0, string.Empty, DateTime.UtcNow)));
            }
            finally
            {
                StandardMessages.TableText = previous;
            }

            // The retail table (ROM/27/76, file 7031) holds MsgStd 267-270 (round 1 printed them as "Msg#267" / "Msg#268").
            string path = Path.Combine(GameDirectory, "ROM", "27", "76.DAT");
            if (!File.Exists(path)) return;
            var tables = new Gordian.Core.Resources.Tables.ClientMessageTables(id => id == Gordian.Core.Resources.Tables.ClientMessageTables.SystemMessagesFileId ? File.ReadAllBytes(path) : null);
            Assert.Equal("Style lock mode enabled.", tables.SystemMessages!.GetPlainText(267).Trim());
            Assert.Equal("Style lock mode disabled.", tables.SystemMessages!.GetPlainText(268).Trim());
        }
        #endregion
    }
}
