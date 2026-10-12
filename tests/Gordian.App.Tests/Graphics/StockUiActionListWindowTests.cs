// tests/Gordian.App.Tests/Graphics/StockUiActionListWindowTests.cs
using System.Collections.Generic;
using System.Diagnostics;
using Gordian.App.Graphics;
using Gordian.Core.Actions;
using Gordian.Core.Ui;
using Xunit;

namespace Gordian.App.Tests.Graphics
{
    /// <summary>
    /// The spell list and its info window (#240) rendered from the retail <c>magic</c>, <c>subwindo</c> and <c>magiconw</c>
    /// resources on D3D11, laid out as the maintainer's retail captures of 2026-10-11 (GORDIAN_UI_DUMP writes the frame).
    /// </summary>
    public class StockUiActionListWindowTests
    {
        [Fact]
        public void RendersSpellListWithIconsGreyedRowsScrollbarAndInfoWindow()
        {
            using var screen = StockUiOffscreen.TryCreate();
            if (screen == null) return;

            long recastEnds = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 30;
            // Songs with their element notes (icons 32-39), as the capture's Bard list.
            var rows = new List<StockUiActionRow>
            {
                new(StockUiActionType.Spell, 405, "Fowl Aubade", ActionTargetFlags.Self, 38, 0, 0, null),
                new(StockUiActionType.Spell, 406, "Herb Pastoral", ActionTargetFlags.Self, 36, 0, recastEnds, null),
                new(StockUiActionType.Spell, 375, "Foe Requiem VIII", ActionTargetFlags.Enemy, 38, 0, 0, "You cannot cast Foe Requiem VIII on your current job and level."),
            };
            for (int i = 0; i < 12; i++) rows.Add(new(StockUiActionType.Spell, (ushort)(410 + i), $"Song {i}", ActionTargetFlags.Self, 32 + i % 8, 0, 0, null));
            var menus = new StockUiMenuController
            {
                Library = screen.Library,
                ActionRowsProvider = (_, _, _) => rows,
            };
            var list = menus.OpenActionList(StockUiActionList.Spells, 5, null)!;
            Assert.True(list.CanScroll);
            var frame = list.Menu.Frame;
            var at = StockUiLayout.Place(frame.Anchor, frame.X, frame.Y, frame.Width, frame.Height, 1, screen.Width, screen.Height);
            var anchor = (screen.Width - 128f, screen.Height - 100f);
            var shot = screen.Render(r =>
            {
                StockUiMenuWindow.Draw(r, screen.Library, screen.Font, list, at, Stopwatch.GetTimestamp(), logFont: screen.LogFont);
                StockUiMenuWindow.DrawActionInfo(r, screen.Library, screen.Font, list, anchor, 1, Stopwatch.GetTimestamp(), 1000);
            }, "gpu_magic_list.png");

            int px = (int)at.X, py = (int)at.Y;
            // Names 35 px in (19 past the row button at 16), on the fourth row (y 4 + 48; the first is selected, pink).
            Assert.True(screen.HasLightPixel(shot, px + 35, px + 120, py + 52 + 8), "no spell name on the fourth row");
            // The note icon in the row's first 16 px: coloured pixels.
            bool icon = false;
            for (int x = px + 16; x < px + 32 && !icon; x++)
            {
                for (int y = py + 52; y < py + 68 && !icon; y++)
                {
                    var p = screen.Pixel(shot, x, y);
                    icon = System.Math.Max(p.R, System.Math.Max(p.G, p.B)) - System.Math.Min(p.R, System.Math.Min(p.G, p.B)) > 50;
                }
            }
            Assert.True(icon, "no icon on the fourth row");
            // The info window above the anchor: "MP Cost" and "Next" lines with light glyphs.
            int infoTop = (int)anchor.Item2 - 44;
            Assert.True(screen.HasLightPixel(shot, (int)anchor.Item1 + 7, (int)anchor.Item1 + 60, infoTop + 12), "no MP Cost label");
            Assert.True(screen.HasLightPixel(shot, (int)anchor.Item1 + 7, (int)anchor.Item1 + 60, infoTop + 30), "no Next label");
        }

        [Fact]
        public void RendersPetRowAndBloodPactInfoWindow()
        {
            using var screen = StockUiOffscreen.TryCreate();
            if (screen == null) return;
            var rows = new List<StockUiActionRow>
            {
                new(StockUiActionType.JobAbility, 513, "Poison Nails", ActionTargetFlags.Enemy, 22, 11, 0, null),
            };
            var menus = new StockUiMenuController { Library = screen.Library, ActionRowsProvider = (_, _, _) => rows };
            var list = menus.OpenActionList(StockUiActionList.PetAbilities, 6, null)!;
            var anchor = (screen.Width - 128f, screen.Height - 100f);
            var shot = screen.Render(r =>
            {
                StockUiPartyWindow.DrawPetRow(r, screen.Font, "Garuda", 100, 300, 400, 1);
                StockUiMenuWindow.DrawActionInfo(r, screen.Library, screen.Font, list, anchor, 1, Stopwatch.GetTimestamp(), 0, ("Garuda", (byte)100));
            }, "gpu_pet_row.png");
            Assert.True(screen.HasLightPixel(shot, 300, 340, 405, 150), "no pet name");
            int infoTop = (int)anchor.Item2 - 44;
            Assert.True(screen.HasLightPixel(shot, (int)anchor.Item1 + 6, (int)anchor.Item1 + 50, infoTop + 8, 150), "no pet name in the info window");
            Assert.True(screen.HasLightPixel(shot, (int)anchor.Item1 + 7, (int)anchor.Item1 + 60, infoTop + 30), "no MP Cost label");
        }

        [Fact]
        public void RendersEmptyMountListText()
        {
            using var screen = StockUiOffscreen.TryCreate();
            if (screen == null) return;
            var menus = new StockUiMenuController { Library = screen.Library };
            var list = menus.OpenActionList(StockUiActionList.Mounts, 0, null)!;
            Assert.Equal("No mounts available.", list.EmptyText);
            var frame = list.Menu.Frame;
            var at = StockUiLayout.Place(frame.Anchor, frame.X, frame.Y, frame.Width, frame.Height, 1, screen.Width, screen.Height);
            var shot = screen.Render(r => StockUiMenuWindow.Draw(r, screen.Library, screen.Font, list, at, 0, logFont: screen.LogFont), "gpu_mount_list.png");
            Assert.True(screen.HasLightPixel(shot, (int)at.X + 11, (int)at.X + 120, (int)at.Y + 20 + 8), "no empty-list text");
        }
    }
}
