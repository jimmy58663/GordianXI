// tests/Gordian.Core.Tests/Audio/FootwearInfoTests.cs
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gordian.Core.Audio;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Containers;
using Gordian.Core.Resources.Tables;
using Xunit;
using Xunit.Abstractions;

namespace Gordian.Core.Tests.Audio
{
    /// <summary>The footwear Info section digits that pick a footstep pointer (#40).</summary>
    public class FootwearInfoTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
        private readonly ITestOutputHelper _output;

        public FootwearInfoTests(ITestOutputHelper output) => _output = output;

        [Fact]
        public void Retail_FeetDigitsNamePointersTheZoneHas()
        {
            if (!Directory.Exists(GameDirectory))
            {
                return;
            }

            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            byte[] zoneDat = rm.LoadDatBytesByFileId(ZoneDataLoader.GetZoneModelFileId(230))!;
            ZoneSoundTable table = ZoneSoundTable.Read(DatDirectoryTree.Build(zoneDat));
            var digits = new Dictionary<string, int>();
            int read = 0, matched = 0;
            foreach (CharacterRace race in new[] { CharacterRace.HumeMale, CharacterRace.TaruFemale, CharacterRace.Galka })
            {
                for (ushort model = 0; model < 120; model++)
                {
                    if (!CharacterEquipmentResolver.TryResolveGearFileId(race, CharacterSlot.Feet, model, out int fid))
                    {
                        continue;
                    }

                    byte[]? dat = rm.LoadDatBytesByFileId(fid);
                    if (dat is null || !FootwearInfo.TryRead(dat, out FootwearInfo info))
                    {
                        continue;
                    }

                    read++;
                    string key = $"{info.MovementChar}{info.ShakeFactor}";
                    digits[key] = digits.GetValueOrDefault(key) + 1;
                    if (table.FootstepSound(5, info.MovementChar, info.ShakeFactor, running: false) != 0)
                    {
                        matched++;
                    }
                }
            }

            _output.WriteLine($"{read} feet DATs, {matched} with a stone pointer; digits: " +
                string.Join(", ", digits.OrderByDescending(d => d.Value).Select(d => $"{d.Key} x{d.Value}")));
            Assert.True(read > 50);
            Assert.True(matched >= read * 9 / 10, $"{matched} of {read}");
        }
    }
}
