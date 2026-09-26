// src/Gordian.Core/Ui/StockUiLayout.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Gordian.Core.Config;
using Gordian.Core.Diagnostics;
using Gordian.Core.Resources.Ui;

namespace Gordian.Core.Ui
{
    /// <summary>
    /// A player's placement of one stock UI window. Null fields keep the authored value.
    /// Positions use the authored convention: 512 x 448 layout pixels, measured from the anchor corner's side of the
    /// layout (a bottom-right window at X = 384 keeps its right edge 128 - width pixels from the screen's right edge).
    /// </summary>
    public sealed class StockUiWindowOverride
    {
        public UiAnchor? Anchor { get; set; }
        public float? X { get; set; }
        public float? Y { get; set; }

        /// <summary>Multiplies the global UI scale for this window.</summary>
        public float? Scale { get; set; }

        public bool Hidden { get; set; }

        [JsonIgnore]
        public bool IsEmpty => Anchor == null && X == null && Y == null && Scale == null && !Hidden;
    }

    /// <summary>
    /// Where a stock window lands on screen: its top-left corner in screen pixels and the pixels per layout pixel.
    /// </summary>
    public readonly record struct StockUiPlacement(float X, float Y, float Scale, bool Hidden)
    {
        public float Right(float layoutWidth) => X + layoutWidth * Scale;
        public float Bottom(float layoutHeight) => Y + layoutHeight * Scale;
    }

    /// <summary>
    /// Which side of the party window the opt-in party member status icons are drawn on.
    /// </summary>
    public enum PartyStatusIconSide
    {
        Left,
        Right,
    }

    /// <summary>
    /// Logical ids of the persistent stock windows a player can place.
    /// </summary>
    public static class StockUiWindowIds
    {
        public const string Log = "log";
        public const string ChatInput = "chat";
        public const string Party = "party";

        /// <summary>The upper alliance window ("raid1"): the first other party of an alliance.</summary>
        public const string Alliance1 = "alliance1";

        /// <summary>The lower alliance window ("raid2"): the second other party of an alliance.</summary>
        public const string Alliance2 = "alliance2";
        public const string Target = "target";
        public const string StatusIcons = "status";
        public const string MainMenu = "menu";
    }

    /// <summary>
    /// The stock UI layout: a global scale plus optional per-window overrides, persisted per character.
    /// <para>
    /// Every persistent window's screen position is resolved here: the authored position (Section 0x30 frame) is
    /// kept at its distance from its anchor corner, so a bottom-right window stays in the bottom-right at any
    /// resolution. With no overrides and scale 1 this is the retail PC layout: one screen pixel per layout pixel,
    /// windows hugging their corners. Overrides move, re-anchor, rescale or hide a window; placements are clamped so
    /// a window never leaves the screen.
    /// </para>
    /// </summary>
    public sealed class StockUiLayout
    {
        public const string FileExtension = ".json";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() },
        };

        /// <summary>Screen pixels per layout pixel for every window (retail: 1).</summary>
        public float Scale { get; set; } = 1.0f;

        /// <summary>
        /// Opt-in enhancement (not in the legacy client): show each party member's TP in the party window.
        /// </summary>
        public bool ShowPartyTp { get; set; }

        /// <summary>
        /// Opt-in enhancement (not in the legacy client): show each party member's status icons beside their row.
        /// </summary>
        public bool ShowPartyStatusIcons { get; set; }

        /// <summary>The side of the party window the party member status icons are drawn on.</summary>
        public PartyStatusIconSide PartyStatusIconSide { get; set; } = PartyStatusIconSide.Left;

        /// <summary>
        /// Overrides keyed by logical window id (<see cref="StockUiWindowIds"/>), case-insensitive. Ids are logical
        /// rather than menu names because a window swaps layouts at runtime (the party window uses "ptw1".."ptw6" by
        /// member count, the log window "log1".."log8" by line count), all sharing one anchor.
        /// </summary>
        public Dictionary<string, StockUiWindowOverride> Windows { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Raised after any override or the scale changes (outside the layout's lock).</summary>
        public event Action? Changed;

        // Overrides are edited by commands and (later) the drag mode while the render thread resolves placements.
        private readonly object _sync = new();

        /// <summary>
        /// Resolves a window's screen placement from its authored frame and any override.
        /// </summary>
        public StockUiPlacement Resolve(string windowId, UiMenuFrame frame, float screenWidth, float screenHeight)
        {
            UiAnchor anchor;
            float x, y, scale;
            bool hidden;
            lock (_sync)
            {
                Windows.TryGetValue(windowId, out var o);
                anchor = o?.Anchor ?? frame.Anchor;
                x = o?.X ?? frame.X;
                y = o?.Y ?? frame.Y;
                scale = Math.Max(0.1f, Scale * (o?.Scale ?? 1.0f));
                hidden = o?.Hidden ?? false;
            }
            return Place(anchor, x, y, frame.Width, frame.Height, scale, screenWidth, screenHeight, hidden);
        }

        /// <summary>True when the player has moved or re-anchored the window.</summary>
        public bool HasPositionOverride(string windowId)
        {
            lock (_sync) return Windows.TryGetValue(windowId, out var o) && (o.X != null || o.Y != null || o.Anchor != null);
        }

        /// <summary>
        /// Places a layout rectangle anchored to a screen corner and clamps it onto the screen.
        /// </summary>
        public static StockUiPlacement Place(UiAnchor anchor, float x, float y, float width, float height, float scale,
            float screenWidth, float screenHeight, bool hidden = false)
        {
            bool right = ((int)anchor & 1) != 0;
            bool bottom = ((int)anchor & 2) != 0;
            float sx = right ? screenWidth - (UiResourceLibrary.LayoutWidth - x) * scale : x * scale;
            float sy = bottom ? screenHeight - (UiResourceLibrary.LayoutHeight - y) * scale : y * scale;

            sx = Math.Clamp(sx, 0.0f, Math.Max(0.0f, screenWidth - width * scale));
            sy = Math.Clamp(sy, 0.0f, Math.Max(0.0f, screenHeight - height * scale));
            return new StockUiPlacement(sx, sy, scale, hidden);
        }

        /// <summary>
        /// Moves a window so its top-left lands on the given screen point, re-anchoring it to the nearest screen
        /// corner (so a window dragged to the right side keeps hugging the right edge when the window resizes).
        /// </summary>
        public void MoveTo(string windowId, UiMenuFrame frame, float screenX, float screenY, float screenWidth, float screenHeight)
        {
            lock (_sync)
            {
                var o = GetOrAdd(windowId);
                float scale = Math.Max(0.1f, Scale * (o.Scale ?? 1.0f));
                float centerX = screenX + frame.Width * scale * 0.5f;
                float centerY = screenY + frame.Height * scale * 0.5f;
                bool right = centerX > screenWidth * 0.5f;
                bool bottom = centerY > screenHeight * 0.5f;

                o.Anchor = (UiAnchor)((right ? 1 : 0) | (bottom ? 2 : 0));
                o.X = right ? UiResourceLibrary.LayoutWidth - (screenWidth - screenX) / scale : screenX / scale;
                o.Y = bottom ? UiResourceLibrary.LayoutHeight - (screenHeight - screenY) / scale : screenY / scale;
            }
            Changed?.Invoke();
        }

        /// <summary>
        /// Sets a window's position in the authored convention (layout pixels, measured from its anchor corner's side
        /// of the 512 x 448 layout), optionally re-anchoring it.
        /// </summary>
        public void SetPosition(string windowId, float x, float y, UiAnchor? anchor = null)
        {
            lock (_sync)
            {
                var o = GetOrAdd(windowId);
                o.X = x;
                o.Y = y;
                if (anchor != null) o.Anchor = anchor;
            }
            Changed?.Invoke();
        }

        public void SetWindowScale(string windowId, float? scale)
        {
            lock (_sync)
            {
                GetOrAdd(windowId).Scale = scale is { } s ? Math.Clamp(s, 0.25f, 8.0f) : null;
                Prune(windowId);
            }
            Changed?.Invoke();
        }

        public void SetHidden(string windowId, bool hidden)
        {
            lock (_sync)
            {
                GetOrAdd(windowId).Hidden = hidden;
                Prune(windowId);
            }
            Changed?.Invoke();
        }

        public void SetScale(float scale)
        {
            lock (_sync) Scale = Math.Clamp(scale, 0.25f, 8.0f);
            Changed?.Invoke();
        }

        public void SetShowPartyTp(bool show)
        {
            lock (_sync) ShowPartyTp = show;
            Changed?.Invoke();
        }

        public void SetShowPartyStatusIcons(bool show, PartyStatusIconSide? side = null)
        {
            lock (_sync)
            {
                ShowPartyStatusIcons = show;
                if (side is { } s) PartyStatusIconSide = s;
            }
            Changed?.Invoke();
        }

        /// <summary>Restores one window's authored placement.</summary>
        public void Reset(string windowId)
        {
            bool removed;
            lock (_sync) removed = Windows.Remove(windowId);
            if (removed) Changed?.Invoke();
        }

        /// <summary>Restores the authored layout for every window and the retail scale.</summary>
        public void ResetAll()
        {
            lock (_sync)
            {
                Windows.Clear();
                Scale = 1.0f;
                ShowPartyTp = false;
                ShowPartyStatusIcons = false;
                PartyStatusIconSide = PartyStatusIconSide.Left;
            }
            Changed?.Invoke();
        }

        /// <summary>A snapshot of the overrides, for reporting.</summary>
        public IReadOnlyList<KeyValuePair<string, StockUiWindowOverride>> GetOverrides()
        {
            lock (_sync) return new List<KeyValuePair<string, StockUiWindowOverride>>(Windows);
        }

        private StockUiWindowOverride GetOrAdd(string windowId)
        {
            if (!Windows.TryGetValue(windowId, out var o))
            {
                o = new StockUiWindowOverride();
                Windows[windowId] = o;
            }
            return o;
        }

        private void Prune(string windowId)
        {
            if (Windows.TryGetValue(windowId, out var o) && o.IsEmpty) Windows.Remove(windowId);
        }

        /// <summary>Directory holding one layout file per character.</summary>
        public static string LayoutDirectory => Path.Combine(GordianStorage.RootDataDirectory, "ui_layouts");

        /// <summary>Layout file for a character ("default" when the name is empty).</summary>
        public static string GetLayoutPath(string? characterName)
        {
            string name = string.IsNullOrWhiteSpace(characterName) ? "default" : characterName.Trim();
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return Path.Combine(LayoutDirectory, name + FileExtension);
        }

        public void SaveToFile(string path)
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            string json;
            lock (_sync) json = JsonSerializer.Serialize(this, JsonOptions);
            File.WriteAllText(path, json);
        }

        /// <summary>Loads a layout file, or the retail defaults when it is missing or unreadable.</summary>
        public static StockUiLayout LoadOrDefault(string path)
        {
            if (File.Exists(path))
            {
                try
                {
                    var layout = JsonSerializer.Deserialize<StockUiLayout>(File.ReadAllText(path), JsonOptions);
                    if (layout != null)
                    {
                        // Re-key case-insensitively (the deserializer builds a case-sensitive dictionary).
                        layout.Windows = new Dictionary<string, StockUiWindowOverride>(layout.Windows, StringComparer.OrdinalIgnoreCase);
                        layout.Scale = Math.Clamp(layout.Scale, 0.25f, 8.0f);
                        return layout;
                    }
                }
                catch (Exception ex)
                {
                    GordianLog.Warning("UI", $"Failed to load UI layout '{path}': {ex.Message}. Using the retail layout.");
                }
            }
            return new StockUiLayout();
        }
    }
}
