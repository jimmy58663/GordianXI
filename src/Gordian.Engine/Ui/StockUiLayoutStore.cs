// src/Gordian.Engine/Ui/StockUiLayoutStore.cs
using System;
using System.Collections.Concurrent;
using Gordian.Core.Config;
using Gordian.Core.Diagnostics;

namespace Gordian.Engine.Ui
{
    /// <summary>
    /// One shared <see cref="StockUiLayout"/> per character, loaded on first use and saved whenever it changes, so
    /// the HUD, the <c>/uilayout</c> command and (later) the drag mode all edit the same layout.
    /// </summary>
    public static class StockUiLayoutStore
    {
        private static readonly ConcurrentDictionary<string, StockUiLayout> Layouts = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Returns the layout of the character <paramref name="key"/> names.</summary>
        public static StockUiLayout GetForCharacter(CharacterFolderKey key)
        {
            return Layouts.GetOrAdd(CharacterConfigStorage.GetDirectory(key), _ =>
            {
                string path = CharacterConfigStorage.Resolve(StockUiLayout.ConfigFile, key);
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
                GordianLog.Warn(LogCategories.Ui, $"Could not save the UI layout '{path}': {ex.Message}");
            }
        }
    }
}
