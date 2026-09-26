// src/Gordian.Core/Ui/StockUiConfigPages.cs
using System;
using System.Collections.Generic;
using Gordian.Core.Network.Packets;

namespace Gordian.Core.Ui
{
    /// <summary>One selectable value of an option row: the DAT button that shows it and the setting value it stands for.</summary>
    public readonly record struct StockUiConfigChoice(int ButtonId, int Value, string Name);

    /// <summary>A row of a config page: a setting and how the page's buttons present it.</summary>
    public abstract class StockUiConfigRow
    {
        protected StockUiConfigRow(string label, StockUiSettingKey key)
        {
            Label = label;
            Key = key;
        }

        /// <summary>The row's caption as the page art shows it.</summary>
        public string Label { get; }
        public StockUiSettingKey Key { get; }
        public StockUiSettingDefinition Definition => StockUiSettings.Definitions[Key];
    }

    /// <summary>A multiple-choice row: one button per value, the value in effect underlined with retail's red bar.</summary>
    public sealed class StockUiOptionRow : StockUiConfigRow
    {
        public StockUiOptionRow(string label, StockUiSettingKey key, params StockUiConfigChoice[] choices) : base(label, key)
        {
            Choices = choices;
        }

        public IReadOnlyList<StockUiConfigChoice> Choices { get; }

        public bool TryGetChoice(int buttonId, out StockUiConfigChoice choice)
        {
            foreach (var c in Choices)
            {
                if (c.ButtonId == buttonId) { choice = c; return true; }
            }
            choice = default;
            return false;
        }

        /// <summary>The button showing the given value, or 0.</summary>
        public int ButtonFor(int value)
        {
            foreach (var c in Choices) if (c.Value == value) return c.ButtonId;
            return 0;
        }
    }

    /// <summary>
    /// A slider row ("Min ... Max"): the DAT button is an invisible 192 x 16 hit region over the bar authored in
    /// the frame art; left/right move the value by the setting's step and the client fills the bar to the value.
    /// </summary>
    public sealed class StockUiSliderRow : StockUiConfigRow
    {
        public StockUiSliderRow(string label, StockUiSettingKey key, int buttonId) : base(label, key)
        {
            ButtonId = buttonId;
        }

        public int ButtonId { get; }

        /// <summary>The bar's fill fraction for a value (0 at Min, 1 at Max).</summary>
        public float Fraction(int value)
        {
            var d = Definition;
            return d.Max <= d.Min ? 0f : (float)(Math.Clamp(value, d.Min, d.Max) - d.Min) / (d.Max - d.Min);
        }
    }

    /// <summary>A config page: the DAT menu that draws it and its rows.</summary>
    public sealed class StockUiConfigPage
    {
        public StockUiConfigPage(string menu, string title, params StockUiConfigRow[] rows)
        {
            Menu = menu;
            Title = title;
            Rows = rows;
        }

        public string Menu { get; }
        public string Title { get; }
        public IReadOnlyList<StockUiConfigRow> Rows { get; }

        /// <summary>The option row (and choice) a button belongs to.</summary>
        public bool TryGetOption(int buttonId, out StockUiOptionRow row, out StockUiConfigChoice choice)
        {
            foreach (var r in Rows)
            {
                if (r is StockUiOptionRow option && option.TryGetChoice(buttonId, out choice)) { row = option; return true; }
            }
            row = null!;
            choice = default;
            return false;
        }

        /// <summary>The slider row a button is.</summary>
        public bool TryGetSlider(int buttonId, out StockUiSliderRow row)
        {
            foreach (var r in Rows)
            {
                if (r is StockUiSliderRow slider && slider.ButtonId == buttonId) { row = slider; return true; }
            }
            row = null!;
            return false;
        }
    }

    /// <summary>
    /// One entry of the Chat Filters list: which filter word (1 or 2 = the server's words sent in C2S 0x0DB; 0 = the
    /// client-only mask for channels the server has no bit for) and which bit it toggles. A set bit means the filter
    /// is ON (the message kind is hidden).
    /// </summary>
    public readonly record struct StockUiChatFilterEntry(string Label, int Word, uint Bit)
    {
        /// <summary>A bit of one of the server's two filter words.</summary>
        public bool IsServerBit => Word is 1 or 2;

        /// <summary>A "System Lv. N" row: <see cref="Bit"/> is the level (1-3) of the server's 2-bit SysMesFilterLevel.</summary>
        public bool IsSystemLevel => Word == StockUiConfigPages.SystemLevelWord;

        public bool IsFiltered(uint filter1, uint filter2, uint clientFilters, int systemLevel) => Word switch
        {
            1 => (filter1 & Bit) != 0,
            2 => (filter2 & Bit) != 0,
            StockUiConfigPages.SystemLevelWord => systemLevel >= Bit,
            _ => (clientFilters & Bit) != 0,
        };
    }

    /// <summary>
    /// The stock config menu's pages: which DAT menu draws each page and what its buttons mean. The DAT only carries
    /// the label sprites and navigation links; the meaning of every button was read from the page art (composited
    /// from ROM/119/51 with <c>GORDIAN_UI_DUMP_MENUS</c>, 2026-09-26). Option values: ON = 1 / OFF = 0, otherwise the
    /// index of the choice in its authored order; sliders hold the setting's value directly.
    /// </summary>
    public static class StockUiConfigPages
    {
        public const string GameplayPage = "conf2win";
        public const string ChatFiltersPage = "cfilter";
        public const string WindowSettingsPage = "conf5win";
        public const string Window1SettingsPage = "conf5w1";
        public const string Window2SettingsPage = "conf5w2";
        public const string MiscPage = "conf3win";
        public const string Misc2Page = "conf6win";
        public const string Misc3Page = "conf12wi";
        public const string Misc4Page = "conf13wi";
        public const string MouseCameraPage = "conf7";
        public const string GlobalPage = "conf4";

        /// <summary>Rows per page of the Chat Filters list (its 14 invisible row buttons).</summary>
        public const int ChatFilterRowsPerPage = 14;

        private static StockUiConfigChoice On(int button) => new(button, 1, "ON");
        private static StockUiConfigChoice Off(int button) => new(button, 0, "OFF");

        private static readonly Dictionary<string, StockUiConfigPage> Pages = new(StringComparer.OrdinalIgnoreCase);

        static StockUiConfigPages()
        {
            void Add(StockUiConfigPage page) => Pages[page.Menu] = page;

            Add(new StockUiConfigPage(GameplayPage, "Game Settings",
                new StockUiOptionRow("Controls: Auto-target during battle", StockUiSettingKey.AutoTarget, On(1), Off(2)),
                new StockUiSliderRow("Sound Effects Volume", StockUiSettingKey.SoundEffectsVolume, 3),
                new StockUiSliderRow("Music Volume", StockUiSettingKey.MusicVolume, 4),
                new StockUiSliderRow("Gamma Adjustment R", StockUiSettingKey.GammaRed, 5),
                new StockUiSliderRow("Gamma Adjustment G", StockUiSettingKey.GammaGreen, 6),
                new StockUiSliderRow("Gamma Adjustment B", StockUiSettingKey.GammaBlue, 7),
                new StockUiOptionRow("Inventory Sort", StockUiSettingKey.InventorySort, On(8), Off(9)),
                new StockUiOptionRow("Inventory Type", StockUiSettingKey.InventoryType, new(10, 1, "Type 1"), new(11, 2, "Type 2"))));

            Add(new StockUiConfigPage(WindowSettingsPage, "Window Settings (Shared)",
                new StockUiOptionRow("Log Window Multi-window", StockUiSettingKey.LogMultiWindow, new(1, 1, "Vertical"), new(2, 2, "Horizontal"), Off(3)),
                new StockUiOptionRow("Timestamp", StockUiSettingKey.LogTimestamp, new(4, 1, "00:00"), new(5, 2, "00:00:00"), Off(6)),
                new StockUiOptionRow("Window Type", StockUiSettingKey.WindowType,
                    new(7, 1, "1"), new(8, 2, "2"), new(9, 3, "3"), new(10, 4, "4"), new(11, 5, "5"), new(12, 6, "6"), new(13, 7, "7"), new(14, 8, "8")),
                new StockUiOptionRow("Window Effect", StockUiSettingKey.WindowEffect, On(15), Off(16))));

            Add(new StockUiConfigPage(Window1SettingsPage, "Window Settings (Window 1)",
                new StockUiOptionRow("Reactive window sizing", StockUiSettingKey.Window1ReactiveSizing, On(1), Off(2)),
                new StockUiSliderRow("Maximum lines displayed", StockUiSettingKey.Window1MaxLines, 3),
                new StockUiSliderRow("Minimum lines displayed", StockUiSettingKey.Window1MinLines, 4),
                new StockUiSliderRow("Window Width", StockUiSettingKey.Window1Width, 5),
                new StockUiSliderRow("Resize Time", StockUiSettingKey.Window1ResizeTime, 6)));

            Add(new StockUiConfigPage(Window2SettingsPage, "Window Settings (Window 2)",
                new StockUiOptionRow("Reactive window sizing", StockUiSettingKey.Window2ReactiveSizing, On(1), Off(2)),
                new StockUiSliderRow("Maximum lines displayed", StockUiSettingKey.Window2MaxLines, 3),
                new StockUiSliderRow("Minimum lines displayed", StockUiSettingKey.Window2MinLines, 4),
                new StockUiSliderRow("Window Width", StockUiSettingKey.Window2Width, 5),
                new StockUiSliderRow("Resize Time", StockUiSettingKey.Window2ResizeTime, 6)));

            Add(new StockUiConfigPage(MiscPage, "Misc.",
                new StockUiOptionRow("On-screen damage display", StockUiSettingKey.DamageDisplay, new(1, 0, "Log"), new(2, 1, "Both"), new(5, 2, "Screen")),
                new StockUiOptionRow("Character Information", StockUiSettingKey.CharacterInfoHidden, new(3, 0, "Show"), new(4, 1, "Hide")),
                new StockUiOptionRow("Shadows", StockUiSettingKey.Shadows, new(6, 0, "High"), new(7, 1, "Normal"), new(8, 2, "Off")),
                new StockUiOptionRow("Weather Effects", StockUiSettingKey.WeatherEffects, On(9), Off(10)),
                new StockUiSliderRow("Character Models Displayed", StockUiSettingKey.CharacterModelsDisplayed, 11),
                new StockUiOptionRow("Icon Type", StockUiSettingKey.IconType, new(12, 1, "1"), new(13, 2, "2")),
                new StockUiOptionRow("PC Armor Display", StockUiSettingKey.PcArmorDisplay, new(14, 0, "Normal"), new(15, 1, "Static"))));

            Add(new StockUiConfigPage(Misc2Page, "Misc. 2",
                new StockUiSliderRow("Clipping Plane", StockUiSettingKey.ClippingPlane, 1),
                new StockUiOptionRow("Footstep Effects", StockUiSettingKey.FootstepEffects, On(2), Off(3)),
                new StockUiSliderRow("Animation Frame Rate", StockUiSettingKey.AnimationFrameRate, 4),
                new StockUiOptionRow("Keyboard Size", StockUiSettingKey.KeyboardSize, new(5, 0, "Full"), new(6, 1, "Compact 1"), new(11, 2, "Compact 2")),
                new StockUiSliderRow("Background Aspect Ratio", StockUiSettingKey.BackgroundAspectRatio, 7),
                new StockUiOptionRow("Key Assignment", StockUiSettingKey.KeyAssignment, new(8, 0, "Movement"), new(9, 1, "Commands"), new(10, 2, "Default")),
                new StockUiOptionRow("Macro Palette Size", StockUiSettingKey.MacroPaletteSize, new(12, 0, "Full"), new(13, 1, "Compact")),
                new StockUiOptionRow("Macro Palette Position", StockUiSettingKey.MacroPalettePosition, new(14, 0, "Dynamic"), new(15, 1, "Static"))));

            Add(new StockUiConfigPage(Misc3Page, "Misc. 3",
                new StockUiOptionRow("Weapon Effect", StockUiSettingKey.WeaponEffect, On(1), Off(2)),
                new StockUiOptionRow("Style Lock", StockUiSettingKey.StyleLock, new(3, 0, "Normal"), new(4, 1, "Static")),
                new StockUiOptionRow("Area Display", StockUiSettingKey.AreaDisplay, On(5), Off(6)),
                new StockUiOptionRow("Target Expressions", StockUiSettingKey.TargetExpressions, On(7), Off(8)),
                new StockUiOptionRow("Status Icons: Party Icon Display", StockUiSettingKey.PartyIconDisplay, On(9), Off(10)),
                new StockUiOptionRow("Status Icons: Timer Display", StockUiSettingKey.TimerDisplay, On(11), Off(12)),
                new StockUiOptionRow("Furniture Camera Collision", StockUiSettingKey.FurnitureCameraCollision, On(13), Off(14))));

            Add(new StockUiConfigPage(Misc4Page, "Misc. 4",
                new StockUiOptionRow("Software Keyboard", StockUiSettingKey.SoftwareKeyboard, new(1, 0, "Off"), new(2, 1, "Full"), new(3, 2, "Compact")),
                new StockUiOptionRow("Term Filter", StockUiSettingKey.TermFilter, new(4, 0, "On"), new(5, 1, "Display Name Only"), new(6, 2, "Off"))));

            Add(new StockUiConfigPage(MouseCameraPage, "Mouse/Camera",
                new StockUiOptionRow("Mouse Control", StockUiSettingKey.MouseControlType, new(1, 1, "Type 1"), new(2, 2, "Type 2")),
                new StockUiOptionRow("Screen Edge Panning", StockUiSettingKey.ScreenEdgePanning, On(3), Off(4)),
                new StockUiOptionRow("Camera View", StockUiSettingKey.CameraView, new(5, 0, "Normal"), new(6, 1, "Chase Cam")),
                new StockUiOptionRow("Third-Person Camera Y Axis", StockUiSettingKey.ThirdPersonInvertY, new(7, 0, "Normal"), new(8, 1, "Inverted")),
                new StockUiOptionRow("Third-Person Camera X Axis", StockUiSettingKey.ThirdPersonInvertX, new(9, 0, "Normal"), new(10, 1, "Inverted")),
                new StockUiOptionRow("First-Person Camera Y Axis", StockUiSettingKey.FirstPersonInvertY, new(11, 0, "Normal"), new(12, 1, "Inverted")),
                new StockUiOptionRow("First-Person Camera X Axis", StockUiSettingKey.FirstPersonInvertX, new(13, 0, "Normal"), new(14, 1, "Inverted"))));

            Add(new StockUiConfigPage(GlobalPage, "Global",
                new StockUiOptionRow("Chat Language Filter", StockUiSettingKey.ChatLanguageFilter, On(1), Off(2)),
                new StockUiOptionRow("Idle time (min.) until auto-disconnect", StockUiSettingKey.AutoDisconnectMinutes,
                    new(3, 10, "10"), new(4, 20, "20"), new(5, 30, "30"), new(6, 40, "40"), new(7, 50, "50"), new(8, 60, "60"), new(9, 0, "OFF"))));
        }

        public static IReadOnlyCollection<StockUiConfigPage> All => Pages.Values;

        public static bool TryGet(string menuName, out StockUiConfigPage page) => Pages.TryGetValue(menuName, out page!);

        /// <summary>
        /// The Chat Filters list in retail's order and wording, read from captures of all four screens (2026-09-26):
        /// one continuous list of 55 entries that scrolls 14 visible rows at a time. The row text is client-drawn
        /// (the DAT rows are invisible hit regions). Tell, Party, Linkshell, Linkshell 2 and Unity have no server
        /// bit (LandSandBoat's filter words carry none), so they are client-only filters (word 0); the three
        /// "System Lv." rows stack on the flag word's 2-bit SysMesFilterLevel (word 3, bit = level). Server bits
        /// referenced from LandSandBoat (<c>filters1_t</c> / <c>filters2_t</c>, <c>src/common/mmo.h</c>).
        /// </summary>
        public static IReadOnlyList<StockUiChatFilterEntry> ChatFilters { get; } = new[]
        {
            new StockUiChatFilterEntry("Say", 1, (uint)ChatFilter1.Say),
            new StockUiChatFilterEntry("Tell", 0, ClientChatFilterTell),
            new StockUiChatFilterEntry("Party", 0, ClientChatFilterParty),
            new StockUiChatFilterEntry("Linkshell", 0, ClientChatFilterLinkshell),
            new StockUiChatFilterEntry("Linkshell 2", 0, ClientChatFilterLinkshell2),
            new StockUiChatFilterEntry("Assist J", 2, (uint)ChatFilter2.AssistJ),
            new StockUiChatFilterEntry("Assist E", 2, (uint)ChatFilter2.AssistE),
            new StockUiChatFilterEntry("Unity", 0, ClientChatFilterUnity),
            new StockUiChatFilterEntry("Emotes", 1, (uint)ChatFilter1.Emotes),
            new StockUiChatFilterEntry("Shout", 1, (uint)ChatFilter1.Shout),
            new StockUiChatFilterEntry("Yell", 2, (uint)ChatFilter2.Yell),
            new StockUiChatFilterEntry("Special actions started on/by you", 1, (uint)ChatFilter1.SpecialActionsStartedByYou),
            new StockUiChatFilterEntry("Special action effects on/by you", 1, (uint)ChatFilter1.SpecialActionEffectsByYou),
            new StockUiChatFilterEntry("Attacks by you", 1, (uint)ChatFilter1.AttacksByYou),
            new StockUiChatFilterEntry("Missed attacks by you", 1, (uint)ChatFilter1.MissedAttacksByYou),
            new StockUiChatFilterEntry("Attacks you evade", 1, (uint)ChatFilter1.AttacksYouEvade),
            new StockUiChatFilterEntry("Damage you take", 1, (uint)ChatFilter1.DamageYouTake),
            new StockUiChatFilterEntry("Special actions started on/by party", 1, (uint)ChatFilter1.SpecialActionsStartedByParty),
            new StockUiChatFilterEntry("Special action effects on/by party", 1, (uint)ChatFilter1.SpecialActionEffectsByParty),
            new StockUiChatFilterEntry("Attacks by party", 1, (uint)ChatFilter1.AttacksByParty),
            new StockUiChatFilterEntry("Missed attacks by party", 1, (uint)ChatFilter1.MissedAttacksByParty),
            new StockUiChatFilterEntry("Attacks evaded by party", 1, (uint)ChatFilter1.AttacksEvadedByParty),
            new StockUiChatFilterEntry("Damage taken by party", 1, (uint)ChatFilter1.DamageTakenByParty),
            new StockUiChatFilterEntry("Special actions started on/by allies", 1, (uint)ChatFilter1.SpecialActionsStartedByAllies),
            new StockUiChatFilterEntry("Special action effects on/by allies", 1, (uint)ChatFilter1.SpecialActionEffectsByAllies),
            new StockUiChatFilterEntry("Attacks by allies", 1, (uint)ChatFilter1.AttacksByAllies),
            new StockUiChatFilterEntry("Missed attacks by allies", 1, (uint)ChatFilter1.MissedAttacksByAllies),
            new StockUiChatFilterEntry("Attacks evaded by allies", 1, (uint)ChatFilter1.AttacksEvadedByAllies),
            new StockUiChatFilterEntry("Damage taken by allies", 1, (uint)ChatFilter1.DamageTakenByAllies),
            new StockUiChatFilterEntry("Special actions started on/by foes", 2, (uint)ChatFilter2.SpecialActionsStartedByFoes),
            new StockUiChatFilterEntry("Special action effects on/by foes", 2, (uint)ChatFilter2.SpecialActionEffectsByFoes),
            new StockUiChatFilterEntry("Attacks by foes", 2, (uint)ChatFilter2.AttacksByFoes),
            new StockUiChatFilterEntry("Missed attacks by foes", 2, (uint)ChatFilter2.MissedAttacksByFoes),
            new StockUiChatFilterEntry("Attacks evaded by foes", 2, (uint)ChatFilter2.AttacksEvadedByFoes),
            new StockUiChatFilterEntry("Damage taken by foes", 2, (uint)ChatFilter2.DamageTakenByFoes),
            new StockUiChatFilterEntry("Special actions started on/by others", 2, (uint)ChatFilter2.SpecialActionsStartedByOthers),
            new StockUiChatFilterEntry("Special action effects on/by others", 2, (uint)ChatFilter2.SpecialActionEffectsByOthers),
            new StockUiChatFilterEntry("Attacks by others", 1, (uint)ChatFilter1.AttacksByOthers),
            new StockUiChatFilterEntry("Missed attacks by others", 1, (uint)ChatFilter1.MissedAttacksByOthers),
            new StockUiChatFilterEntry("Attacks evaded by others", 2, (uint)ChatFilter2.AttacksEvadedByOthers),
            new StockUiChatFilterEntry("Damage taken by others", 2, (uint)ChatFilter2.DamageTakenByOthers),
            new StockUiChatFilterEntry("Special actions started on/by NPCs", 1, (uint)ChatFilter1.SpecialActionsStartedByNpcs),
            new StockUiChatFilterEntry("Special action effects on/by NPCs", 1, (uint)ChatFilter1.SpecialActionEffectsByNpcs),
            new StockUiChatFilterEntry("Attacks by NPCs", 1, (uint)ChatFilter1.AttacksByNpcs),
            new StockUiChatFilterEntry("Missed attacks by NPCs", 1, (uint)ChatFilter1.MissedAttacksByNpcs),
            new StockUiChatFilterEntry("Others' synthesis and fishing results", 1, (uint)ChatFilter1.OthersSynthesisAndFishingResults),
            new StockUiChatFilterEntry("Lot results", 1, (uint)ChatFilter1.LotResults),
            new StockUiChatFilterEntry("Campaign-Related Data", 2, (uint)ChatFilter2.CampaignRelatedData),
            new StockUiChatFilterEntry("Tell messages deemed spam", 2, (uint)ChatFilter2.TellMessagesDeemedSpam),
            new StockUiChatFilterEntry("Shout/Yell messages deemed spam", 2, (uint)ChatFilter2.ShoutYellMessagesDeemedSpam),
            new StockUiChatFilterEntry("Job-specific emote", 2, (uint)ChatFilter2.JobSpecificEmote),
            new StockUiChatFilterEntry("Messages from alter egos", 2, (uint)ChatFilter2.MessagesFromAlterEgos),
            new StockUiChatFilterEntry("System Lv. 1 (\"Check\" notices)", SystemLevelWord, 1),
            new StockUiChatFilterEntry("System Lv. 2 (Bazaar notices)", SystemLevelWord, 2),
            new StockUiChatFilterEntry("System Lv. 3 (Countdowns)", SystemLevelWord, 3),
        };

        /// <summary>The <see cref="StockUiChatFilterEntry.Word"/> of the "System Lv." rows.</summary>
        public const int SystemLevelWord = 3;

        /// <summary>Bits of the client-only chat filter mask (<see cref="StockUiSettingKey.ClientChatFilters"/>).</summary>
        public const uint ClientChatFilterTell = 1u << 0;
        public const uint ClientChatFilterParty = 1u << 1;
        public const uint ClientChatFilterLinkshell = 1u << 2;
        public const uint ClientChatFilterLinkshell2 = 1u << 3;
        public const uint ClientChatFilterUnity = 1u << 4;

    }
}
