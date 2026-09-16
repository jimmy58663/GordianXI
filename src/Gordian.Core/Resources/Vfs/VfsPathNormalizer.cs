// src/Gordian.Core/Resources/Vfs/VfsPathNormalizer.cs
using System;

namespace Gordian.Core.Resources.Vfs
{
    /// <summary>
    /// High-performance cross-platform path normalizer for VFS keys.
    /// Normalizes path separators to '/' and standardizes to uppercase invariant to guarantee
    /// consistent, case-insensitive asset resolution across Windows, Linux, and macOS.
    /// </summary>
    public static class VfsPathNormalizer
    {
        /// <summary>
        /// Normalizes a relative virtual file path into an uppercase, forward-slash indexed key.
        /// Trims leading and trailing slashes.
        /// Example: "rom\\118\\106.dat" -> "ROM/118/106.DAT"
        /// </summary>
        public static string Normalize(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            ReadOnlySpan<char> span = path.AsSpan().Trim();

            // Trim leading slashes or dots
            while (span.Length > 0 && (span[0] == '/' || span[0] == '\\' || span[0] == '.'))
            {
                span = span[1..];
            }

            // Trim trailing slashes
            while (span.Length > 0 && (span[^1] == '/' || span[^1] == '\\'))
            {
                span = span[..^1];
            }

            if (span.IsEmpty)
            {
                return string.Empty;
            }

            // Allocate buffer on stack if reasonably small, or fallback to heap array
            Span<char> buffer = span.Length <= 512 ? stackalloc char[span.Length] : new char[span.Length];

            for (int i = 0; i < span.Length; i++)
            {
                char c = span[i];
                if (c == '\\')
                {
                    buffer[i] = '/';
                }
                else
                {
                    buffer[i] = char.ToUpperInvariant(c);
                }
            }

            return new string(buffer);
        }
    }
}
