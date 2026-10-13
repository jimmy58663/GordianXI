// tests/Gordian.Core.Tests/Macros/RetailMacroFilesTests.cs
using System;
using System.IO;
using System.Text;
using Gordian.Core.Macros;
using Xunit;

namespace Gordian.Core.Tests.Macros
{
    /// <summary>
    /// The retail macro files (docs/ui/macros.md) with synthesized fixtures built to the layout read from the maintainer's
    /// files, plus a read-only check against the maintainer's install when it is present.
    /// </summary>
    public class RetailMacroFilesTests
    {
        [Fact]
        public void SetFileNames_AreBookTimesTenPlusSet()
        {
            Assert.Equal("mcr.dat", RetailMacroFiles.SetFileName(0, 0));
            Assert.Equal("mcr1.dat", RetailMacroFiles.SetFileName(0, 1));
            Assert.Equal("mcr222.dat", RetailMacroFiles.SetFileName(22, 2));
            Assert.Equal("mcr399.dat", RetailMacroFiles.SetFileName(39, 9));
            Assert.Equal("mcr.ttl", RetailMacroFiles.TitleFileName(19));
            Assert.Equal("mcr_2.ttl", RetailMacroFiles.TitleFileName(20));
        }

        [Fact]
        public void SetFile_RoundTripsWithAValidChecksum()
        {
            var macros = new Macro[MacroLibrary.MacrosPerSet];
            Array.Fill(macros, Macro.Empty);
            macros[0] = new Macro("Ichi", new[] { "/ma \"Utsusemi: Ichi\" <me>", "/recast \"Utsusemi: Ichi\"" });
            macros[19] = new Macro("Retaliat", new[] { "/ja \"Retaliation\" <me>" });
            byte[] file = RetailMacroFiles.EncodeSet(macros);
            Assert.Equal(7624, file.Length);
            Assert.True(RetailMacroFiles.HasValidChecksum(file));
            // Macro 0's first line at 24 + 4, its title at 24 + 370.
            Assert.Equal((byte)'/', file[28]);
            Assert.Equal("Ichi", Encoding.ASCII.GetString(file, 24 + 370, 4));

            var decoded = RetailMacroFiles.DecodeSet(file);
            Assert.NotNull(decoded);
            Assert.Equal(macros[0], decoded![0]);
            Assert.Equal(macros[19], decoded[19]);
            Assert.True(decoded[5].IsEmpty);
        }

        [Fact]
        public void SetFile_DropsAutoTranslatePhrasesAndTitleLeftovers()
        {
            byte[] file = RetailMacroFiles.EncodeSet(new[] { new Macro("Hi", new[] { "/p ab" }) });
            // "/p " then an auto-translate phrase (FD 02 02 0A 1B FD) then "ab".
            byte[] line = { (byte)'/', (byte)'p', (byte)' ', 0xFD, 0x02, 0x02, 0x0A, 0x1B, 0xFD, (byte)'a', (byte)'b', 0 };
            line.CopyTo(file, 28);
            // Bytes after the title's NUL are leftovers ("Ichi\0rk" in a retail file).
            file[24 + 370 + 2] = 0;
            file[24 + 370 + 3] = (byte)'r';
            var decoded = RetailMacroFiles.DecodeSet(file, out int dropped);
            Assert.Equal("/p ab", decoded![0].Lines[0]);
            Assert.Equal("Hi", decoded[0].Title);
            Assert.Equal(1, dropped);
        }

        [Fact]
        public void TitleAndSystemFiles_RoundTrip()
        {
            var titles = new string[20];
            for (int i = 0; i < titles.Length; i++) titles[i] = "Book" + (i + 21);
            titles[0] = "GEO";
            byte[] ttl = RetailMacroFiles.EncodeTitles(titles);
            Assert.Equal(344, ttl.Length);
            Assert.True(RetailMacroFiles.HasValidChecksum(ttl));
            Assert.Equal(titles, RetailMacroFiles.DecodeTitles(ttl));

            byte[] sys = RetailMacroFiles.EncodeSystem(22, 2);
            Assert.Equal(28, sys.Length);
            Assert.Equal(222u, BitConverter.ToUInt32(sys, 24));
            Assert.Equal((22, 2), RetailMacroFiles.DecodeSystem(sys));
            Assert.Null(RetailMacroFiles.DecodeSet(new byte[100]));
        }

        [Fact]
        public void Import_ReadsSetsTitlesAndTheCurrentSet()
        {
            string dir = Path.Combine(Path.GetTempPath(), "gordian-retail-macros-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var set = new Macro[MacroLibrary.MacrosPerSet];
                Array.Fill(set, Macro.Empty);
                set[3] = new Macro("Berserk", new[] { "/ja \"Berserk\" <me>" });
                File.WriteAllBytes(Path.Combine(dir, "mcr12.dat"), RetailMacroFiles.EncodeSet(set));
                var titles = new string[20];
                titles[1] = "MNK";
                File.WriteAllBytes(Path.Combine(dir, "mcr.ttl"), RetailMacroFiles.EncodeTitles(titles));
                File.WriteAllBytes(Path.Combine(dir, "mcr.sys"), RetailMacroFiles.EncodeSystem(1, 2));

                var library = new MacroLibrary();
                library.Set(new MacroSlot(5, 5, MacroPalette.Alt, 0), new Macro("Old", new[] { "/echo old" }));
                int changes = 0;
                library.Changed += () => changes++;
                var result = RetailMacroFiles.Import(dir, library);

                Assert.Equal(1, changes); // one save for the whole import
                Assert.Equal(1, result.Sets);
                Assert.Equal(1, result.Macros);
                Assert.True(result.CurrentSet);
                Assert.Equal((1, 2), (library.CurrentBook, library.CurrentSet));
                Assert.Equal("Berserk", library.Get(new MacroSlot(1, 2, MacroPalette.Ctrl, 3)).Title);
                Assert.Equal("MNK", library.GetBookTitle(1));
                Assert.True(library.IsSetEmpty(5, 5)); // replaced: retail has no file for it
                Assert.True(RetailMacroFiles.HasMacroFiles(dir));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        /// <summary>The maintainer's retail files (read only), when present: every set file decodes and checks out.</summary>
        [Fact]
        public void RetailInstall_SetFilesDecodeWhenPresent()
        {
            string root = Path.Combine("G:" + Path.DirectorySeparatorChar, "Program Files (x86)", "PlayOnline", "SquareEnix", "FINAL FANTASY XI", "USER", "e50480");
            if (!Directory.Exists(root)) return;
            int checkedFiles = 0;
            foreach (string path in Directory.EnumerateFiles(root, "mcr*.dat"))
            {
                byte[] bytes = File.ReadAllBytes(path);
                Assert.True(RetailMacroFiles.HasValidChecksum(bytes), path);
                Assert.NotNull(RetailMacroFiles.DecodeSet(bytes));
                checkedFiles++;
            }
            Assert.True(checkedFiles > 0);
            var library = new MacroLibrary();
            var result = RetailMacroFiles.Import(root, library);
            Assert.Equal(checkedFiles, result.Sets);
            Assert.True(result.CurrentSet);
        }
    }
}
