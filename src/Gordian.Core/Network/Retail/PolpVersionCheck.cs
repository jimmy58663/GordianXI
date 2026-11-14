// src/Gordian.Core/Network/Retail/PolpVersionCheck.cs
using System;
using System.Buffers.Binary;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Gordian.Core.Network.Retail
{
    /// <summary>A decoded POLP Type 8 version response.</summary>
    /// <param name="Status">1 (<c>"registered"</c>); retail returns it even for an outdated install, and no other value has been seen.</param>
    /// <param name="StatusText">The status string (<c>"registered"</c>).</param>
    /// <param name="ServerIp">The server's address as it reports it.</param>
    /// <param name="CurrentVersion">The current retail version (<c>"30261006_0"</c>).</param>
    /// <param name="ChecksumValid">Whether the header checksum (<see cref="PolpPackets.Checksum"/>) matched the frame.</param>
    public readonly record struct PolpVersionResponse(uint Status, string StatusText, string ServerIp, string CurrentVersion, bool ChecksumValid)
    {
        /// <summary>
        /// Whether <paramref name="installedVersion"/> is current: <see cref="CurrentVersion"/> names it and the status is 1.
        /// An outdated install still gets status 1 with a newer <see cref="CurrentVersion"/> (spec v2.1.20 section 4.1.5).
        /// </summary>
        public bool IsCurrent(string installedVersion) => Status == 1 && string.Equals(CurrentVersion, installedVersion, StringComparison.Ordinal);
    }

    /// <summary>
    /// POLP frames: the unauthenticated version checks of the PlayOnline Viewer (<c>pt008.pol.com:54000</c>) and of FFXI
    /// (<c>pc001W2U.pol.com:53001</c>). The client sends Type 7 on connect and the server answers Type 8, then closes.
    /// <para>
    /// Protocol referenced from the GordianXI retail login spec, docs/network/retail-login-spec.md v2.1.20, sections 4.1.1-4.1.5
    /// (the header checksum is the first 4 bytes of MD5 over the frame from the magic on, not CRC-32).
    /// </para>
    /// </summary>
    public static class PolpPackets
    {
        public const int HeaderSize = 16;
        public const int VersionRequestSize = 88;
        public const int VersionResponseSize = 103;
        public const uint TypeVersionRequest = 7;
        public const uint TypeVersionResponse = 8;

        /// <summary>The PlayOnline Viewer's product code and check server.</summary>
        public const string ViewerProduct = "W2U", ViewerHost = "pt008.pol.com";
        public const int ViewerPort = 54000;

        /// <summary>The Viewer's product id: its install folder's registry value name and the prefix of its POLP version string.</summary>
        public const string ViewerProductId = "1000";

        /// <summary>FFXI (US, Windows)'s product code and check server.</summary>
        public const string FfxiProduct = "W20", FfxiHost = "pc001W2U.pol.com";
        public const int FfxiPort = 53001;

        /// <summary>FFXI's product id: its install folder's registry value name and the prefix of its POLP version string.</summary>
        public const string FfxiProductId = "0001";

        private static ReadOnlySpan<byte> Magic => "POLP"u8;

        /// <summary>Writes a Type 7 version request: product code at +0x10, the version string at +0x14 (68 bytes).</summary>
        public static void WriteVersionRequest(Span<byte> packet, string product, string version)
        {
            packet = packet[..VersionRequestSize];
            packet.Clear();
            WriteAscii(packet.Slice(0x10, 4), product);
            WriteAscii(packet.Slice(0x14, 68), version);
            WriteHeader(packet, TypeVersionRequest);
        }

        /// <summary>Writes the 16-byte header over a whole frame: size, checksum, magic, type (all little-endian).</summary>
        public static void WriteHeader(Span<byte> frame, uint type)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(frame, (uint)frame.Length);
            Magic.CopyTo(frame[8..]);
            BinaryPrimitives.WriteUInt32LittleEndian(frame[12..], type);
            Checksum(frame[8..], frame.Slice(4, 4));
        }

        /// <summary>
        /// The header checksum: the first 4 bytes of the MD5 of the frame from the magic (+0x08) to its end, copied as they
        /// are (spec section 4.1.1).
        /// </summary>
        public static void Checksum(ReadOnlySpan<byte> fromMagic, Span<byte> destination)
        {
            Span<byte> digest = stackalloc byte[16];
            System.Security.Cryptography.MD5.HashData(fromMagic, digest);
            digest[..4].CopyTo(destination);
        }

        /// <summary>Decodes a Type 8 version response; false when it is not one.</summary>
        public static bool TryParseVersionResponse(ReadOnlySpan<byte> frame, out PolpVersionResponse response)
        {
            response = default;
            if (frame.Length < 0x5C || !frame.Slice(8, 4).SequenceEqual(Magic) ||
                BinaryPrimitives.ReadUInt32LittleEndian(frame[12..]) != TypeVersionResponse)
            {
                return false;
            }
            int size = (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(frame), (uint)frame.Length);
            Span<byte> expected = stackalloc byte[4];
            bool checksumOk = false;
            if (size >= HeaderSize)
            {
                Checksum(frame[8..size], expected);
                checksumOk = frame.Slice(4, 4).SequenceEqual(expected);
            }
            int versionLength = (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(frame[0x58..]), (uint)(frame.Length - 0x5C));
            response = new PolpVersionResponse(
                BinaryPrimitives.ReadUInt32LittleEndian(frame[0x14..]),
                ReadAscii(frame.Slice(0x18, 11)),
                ReadAscii(frame.Slice(0x23, 16)),
                ReadAscii(frame.Slice(0x5C, versionLength)),
                checksumOk);
            return true;
        }

        internal static void WriteAscii(Span<byte> field, string value)
        {
            field.Clear();
            int n = Math.Min(value.Length, field.Length - 1);
            Encoding.ASCII.GetBytes(value.AsSpan(0, n), field);
        }

        internal static string ReadAscii(ReadOnlySpan<byte> field)
        {
            int end = field.IndexOf((byte)0);
            return Encoding.ASCII.GetString(end < 0 ? field : field[..end]);
        }
    }

    /// <summary>
    /// <c>patch.cfg</c>: the plain-text patch manifest in a product's install folder. Every patched file lists one line
    /// per release, each starting with its version tag (<c>YYYYMMDD_N</c> for the Viewer, <c>3YYYMMDD_N</c> for FFXI); the
    /// installed version is the highest tag in the file. The POLP version string is the product id and that version
    /// (<c>"1000"</c> or <c>"0001"</c> first), and the lobby's 0x26 sends FFXI's version alone.
    /// <para>
    /// Format referenced from the GordianXI retail login spec, docs/network/retail-login-spec.md v2.1.19, section 4.1.3
    /// (<c>patch.ver</c> is encrypted and not read).
    /// </para>
    /// </summary>
    public static class PatchConfigFile
    {
        public const string FileName = "patch.cfg";

        /// <summary>The installed version in <paramref name="rootDirectory"/>'s <c>patch.cfg</c>, or null when it is missing or has no tag.</summary>
        public static string? ReadInstalledVersion(string rootDirectory)
        {
            if (string.IsNullOrWhiteSpace(rootDirectory)) return null;
            string path = Path.Combine(rootDirectory, FileName);
            if (!File.Exists(path)) return null;
            using var reader = new StreamReader(path, Encoding.ASCII);
            return HighestTag(reader);
        }

        /// <summary>The highest version tag at the start of a line (ordinal comparison), or null.</summary>
        public static string? HighestTag(TextReader reader)
        {
            string? best = null;
            for (string? line = reader.ReadLine(); line != null; line = reader.ReadLine())
            {
                string tag = ReadTag(line.AsSpan().TrimStart());
                if (tag.Length > 0 && (best == null || string.CompareOrdinal(tag, best) > 0)) best = tag;
            }
            return best;
        }

        /// <summary>The POLP version string for a product: its id, then its installed version.</summary>
        public static string PolpVersion(string productId, string installedVersion) => productId + installedVersion;

        // A tag is 8 digits, '_', then digits.
        private static string ReadTag(ReadOnlySpan<char> line)
        {
            if (line.Length < 10) return string.Empty;
            for (int i = 0; i < 8; i++) if (!char.IsAsciiDigit(line[i])) return string.Empty;
            if (line[8] != '_' || !char.IsAsciiDigit(line[9])) return string.Empty;
            int end = 10;
            while (end < line.Length && char.IsAsciiDigit(line[end])) end++;
            return line[..end].ToString();
        }
    }

    /// <summary>Runs one POLP version check over TCP.</summary>
    public static class PolpVersionChecker
    {
        /// <summary>Connects, sends the Type 7 request and reads the Type 8 answer.</summary>
        public static async Task<PolpVersionResponse> CheckAsync(string host, int port, string product, string version, CancellationToken ct)
        {
            using var client = new TcpClient();
            await client.ConnectAsync(host, port, ct).ConfigureAwait(false);
            var stream = client.GetStream();

            byte[] request = new byte[PolpPackets.VersionRequestSize];
            PolpPackets.WriteVersionRequest(request, product, version);
            await stream.WriteAsync(request, ct).ConfigureAwait(false);

            // Read what the server sends: the header, then up to the size it declares, or until it closes.
            byte[] buffer = new byte[MaxResponse];
            int total = await ReadUpToAsync(stream, buffer, PolpPackets.HeaderSize, ct).ConfigureAwait(false);
            if (total >= PolpPackets.HeaderSize)
            {
                int declared = (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(buffer), (uint)MaxResponse);
                if (declared > total) total += await ReadUpToAsync(stream, buffer.AsMemory(total), declared - total, ct).ConfigureAwait(false);
            }
            var received = buffer.AsSpan(0, total);
            Diagnostics.GordianLog.Debug(Diagnostics.LogCategories.RetailLogin, $"POLP reply from {host}:{port}: {total} bytes: {Convert.ToHexString(received[..Math.Min(total, 160)])}");
            if (!PolpPackets.TryParseVersionResponse(received, out var parsed))
            {
                string type = total >= PolpPackets.HeaderSize ? $"type {BinaryPrimitives.ReadUInt32LittleEndian(received[12..])}" : "no complete header";
                throw new RetailLoginException("version check", $"{host}:{port} answered {total} bytes ({type}), not a POLP version response.");
            }
            return parsed;
        }

        /// <summary>The largest reply read: a version response is 103 bytes; a patch manifest would be larger and is not read.</summary>
        private const int MaxResponse = 4096;

        /// <summary>Reads up to <paramref name="count"/> bytes, stopping early when the server closes; returns how many arrived.</summary>
        private static async Task<int> ReadUpToAsync(NetworkStream stream, Memory<byte> buffer, int count, CancellationToken ct)
        {
            int read = 0;
            while (read < count)
            {
                int n = await stream.ReadAsync(buffer[read..count], ct).ConfigureAwait(false);
                if (n == 0) break;
                read += n;
            }
            return read;
        }
    }
}
