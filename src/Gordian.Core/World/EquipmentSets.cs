// src/Gordian.Core/World/EquipmentSets.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Gordian.Core.Config;
using Gordian.Core.Diagnostics;
using Gordian.Core.Network.Packets;

namespace Gordian.Core.World
{
    /// <summary>What one slot of an equipment set holds.</summary>
    public enum EquipmentSetPieceKind : byte
    {
        /// <summary>Nothing: equipping the set leaves the slot as it is.</summary>
        Empty = 0,

        /// <summary>An item, found by its container, index and id.</summary>
        Item = 1,

        /// <summary>"Remove piece" (retail's red X): equipping the set takes off whatever the slot holds.</summary>
        Remove = 2,
    }

    /// <summary>
    /// One slot of an equipment set: an item (the container and index it was in when it was put in the set, and its id) or
    /// a "Remove piece" mark. The same fields as XiPackets' <c>equipsetrequestitem_t</c> (C2S 0x052, S2C 0x116), which is
    /// also how the retail client stores a slot in its <c>es&lt;n&gt;.dat</c> files (<c>RetailEquipSets</c>).
    /// Structure referenced from XiPackets (https://github.com/atom0s/XiPackets), world/client 0x0052.
    /// </summary>
    public readonly record struct EquipmentSetPiece(EquipmentSetPieceKind Kind, ContainerId Container, byte Index, ushort ItemId)
    {
        public static EquipmentSetPiece Empty => default;

        public static EquipmentSetPiece Remove => new(EquipmentSetPieceKind.Remove, ContainerId.Inventory, 0, 0);

        public static EquipmentSetPiece Item(ContainerId container, byte index, ushort itemId) => new(EquipmentSetPieceKind.Item, container, index, itemId);

        public bool HasItem => Kind == EquipmentSetPieceKind.Item && ItemId != 0;

        /// <summary>The C2S 0x052 / S2C 0x116 form of the slot.</summary>
        public EquipsetRequestItem ToRequestItem() => new(HasItem, Kind == EquipmentSetPieceKind.Remove, Container, HasItem ? Index : (byte)0, HasItem ? ItemId : (ushort)0);

        /// <summary>A slot from its C2S 0x052 / S2C 0x116 form: an item when <c>HasItemFlg</c> is set, else "Remove piece" when <c>RemoveItemFlg</c> is.</summary>
        public static EquipmentSetPiece FromRequestItem(bool hasItem, bool remove, ContainerId container, byte index, ushort itemId) =>
            hasItem && itemId != 0 ? Item(container, index, itemId) : remove ? Remove : Empty;
    }

    /// <summary>
    /// One equipment set: a name and the sixteen equipment slots in <see cref="EquipSlotId"/> order (main, sub, range,
    /// ammo, head, body, hands, legs, feet, neck, waist, ears, rings, back). Immutable; the <c>With</c> methods return a
    /// changed copy.
    /// </summary>
    public sealed class EquipmentSet
    {
        public const int SlotCount = 16;

        /// <summary>The name field is 16 bytes in the retail files, NUL-terminated (<c>RetailEquipSets</c>).</summary>
        public const int MaxNameLength = 15;

        private readonly EquipmentSetPiece[] _pieces;

        public EquipmentSet(string name, IReadOnlyList<EquipmentSetPiece>? pieces = null)
        {
            Name = ClampName(name);
            _pieces = new EquipmentSetPiece[SlotCount];
            if (pieces != null)
            {
                for (int i = 0; i < SlotCount && i < pieces.Count; i++) _pieces[i] = pieces[i];
            }
        }

        public string Name { get; }

        public IReadOnlyList<EquipmentSetPiece> Pieces => _pieces;

        public EquipmentSetPiece this[EquipSlotId slot] => (int)slot < SlotCount ? _pieces[(int)slot] : default;

        /// <summary>Whether no slot holds an item or a "Remove piece" mark.</summary>
        public bool IsEmpty
        {
            get
            {
                foreach (var piece in _pieces)
                {
                    if (piece.Kind != EquipmentSetPieceKind.Empty) return false;
                }
                return true;
            }
        }

        public EquipmentSet WithName(string name) => new(name, _pieces);

        public EquipmentSet WithPiece(EquipSlotId slot, EquipmentSetPiece piece)
        {
            if ((int)slot >= SlotCount) return this;
            var pieces = (EquipmentSetPiece[])_pieces.Clone();
            pieces[(int)slot] = piece;
            return new EquipmentSet(Name, pieces);
        }

        public EquipmentSet WithPieces(IReadOnlyList<EquipmentSetPiece> pieces) => new(Name, pieces);

        /// <summary>The default name of set <paramref name="number"/>: "Set001" ... (as the retail files hold for unnamed sets).</summary>
        public static string DefaultName(int number) => "Set" + number.ToString("D3", CultureInfo.InvariantCulture);

        /// <summary>An empty set with its default name.</summary>
        public static EquipmentSet Blank(int number) => new(DefaultName(number));

        private static string ClampName(string? name)
        {
            name = (name ?? string.Empty).Trim();
            return name.Length > MaxNameLength ? name[..MaxNameLength] : name;
        }
    }

    /// <summary>
    /// The character's equipment sets (#92): <see cref="SetCount"/> sets in pages of <see cref="SetsPerPage"/>, as the
    /// retail client keeps them client-side (the server stores none: C2S 0x051 and 0x053 carry the items). Edited by the
    /// Equipment Sets windows and used by <c>/equipset</c> and <c>/lockstyleset</c>; saved to
    /// <c>equipment_sets/&lt;character&gt;.json</c> under the GordianXI data folder whenever it changes.
    /// </summary>
    public sealed class EquipmentSetBook
    {
        /// <summary>
        /// Sets in all: ten pages of twenty (the <c>mcres20</c> window lists twenty, and each retail <c>es&lt;n&gt;.dat</c> holds
        /// twenty). PROVISIONAL: only <c>es0.dat</c> was seen in the maintainer's install, so the page count is not confirmed.
        /// </summary>
        public const int SetCount = 200, SetsPerPage = 20, PageCount = SetCount / SetsPerPage;

        private readonly object _sync = new();
        private readonly EquipmentSet?[] _sets = new EquipmentSet?[SetCount];

        /// <summary>Raised with the set number (1-based) after a set changes.</summary>
        public event Action<int>? Changed;

        public static bool IsValidNumber(int number) => number >= 1 && number <= SetCount;

        /// <summary>Set <paramref name="number"/> (1-based); an untouched set is empty with its default name.</summary>
        public EquipmentSet Get(int number)
        {
            if (!IsValidNumber(number)) throw new ArgumentOutOfRangeException(nameof(number));
            lock (_sync) return _sets[number - 1] ?? EquipmentSet.Blank(number);
        }

        public void Set(int number, EquipmentSet set)
        {
            if (!IsValidNumber(number)) throw new ArgumentOutOfRangeException(nameof(number));
            ArgumentNullException.ThrowIfNull(set);
            lock (_sync) _sets[number - 1] = set;
            Changed?.Invoke(number);
        }

        /// <summary>Empties set <paramref name="number"/> and gives it back its default name (retail's Delete).</summary>
        public void Clear(int number) => Set(number, EquipmentSet.Blank(number));

        /// <summary>Replaces sets without raising <see cref="Changed"/> for each (an import); raises it once with 0.</summary>
        public void SetMany(IEnumerable<(int Number, EquipmentSet Set)> sets)
        {
            lock (_sync)
            {
                foreach (var (number, set) in sets)
                {
                    if (IsValidNumber(number) && set != null) _sets[number - 1] = set;
                }
            }
            Changed?.Invoke(0);
        }

        #region Persistence

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() },
        };

        private sealed class FileModel
        {
            public int Version { get; set; } = 1;
            public List<SetModel> Sets { get; set; } = new();
        }

        private sealed class SetModel
        {
            public int Number { get; set; }
            public string Name { get; set; } = string.Empty;
            public List<PieceModel> Pieces { get; set; } = new();
        }

        private sealed class PieceModel
        {
            public EquipSlotId Slot { get; set; }
            public EquipmentSetPieceKind Kind { get; set; }
            public ContainerId Container { get; set; }
            public byte Index { get; set; }
            public ushort Item { get; set; }
        }

        /// <summary>The book as JSON: only the sets that differ from a blank one.</summary>
        public string ToJson()
        {
            var model = new FileModel();
            lock (_sync)
            {
                for (int i = 0; i < SetCount; i++)
                {
                    var set = _sets[i];
                    if (set == null || (set.IsEmpty && set.Name == EquipmentSet.DefaultName(i + 1))) continue;
                    var setModel = new SetModel { Number = i + 1, Name = set.Name };
                    for (int slot = 0; slot < EquipmentSet.SlotCount; slot++)
                    {
                        var piece = set.Pieces[slot];
                        if (piece.Kind == EquipmentSetPieceKind.Empty) continue;
                        setModel.Pieces.Add(new PieceModel { Slot = (EquipSlotId)slot, Kind = piece.Kind, Container = piece.Container, Index = piece.Index, Item = piece.ItemId });
                    }
                    model.Sets.Add(setModel);
                }
            }
            return JsonSerializer.Serialize(model, JsonOptions);
        }

        /// <summary>Reads a book from <see cref="ToJson"/>'s form; sets out of range and slots past the sixteenth are skipped.</summary>
        public static EquipmentSetBook FromJson(string json)
        {
            var book = new EquipmentSetBook();
            var model = JsonSerializer.Deserialize<FileModel>(json, JsonOptions);
            if (model?.Sets == null) return book;
            foreach (var setModel in model.Sets)
            {
                if (setModel == null || !IsValidNumber(setModel.Number)) continue;
                var pieces = new EquipmentSetPiece[EquipmentSet.SlotCount];
                foreach (var p in setModel.Pieces ?? new List<PieceModel>())
                {
                    if (p == null || (int)p.Slot >= EquipmentSet.SlotCount) continue;
                    pieces[(int)p.Slot] = p.Kind switch
                    {
                        EquipmentSetPieceKind.Item when p.Item != 0 => EquipmentSetPiece.Item(p.Container, p.Index, p.Item),
                        EquipmentSetPieceKind.Remove => EquipmentSetPiece.Remove,
                        _ => EquipmentSetPiece.Empty,
                    };
                }
                book._sets[setModel.Number - 1] = new EquipmentSet(setModel.Name ?? EquipmentSet.DefaultName(setModel.Number), pieces);
            }
            return book;
        }

        public void SaveToFile(string path)
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, ToJson());
        }

        /// <summary>Loads a book file, or an empty book when it is missing or unreadable.</summary>
        public static EquipmentSetBook LoadOrEmpty(string path)
        {
            if (File.Exists(path))
            {
                try
                {
                    return FromJson(File.ReadAllText(path));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
                {
                    GordianLog.Warning("EQUIPSET", $"Failed to load equipment sets '{path}': {ex.Message}. Starting with empty sets.");
                }
            }
            return new EquipmentSetBook();
        }

        public static string DataDirectory => Path.Combine(GordianStorage.RootDataDirectory, "equipment_sets");

        public static string GetPath(string? characterName)
        {
            string name = string.IsNullOrWhiteSpace(characterName) ? "default" : characterName.Trim();
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return Path.Combine(DataDirectory, name + ".json");
        }

        #endregion
    }

    /// <summary>One <see cref="EquipmentSetBook"/> per character, loaded on first use and saved whenever a set changes.</summary>
    public static class EquipmentSetStore
    {
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, EquipmentSetBook> Books = new(StringComparer.OrdinalIgnoreCase);

        public static EquipmentSetBook GetForCharacter(string? characterName)
        {
            string key = string.IsNullOrWhiteSpace(characterName) ? string.Empty : characterName.Trim();
            return Books.GetOrAdd(key, name =>
            {
                string path = EquipmentSetBook.GetPath(name);
                var book = EquipmentSetBook.LoadOrEmpty(path);
                book.Changed += _ =>
                {
                    try
                    {
                        book.SaveToFile(path);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        GordianLog.Warning("EQUIPSET", $"Could not save the equipment sets '{path}': {ex.Message}");
                    }
                };
                return book;
            });
        }
    }
}
