// src/Gordian.Core/Network/Compression/IFfxiCompressionTableProvider.cs
using System;
using System.IO;
using System.Reflection;

namespace Gordian.Core.Network.Compression
{
    /// <summary>
    /// Provides the 2048-byte compress.dat and 10224-byte decompress.dat lookup tables
    /// required by the FFXI custom bitstream codec.
    /// </summary>
    public interface IFfxiCompressionTableProvider
    {
        /// <summary>
        /// Gets the 512 uint32s of the compression table (2048 bytes).
        /// </summary>
        uint[] GetCompressTable();

        /// <summary>
        /// Gets the 2556 uint32s of the decompression table (10224 bytes).
        /// </summary>
        uint[] GetDecompressTable();
    }

    /// <summary>
    /// Default provider that loads compression tables from embedded assembly resources.
    /// </summary>
    public sealed class EmbeddedCompressionTableProvider : IFfxiCompressionTableProvider
    {
        private static readonly Lazy<EmbeddedCompressionTableProvider> _instance =
            new(() => new EmbeddedCompressionTableProvider());

        public static EmbeddedCompressionTableProvider Instance => _instance.Value;

        private readonly uint[] _compressTable;
        private readonly uint[] _decompressTable;

        public EmbeddedCompressionTableProvider()
        {
            var assembly = typeof(EmbeddedCompressionTableProvider).Assembly;

            using (var stream = assembly.GetManifestResourceStream("Gordian.Core.Resources.compress.dat"))
            {
                if (stream == null)
                {
                    throw new InvalidOperationException("Embedded resource 'Gordian.Core.Resources.compress.dat' not found.");
                }

                _compressTable = ReadUInt32Array(stream, 2048 / 4);
            }

            using (var stream = assembly.GetManifestResourceStream("Gordian.Core.Resources.decompress.dat"))
            {
                if (stream == null)
                {
                    throw new InvalidOperationException("Embedded resource 'Gordian.Core.Resources.decompress.dat' not found.");
                }

                _decompressTable = ReadUInt32Array(stream, 10224 / 4);
            }
        }

        private static uint[] ReadUInt32Array(Stream stream, int count)
        {
            byte[] bytes = new byte[count * 4];
            int read = 0;
            while (read < bytes.Length)
            {
                int r = stream.Read(bytes, read, bytes.Length - read);
                if (r == 0) break;
                read += r;
            }

            if (read != bytes.Length)
            {
                throw new EndOfStreamException($"Expected {bytes.Length} bytes but only read {read}.");
            }

            uint[] result = new uint[count];
            Buffer.BlockCopy(bytes, 0, result, 0, bytes.Length);
            return result;
        }

        public uint[] GetCompressTable() => _compressTable;
        public uint[] GetDecompressTable() => _decompressTable;
    }

    /// <summary>
    /// Optional provider that loads tables from specified disk file paths (e.g. from a legacy client installation).
    /// </summary>
    public sealed class FileCompressionTableProvider : IFfxiCompressionTableProvider
    {
        private readonly uint[] _compressTable;
        private readonly uint[] _decompressTable;

        public FileCompressionTableProvider(string compressDatPath, string decompressDatPath)
        {
            if (!File.Exists(compressDatPath))
            {
                throw new FileNotFoundException($"Compression table not found at '{compressDatPath}'.", compressDatPath);
            }
            if (!File.Exists(decompressDatPath))
            {
                throw new FileNotFoundException($"Decompression table not found at '{decompressDatPath}'.", decompressDatPath);
            }

            _compressTable = ReadFileUInt32Array(compressDatPath, 2048 / 4);
            _decompressTable = ReadFileUInt32Array(decompressDatPath, 10224 / 4);
        }

        private static uint[] ReadFileUInt32Array(string path, int count)
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length != count * 4)
            {
                throw new InvalidDataException($"Expected {count * 4} bytes from '{path}' but got {bytes.Length}.");
            }

            uint[] result = new uint[count];
            Buffer.BlockCopy(bytes, 0, result, 0, bytes.Length);
            return result;
        }

        public uint[] GetCompressTable() => _compressTable;
        public uint[] GetDecompressTable() => _decompressTable;
    }
}
