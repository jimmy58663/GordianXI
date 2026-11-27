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

        /// <summary>Returns the layout of <paramref name="characterName"/> on <paramref name="server"/>.</summary>
        public static StockUiLayout GetForCharacter(string? server, string? characterName)
        {
            return Layouts.GetOrAdd(CharacterConfigStorage.GetDirectory(server, characterName), _ =>
            {
                string path = CharacterConfigStorage.Resolve(StockUiLayout.ConfigFile, server, characterName);
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
