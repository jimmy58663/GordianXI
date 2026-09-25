// src/Gordian.Core/Ui/StockUiLayoutStore.cs
using System;
using System.Collections.Concurrent;
using Gordian.Core.Diagnostics;

namespace Gordian.Core.Ui
{
    /// <summary>
    /// One shared <see cref="StockUiLayout"/> per character, loaded on first use and saved whenever it changes, so
    /// the HUD, the <c>/uilayout</c> command and (later) the drag mode all edit the same layout.
    /// </summary>
    public static class StockUiLayoutStore
    {
        private static readonly ConcurrentDictionary<string, StockUiLayout> Layouts = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Returns the character's layout ("default" when no name is known).
        /// </summary>
        public static StockUiLayout GetForCharacter(string? characterName)
        {
            string key = string.IsNullOrWhiteSpace(characterName) ? string.Empty : characterName.Trim();
            return Layouts.GetOrAdd(key, name =>
            {
                string path = StockUiLayout.GetLayoutPath(name);
                var layout = StockUiLayout.LoadOrDefault(path);
                layout.Changed += () => Save(layout, path);
                return layout;
            });
        }

        private static void Save(StockUiLayout layout, string path)
        {
            try
            {
                layout.SaveToFile(path);
            }
            catch (Exception ex)
            {
                GordianLog.Warning("UI", $"Could not save the UI layout '{path}': {ex.Message}");
            }
        }
    }
}
