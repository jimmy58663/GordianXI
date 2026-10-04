// src/Gordian.Core/Resources/ItemNameResolver.cs
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO;

namespace Gordian.Core.Resources
{
    /// <summary>
    /// Clean-room DAT item string table parser and resolver.
    /// Provides high-performance, cached item name lookups directly from client DAT files.
    /// Format specification referenced from community DAT format research and xi-model-viewer (https://github.com/vekien/xi-model-viewer).
    /// </summary>
    public static class ItemNameResolver
    {
        private static readonly ConcurrentDictionary<ushort, string> _cache = new();
        private static string? _gameDirectory;

        static ItemNameResolver()
        {
            // Item names decode through DMsgStringTable now; the code page provider stays registered here as before,
            // since other Shift-JIS readers (chat) may have relied on this type registering it first.
            try { System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance); }
            catch { }
        }

        public static void Initialize(string? gameDirectory)
        {
            _gameDirectory = gameDirectory;
        }

        /// <summary>
        /// Resolves an item's in-game display name from its 16-bit Item ID.
        /// </summary>
        public static string Resolve(ushort itemId)
        {
            if (itemId == 0) return string.Empty;
            if (itemId == 65535) return "Gil";

            if (_cache.TryGetValue(itemId, out var cachedName))
            {
                return cachedName;
            }

            string? parsed = TryParseFromDat(itemId);
            string result = !string.IsNullOrWhiteSpace(parsed) ? parsed : $"Item 0x{itemId:X4} ({itemId})";
            _cache[itemId] = result;
            return result;
        }

        private static string? TryParseFromDat(ushort itemId)
        {
            if (string.IsNullOrWhiteSpace(_gameDirectory) || !Directory.Exists(_gameDirectory))
            {
                return null;
            }

            // Item tables shared with ResourceManager (#203).
            if (Tables.ItemTables.TryGetRange(itemId, out var entry))
            {
                string fullPath = Path.Combine(_gameDirectory, entry.RelativePath);
                if (!File.Exists(fullPath)) return null;

                try
                {
                    using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    int stride = stream.Length % 5120 == 0 ? 5120 : 3072;
                    long blockIndex = itemId - entry.StartId;
                    long offset = (long)blockIndex * stride;

                    if (offset + stride > stream.Length) return null;

                    byte[] buffer = ArrayPool<byte>.Shared.Rent(stride);
                    try
                    {
                        stream.Seek(offset, SeekOrigin.Begin);
                        int read = stream.Read(buffer, 0, stride);
                        if (read < stride) return null;

                        // Clean-room cipher inversion: rotate each byte left by 3
                        for (int i = 0; i < stride; i++)
                        {
                            byte b = buffer[i];
                            buffer[i] = (byte)((b << 3) | (b >> 5));
                        }

                        uint readId = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(0, 4));
                        if (readId != itemId) return null;

                        // The same string-block reader as the item cache (ItemTableDecoder), so both give one name (#203).
                        string name = Tables.ItemTableDecoder.DecodeName(buffer.AsSpan(0, stride));
                        return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(buffer);
                    }
                }
                catch
                {
                    return null;
                }
            }

            return null;
        }
    }
}
