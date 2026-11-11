// src/Gordian.Core/Resources/Tables/RecordsOfEminenceTables.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Gordian.Core.Resources.Tables
{
    /// <summary>One Records of Eminence objective from the client's table.</summary>
    /// <param name="Id">The objective id C2S 0x10C / 0x10D and S2C 0x111 carry (the record's id less 57344, 0-4095).</param>
    /// <param name="Name">Its name ("Vanquish Multiple Enemies I").</param>
    /// <param name="Description">Its text ("Defeat the requisite number of experience-yielding enemies ...").</param>
    /// <param name="Required">The progress it needs to complete (200 for Vanquish Multiple Enemies I; 1 for most).</param>
    /// <param name="Repeatable">Whether it can be taken again once completed.</param>
    /// <param name="Sparks">The sparks of eminence it rewards.</param>
    /// <param name="Experience">The experience points it rewards.</param>
    /// <param name="Accolades">The Unity accolades it rewards.</param>
    public sealed record RoeObjective(int Id, string Name, string Description, int Required, bool Repeatable, int Sparks, int Experience, int Accolades);

    /// <summary>A condition on a category's child: its kind (the word's top byte) and value (the low 24 bits).</summary>
    public readonly record struct RoeCondition(byte Kind, int Value)
    {
        /// <summary>Kind 1: the objective <see cref="Value"/> must be completed (the S2C 0x112 bit) for the child to show.</summary>
        public const byte ObjectiveCompleted = 1;

        /// <summary>Kind 4: the zone <see cref="Value"/> must have been visited (S2C 0x008's table).</summary>
        public const byte ZoneVisited = 4;

        /// <summary>
        /// Kind 6: the Unity rotation: 0-5 S2C 0x110 <c>RoEUnityShared</c> must be <see cref="Value"/>, 0x100-0x103
        /// <c>RoEUnityLeader</c> must be <see cref="Value"/> less <see cref="LeaderRotationBase"/>.
        /// </summary>
        public const byte UnityRotation = 6;

        /// <summary>Kind 7: the character's Unity leader (1-11) must be <see cref="Value"/>.</summary>
        public const byte UnityLeader = 7;

        /// <summary>Kind 6's values from here on are the leader rotation.</summary>
        public const int LeaderRotationBase = 0x100;
    }

    /// <summary>One child of a category: a subcategory (an id of 0xF2xx) or an objective (0xE000 + objective id).</summary>
    public readonly record struct RoeChild(int RecordId, IReadOnlyList<RoeCondition> Conditions)
    {
        public bool IsObjective => RecordId >= RecordsOfEminenceTables.FirstObjectiveRecord && RecordId < RecordsOfEminenceTables.FirstCategoryRecord;

        /// <summary>The objective id (the record id less 57344); -1 for a subcategory.</summary>
        public int ObjectiveId => IsObjective ? RecordId - RecordsOfEminenceTables.FirstObjectiveRecord : -1;
    }

    /// <summary>A Records of Eminence category ("Tutorial", "Combat (Wide Area)" ...) with its children in the client's order.</summary>
    public sealed record RoeCategory(int RecordId, string Name, IReadOnlyList<RoeChild> Children);

    /// <summary>
    /// The client's Records of Eminence tables (#493): the objectives (ROM/307/16, file id 0xD978, records 57344-61431) and the
    /// category tree (ROM/307/24, file id 0xD979, records 61952-62975). Both files are 0x1400-byte records rotated as the item
    /// tables are (<see cref="ItemTableDecoder.DecryptBlock"/>), each starting with its u32 record id. Files named by xi-tools
    /// (docs/reference/named-dats.md); the layouts below were read from the retail files on 2026-11-03. **Beyond xi-tools:**
    /// it names the files only.
    /// <list type="bullet">
    /// <item>Objective: 0x04 the date it was added (yyyymmdd, 20131211), 0x08 repeatable (1; matches LandSandBoat's
    /// <c>repeat</c> flags), 0x0C the progress needed (Vanquish Multiple Enemies I/II/III: 200 / 500 / 750, LSB's
    /// <c>goal</c>), 0x10 sparks, 0x14 experience (LSB's <c>reward</c>), 0x1C Unity accolades (All for One: 1,000); a
    /// string table at 0x20 (u32 count, then u32 offset / u32 kind pairs from the table's start; a string at offset + 0x1C):
    /// [0] the name, [4] the description. Unused records are named ".".</item>
    /// <item>Category: 0x04 the child count, 0x08 up to 28 children of 20 bytes (u32 record id, four u32 conditions: kind in the
    /// top byte, value below: kind 1 an objective completed, 4 a zone visited, 6 the Unity rotation, 7 the Unity leader, see
    /// <see cref="RoeCondition"/>; read from the retail file and matched to XiPackets' S2C 0x008 and 0x110 fields, 2026-11-04),
    /// a string table at 0x238: [0] the name.
    /// Record 61952 (0xF200) is the root: its children are the top categories (Tutorial ... Other; twelve since retail patch
    /// 30261102_0 added Trust Ascension).</item>
    /// </list>
    /// </summary>
    public sealed class RecordsOfEminenceTables
    {
        public const int FirstObjectiveRecord = 0xE000, FirstCategoryRecord = 0xF200, RootCategory = 0xF200;
        public const int ObjectiveFileId = 0xD978, CategoryFileId = 0xD979;

        /// <summary>The paths of the English client's tables, for loading by path when the file table is not read.</summary>
        public static readonly string ObjectivePath = Path.Combine("ROM", "307", "16.DAT"), CategoryPath = Path.Combine("ROM", "307", "24.DAT");

        private const int RecordSize = 0x1400, ObjectiveStrings = 0x20, CategoryChildren = 0x08, ChildSize = 20, MaxChildren = 28;
        private const int CategoryStrings = CategoryChildren + MaxChildren * ChildSize;

        private readonly Dictionary<int, RoeObjective> _objectives;
        private readonly Dictionary<int, RoeCategory> _categories;

        private RecordsOfEminenceTables(Dictionary<int, RoeObjective> objectives, Dictionary<int, RoeCategory> categories)
        {
            _objectives = objectives;
            _categories = categories;
        }

        public int ObjectiveCount => _objectives.Count;

        public int CategoryCount => _categories.Count;

        /// <summary>An objective by objective id (0-4095), or null for an unused one.</summary>
        public RoeObjective? Objective(int id) => _objectives.TryGetValue(id, out var o) ? o : null;

        /// <summary>A category by record id (0xF2xx), or null.</summary>
        public RoeCategory? Category(int recordId) => _categories.TryGetValue(recordId, out var c) ? c : null;

        /// <summary>The top categories (the root's children), in the client's order.</summary>
        public IReadOnlyList<RoeCategory> TopCategories()
        {
            var list = new List<RoeCategory>();
            if (Category(RootCategory) is not { } root) return list;
            foreach (var child in root.Children)
            {
                if (Category(child.RecordId) is { } c) list.Add(c);
            }
            return list;
        }

        /// <summary>Parses both files; null when either is missing or holds no record.</summary>
        public static RecordsOfEminenceTables? Parse(ReadOnlySpan<byte> objectives, ReadOnlySpan<byte> categories)
        {
            var objectiveMap = new Dictionary<int, RoeObjective>();
            var categoryMap = new Dictionary<int, RoeCategory>();
            var block = new byte[RecordSize];
            for (int at = 0; at + RecordSize <= objectives.Length; at += RecordSize)
            {
                ItemTableDecoder.DecryptBlock(objectives.Slice(at, RecordSize), block);
                int record = BinaryPrimitives.ReadInt32LittleEndian(block);
                string name = ReadString(block, ObjectiveStrings, 0);
                if (record < FirstObjectiveRecord || record >= FirstCategoryRecord || name.Length == 0 || name == ".") continue;
                int id = record - FirstObjectiveRecord;
                objectiveMap[id] = new RoeObjective(id, name, ReadString(block, ObjectiveStrings, 4),
                    Math.Max(1, BinaryPrimitives.ReadInt32LittleEndian(block.AsSpan(0x0C))),
                    BinaryPrimitives.ReadInt32LittleEndian(block.AsSpan(0x08)) != 0,
                    BinaryPrimitives.ReadInt32LittleEndian(block.AsSpan(0x10)),
                    BinaryPrimitives.ReadInt32LittleEndian(block.AsSpan(0x14)),
                    BinaryPrimitives.ReadInt32LittleEndian(block.AsSpan(0x1C)));
            }
            for (int at = 0; at + RecordSize <= categories.Length; at += RecordSize)
            {
                ItemTableDecoder.DecryptBlock(categories.Slice(at, RecordSize), block);
                int record = BinaryPrimitives.ReadInt32LittleEndian(block);
                int count = BinaryPrimitives.ReadInt32LittleEndian(block.AsSpan(4));
                if (record < FirstCategoryRecord || count <= 0) continue;
                var children = new List<RoeChild>(Math.Min(count, MaxChildren));
                for (int i = 0; i < Math.Min(count, MaxChildren); i++)
                {
                    int c = CategoryChildren + i * ChildSize;
                    var conditions = new List<RoeCondition>(4);
                    for (int k = 0; k < 4; k++)
                    {
                        uint word = BinaryPrimitives.ReadUInt32LittleEndian(block.AsSpan(c + 4 + k * 4));
                        if (word != 0) conditions.Add(new RoeCondition((byte)(word >> 24), (int)(word & 0xFFFFFF)));
                    }
                    children.Add(new RoeChild(BinaryPrimitives.ReadInt32LittleEndian(block.AsSpan(c)), conditions));
                }
                categoryMap[record] = new RoeCategory(record, record == RootCategory ? string.Empty : ReadString(block, CategoryStrings, 0), children);
            }
            return objectiveMap.Count > 0 && categoryMap.ContainsKey(RootCategory) ? new RecordsOfEminenceTables(objectiveMap, categoryMap) : null;
        }

        /// <summary>Loads both tables by path through <paramref name="load"/>; null when either is missing.</summary>
        public static RecordsOfEminenceTables? Load(Func<string, byte[]?> load)
        {
            var objectives = load(ObjectivePath);
            var categories = load(CategoryPath);
            return objectives == null || categories == null ? null : Parse(objectives, categories);
        }

        /// <summary>String <paramref name="index"/> of the table at <paramref name="table"/>; empty when absent or a number.</summary>
        private static string ReadString(byte[] block, int table, int index)
        {
            int count = BinaryPrimitives.ReadInt32LittleEndian(block.AsSpan(table));
            if (index < 0 || index >= count || table + 12 + index * 8 > block.Length) return string.Empty;
            int offset = BinaryPrimitives.ReadInt32LittleEndian(block.AsSpan(table + 4 + index * 8));
            int kind = BinaryPrimitives.ReadInt32LittleEndian(block.AsSpan(table + 8 + index * 8));
            int start = table + offset + 0x1C;
            if (kind != 0 || offset <= 0 || start >= block.Length) return string.Empty;
            int end = start;
            while (end < block.Length && block[end] != 0) end++;
            return Encoding.Latin1.GetString(block, start, end - start).Replace('\n', ' ').Trim();
        }
    }
}
