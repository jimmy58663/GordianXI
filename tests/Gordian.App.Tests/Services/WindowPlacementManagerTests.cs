// tests/Gordian.App.Tests/Services/WindowPlacementManagerTests.cs
using System;
using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Gordian.App.Services;
using Xunit;

namespace Gordian.App.Tests.Services
{
    public sealed class WindowPlacementManagerTests : IDisposable
    {
        private readonly string _tempFilePath;

        public WindowPlacementManagerTests()
        {
            _tempFilePath = Path.Combine(Path.GetTempPath(), $"gordian_win_placement_test_{Guid.NewGuid():N}.json");
        }

        public void Dispose()
        {
            if (File.Exists(_tempFilePath))
            {
                try { File.Delete(_tempFilePath); } catch { }
            }
        }

        [Fact]
        public void SetAndGetPlacement_ReturnsExpectedCoordinatesAndState()
        {
            var manager = new WindowPlacementManager(_tempFilePath);
            var placement = new WindowPlacement
            {
                X = 250,
                Y = 150,
                Width = 1440,
                Height = 900,
                WindowState = WindowState.Normal
            };

            manager.SetPlacement("MainWindow", placement);
            var retrieved = manager.GetPlacement("MainWindow");

            Assert.NotNull(retrieved);
            Assert.Equal(250, retrieved.X);
            Assert.Equal(150, retrieved.Y);
            Assert.Equal(1440, retrieved.Width);
            Assert.Equal(900, retrieved.Height);
            Assert.Equal(WindowState.Normal, retrieved.WindowState);

            // Verify clone isolation: mutating retrieved does not alter store
            retrieved.X = 999;
            var retrievedAgain = manager.GetPlacement("MainWindow");
            Assert.NotNull(retrievedAgain);
            Assert.Equal(250, retrievedAgain.X);
        }

        [Fact]
        public void Persistence_SavesAndLoadsAcrossManagerInstances()
        {
            var manager1 = new WindowPlacementManager(_tempFilePath);
            manager1.SetPlacement("MainWindow", new WindowPlacement { X = 100, Y = 100, Width = 1280, Height = 840, WindowState = WindowState.Normal });
            manager1.SetPlacement("ChatWindow", new WindowPlacement { X = 500, Y = 600, Width = 720, Height = 500, WindowState = WindowState.Normal });
            manager1.SetPlacement("ViewportWindow", new WindowPlacement { X = 0, Y = 0, Width = 1920, Height = 1080, WindowState = WindowState.Maximized });
            manager1.SetPlacement("ViewportWindow_Cybin", new WindowPlacement { X = 1920, Y = 0, Width = 1600, Height = 900, WindowState = WindowState.Normal });

            // Force immediate synchronous write
            manager1.Save();
            Assert.True(File.Exists(_tempFilePath));

            // Create fresh instance loading from same file
            var manager2 = new WindowPlacementManager(_tempFilePath);

            var main = manager2.GetPlacement("MainWindow");
            Assert.NotNull(main);
            Assert.Equal(100, main.X);
            Assert.Equal(1280, main.Width);

            var chat = manager2.GetPlacement("ChatWindow");
            Assert.NotNull(chat);
            Assert.Equal(500, chat.X);
            Assert.Equal(600, chat.Y);

            var viewport = manager2.GetPlacement("ViewportWindow");
            Assert.NotNull(viewport);
            Assert.Equal(WindowState.Maximized, viewport.WindowState);

            var cybinViewport = manager2.GetPlacement("ViewportWindow_Cybin");
            Assert.NotNull(cybinViewport);
            Assert.Equal(1920, cybinViewport.X);
            Assert.Equal(1600, cybinViewport.Width);
        }

        [Fact]
        public void IsPlacementOnScreen_SingleMonitor_ValidatesCorrectly()
        {
            var screens = new List<PixelRect>
            {
                new PixelRect(0, 0, 1920, 1080)
            };

            var onScreenPlacement = new WindowPlacement { X = 100, Y = 100, Width = 1280, Height = 720 };
            Assert.True(WindowPlacementManager.IsPlacementOnScreen(onScreenPlacement, screens));

            var offScreenPlacement = new WindowPlacement { X = 3000, Y = 2000, Width = 1280, Height = 720 };
            Assert.False(WindowPlacementManager.IsPlacementOnScreen(offScreenPlacement, screens));
        }

        [Fact]
        public void IsPlacementOnScreen_DualMonitor_ValidatesBothMonitors()
        {
            var screens = new List<PixelRect>
            {
                new PixelRect(0, 0, 1920, 1080),        // Primary
                new PixelRect(1920, 0, 1920, 1080)     // Secondary on the right
            };

            var monitor1Placement = new WindowPlacement { X = 50, Y = 50, Width = 1280, Height = 720 };
            Assert.True(WindowPlacementManager.IsPlacementOnScreen(monitor1Placement, screens));

            var monitor2Placement = new WindowPlacement { X = 2000, Y = 50, Width = 1280, Height = 720 };
            Assert.True(WindowPlacementManager.IsPlacementOnScreen(monitor2Placement, screens));

            // Window saved on disconnected 3rd monitor
            var disconnectedMonitorPlacement = new WindowPlacement { X = 4000, Y = 50, Width = 1280, Height = 720 };
            Assert.False(WindowPlacementManager.IsPlacementOnScreen(disconnectedMonitorPlacement, screens));
        }

        [Fact]
        public void IsPlacementOnScreen_NegativeCoordinates_ValidatesLeftMonitor()
        {
            var screens = new List<PixelRect>
            {
                new PixelRect(-1920, 0, 1920, 1080),   // Secondary on the left
                new PixelRect(0, 0, 1920, 1080)         // Primary
            };

            var leftMonitorPlacement = new WindowPlacement { X = -1600, Y = 100, Width = 1280, Height = 720 };
            Assert.True(WindowPlacementManager.IsPlacementOnScreen(leftMonitorPlacement, screens));

            var farLeftOffScreen = new WindowPlacement { X = -3500, Y = 100, Width = 1280, Height = 720 };
            Assert.False(WindowPlacementManager.IsPlacementOnScreen(farLeftOffScreen, screens));
        }

        [Fact]
        public void IsPlacementOnScreen_EdgeOverlap_RequiresMinimumVisibleArea()
        {
            var screens = new List<PixelRect>
            {
                new PixelRect(0, 0, 1920, 1080)
            };

            // 60px horizontal, 40px vertical overlap -> meets 50x30 threshold
            var barelyOnScreen = new WindowPlacement { X = 1860, Y = 1040, Width = 500, Height = 300 };
            Assert.True(WindowPlacementManager.IsPlacementOnScreen(barelyOnScreen, screens));

            // Only 10px horizontal overlap -> below 50px threshold
            var barelyOffScreen = new WindowPlacement { X = 1910, Y = 500, Width = 500, Height = 300 };
            Assert.False(WindowPlacementManager.IsPlacementOnScreen(barelyOffScreen, screens));
        }

        [Fact]
        public void IsPlacementOnScreen_WhenScreensEmptyOrNull_ReturnsTrueFallback()
        {
            var placement = new WindowPlacement { X = 500, Y = 500, Width = 800, Height = 600 };
            Assert.True(WindowPlacementManager.IsPlacementOnScreen(placement, null));
            Assert.True(WindowPlacementManager.IsPlacementOnScreen(placement, new List<PixelRect>()));
        }

        [Fact]
        public void GetDefaultFilePath_ReturnsExpectedJsonFileName()
        {
            string path = WindowPlacementManager.GetDefaultFilePath();
            Assert.False(string.IsNullOrWhiteSpace(path));
            Assert.EndsWith("window_placements.json", path);
        }
    }
}
