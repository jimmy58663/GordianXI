// tests/Gordian.Core.Tests/Ui/StockUiConfigPagesTests.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Gordian.Core.Actions;
using Gordian.Core.Config;
using Gordian.Core.Input;
using Gordian.Core.Network;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources.Ui;
using Gordian.Core.Ui;
using Gordian.Core.World;
using Xunit;

namespace Gordian.Core.Tests.Ui
{
    public class StockUiConfigPagesTests
    {
        /// <summary>A page whose buttons link up/down in a ring and left/right to themselves (as the DAT option rows do).</summary>
        private static UiMenuDefinition Page(string name, params int[] buttonIds)
        {
            var buttons = new List<UiMenuButton>();
            for (int i = 0; i < buttonIds.Length; i++)
            {
                int id = buttonIds[i];
                int up = buttonIds[(i - 1 + buttonIds.Length) % buttonIds.Length];
                int down = buttonIds[(i + 1) % buttonIds.Length];
                buttons.Add(new UiMenuButton
                {
                    ButtonId = (short)id, X = 78, Y = (short)(62 + 32 * i), Width = 88, Height = 16,
                    NavUp = (sbyte)up, NavDown = (sbyte)down, NavLeft = (sbyte)id, NavRight = (sbyte)id,
                });
            }
            return new UiMenuDefinition
            {
                Name = name,
                Frame = new UiMenuFrame { X = 16, Y = 48, Width = 366, Height = 248, Anchor = UiAnchor.TopLeft },
                Buttons = buttons,
            };
        }

        /// <summary>The Chat Filters list as the DAT links it: 14 rows, the first linking up to 16 and the last down to 26.</summary>
        private static UiMenuDefinition FilterList()
        {
            var buttons = new List<UiMenuButton>();
            for (int row = 1; row <= 14; row++)
            {
                buttons.Add(new UiMenuButton
                {
                    ButtonId = (short)row, X = 34, Y = (short)(21 + 16 * (row - 1)), Width = 256, Height = 16,
                    NavUp = (sbyte)(row == 1 ? 16 : row - 1), NavDown = (sbyte)(row == 14 ? 26 : row + 1),
                    NavLeft = (sbyte)(row == 1 ? 16 : row - 1), NavRight = (sbyte)(row == 14 ? 26 : row + 1),
                });
            }
            return new UiMenuDefinition
            {
                Name = StockUiConfigPages.ChatFiltersPage,
                Frame = new UiMenuFrame { X = 16, Y = 48, Width = 366, Height = 248, Anchor = UiAnchor.TopLeft },
                Buttons = buttons,
            };
        }

        private static StockUiMenuController Controller() => new()
        {
            Library = UiResourceLibrary.FromDefinitions(new[]
            {
                Page(StockUiConfigPages.GameplayPage, 1, 2, 3, 4),
                Page(StockUiConfigPages.GlobalPage, 1, 2, 3, 9),
                FilterList(),
            }),
        };

        [Fact]
        public void EveryPageRowMapsToADefinedSetting_AndChoicesAreDistinct()
        {
            foreach (var page in StockUiConfigPages.All)
            {
                var buttons = new HashSet<int>();
                foreach (var row in page.Rows)
                {
                    Assert.True(StockUiSettings.Definitions.ContainsKey(row.Key), $"{page.Menu} {row.Label}");
                    switch (row)
                    {
                        case StockUiOptionRow option:
                            var values = new HashSet<int>();
                            foreach (var choice in option.Choices)
                            {
                                Assert.True(buttons.Add(choice.ButtonId), $"{page.Menu} button {choice.ButtonId} used twice");
                                Assert.True(values.Add(choice.Value), $"{page.Menu} {row.Label} value {choice.Value} twice");
                                Assert.InRange(choice.Value, row.Definition.Min, row.Definition.Max);
                            }
                            Assert.NotEqual(0, option.ButtonFor(row.Definition.Default));
                            break;
                        case StockUiSliderRow slider:
                            Assert.True(buttons.Add(slider.ButtonId), $"{page.Menu} button {slider.ButtonId} used twice");
                            break;
                    }
                }
            }
            Assert.Equal(StockUiConfigPages.ChatFilters.Count, StockUiConfigPages.ChatFilters.Select(f => (f.Word, f.Bit)).Distinct().Count());
            Assert.Equal(55, StockUiConfigPages.ChatFilters.Count); // retail's list, four captured screens
        }

        [Fact]
        public void OptionRow_ConfirmSelectsTheChoice_MarksIt_AndRaisesChanged()
        {
            var menus = Controller();
            var changes = new List<(StockUiSettingKey Key, int Value)>();
            menus.Settings.Changed += (k, v) => changes.Add((k, v));
            Assert.True(menus.Open(StockUiConfigPages.GameplayPage));

            var page = menus.Top!;
            Assert.True(page.IsMarked(1));             // Auto-target ON is the default
            Assert.False(page.IsMarked(2));
            menus.Move(InputAction.MenuDown);          // 2 = OFF
            menus.Activate();
            Assert.Equal(0, menus.Settings.GetValue(StockUiSettingKey.AutoTarget));
            Assert.True(page.IsMarked(2));
            Assert.False(page.IsMarked(1));
            Assert.Equal((StockUiSettingKey.AutoTarget, 0), Assert.Single(changes));

            // The Global page's auto-disconnect row carries values, not indices: button 9 is OFF (0).
            Assert.True(menus.Open(StockUiConfigPages.GlobalPage));
            Assert.True(menus.Top!.IsMarked(9));
            menus.Move(InputAction.MenuDown);
            menus.Move(InputAction.MenuDown);          // 3 = 10 minutes
            menus.Activate();
            Assert.Equal(10, menus.Settings.GetValue(StockUiSettingKey.AutoDisconnectMinutes));
            Assert.True(menus.Top.IsMarked(3));
        }

        [Fact]
        public void SliderRow_LeftRightMoveByTheStep_ClampToTheRange_AndFillFollows()
        {
            var menus = Controller();
            Assert.True(menus.Open(StockUiConfigPages.GameplayPage));
            var page = menus.Top!;
            menus.Move(InputAction.MenuDown);
            menus.Move(InputAction.MenuDown);          // 3 = Sound Effects Volume (default 100)
            Assert.Equal(3, page.SelectedButtonId);
            Assert.Equal(1f, page.SliderFractions[3]);

            menus.Move(InputAction.MenuLeft);
            Assert.Equal(95, menus.Settings.GetValue(StockUiSettingKey.SoundEffectsVolume));
            Assert.Equal(0.95f, page.SliderFractions[3], 3);
            Assert.Equal(3, page.SelectedButtonId);    // the cursor stays on the bar

            for (int i = 0; i < 30; i++) menus.Move(InputAction.MenuLeft);
            Assert.Equal(0, menus.Settings.GetValue(StockUiSettingKey.SoundEffectsVolume));
            Assert.Equal(0f, page.SliderFractions[3]);
            menus.Move(InputAction.MenuRight);
            Assert.Equal(5, menus.Settings.GetValue(StockUiSettingKey.SoundEffectsVolume));

            menus.Activate();                          // confirm does nothing on a slider
            Assert.Equal(StockUiConfigPages.GameplayPage, menus.Top!.Name);
            Assert.Equal(5, menus.Settings.GetValue(StockUiSettingKey.SoundEffectsVolume));
        }

        [Fact]
        public void ChatFilters_RowsToggleBits_ScrollPastTheEdges_AndTheWordsAreSent()
        {
            var menus = Controller();
            var sent = new List<(uint, uint)>();
            menus.Settings.ChatFiltersChanged += (a, b) => sent.Add((a, b));
            Assert.True(menus.Open(StockUiConfigPages.ChatFiltersPage));
            var list = menus.Top!;
            int total = StockUiConfigPages.ChatFilters.Count;
            Assert.Equal(total, list.Rows.Count);
            Assert.Equal(14, list.VisibleRows);
            Assert.True(list.CanScroll);
            Assert.Equal("Say", list.Rows[0].Text);
            Assert.False(list.Rows[0].Marked);

            menus.Activate();                          // filter Say
            Assert.Equal((uint)ChatFilter1.Say, menus.Settings.MessageFilter1);
            Assert.True(list.Rows[0].Marked);
            Assert.Equal(((uint)ChatFilter1.Say, 0u), Assert.Single(sent));

            // Tell has no server bit: it toggles the client-only mask (persisted) and sends nothing.
            menus.Move(InputAction.MenuDown);
            Assert.Equal("Tell", list.Rows[1].Text);
            menus.Activate();
            Assert.True(list.Rows[1].Marked);
            Assert.Equal((int)StockUiConfigPages.ClientChatFilterTell, menus.Settings.GetValue(StockUiSettingKey.ClientChatFilters));
            Assert.Single(sent);
            menus.Activate();
            Assert.False(list.Rows[1].Marked);
            Assert.Equal(0, menus.Settings.GetValue(StockUiSettingKey.ClientChatFilters));
            menus.Move(InputAction.MenuUp);

            for (int i = 0; i < 13; i++) menus.Move(InputAction.MenuDown);
            Assert.Equal(14, list.SelectedButtonId);
            Assert.Equal(0, list.FirstRow);
            menus.Move(InputAction.MenuDown);          // past the last visible row: the list scrolls one entry, the cursor stays on row 14
            Assert.Equal(1, list.FirstRow);
            Assert.Equal(0f, list.ScrollFrom);         // animated from the previous position
            Assert.Equal(14, list.SelectedButtonId);
            Assert.Equal(StockUiConfigPages.ChatFilters[14].Label, list.Rows[list.EntryIndex(14)].Text);
            for (int i = 0; i < 13; i++) menus.Move(InputAction.MenuUp);
            Assert.Equal(1, list.SelectedButtonId);
            Assert.Equal(1, list.FirstRow);
            menus.Move(InputAction.MenuUp);            // scrolls back up
            Assert.Equal(0, list.FirstRow);
            Assert.Equal(1, list.SelectedButtonId);
            menus.Move(InputAction.MenuUp);            // nothing above: wraps to the end (a jump, not a slide)
            Assert.Equal(total - 14, list.FirstRow);
            Assert.Equal((float)(total - 14), list.ScrollFrom);
            Assert.Equal(14, list.SelectedButtonId);
            Assert.Equal(total - 1, list.EntryIndex(14));
            menus.Move(InputAction.MenuDown);          // and forward past the end wraps to the top
            Assert.Equal(0, list.FirstRow);
            Assert.Equal(1, list.SelectedButtonId);

            // The three "System Lv." rows stack on the server's 2-bit level, sent in the flag word.
            menus.Move(InputAction.MenuUp);
            Assert.Equal("System Lv. 3 (Countdowns)", list.Rows[list.EntryIndex(14)].Text);
            menus.Activate();
            Assert.Equal(3, menus.Settings.GetValue(StockUiSettingKey.SystemMessageFilterLevel));
            Assert.True(list.Rows[total - 1].Marked);
            Assert.True(list.Rows[total - 2].Marked);
            Assert.True(list.Rows[total - 3].Marked);
            menus.Move(InputAction.MenuUp);            // Lv. 2
            menus.Activate();                          // off: the level drops to 1
            Assert.Equal(1, menus.Settings.GetValue(StockUiSettingKey.SystemMessageFilterLevel));
            Assert.False(list.Rows[total - 1].Marked);
            Assert.False(list.Rows[total - 2].Marked);
            Assert.True(list.Rows[total - 3].Marked);
            Assert.Single(sent);

            menus.CloseTop();
            Assert.True(menus.Open(StockUiConfigPages.ChatFiltersPage)); // the scroll position and row are remembered
            Assert.Equal(total - 14, menus.Top!.FirstRow);
            Assert.Equal(13, menus.Top.SelectedButtonId);
            menus.Move(InputAction.MenuDown);
            menus.Move(InputAction.MenuDown);          // wrap to the top: Say
            Assert.Equal(0, menus.Top.FirstRow);
            Assert.Equal(1, menus.Top.SelectedButtonId);
            menus.Activate();                          // un-filter Say
            Assert.Equal(0u, menus.Settings.MessageFilter1);
            Assert.False(menus.Top.Rows[0].Marked);
        }

        [Fact]
        public void ServerValues_RefreshTheMarkers_WithoutBeingEchoed()
        {
            var menus = Controller();
            var changes = new List<StockUiSettingKey>();
            menus.Settings.Changed += (k, _) => changes.Add(k);
            Assert.True(menus.Open(StockUiConfigPages.GameplayPage));
            Assert.True(menus.Top!.IsMarked(1));

            menus.Settings.ApplyServer(autoTargetOff: true, anonymous: true, systemMessageFilterLevel: 0, messageFilter1: (uint)ChatFilter1.Shout, messageFilter2: 0);
            Assert.True(menus.Top.IsMarked(2));
            Assert.Equal(1, menus.Settings.GetValue(StockUiSettingKey.CharacterInfoHidden));
            Assert.Equal((uint)ChatFilter1.Shout, menus.Settings.MessageFilter1);
            Assert.Empty(changes);

            // Swapping in another settings object (the character's, once known) re-reads the markers from it.
            var other = new StockUiSettings();
            menus.Settings = other;
            Assert.True(menus.Top.IsMarked(1));
        }

        [Fact]
        public void LayoutBackedRows_GoThroughTheDelegates()
        {
            bool icons = false;
            var menus = new StockUiMenuController
            {
                Library = UiResourceLibrary.FromDefinitions(new[] { Page(StockUiConfigPages.Misc3Page, 9, 10) }),
                CurrentPartyIcons = () => icons,
                PartyIconsSelected = on => icons = on,
            };
            Assert.True(menus.Open(StockUiConfigPages.Misc3Page));
            Assert.True(menus.Top!.IsMarked(10));      // OFF
            menus.Activate();                          // 9 = ON
            Assert.True(icons);
            Assert.True(menus.Top.IsMarked(9));
            Assert.False(menus.Settings.HasValue(StockUiSettingKey.PartyIconDisplay));
        }

        [Fact]
        public void Settings_PersistClientValuesOnly_ClampAndIgnoreUnknownKeys()
        {
            string path = Path.Combine(Path.GetTempPath(), "gordian_ui_settings_" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var settings = new StockUiSettings();
                settings.SetValue(StockUiSettingKey.MusicVolume, 40);
                settings.SetValue(StockUiSettingKey.Window1MaxLines, 99);      // clamped to 8
                settings.SetValue(StockUiSettingKey.AutoTarget, 0);            // server-scoped: not saved
                settings.SaveToFile(path);

                string json = File.ReadAllText(path);
                Assert.Contains("MusicVolume", json);
                Assert.DoesNotContain("AutoTarget", json);
                File.WriteAllText(path, json.Replace("\"MusicVolume\": 40", "\"MusicVolume\": 40, \"NotASetting\": 3, \"GammaRed\": 900"));

                var loaded = StockUiSettings.LoadOrDefault(path);
                Assert.Equal(40, loaded.GetValue(StockUiSettingKey.MusicVolume));
                Assert.Equal(8, loaded.GetValue(StockUiSettingKey.Window1MaxLines));
                Assert.Equal(100, loaded.GetValue(StockUiSettingKey.GammaRed));
                Assert.True(loaded.HasValue(StockUiSettingKey.GammaRed));
                Assert.False(loaded.HasValue(StockUiSettingKey.SoundEffectsVolume));
                Assert.Equal(1, loaded.GetValue(StockUiSettingKey.AutoTarget));

                Assert.Equal(new StockUiSettings().GetValue(StockUiSettingKey.Shadows), StockUiSettings.LoadOrDefault(path + ".missing").GetValue(StockUiSettingKey.Shadows));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public async Task ActionService_SendsServerSettingsThroughTheConfigModule()
        {
            var profile = new SessionProfile();
            var world = new WorldState();
            var localPlayer = new LocalPlayerState { ServerId = 0x01020304 };
            var sent = new List<byte[]>();
            Task Send(ReadOnlyMemory<byte> chunk, bool urgent) { sent.Add(chunk.ToArray()); return Task.CompletedTask; }
            var service = new PlayerActionService(profile, world, localPlayer,
                new CombatPacketModule(new CombatState(), localPlayer, Send), new ChatPacketModule(Send),
                new PartyPacketModule(new PartyState(), Send), new EntityPacketModule(world, localPlayer, Send),
                new LifecyclePacketModule(profile, Send), Send);
            var state = new PlayerConfigState();
            service.ConfigModule = new ConfigPacketModule(state, Send);

            // The server's 0x0B4 lands in the settings: auto-target off, anonymous, Shout filtered.
            var payload = new byte[20];
            BitConverter.GetBytes((uint)(PlayerConfigFlags.AutoTargetOff | PlayerConfigFlags.Anonymity | (PlayerConfigFlags)(3u << 3))).CopyTo(payload, 0);
            BitConverter.GetBytes((uint)ChatFilter1.Shout).CopyTo(payload, 4);
            state.Apply(new S2C_0x0B4_Config(payload));
            Assert.Equal(0, service.UiSettings.GetValue(StockUiSettingKey.AutoTarget));
            Assert.Equal(1, service.UiSettings.GetValue(StockUiSettingKey.CharacterInfoHidden));
            Assert.Equal((uint)ChatFilter1.Shout, service.UiSettings.MessageFilter1);

            // Turning auto-target on clears the AutoTargetOff flag with 0x0DC.
            service.UiSettings.SetValue(StockUiSettingKey.AutoTarget, 1);
            await Task.Delay(10);
            var flagPacket = Assert.Single(sent);
            Assert.Equal(0x0DC, BitConverter.ToUInt16(flagPacket, 0) & 0x1FF);
            Assert.Equal((uint)PlayerConfigFlags.AutoTargetOff, BitConverter.ToUInt32(flagPacket, 4));
            Assert.Equal(2, flagPacket[16]);           // off
            Assert.False(state.IsSet(PlayerConfigFlags.AutoTargetOff));

            // A chat-filter change sends both words and echoes the server's flag word (with AutoTargetOff now clear).
            service.UiSettings.SetChatFilters((uint)ChatFilter1.Shout | (uint)ChatFilter1.Say, (uint)ChatFilter2.Yell);
            await Task.Delay(10);
            Assert.Equal(2, sent.Count);
            var filterPacket = sent[1];
            Assert.Equal(0x0DB, BitConverter.ToUInt16(filterPacket, 0) & 0x1FF);
            Assert.Equal(0, filterPacket[6]);
            Assert.Equal((uint)(PlayerConfigFlags.Anonymity | (PlayerConfigFlags)(3u << 3)), BitConverter.ToUInt32(filterPacket, 8));
            Assert.Equal((uint)ChatFilter1.Shout | (uint)ChatFilter1.Say, BitConverter.ToUInt32(filterPacket, 12));
            Assert.Equal((uint)ChatFilter2.Yell, BitConverter.ToUInt32(filterPacket, 16));

            // Client settings never reach the server.
            service.UiSettings.SetValue(StockUiSettingKey.MusicVolume, 10);
            await Task.Delay(10);
            Assert.Equal(2, sent.Count);
        }
    }
}
