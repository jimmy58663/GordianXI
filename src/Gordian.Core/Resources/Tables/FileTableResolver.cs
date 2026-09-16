// src/Gordian.Core/Resources/Tables/FileTableResolver.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Gordian.Core.Resources.Tables
{
    /// <summary>
    /// File table entry mapping a numeric File ID to a physical ROM DAT file.
    /// </summary>
    public readonly record struct FileTableEntry(int FileId, byte RomIndex, ushort SubDir, byte FileNum, string RelativePath)
    {
        public override string ToString() => $"[ID: {FileId}] -> {RelativePath}";
    }

    /// <summary>
    /// Parses FFXI FTABLE and VTABLE index files to resolve numeric client File IDs
    /// to game directory paths (e.g. ROM/118/106.DAT).
    /// Derived from community research in xi-model-viewer (https://github.com/vekien/xi-model-viewer)
    /// and xi-tools (xi/ftable/xi_core.py).
    /// </summary>
    public sealed class FileTableResolver
    {
        private readonly Dictionary<int, FileTableEntry> _entries = new();

        public int Count => _entries.Count;

        /// <summary>
        /// Registers or replaces mappings from a pair of FTABLE and VTABLE buffers.
        /// </summary>
        /// <param name="ftableBytes">FTABLE binary buffer (ushort per file ID).</param>
        /// <param name="vtableBytes">VTABLE binary buffer (byte per file ID indicating ROM root).</param>
        public void LoadTablePair(ReadOnlySpan<byte> ftableBytes, ReadOnlySpan<byte> vtableBytes)
        {
            int capacity = Math.Min(ftableBytes.Length / 2, vtableBytes.Length);

            for (int id = 0; id < capacity; id++)
            {
                byte rom = vtableBytes[id];
                if (rom == 0)
                {
                    // 0 = unregistered
                    continue;
                }

                ushort ftVal = BinaryPrimitives.ReadUInt16LittleEndian(ftableBytes.Slice(id * 2, 2));
                ushort subDir = (ushort)(ftVal >> 7);
                byte fileNum = (byte)(ftVal & 0x7F);

                string romRoot = rom == 1 ? "ROM" : $"ROM{rom}";
                string relativePath = Path.Combine(romRoot, subDir.ToString(), $"{fileNum}.DAT");

                _entries[id] = new FileTableEntry(id, rom, subDir, fileNum, relativePath);
            }
        }

        /// <summary>
        /// Attempts to resolve a numeric File ID to its canonical relative game path.
        /// </summary>
        public bool TryResolve(int fileId, out string relativePath)
        {
            if (_entries.TryGetValue(fileId, out var entry))
            {
                relativePath = entry.RelativePath;
                return true;
            }

            relativePath = string.Empty;
            return false;
        }

        /// <summary>
        /// Attempts to get the full file table entry for a file ID.
        /// </summary>
        public bool TryGetEntry(int fileId, out FileTableEntry entry)
        {
            return _entries.TryGetValue(fileId, out entry);
        }

        /// <summary>
        /// Clears all loaded table mappings.
        /// </summary>
        public void Clear()
        {
            _entries.Clear();
        }
    }
}
