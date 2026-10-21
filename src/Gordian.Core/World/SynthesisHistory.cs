// src/Gordian.Core/World/SynthesisHistory.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Gordian.Core.Diagnostics;
using Gordian.Core.Config;

namespace Gordian.Core.World
{
    /// <summary>One ingredient of a remembered synthesis: the item and how many of it.</summary>
    public readonly record struct SynthesisIngredient(ushort ItemId, byte Count);

    /// <summary>
    /// One synthesis the character has done (the History window's row): the crystal, the ingredients grouped by item (at most
    /// eight items in all, the C2S 0x096 limit), the item it made (0 while unknown: a break names only Mangled Mess), and
    /// whether the player marked it a favourite.
    /// </summary>
    public sealed record SynthesisHistoryEntry(ushort CrystalItemId, IReadOnlyList<SynthesisIngredient> Ingredients, ushort ResultItemId, bool Favorite = false)
    {
        /// <summary>Whether two entries are the same recipe (crystal and ingredients; the favourite mark and result aside).</summary>
        public bool SameRecipe(SynthesisHistoryEntry other)
        {
            if (other.CrystalItemId != CrystalItemId || other.Ingredients.Count != Ingredients.Count) return false;
            for (int i = 0; i < Ingredients.Count; i++)
            {
                if (Ingredients[i] != other.Ingredients[i]) return false;
            }
            return true;
        }

        /// <summary>Groups the one-entry-per-item ingredient list of C2S 0x096 by item id, in item id order.</summary>
        public static IReadOnlyList<SynthesisIngredient> Group(IEnumerable<ushort> itemIds)
        {
            var counts = new SortedDictionary<ushort, int>();
            foreach (ushort id in itemIds)
            {
                if (id == 0) continue;
                counts[id] = counts.TryGetValue(id, out int n) ? n + 1 : 1;
            }
            var list = new List<SynthesisIngredient>(counts.Count);
            foreach (var (id, n) in counts) list.Add(new SynthesisIngredient(id, (byte)Math.Min(n, byte.MaxValue)));
            return list;
        }
    }

    /// <summary>
    /// The character's synthesis history (main menu > Synthesis > History; retail capture <c>Synthesis_History.png</c>,
    /// 2026-10-16): every recipe the character synthesized, successes and breaks alike (the maintainer's round 1, 2026-10-18:
    /// a break must show up too), newest first, with favourites (Synthesis Options:
    /// Favorite / Unfavorite) listed before the rest. LandSandBoat has no packet for it, so the client keeps it, as retail
    /// appears to. PROVISIONAL: how many recipes retail keeps (<see cref="MaxEntries"/>) and where favourites sort are not
    /// captured; the history is saved per character to <c>synthesis_history/&lt;character&gt;.json</c>.
    /// </summary>
    public sealed class SynthesisHistory
    {
        /// <summary>How many recipes are kept (PROVISIONAL: retail's limit is not known).</summary>
        public const int MaxEntries = 50;

        private readonly object _sync = new();
        private readonly List<SynthesisHistoryEntry> _entries = new();

        /// <summary>Raised after any change (an entry recorded, a favourite toggled).</summary>
        public event Action? Changed;

        /// <summary>The entries in the order the History list shows them: favourites first, then the rest, newest first in each.</summary>
        public IReadOnlyList<SynthesisHistoryEntry> Entries
        {
            get
            {
                lock (_sync)
                {
                    var list = new List<SynthesisHistoryEntry>(_entries.Count);
                    foreach (var e in _entries) if (e.Favorite) list.Add(e);
                    foreach (var e in _entries) if (!e.Favorite) list.Add(e);
                    return list;
                }
            }
        }

        /// <summary>
        /// Records a synthesis that ran (a success, or a break with <paramref name="resultItemId"/> 0: S2C 0x06F names only Mangled
        /// Mess then): the recipe moves to the top, keeping its favourite mark and, when this one is 0, the result an earlier
        /// success named; the oldest non-favourite drops off past <see cref="MaxEntries"/>. Ignored without ingredients.
        /// </summary>
        public void Record(SynthesisRequest request, ushort resultItemId)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (request.IngredientItemIds.Length == 0) return;
            var entry = new SynthesisHistoryEntry(request.CrystalItemId, SynthesisHistoryEntry.Group(request.IngredientItemIds), resultItemId);
            lock (_sync)
            {
                int index = _entries.FindIndex(e => e.SameRecipe(entry));
                if (index >= 0)
                {
                    entry = entry with
                    {
                        Favorite = _entries[index].Favorite,
                        ResultItemId = resultItemId != 0 ? resultItemId : _entries[index].ResultItemId,
                    };
                    _entries.RemoveAt(index);
                }
                _entries.Insert(0, entry);
                while (_entries.Count > MaxEntries)
                {
                    int drop = _entries.FindLastIndex(e => !e.Favorite);
                    _entries.RemoveAt(drop >= 0 ? drop : _entries.Count - 1);
                }
            }
            Changed?.Invoke();
        }

        /// <summary>Marks or unmarks a recipe as a favourite; false when it is not in the history or already so.</summary>
        public bool SetFavorite(SynthesisHistoryEntry entry, bool favorite)
        {
            ArgumentNullException.ThrowIfNull(entry);
            lock (_sync)
            {
                int index = _entries.FindIndex(e => e.SameRecipe(entry));
                if (index < 0 || _entries[index].Favorite == favorite) return false;
                _entries[index] = _entries[index] with { Favorite = favorite };
            }
            Changed?.Invoke();
            return true;
        }

        #region Persistence

        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        private sealed class FileModel
        {
            public int Version { get; set; } = 1;
            public List<EntryModel> Entries { get; set; } = new();
        }

        private sealed class EntryModel
        {
            public ushort Crystal { get; set; }
            public ushort Result { get; set; }
            public bool Favorite { get; set; }
            public List<ushort> Items { get; set; } = new();
            public List<byte> Counts { get; set; } = new();
        }

        /// <summary>The history as JSON, in its stored order (newest first).</summary>
        public string ToJson()
        {
            var model = new FileModel();
            lock (_sync)
            {
                foreach (var e in _entries)
                {
                    var m = new EntryModel { Crystal = e.CrystalItemId, Result = e.ResultItemId, Favorite = e.Favorite };
                    foreach (var i in e.Ingredients)
                    {
                        m.Items.Add(i.ItemId);
                        m.Counts.Add(i.Count);
                    }
                    model.Entries.Add(m);
                }
            }
            return JsonSerializer.Serialize(model, JsonOptions);
        }

        /// <summary>Reads <see cref="ToJson"/>'s form; malformed entries are skipped.</summary>
        public static SynthesisHistory FromJson(string json)
        {
            var history = new SynthesisHistory();
            var model = JsonSerializer.Deserialize<FileModel>(json, JsonOptions);
            if (model?.Entries == null) return history;
            foreach (var m in model.Entries)
            {
                if (m == null || m.Items == null || m.Counts == null || m.Items.Count == 0 || m.Items.Count != m.Counts.Count) continue;
                var ingredients = new List<SynthesisIngredient>(m.Items.Count);
                for (int i = 0; i < m.Items.Count; i++) ingredients.Add(new SynthesisIngredient(m.Items[i], m.Counts[i]));
                if (history._entries.Count < MaxEntries) history._entries.Add(new SynthesisHistoryEntry(m.Crystal, ingredients, m.Result, m.Favorite));
            }
            return history;
        }

        public void SaveToFile(string path)
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, ToJson());
        }

        /// <summary>Loads a history file, or an empty history when it is missing or unreadable.</summary>
        public static SynthesisHistory LoadOrEmpty(string path)
        {
            if (File.Exists(path))
            {
                try
                {
                    return FromJson(File.ReadAllText(path));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
                {
                    GordianLog.Warning("CRAFT", $"Failed to load the synthesis history '{path}': {ex.Message}. Starting with an empty history.");
                }
            }
            return new SynthesisHistory();
        }

        public static string DataDirectory => Path.Combine(GordianStorage.RootDataDirectory, "synthesis_history");

        public static string GetPath(string? characterName)
        {
            string name = string.IsNullOrWhiteSpace(characterName) ? "default" : characterName.Trim();
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return Path.Combine(DataDirectory, name + ".json");
        }

        #endregion
    }

    /// <summary>One <see cref="SynthesisHistory"/> per character, loaded on first use and saved whenever it changes.</summary>
    public static class SynthesisHistoryStore
    {
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SynthesisHistory> Histories = new(StringComparer.OrdinalIgnoreCase);

        public static SynthesisHistory GetForCharacter(string? characterName)
        {
            string key = string.IsNullOrWhiteSpace(characterName) ? string.Empty : characterName.Trim();
            return Histories.GetOrAdd(key, name =>
            {
                string path = SynthesisHistory.GetPath(name);
                var history = SynthesisHistory.LoadOrEmpty(path);
                history.Changed += () =>
                {
                    try
                    {
                        history.SaveToFile(path);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        GordianLog.Warning("CRAFT", $"Could not save the synthesis history '{path}': {ex.Message}");
                    }
                };
                return history;
            });
        }
    }
}
