using System.IO;
using System.Linq;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Ui;
using Gordian.Core.Ui;
using Xunit;

namespace Gordian.Core.Tests.Ui
{
    public class ZoneLoadingIndicatorTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";

        [Fact]
        public void ImageAt_CyclesFromAllLitThroughTheDimmedPairs()
        {
            double f = ZoneLoadingIndicator.FrameSeconds;
            Assert.Equal(165, ZoneLoadingIndicator.ImageAt(0));
            Assert.Equal(165, ZoneLoadingIndicator.ImageAt(f * 0.5));
            Assert.Equal(166, ZoneLoadingIndicator.ImageAt(f * 1.5));
            Assert.Equal(169, ZoneLoadingIndicator.ImageAt(f * 4.5));
            Assert.Equal(165, ZoneLoadingIndicator.ImageAt(f * 5.5));
            Assert.Equal(165, ZoneLoadingIndicator.ImageAt(double.NaN));
            Assert.Equal(165, ZoneLoadingIndicator.ImageAt(-1));
        }

        [Fact]
        public void Position_MatchesTheRetailRecording()
        {
            // 2558 x 1440 at UI scale 1: the sprite's box spans x 2326-2532, y 1392-1408.
            var (x, y) = ZoneLoadingIndicator.Position(2558, 1440, 1f);
            Assert.Equal(2326f, x);
            Assert.Equal(1392f, y);
            Assert.Equal(2532f, x + ZoneLoadingIndicator.Width);
        }

        /// <summary>
        /// keytops3 images 165-169 of the retail menu DAT: 206 x 16 sprites of 16 glyphs ("Downloading data ...") and eight
        /// dots, 165 all lit and 166-169 dimming one mirrored pair each, outermost first.
        /// </summary>
        [Fact]
        public void RetailSprites_AreTheDownloadingDataFrames()
        {
            if (!Directory.Exists(GameDirectory)) return;
            var resources = new ResourceManager(GameDirectory);
            resources.InitializeFileTable();
            var library = UiResourceLibrary.Load(resources);
            Assert.NotNull(library);
            Assert.True(library!.TryGetGroup(ZoneLoadingIndicator.GroupName, out var group));

            int[] dotX = { 0, 6, 14, 25 };
            for (int i = 0; i <= ZoneLoadingIndicator.DimmedImageCount; i++)
            {
                var image = group.Images[ZoneLoadingIndicator.AllLitImage + i];
                Assert.Equal(24, image.Parts.Count);
                Assert.Equal(16, image.Parts.Count(p => p.TextureName.Trim().EndsWith("font", System.StringComparison.OrdinalIgnoreCase) && !p.TextureName.Contains("menu2fon")));
                var dots = image.Parts.Where(p => p.TextureName.Contains("menu2fon")).ToList();
                Assert.Equal(8, dots.Count);
                Assert.Equal(ZoneLoadingIndicator.Width, image.Parts.Max(p => (float)p.BottomRight.X));
                for (int d = 0; d < 4; d++)
                {
                    var left = dots.Single(p => p.TopLeft.X == dotX[d]);
                    byte expected = i == d + 1 ? (byte)0x60 : (byte)0x7F;
                    Assert.Equal(expected, left.ColorTopLeft.R);
                }
            }
        }
    }
}
