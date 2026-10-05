// src/Gordian.Core/Audio/FfxiAdpcm.cs
using System;

namespace Gordian.Core.Audio
{
    /// <summary>
    /// The block ADPCM of retail <c>.bgw</c> / <c>.spw</c> files: one frame per channel per block, a header byte
    /// (high nibble = filter index, low nibble = range) followed by packed 4-bit samples, low nibble first, predicted
    /// from the last two output samples.
    /// </summary>
    /// <remarks>
    /// Codec referenced from xi-tools <c>docs/audio/format.md</c> ("ADPCM codec", https://github.com/vekien/xi-tools):
    /// the five filter coefficient pairs, the shift <c>(12 - range) &amp; 31</c>, the arithmetic <c>&gt;&gt; 8</c> on the
    /// prediction (not a truncating divide), and filter indices of 5 and above leaving the channel silent for the block
    /// with its history untouched. Implemented from that description.
    /// </remarks>
    public static class FfxiAdpcm
    {
        private static ReadOnlySpan<short> Filter0 => [0x0000, 0x00F0, 0x01CC, 0x0188, 0x01E8];
        private static ReadOnlySpan<short> Filter1 => [0x0000, 0x0000, -0x00D0, -0x00DC, -0x00F0];

        /// <summary>
        /// Decodes one channel's frame into every <paramref name="stride"/>-th sample of <paramref name="output"/>,
        /// carrying the two-sample history <paramref name="h0"/> (newest) and <paramref name="h1"/>.
        /// </summary>
        /// <param name="frame">The frame: header byte plus <c>(samples / 2)</c> data bytes.</param>
        /// <param name="output">Interleaved output; sample <c>i</c> goes to <c>output[i * stride]</c>.</param>
        /// <param name="stride">The channel count of the interleaved output.</param>
        /// <param name="h0">The previous sample.</param>
        /// <param name="h1">The sample before that.</param>
        /// <returns>The number of samples written (<c>(frame.Length - 1) * 2</c>).</returns>
        public static int DecodeFrame(ReadOnlySpan<byte> frame, Span<short> output, int stride, ref int h0, ref int h1)
        {
            if (frame.IsEmpty)
            {
                return 0;
            }

            int samples = (frame.Length - 1) * 2;
            byte header = frame[0];
            int index = header >> 4;
            if (index >= Filter0.Length)
            {
                // Out-of-range filter: the channel is silent for this block and its history is kept.
                for (int i = 0; i < samples; i++)
                {
                    output[i * stride] = 0;
                }

                return samples;
            }

            int scale = (0x0C - (header & 0x0F)) & 0x1F;
            int f0 = Filter0[index];
            int f1 = Filter1[index];
            int o = 0;
            for (int b = 1; b < frame.Length; b++)
            {
                int packed = frame[b];
                for (int n = 0; n < 2; n++)
                {
                    int v = n == 0 ? packed & 0x0F : packed >> 4;
                    if (v >= 8)
                    {
                        v -= 16;
                    }

                    int sample = (v << scale) + ((h0 * f0 + h1 * f1) >> 8);
                    if (sample > short.MaxValue)
                    {
                        sample = short.MaxValue;
                    }
                    else if (sample < short.MinValue)
                    {
                        sample = short.MinValue;
                    }

                    output[o * stride] = (short)sample;
                    o++;
                    h1 = h0;
                    h0 = sample;
                }
            }

            return samples;
        }
    }
}
