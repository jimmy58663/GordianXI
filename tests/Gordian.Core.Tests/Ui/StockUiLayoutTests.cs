// tests/Gordian.Core.Tests/Ui/StockUiLayoutTests.cs
using System;
using System.IO;
using Gordian.Core.Resources.Ui;
using Gordian.Core.Ui;
using Xunit;

namespace Gordian.Core.Tests.Ui
{
    public class StockUiLayoutTests
    {
        // The retail solo party window: 112 x 34 at (384, 398), anchored bottom-right.
        private static readonly UiMenuFrame Party = new() { X = 384, Y = 398, Width = 112, Height = 34, Anchor = UiAnchor.BottomRight };

        // The retail log window: 366 x 134 at (16, 298), anchored bottom-left.
        private static readonly UiMenuFrame Log = new() { X = 16, Y = 298, Width = 366, Height = 134, Anchor = UiAnchor.BottomLeft };

        [Fact]
        public void Resolve_AtLayoutSize_ReturnsAuthoredPositions()
        {
            var layout = new StockUiLayout();
            var party = layout.Resolve(StockUiWindowIds.Party, Party, 512, 448);
            Assert.Equal((384f, 398f, 1f), (party.X, party.Y, party.Scale));
        }

        [Fact]
        public void Resolve_KeepsDistanceToAnchorCorner()
        {
            var layout = new StockUiLayout();
            var party = layout.Resolve(StockUiWindowIds.Party, Party, 1920, 1080);
            Assert.Equal(1920 - 128, party.X);
            Assert.Equal(1080 - 50, party.Y);

            var log = layout.Resolve(StockUiWindowIds.Log, Log, 1920, 1080);
            Assert.Equal(16, log.X);
            Assert.Equal(1080 - 150, log.Y);
        }

        [Fact]
        public void Resolve_ScalesMarginsAndSize()
        {
            var layout = new StockUiLayout { Scale = 2.0f };
            var party = layout.Resolve(StockUiWindowIds.Party, Party, 1920, 1080);
            Assert.Equal(1920 - 32, party.Right(Party.Width));
            Assert.Equal(1080 - 32, party.Bottom(Party.Height));
        }

        [Fact]
        public void Resolve_ClampsOntoSmallScreens()
        {
            var layout = new StockUiLayout { Scale = 4.0f };
            var log = layout.Resolve(StockUiWindowIds.Log, Log, 800, 600);
            Assert.Equal(0, log.X);
            Assert.Equal(0, log.Y); // 536 x 4 does not fit: pinned to the top-left
        }

        [Fact]
        public void MoveTo_ReanchorsToNearestCornerAndSurvivesResize()
        {
            var layout = new StockUiLayout();
            layout.MoveTo(StockUiWindowIds.Party, Party, 100, 50, 1920, 1080); // dragged to the top-left
            var moved = layout.Resolve(StockUiWindowIds.Party, Party, 1920, 1080);
            Assert.Equal((100f, 50f), (moved.X, moved.Y));
            Assert.Equal(UiAnchor.TopLeft, layout.Windows[StockUiWindowIds.Party].Anchor);

            layout.MoveTo(StockUiWindowIds.Log, Log, 1500, 900, 1920, 1080); // dragged to the bottom-right
            var log = layout.Resolve(StockUiWindowIds.Log, Log, 2560, 1440);
            Assert.Equal(2560 - 420, log.X, 3);
            Assert.Equal(1440 - 180, log.Y, 3);
        }

        [Fact]
        public void HiddenAndReset()
        {
            var layout = new StockUiLayout();
            int changes = 0;
            layout.Changed += () => changes++;

            layout.SetHidden(StockUiWindowIds.Log, true);
            Assert.True(layout.Resolve(StockUiWindowIds.Log, Log, 1920, 1080).Hidden);
            layout.SetHidden(StockUiWindowIds.Log, false);
            Assert.False(layout.Windows.ContainsKey(StockUiWindowIds.Log)); // an empty override is dropped

            layout.MoveTo(StockUiWindowIds.Party, Party, 10, 10, 1920, 1080);
            layout.ResetAll();
            Assert.Empty(layout.Windows);
            Assert.Equal(4, changes);
        }

        [Fact]
        public void SaveAndLoad_RoundTripsCaseInsensitively()
        {
            string path = Path.Combine(Path.GetTempPath(), $"gordian_ui_layout_{Guid.NewGuid():N}.json");
            try
            {
                var layout = new StockUiLayout { Scale = 1.5f };
                layout.MoveTo(StockUiWindowIds.Party, Party, 100, 50, 1920, 1080);
                layout.SetWindowScale(StockUiWindowIds.Log, 0.75f);
                layout.SaveToFile(path);

                var loaded = StockUiLayout.LoadOrDefault(path);
                Assert.Equal(1.5f, loaded.Scale);
                Assert.True(loaded.Windows.ContainsKey("PARTY"));
                Assert.Equal(0.75f, loaded.Windows[StockUiWindowIds.Log].Scale);
                var party = loaded.Resolve(StockUiWindowIds.Party, Party, 1920, 1080);
                Assert.Equal((100f, 50f), (party.X, party.Y));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void LoadOrDefault_MissingOrCorruptFile_ReturnsRetailLayout()
        {
            string path = Path.Combine(Path.GetTempPath(), $"gordian_ui_layout_{Guid.NewGuid():N}.json");
            Assert.Equal(1.0f, StockUiLayout.LoadOrDefault(path).Scale);
            File.WriteAllText(path, "{ not json");
            try
            {
                var layout = StockUiLayout.LoadOrDefault(path);
                Assert.Equal(1.0f, layout.Scale);
                Assert.Empty(layout.Windows);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
