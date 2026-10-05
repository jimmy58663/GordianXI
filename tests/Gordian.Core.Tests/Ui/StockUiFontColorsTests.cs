// tests/Gordian.Core.Tests/Ui/StockUiFontColorsTests.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Gordian.Core.Input;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Ui;
using Gordian.Core.Ui;
using Xunit;

namespace Gordian.Core.Tests.Ui
{
    public class StockUiFontColorsTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";

        /// <summary>
        /// The colour table of a never-edited character's cnf.dat (the maintainer's USER/3..6, 2026-09): 23 entries
        /// at 0x50 and five later ones, each B, G, R, 0x80.
        /// </summary>
        internal static byte[] FreshCnf()
        {
            var cnf = new byte[744];
            byte[] table =
            {
                0x80, 0x80, 0x80, 0x80, 0x40, 0x50, 0xa0, 0x80, 0xa0, 0x40, 0xa0, 0x80, 0xa0, 0xc0, 0x20, 0x80,
                0x60, 0xff, 0x50, 0x80, 0xa0, 0x50, 0x60, 0x80, 0xd0, 0xd0, 0xa0, 0x80, 0x80, 0x80, 0x80, 0x80,
                0xc0, 0x90, 0x60, 0x80, 0x40, 0x40, 0xa0, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80,
                0x50, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0xf0, 0xc0, 0x90, 0x80, 0x80, 0x80, 0xc0, 0x80,
                0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x40, 0x80, 0xa0, 0x80, 0x70, 0x70, 0x70, 0x80,
                0x10, 0x80, 0x80, 0x80, 0xd0, 0x60, 0xc0, 0x80, 0x50, 0xc0, 0xc0, 0x80,
            };
            table.CopyTo(cnf, 0x50);
            new byte[] { 0x30, 0x40, 0xa0, 0x80 }.CopyTo(cnf, 0x22C);
            new byte[] { 0x3f, 0xaf, 0xff, 0x80 }.CopyTo(cnf, 0x26C);
            new byte[] { 0x00, 0xcc, 0x00, 0x80 }.CopyTo(cnf, 0x278);
            new byte[] { 0xff, 0x50, 0x00, 0x80 }.CopyTo(cnf, 0x2D8);
            new byte[] { 0xff, 0x70, 0x00, 0x80 }.CopyTo(cnf, 0x2DC);
            return cnf;
        }

        [Fact]
        public void Table_HasEveryRowOnce_AndTheCnfOffsetsAreDistinctEntries()
        {
            var ids = Enum.GetValues<StockUiFontColorId>();
            Assert.Equal(ids.Length, StockUiFontColors.Entries.Count);
            Assert.Equal(ids.Length, StockUiFontColors.Entries.Select(e => e.Id).Distinct().Count());
            Assert.Equal(ids.Length, StockUiFontColors.Entries.Select(e => e.CnfOffset).Distinct().Count());
            Assert.All(StockUiFontColors.Entries, e => Assert.Equal(0, e.CnfOffset % 4));
            // The 23-entry table holds 23 of the rows, the rest are the five later entries.
            Assert.Equal(StockUiFontColors.CnfTableEntries, StockUiFontColors.Entries.Count(e =>
                e.CnfOffset >= StockUiFontColors.CnfTableOffset && e.CnfOffset < StockUiFontColors.CnfTableOffset + 23 * 4));
            Assert.Equal(13, StockUiFontColors.InCategory(StockUiFontColorCategory.Chat).Count);
            Assert.Equal(6, StockUiFontColors.InCategory(StockUiFontColorCategory.ForSelf).Count);
            Assert.Equal(6, StockUiFontColors.InCategory(StockUiFontColorCategory.ForOthers).Count);
            Assert.Equal(3, StockUiFontColors.InCategory(StockUiFontColorCategory.System).Count);
        }

        [Fact]
        public void ReadCnf_ReadsBgrEntries_AndAFreshFileGivesTheDefaults()
        {
            var colors = StockUiFontColors.ReadCnf(FreshCnf());
            Assert.Equal(StockUiFontColors.Entries.Count, colors.Count);
            foreach (var e in StockUiFontColors.Entries) Assert.Equal(e.Default, colors[e.Id]);

            // B, G, R order: shout is red-heavy (peach), party blue-green (cyan), the tell pink.
            Assert.Equal(new StockUiRgb(0xA0, 0x50, 0x40), colors[StockUiFontColorId.Shout]);
            Assert.Equal(new StockUiRgb(0x20, 0xC0, 0xA0), colors[StockUiFontColorId.Party]);
            Assert.Equal(new StockUiRgb(0xA0, 0x40, 0xA0), colors[StockUiFontColorId.Tell]);

            // A short file gives what it has.
            Assert.Equal(StockUiFontColors.CnfTableEntries, StockUiFontColors.ReadCnf(FreshCnf().AsSpan(0, 0x100)).Count);
        }

        /// <summary>Against the maintainer's install: every never-edited character's cnf.dat carries the defaults.</summary>
        [Fact]
        public void RetailUserFolders_FreshCharactersCarryTheDefaults()
        {
            string user = Path.Combine(GameDirectory, "USER");
            if (!Directory.Exists(user)) return;
            int checkedFiles = 0;
            foreach (string folder in new[] { "3", "4", "5", "6" })
            {
                string path = Path.Combine(user, folder, "cnf.dat");
                if (!File.Exists(path)) continue;
                var colors = StockUiFontColors.ReadCnf(File.ReadAllBytes(path));
                foreach (var e in StockUiFontColors.Entries) Assert.Equal(e.Default, colors[e.Id]);
                checkedFiles++;
            }
            _ = checkedFiles;
        }

        [Fact]
        public void Settings_KeepColoursPerRow_PersistThem_AndResetToTheDefaults()
        {
            string path = Path.Combine(Path.GetTempPath(), "gordian_ui_colors_" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var settings = new StockUiSettings();
                var changed = new List<StockUiFontColorId?>();
                settings.FontColorsChanged += id => changed.Add(id);
                Assert.Equal(StockUiFontColors.Get(StockUiFontColorId.Say).Default, settings.GetFontColor(StockUiFontColorId.Say));
                Assert.True(settings.SetFontColor(StockUiFontColorId.Say, new StockUiRgb(0x80, 0, 0)));
                Assert.False(settings.SetFontColor(StockUiFontColorId.Say, new StockUiRgb(0x80, 0, 0)));
                Assert.Equal(new StockUiFontColorId?[] { StockUiFontColorId.Say }, changed);
                settings.SetValue(StockUiSettingKey.FontColorEditRed, 12); // transient: not saved
                settings.SaveToFile(path);

                string json = File.ReadAllText(path);
                Assert.Contains("\"Say\": \"800000\"", json);
                Assert.DoesNotContain("FontColorEdit", json);

                var loaded = StockUiSettings.LoadOrDefault(path);
                Assert.Equal(new StockUiRgb(0x80, 0, 0), loaded.GetFontColor(StockUiFontColorId.Say));
                Assert.True(loaded.HasFontColor(StockUiFontColorId.Say));
                Assert.False(loaded.HasFontColor(StockUiFontColorId.Tell));

                loaded.ResetFontColors();
                Assert.Equal(StockUiFontColors.Get(StockUiFontColorId.Say).Default, loaded.GetFontColor(StockUiFontColorId.Say));
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        private static UiMenuDefinition Menu(string name, short x, short y, UiAnchor anchor, short width, short height, IEnumerable<UiMenuButton> buttons) => new()
        {
            Name = name,
            Frame = new UiMenuFrame { X = x, Y = y, Width = width, Height = height, Anchor = anchor },
            Buttons = buttons.ToList(),
        };

        private static UiMenuButton Button(int id, short x, short y, short w, short h, int up, int down, int left, int right) => new()
        {
            ButtonId = (short)id, X = x, Y = y, Width = w, Height = h,
            NavUp = (sbyte)up, NavDown = (sbyte)down, NavLeft = (sbyte)left, NavRight = (sbyte)right,
        };

        /// <summary>The three Font Colors windows as the English menu DAT authors them (ROM/119/51).</summary>
        internal static IEnumerable<UiMenuDefinition> FontColorMenus()
        {
            yield return Menu(StockUiConfigPages.FontColorCategoryMenu, 384, 48, UiAnchor.TopRight, 112, 90,
                Enumerable.Range(1, 5).Select(i => Button(i, 16, (short)(6 + 16 * (i - 1)), 88, 16, i == 1 ? 5 : i - 1, i == 5 ? 1 : i + 1, i, i)));
            yield return Menu(StockUiConfigPages.FontColorListMenu, 16, 106, UiAnchor.TopLeft, 366, 206,
                Enumerable.Range(1, 11).Select(i => Button(i, 0, (short)(5 + 18 * (i - 1)), 356, 16, i == 1 ? 11 : i - 1, i == 11 ? 1 : i + 1, i, i)));
            yield return Menu(StockUiConfigPages.FontColorEditPage, 16, 48, UiAnchor.TopLeft, 366, 56, new[]
            {
                Button(4, 292, 11, 34, 34, 4, 4, 2, 5),
                Button(5, 328, 11, 34, 34, 5, 5, 4, 2),
                Button(1, 210, 14, 82, 11, 3, 2, 5, 4),
                Button(2, 210, 24, 82, 12, 1, 3, 5, 4),
                Button(3, 210, 34, 82, 11, 2, 1, 5, 4),
            });
            yield return Menu(StockUiMenuEntries.YesNoMenu, 16, 256, UiAnchor.BottomLeft, 112, 42, new[]
            {
                Button(1, 16, 6, 88, 16, 2, 2, 1, 1),
                Button(2, 16, 22, 88, 16, 1, 1, 2, 2),
            });
        }

        [Fact]
        public void FontColorsPage_ListsACategory_EditsARowWithTheSliders_AndOkKeepsIt()
        {
            var menus = new StockUiMenuController { Library = UiResourceLibrary.FromDefinitions(FontColorMenus()) };
            Assert.True(menus.Open(StockUiConfigPages.FontColorCategoryMenu));
            menus.Move(InputAction.MenuDown);             // For Self
            menus.Activate();
            var list = menus.Top!;
            Assert.Equal(StockUiListKind.FontColors, list.ListKind);
            Assert.Equal(StockUiFontColorCategory.ForSelf, list.FontColorCategory);
            Assert.Equal(6, list.Rows.Count);
            Assert.Equal("HP/MP you recover", list.Rows[0].Text);                 // the message type, white
            Assert.Null(list.Rows[0].Color);
            Assert.Equal("Player recovers 10 HP.", list.SelectedFontColorSample!.Value.Text);
            Assert.False(list.CanScroll);

            menus.Move(InputAction.MenuDown);
            menus.Move(InputAction.MenuDown);             // Beneficial effects you are granted (white, 0x80)
            menus.Activate();
            var editor = menus.Top!;
            Assert.Equal(StockUiConfigPages.FontColorEditPage, editor.Name);
            Assert.Equal(StockUiFontColorId.SelfBeneficial, menus.FontColorEditing!.Id);
            Assert.Equal("Player gains beneficial effect.", editor.SampleText);
            Assert.Equal(0x80 / 255f, editor.SliderFractions[1], 3);
            Assert.Equal(1, editor.SelectedButtonId);

            menus.Move(InputAction.MenuLeft);             // R down a step
            menus.Move(InputAction.MenuDown);             // G
            for (int i = 0; i < 40; i++) menus.Move(InputAction.MenuRight); // G to the top
            Assert.Equal(new UiColor(0x80 - StockUiSettings.FontColorStep, 0xFF, 0x80, 0x80), editor.SampleColor);
            // The row keeps its colour until OK.
            Assert.Equal(StockUiFontColors.Get(StockUiFontColorId.SelfBeneficial).Default, menus.Settings.GetFontColor(StockUiFontColorId.SelfBeneficial));

            menus.Activate();                             // Confirm on a bar = OK
            Assert.Same(list, menus.Top);
            Assert.Null(menus.FontColorEditing);
            var set = new StockUiRgb(0x80 - StockUiSettings.FontColorStep, 0xFF, 0x80);
            Assert.Equal(set, menus.Settings.GetFontColor(StockUiFontColorId.SelfBeneficial));
            Assert.Equal(set.ToUiColor(), list.FontColorSamples[2].Color);

            // Cancel leaves the colour alone.
            menus.Activate();
            menus.Move(InputAction.MenuRight);
            menus.CloseTop();
            Assert.Equal(set, menus.Settings.GetFontColor(StockUiFontColorId.SelfBeneficial));

            // Another edit, kept with Confirm.
            menus.Activate();
            menus.Move(InputAction.MenuLeft);
            menus.Activate();
            Assert.Equal(set with { R = (byte)(set.R - StockUiSettings.FontColorStep) }, menus.Settings.GetFontColor(StockUiFontColorId.SelfBeneficial));
        }

        [Fact]
        public void FontColorsPage_TheChatListScrollsPastItsElevenRows()
        {
            var menus = new StockUiMenuController { Library = UiResourceLibrary.FromDefinitions(FontColorMenus()) };
            Assert.True(menus.Open(StockUiConfigPages.FontColorCategoryMenu));
            menus.Activate();                             // Chat
            var list = menus.Top!;
            Assert.Equal(13, list.Rows.Count);
            Assert.True(list.CanScroll);
            for (int i = 0; i < 10; i++) menus.Move(InputAction.MenuDown);
            Assert.Equal(11, list.SelectedButtonId);
            Assert.Equal(0, list.FirstRow);
            menus.Move(InputAction.MenuDown);             // the DAT row wraps to 1; the list scrolls instead
            Assert.Equal(1, list.FirstRow);
            Assert.Equal(11, list.SelectedButtonId);
            menus.Move(InputAction.MenuDown);
            Assert.Equal(2, list.FirstRow);
            menus.Move(InputAction.MenuDown);             // past the end: back to the top
            Assert.Equal(0, list.FirstRow);
            Assert.Equal(1, list.SelectedButtonId);
            menus.Move(InputAction.MenuUp);               // and up from the top to the end
            Assert.Equal(2, list.FirstRow);
            Assert.Equal(11, list.SelectedButtonId);
            Assert.Equal(12, list.EntryIndex(list.SelectedButtonId));
        }

        [Fact]
        public async Task FontColorsPage_DefaultAsksThenResetsEveryRow()
        {
            var menus = new StockUiMenuController { Library = UiResourceLibrary.FromDefinitions(FontColorMenus()) };
            menus.Settings.SetFontColor(StockUiFontColorId.Tell, new StockUiRgb(1, 2, 3));
            Assert.True(menus.Open(StockUiConfigPages.FontColorCategoryMenu));
            for (int i = 0; i < 4; i++) menus.Move(InputAction.MenuDown);
            Assert.Equal(StockUiConfigPages.FontColorDefaultButton, menus.Top!.SelectedButtonId);
            menus.Activate();
            Assert.Equal(StockUiMenuEntries.YesNoMenu, menus.Top!.Name);
            Assert.Equal(2, menus.Top.SelectedButtonId); // No by default
            menus.Move(InputAction.MenuUp);
            menus.Activate();
            await Task.Delay(50);
            Assert.False(menus.Settings.HasFontColor(StockUiFontColorId.Tell));
        }

        [Fact]
        public void Lists_FollowRetailsOrder_AndTheFirstRoundsColours()
        {
            // Retail's Chat list (the maintainer's screenshots, 2026-10-04): Shout and Yell come last.
            Assert.Equal(new[]
            {
                StockUiFontColorId.Say, StockUiFontColorId.Tell, StockUiFontColorId.Party, StockUiFontColorId.Linkshell,
                StockUiFontColorId.Linkshell2, StockUiFontColorId.AssistJ, StockUiFontColorId.AssistE, StockUiFontColorId.Unity,
                StockUiFontColorId.Emote, StockUiFontColorId.Message, StockUiFontColorId.Npc, StockUiFontColorId.Shout, StockUiFontColorId.Yell,
            }, StockUiFontColors.InCategory(StockUiFontColorCategory.Chat).Select(e => e.Id));
            Assert.Equal(new[] { "Standard battle messages", "Calls for help", "Basic system messages" },
                StockUiFontColors.InCategory(StockUiFontColorCategory.System).Select(e => e.Label));
            // The settled diff: Say is entry 0 (0x50). Rows checked against the retail editor (2026-10-05).
            Assert.Equal(StockUiFontColors.CnfTableOffset, StockUiFontColors.Get(StockUiFontColorId.Say).CnfOffset);
            Assert.Equal(new StockUiRgb(0x00, 0xCC, 0x00), StockUiFontColors.Get(StockUiFontColorId.Linkshell2).Default);
            Assert.Equal(new StockUiRgb(0xA0, 0xD0, 0xD0), StockUiFontColors.Get(StockUiFontColorId.Message).Default);
            Assert.Equal(new StockUiRgb(0x60, 0x90, 0xC0), StockUiFontColors.Get(StockUiFontColorId.SelfRecover).Default);
            Assert.Equal(new StockUiRgb(0xC0, 0x60, 0xD0), StockUiFontColors.Get(StockUiFontColorId.CallForHelp).Default);
            Assert.Equal(new StockUiRgb(0xC0, 0xC0, 0x50), StockUiFontColors.Get(StockUiFontColorId.BasicSystem).Default);
            // NPC text draws as Say; emotes purple.
            Assert.Equal(StockUiFontColors.Get(StockUiFontColorId.Say).Default, StockUiFontColors.Get(StockUiFontColorId.Npc).Default);
            var emote = StockUiFontColors.Get(StockUiFontColorId.Emote).Default;
            Assert.True(emote.B > emote.G && emote.R > emote.G, emote.ToString());
        }

        [Fact]
        public void Texts_ComeFromTheConfigRowTable_WhenItIsRead()
        {
            var menus = new StockUiMenuController
            {
                Library = UiResourceLibrary.FromDefinitions(FontColorMenus()),
                ConfigRowText = i => i == 36 ? "Table say" : i == 63 ? "Table say sample" : null,
            };
            Assert.True(menus.Open(StockUiConfigPages.FontColorCategoryMenu));
            menus.Activate();
            Assert.Equal("Table say", menus.Top!.Rows[0].Text);
            Assert.Equal("Table say sample", menus.Top.SelectedFontColorSample!.Value.Text);
            Assert.Equal("Tell target only (\"Tell\")", menus.Top.Rows[1].Text); // fallback
        }

        /// <summary>Against the retail table: every row's label and sample index holds the fallback text.</summary>
        [Fact]
        public void RetailConfigRowTable_HoldsEveryRowsText()
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            foreach (var e in StockUiFontColors.Entries)
            {
                Assert.True(rm.TryGetString(Gordian.Core.Resources.Models.DMsgCategory.MenuConfigRows, e.LabelIndex, out var label));
                Assert.Equal(e.Label, label.Trim());
                Assert.True(rm.TryGetString(Gordian.Core.Resources.Models.DMsgCategory.MenuConfigRows, e.SampleIndex, out var sample));
                Assert.Equal(e.Sample, sample.Trim());
            }
        }

        [Fact]
        public void LogLines_TakeTheirChannelsRow_AndCombatLinesTheirEffectsRow()
        {
            Assert.Equal(StockUiFontColorId.Tell, new ChatLogLine(ChatLogChannel.Tell, "x", DateTime.Now).FontColor);
            Assert.Equal(StockUiFontColorId.Npc, new ChatLogLine(ChatLogChannel.Dialog, "x", DateTime.Now).FontColor);
            Assert.Null(new ChatLogLine(ChatLogChannel.System, "x", DateTime.Now).FontColor);
            Assert.Null(new ChatLogLine(ChatLogChannel.ServerMessage, "x", DateTime.Now).FontColor);

            const uint me = 0x1001, mob = 0x01000F00;
            var miss = new CombatLogLine("The Rabbit misses Me.", mob, me, 15, ActionCategory.BasicAttack, ActionResolution.Miss, CombatLogLinePart.Primary);
            var line = StockUiCombatLog.LineFor(miss, me, DateTime.Now);
            Assert.Equal(StockUiFontColorId.SelfMiss, line.FontColor);
            Assert.Equal(ChatLogType.SelfEvade, line.Type);

            var hit = new CombatLogLine("Me hits the Rabbit for 5 points of damage.", me, mob, 1, ActionCategory.BasicAttack, ActionResolution.Hit, CombatLogLinePart.Primary);
            line = StockUiCombatLog.LineFor(hit, me, DateTime.Now);
            Assert.Equal(StockUiFontColorId.OthersDamage, line.FontColor);
            Assert.Equal(ChatLogType.OthersLose, line.Type);

            var cast = StockUiCombatLog.LineFor("Me starts casting Cure.", 3, me, me, DateTime.Now);
            Assert.Equal(StockUiFontColorId.StandardBattle, cast.FontColor);
            Assert.Equal(ChatLogType.StandardBattle, cast.Type);
            var check = StockUiCombatLog.LineFor("The Rabbit seems to be level 1.", 174, mob, me, DateTime.Now);
            Assert.Equal(StockUiFontColorId.StandardBattle, check.FontColor);
            Assert.Equal(ChatLogType.StandardBattle, check.Type);
            var help = StockUiCombatLog.LineFor("Me calls for help!", 19, 0, me, DateTime.Now);
            Assert.Equal(StockUiFontColorId.CallForHelp, help.FontColor);
            Assert.Equal(ChatLogType.CallsForHelp, help.Type);

            // A reaction (spikes) is about the actor it hits.
            var record = new CombatActionRecord
            {
                ActorId = mob,
                Category = ActionCategory.BasicAttack,
                Targets = { new CombatActionTargetRecord { TargetId = me, Results = { new CombatActionResult { Param = 3, MessageId = 1, HasReaction = true, ReactionKind = ActionReactKind.Counter, ReactionParam = 4 } } } },
            };
            var lines = CombatLogFormatter.FormatActionLines(record, _ => "X");
            Assert.Equal(2, lines.Count);
            Assert.Equal(StockUiFontColorId.SelfDamage, StockUiCombatLog.LineFor(lines[0], me, DateTime.Now).FontColor);
            Assert.Equal(StockUiFontColorId.OthersDamage, StockUiCombatLog.LineFor(lines[1], me, DateTime.Now).FontColor);
            Assert.Equal(CombatLogFormatter.FormatAction(record, _ => "X"), lines.Select(l => l.Text));

            // A weapon skill's skillchain (its added effect) is a standard battle message; search replies are system text.
            var chain = new CombatLogLine("Skillchain: Light.", me, mob, 288, ActionCategory.SkillFinish, ActionResolution.Hit, CombatLogLinePart.AddedEffect);
            Assert.Equal(StockUiFontColorId.StandardBattle, StockUiCombatLog.LineFor(chain, me, DateTime.Now).FontColor);
            Assert.True(StockUiChat.IsSystemReply(Gordian.Core.Network.ChatCommandResultKind.PlayerSearch));
            Assert.False(StockUiChat.IsSystemReply(Gordian.Core.Network.ChatCommandResultKind.UiLayout));
        }
    }
}
