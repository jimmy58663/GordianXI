// tests/Gordian.Engine.Tests/Macros/MacroLibraryTests.cs
using System;
using System.IO;
using System.Linq;
using Gordian.Engine.Macros;
using Xunit;

namespace Gordian.Engine.Tests.Macros
{
    public class MacroLibraryTests
    {
        [Fact]
        public void NewLibrary_HasFortyBooksOfTenEmptySets()
        {
            var library = new MacroLibrary();
            Assert.Equal(0, library.CurrentBook);
            Assert.Equal(0, library.CurrentSet);
            Assert.True(library.IsSetEmpty(39, 9));
            Assert.Equal("Book01", library.GetBookTitle(0));
            Assert.Equal("Book40", library.GetBookTitle(39));
            Assert.Equal(20, library.GetSet(0, 0).Length);
        }

        [Fact]
        public void Macro_ClipsTitleAndLinesToRetailLimits()
        {
            var macro = new Macro("Metatron Torment", new[] { new string('x', 70), "/wait 1", "a", "b", "c", "d", "seventh" });
            Assert.Equal("Metatron", macro.Title);
            Assert.Equal(MacroLibrary.LinesPerMacro, macro.Lines.Count);
            Assert.Equal(60, macro.Lines[0].Length);
            Assert.Equal("d", macro.Lines[5]);
        }

        [Fact]
        public void KeysAndSlots_FollowTheRetailOrder()
        {
            Assert.Equal("Ctrl1", MacroLibrary.KeyLabel(MacroPalette.Ctrl, 0));
            Assert.Equal("Ctrl0", MacroLibrary.KeyLabel(MacroPalette.Ctrl, 9));
            Assert.Equal("Alt3", MacroLibrary.KeyLabel(MacroPalette.Alt, 2));
            Assert.Equal(12, new MacroSlot(0, 0, MacroPalette.Alt, 2).IndexInSet);
            Assert.Equal(9, new MacroSlot(0, 0, MacroPalette.Ctrl, 9).IndexInSet);
        }

        [Fact]
        public void SetAndCycle_ChangeTheCurrentSetAndRaiseChanged()
        {
            var library = new MacroLibrary();
            int changes = 0;
            library.Changed += () => changes++;
            library.SelectSet(4, 9);
            library.CycleSet(1);
            Assert.Equal((4, 0), (library.CurrentBook, library.CurrentSet));
            library.CycleSet(-1);
            Assert.Equal(9, library.CurrentSet);
            library.SelectBook(10);
            Assert.Equal((10, 9), (library.CurrentBook, library.CurrentSet));
            Assert.Equal(4, changes);

            var slot = library.CurrentSlot(MacroPalette.Ctrl, 0);
            library.Set(slot, new Macro("Cure", new[] { "/ma \"Cure\" <stpc>" }));
            Assert.Equal("Cure", library.GetCurrent(MacroPalette.Ctrl, 0).Title);
            Assert.Equal(5, changes);
            library.Set(slot, new Macro("Cure", new[] { "/ma \"Cure\" <stpc>" }));
            Assert.Equal(5, changes); // the same macro again is no change
        }

        [Fact]
        public void BookTitles_DefaultNamesAreNotStored()
        {
            var library = new MacroLibrary();
            library.SetBookTitle(2, "Book03");
            Assert.Equal(string.Empty, library.GetStoredBookTitle(2));
            library.SetBookTitle(2, "A very long book title");
            Assert.Equal("A very long boo", library.GetBookTitle(2));
        }

        [Fact]
        public void Store_RoundTripsOnlyWhatIsUsed()
        {
            var library = new MacroLibrary();
            library.SetBookTitle(0, "WAR");
            library.Set(new MacroSlot(0, 2, MacroPalette.Alt, 9), new Macro("Provoke", new[] { "/ja \"Provoke\" <t>", "/recast \"Provoke\"" }));
            library.Set(new MacroSlot(39, 9, MacroPalette.Ctrl, 0), new Macro(string.Empty, new[] { "/echo hi" }));
            library.SelectSet(22, 2);

            string json = MacroStore.ToJson(library);
            Assert.Contains("\"Alt0\"", json);
            Assert.DoesNotContain("Book02", json);
            var loaded = MacroStore.FromJson(json);
            Assert.Equal((22, 2), (loaded.CurrentBook, loaded.CurrentSet));
            Assert.Equal("WAR", loaded.GetBookTitle(0));
            Assert.Equal("/recast \"Provoke\"", loaded.Get(new MacroSlot(0, 2, MacroPalette.Alt, 9)).Lines[1]);
            Assert.Equal("/echo hi", loaded.Get(new MacroSlot(39, 9, MacroPalette.Ctrl, 0)).Lines[0]);
            Assert.True(loaded.IsSetEmpty(0, 0));
        }

        [Fact]
        public void Store_SavesAndLoadsAFile()
        {
            string dir = Path.Combine(Path.GetTempPath(), "gordian-macros-" + Guid.NewGuid().ToString("N"));
            try
            {
                string path = Path.Combine(dir, "Tester.json");
                var library = new MacroLibrary();
                library.Set(new MacroSlot(1, 1, MacroPalette.Ctrl, 4), new Macro("Hi", new[] { "/wave" }));
                MacroStore.Save(library, path);
                var loaded = MacroStore.LoadOrEmpty(path);
                Assert.Equal("Hi", loaded.Get(new MacroSlot(1, 1, MacroPalette.Ctrl, 4)).Title);
                Assert.True(MacroStore.LoadOrEmpty(Path.Combine(dir, "missing.json")).IsSetEmpty(0, 0));
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }
    }
}
