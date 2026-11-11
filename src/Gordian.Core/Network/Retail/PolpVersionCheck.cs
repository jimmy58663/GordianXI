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
    /// <param name="Status">1 when the client is registered and up to date; anything else means it needs patching.</param>
    /// <param name="StatusText">The status string (<c>"registered"</c>).</param>
    /// <param name="ServerIp">The server's address as it reports it.</param>
    /// <param name="CurrentVersion">The current retail version (<c>"30261006_0"</c>).</param>
    /// <param name="ChecksumValid">Whether the header CRC-32 matched the payload.</param>
    public readonly record struct PolpVersionResponse(uint Status, string StatusText, string ServerIp, string CurrentVersion, bool ChecksumValid)
    {
        /// <summary>Whether the server accepted the version (<see cref="Status"/> 1).</summary>
        public bool IsUpToDate => Status == 1;
    }

    /// <summary>
    /// POLP frames: the unauthenticated version checks of the PlayOnline Viewer (<c>pt008.pol.com:54000</c>) and of FFXI
    /// (<c>pc001W2U.pol.com:53001</c>). The client sends Type 7 on connect and the server answers Type 8, then closes.
    /// <para>
    /// Protocol referenced from the GordianXI retail login spec, docs/network/retail-login-spec.md v2.1.19, section 4.1.
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

        /// <summary>Writes the 16-byte header over a whole frame: size, CRC-32 of the payload, magic, type.</summary>
        public static void WriteHeader(Span<byte> frame, uint type)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(frame, (uint)frame.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(frame[4..], PolHashes.Crc32(frame[HeaderSize..]));
            Magic.CopyTo(frame[8..]);
            BinaryPrimitives.WriteUInt32LittleEndian(frame[12..], type);
        }

        /// <summary>Decodes a Type 8 version response; false when it is not one.</summary>
        public static bool TryParseVersionResponse(ReadOnlySpan<byte> frame, out PolpVersionResponse response)
        {
            response = default;
            if (frame.Length < VersionResponseSize || !frame.Slice(8, 4).SequenceEqual(Magic) ||
                BinaryPrimitives.ReadUInt32LittleEndian(frame[12..]) != TypeVersionResponse)
            {
                return false;
            }
            int size = (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(frame), (uint)frame.Length);
            bool crcOk = size >= HeaderSize && BinaryPrimitives.ReadUInt32LittleEndian(frame[4..]) == PolHashes.Crc32(frame[HeaderSize..size]);
            int versionLength = (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(frame[0x58..]), 11u);
            response = new PolpVersionResponse(
                BinaryPrimitives.ReadUInt32LittleEndian(frame[0x14..]),
                ReadAscii(frame.Slice(0x18, 11)),
                ReadAscii(frame.Slice(0x23, 16)),
                ReadAscii(frame.Slice(0x5C, versionLength)),
                crcOk);
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

            byte[] response = new byte[PolpPackets.VersionResponseSize];
            await stream.ReadExactlyAsync(response, ct).ConfigureAwait(false);
            if (!PolpPackets.TryParseVersionResponse(response, out var parsed))
                throw new RetailLoginException("version check", $"{host}:{port} did not answer with a POLP version response.");
            return parsed;
        }
    }
}
