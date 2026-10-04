// src/Gordian.Core/Audio/SoundEffectPointer.cs
using System;
using System.Buffers.Binary;

namespace Gordian.Core.Audio
{
    /// <summary>
    /// Decodes a DAT section of type <c>0x3D</c> (SoundEffectPointer): the 8-byte magic <c>"SeSep  "</c> followed by a
    /// <c>u32</c> sound effect id, which <see cref="FfxiSoundLocator.FindEffect"/> resolves to a <c>.spw</c> file.
    /// </summary>
    /// <remarks>
    /// Layout referenced from xi-tools <c>docs/audio/refs.md</c> (https://github.com/vekien/xi-tools). The section's own
    /// four-character name is not the sound id (the Home Point activation sound sits in a section named <c>6023</c> whose
    /// id is 16023), so the id must be read from the payload.
    /// </remarks>
    public static class SoundEffectPointer
    {
        private static ReadOnlySpan<byte> Magic => "SeSep"u8;

        /// <summary>Reads the sound id from a 0x3D section payload (the bytes after the 16-byte section header).</summary>
        public static bool TryDecode(ReadOnlySpan<byte> payload, out int soundId)
        {
            soundId = 0;
            if (payload.Length < 12 || !payload.StartsWith(Magic))
            {
                return false;
            }

            uint id = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(8));
            if (id == 0 || id > int.MaxValue)
            {
                return false;
            }

            soundId = (int)id;
            return true;
        }
    }
}
