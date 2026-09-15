// src/Gordian.Core/Resources/ItemNameResolver.cs
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO;
using System.Text;

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
        private static Encoding? _shiftJisEncoding;

        private static readonly (ushort StartId, ushort EndId, string RelPath)[] TableDats = new[]
        {
            ((ushort)0,     (ushort)4095,  Path.Combine("ROM", "118", "106.DAT")), // General 1
            ((ushort)4096,  (ushort)8191,  Path.Combine("ROM", "118", "107.DAT")), // Usable / Consumables
            ((ushort)8192,  (ushort)8703,  Path.Combine("ROM", "118", "110.DAT")), // Puppet / Automaton
            ((ushort)8704,  (ushort)10239, Path.Combine("ROM", "301", "115.DAT")), // General 2
            ((ushort)10240, (ushort)16383, Path.Combine("ROM", "118", "109.DAT")), // Armor 1
            ((ushort)16384, (ushort)23039, Path.Combine("ROM", "118", "108.DAT")), // Weapons 1
            ((ushort)23040, (ushort)28671, Path.Combine("ROM", "286", "73.DAT")),  // Armor 2
            ((ushort)28672, (ushort)32767, Path.Combine("ROM", "286", "74.DAT")),  // Weapons 2
        };

        static ItemNameResolver()
        {
            try
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                _shiftJisEncoding = Encoding.GetEncoding("shift_jis");
            }
            catch
            {
                _shiftJisEncoding = null;
            }
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

            for (int t = 0; t < TableDats.Length; t++)
            {
                var entry = TableDats[t];
                if (itemId < entry.StartId || itemId > entry.EndId) continue;

                string fullPath = Path.Combine(_gameDirectory, entry.RelPath);
                if (!File.Exists(fullPath)) return null;

                try
                {
                    using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    int stride = stream.Length % 5120 == 0 ? 5120 : 3072;
                    int blockIndex = itemId - entry.StartId;
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

                        // Locate string table between 0x08 and 0x80
                        for (int off = 0x08; off <= 0x80; off += 2)
                        {
                            uint n = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(off, 4));
                            if (n < 1 || n > 8) continue;

                            uint first = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(off + 4, 4));
                            if (first != 4 + n * 8) continue;

                            uint strOff = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(off + 4, 4));
                            int textStart = (int)(off + strOff + 0x1C);
                            if (textStart >= stride) break;

                            int textLen = 0;
                            while (textStart + textLen < stride && buffer[textStart + textLen] != 0)
                            {
                                textLen++;
                            }

                            if (textLen > 0)
                            {
                                string name = DecodeString(buffer.AsSpan(textStart, textLen));
                                if (!string.IsNullOrWhiteSpace(name))
                                {
                                    return name.Trim();
                                }
                            }
                            break;
                        }
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

        private static string DecodeString(ReadOnlySpan<byte> bytes)
        {
            if (_shiftJisEncoding != null)
            {
                try
                {
                    return _shiftJisEncoding.GetString(bytes);
                }
                catch
                {
                    // Fallback to ASCII
                }
            }

            // Fallback: ASCII filtering
            var sb = new StringBuilder(bytes.Length);
            for (int i = 0; i < bytes.Length; i++)
            {
                byte b = bytes[i];
                if (b >= 0x20 && b <= 0x7E)
                {
                    sb.Append((char)b);
                }
            }
            return sb.ToString();
        }
    }
}
