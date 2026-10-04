// src/Gordian.Core/Audio/FfxiSoundDecoder.cs
using System;

namespace Gordian.Core.Audio
{
    /// <summary>
    /// Decodes the ATRAC3 coding (format 3) of retail sound files. Not implemented in GordianXI: ATRAC3 is Sony's MDCT
    /// codec; which decoder supplies it is still being decided (FFmpeg's, dynamically linked, is the candidate). It is registered at startup through
    /// <see cref="FfxiSoundDecoder.Atrac3"/>.
    /// </summary>
    public interface IAtrac3Decoder
    {
        /// <summary>Opens the PCM of an ATRAC3 <c>.bgw</c> / <c>.spw</c>; null when it cannot.</summary>
        /// <param name="file">The whole file, header included (sample data from <see cref="FfxiSoundHeader.DataOffset"/>).</param>
        /// <param name="header">The parsed header (channels, rate, blocks, loop block, block size).</param>
        /// <param name="loop">Whether to loop back to the header's loop point after the end.</param>
        IPcmSource? Open(byte[] file, in FfxiSoundHeader header, bool loop);
    }

    /// <summary>
    /// Opens any retail sound file: ADPCM and PCM in managed code (<see cref="FfxiSoundStream"/>), ATRAC3 through the
    /// registered <see cref="Atrac3"/> decoder when there is one.
    /// </summary>
    public static class FfxiSoundDecoder
    {
        /// <summary>The ATRAC3 decoder, or null (ATRAC3 files then do not play).</summary>
        public static IAtrac3Decoder? Atrac3 { get; set; }

        /// <summary>Opens a file as a stream. Null when it is not a sound file or its coding cannot be decoded.</summary>
        /// <param name="file">The whole file.</param>
        /// <param name="loop">Null keeps the header's looping; true or false forces it.</param>
        public static IPcmSource? Open(byte[] file, bool? loop = null)
        {
            if (!FfxiSoundHeader.TryParse(file, out FfxiSoundHeader header) || header.IsEncrypted)
            {
                return null;
            }

            if (header.Format == FfxiSampleFormat.Atrac3)
            {
                return Atrac3?.Open(file, header, loop ?? header.IsLooped);
            }

            return FfxiSoundStream.Open(file, loop);
        }

        /// <summary>Decodes one pass of a file into a clip (sound effects). Null when it cannot be decoded.</summary>
        public static PcmClip? DecodeClip(byte[] file)
        {
            if (!FfxiSoundHeader.TryParse(file, out FfxiSoundHeader header))
            {
                return null;
            }

            if (header.Format != FfxiSampleFormat.Atrac3)
            {
                return FfxiSoundStream.DecodeClip(file);
            }

            IPcmSource? source = header.IsEncrypted ? null : Atrac3?.Open(file, header, loop: false);
            if (source is null)
            {
                return null;
            }

            var samples = new System.Collections.Generic.List<short>();
            var buffer = new short[4096];
            int n;
            while ((n = source.Read(buffer)) > 0 && samples.Count < 32 * 1024 * 1024)
            {
                samples.AddRange(new ReadOnlySpan<short>(buffer, 0, n));
            }

            long loopFrame = header.IsLooped && header.DeclaredBlockSamples > 0
                ? (long)header.LoopStartBlock * header.DeclaredBlockSamples
                : -1;
            return new PcmClip(samples.ToArray(), source.Channels, source.SampleRate, loopFrame, header.Id);
        }
    }
}
