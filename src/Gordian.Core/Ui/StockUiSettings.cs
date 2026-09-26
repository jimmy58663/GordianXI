// src/Gordian.Core/Ui/StockUiSettings.cs
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Gordian.Core.Config;
using Gordian.Core.Diagnostics;

namespace Gordian.Core.Ui
{
    /// <summary>Where a config-menu setting lives.</summary>
    public enum StockUiSettingScope
    {
        /// <summary>Client-only; persisted per character in the settings file.</summary>
        Client,

        /// <summary>Held by the server (S2C 0x0B4) and changed with C2S 0x0DB / 0x0DC; mirrored here, not persisted.</summary>
        Server,

        /// <summary>Stored on the <see cref="StockUiLayout"/> (window skin, party status icons); routed there by the menu controller.</summary>
        Layout,
    }

    /// <summary>
    /// Every option of the stock config menu (its pages were read from the menu DAT's label sprites, 2026-09-26).
    /// Values are small integers: 0/1 for ON/OFF rows, an index for multi-choice rows, the slider position otherwise.
    /// </summary>
    public enum StockUiSettingKey
    {
        // Gameplay ("Game Settings", conf2win)
        AutoTarget,
        SoundEffectsVolume,
        MusicVolume,
        GammaRed,
        GammaGreen,
        GammaBlue,
        InventorySort,
        InventoryType,

        // Window Settings (Shared) (conf5win)
        LogMultiWindow,
        LogTimestamp,
        WindowType,
        WindowEffect,

        // Window Settings (Window 1 / Window 2) (conf5w1 / conf5w2)
        Window1ReactiveSizing,
        Window1MaxLines,
        Window1MinLines,
        Window1Width,
        Window1ResizeTime,
        Window2ReactiveSizing,
        Window2MaxLines,
        Window2MinLines,
        Window2Width,
        Window2ResizeTime,

        // Misc. (conf3win)
        DamageDisplay,
        CharacterInfoHidden,
        Shadows,
        WeatherEffects,
        CharacterModelsDisplayed,
        IconType,
        PcArmorDisplay,

        // Misc. 2 (conf6win)
        ClippingPlane,
        FootstepEffects,
        AnimationFrameRate,
        KeyboardSize,
        BackgroundAspectRatio,
        KeyAssignment,
        MacroPaletteSize,
        MacroPalettePosition,

        // Misc. 3 (conf12wi)
        WeaponEffect,
        StyleLock,
        AreaDisplay,
        TargetExpressions,
        PartyIconDisplay,
        TimerDisplay,
        FurnitureCameraCollision,

        // Misc. 4 (conf13wi)
        SoftwareKeyboard,
        TermFilter,

        // Mouse/Camera (conf7)
        MouseControlType,
        ScreenEdgePanning,
        CameraView,
        ThirdPersonInvertY,
        ThirdPersonInvertX,
        FirstPersonInvertY,
        FirstPersonInvertX,

        // Global (conf4)
        ChatLanguageFilter,
        AutoDisconnectMinutes,

        /// <summary>Bit mask of the Chat Filters rows the server has no bit for (Tell, Party, Linkshell, Linkshell 2, Unity).</summary>
        ClientChatFilters,

        /// <summary>The server's 2-bit system message filter level (0-3): the Chat Filters list's three "System Lv." rows stack on it.</summary>
        SystemMessageFilterLevel,
    }

    /// <summary>A setting's range, default and scope; <see cref="Step"/> is a slider's increment per key press.</summary>
    public sealed record StockUiSettingDefinition(StockUiSettingKey Key, int Default, int Min, int Max, StockUiSettingScope Scope = StockUiSettingScope.Client, int Step = 1)
    {
        public int Clamp(int value) => Math.Clamp(value, Min, Max);
    }

    /// <summary>
    /// The character's stock config-menu settings: one integer per <see cref="StockUiSettingKey"/> (defaults are
    /// the retail defaults where known) plus the two chat-filter words mirrored from the server. Client settings are
    /// saved per character (<c>ui_settings/&lt;name&gt;.json</c>); server settings are mirrored from S2C 0x0B4 and
    /// sent back through the config packets when changed; layout settings live on <see cref="StockUiLayout"/>.
    /// Consumers (the HUD, the input profile, the renderer) read the values they understand; the rest are kept so
    /// the menu shows and remembers them.
    /// </summary>
    public sealed class StockUiSettings
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        private const string FileExtension = ".json";
        private readonly object _sync = new();
        private readonly Dictionary<StockUiSettingKey, int> _values = new();

        /// <summary>Every setting's definition, in menu order.</summary>
        public static IReadOnlyDictionary<StockUiSettingKey, StockUiSettingDefinition> Definitions { get; } = BuildDefinitions();

        private static Dictionary<StockUiSettingKey, StockUiSettingDefinition> BuildDefinitions()
        {
            var d = new Dictionary<StockUiSettingKey, StockUiSettingDefinition>();
            void Add(StockUiSettingKey key, int @default, int min, int max, StockUiSettingScope scope = StockUiSettingScope.Client, int step = 1) =>
                d[key] = new StockUiSettingDefinition(key, @default, min, max, scope, step);
            void Toggle(StockUiSettingKey key, bool on, StockUiSettingScope scope = StockUiSettingScope.Client) => Add(key, on ? 1 : 0, 0, 1, scope);
            void Slider(StockUiSettingKey key, int @default, int min = 0, int max = 100, int step = 5) => Add(key, @default, min, max, StockUiSettingScope.Client, step);

            Toggle(StockUiSettingKey.AutoTarget, true, StockUiSettingScope.Server);
            Slider(StockUiSettingKey.SoundEffectsVolume, 100);
            Slider(StockUiSettingKey.MusicVolume, 100);
            Slider(StockUiSettingKey.GammaRed, 50);
            Slider(StockUiSettingKey.GammaGreen, 50);
            Slider(StockUiSettingKey.GammaBlue, 50);
            Toggle(StockUiSettingKey.InventorySort, false);
            Add(StockUiSettingKey.InventoryType, 1, 1, 2);

            Add(StockUiSettingKey.LogMultiWindow, 0, 0, 2); // OFF / Vertical / Horizontal; one log window until chunk 5
            Add(StockUiSettingKey.LogTimestamp, 0, 0, 2);
            Add(StockUiSettingKey.WindowType, 1, 1, 8, StockUiSettingScope.Layout);
            Toggle(StockUiSettingKey.WindowEffect, true);

            Toggle(StockUiSettingKey.Window1ReactiveSizing, true);
            Slider(StockUiSettingKey.Window1MaxLines, 8, 1, 8, 1);
            Slider(StockUiSettingKey.Window1MinLines, 1, 1, 8, 1);
            Slider(StockUiSettingKey.Window1Width, 100);
            Slider(StockUiSettingKey.Window1ResizeTime, 50);
            Toggle(StockUiSettingKey.Window2ReactiveSizing, true);
            Slider(StockUiSettingKey.Window2MaxLines, 4, 1, 8, 1);
            Slider(StockUiSettingKey.Window2MinLines, 1, 1, 8, 1);
            Slider(StockUiSettingKey.Window2Width, 100);
            Slider(StockUiSettingKey.Window2ResizeTime, 50);

            Add(StockUiSettingKey.DamageDisplay, 1, 0, 2);
            Toggle(StockUiSettingKey.CharacterInfoHidden, false, StockUiSettingScope.Server);
            Add(StockUiSettingKey.Shadows, 1, 0, 2);
            Toggle(StockUiSettingKey.WeatherEffects, true);
            Slider(StockUiSettingKey.CharacterModelsDisplayed, 100);
            Add(StockUiSettingKey.IconType, 1, 1, 2);
            Add(StockUiSettingKey.PcArmorDisplay, 0, 0, 1);

            Slider(StockUiSettingKey.ClippingPlane, 100);
            Toggle(StockUiSettingKey.FootstepEffects, true);
            Slider(StockUiSettingKey.AnimationFrameRate, 100);
            Add(StockUiSettingKey.KeyboardSize, 0, 0, 2);
            Slider(StockUiSettingKey.BackgroundAspectRatio, 50);
            Add(StockUiSettingKey.KeyAssignment, 2, 0, 2);
            Add(StockUiSettingKey.MacroPaletteSize, 0, 0, 1);
            Add(StockUiSettingKey.MacroPalettePosition, 0, 0, 1);

            Toggle(StockUiSettingKey.WeaponEffect, true);
            Add(StockUiSettingKey.StyleLock, 0, 0, 1);
            Toggle(StockUiSettingKey.AreaDisplay, true);
            Toggle(StockUiSettingKey.TargetExpressions, true);
            Toggle(StockUiSettingKey.PartyIconDisplay, false, StockUiSettingScope.Layout);
            Toggle(StockUiSettingKey.TimerDisplay, true);
            Toggle(StockUiSettingKey.FurnitureCameraCollision, true);

            Add(StockUiSettingKey.SoftwareKeyboard, 0, 0, 2);
            Add(StockUiSettingKey.TermFilter, 0, 0, 2);

            Add(StockUiSettingKey.MouseControlType, 1, 1, 2);
            Toggle(StockUiSettingKey.ScreenEdgePanning, true);
            Add(StockUiSettingKey.CameraView, 0, 0, 1);
            Toggle(StockUiSettingKey.ThirdPersonInvertY, false);
            Toggle(StockUiSettingKey.ThirdPersonInvertX, false);
            Toggle(StockUiSettingKey.FirstPersonInvertY, false);
            Toggle(StockUiSettingKey.FirstPersonInvertX, false);

            Toggle(StockUiSettingKey.ChatLanguageFilter, false);
            Add(StockUiSettingKey.AutoDisconnectMinutes, 0, 0, 60, StockUiSettingScope.Client, 10);
            Add(StockUiSettingKey.ClientChatFilters, 0, 0, int.MaxValue);
            Add(StockUiSettingKey.SystemMessageFilterLevel, 0, 0, 3, StockUiSettingScope.Server);
            return d;
        }

        /// <summary>Raised after a local edit of a client or server setting (not for values applied from the server).</summary>
        public event Action<StockUiSettingKey, int>? Changed;

        /// <summary>Raised after a local edit of the chat filters: (filter word 1, filter word 2).</summary>
        public event Action<uint, uint>? ChatFiltersChanged;

        /// <summary>Raised after server values were applied (mirrors refreshed; open menus re-read their markers).</summary>
        public event Action? Synchronized;

        /// <summary>The first chat-filter word (mirrored from the server; a set bit hides that message kind).</summary>
        public uint MessageFilter1 { get; private set; }

        /// <summary>The second chat-filter word.</summary>
        public uint MessageFilter2 { get; private set; }

        /// <summary>The value in effect (the default until set).</summary>
        public int GetValue(StockUiSettingKey key)
        {
            lock (_sync) return _values.TryGetValue(key, out int v) ? v : Definitions[key].Default;
        }

        public bool IsOn(StockUiSettingKey key) => GetValue(key) != 0;

        /// <summary>True when the setting has been set explicitly (saved or edited), rather than left at its default.</summary>
        public bool HasValue(StockUiSettingKey key)
        {
            lock (_sync) return _values.ContainsKey(key);
        }

        /// <summary>Sets a value (clamped to its range); true when it changed. Raises <see cref="Changed"/>.</summary>
        public bool SetValue(StockUiSettingKey key, int value)
        {
            var definition = Definitions[key];
            value = definition.Clamp(value);
            lock (_sync)
            {
                if (_values.TryGetValue(key, out int current) && current == value) return false;
                _values[key] = value;
            }
            Changed?.Invoke(key, value);
            return true;
        }

        /// <summary>Sets both chat-filter words from the menu and raises <see cref="ChatFiltersChanged"/>.</summary>
        public void SetChatFilters(uint messageFilter1, uint messageFilter2)
        {
            lock (_sync)
            {
                MessageFilter1 = messageFilter1;
                MessageFilter2 = messageFilter2;
            }
            ChatFiltersChanged?.Invoke(messageFilter1, messageFilter2);
        }

        /// <summary>
        /// Mirrors the server's configuration (S2C 0x0B4) into the server-scoped settings and the chat filters
        /// without raising <see cref="Changed"/>, then raises <see cref="Synchronized"/>.
        /// </summary>
        public void ApplyServer(bool autoTargetOff, bool anonymous, int systemMessageFilterLevel, uint messageFilter1, uint messageFilter2)
        {
            lock (_sync)
            {
                _values[StockUiSettingKey.AutoTarget] = autoTargetOff ? 0 : 1;
                _values[StockUiSettingKey.CharacterInfoHidden] = anonymous ? 1 : 0;
                _values[StockUiSettingKey.SystemMessageFilterLevel] = Math.Clamp(systemMessageFilterLevel, 0, 3);
                MessageFilter1 = messageFilter1;
                MessageFilter2 = messageFilter2;
            }
            Synchronized?.Invoke();
        }

        /// <summary>Restores every client setting to its default.</summary>
        public void ResetAll()
        {
            lock (_sync)
            {
                foreach (var key in new List<StockUiSettingKey>(_values.Keys))
                {
                    if (Definitions[key].Scope == StockUiSettingScope.Client) _values.Remove(key);
                }
            }
            Synchronized?.Invoke();
        }

        #region Persistence

        /// <summary>The persisted document: client-scoped values by key name.</summary>
        private sealed class Document
        {
            public Dictionary<string, int> Values { get; set; } = new();
        }

        /// <summary>Directory holding one settings file per character.</summary>
        public static string SettingsDirectory => Path.Combine(GordianStorage.RootDataDirectory, "ui_settings");

        /// <summary>Settings file for a character ("default" when the name is empty).</summary>
        public static string GetSettingsPath(string? characterName)
        {
            string name = string.IsNullOrWhiteSpace(characterName) ? "default" : characterName.Trim();
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return Path.Combine(SettingsDirectory, name + FileExtension);
        }

        public void SaveToFile(string path)
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var document = new Document();
            lock (_sync)
            {
                foreach (var (key, value) in _values)
                {
                    if (Definitions[key].Scope == StockUiSettingScope.Client) document.Values[key.ToString()] = value;
                }
            }
            File.WriteAllText(path, JsonSerializer.Serialize(document, JsonOptions));
        }

        /// <summary>Loads a settings file, or the defaults when it is missing or unreadable. Unknown keys are ignored.</summary>
        public static StockUiSettings LoadOrDefault(string path)
        {
            var settings = new StockUiSettings();
            if (!File.Exists(path)) return settings;
            try
            {
                var document = JsonSerializer.Deserialize<Document>(File.ReadAllText(path), JsonOptions);
                if (document != null)
                {
                    foreach (var (name, value) in document.Values)
                    {
                        if (Enum.TryParse<StockUiSettingKey>(name, ignoreCase: true, out var key) && Definitions.TryGetValue(key, out var definition)
                            && definition.Scope == StockUiSettingScope.Client)
                        {
                            settings._values[key] = definition.Clamp(value);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                GordianLog.Warning("UI", $"Failed to load UI settings '{path}': {ex.Message}. Using the defaults.");
            }
            return settings;
        }

        #endregion
    }

    /// <summary>
    /// One shared <see cref="StockUiSettings"/> per character, loaded on first use and saved whenever a client
    /// setting changes.
    /// </summary>
    public static class StockUiSettingsStore
    {
        private static readonly ConcurrentDictionary<string, StockUiSettings> Settings = new(StringComparer.OrdinalIgnoreCase);

        public static StockUiSettings GetForCharacter(string? characterName)
        {
            string key = string.IsNullOrWhiteSpace(characterName) ? string.Empty : characterName.Trim();
            return Settings.GetOrAdd(key, name =>
            {
                string path = StockUiSettings.GetSettingsPath(name);
                var settings = StockUiSettings.LoadOrDefault(path);
                settings.Changed += (k, _) =>
                {
                    if (StockUiSettings.Definitions[k].Scope != StockUiSettingScope.Client) return;
                    try
                    {
                        settings.SaveToFile(path);
                    }
                    catch (Exception ex)
                    {
                        GordianLog.Warning("UI", $"Could not save the UI settings '{path}': {ex.Message}");
                    }
                };
                return settings;
            });
        }
    }
}
